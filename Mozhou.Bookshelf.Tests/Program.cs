using System.Text.Json;
using Mozhou.Core;

var tests = new (string Name, Action<string> Run)[]
{
    ("Different books and copies with the same project ID survive restart", dir =>
    {
        var a = SaveBook(dir, "a", "海上的约定");
        var b = SaveBook(dir, "b", "第二本书");
        var copy = Path.Combine(dir, "copy"); Directory.CreateDirectory(copy);
        File.Copy(new ProjectStore(a).FilePath, new ProjectStore(copy).FilePath);
        var shelf = Shelf(dir);
        foreach (var folder in new[] { a, b, copy }) shelf.Remember(folder, new ProjectStore(folder).Load());
        var reopened = Shelf(dir).Read(); Equal(3, reopened.Count);
        Equal(2, reopened.Count(book => book.Title == "海上的约定"));
        True(reopened.All(book => book.IsAvailable && book.Problem == null && book.ChapterCount == 1 && book.WordCount == 5), "snapshot statistics or availability lost");
        True(reopened.All(book => Path.IsPathFullyQualified(book.Folder)), "relative path persisted");
    }),
    ("Remember normalizes paths and replaces one record while preserving other books", dir =>
    {
        var folder = SaveBook(dir, "book", "旧名字"); var other = SaveBook(dir, "other", "另外一本");
        var shelf = Shelf(dir); shelf.Remember(folder, new ProjectStore(folder).Load()); shelf.Remember(other, new ProjectStore(other).Load());
        var project = new ProjectStore(folder).Load(); project.Title = "更新的名字"; var before = DateTimeOffset.UtcNow;
        shelf.Remember(Path.Combine(folder, "..", "book") + Path.DirectorySeparatorChar, project);
        if (OperatingSystem.IsWindows()) shelf.Remember(folder.ToUpperInvariant(), project);
        var books = shelf.Read(); Equal(2, books.Count); Equal("更新的名字", books[0].Title);
        True(books[0].LastOpenedAt >= before, "opening time was not updated"); Equal("另外一本", books[1].Title);
        Equal("旧名字", new ProjectStore(folder).Load().Title);
    }),
    ("Read sorts by last opened then manuscript update time", dir =>
    {
        var shelf = Shelf(dir); Directory.CreateDirectory(Path.GetDirectoryName(shelf.FilePath)!);
        var old = DateTimeOffset.Parse("2024-01-01T00:00:00Z"); var recent = old.AddDays(1);
        File.WriteAllText(shelf.FilePath, JsonSerializer.Serialize(new[]
        {
            new ShelfBook { Folder = Path.Combine(dir, "a"), Title = "A", LastOpenedAt = old, UpdatedAt = recent },
            new ShelfBook { Folder = Path.Combine(dir, "b"), Title = "B", LastOpenedAt = recent, UpdatedAt = old },
            new ShelfBook { Folder = Path.Combine(dir, "c"), Title = "C", LastOpenedAt = recent, UpdatedAt = recent }
        }));
        Equal("C,B,A", string.Join(',', shelf.Read().Select(book => book.Title)));
    }),
    ("Refresh updates cached chapter statistics without writing manuscripts or backups", dir =>
    {
        var folder = SaveBook(dir, "book", "旧书名"); var shelf = Shelf(dir);
        shelf.Remember(folder, new ProjectStore(folder).Load()); var opened = shelf.Read().Single().LastOpenedAt;
        var projectStore = new ProjectStore(folder); var project = projectStore.Load(); project.Title = "新书名"; project.Description = "新简介";
        project.Entries = [new NovelEntry { Kind = "chapter", Content = "中文 hello world 𠀀" }, new NovelEntry { Kind = "chapter", Content = "你好abc" }, new NovelEntry { Kind = "note", Content = "这段不属于正文" }];
        projectStore.Save(project); var files = SnapshotFiles(folder); var refreshed = shelf.Refresh().Single();
        Equal("新书名", refreshed.Title); Equal("新简介", refreshed.Description); Equal(2, refreshed.ChapterCount); Equal(8, refreshed.WordCount);
        Equal(project.UpdatedAt, refreshed.UpdatedAt); Equal(opened, refreshed.LastOpenedAt);
        SameFiles(files, folder); Equal(8, Shelf(dir).Read().Single().WordCount);
    }),
    ("Missing and corrupt manuscripts retain metadata and recover on refresh", dir =>
    {
        var missing = SaveBook(dir, "missing", "暂时离线"); var corrupt = SaveBook(dir, "corrupt", "损坏但仍在书架");
        var shelf = Shelf(dir); shelf.Remember(missing, new ProjectStore(missing).Load()); shelf.Remember(corrupt, new ProjectStore(corrupt).Load());
        var missingPath = new ProjectStore(missing).FilePath; var corruptPath = new ProjectStore(corrupt).FilePath;
        var original = File.ReadAllBytes(corruptPath); File.WriteAllBytes(corruptPath + ".bak", original);
        File.Delete(missingPath); File.WriteAllText(corruptPath, "{broken manuscript");
        var files = SnapshotFiles(corrupt); var refreshed = shelf.Refresh(); Equal(2, refreshed.Count);
        True(refreshed.All(book => !book.IsAvailable && !string.IsNullOrWhiteSpace(book.Problem)), "unavailable books were not marked");
        True(refreshed.All(book => book.WordCount == 5 && book.ChapterCount == 1), "cached statistics lost");
        True(refreshed.Any(book => book.Title == "暂时离线") && refreshed.Any(book => book.Title == "损坏但仍在书架"), "cached titles lost");
        SameFiles(files, corrupt); True(!File.Exists(missingPath), "refresh recreated a missing manuscript");
        File.WriteAllBytes(corruptPath, original); var recovered = shelf.Refresh().Single(book => book.Folder == corrupt);
        True(recovered.IsAvailable && recovered.Problem == null, "repaired manuscript remains unavailable"); Equal(2, Shelf(dir).Read().Count);
    }),
    ("Valid JSON with invalid manuscript structure remains selectable during refresh and seed", dir =>
    {
        var folder = SaveBook(dir, "invalid-structure", "应保留缓存"); var shelf = Shelf(dir);
        shelf.Remember(folder, new ProjectStore(folder).Load());
        var manuscriptPath = new ProjectStore(folder).FilePath;
        foreach (var invalid in new[] { "{}", "null", "{\"FormatVersion\":2,\"Id\":\"future\",\"Title\":\"未来格式\",\"Entries\":[],\"Relations\":[]}" })
        {
            File.WriteAllText(manuscriptPath, invalid);
            var refreshed = shelf.Refresh().Single();
            True(!refreshed.IsAvailable && !string.IsNullOrWhiteSpace(refreshed.Problem), "invalid document was treated as available");
            Equal("应保留缓存", refreshed.Title); Equal(5, refreshed.WordCount); Equal(invalid, File.ReadAllText(manuscriptPath));
            var seed = new BookshelfStore(Path.Combine(dir, "seed-" + Guid.NewGuid().ToString("N")));
            seed.Seed([folder]); True(seed.Exists, "bad manuscript prevented seed initialization");
            var seeded = seed.Read().Single(); Equal(folder, seeded.Folder); Equal("invalid-structure", seeded.Title);
            True(!seeded.IsAvailable && !string.IsNullOrWhiteSpace(seeded.Problem), "invalid manuscript was not retained as unavailable");
        }
    }),
    ("Forget removes only the selected registry record", dir =>
    {
        var folder = SaveBook(dir, "book", "保留在磁盘"); var other = SaveBook(dir, "other", "继续展示");
        File.WriteAllText(Path.Combine(folder, "assistant-chat.json"), "聊天记录");
        var shelf = Shelf(dir); shelf.Remember(folder, new ProjectStore(folder).Load()); shelf.Remember(other, new ProjectStore(other).Load());
        var files = SnapshotFiles(folder); shelf.Forget(folder + Path.DirectorySeparatorChar);
        Equal("继续展示", Shelf(dir).Read().Single().Title); SameFiles(files, folder);
    }),
    ("Malformed registries reject reads and mutations while preserving their bytes", dir =>
    {
        var folder = SaveBook(dir, "book", "不能覆盖书架"); var project = new ProjectStore(folder).Load(); var shelf = Shelf(dir);
        Directory.CreateDirectory(Path.GetDirectoryName(shelf.FilePath)!);
        foreach (var invalid in new[] { "{broken", "{}", "null", "\"wrong root\"", "" })
        {
            File.WriteAllText(shelf.FilePath, invalid); var original = File.ReadAllBytes(shelf.FilePath);
            foreach (var action in new Action[] { () => shelf.Read(), () => shelf.Refresh(), () => shelf.Remember(folder, project), () => shelf.Forget(folder) })
            {
                Throws<InvalidDataException>(action); True(original.SequenceEqual(File.ReadAllBytes(shelf.FilePath)), "malformed registry was changed");
            }
            shelf.Seed([folder]); True(original.SequenceEqual(File.ReadAllBytes(shelf.FilePath)), "seed overwrote an existing registry");
        }
        Equal(0, Directory.GetFiles(Path.GetDirectoryName(shelf.FilePath)!, "*.tmp").Length);
    }),
    ("Null and invalid rows do not hide valid books or crash later updates", dir =>
    {
        var folder = SaveBook(dir, "valid", "可用小说"); var shelf = Shelf(dir); Directory.CreateDirectory(Path.GetDirectoryName(shelf.FilePath)!);
        var valid = JsonSerializer.Serialize(new ShelfBook { Folder = folder, Title = null!, Description = null!, WordCount = -1, ChapterCount = -2 });
        File.WriteAllText(shelf.FilePath, "[null,{},42,{\"Folder\":\"bad\\u0000path\"},{\"Folder\":\"x\",\"UpdatedAt\":\"not a date\"}," + valid + "]");
        var read = shelf.Read().Single(); Equal(folder, read.Folder); Equal("", read.Title); Equal("", read.Description);
        Equal(0, read.WordCount); Equal(0, read.ChapterCount);
        shelf.Remember(folder, new ProjectStore(folder).Load()); Equal("可用小说", shelf.Read().Single().Title);
    }),
    ("Duplicate disk rows retain the most recently opened snapshot", dir =>
    {
        var folder = SaveBook(dir, "book", "正文"); var shelf = Shelf(dir); Directory.CreateDirectory(Path.GetDirectoryName(shelf.FilePath)!);
        var recent = DateTimeOffset.Parse("2024-01-02T00:00:00Z");
        File.WriteAllText(shelf.FilePath, JsonSerializer.Serialize(new[]
        {
            new ShelfBook { Folder = folder, Title = "新的缓存", LastOpenedAt = recent },
            new ShelfBook { Folder = folder + Path.DirectorySeparatorChar, Title = "旧的缓存", LastOpenedAt = recent.AddDays(-1) }
        }));
        Equal("新的缓存", shelf.Read().Single().Title);
    }),
    ("Previously read store instances merge additions from disk", dir =>
    {
        var a = SaveBook(dir, "a", "甲"); var b = SaveBook(dir, "b", "乙"); var first = Shelf(dir); var second = Shelf(dir);
        Equal(0, first.Read().Count); Equal(0, second.Read().Count);
        first.Remember(a, new ProjectStore(a).Load()); second.Remember(b, new ProjectStore(b).Load()); Equal(2, Shelf(dir).Read().Count);
        first.Forget(a); Equal("乙", second.Read().Single().Title);
    }),
    ("Concurrent independent stores do not drop additions", dir =>
    {
        var books = Enumerable.Range(0, 12).Select(i => SaveBook(dir, "book-" + i, "小说 " + i)).ToArray();
        var stores = books.Select(_ => Shelf(dir)).ToArray(); foreach (var store in stores) store.Read();
        using var start = new ManualResetEventSlim(false);
        var tasks = books.Select((folder, i) => Task.Run(() => { start.Wait(); stores[i].Remember(folder, new ProjectStore(folder).Load()); })).ToArray();
        start.Set(); Task.WaitAll(tasks); Equal(12, Shelf(dir).Read().Count);
        Equal(0, Directory.GetFiles(Path.GetDirectoryName(stores[0].FilePath)!, "*.tmp").Length);
    }),
    ("Seed imports only supplied known folders, retains unavailable books and is idempotent", dir =>
    {
        var good = SaveBook(dir, "good", "初始小说"); var hidden = SaveBook(dir, "hidden", "不能扫描导入");
        var corrupt = Path.Combine(dir, "corrupt"); Directory.CreateDirectory(corrupt); File.WriteAllText(new ProjectStore(corrupt).FilePath, "bad");
        var missing = Path.Combine(dir, "missing");
        var shelf = Shelf(dir); shelf.Seed([good, good + Path.DirectorySeparatorChar, corrupt, missing, "bad\0path", "", null!]);
        True(shelf.Exists, "seed did not create a registry"); var seeded = shelf.Read(); Equal(3, seeded.Count);
        Equal("初始小说", seeded.Single(book => book.IsAvailable).Title);
        True(seeded.Where(book => !book.IsAvailable).All(book => !string.IsNullOrWhiteSpace(book.Problem)), "unavailable known novels lost their error");
        Equal("missing", seeded.Single(book => book.Folder == missing).Title);
        True(!Directory.Exists(missing), "seed created an unavailable manuscript directory");
        var bytes = File.ReadAllBytes(shelf.FilePath); shelf.Seed([hidden]); shelf.Seed(ThrowOnEnumeration());
        True(bytes.SequenceEqual(File.ReadAllBytes(shelf.FilePath)), "seed changed an existing registry");
    }),
    ("Seed retains backup-only novels without restoring or rewriting the backup", dir =>
    {
        var folder = SaveBook(dir, "backup-only", "备份里的书名"); var path = new ProjectStore(folder).FilePath;
        File.Move(path, path + ".bak"); var files = SnapshotFiles(folder); var shelf = Shelf(dir);
        shelf.Seed([folder]); var book = shelf.Read().Single(); Equal(folder, book.Folder); Equal("backup-only", book.Title);
        True(!book.IsAvailable && !string.IsNullOrWhiteSpace(book.Problem), "backup-only book was not retained for recovery");
        SameFiles(files, folder); True(!File.Exists(path), "seed restored a manuscript without user action");
    }),
    ("Empty seed persists initialization and stays empty on subsequent startup", dir =>
    {
        var shelf = Shelf(dir); True(!shelf.Exists, "new shelf unexpectedly exists"); Equal(0, shelf.Read().Count);
        True(!shelf.Exists, "reading initialized the registry"); Equal(0, shelf.Refresh().Count); True(!shelf.Exists, "refresh initialized the registry");
        shelf.Seed([]); True(shelf.Exists, "empty seed was not persisted");
        shelf.Seed([SaveBook(dir, "later", "不应自动导入")]); Equal(0, Shelf(dir).Read().Count);
    })
};

var failures = 0;
foreach (var (name, run) in tests)
{
    var dir = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try { run(dir); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex}"); }
    finally { Directory.Delete(dir, true); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed"); return failures == 0 ? 0 : 1;

static BookshelfStore Shelf(string dir) => new(Path.Combine(dir, "registry"));
static string SaveBook(string dir, string name, string title)
{
    var folder = Path.Combine(dir, name);
    new ProjectStore(folder).Save(new NovelProject { Title = title, Description = "简介", Entries = [new NovelEntry { Kind = "chapter", Content = "中文 hello world 𠀀" }] });
    return folder;
}
static Dictionary<string, byte[]> SnapshotFiles(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
static void SameFiles(Dictionary<string, byte[]> expected, string folder)
{
    var actual = SnapshotFiles(folder); Equal(expected.Count, actual.Count);
    foreach (var (path, bytes) in expected) True(actual.TryGetValue(path, out var content) && bytes.SequenceEqual(content), "manuscript file changed: " + path);
}
static IEnumerable<string> ThrowOnEnumeration() => Enumerable.Repeat("", 1).Select<string, string>(_ => throw new Exception("existing seed enumerated supplied folders"));
static void True(bool value, string message) { if (!value) throw new Exception(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected exception " + typeof(T).Name);
}
