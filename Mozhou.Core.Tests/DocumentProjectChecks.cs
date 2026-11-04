using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Mozhou.Core;

internal static class DocumentProjectChecks
{
    private const string Main = "project.mozhou.json";
    private const string Previous = "project.mozhou.json.bak";
    private const string Chat = "assistant-chat.json";
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    internal static readonly (string Name, Action<string> Test)[] Cases =
    [
        ("Document save and load persist in the main file without creating an image cache", dir =>
        {
            var path = DocumentPath(dir, "novel");
            using var store = new ProjectStore(path);
            True(store.IsDocument, "new .mozhou path was treated as a folder");
            Equal(Path.GetFullPath(path), store.Location); Equal(Path.GetFullPath(path), store.FilePath);
            var cache = store.Folder; True(!Directory.Exists(cache), "constructing a document created an image cache");
            var project = Project("一本独立小说"); store.Save(project);
            True(File.Exists(path) && !Directory.Exists(path), "saving did not create a single document file");
            Equal("一本独立小说", store.Load().Title);
            store.Images.ValidateReferences(project);
            True(!Directory.Exists(cache), "saving, loading or validating empty references created an image cache");
            var raw = new SingleFileDocument(path).ReadFile(Main);
            True(raw != null, "document did not contain its logical manuscript");
            Equal("正文内容", JsonSerializer.Deserialize<NovelProject>(raw!)!.Entries[0].Content);
        }),
        ("Existing folders take precedence over the .mozhou suffix and existing files remain documents after rename", dir =>
        {
            var legacy = DocumentPath(dir, "legacy-folder"); Directory.CreateDirectory(legacy);
            using (var folderStore = new ProjectStore(legacy))
            {
                True(!folderStore.IsDocument, "existing .mozhou directory was mistaken for a document");
                Equal(Path.Combine(legacy, Main), folderStore.FilePath);
                folderStore.Save(Project("旧文件夹小说")); Equal("旧文件夹小说", folderStore.Load().Title);
                Equal(legacy, folderStore.Folder);
            }
            var original = DocumentPath(dir, "document");
            using (var store = new ProjectStore(original)) store.Save(Project("更换扩展名后仍可读"));
            var renamed = Path.Combine(dir, "renamed-document.bin"); File.Move(original, renamed);
            using var renamedStore = new ProjectStore(renamed);
            True(renamedStore.IsDocument, "existing document file was mistaken for a legacy folder");
            Equal("更换扩展名后仍可读", renamedStore.Load().Title);
        }),
        ("Two document stores reject stale manuscript saves without losing the in-memory draft", dir =>
        {
            var path = DocumentPath(dir, "conflict");
            using (var seed = new ProjectStore(path)) seed.Save(Project("初稿"));
            using var first = new ProjectStore(path); using var second = new ProjectStore(path);
            var firstProject = first.Load(); var secondProject = second.Load();
            firstProject.Title = "另一个窗口的保存"; first.Save(firstProject);
            var persisted = new SingleFileDocument(path).ReadFile(Main)!;
            secondProject.Entries[0].Content = "不能丢失的窗口草稿";
            Throws<ConflictException>(() => second.Save(secondProject));
            SameBytes(persisted, new SingleFileDocument(path).ReadFile(Main), "stale save overwrote the other window");
            Equal("不能丢失的窗口草稿", secondProject.Entries[0].Content);
        }),
        ("Externally deleted documents are not recreated by a stale save", dir =>
        {
            var path = DocumentPath(dir, "deleted"); using var store = new ProjectStore(path);
            store.Save(Project("原小说")); var project = store.Load(); File.Delete(path);
            project.Title = "未保存修改"; Throws<ConflictException>(() => store.Save(project));
            True(!File.Exists(path), "stale save recreated an externally deleted document");
            Equal("未保存修改", project.Title);
        }),
        ("Missing saved images allow manuscript edits but reject complete backups and copies", dir =>
        {
            var path = DocumentPath(dir, "missing-image"); var project = Project("缺图仍能写作");
            project.FormatVersion = 2; project.Entries[1].Images.Add(Image("遗失的图片"));
            SingleFileDocument.CreateNew(path, new Dictionary<string, byte[]> { [Main] = JsonSerializer.SerializeToUtf8Bytes(project) });
            using var store = new ProjectStore(path); var loaded = store.Load(); loaded.Entries[0].Content = "图片遗失后的新正文";
            store.Save(loaded); Equal("图片遗失后的新正文", store.Load().Entries[0].Content);
            True(!Directory.Exists(store.Folder), "editing a missing-image document created a cache");
            var zip = Path.Combine(dir, "incomplete.zip"); var copy = DocumentPath(dir, "incomplete-copy");
            Throws<FileNotFoundException>(() => store.CreateBackup(zip)); Throws<FileNotFoundException>(() => store.SaveDocumentCopy(copy, loaded));
            True(!File.Exists(zip) && !File.Exists(copy), "an incomplete backup or copy was published");
        }),
        ("Deleting a loaded source refuses a supposedly complete copy while dirty text export remains available", dir =>
        {
            var path = DocumentPath(dir, "deleted-copy-source"); using var store = new ProjectStore(path);
            var project = Project("完整源稿"); store.Save(project); store.SaveChat(Bytes("[\"不可遗漏的对话\"]"));
            project.Entries[0].Content = "窗口中的待抢救正文"; File.Delete(path);
            var target = DocumentPath(dir, "incomplete-copy"); Throws<FileNotFoundException>(() => store.SaveDocumentCopy(target, project));
            True(!File.Exists(target), "a copy without deleted source history and chat was published as complete");
            var text = Path.Combine(dir, "rescue.txt"); store.ExportText(text); True(File.ReadAllText(text).Contains("窗口中的待抢救正文"), "source deletion blocked dirty-text rescue");
        }),
        ("Pending image imports stay outside the document until its manuscript save commits", dir =>
        {
            var path = DocumentPath(dir, "pending"); using var store = new ProjectStore(path);
            var project = Project("带图片的小说"); store.Save(project);
            var before = new SingleFileDocument(path).ReadFile(Main)!;
            var image = store.Images.Import(Source(dir, "人物.png")); project.Entries[1].Images.Add(image);
            var logicalImage = ImagePath(image);
            SameBytes(before, new SingleFileDocument(path).ReadFile(Main), "image import prematurely committed manuscript metadata");
            True(new SingleFileDocument(path).ReadFile(logicalImage) == null, "pending import prematurely changed the document blob store");
            store.Save(project);
            SameBytes(Png, new SingleFileDocument(path).ReadFile(logicalImage), "saved document omitted imported image bytes");
            var loaded = store.Load(); Equal(image.FileName, loaded.Entries[1].Images.Single().FileName); Equal(2, loaded.FormatVersion);
            loaded.Title = "只修改文字"; store.Save(loaded);
            Equal(1, new SingleFileDocument(path).ListFiles("assets/images/").Count());
            SameBytes(Png, new SingleFileDocument(path).ReadFile(logicalImage), "later text save changed an existing image");
        }),
        ("Failed document saves retain pending images for a complete rescue copy", dir =>
        {
            var path = DocumentPath(dir, "pending-conflict"); using (var seed = new ProjectStore(path)) seed.Save(Project("初始版本"));
            using var stale = new ProjectStore(path); var dirty = stale.Load();
            var image = stale.Images.Import(Source(dir, "pending.png")); dirty.Entries[1].Images.Add(image); dirty.Entries[0].Content = "窗口中的抢救草稿";
            using (var other = new ProjectStore(path)) { var external = other.Load(); external.Title = "其他窗口已保存"; other.Save(external); }
            var persisted = new SingleFileDocument(path).ReadFile(Main)!;
            Throws<ConflictException>(() => stale.Save(dirty));
            SameBytes(persisted, new SingleFileDocument(path).ReadFile(Main), "failed transaction changed the current manuscript");
            True(new SingleFileDocument(path).ReadFile(ImagePath(image)) == null, "failed transaction leaked a pending image into the document");
            SameBytes(Png, File.ReadAllBytes(stale.Images.ResolvePath(image)), "save conflict discarded the pending image needed for rescue");
            var rescued = DocumentPath(dir, "rescued"); stale.SaveDocumentCopy(rescued, dirty);
            using var copy = new ProjectStore(rescued); var restored = copy.Load();
            Equal("窗口中的抢救草稿", restored.Entries[0].Content);
            SameBytes(Png, File.ReadAllBytes(copy.Images.ResolvePath(restored.Entries[1].Images.Single())), "rescue copy lost the pending image");
        }),
        ("Document removal and previous-save restoration retain historical image bytes", dir =>
        {
            var path = DocumentPath(dir, "history"); using var store = new ProjectStore(path);
            var project = Project("图片历史"); var image = store.Images.Import(Source(dir, "history.png")); project.Entries[1].Images.Add(image); store.Save(project);
            project.Entries[1].Images.Clear(); store.Save(project);
            SameBytes(Png, new SingleFileDocument(path).ReadFile(ImagePath(image)), "removing an association deleted history image bytes");
            var previous = store.RestorePrevious(); Equal(image.FileName, previous.Entries[1].Images.Single().FileName);
            SameBytes(Png, File.ReadAllBytes(store.Images.ResolvePath(previous.Entries[1].Images.Single())), "restored previous version cannot resolve its image");
        }),
        ("Closing an image session removes only its cache and a reopened document can materialize images again", dir =>
        {
            var path = DocumentPath(dir, "reopen"); string cache; EntryImage image;
            using (var store = new ProjectStore(path))
            {
                var project = Project("缓存可重建"); image = store.Images.Import(Source(dir, "reopen.png")); project.Entries[1].Images.Add(image); store.Save(project);
                cache = store.Folder; var resolved = store.Images.ResolvePath(image);
                True(Directory.Exists(cache), "resolving an image did not create its cache"); SameBytes(Png, File.ReadAllBytes(resolved), "initial image cache is wrong");
            }
            True(!Directory.Exists(cache), "disposing a document kept its owned image cache"); True(File.Exists(path), "disposing removed the primary document");
            using var reopened = new ProjectStore(path); var loaded = reopened.Load();
            True(!Directory.Exists(reopened.Folder), "loading a document with images extracted its cache eagerly");
            reopened.Images.ValidateReferences(loaded);
            True(!Directory.Exists(reopened.Folder), "reference validation materialized images instead of checking the document");
            SameBytes(Png, File.ReadAllBytes(reopened.Images.ResolvePath(loaded.Entries[1].Images.Single())), "reopening depended on a deleted cache");
        }),
        ("Chat writes have their own baseline and cannot overwrite another window's newer chat", dir =>
        {
            var path = DocumentPath(dir, "chat-conflict"); var original = Bytes("[\"原对话\"]"); var latest = Bytes("[\"另一个窗口的新对话\"]");
            using (var seed = new ProjectStore(path)) { seed.Save(Project("聊天测试")); True(seed.ReadChat() == null, "new document contains invented chat"); seed.SaveChat(original); }
            using var first = new ProjectStore(path); using var second = new ProjectStore(path);
            var firstProject = first.Load(); var secondProject = second.Load(); SameBytes(original, first.ReadChat(), "first chat read failed"); SameBytes(original, second.ReadChat(), "second chat read failed");
            first.SaveChat(latest); Throws<ConflictException>(() => second.SaveChat(Bytes("[\"旧窗口企图覆盖\"]")));
            SameBytes(latest, new SingleFileDocument(path).ReadFile(Chat), "stale chat replaced a newer transcript");
            secondProject.Title = "正文单独保存"; second.Save(secondProject);
            SameBytes(latest, new SingleFileDocument(path).ReadFile(Chat), "manuscript save overwrote a separately updated transcript");
            firstProject.Title = "仍需检查正文自己的基线"; Throws<ConflictException>(() => first.Save(firstProject));
        }),
        ("Complete document copies contain dirty text, pending images, all image history and raw damaged history", dir =>
        {
            var sourcePath = DocumentPath(dir, "copy-source"); var currentImage = Image("current"); var historicalImage = Image("historical");
            var project = Project("磁盘标题"); project.FormatVersion = 2; project.Entries[1].Images.Add(currentImage);
            var old = Project("上一次保存"); old.FormatVersion = 2; old.Entries[1].Images.Add(historicalImage);
            var mainBytes = JsonSerializer.SerializeToUtf8Bytes(project); var previousBytes = JsonSerializer.SerializeToUtf8Bytes(old);
            var damagedHistory = new byte[] { 0xFF, 0x00, 0x81, 0x0D, 0x0A }; var chat = Bytes("[\"源对话记录\"]"); var overrideChat = Bytes("[\"副本中的最新对话\"]");
            SingleFileDocument.CreateNew(sourcePath, new Dictionary<string, byte[]>
            {
                [Main] = mainBytes, [Previous] = previousBytes, ["backups/project-damaged.json"] = damagedHistory,
                ["backups/project-history.json"] = previousBytes, [Chat] = chat, [ImagePath(currentImage)] = Png, [ImagePath(historicalImage)] = Png
            });
            using var source = new ProjectStore(sourcePath); var dirty = source.Load(); dirty.Entries[0].Content = "完整副本必须包含的未保存正文";
            var pending = source.Images.Import(Source(dir, "pending-copy.png")); dirty.Entries[1].Images.Add(pending);
            pending.Hairstyle = "高马尾"; pending.Outfit = "巡夜披风";
            dirty.Entries[1].Images[0].LookName = pending.LookName = "雪夜巡城";
            var target = DocumentPath(dir, "complete-copy"); source.SaveDocumentCopy(target, dirty, overrideChat);
            using var copy = new ProjectStore(target); var loaded = copy.Load();
            Equal("完整副本必须包含的未保存正文", loaded.Entries[0].Content); Equal(2, loaded.Entries[1].Images.Count);
            Equal(3, loaded.FormatVersion);
            var copiedLook = loaded.Entries[1].Images.Single(image => image.Id == pending.Id);
            Equal("高马尾", copiedLook.Hairstyle); Equal("巡夜披风", copiedLook.Outfit);
            True(loaded.Entries[1].Images.All(image => image.LookName == "雪夜巡城"), "images sharing a named look lost their name in the copy");
            var document = new SingleFileDocument(target);
            foreach (var image in new[] { currentImage, historicalImage, pending }) SameBytes(Png, document.ReadFile(ImagePath(image)), "complete copy omitted a current, pending or historical image");
            SameBytes(previousBytes, document.ReadFile(Previous), "copy discarded the original previous save");
            SameBytes(damagedHistory, document.ReadFile("backups/project-damaged.json"), "copy parsed or changed raw damaged history");
            SameBytes(previousBytes, document.ReadFile("backups/project-history.json"), "copy omitted readable history");
            SameBytes(overrideChat, copy.ReadChat(), "copy did not preserve its requested chat snapshot");
            SameBytes(mainBytes, new SingleFileDocument(sourcePath).ReadFile(Main), "creating a rescue copy saved the source's dirty draft");
            SameBytes(chat, new SingleFileDocument(sourcePath).ReadFile(Chat), "creating a copy changed source chat");
        }),
        ("Document copies never overwrite an existing destination", dir =>
        {
            var path = DocumentPath(dir, "source"); using var source = new ProjectStore(path); var project = Project("原稿"); source.Save(project);
            var destination = DocumentPath(dir, "occupied"); var sentinel = Bytes("do not replace this existing file"); File.WriteAllBytes(destination, sentinel);
            ThrowsRejected(() => source.SaveDocumentCopy(destination, project)); SameBytes(sentinel, File.ReadAllBytes(destination), "copy replaced an existing destination");
            Equal("原稿", source.Load().Title);
        }),
        ("ZIP backups expose logical project files rather than database implementation files", dir =>
        {
            var path = DocumentPath(dir, "zip"); var image = Image("zip image"); var project = Project("可移植备份"); project.FormatVersion = 2; project.Entries[1].Images.Add(image);
            var expected = new Dictionary<string, byte[]>
            {
                [Main] = JsonSerializer.SerializeToUtf8Bytes(project), [Previous] = JsonSerializer.SerializeToUtf8Bytes(Project("上一版")),
                [Chat] = Bytes("[\"对话原字节\"]"), ["backups/project-raw.json"] = [0xFF, 0, 0x81], [ImagePath(image)] = Png
            };
            SingleFileDocument.CreateNew(path, expected); using var store = new ProjectStore(path); store.Load();
            var zip = Path.Combine(dir, "backup.zip"); store.CreateBackup(zip);
            using var archive = ZipFile.OpenRead(zip); Equal(expected.Count, archive.Entries.Count);
            foreach (var (name, bytes) in expected)
            {
                var entry = archive.GetEntry(name); True(entry != null, "ZIP omitted a logical file: " + name);
                using var input = entry!.Open(); using var output = new MemoryStream(); input.CopyTo(output);
                SameBytes(bytes, output.ToArray(), "ZIP changed a logical file: " + name);
            }
            True(archive.Entries.All(entry => !entry.FullName.EndsWith(".mozhou", StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith("-wal") && !entry.FullName.EndsWith("-shm")), "ZIP leaked database implementation files");
        }),
        ("Exports cannot overwrite a document, its database sidecars or its image cache", dir =>
        {
            var path = DocumentPath(dir, "protected"); using var store = new ProjectStore(path); var project = Project("保护文档");
            var image = store.Images.Import(Source(dir, "protected.png")); project.Entries[1].Images.Add(image); store.Save(project);
            var cachedImage = store.Images.ResolvePath(image); var original = new SingleFileDocument(path).ReadFile(Main)!;
            foreach (var destination in new[] { path, path + "-journal", path + "-wal", path + "-shm", cachedImage, Path.Combine(store.Folder, "unrelated-export.txt") })
            {
                ThrowsRejected(() => store.ExportMarkdown(destination)); ThrowsRejected(() => store.ExportText(destination)); ThrowsRejected(() => store.CreateBackup(destination));
            }
            SameBytes(original, new SingleFileDocument(path).ReadFile(Main), "protected export changed the document manuscript");
            SameBytes(Png, File.ReadAllBytes(cachedImage), "protected export replaced the image cache");
            var safe = Path.Combine(dir, "正文.txt"); store.ExportText(safe); True(File.ReadAllText(safe).Contains("正文内容"), "safe text export was blocked");
        }),
        ("Legacy folders convert to complete document copies while retaining their original files", dir =>
        {
            var folder = Path.Combine(dir, "legacy"); Directory.CreateDirectory(folder);
            using var legacy = new ProjectStore(folder); var project = Project("旧版目录"); var image = legacy.Images.Import(Source(dir, "legacy.png")); project.Entries[1].Images.Add(image); legacy.Save(project);
            project.Title = "旧版第二稿"; legacy.Save(project);
            var history = Path.Combine(folder, "backups", "project-damaged.json"); var damaged = new byte[] { 0xFF, 1, 0x81 }; File.WriteAllBytes(history, damaged);
            var chat = Bytes("[\"旧版完整聊天\"]"); True(legacy.ReadChat() == null, "legacy folder started with invented chat"); legacy.SaveChat(chat);
            var original = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllBytes);
            project.Entries[0].Content = "尚未写回旧目录的草稿"; var destination = DocumentPath(dir, "converted"); legacy.SaveDocumentCopy(destination, project);
            using var converted = new ProjectStore(destination); var loaded = converted.Load(); Equal("尚未写回旧目录的草稿", loaded.Entries[0].Content);
            SameBytes(Png, File.ReadAllBytes(converted.Images.ResolvePath(loaded.Entries[1].Images.Single())), "legacy conversion omitted image bytes");
            SameBytes(damaged, new SingleFileDocument(destination).ReadFile("backups/project-damaged.json"), "legacy conversion changed damaged history");
            SameBytes(chat, converted.ReadChat(), "legacy conversion omitted raw chat");
            foreach (var (file, bytes) in original) SameBytes(bytes, File.ReadAllBytes(file), "legacy conversion changed an original file");
        })
    ];

    private static NovelProject Project(string title) => new()
    {
        Title = title,
        Entries = [new NovelEntry { Kind = "chapter", Title = "第一章", Content = "正文内容" }, new NovelEntry { Kind = "character", Title = "人物", Content = "人物资料" }]
    };
    private static EntryImage Image(string title) => new() { FileName = Guid.NewGuid().ToString("N") + ".png", Title = title };
    private static string ImagePath(EntryImage image) => "assets/images/" + image.FileName;
    private static string DocumentPath(string dir, string name) => Path.Combine(dir, name + ".mozhou");
    private static string Source(string dir, string name) { var path = Path.Combine(dir, name); File.WriteAllBytes(path, Png); return path; }
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static void SameBytes(byte[] expected, byte[]? actual, string message) => True(actual != null && expected.AsSpan().SequenceEqual(actual), message);
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected exception " + typeof(T).Name);
    }
    private static void ThrowsRejected(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or NotSupportedException) { return; }
        throw new Exception("Expected invalid or unsafe document operation to be rejected");
    }
}
