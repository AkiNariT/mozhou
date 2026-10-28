using System.Runtime.CompilerServices;
using Mozhou.Core;

internal static class StatisticsChecks
{
    internal static readonly (string Name, Action<string> Test)[] Cases =
    [
        ("Statistics preserve Unicode counts and exclude non-chapters", _ =>
        {
            var chapter = new NovelEntry { Content = "中文，hello world！𠀀 test-case 123\n\n雪\n \t\r\n" };
            var project = new NovelProject { Entries = [chapter, new NovelEntry { Kind = "note", Content = "不计入正文" }] };
            var statistics = new ManuscriptStatistics();
            Equal(8, statistics.GetChapterWordCount(project));
            Equal(new EntryStatistics(8, 2), statistics.GetEntryStatistics(chapter));
            Equal(default(EntryStatistics), statistics.GetEntryStatistics(null));
        }),
        ("Unchanged chapters and selected entry reuse counts", _ =>
        {
            var calls = 0;
            var statistics = new ManuscriptStatistics(text => { calls++; return ProjectContent.CountWords(text); });
            var first = new NovelEntry { Content = "第一章" };
            var project = new NovelProject { Entries = [first, new NovelEntry { Content = "第二章" }] };
            Equal(6, statistics.GetChapterWordCount(project));
            Equal(3, statistics.GetEntryStatistics(first).WordCount);
            Equal(6, statistics.GetChapterWordCount(project));
            Equal(2, calls);
        }),
        ("Editing one chapter recounts only changed content", _ =>
        {
            var calls = 0;
            var statistics = new ManuscriptStatistics(text => { calls++; return ProjectContent.CountWords(text); });
            var first = new NovelEntry { Content = "甲\n乙" };
            var project = new NovelProject { Entries = [first, new NovelEntry { Content = "丙丁" }] };
            Equal(4, statistics.GetChapterWordCount(project));
            first.Title = "只改标题"; first.Summary = "只改摘要";
            Equal(4, statistics.GetChapterWordCount(project)); Equal(2, calls);
            first.Content = "甲\n乙\n丙";
            Equal(5, statistics.GetChapterWordCount(project));
            Equal(new EntryStatistics(3, 3), statistics.GetEntryStatistics(first)); Equal(3, calls);
        }),
        ("Add remove reclassify and same-ID replacement update totals", _ =>
        {
            var statistics = new ManuscriptStatistics();
            var first = new NovelEntry { Id = "same", Content = "甲乙" };
            var second = new NovelEntry { Content = "丙" };
            var project = new NovelProject { Entries = [first] };
            Equal(2, statistics.GetChapterWordCount(project));
            project.Entries.Add(second); Equal(3, statistics.GetChapterWordCount(project));
            first.Kind = "note"; Equal(1, statistics.GetChapterWordCount(project));
            first.Kind = "chapter"; project.Entries.Remove(second); Equal(2, statistics.GetChapterWordCount(project));
            project.Entries[0] = new NovelEntry { Id = "same", Content = "全新的小说" };
            Equal(5, statistics.GetChapterWordCount(project));
        }),
        ("Statistics do not mix novels sharing project and entry IDs", _ =>
        {
            var statistics = new ManuscriptStatistics();
            var first = new NovelProject { Id = "copy", Entries = [new NovelEntry { Id = "entry", Content = "甲" }] };
            var second = new NovelProject { Id = "copy", Entries = [new NovelEntry { Id = "entry", Content = "乙丙丁" }] };
            Equal(1, statistics.GetChapterWordCount(first)); Equal(3, statistics.GetChapterWordCount(second));
            Equal(1, statistics.GetChapterWordCount(first));
        }),
        ("Paragraph scan matches previous whitespace and newline semantics", _ =>
        {
            var statistics = new ManuscriptStatistics();
            foreach (var text in new[] { "", "\n\n", " \t\r\n\u3000\n\u00a0", "甲\r\n乙\n", "hello\rworld", "𠀀\n🌊\n", "a\u2028b\n\n c" })
            {
                var expected = text.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));
                Equal(expected, statistics.GetEntryStatistics(new NovelEntry { Content = text }).ParagraphCount);
            }
        }),
        ("Cached statistics release novels no longer in use", _ =>
        {
            var statistics = new ManuscriptStatistics();
            var weak = SeedTemporaryEntry(statistics);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            if (weak.TryGetTarget(out var retained)) throw new Exception("Statistics retained a closed novel entry: " + retained.Id);
            GC.KeepAlive(statistics);
        })
    ];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<NovelEntry> SeedTemporaryEntry(ManuscriptStatistics statistics)
    {
        var entry = new NovelEntry { Content = new string('字', 10000) };
        statistics.GetEntryStatistics(entry);
        return new WeakReference<NovelEntry>(entry);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
    }
}
