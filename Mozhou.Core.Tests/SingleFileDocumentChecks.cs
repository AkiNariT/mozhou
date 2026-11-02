using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Mozhou.Core;

internal static class SingleFileDocumentChecks
{
    private const string Project = "project.mozhou.json";
    private const string Image = "assets/images/0123456789abcdef0123456789abcdef.png";
    private static readonly byte[] Original = Encoding.UTF8.GetBytes("{\"标题\":\"海上🌊𠀀\"}\r\n");

    internal static readonly (string Name, Action<string> Test)[] Cases =
    [
        ("Single file preserves Unicode, raw history, chat and image bytes", dir =>
        {
            var path = Path.Combine(dir, "中文🌊小说.mozhou");
            var files = Fixtures(); SingleFileDocument.CreateNew(path, files);
            var document = new SingleFileDocument(path);
            foreach (var (name, bytes) in files) Same(bytes, document.ReadFile(name), name);
            True(document.ReadFile("backups/project-missing.json") is null, "missing file is not null");
            True(document.ListFiles("assets/images/").SequenceEqual([Image]), "prefix listing changed");
            True(Directory.GetFiles(dir).Length == 1, "successful create left sidecars");
        }),
        ("Single file rejects stale project atomically across all changed files", dir =>
        {
            var document = Create(dir); var changed = Encoding.UTF8.GetBytes("new title");
            document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = changed }, Original, true);
            Throws<ConflictException>(() => document.WriteFiles(new Dictionary<string, byte[]?>
            { [Project] = Original, [Image] = null, ["assistant-chat.json"] = [99] }, Original, true));
            Same(changed, document.ReadFile(Project), "project overwritten after conflict");
            Same(Fixtures()[Image], document.ReadFile(Image), "image deleted after conflict");
            Same(Fixtures()["assistant-chat.json"], document.ReadFile("assistant-chat.json"), "chat changed after conflict");
        }),
        ("Single file checks absent and stale chat baselines inside transaction", dir =>
        {
            var document = Create(dir);
            Throws<ConflictException>(() => document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [1] }, Original, true, null, true));
            Same(Original, document.ReadFile(Project), "chat conflict wrote manuscript");
            document.WriteFiles(new Dictionary<string, byte[]?> { ["assistant-chat.json"] = null });
            document.WriteFiles(new Dictionary<string, byte[]?> { ["assistant-chat.json"] = [2] }, Original, true, null, true);
            Same([2], document.ReadFile("assistant-chat.json"), "absent baseline prevented insert");
            Throws<ConflictException>(() => document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [3] }, null, true));
            Same(Original, document.ReadFile(Project), "absent project baseline overwrote project");
        }),
        ("Single file rejects unsafe logical names and duplicate creation without partial output", dir =>
        {
            var document = Create(dir);
            foreach (var name in new[] { "../project.mozhou.json", "/project.mozhou.json", "assets\\images\\a.png", "backups/project-../x.json", "backups/project-.json", "other.json", "assets/images/a.png", "assistant-chat.json\0suffix" })
            {
                Throws<InvalidDataException>(() => document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [1], [name] = [2] }));
                Same(Original, document.ReadFile(Project), "unsafe path partially wrote project");
            }
            var duplicate = Path.Combine(dir, "duplicate.mozhou");
            Throws<InvalidDataException>(() => SingleFileDocument.CreateNew(duplicate, new[] { KeyValuePair.Create(Project, Original), KeyValuePair.Create(Project, Original) }));
            True(!File.Exists(duplicate), "duplicate creation published partial database");
        }),
        ("Single file refuses corrupt, foreign, future and altered schemas without modifying source", dir =>
        {
            var corrupt = Path.Combine(dir, "broken.mozhou"); File.WriteAllBytes(corrupt, [1, 2, 3]);
            Throws<InvalidDataException>(() => new SingleFileDocument(corrupt)); Same([1, 2, 3], File.ReadAllBytes(corrupt), "corrupt file modified");
            var foreign = Path.Combine(dir, "foreign.mozhou"); using (var raw = new RawDatabase(foreign)) raw.Execute("CREATE TABLE unrelated (value TEXT)");
            var bytes = File.ReadAllBytes(foreign); Throws<InvalidDataException>(() => new SingleFileDocument(foreign)); Same(bytes, File.ReadAllBytes(foreign), "foreign DB modified");
            foreach (var sql in new[] { "PRAGMA user_version=999", "CREATE TABLE unexpected (value BLOB)", "CREATE TRIGGER unexpected AFTER INSERT ON files BEGIN DELETE FROM files; END", "ALTER TABLE files ADD COLUMN extra TEXT" })
            {
                var path = Path.Combine(dir, Guid.NewGuid() + ".mozhou"); SingleFileDocument.CreateNew(path, Fixtures());
                using (var raw = new RawDatabase(path)) raw.Execute(sql);
                bytes = File.ReadAllBytes(path); Throws<InvalidDataException>(() => new SingleFileDocument(path)); Same(bytes, File.ReadAllBytes(path), "rejection modified DB");
            }
        }),
        ("Single file backup moves as one complete file and remains independent", dir =>
        {
            var document = Create(dir); var copy = Path.Combine(dir, "copy.mozhou"); document.CopyTo(copy);
            var elsewhere = Path.Combine(dir, "elsewhere"); Directory.CreateDirectory(elsewhere);
            var moved = Path.Combine(elsewhere, "已搬走.mozhou"); File.Move(copy, moved);
            var independent = new SingleFileDocument(moved);
            foreach (var (name, bytes) in Fixtures()) Same(bytes, independent.ReadFile(name), name);
            independent.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [7] }, Original, true);
            Same(Original, document.ReadFile(Project), "copy modified source");
            True(Directory.GetFiles(elsewhere).Length == 1, "copy requires sidecars");
        }),
        ("Single file ZIP contains exact rows and export never overwrites a destination", dir =>
        {
            var document = Create(dir); var before = File.ReadAllBytes(document.Path);
            var zip = Path.Combine(dir, "导出.zip"); document.ExportZip(zip);
            using (var archive = ZipFile.OpenRead(zip))
            {
                True(archive.Entries.Count == Fixtures().Count, "ZIP entries differ");
                foreach (var (name, bytes) in Fixtures())
                {
                    using var input = archive.GetEntry(name)!.Open(); using var output = new MemoryStream(); input.CopyTo(output);
                    Same(bytes, output.ToArray(), name);
                }
            }
            foreach (var destination in new[] { document.Path, zip })
            {
                var existing = File.ReadAllBytes(destination);
                Throws<IOException>(() => document.ExportZip(destination)); Throws<IOException>(() => document.CopyTo(destination));
                Throws<IOException>(() => SingleFileDocument.CreateNew(destination, Fixtures()));
                Same(existing, File.ReadAllBytes(destination), "existing destination overwritten");
            }
            Same(before, File.ReadAllBytes(document.Path), "export modified source");
            True(!Directory.GetFiles(dir, "*.tmp*").Any(), "failed export leaked temporary file");
        }),
        ("Single file waits for competing writer then checks its committed baseline", dir =>
        {
            var document = Create(dir);
            using var raw = new RawDatabase(document.Path); raw.Execute("BEGIN IMMEDIATE");
            var save = Task.Run(() => document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [8] }, Original, true));
            Thread.Sleep(150); True(!save.IsCompleted, "save bypassed held writer lock");
            raw.Execute("UPDATE files SET content=x'09' WHERE name='project.mozhou.json'"); raw.Execute("COMMIT");
            Throws<ConflictException>(() => save.GetAwaiter().GetResult());
            Same([9], document.ReadFile(Project), "waiting save ignored newly committed content");
        }),
        ("Single file read and snapshot ignore an uncommitted writer", dir =>
        {
            var document = Create(dir);
            using var raw = new RawDatabase(document.Path); raw.Execute("BEGIN IMMEDIATE");
            raw.Execute("UPDATE files SET content=x'09' WHERE name='project.mozhou.json'");
            Same(Original, document.ReadFile(Project), "read exposed uncommitted bytes");
            var copy = Path.Combine(dir, "snapshot.mozhou"); document.CopyTo(copy);
            Same(Original, new SingleFileDocument(copy).ReadFile(Project), "backup exposed uncommitted bytes");
            raw.Execute("ROLLBACK");
        }),
        ("Single file rejects oversized image rows without imposing a database size cap", dir =>
        {
            var document = Create(dir); var oversized = new byte[25 * 1024 * 1024 + 1];
            Throws<InvalidDataException>(() => document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [1], [Image] = oversized }));
            Same(Original, document.ReadFile(Project), "oversized image partially wrote project");
            var second = "assets/images/fedcba9876543210fedcba9876543210.jpg";
            document.WriteFiles(new Dictionary<string, byte[]?> { [Image] = new byte[14 * 1024 * 1024], [second] = new byte[14 * 1024 * 1024] });
            True(document.ReadFile(Image)!.Length == 14 * 1024 * 1024 && document.ReadFile(second)!.Length == 14 * 1024 * 1024, "whole database incorrectly capped at 25 MiB");
            using (var raw = new RawDatabase(document.Path)) raw.Execute("UPDATE files SET content=zeroblob(26214401) WHERE name='" + Image + "'");
            Throws<InvalidDataException>(() => document.ReadFile(Image));
            var rejected = Path.Combine(dir, "oversized-copy.mozhou");
            Throws<InvalidDataException>(() => document.CopyTo(rejected)); True(!File.Exists(rejected), "copy published oversized image");
        }),
        ("Single file leaves no journal after commit or conflict and releases handles", dir =>
        {
            var document = Create(dir); document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [] }, Original, true);
            Same([], document.ReadFile(Project), "zero length BLOB turned into null");
            Throws<ConflictException>(() => document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [1] }, Original, true));
            True(Directory.GetFiles(dir).Length == 1, "journal or WAL remains after completed operation");
            using var exclusive = new FileStream(document.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }),
        ("Single file refuses case-colliding backup names without changing the manuscript", dir =>
        {
            var document = Create(dir);
            document.WriteFiles(new Dictionary<string, byte[]?> { ["backups/project-Abc.json"] = [1] });
            Throws<InvalidDataException>(() => document.WriteFiles(new Dictionary<string, byte[]?>
            { [Project] = [9], ["backups/project-abc.json"] = [2] }));
            Same(Original, document.ReadFile(Project), "name collision partially changed manuscript");
            True(document.ReadFile("backups/project-abc.json") is null, "name collision was inserted");
            document.WriteFiles(new Dictionary<string, byte[]?> { ["backups/project-Abc.json"] = null, ["backups/project-abc.json"] = [3] });
            Same([3], document.ReadFile("backups/project-abc.json"), "explicit case rename rejected");
        }),
        ("Single file commit lock failure rolls back project and related rows", dir =>
        {
            var document = Create(dir);
            using (var raw = new RawDatabase(document.Path))
            {
                raw.Execute("BEGIN"); raw.Execute("SELECT content FROM files");
                Throws<IOException>(() => document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [8], [Image] = null }, Original, true));
                raw.Execute("ROLLBACK");
            }
            Same(Original, document.ReadFile(Project), "commit failure changed manuscript");
            Same(Fixtures()[Image], document.ReadFile(Image), "commit failure deleted image");
            True(Directory.GetFiles(dir).Length == 1, "commit failure left SQLite sidecars");
        }),
        ("Single file incomplete creation never publishes a destination or leaves temporary files", dir =>
        {
            static IEnumerable<KeyValuePair<string, byte[]>> BrokenInput()
            {
                yield return KeyValuePair.Create(Project, Original);
                throw new IOException("source became unreadable");
            }
            var path = Path.Combine(dir, "failed.mozhou");
            Throws<IOException>(() => SingleFileDocument.CreateNew(path, BrokenInput()));
            True(Directory.GetFiles(dir).Length == 0, "failed creation published or leaked files");
        }),
        ("Single file recognizes existing containers by content and new outputs require mozhou extension", dir =>
        {
            var document = Create(dir); var renamed = Path.Combine(dir, "renamed.backup"); File.Move(document.Path, renamed);
            var reopened = new SingleFileDocument(renamed); Same(Original, reopened.ReadFile(Project), "renamed valid container rejected");
            reopened.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [4] }, Original, true);
            Same([4], reopened.ReadFile(Project), "renamed container cannot save");
            var invalid = Path.Combine(dir, "new.backup");
            Throws<InvalidDataException>(() => SingleFileDocument.CreateNew(invalid, Fixtures()));
            Throws<InvalidDataException>(() => reopened.CopyTo(invalid));
            True(!File.Exists(invalid), "unsupported new extension published");
        }),
        ("Single file preserves legacy image filename casing and rejects ambiguous image names", dir =>
        {
            var path = Path.Combine(dir, "legacy.mozhou"); var upper = "assets/images/ABCDEF0123456789ABCDEF0123456789.PNG";
            var lower = "assets/images/abcdef0123456789abcdef0123456789.png";
            var files = Fixtures(); files.Add(upper, [3, 2, 1]); SingleFileDocument.CreateNew(path, files);
            var document = new SingleFileDocument(path); Same([3, 2, 1], document.ReadFile(upper), "legacy image casing lost");
            Throws<InvalidDataException>(() => document.WriteFiles(new Dictionary<string, byte[]?> { [Project] = [9], [lower] = [4] }));
            Same(Original, document.ReadFile(Project), "ambiguous image name partially changed manuscript");
            True(document.ReadFile(lower) is null, "ambiguous image was inserted");
        }),
        ("Single file recovers the last committed version from an interrupted rollback-journal write", dir =>
        {
            var document = Create(dir); var crashed = Path.Combine(dir, "interrupted.mozhou");
            using (var raw = new RawDatabase(document.Path))
            {
                raw.Execute("PRAGMA cache_size=1"); raw.Execute("BEGIN IMMEDIATE");
                raw.Execute("UPDATE files SET content=zeroblob(131072) WHERE name='project.mozhou.json'");
                // Test-only capture while the writer is quiescent: copying both DB and journal reproduces
                // a process interruption after page spill without terminating the test runner.
                File.Copy(document.Path, crashed); File.Copy(document.Path + "-journal", crashed + "-journal");
                raw.Execute("ROLLBACK");
            }
            var recovered = new SingleFileDocument(crashed);
            Same(Original, recovered.ReadFile(Project), "hot journal did not restore committed manuscript");
            True(!File.Exists(crashed + "-journal"), "successful recovery retained hot journal");
        }),
        ("Single file copy refuses invalid logical rows and cleans its private snapshot", dir =>
        {
            foreach (var sql in new[]
            {
                "INSERT INTO files(name,content) VALUES('../outside',x'01')",
                "UPDATE files SET content='text instead of blob' WHERE name='project.mozhou.json'",
                "INSERT INTO files(name,content) VALUES('backups/project-ABC.json',x'01'),('backups/project-abc.json',x'02')"
            })
            {
                var source = Path.Combine(dir, Guid.NewGuid() + ".mozhou"); SingleFileDocument.CreateNew(source, Fixtures());
                using (var raw = new RawDatabase(source)) raw.Execute(sql);
                var target = Path.Combine(dir, Guid.NewGuid() + ".mozhou");
                Throws<InvalidDataException>(() => new SingleFileDocument(source).CopyTo(target));
                True(!File.Exists(target) && !Directory.GetFiles(dir, "*.tmp*").Any(), "invalid copy was published or leaked temporary files");
            }
        })
    ];

    private static Dictionary<string, byte[]> Fixtures() => new()
    {
        [Project] = Original, ["project.mozhou.json.bak"] = [0xFF, 0, 0x81],
        ["backups/project-中文_旧稿.json"] = [], ["assistant-chat.json"] = Encoding.UTF8.GetBytes("[\"本地对话🌊\"]"),
        [Image] = [0, 1, 2, 255, 0, 7]
    };
    private static SingleFileDocument Create(string dir)
    {
        var path = Path.Combine(dir, "test.mozhou"); SingleFileDocument.CreateNew(path, Fixtures()); return new(path);
    }
    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Same(byte[] expected, byte[]? actual, string message) => True(actual is not null && expected.AsSpan().SequenceEqual(actual), message);
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
    }

    // Raw test connections intentionally bypass production validation to create hostile fixtures and held locks.
    private sealed class RawDatabase : IDisposable
    {
        private IntPtr handle;
        internal RawDatabase(string path)
        {
            var code = sqlite3_open_v2(Encoding.UTF8.GetBytes(path + '\0'), out handle, 6, IntPtr.Zero);
            if (code != 0) { Dispose(); throw new IOException("Fixture open: " + code); }
        }
        internal void Execute(string sql)
        {
            var code = sqlite3_exec(handle, Encoding.UTF8.GetBytes(sql + '\0'), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (code != 0) throw new IOException("Fixture SQL: " + code);
        }
        public void Dispose() { if (handle != IntPtr.Zero) { sqlite3_close_v2(handle); handle = IntPtr.Zero; } }
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr context, IntPtr error);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr db);
    }
}
