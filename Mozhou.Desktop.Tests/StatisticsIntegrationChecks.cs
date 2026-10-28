using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using Mozhou.Core;
using Mozhou.Desktop;

internal static class StatisticsIntegrationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var window = new MainWindow(null);
        var first = new NovelEntry { Id = "same", Content = "甲乙\n\nhello" };
        var second = new NovelEntry { Content = "丙丁" };
        var project = new NovelProject { Entries = [first, second] };
        try
        {
            Set(window, "_project", project);
            Invoke(window, "SelectEntry", first);
            CheckCount(window, project, first, check, "initial selection");
            ((TextBox)window.FindName("ContentBox")).Text = "甲乙丙\nhello world\n\n";
            CheckCount(window, project, first, check, "typing");
            ((TextBox)window.FindName("TitleBox")).Text = "修改标题";
            CheckCount(window, project, first, check, "title-only edit");
            second.Content += "外部采用的文本";
            Invoke(window, "UpdateCounts");
            CheckCount(window, project, first, check, "non-selected entry edit");
            project.Entries.Remove(second);
            Invoke(window, "UpdateCounts");
            CheckCount(window, project, first, check, "chapter removal");
            var copied = new NovelEntry { Id = "same", Content = "新书\n𠀀" };
            var next = new NovelProject { Id = project.Id, Entries = [copied] };
            Set(window, "_project", next); Invoke(window, "SelectEntry", copied);
            CheckCount(window, next, copied, check, "same-ID project switch");
        }
        finally
        {
            ((DispatcherTimer)Field("_saveTimer").GetValue(window)!).Stop();
            Set(window, "_dirty", false);
        }
    }

    private static void CheckCount(MainWindow window, NovelProject project, NovelEntry entry, Action<bool, string> check, string operation)
    {
        var expectedWords = project.Entries.Where(e => e.Kind == "chapter").Sum(e => ProjectContent.CountWords(e.Content));
        var expectedEntry = $"{ProjectContent.CountWords(entry.Content):N0} 字 · {entry.Content.Split('\n').Count(s => !string.IsNullOrWhiteSpace(s))} 段";
        check(((TextBlock)window.FindName("TotalWords")).Text == $"{expectedWords:N0}", operation + " updates total word display");
        check(((TextBlock)window.FindName("EntryWords")).Text == expectedEntry, operation + " updates selected entry statistics");
    }

    private static FieldInfo Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static void Set(MainWindow window, string name, object value) => Field(name).SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
}
