using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using Mozhou.Core;
using Mozhou.Desktop;

internal static class DocumentNavigationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "documents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var document = Path.Combine(root, "长夜行舟.mozhou");
        var normalized = BookshelfStartup.ProjectFolder(document);
        check(normalized == document, "opening a novel document preserves the selected file instead of using its parent folder");
        var validate = typeof(MainWindow).GetMethod("ValidateNewDocumentPath", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var invalid in new[] { Path.Combine(root, "小说.txt"), Path.Combine(root, "无扩展名") })
        {
            try { validate.Invoke(null, [invalid]); throw new Exception("Expected extension rejection"); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidDataException) { }
            check(!File.Exists(invalid) && !Directory.Exists(invalid), "UI destination cannot silently create legacy folder for " + Path.GetFileName(invalid));
        }
        validate.Invoke(null, [document]);
        var imagePath = Path.Combine(root, "portrait.png");
        ReferenceImageChecks.WriteImage(imagePath, 120, 80);
        var imageBytes = File.ReadAllBytes(imagePath);
        using (var store = new ProjectStore(document))
        {
            store.Save(new NovelProject { Title = "长夜行舟 · 文档检查", Entries = [
                new NovelEntry { Kind = "chapter", Title = "渡口", Content = "灯火渐远，舟行长夜。" },
                new NovelEntry { Kind = "character", Title = "渡舟少年", Content = "在渡口长大的少年。" }
            ] });
            store.SaveChat(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("[{\"Role\":\"user\",\"Text\":\"保存创作思路\"}]")).ToArray());
        }
        var window = new MainWindow(null);
        try
        {
            check((bool)Invoke(window, "TryOpenShelfBook", document)!, "bookshelf opens a standalone document");
            var active = (ProjectStore)Field(window, "_store")!;
            var project = (NovelProject)Field(window, "_project")!;
            check(active.Location == document && active.IsDocument, "editor retains the actual document location");
            check(((UserSettings)Field(window, "_settings")!).LastProjectPath == document, "last project points to document rather than image cache");
            check(((BookshelfStore)Field(window, "_bookshelf")!).Read().Any(book => book.Folder == document), "bookshelf remembers the selected document file");
            check(!Directory.Exists(active.Folder), "opening text-only entry does not extract images or create a cache");
            var chat = (List<ChatRecord>)Field(window, "_chat")!;
            check(!(bool)Field(window, "_chatDamaged")! && chat.Single().Text == "保存创作思路", "document chat supports legacy UTF8 BOM");
            ((TextBox)window.FindName("ContentBox")).Text = "独立文档里的第一段正文。";
            check((bool)Invoke(window, "Flush")!, "editor writes directly to the document");
            using (var reader = new ProjectStore(document))
                check(reader.Load().Entries[0].Content == "独立文档里的第一段正文。", "another reader sees saved document text");
            check((bool)typeof(MainWindow).GetMethod("HasPreviousVersion", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [active])!, "standalone document exposes previous save for recovery UI");
            Invoke(window, "SelectEntry", project.Entries[1]);
            check((bool)Invoke(window, "AddReferenceImages", (object)new[] { imagePath })!, "reference import saves pixels inside document");
            var image = project.Entries[1].Images.Single();
            var cache = active.Folder;
            check(File.ReadAllBytes(active.Images.ResolvePath(image)).SequenceEqual(imageBytes), "embedded reference image displays identical pixels");
            File.Delete(imagePath);
            chat.Add(new ChatRecord { Role = "assistant", Text = "这一段可从渡口的灯火开始。" });
            Invoke(window, "PersistChat");
            ((TextBox)window.FindName("ContentBox")).Text = "未保存的人物补充。";
            var copy = Path.Combine(root, "独立副本.mozhou");
            Invoke(window, "SaveProjectCopy", copy);
            using (var reader = new ProjectStore(copy))
            {
                var copied = reader.Load();
                check(copied.Entries[1].Content == "未保存的人物补充。", "document save-as includes unsaved editor changes");
                check(File.ReadAllBytes(reader.Images.ResolvePath(copied.Entries[1].Images.Single())).SequenceEqual(imageBytes), "document save-as carries independent image bytes");
                check(JsonSerializer.Deserialize<List<ChatRecord>>(reader.ReadChat()!)!.Count == 2, "document save-as includes chat history");
            }
            using (var reader = new ProjectStore(document))
                check(reader.Load().Entries[1].Content == "在渡口长大的少年。", "save-as leaves original document contents unchanged");
            check((bool)Invoke(window, "TryShowBookshelf")!, "document can return to bookshelf after saving");
            check(!Directory.Exists(cache), "leaving document removes its disposable image cache");
            var movedFolder = Path.Combine(root, "moved"); Directory.CreateDirectory(movedFolder);
            var moved = Path.Combine(movedFolder, "长夜行舟.mozhou"); File.Move(document, moved);
            check((bool)Invoke(window, "TryOpenShelfBook", moved)!, "moving only one document preserves editing");
            active = (ProjectStore)Field(window, "_store")!;
            project = (NovelProject)Field(window, "_project")!;
            Invoke(window, "SelectEntry", project.Entries[1]);
            check(File.ReadAllBytes(active.Images.ResolvePath(project.Entries[1].Images.Single())).SequenceEqual(imageBytes), "reference images survive moving document and deleting original image/cache");
            check(((List<ChatRecord>)Field(window, "_chat")!).Count == 2, "chat survives a single-file move");

            using (var external = new ProjectStore(moved))
            {
                external.Load(); external.ReadChat();
                external.SaveChat(JsonSerializer.SerializeToUtf8Bytes(new[] { new ChatRecord { Text = "另一窗口的聊天" } }));
            }
            ((List<ChatRecord>)Field(window, "_chat")!).Add(new ChatRecord { Text = "本窗口未保存的聊天" });
            Invoke(window, "PersistChat");
            check(!(bool)Invoke(window, "TryShowBookshelf")!, "chat save conflict blocks discarding the current window history");
            var chatRecovery = Path.Combine(root, "对话副本.mozhou");
            Invoke(window, "SaveProjectCopy", chatRecovery);
            using (var reader = new ProjectStore(chatRecovery))
                check(JsonSerializer.Deserialize<List<ChatRecord>>(reader.ReadChat()!)!.Last().Text == "本窗口未保存的聊天", "save-as recovers locally unsaved chat after conflict");
            Invoke(window, "ClearChatForProject");
            check(((List<ChatRecord>)Field(window, "_chat")!).Single().Text == "另一窗口的聊天", "explicit reload retains the other editor's saved chat");

            typeof(MainWindow).GetField("_generating", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            var closing = new System.ComponentModel.CancelEventArgs();
            Invoke(window, "WindowClosing", window, closing);
            check(closing.Cancel && ReferenceEquals(active, Field(window, "_store")), "closing during generation defers disposal until partial answer is saved");
            typeof(MainWindow).GetField("_generating", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            typeof(MainWindow).GetField("_closeRequested", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(window, false);

            ((TextBox)window.FindName("ContentBox")).Text = "冲突时仍要保留的修改。";
            using (var external = new ProjectStore(moved))
            {
                var other = external.Load(); other.Title = "外部窗口修改"; external.Save(other);
            }
            check(!(bool)Invoke(window, "Flush")!, "document save detects another editor's changes");
            var recovery = Path.Combine(root, "冲突副本.mozhou");
            Invoke(window, "SaveProjectCopy", recovery);
            using (var reader = new ProjectStore(recovery))
                check(reader.Load().Entries[1].Content == "冲突时仍要保留的修改。", "document save-as recovers unsaved text after conflict");
            using (var reader = new ProjectStore(moved))
                check(reader.Load().Title == "外部窗口修改", "conflict recovery does not overwrite the other editor");
            typeof(MainWindow).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            check((bool)Invoke(window, "TryShowBookshelf")!, "document fixture releases active cache and handles");
        }
        finally
        {
            typeof(MainWindow).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            typeof(MainWindow).GetField("_generating", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            typeof(MainWindow).GetField("_chatSaveFailed", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(window, false);
            window.Close();
        }
    }
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static object? Invoke(object target, string name, params object?[] arguments) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
}
