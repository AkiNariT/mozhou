namespace Mozhou.Core;

public sealed class ProjectImageStore(string folder)
{
    private const string ImagePrefix = "assets/images/";
    private readonly string? _documentPath;
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _materialized = new(StringComparer.OrdinalIgnoreCase);
    private bool _ownsCache, _disposed;
    public const long MaximumImportBytes = 25L * 1024 * 1024;
    internal static string CacheRoot => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Mozhou", "ImageCache"));
    public string Folder { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    private string AssetsFolder => Path.Combine(Folder, "assets");
    private string ImagesFolder => Path.Combine(AssetsFolder, "images");

    internal ProjectImageStore(string folder, string documentPath) : this(folder) => _documentPath = documentPath;

    public EntryImage Import(string sourcePath)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var source = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (!IsSupportedExtension(extension)) throw new InvalidDataException("仅支持 PNG、JPEG、BMP 和 GIF 图片。");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        if (input.Length > MaximumImportBytes) throw new InvalidDataException("图片超过 25 MiB，请缩小后重试。");
        Span<byte> header = stackalloc byte[8];
        var read = input.ReadAtLeast(header, header.Length, false);
        if (!HasSignature(extension, header[..read])) throw new InvalidDataException("图片内容与文件格式不符，未导入。");
        input.Position = 0;
        var image = new EntryImage { FileName = Guid.NewGuid().ToString("N") + extension, Title = Path.GetFileNameWithoutExtension(source) };
        if (_documentPath == null)
        {
            EnsureSafeDirectories(true);
            CopyNewFile(input, Path.Combine(ImagesFolder, image.FileName));
        }
        else
        {
            using var memory = new MemoryStream(checked((int)input.Length)); input.CopyTo(memory);
            var bytes = memory.ToArray();
            lock (_gate)
            {
                ThrowIfDisposed(); EnsureSafeDirectories(true);
                using var content = new MemoryStream(bytes, false);
                CopyNewFile(content, Path.Combine(ImagesFolder, image.FileName));
                _pending.Add(ImagePrefix + image.FileName, bytes); _materialized.Add(image.FileName);
            }
        }
        return image;
    }

    public string ResolvePath(EntryImage image)
    {
        ThrowIfDisposed();
        ValidateDescriptor(image);
        if (_documentPath != null) return ResolveDocumentPath(image);
        EnsureSafeDirectories(false);
        var path = Path.Combine(ImagesFolder, image.FileName);
        RejectLink(path);
        if (!File.Exists(path)) throw new FileNotFoundException("参考图片文件缺失：" + image.FileName, path);
        return path;
    }

    public IReadOnlyList<string> EnumerateFiles()
    {
        ThrowIfDisposed();
        if (_documentPath != null)
            return DocumentImageNames(true).Select(name => ResolvePath(new EntryImage { FileName = name[ImagePrefix.Length..] })).ToArray();
        EnsureSafeDirectories(false);
        if (!Directory.Exists(ImagesFolder)) return [];
        var files = new List<string>();
        foreach (var path in Directory.EnumerateFiles(ImagesFolder))
        {
            if (!IsManagedFileName(Path.GetFileName(path))) continue;
            RejectLink(path); files.Add(path);
        }
        return files.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    public void ValidateReferences(NovelProject project)
        => ValidateReferences(project, true);

    internal void ValidateReferences(NovelProject project, bool includePending)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(project);
        if (project.Entries == null) throw new InvalidDataException("小说条目列表无效。");
        // Document validation looks at logical names only; an ordinary text save never reads image BLOBs.
        var names = _documentPath == null ? null : DocumentImageNames(includePending).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in project.Entries)
        {
            if (entry?.Images == null) throw new InvalidDataException("条目的参考图片列表无效。");
            foreach (var image in entry.Images)
            {
                ValidateDescriptor(image);
                if (!ids.Add(image.Id)) throw new InvalidDataException("参考图片编号重复。");
                if (names == null) ResolvePath(image);
                else if (!names.Contains(ImagePrefix + image.FileName)) throw new FileNotFoundException("小说文档缺少参考图片：" + image.FileName);
            }
        }
    }

    // Copy every managed image, including images removed from the current model but used by history.
    // The caller validates references in its in-memory project before copying, then saves the new JSON.
    public void CopyTo(string destinationFolder)
    {
        ThrowIfDisposed();
        var destination = new ProjectImageStore(destinationFolder);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Folder, destination.Folder, comparison) ||
            string.Equals(AssetsFolder, destination.Folder, comparison) ||
            destination.Folder.StartsWith(AssetsFolder + Path.DirectorySeparatorChar, comparison))
            throw new IOException("图片副本必须保存到另一小说目录。");
        var files = EnumerateFiles();
        if (files.Count == 0) return;
        destination.EnsureSafeDirectories(true);
        foreach (var source in files)
        {
            RejectLink(source);
            var target = Path.Combine(destination.ImagesFolder, Path.GetFileName(source));
            RejectLink(target);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("目标目录已存在同名图片，未覆盖：" + Path.GetFileName(target));
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            CopyNewFile(input, target);
        }
    }

    internal IReadOnlyDictionary<string, byte[]> PendingFiles()
    {
        lock (_gate) { ThrowIfDisposed(); return new Dictionary<string, byte[]>(_pending, StringComparer.OrdinalIgnoreCase); }
    }

    internal void AcceptPending(IReadOnlyDictionary<string, byte[]> committed)
    {
        lock (_gate)
            foreach (var (name, bytes) in committed)
                if (_pending.TryGetValue(name, out var current) && ReferenceEquals(bytes, current)) _pending.Remove(name);
    }

    private IReadOnlyList<string> DocumentImageNames(bool includePending)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(_documentPath))
            foreach (var name in new SingleFileDocument(_documentPath!).ListFiles(ImagePrefix))
            {
                if (!IsManagedFileName(name[ImagePrefix.Length..])) throw new InvalidDataException("小说文档包含非法图片文件名。");
                names.Add(name);
            }
        if (includePending) lock (_gate) names.UnionWith(_pending.Keys);
        return names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private string ResolveDocumentPath(EntryImage image)
    {
        lock (_gate)
        {
            ThrowIfDisposed(); EnsureSafeDirectories(false);
            var path = Path.Combine(ImagesFolder, image.FileName); RejectLink(path);
            if (_materialized.Contains(image.FileName) && File.Exists(path)) return path;
            var logical = ImagePrefix + image.FileName;
            if (!_pending.TryGetValue(logical, out var bytes))
            {
                var storedName = DocumentImageNames(false).FirstOrDefault(name => string.Equals(name, logical, StringComparison.OrdinalIgnoreCase));
                bytes = storedName == null ? null : new SingleFileDocument(_documentPath!).ReadFile(storedName);
            }
            if (bytes == null) throw new FileNotFoundException("小说文档缺少参考图片：" + image.FileName);
            if (bytes.LongLength > MaximumImportBytes) throw new InvalidDataException("小说文档中的参考图片超过 25 MiB。");
            EnsureSafeDirectories(true);
            using var input = new MemoryStream(bytes, false); CopyNewFile(input, path);
            _materialized.Add(image.FileName); return path;
        }
    }

    internal void DisposeDocumentCache()
    {
        lock (_gate)
        {
            if (_documentPath == null || _disposed) return;
            _disposed = true; _pending.Clear(); _materialized.Clear();
            if (!_ownsCache) return;
            try
            {
                EnsureOwnedCachePath(); RejectLink(CacheRoot); RejectLink(Folder);
                if (Directory.Exists(Folder)) DeleteCacheTree(Folder, Folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
    }

    private static void DeleteCacheTree(string directory, string ownedRoot)
    {
        var full = Path.GetFullPath(directory);
        if (!string.Equals(full, ownedRoot, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(ownedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("图片缓存路径超出本次会话目录。");
        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) == 0) File.Delete(entry);
            else if ((attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(entry);
            else DeleteCacheTree(entry, ownedRoot);
        }
        Directory.Delete(full);
    }

    private void EnsureOwnedCachePath()
    {
        if (!string.Equals(Path.GetDirectoryName(Folder), CacheRoot, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(Folder), "N", out _))
            throw new IOException("图片缓存目录无效。");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProjectImageStore));
    }

    internal static void ValidateDescriptor(EntryImage? image)
    {
        if (image == null || string.IsNullOrWhiteSpace(image.Id) || image.Title == null || !IsManagedFileName(image.FileName))
            throw new InvalidDataException("参考图片记录包含空字段或非法文件名。");
    }

    private static bool IsManagedFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Length < 36 || fileName[32] != '.') return false;
        return Guid.TryParseExact(fileName.AsSpan(0, 32), "N", out _) && IsSupportedExtension(fileName[32..].ToLowerInvariant());
    }

    private static bool IsSupportedExtension(string extension) => extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
    private static bool HasSignature(string extension, ReadOnlySpan<byte> header) => extension switch
    {
        ".png" => header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".jpg" or ".jpeg" => header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        ".bmp" => header.StartsWith("BM"u8),
        ".gif" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
        _ => false
    };

    private void EnsureSafeDirectories(bool create)
    {
        if (_documentPath != null && create && !_ownsCache)
        {
            EnsureOwnedCachePath(); RejectLink(Path.GetDirectoryName(CacheRoot)!); RejectLink(CacheRoot);
            if (Directory.Exists(Folder) || File.Exists(Folder)) throw new IOException("本次图片缓存目录已被其他文件占用。");
            Directory.CreateDirectory(Folder); _ownsCache = true;
        }
        foreach (var directory in new[] { Folder, AssetsFolder, ImagesFolder })
        {
            RejectLink(directory);
            if (File.Exists(directory)) throw new IOException("图片存储目录被同名文件占用：" + directory);
            if (create) { Directory.CreateDirectory(directory); RejectLink(directory); }
        }
    }

    private static void RejectLink(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("图片存储路径不能是符号链接或目录联接：" + path);
    }

    private static void CopyNewFile(Stream input, string destination)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                input.CopyTo(output, 64 * 1024); output.Flush(true);
            }
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
