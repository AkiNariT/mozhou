using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Mozhou.Core;
using Mozhou.Desktop;

internal static class Program
{
    private static int _checks;

    [STAThread] private static void Main()
    {
        var app = new App(); app.InitializeComponent();
        var dir = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var store = new ProjectStore(dir); var p = ProjectContent.CreateDemo();
        store.Save(p); p.Title = "newer"; store.Save(p);
        var window = new MainWindow(null);
        Invoke(window, "OpenFolder", dir);
        foreach (var invalid in new[] { "[null]", "null", "[{\"Role\":\"assistant\",\"Text\":null}]", "[{\"Role\":\"invalid\",\"Text\":\"x\"}]" })
        {
            File.WriteAllText(Path.Combine(dir, "assistant-chat.json"), invalid);
            Invoke(window, "ClearChatForProject");
            Check((bool)Field(window, "_chatDamaged")!, "invalid chat is marked damaged: " + invalid);
            Check(((string)Invoke(window, "BuildRequest", "test")!).Contains("test"), "invalid chat does not block new request");
            Invoke(window, "PersistChat");
            Check(File.ReadAllText(Path.Combine(dir, "assistant-chat.json")) == invalid, "invalid chat original remains intact");
        }
        ((TextBox)window.FindName("ContentBox")).Text = "unsaved chapter text";
        var external = File.ReadAllText(store.FilePath).Replace("newer", "external");
        File.WriteAllText(store.FilePath, external);
        Check((bool)Invoke(window, "Flush")! == false, "desktop refuses external conflict");
        var loadedStore = (ProjectStore)Field(window, "_store")!;
        foreach (var target in new[] { loadedStore.FilePath, loadedStore.FilePath + ".bak", Path.Combine(dir,"assistant-chat.json"), Path.Combine(dir,"backups","protected.json") })
        {
            try { loadedStore.ExportText(target); throw new Exception("Expected protected export rejection"); }
            catch(IOException) { Check(true, "post-conflict export refuses " + Path.GetFileName(target)); }
        }
        loadedStore.ExportText(Path.Combine(dir, "safe-export.txt"));
        Check(File.ReadAllText(Path.Combine(dir, "safe-export.txt")).Contains("unsaved chapter text"), "post-conflict export includes unsaved desktop text");
        Check(File.ReadAllText(store.FilePath) == external, "post-conflict export preserves external manuscript");
        var project = (NovelProject)Field(window, "_project")!;
        Invoke(window, "SelectEntry", project.Entries[1]);
        foreach (var name in new[] { "TitleBox", "GroupBox", "TagsBox", "SummaryBox", "ContentBox" })
            Check(!((TextBox)window.FindName(name)).CanUndo, "entry switch clears " + name + " undo");
        File.WriteAllText(store.FilePath, "{broken");
        Check(new ProjectStore(dir).RestorePrevious().Title == "雾港来信 · 示例", "corrupt main recovers valid previous version");
        Check(Directory.GetFiles(Path.Combine(dir,"backups"), "*.json").Any(x=>File.ReadAllText(x)=="{broken"), "corrupt original retained in timestamp backup");
        BookshelfNavigationChecks.Run(Check);
        StatisticsIntegrationChecks.Run(Check);
        StreamingChecks.Run(Check);
        app.Shutdown();
        Console.WriteLine($"{_checks}/{_checks} passed");
    }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); _checks++; Console.WriteLine("PASS " + name); }
    private static object? Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
    private static object? Invoke(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o,args);
}
