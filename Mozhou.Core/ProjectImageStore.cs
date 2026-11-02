namespace Mozhou.Core;

public sealed class ProjectImageStore(string folder)
{
    public const long MaximumImportBytes = 25L * 1024 * 1024;
    public string Folder { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    private string AssetsFolder => Path.Combine(Folder, "assets");
    private string ImagesFolder => Path.Combine(AssetsFolder, "images");

    public EntryImage Import(string sourcePath)
    {
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
        EnsureSafeDirectories(true);
        var image = new EntryImage { FileName = Guid.NewGuid().ToString("N") + extension, Title = Path.GetFileNameWithoutExtension(source) };
        CopyNewFile(input, Path.Combine(ImagesFolder, image.FileName));
        return image;
    }

    public string ResolvePath(EntryImage image)
    {
        ValidateDescriptor(image);
        EnsureSafeDirectories(false);
        var path = Path.Combine(ImagesFolder, image.FileName);
        RejectLink(path);
        if (!File.Exists(path)) throw new FileNotFoundException("参考图片文件缺失：" + image.FileName, path);
        return path;
    }

    public IReadOnlyList<string> EnumerateFiles()
    {
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
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Entries == null) throw new InvalidDataException("小说条目列表无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in project.Entries)
        {
            if (entry?.Images == null) throw new InvalidDataException("条目的参考图片列表无效。");
            foreach (var image in entry.Images)
            {
                ValidateDescriptor(image);
                if (!ids.Add(image.Id)) throw new InvalidDataException("参考图片编号重复。");
                ResolvePath(image);
            }
        }
    }

    // Copy every managed image, including images removed from the current model but used by history.
    // The caller validates references in its in-memory project before copying, then saves the new JSON.
    public void CopyTo(string destinationFolder)
    {
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
