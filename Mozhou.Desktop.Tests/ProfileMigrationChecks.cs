using System.IO;
using System.Text.Json;
using Mozhou.Core;
using Mozhou.Desktop;

internal static class ProfileMigrationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "profiles-" + Guid.NewGuid().ToString("N"));
        var app = Directory.CreateDirectory(Path.Combine(fixture, "应用")).FullName;
        var legacy = Directory.CreateDirectory(Path.Combine(fixture, "旧便携版")).FullName;
        var local = Path.Combine(fixture, "user-local");
        var profile = Path.Combine(local, "Mozhou", "UserData");
        var oldSettings = Path.Combine(legacy, "mozhou-settings.json");
        var newSettings = Path.Combine(profile, "mozhou-settings.json");
        var oldShelf = Path.Combine(legacy, "bookshelf.json");
        var newShelf = Path.Combine(profile, "bookshelf.json");
        string? recorded = null;
        string Start() => SettingsStore.PrepareProfile(app, local, () => legacy, path => recorded = path);
        try
        {
            check(Start() == app && recorded == app && !Directory.Exists(local), "portable startup keeps adjacent data and records its location");
            File.WriteAllText(Path.Combine(app, "mozhou-installed.marker"), "");
            recorded = null;
            var settings = """{"CodexPath":"D:\\助手\\codex.exe","Model":"preserved-model","UnknownKey":true}""";
            var shelf = """[{"Title":"离线也保留","Folder":"E:\\旧稿.mozhou"}]""";
            File.WriteAllText(oldSettings, settings, new System.Text.UTF8Encoding(true));
            File.WriteAllText(oldShelf, shelf);
            using (var locked = new FileStream(oldShelf, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool refused = false;
                try { Start(); } catch (IOException) { refused = true; }
                check(refused && !File.Exists(newShelf), "locked legacy index stops first-run before an empty shelf can be created");
            }
            var roots = Enumerable.Range(0, 4).AsParallel().Select(_ => Start()).ToArray();
            check(roots.All(path => path == profile) && recorded == null, "concurrent installed launches use stable per-user profile");
            check(File.ReadAllBytes(newSettings).SequenceEqual(File.ReadAllBytes(oldSettings)) && File.ReadAllText(newShelf) == shelf, "retry preserves complete settings and offline shelf metadata");
            check(File.ReadAllText(oldSettings) == settings && File.ReadAllText(oldShelf) == shelf, "migration preserves both portable originals");
            check(!Directory.EnumerateFiles(profile, "*.tmp").Any(), "migration removes staging files after failure and retry");
            File.WriteAllText(newSettings, """{"Model":"installed override"}""");
            File.WriteAllText(newShelf, "[]");
            File.Delete(oldShelf);
            Start();
            check(File.ReadAllText(newShelf) == "[]" && File.ReadAllText(newSettings).Contains("installed override"), "update preserves installed settings and an intentionally empty shelf");

            var libraryOnly = Directory.CreateDirectory(Path.Combine(fixture, "library-only")).FullName;
            var book = Path.Combine(libraryOnly, "Library", "雾港");
            using var original = new ProjectStore(book);
            original.Save(new NovelProject { Title = "雾港" });
            var manuscript = File.ReadAllBytes(original.FilePath);
            using var standalone = new ProjectStore(Path.Combine(libraryOnly, "Library", "单文档.mozhou"));
            standalone.Save(new NovelProject { Title = "单文档" });
            var outside = Path.Combine(libraryOnly, "other");
            using var unrelated = new ProjectStore(outside);
            unrelated.Save(new NovelProject { Title = "隐私" });
            var lastProject = Path.Combine(fixture, "last");
            using var recent = new ProjectStore(lastProject);
            recent.Save(new NovelProject { Title = "最近" });
            var partialLocal = Path.Combine(fixture, "partial");
            var partialRoot = Path.Combine(partialLocal, "Mozhou", "UserData");
            Directory.CreateDirectory(partialRoot);
            var currentSettings = JsonSerializer.Serialize(new UserSettings { LastProjectPath = recent.FilePath, Model = "keep" });
            File.WriteAllText(Path.Combine(partialRoot, "mozhou-settings.json"), currentSettings);
            File.WriteAllText(Path.Combine(libraryOnly, "mozhou-settings.json"), """{"Model":"old"}""");
            SettingsStore.PrepareProfile(app, partialLocal, () => libraryOnly, _ => { });
            var books = new BookshelfStore(partialRoot).Read();
            check(books.Count == 3 && books.Any(b => b.Folder == book) && books.Any(b => b.Folder == standalone.FilePath) && books.Any(b => b.Folder == lastProject), "missing index seeds only the known Library and previously opened book");
            check(manuscript.SequenceEqual(File.ReadAllBytes(original.FilePath)) && !Directory.Exists(Path.Combine(partialRoot, "Library")), "legacy manuscripts stay in their original folders");
            check(File.ReadAllText(Path.Combine(partialRoot, "mozhou-settings.json")) == currentSettings, "partial installed profile keeps its newer settings while importing missing shelf");

            var shelfOnly = Directory.CreateDirectory(Path.Combine(fixture, "shelf-only")).FullName;
            File.WriteAllText(Path.Combine(shelfOnly, "bookshelf.json"), "[]");
            var thirdLocal = Path.Combine(fixture, "shelf-only-user");
            var migrated = SettingsStore.PrepareProfile(app, thirdLocal, () => shelfOnly, _ => { });
            Directory.Move(shelfOnly, shelfOnly + "-unplugged");
            check(SettingsStore.PrepareProfile(app, thirdLocal, () => shelfOnly, _ => { }) == migrated, "successful shelf-only import can restart after portable drive disconnects");

            var retrySource = Path.Combine(fixture, "was-offline");
            var retried = SettingsStore.PrepareProfile(app, Path.Combine(fixture, "retry-user"), () => retrySource, _ => { }, failure =>
            {
                if (failure is not DirectoryNotFoundException) throw failure;
                Directory.CreateDirectory(retrySource);
                File.WriteAllText(Path.Combine(retrySource, "bookshelf.json"), shelf);
                return true;
            });
            check(File.ReadAllText(Path.Combine(retried, "bookshelf.json")) == shelf, "retry imports the actual old index after the drive becomes available");
            var skipLocal = Path.Combine(fixture, "skip-user");
            var skipRoot = Directory.CreateDirectory(Path.Combine(skipLocal, "Mozhou", "UserData")).FullName;
            File.WriteAllText(Path.Combine(skipRoot, "mozhou-settings.json"), currentSettings);
            var skipped = SettingsStore.PrepareProfile(app, skipLocal, () => throw new UnauthorizedAccessException(), _ => { }, _ => false);
            check(skipped == skipRoot && File.ReadAllText(Path.Combine(skipped, "mozhou-settings.json")) == currentSettings, "explicit skip after registry denial opens the existing partial installed profile");
            check(SettingsStore.PrepareProfile(app, skipLocal, () => throw new Exception("unexpected repeated lookup"), _ => { }) == skipRoot, "future launches remember the user's explicit skip");
            var unusable = Path.Combine(fixture, "file-instead-of-directory");
            File.WriteAllText(unusable, "occupied");
            try
            {
                SettingsStore.PrepareProfile(app, unusable, () => legacy, _ => { }, _ => throw new Exception("migration prompt on profile creation failure"));
                throw new Exception("unusable installed directory was accepted");
            }
            catch (IOException) { check(true, "unusable installed destination remains a startup error even when skipping migration"); }
        }
        finally { Directory.Delete(fixture, true); }
    }
}
