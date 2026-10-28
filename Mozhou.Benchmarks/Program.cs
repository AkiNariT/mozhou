using System.Diagnostics;
using System.Text.Json;
using Mozhou.Core;

const int chapters = 200, charactersPerChapter = 5000, edits = 100, rounds = 5;
var project = new NovelProject { Entries = Enumerable.Range(0, chapters).Select(i => new NovelEntry { Title = $"Chapter {i}", Content = new string('字', charactersPerChapter) }).ToList() };
var current = project.Entries[0]; var original = current.Content;
var cache = new ManuscriptStatistics();
for (var i = 0; i < 10; i++) { Before(); After(); }
var baseline = new List<double>(); var optimized = new List<double>();
long baselineBytes = 0, optimizedBytes = 0;
for (var round = 0; round < rounds; round++)
{
    var before = Measure(Before); var after = Measure(After);
    if (before.Checksum != after.Checksum) throw new InvalidOperationException("Benchmark changed statistics output.");
    baseline.Add(before.Ms); optimized.Add(after.Ms); baselineBytes += before.Bytes; optimizedBytes += after.Bytes;
}
baseline.Sort(); optimized.Sort();
Console.WriteLine(JsonSerializer.Serialize(new
{
    Scenario = "200 chapters, 1,000,000 Han characters; 100 edits to one chapter; warmed statistics only",
    Rounds = rounds, BaselineMedianMs = baseline[rounds / 2], OptimizedMedianMs = optimized[rounds / 2],
    BaselineAllocatedBytesPerRound = baselineBytes / rounds, OptimizedAllocatedBytesPerRound = optimizedBytes / rounds,
    Includes = "word and paragraph statistics only; excludes editor layout, disk save, and AI"
}, new JsonSerializerOptions { WriteIndented = true }));

long Before() => ProjectContent.CountWords(current.Content)
    + current.Content.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line))
    + project.Entries.Where(entry => entry.Kind == "chapter").Sum(entry => ProjectContent.CountWords(entry.Content));
long After()
{
    var entry = cache.GetEntryStatistics(current);
    return entry.WordCount + entry.ParagraphCount + cache.GetChapterWordCount(project);
}
(double Ms, long Bytes, long Checksum) Measure(Func<long> count)
{
    current.Content = original;
    count();
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var watch = Stopwatch.StartNew(); long checksum = 0;
    for (var i = 0; i < edits; i++) { current.Content = original + new string('改', i + 1); checksum += count(); }
    return (watch.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated, checksum);
}
