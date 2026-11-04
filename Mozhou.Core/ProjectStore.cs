using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mozhou.Core;

public sealed class ConflictException(string message) : IOException(message);

public sealed class ProjectStore : IDisposable
{
    private const string ProjectName = "project.mozhou.json", PreviousName = "project.mozhou.json.bak", ChatName = "assistant-chat.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private byte[]? _baseline;
    private bool _hasBaseline;
    private NovelProject? _project;
    private byte[]? _chatBaseline;
    private bool _disposed;
    public string Location { get; }
    public bool IsDocument { get; }
    public string Folder { get; }
    public string FilePath => IsDocument ? Location : Path.Combine(Folder, "project.mozhou.json");
    public ProjectImageStore Images { get; }

    public ProjectStore(string location)
    {
        Location = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
        IsDocument = !Directory.Exists(Location) && (File.Exists(Location) || string.Equals(Path.GetExtension(Location), ".mozhou", StringComparison.OrdinalIgnoreCase));
        Folder = IsDocument ? Path.Combine(ProjectImageStore.CacheRoot, Guid.NewGuid().ToString("N")) : Location;
        Images = IsDocument ? new ProjectImageStore(Folder, Location) : new ProjectImageStore(Folder);
    }

    public byte[]? ReadChat()
    {
        ThrowIfDisposed();
        var path = Path.Combine(Folder, ChatName);
        var bytes = IsDocument ? new SingleFileDocument(Location).ReadFile(ChatName) : File.Exists(path) ? File.ReadAllBytes(path) : null;
        _chatBaseline = bytes?.ToArray(); return bytes;
    }

    public void SaveChat(byte[] bytes)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(bytes);
        if (IsDocument)
            new SingleFileDocument(Location).WriteFiles(new Dictionary<string, byte[]?> { [ChatName] = bytes }, expectedChat: _chatBaseline, checkChat: true);
        else
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, ChatName); var temporary = WriteTemporary(path, bytes);
            try { File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        _chatBaseline = bytes.ToArray();
    }

    public void SaveDocumentCopy(string destination, NovelProject current, byte[]? chatOverride = null)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(current);
        if (IsDocument && _hasBaseline && !File.Exists(Location))
            throw new FileNotFoundException("源文档已移动或删除，无法生成完整副本。可先导出正文保留当前内容。", Location);
        var target = ValidateDestination(destination);
        if (!string.Equals(Path.GetExtension(target), ".mozhou", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("单文件小说副本须使用 .mozhou 扩展名。");
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("目标已存在，请选择新的小说文件名。");
        var copy = JsonSerializer.Deserialize<NovelProject>(JsonSerializer.SerializeToUtf8Bytes(current, JsonOptions), JsonOptions)!;
        Validate(copy, upgradeFormat: true); Images.ValidateReferences(copy);
        copy.UpdatedAt = DateTimeOffset.Now;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(copy, JsonOptions);
        var pending = Images.PendingFiles();
        if (!IsDocument)
        {
            using var gate = Directory.Exists(Folder) ? AcquireWriteLock() : null;
            SingleFileDocument.CreateNew(target, LegacyCopyFiles(bytes, chatOverride));
            return;
        }
        if (!File.Exists(Location))
        {
            var files = pending.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            files[ProjectName] = bytes;
            if (chatOverride != null) files[ChatName] = chatOverride;
            SingleFileDocument.CreateNew(target, files); return;
        }

        // Apply draft overrides to a private complete snapshot before publishing the new document.
        var staging = target + "." + Guid.NewGuid().ToString("N") + ".tmp.mozhou";
        try
        {
            new SingleFileDocument(Location).CopyTo(staging);
            var staged = new SingleFileDocument(staging);
            var changes = pending.ToDictionary(pair => pair.Key, pair => (byte[]?)pair.Value, StringComparer.OrdinalIgnoreCase);
            changes[ProjectName] = bytes;
            if (chatOverride != null) changes[ChatName] = chatOverride;
            staged.WriteFiles(changes);
            // Check the final staged snapshot, including pending images, before it becomes visible.
            using (var verify = new ProjectStore(staging)) verify.Images.ValidateReferences(verify.Load());
            File.Move(staging, target, false);
        }
        finally { DeletePrivateDocument(staging); }
    }

    private IEnumerable<KeyValuePair<string, byte[]>> LegacyCopyFiles(byte[] current, byte[]? chatOverride)
    {
        yield return new(ProjectName, current);
        var previous = Path.Combine(Folder, PreviousName);
        if (File.Exists(previous))
        {
            if (IsLink(previous)) throw new IOException("上一版本不能是符号链接。");
            yield return new(PreviousName, File.ReadAllBytes(previous));
        }
        var backups = Path.Combine(Folder, "backups");
        if (Directory.Exists(backups))
        {
            if (IsLink(backups)) throw new IOException("备份目录不能是符号链接。");
            foreach (var path in Directory.GetFiles(backups, "*.json").OrderBy(path => path, StringComparer.Ordinal))
            {
                if (IsLink(path)) throw new IOException("历史备份不能是符号链接。");
                yield return new("backups/" + Path.GetFileName(path), File.ReadAllBytes(path));
            }
        }
        var chat = Path.Combine(Folder, ChatName);
        if (chatOverride != null) yield return new(ChatName, chatOverride);
        else if (File.Exists(chat))
        {
            if (IsLink(chat)) throw new IOException("对话记录不能是符号链接。");
            yield return new(ChatName, File.ReadAllBytes(chat));
        }
        foreach (var image in Images.EnumerateFiles()) yield return new("assets/images/" + Path.GetFileName(image), File.ReadAllBytes(image));
    }

    private static void DeletePrivateDocument(string staging)
    {
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            if (File.Exists(staging + suffix)) File.Delete(staging + suffix);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsDocument) Images.DisposeDocumentCache();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProjectStore));
    }

    public NovelProject Load()
    {
        ThrowIfDisposed();
        var bytes = ReadCurrent() ?? throw new FileNotFoundException("小说正文不存在。", FilePath);
        var project = Parse(bytes);
        _baseline = bytes; _hasBaseline = true; _project = project;
        return project;
    }

    public void Save(NovelProject project)
    {
        ThrowIfDisposed(); Validate(project, upgradeFormat: true);
        if (IsDocument) { SaveDocument(project); return; }
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
        ThrowIfDisposed();
        if (IsDocument) return RestoreDocumentPrevious();
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

    private void SaveDocument(NovelProject project)
    {
        var previous = ReadCurrent();
        EnsureUnchanged(previous);
        if (!_hasBaseline && File.Exists(Location))
            throw new ConflictException("现有小说文档尚未成功读取，未覆盖原文件。");
        project.UpdatedAt = DateTimeOffset.Now;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, JsonOptions);
        var pending = Images.PendingFiles();
        if (!_hasBaseline)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Location)!);
            var files = pending.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            files[ProjectName] = bytes;
            SingleFileDocument.CreateNew(Location, files);
        }
        else
        {
            var document = new SingleFileDocument(Location);
            var changes = pending.ToDictionary(pair => pair.Key, pair => (byte[]?)pair.Value, StringComparer.OrdinalIgnoreCase);
            changes[ProjectName] = bytes;
            if (previous != null)
            {
                changes[PreviousName] = previous;
                AddDocumentHistory(document, changes, previous);
            }
            document.WriteFiles(changes, expectedProject: _baseline, checkProject: true);
        }
        // Retain pending imports on every failure so a complete rescue copy remains possible.
        _baseline = bytes; _hasBaseline = true; _project = project;
        Images.AcceptPending(pending);
    }

    private NovelProject RestoreDocumentPrevious()
    {
        var document = new SingleFileDocument(Location);
        var current = document.ReadFile(ProjectName);
        var backup = document.ReadFile(PreviousName) ?? throw new FileNotFoundException("小说文档没有上一版本。", Location);
        var restored = Parse(backup);
        var changes = new Dictionary<string, byte[]?> { [ProjectName] = backup };
        if (current != null) AddDocumentHistory(document, changes, current);
        document.WriteFiles(changes, expectedProject: current, checkProject: true);
        _baseline = backup; _hasBaseline = true; _project = restored;
        return restored;
    }

    private static void AddDocumentHistory(SingleFileDocument document, IDictionary<string, byte[]?> changes, byte[] previous)
    {
        var name = $"backups/project-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.json";
        changes[name] = previous;
        foreach (var old in document.ListFiles("backups/").Append(name).OrderByDescending(value => value, StringComparer.Ordinal).Skip(30))
            changes[old] = null;
    }

    public void CreateBackup(string destinationZip)
    {
        ThrowIfDisposed();
        if (IsDocument)
        {
            var target = ValidateDestination(destinationZip);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("备份目标已存在，请选择新的文件名。");
            var temporaryZip = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                new SingleFileDocument(Location).ExportZip(temporaryZip);
                // Validate the exact exported snapshot, even if another window saved while it was made.
                using (var zip = ZipFile.OpenRead(temporaryZip))
                {
                    var main = zip.GetEntry(ProjectName) ?? throw new FileNotFoundException("小说正文不存在。", Location);
                    using var input = main.Open(); using var memory = new MemoryStream(); input.CopyTo(memory);
                    var savedProject = Parse(memory.ToArray());
                    var files = zip.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var image in savedProject.Entries.SelectMany(entry => entry.Images))
                        if (!files.Contains("assets/images/" + image.FileName)) throw new FileNotFoundException("小说文档缺少参考图片：" + image.FileName);
                }
                File.Move(temporaryZip, target, false);
            }
            finally { if (File.Exists(temporaryZip)) File.Delete(temporaryZip); }
            return;
        }
        using var gate = AcquireWriteLock();
        var current = File.ReadAllBytes(FilePath);
        var project = Parse(current);
        var images = new ProjectImageStore(Folder);
        images.ValidateReferences(project);
        var imageFiles = images.EnumerateFiles();
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
                    foreach (var image in imageFiles) AddZipFile(zip, image, "assets/images/" + Path.GetFileName(image));
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
        ThrowIfDisposed();
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
        if (IsDocument && (string.Equals(full, Location, StringComparison.OrdinalIgnoreCase) ||
            new[] { "-journal", "-wal", "-shm" }.Any(suffix => string.Equals(full, Location + suffix, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(full, Folder, StringComparison.OrdinalIgnoreCase) || full.StartsWith(Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("导出目标不能覆盖小说文档、数据库日志或图片缓存。");
        var backupRoot = Path.Combine(Folder, "backups") + Path.DirectorySeparatorChar;
        var assetsRoot = Path.Combine(Folder, "assets");
        if (string.Equals(full, FilePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, FilePath + ".bak", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, Path.Combine(Folder, "assistant-chat.json"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, Path.Combine(Folder, "project.mozhou.lock"), StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, assetsRoot, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("导出目标不能覆盖小说数据或内部备份。");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    private FileStream AcquireWriteLock()
    {
        try { return new FileStream(Path.Combine(Folder, "project.mozhou.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new ConflictException("另一个墨舟窗口正在保存，请稍后重试。" + ex.Message); }
    }

    private byte[]? ReadCurrent() => !File.Exists(FilePath) ? null : IsDocument ? new SingleFileDocument(Location).ReadFile(ProjectName) : File.ReadAllBytes(FilePath);
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

    private static void Validate(NovelProject project, bool upgradeFormat = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.FormatVersion is < 1 or > 3) throw new InvalidDataException("此小说文件版本不受支持，请勿覆盖原文件。");
        if (string.IsNullOrWhiteSpace(project.Id) || project.Title == null || project.Description == null || project.Entries == null || project.Relations == null)
            throw new InvalidDataException("小说文件缺少必要字段。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var imageIds = new HashSet<string>(StringComparer.Ordinal);
        var requiredFormat = 1;
        foreach (var entry in project.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id) || entry.Kind == null || entry.Title == null || entry.Group == null || entry.Summary == null || entry.Content == null || entry.Tags == null || entry.Color == null || entry.LinkedIds == null || entry.Images == null || !double.IsFinite(entry.X) || !double.IsFinite(entry.Y))
                throw new InvalidDataException("小说条目缺少字段、编号重复或坐标无效。");
            foreach (var image in entry.Images)
            {
                ProjectImageStore.ValidateDescriptor(image);
                var classified = image.Hairstyle.Length > 0 || image.Outfit.Length > 0 || image.LookName.Length > 0;
                requiredFormat = Math.Max(requiredFormat, classified ? 3 : 2);
                if (!imageIds.Add(image.Id)) throw new InvalidDataException("参考图片编号重复。");
            }
        }
        var relationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in project.Relations)
            if (edge == null || string.IsNullOrWhiteSpace(edge.Id) || !relationIds.Add(edge.Id) || !ids.Contains(edge.FromId) || !ids.Contains(edge.ToId) || edge.Label == null || edge.Note == null || edge.Color == null)
                throw new InvalidDataException("人物关系含无效条目或编号。");
        if (project.FormatVersion < requiredFormat)
        {
            if (!upgradeFormat) throw new InvalidDataException($"此稿件的参考图片数据需要格式版本 {requiredFormat}，原文件未修改。");
            project.FormatVersion = requiredFormat;
        }
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
