namespace Mozhou.Core;

public sealed class ShelfBook
{
    public string Folder { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int WordCount { get; set; }
    public int ChapterCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset LastOpenedAt { get; set; }
    public string? Problem { get; set; }
    public bool IsAvailable { get; set; }
}
