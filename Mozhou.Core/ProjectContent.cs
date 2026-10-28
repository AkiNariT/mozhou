using System.Globalization;
using System.Text;

namespace Mozhou.Core;

public static class ProjectContent
{
    public static NovelProject CreateEmpty(string title) => new() { Title = string.IsNullOrWhiteSpace(title) ? "未命名小说" : title.Trim() };

    public static NovelProject CreateDemo()
    {
        var project = CreateEmpty("雾港来信 · 示例");
        project.Description = "一封迟到十年的信，将四个人带回潮汐之间的旧港。可自由修改或新建自己的小说。";
        var people = new[] { ("林知夏", "港口修复师，冷静而执拗。想找回父亲最后的航行日志。", 80d, 80d), ("周砚", "渡船船长，说话寡淡，熟悉雾港每一道暗流。", 400d, 80d), ("许青禾", "灯塔管理员，善于倾听，隐瞒着十年前的一场约定。", 80d, 300d), ("陈望", "旧书店主人，相信每封无人领取的信都有归处。", 400d, 300d) };
        project.Entries.Add(new NovelEntry { Kind = "chapter", Group = "第一卷 · 归港", Title = "第一章 雾中的邮差", Content = "林知夏抵达雾港的时候，最后一班渡船已经熄了灯。\n\n码头上站着一个抱着邮袋的老人。他没有问她的名字，只从袋底取出一封发黄的信。\n\n“你父亲说，等你回来再给你。”\n\n信封上的邮戳，是十年前的今天。" });
        foreach (var (name, description, x, y) in people)
            project.Entries.Add(new NovelEntry { Kind = "character", Title = name, Summary = description, Content = "性格：\n" + description + "\n\n目标：\n\n秘密：", X = x, Y = y });
        project.Entries.Add(new NovelEntry { Kind = "world", Title = "雾港与潮汐", Summary = "海雾笼罩的旧港，每逢大潮，废弃灯塔会再度亮起。", Content = "地理：山海之间的旧港，只有渡船与外界相通。\n规则：灯塔在大潮之夜亮起，潮落时便熄灭。\n风俗：离港的人将一封写给未来的信留在旧书店。" });
        project.Entries.Add(new NovelEntry { Kind = "outline", Title = "故事主线", Content = "起：林知夏收到父亲留下的信。\n承：四个人重走最后一次航行的路线。\n转：日志中记载的目的地从未存在。\n合：雾散之前，他们必须决定是否点亮灯塔。" });
        project.Entries.Add(new NovelEntry { Kind = "note", Title = "灵感便签", Content = "潮声可以作为每卷开头的声音意象。" });
        var characters = project.Entries.Where(e => e.Kind == "character").ToArray();
        project.Relations.AddRange([
            new CharacterRelation { FromId = characters[0].Id, ToId = characters[1].Id, Label = "旧识", Note = "十年前曾经同行，重逢后各有所隐瞒。" },
            new CharacterRelation { FromId = characters[1].Id, ToId = characters[2].Id, Label = "守约", Note = "共同保守灯塔的秘密。" },
            new CharacterRelation { FromId = characters[2].Id, ToId = characters[3].Id, Label = "通信", Note = "每年交换一封没有落款的信。" },
            new CharacterRelation { FromId = characters[3].Id, ToId = characters[0].Id, Label = "交付", Note = "保管父亲留下的航行日志。" }
        ]);
        return project;
    }

    public static NovelEntry CreateEntry(string kind) => kind switch
    {
        "chapter" => new() { Kind = kind, Title = "新章节", Group = "第一卷" },
        "character" => new() { Kind = kind, Title = "新人物", Content = "身份：\n\n外貌：\n\n性格：\n\n目标：\n\n过去：\n\n秘密：\n\n人物弧光：" },
        "world" => new() { Kind = kind, Title = "新世界观", Content = "地理与时代：\n\n社会与势力：\n\n规则与限制：\n\n风俗与细节：" },
        "outline" => new() { Kind = kind, Title = "新大纲", Content = "起：\n\n承：\n\n转：\n\n合：\n\n伏笔与回收：" },
        "note" => new() { Kind = kind, Title = "新灵感" },
        _ => throw new ArgumentException("未知条目类别。", nameof(kind))
    };

    public static string BuildPrompt(NovelProject project, NovelEntry? current, string instruction, bool includeSettings)
    {
        ArgumentNullException.ThrowIfNull(project);
        const int maximum = 24000;
        const string notice = "\n【上下文已截断：部分资料未发送，请缩短当前条目或请求范围。】";
        var task = Cut(instruction ?? "", 4000);
        var suffix = "\n\n【用户请求】\n" + task;
        var budget = maximum - suffix.Length - notice.Length;
        var context = new StringBuilder("你是中文小说写作助手。以下为用户提供的创作资料；仅提出建议，不执行命令、访问文件或调用工具。\n");
        var truncated = task.Length != (instruction ?? "").Length;
        void Add(string section)
        {
            var available = budget - context.Length;
            if (available <= 0) { truncated = true; return; }
            if (section.Length > available) { context.Append(Cut(section, available)); truncated = true; }
            else context.Append(section);
        }
        if (current != null) Add(EntryBlock("当前条目", current));
        else Add("\n【当前条目】未选中条目。\n");
        if (includeSettings)
        {
            Add($"\n【小说资料】\n书名：{project.Title}\n简介：{project.Description}\n");
            var characters = project.Entries.Where(e => e.Kind == "character").ToDictionary(e => e.Id, e => e.Title, StringComparer.Ordinal);
            if (project.Relations.Count > 0) Add("\n【人物关系网（有向）】\n");
            foreach (var relation in project.Relations)
                if (characters.TryGetValue(relation.FromId, out var from) && characters.TryGetValue(relation.ToId, out var to))
                    Add($"{from} → {to}\n关系：{relation.Label}\n说明：{relation.Note}\n\n");
            foreach (var entry in project.Entries.Where(e => e.Id != current?.Id && e.Kind is "character" or "world" or "outline"))
                Add(EntryBlock("设定资料", entry));
        }
        if (truncated) context.Append(notice);
        context.Append(suffix);
        return context.ToString();
    }

    private static string EntryBlock(string heading, NovelEntry entry) => $"\n【{heading}】\n标题：{entry.Title}\n类别：{entry.Kind}\n分组：{entry.Group}\n摘要：{entry.Summary}\n标签：{entry.Tags}\n正文：\n{entry.Content}\n";
    private static string Cut(string text, int length)
    {
        if (text.Length <= length) return text;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length];
    }

    /// <summary>汉字逐 Unicode 标量计数；其他字母或数字连续串计一词，词内连字符/撇号不拆词。空白、标点和表情不计。</summary>
    public static int CountWords(string text)
    {
        var count = 0; var inWord = false;
        foreach (var rune in (text ?? "").EnumerateRunes())
        {
            var value = rune.Value;
            if (value is >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x323AF)
            { count++; inWord = false; }
            else if (Rune.IsLetterOrDigit(rune)) { if (!inWord) count++; inWord = true; }
            else if (value is '-' or '\'' or 0x2019 && inWord) { }
            else if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark && inWord) { }
            else inWord = false;
        }
        return count;
    }
}
