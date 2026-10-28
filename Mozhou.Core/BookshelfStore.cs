using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mozhou.Core;

public sealed class BookshelfStore(string dataRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly StringComparer FolderComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public string FilePath { get; } = Path.Combine(Path.GetFullPath(dataRoot), "bookshelf.json");
    public bool Exists => File.Exists(FilePath);

    public IReadOnlyList<ShelfBook> Read()
    {
        using var gate = AcquireIndexLock();
        return ReadCurrent();
    }

    public IReadOnlyList<ShelfBook> Refresh()
    {
        using var gate = AcquireIndexLock();
        var books = ReadCurrent();
        if (!Exists) return books;
        for (var i = 0; i < books.Count; i++)
        {
            var cached = books[i];
            try { books[i] = Snapshot(cached.Folder, new ProjectStore(cached.Folder).Load(), cached.LastOpenedAt); }
            catch (Exception ex) when (IsUnreadableProject(ex))
            {
                // Keep the cached title and statistics so the user can locate or recover the book.
                cached.IsAvailable = false; cached.Problem = ex.Message;
            }
        }
        var sorted = Sort(books); WriteCurrent(sorted); return sorted;
    }

    public void Remember(string folder, NovelProject project)
    {
        var normalized = NormalizeFolder(folder);
        var snapshot = Snapshot(normalized, project, DateTimeOffset.Now);
        using var gate = AcquireIndexLock();
        // Always merge with disk under the lock; this instance may predate another window's additions.
        var books = ReadCurrent(); books.RemoveAll(book => FolderComparer.Equals(book.Folder, normalized));
        books.Add(snapshot); WriteCurrent(Sort(books));
    }

    public void Forget(string folder)
    {
        var normalized = NormalizeFolder(folder);
        using var gate = AcquireIndexLock();
        var books = ReadCurrent();
        if (books.RemoveAll(book => FolderComparer.Equals(book.Folder, normalized)) > 0) WriteCurrent(books);
    }

    public void Seed(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        using var gate = AcquireIndexLock();
        if (Exists) return;
        var books = new Dictionary<string, ShelfBook>(FolderComparer);
        foreach (var folder in folders)
        {
            string normalized;
            try { normalized = NormalizeFolder(folder); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (books.ContainsKey(normalized)) continue;
            try
            {
                books.Add(normalized, Snapshot(normalized, new ProjectStore(normalized).Load(), default));
            }
            catch (Exception ex) when (IsUnreadableProject(ex))
            {
                // The caller supplies known novels only; keep unavailable ones reachable for recovery.
                var title = Path.GetFileName(normalized);
                books.Add(normalized, new ShelfBook { Folder = normalized, Title = string.IsNullOrEmpty(title) ? normalized : title, Problem = ex.Message });
            }
        }
        // An empty index is still initialized: removed books must not reappear on the next startup.
        WriteCurrent(Sort(books.Values));
    }

    private List<ShelfBook> ReadCurrent()
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(FilePath); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        try
        {
            ReadOnlyMemory<byte> content = bytes;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) content = content[3..];
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("书架索引格式无效，原文件未修改。");
            var books = new List<ShelfBook>();
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                ShelfBook? book;
                try
                {
                    book = row.Deserialize<ShelfBook>(JsonOptions);
                    if (book == null || string.IsNullOrWhiteSpace(book.Folder)) continue;
                    book.Folder = NormalizeFolder(book.Folder);
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException or PathTooLongException) { continue; }
                book.Title ??= ""; book.Description ??= "";
                book.WordCount = Math.Max(0, book.WordCount); book.ChapterCount = Math.Max(0, book.ChapterCount);
                books.Add(book);
            }
            return Sort(books).DistinctBy(book => book.Folder, FolderComparer).ToList();
        }
        catch (JsonException ex) { throw new InvalidDataException("书架索引无法读取，原文件未修改。", ex); }
    }

    private FileStream AcquireIndexLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 11 or 32 or 33)
            {
                if (timer.Elapsed >= TimeSpan.FromSeconds(5)) throw new IOException("另一个墨舟窗口正在更新书架，请稍后重试。", ex);
                Thread.Sleep(25);
            }
        }
    }

    private void WriteCurrent(IReadOnlyList<ShelfBook> books)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(books, JsonOptions);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes); stream.Flush(true);
            }
            if (Exists) File.Replace(temporary, FilePath, null, true);
            else File.Move(temporary, FilePath, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static ShelfBook Snapshot(string folder, NovelProject project, DateTimeOffset openedAt)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Title == null || project.Description == null || project.Entries == null || project.Entries.Any(entry => entry == null))
            throw new InvalidDataException("小说资料不完整，书架未修改。");
        var chapters = project.Entries.Where(entry => entry.Kind == "chapter").ToArray();
        return new ShelfBook
        {
            Folder = folder, Title = project.Title, Description = project.Description,
            WordCount = chapters.Sum(entry => ProjectContent.CountWords(entry.Content)), ChapterCount = chapters.Length,
            UpdatedAt = project.UpdatedAt, LastOpenedAt = openedAt, IsAvailable = true
        };
    }

    private static string NormalizeFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    }

    private static List<ShelfBook> Sort(IEnumerable<ShelfBook> books) => books.OrderByDescending(book => book.LastOpenedAt)
        .ThenByDescending(book => book.UpdatedAt).ThenBy(book => book.Folder, FolderComparer).ToList();

    private static bool IsUnreadableProject(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException;
}
