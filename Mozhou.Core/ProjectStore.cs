using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mozhou.Core;

public sealed class ConflictException(string message) : IOException(message);

public sealed class ProjectStore(string folder)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private byte[]? _baseline;
    private bool _hasBaseline;
    private NovelProject? _project;
    public string Folder { get; } = Path.GetFullPath(folder);
    public string FilePath => Path.Combine(Folder, "project.mozhou.json");

    public NovelProject Load()
    {
        var bytes = File.ReadAllBytes(FilePath);
        var project = Parse(bytes);
        _baseline = bytes; _hasBaseline = true; _project = project;
        return project;
    }

    public void Save(NovelProject project)
    {
        Validate(project);
        Directory.CreateDirectory(Folder);
        using var gate = AcquireWriteLock();
        var previous = ReadCurrent();
        EnsureUnchanged(previous);
        // _baseline 仅在成功加载、保存或恢复后更新；逐字节检查已确认旧稿与已验证的基线一致。
        project.UpdatedAt = DateTimeOffset.Now;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, JsonOptions);
        var temporary = WriteTemporary(FilePath, bytes);
        try
        {
            EnsureUnchanged(ReadCurrent());
            if (previous != null)
            {
                SaveTimestamp(previous);
                EnsureUnchanged(ReadCurrent());
                File.Replace(temporary, FilePath, FilePath + ".bak", true);
            }
            else File.Move(temporary, FilePath, false);
            _baseline = bytes; _hasBaseline = true; _project = project;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public NovelProject RestorePrevious()
    {
        Directory.CreateDirectory(Folder);
        using var gate = AcquireWriteLock();
        var backup = File.ReadAllBytes(FilePath + ".bak");
        var restored = Parse(backup); // 先验证备份，出错不动原文件。
        var current = ReadCurrent();
        var temporary = WriteTemporary(FilePath, backup);
        try
        {
            if (!Same(current, ReadCurrent())) throw new ConflictException("恢复期间文件被其他程序修改，请重新打开后重试。");
            if (current != null)
            {
                // 保留恢复前现场（包括损坏内容）；.bak 仍是已知可读的版本。
                SaveTimestamp(current);
                if (!Same(current, ReadCurrent())) throw new ConflictException("恢复期间文件被其他程序修改，请重新打开后重试。");
                File.Replace(temporary, FilePath, null, true);
            }
            else File.Move(temporary, FilePath, false);
            _baseline = backup; _hasBaseline = true; _project = restored;
            return restored;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void CreateBackup(string destinationZip)
    {
        var current = File.ReadAllBytes(FilePath);
        Parse(current);
        var destination = ValidateDestination(destinationZip);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    AddZipBytes(zip, "project.mozhou.json", current);
                    if (File.Exists(FilePath + ".bak")) AddZipFile(zip, FilePath + ".bak", "project.mozhou.json.bak");
                    var backups = Path.Combine(Folder, "backups");
                    if (Directory.Exists(backups) && !IsLink(backups))
                        foreach (var path in Directory.GetFiles(backups, "*.json").Where(p => !IsLink(p)).OrderBy(p => p, StringComparer.Ordinal))
                            AddZipFile(zip, path, "backups/" + Path.GetFileName(path));
                    // 助手聊天记录若存在，同属小说备份；不扫描小说目录外或凭据文件。
                    var chat = Path.Combine(Folder, "assistant-chat.json");
                    if (File.Exists(chat) && !IsLink(chat)) AddZipFile(zip, chat, "assistant-chat.json");
                }
                output.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void ExportMarkdown(string destination) => Export(destination, true);
    public void ExportText(string destination) => Export(destination, false);

    private void Export(string destination, bool markdown)
    {
        var project = _project ?? Load();
        var text = new StringBuilder().AppendLine(markdown ? "# " + project.Title : project.Title).AppendLine();
        string? lastGroup = null;
        foreach (var entry in project.Entries.Where(e => e.Kind == "chapter"))
        {
            if (entry.Group != lastGroup && !string.IsNullOrWhiteSpace(entry.Group))
                text.AppendLine(markdown ? "## " + entry.Group : entry.Group).AppendLine();
            lastGroup = entry.Group;
            text.AppendLine(markdown ? "### " + entry.Title : entry.Title).AppendLine().AppendLine(entry.Content).AppendLine();
        }
        var path = ValidateDestination(destination);
        var temporary = WriteTemporary(path, Encoding.UTF8.GetBytes(text.ToString()));
        try { File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string ValidateDestination(string destination)
    {
        var full = Path.GetFullPath(destination);
        var backupRoot = Path.Combine(Folder, "backups") + Path.DirectorySeparatorChar;
        if (string.Equals(full, FilePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, FilePath + ".bak", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, Path.Combine(Folder, "assistant-chat.json"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, Path.Combine(Folder, "project.mozhou.lock"), StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException("导出目标不能覆盖小说数据或内部备份。");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    private FileStream AcquireWriteLock()
    {
        try { return new FileStream(Path.Combine(Folder, "project.mozhou.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new ConflictException("另一个墨舟窗口正在保存，请稍后重试。" + ex.Message); }
    }

    private byte[]? ReadCurrent() => File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null;
    private static bool Same(byte[]? left, byte[]? right) => left == null ? right == null : right != null && left.AsSpan().SequenceEqual(right);
    private void EnsureUnchanged(byte[]? current)
    {
        if ((!_hasBaseline && current != null) || (_hasBaseline && !Same(_baseline, current)))
            throw new ConflictException("小说文件已被其他程序修改或删除。为保护两份内容，本次保存已停止，请备份当前文字并重新打开小说。");
    }

    private void SaveTimestamp(byte[] bytes)
    {
        var directory = Path.Combine(Folder, "backups");
        if (Directory.Exists(directory) && IsLink(directory)) throw new IOException("备份目录不能是符号链接。");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"project-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.json");
        var temporary = WriteTemporary(path, bytes);
        try { File.Move(temporary, path, false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        // 清理失败也发生在替换正文之前，避免“报错但正文已经写入”的含糊状态。
        TrimBackups();
    }

    private void TrimBackups()
    {
        var directory = Path.Combine(Folder, "backups");
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.GetFiles(directory, "project-*.json").OrderByDescending(p => p, StringComparer.Ordinal).Skip(30))
            File.Delete(file);
    }

    private static string WriteTemporary(string target, byte[] bytes)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            stream.Write(bytes); stream.Flush(true);
            return temporary;
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
    }

    private static NovelProject Parse(byte[] bytes)
    {
        ReadOnlyMemory<byte> content = bytes;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) content = content[3..];
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            new[] { "FormatVersion", "Id", "Title", "Entries", "Relations" }.Any(name => !document.RootElement.TryGetProperty(name, out _)))
            throw new InvalidDataException("小说文件缺少必要结构，未作任何修改。");
        var project = document.RootElement.Deserialize<NovelProject>(JsonOptions) ?? throw new InvalidDataException("小说文件内容为空。");
        Validate(project); return project;
    }

    private static void Validate(NovelProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.FormatVersion != 1) throw new InvalidDataException("此小说文件版本不受支持，请勿覆盖原文件。");
        if (string.IsNullOrWhiteSpace(project.Id) || project.Title == null || project.Description == null || project.Entries == null || project.Relations == null)
            throw new InvalidDataException("小说文件缺少必要字段。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in project.Entries)
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id) || entry.Kind == null || entry.Title == null || entry.Group == null || entry.Summary == null || entry.Content == null || entry.Tags == null || entry.Color == null || entry.LinkedIds == null || !double.IsFinite(entry.X) || !double.IsFinite(entry.Y))
                throw new InvalidDataException("小说条目缺少字段、编号重复或坐标无效。");
        var relationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in project.Relations)
            if (edge == null || string.IsNullOrWhiteSpace(edge.Id) || !relationIds.Add(edge.Id) || !ids.Contains(edge.FromId) || !ids.Contains(edge.ToId) || edge.Label == null || edge.Note == null || edge.Color == null)
                throw new InvalidDataException("人物关系含无效条目或编号。");
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static void AddZipFile(ZipArchive archive, string path, string name)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        using var output = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        input.CopyTo(output, 64 * 1024);
    }
    private static void AddZipBytes(ZipArchive archive, string name, byte[] bytes)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open(); stream.Write(bytes);
    }
}
