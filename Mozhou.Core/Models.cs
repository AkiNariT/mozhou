namespace Mozhou.Core;

public sealed class NovelProject
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未命名小说";
    public string Description { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<NovelEntry> Entries { get; set; } = [];
    public List<CharacterRelation> Relations { get; set; } = [];
}

public sealed class NovelEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "chapter";
    public string Title { get; set; } = "未命名";
    public string Group { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Content { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Color { get; set; } = "#5E8078";
    public double X { get; set; } = 80;
    public double Y { get; set; } = 80;
    public List<string> LinkedIds { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public override string ToString() => Title;
}

public sealed class CharacterRelation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
    public string Label { get; set; } = "相识";
    public string Note { get; set; } = "";
    public string Color { get; set; } = "#89958A";
}
