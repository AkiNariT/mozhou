using System.IO;
using Mozhou.Core;

namespace Mozhou.Desktop;

internal static class BookshelfStartup
{
    internal static string? Seed(BookshelfStore shelf, string profile, string? recentPath)
    {
        var candidates = new List<string>();
        string? notice = null;
        if (!string.IsNullOrWhiteSpace(recentPath))
        {
            try { candidates.Add(ProjectFolder(recentPath)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { notice = "旧设置中的小说路径无效，已跳过。你可以通过“打开 / 导入小说”重新添加。"; }
        }

        var library = new DirectoryInfo(Path.Combine(profile, "Library"));
        try
        {
            if (!library.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                foreach (var item in library.EnumerateFileSystemInfos())
                {
                    if (item.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (item is DirectoryInfo folder)
                    {
                        if (File.Exists(Path.Combine(folder.FullName, "project.mozhou.json")) ||
                            File.Exists(Path.Combine(folder.FullName, "project.mozhou.json.bak"))) candidates.Add(folder.FullName);
                    }
                    else if (item.Extension.Equals(".mozhou", StringComparison.OrdinalIgnoreCase)) candidates.Add(item.FullName);
                }
            }
        }
        catch (DirectoryNotFoundException) when (!Directory.Exists(library.FullName)) { }
        shelf.Seed(candidates);
        return notice;
    }

    internal static string ProjectFolder(string path)
    {
        var folder = !Directory.Exists(path) && string.Equals(Path.GetFileName(path), "project.mozhou.json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.GetFullPath(path))! : Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(folder);
    }
}
