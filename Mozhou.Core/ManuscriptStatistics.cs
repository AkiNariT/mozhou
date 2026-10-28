using System.Runtime.CompilerServices;

namespace Mozhou.Core;

public readonly record struct EntryStatistics(int WordCount, int ParagraphCount);

/// <summary>UI-thread statistics. Unchanged text is reused; closed novels are not retained by the cache.</summary>
public sealed class ManuscriptStatistics(Func<string, int>? wordCounter = null)
{
    private readonly Func<string, int> _countWords = wordCounter ?? ProjectContent.CountWords;
    private readonly ConditionalWeakTable<NovelEntry, CachedEntry> _entries = new();

    private sealed class CachedEntry
    {
        public string? Content;
        public EntryStatistics Statistics;
    }

    public EntryStatistics GetEntryStatistics(NovelEntry? entry)
    {
        if (entry == null) return default;
        var content = entry.Content ?? "";
        var cached = _entries.GetValue(entry, static _ => new CachedEntry());
        // Strings are immutable. Entry identity also keeps copied novels with identical IDs separate.
        if (!ReferenceEquals(content, cached.Content))
        {
            var statistics = new EntryStatistics(_countWords(content), CountParagraphs(content));
            cached.Content = content;
            cached.Statistics = statistics;
        }
        return cached.Statistics;
    }

    public int GetChapterWordCount(NovelProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var total = 0;
        foreach (var entry in project.Entries)
            if (entry.Kind == "chapter") total = checked(total + GetEntryStatistics(entry).WordCount);
        return total;
    }

    private static int CountParagraphs(string content)
    {
        var paragraphs = 0;
        var hasText = false;
        foreach (var character in content)
        {
            if (character == '\n') { if (hasText) paragraphs++; hasText = false; }
            else if (!char.IsWhiteSpace(character)) hasText = true;
        }
        return paragraphs + (hasText ? 1 : 0);
    }
}
