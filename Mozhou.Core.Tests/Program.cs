using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Mozhou.Core;

var tests = new (string Name, Action<string> Test)[]
{
    ("Unicode roundtrip, title rename, graph links", dir =>
    {
        var p = Fixture(); var store = new ProjectStore(dir); store.Save(p);
        var loaded = new ProjectStore(dir).Load(); Equal("海上🌊的约定", loaded.Title);
        Equal("雪落京城，hello world。𠀀", loaded.Entries[0].Content);
        Equal("乙", loaded.Entries[1].LinkedIds.Single()); Equal(123.5, loaded.Entries[1].X);
        Equal(234.25, loaded.Entries[1].Y); Equal("乙", loaded.Relations.Single().ToId);
        loaded.Title = "改名后的小说"; var second = new ProjectStore(dir); second.Load(); second.Save(loaded);
        Equal("改名后的小说", new ProjectStore(dir).Load().Title);
    }),
    ("External writes refused, including same length and timestamp", dir =>
    {
        var s = new ProjectStore(dir); s.Save(Fixture()); var p = s.Load();
        var stamp = File.GetLastWriteTimeUtc(s.FilePath);
        var external = File.ReadAllText(s.FilePath).Replace("海上", "岸边");
        File.WriteAllText(s.FilePath, external); File.SetLastWriteTimeUtc(s.FilePath, stamp);
        Throws<ConflictException>(() => s.Save(p)); Equal(external, File.ReadAllText(s.FilePath));
        Throws<ConflictException>(() => new ProjectStore(dir).Save(Fixture()));
    }),
    ("External deletion is refused", dir =>
    {
        var s = new ProjectStore(dir); s.Save(Fixture()); var p = s.Load(); File.Delete(s.FilePath);
        Throws<ConflictException>(() => s.Save(p)); True(!File.Exists(s.FilePath), "deleted file recreated");
    }),
    ("Previous version recovery and bounded timestamp backups", dir =>
    {
        var s = new ProjectStore(dir); var p = Fixture(); s.Save(p); p.Title = "第二版"; s.Save(p);
        Equal("海上🌊的约定", JsonSerializer.Deserialize<NovelProject>(File.ReadAllText(s.FilePath + ".bak"))!.Title);
        Equal("海上🌊的约定", s.RestorePrevious().Title);
        for (var i = 0; i < 34; i++) { p.Title = "版本" + i; s.Save(p); }
        Equal(30, Directory.GetFiles(Path.Combine(dir, "backups"), "*.json").Length);
        True(!Directory.GetFiles(dir, "*.tmp").Any(), "temporary file leaked");
    }),
    ("Invalid JSON preserved and valid backup recovers it", dir =>
    {
        var s = new ProjectStore(dir); var p = Fixture(); s.Save(p); p.Title = "新版"; s.Save(p);
        const string broken = "{broken-中文"; File.WriteAllText(s.FilePath, broken);
        var recovery = new ProjectStore(dir); Throws<JsonException>(() => recovery.Load());
        Equal(broken, File.ReadAllText(s.FilePath));
        Equal("海上🌊的约定", recovery.RestorePrevious().Title);
        Equal("海上🌊的约定", new ProjectStore(dir).Load().Title);
    }),
    ("Invalid model and backup cannot destroy disk", dir =>
    {
        var s = new ProjectStore(dir); s.Save(Fixture()); var original = File.ReadAllText(s.FilePath);
        File.WriteAllText(s.FilePath + ".bak", "null"); Throws<InvalidDataException>(() => s.RestorePrevious());
        Equal(original, File.ReadAllText(s.FilePath));
        File.WriteAllText(s.FilePath, "{\"FormatVersion\":999,\"Entries\":[],\"Relations\":[]}");
        Throws<InvalidDataException>(() => new ProjectStore(dir).Load());
        File.WriteAllText(s.FilePath, "{\"Entries\":null,\"Relations\":[]}");
        Throws<InvalidDataException>(() => new ProjectStore(dir).Load());
    }),
    ("Incomplete document is rejected without inventing a new project", dir =>
    {
        var s = new ProjectStore(dir); const string incomplete = "{}"; File.WriteAllText(s.FilePath, incomplete);
        Throws<InvalidDataException>(() => s.Load()); Equal(incomplete, File.ReadAllText(s.FilePath));
    }),
    ("Human edited UTF8 BOM manuscript loads", dir =>
    {
        var s = new ProjectStore(dir); s.Save(Fixture()); var contents = File.ReadAllText(s.FilePath);
        File.WriteAllText(s.FilePath, contents, new UTF8Encoding(true));
        Equal("海上🌊的约定", new ProjectStore(dir).Load().Title);
    }),
    ("Locked target save failure leaves original and backup readable", dir =>
    {
        var s = new ProjectStore(dir); var p = Fixture(); s.Save(p); p.Title = "第二版"; s.Save(p);
        for (var i = 0; i < 30; i++) { p.Title = "锁定前" + i; s.Save(p); }
        var original = File.ReadAllBytes(s.FilePath); var backup = File.ReadAllBytes(s.FilePath + ".bak");
        p.Title = "失败的保存";
        using (var reader = new FileStream(s.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var refused = false;
            try { s.Save(p); } catch (IOException) { refused = true; } catch (UnauthorizedAccessException) { refused = true; }
            True(refused, "locked file unexpectedly replaced");
            True(original.SequenceEqual(File.ReadAllBytes(s.FilePath)), "failed save changed main file");
            True(backup.SequenceEqual(File.ReadAllBytes(s.FilePath + ".bak")), "failed save changed backup");
            True(Directory.GetFiles(Path.Combine(dir, "backups"), "*.json").Length <= 30, "failed save exceeded backup retention");
        }
        s.Save(p); Equal("失败的保存", new ProjectStore(dir).Load().Title);
        True(!Directory.GetFiles(dir, "*.tmp").Any(), "failed save leaked temporary file");
    }),
    ("Dangling relation refuses save without changing manuscript", dir =>
    {
        var s = new ProjectStore(dir); var p = Fixture(); s.Save(p); var original = File.ReadAllBytes(s.FilePath);
        p.Relations[0].ToId = "不存在的人物";
        Throws<InvalidDataException>(() => s.Save(p));
        True(original.SequenceEqual(File.ReadAllBytes(s.FilePath)), "invalid graph overwrote manuscript");
    }),
    ("ZIP contains readable project and previous save", dir =>
    {
        var s = new ProjectStore(dir); var p = Fixture(); s.Save(p); p.Title = "ZIP小说"; s.Save(p);
        File.WriteAllText(Path.Combine(dir, "assistant-chat.json"), "[\"小说对话\"]");
        var zip = Path.Combine(dir, "备份.zip"); s.CreateBackup(zip);
        using var archive = ZipFile.OpenRead(zip);
        True(archive.GetEntry("project.mozhou.json.bak") != null, "missing previous save");
        True(archive.GetEntry("assistant-chat.json") != null, "missing local chat");
        using var reader = new StreamReader(archive.GetEntry("project.mozhou.json")!.Open());
        Equal("ZIP小说", JsonSerializer.Deserialize<NovelProject>(reader.ReadToEnd())!.Title);
        True(archive.Entries.All(e => !e.Name.EndsWith(".zip")), "backup includes itself");
    }),
    ("ZIP preserves exact manuscript, history and chat bytes across copy buffers", dir =>
    {
        var store = new ProjectStore(dir); var project = Fixture();
        project.Entries[0].Content = string.Concat(Enumerable.Repeat("大篇幅正文🌊𠀀\r\n", 12000));
        store.Save(project); project.Title = "第二版"; store.Save(project); project.Title = "第三版"; store.Save(project);
        var backups = Path.Combine(dir, "backups");
        // 恢复现场可能包含空文件或损坏字节，ZIP 必须原样保留，不能转码或再次解析。
        File.WriteAllBytes(Path.Combine(backups, "project-empty.json"), []);
        File.WriteAllBytes(Path.Combine(backups, "project-damaged.json"), [0xFF, 0x00, 0x81, 0x0D, 0x0A]);
        var chat = Path.Combine(dir, "assistant-chat.json");
        File.WriteAllText(chat, "[\r\n  \"" + string.Concat(Enumerable.Repeat("跨缓冲区对话🌊𠀀", 12000)) + "\"\r\n]\r\n", new UTF8Encoding(true));
        var expected = new Dictionary<string, byte[]>
        {
            ["project.mozhou.json"] = File.ReadAllBytes(store.FilePath),
            ["project.mozhou.json.bak"] = File.ReadAllBytes(store.FilePath + ".bak"),
            ["assistant-chat.json"] = File.ReadAllBytes(chat)
        };
        foreach (var path in Directory.GetFiles(backups, "*.json"))
            expected.Add("backups/" + Path.GetFileName(path), File.ReadAllBytes(path));

        var destination = Path.Combine(dir, "完整备份.zip"); store.CreateBackup(destination);
        using var archive = ZipFile.OpenRead(destination);
        Equal(expected.Count, archive.Entries.Count);
        foreach (var (name, bytes) in expected)
        {
            var entry = archive.GetEntry(name); True(entry != null, "missing ZIP entry: " + name);
            using var input = entry!.Open(); using var output = new MemoryStream(); input.CopyTo(output);
            True(bytes.AsSpan().SequenceEqual(output.ToArray()), "ZIP changed bytes: " + name);
        }
        True(!Directory.GetFiles(dir, "*.tmp").Any(), "backup leaked temporary file");
    }),
    ("Export destinations cannot overwrite project or chat data", dir =>
    {
        var s = new ProjectStore(dir); s.Save(Fixture()); var original = File.ReadAllBytes(s.FilePath);
        Throws<IOException>(() => s.ExportText(s.FilePath));
        Throws<IOException>(() => s.CreateBackup(s.FilePath + ".bak"));
        var chat = Path.Combine(dir, "assistant-chat.json"); File.WriteAllText(chat, "private transcript");
        Throws<IOException>(() => s.ExportMarkdown(chat)); Equal("private transcript", File.ReadAllText(chat));
        True(original.SequenceEqual(File.ReadAllBytes(s.FilePath)), "export overwrote project");
    }),
    ("Exports follow chapter list order and exclude settings", dir =>
    {
        var s = new ProjectStore(dir); var p = Fixture(); p.Entries.Insert(0, new NovelEntry { Kind = "chapter", Title = "先写的序章", Group = "上卷", Content = "序章正文" });
        s.Save(p); var md = Path.Combine(dir, "导出.md"); var txt = Path.Combine(dir, "导出.txt");
        s.ExportMarkdown(md); s.ExportText(txt);
        foreach (var file in new[] { md, txt })
        {
            var text = File.ReadAllText(file); True(text.IndexOf("序章正文") < text.IndexOf("雪落京城"), "wrong chapter order");
            True(text.Contains("上卷") && text.Contains("海上🌊的约定"), "missing headings");
            True(!text.Contains("绝密人设"), "export includes settings");
        }
    }),
    ("Conflict still permits dirty in-memory draft export without touching internal data", dir =>
    {
        var s = new ProjectStore(dir); var p = Fixture(); s.Save(p); p.Title = "保存的第二版"; s.Save(p);
        p = s.Load(); p.Entries[0].Content = "尚未保存的珍贵草稿𠀀"; p.Title = "内存中的书名";
        var external = File.ReadAllText(s.FilePath).Replace("保存的第二版", "外部程序版本");
        File.WriteAllText(s.FilePath, external);
        var main = File.ReadAllBytes(s.FilePath); var previous = File.ReadAllBytes(s.FilePath + ".bak");
        var timestamp = Directory.GetFiles(Path.Combine(dir, "backups"), "*.json").Single();
        var timestampBytes = File.ReadAllBytes(timestamp);
        var chat = Path.Combine(dir, "assistant-chat.json"); File.WriteAllText(chat, "本地对话");
        Throws<ConflictException>(() => s.Save(p));
        var md = Path.Combine(dir, "抢救草稿.md"); var txt = Path.Combine(dir, "抢救草稿.txt");
        s.ExportMarkdown(md); s.ExportText(txt);
        foreach (var file in new[] { md, txt })
        {
            var content = File.ReadAllText(file);
            True(content.Contains("尚未保存的珍贵草稿𠀀") && content.Contains("内存中的书名"), "export lost dirty draft");
            True(!content.Contains("外部程序版本"), "export replaced in-memory draft with disk data");
        }
        foreach (var protectedPath in new[] { s.FilePath, s.FilePath + ".bak", timestamp, chat, Path.Combine(dir, "project.mozhou.lock") })
        {
            Throws<IOException>(() => s.ExportMarkdown(protectedPath));
            Throws<IOException>(() => s.ExportText(protectedPath));
        }
        True(main.SequenceEqual(File.ReadAllBytes(s.FilePath)), "draft export changed external main");
        True(previous.SequenceEqual(File.ReadAllBytes(s.FilePath + ".bak")), "draft export changed previous save");
        True(timestampBytes.SequenceEqual(File.ReadAllBytes(timestamp)), "draft export changed timestamp backup");
        Equal("本地对话", File.ReadAllText(chat));
    }),
    ("Prompt isolates current entry and selected setting kinds", _ =>
    {
        var p = Fixture(); p.Entries.AddRange([new NovelEntry { Kind = "chapter", Content = "未选章节秘密" }, new NovelEntry { Kind = "world", Content = "月球设定" }, new NovelEntry { Kind = "outline", Content = "终局大纲" }, new NovelEntry { Kind = "note", Content = "私密灵感" }]);
        var prompt = ProjectContent.BuildPrompt(p, p.Entries[0], "润色当前段落", true);
        True(prompt.Contains("雪落京城") && prompt.Contains("绝密人设") && prompt.Contains("月球设定") && prompt.Contains("终局大纲"), "missing selected context");
        True(!prompt.Contains("未选章节秘密") && !prompt.Contains("私密灵感"), "unselected prose leaked");
        True(prompt.IndexOf("雪落京城") < prompt.IndexOf("绝密人设"), "current entry not first");
        var noSettings = ProjectContent.BuildPrompt(p, p.Entries[0], "润色", false);
        True(!noSettings.Contains("绝密人设"), "settings leaked when disabled");
        True(ProjectContent.BuildPrompt(p, null, "构思", false).Contains("构思"), "null entry loses instruction");
    }),
    ("Oversized context is capped with visible truncation", _ =>
    {
        var p = Fixture(); p.Entries[0].Content = string.Concat(Enumerable.Repeat("𠀀中", 50000));
        var prompt = ProjectContent.BuildPrompt(p, p.Entries[0], "请续写", true);
        True(prompt.Length <= 24000 && prompt.Contains("截断") && prompt.Contains("请续写"), "cap/truncation/instruction missing");
        True(!char.IsHighSurrogate(prompt[^1]), "split Unicode scalar");
    }),
    ("Selected settings include novel identity and directed named character relations", _ =>
    {
        var p = Fixture(); p.Description = "一封无人领取的信引发的港口悬案";
        var prompt = ProjectContent.BuildPrompt(p, p.Entries[0], "根据人物关系续写", true);
        True(prompt.Contains("海上🌊的约定") && prompt.Contains(p.Description), "missing novel title or description");
        True(prompt.Contains("阿青 → 小白") && prompt.Contains("故友") && prompt.Contains("十年之约"), "missing directed named relation or note");
        True(!prompt.Contains("小白 → 阿青"), "relationship direction reversed");
        True(prompt.IndexOf("雪落京城", StringComparison.Ordinal) < prompt.IndexOf(p.Description, StringComparison.Ordinal), "project metadata displaced current entry");
        True(prompt.IndexOf("雪落京城", StringComparison.Ordinal) < prompt.IndexOf("阿青 → 小白", StringComparison.Ordinal), "graph displaced current entry");
        var minimal = ProjectContent.BuildPrompt(p, p.Entries[0], "继续写", false);
        True(!minimal.Contains(p.Description) && !minimal.Contains("海上🌊的约定") && !minimal.Contains("阿青") && !minimal.Contains("故友") && !minimal.Contains("十年之约"), "unselected metadata or relationship leaked");
    }),
    ("Large relationship context remains bounded and keeps current draft first", _ =>
    {
        var p = Fixture(); p.Description = "短篇港口悬案"; p.Relations[0].Note = string.Concat(Enumerable.Repeat("关系秘密𠀀", 20000));
        var prompt = ProjectContent.BuildPrompt(p, p.Entries[0], "按关系续写", true);
        True(prompt.Length <= 24000 && prompt.Contains("截断") && prompt.Contains("雪落京城") && prompt.Contains("按关系续写"), "oversized graph lost cap/current/instruction/truncation");
        True(prompt.Contains("阿青 → 小白") && prompt.Contains("关系秘密𠀀"), "graph omitted instead of bounded");
    }),
    ("Word counts use Han characters and Latin words", _ =>
    {
        Equal(7, ProjectContent.CountWords("中文，hello world！𠀀 test-case 123"));
        Equal(0, ProjectContent.CountWords(" \t\r\n，。!?🌊"));
        Equal(3, ProjectContent.CountWords("你好abc"));
    }),
    ("Factories create editable content and valid demo relations", _ =>
    {
        Equal("我的书", ProjectContent.CreateEmpty("我的书").Title);
        var demo = ProjectContent.CreateDemo(); True(demo.Entries.Any(e => e.Kind == "chapter"), "demo has no manuscript");
        True(demo.Relations.All(r => demo.Entries.Any(e => e.Id == r.FromId) && demo.Entries.Any(e => e.Id == r.ToId)), "dangling demo relation");
        True(ProjectContent.CreateEntry("character").Content.Contains("性格"), "missing character template");
    })
};
tests = tests.Concat(StatisticsChecks.Cases).ToArray();
var failures = 0;
foreach (var (name, test) in tests)
{
    var dir = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try { test(dir); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex}"); }
    finally { Directory.Delete(dir, true); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed"); return failures == 0 ? 0 : 1;

static NovelProject Fixture() => new()
{
    Title = "海上🌊的约定",
    Entries = [new NovelEntry { Kind = "chapter", Title = "第一章", Group = "上卷", Content = "雪落京城，hello world。𠀀" }, new NovelEntry { Id = "甲", Kind = "character", Title = "阿青", Content = "绝密人设", X = 123.5, Y = 234.25, LinkedIds = ["乙"] }, new NovelEntry { Id = "乙", Kind = "character", Title = "小白" }],
    Relations = [new CharacterRelation { FromId = "甲", ToId = "乙", Label = "故友", Note = "十年之约" }]
};
static void True(bool value, string message) { if (!value) throw new Exception(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected exception " + typeof(T).Name);
}
