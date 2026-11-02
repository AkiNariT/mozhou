using System.Buffers.Binary;
using System.IO.Compression;

namespace Mozhou.Core;

/// <summary>A complete novel in one SQLite document. Every operation owns and closes its connection.</summary>
public sealed class SingleFileDocument
{
    private const string ProjectName = "project.mozhou.json", ChatName = "assistant-chat.json";
    private const int ApplicationId = 0x4D5A4831;
    private const int MaximumJsonBytes = 64 * 1024 * 1024, MaximumImageBytes = 25 * 1024 * 1024;
    private const string TableSql = "CREATE TABLE files (name TEXT PRIMARY KEY, content BLOB NOT NULL)";
    public string Path { get; }

    public SingleFileDocument(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        using var database = OpenValidated(false);
    }

    public byte[]? ReadFile(string name)
    {
        ValidateName(name);
        using var database = OpenValidated(false);
        return Read(database, name);
    }

    public IReadOnlyList<string> ListFiles(string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length > 240 || prefix.Contains('\\') || prefix.Contains('\0') || prefix.Contains("..", StringComparison.Ordinal) || prefix.StartsWith('/'))
            throw new InvalidDataException("小说内部路径无效。");
        using var database = OpenValidated(false);
        using var statement = database.Prepare("SELECT name FROM files ORDER BY name");
        var names = new List<string>();
        while (statement.Step())
        {
            var name = statement.Text(0, 960); ValidateName(name);
            if (name.StartsWith(prefix, StringComparison.Ordinal)) names.Add(name);
        }
        return names;
    }

    public void WriteFiles(IReadOnlyDictionary<string, byte[]?> changes,
        byte[]? expectedProject = null, bool checkProject = false, byte[]? expectedChat = null, bool checkChat = false)
    {
        ArgumentNullException.ThrowIfNull(changes);
        // Validate the complete change set before acquiring the writer lock or modifying any row.
        foreach (var (name, bytes) in changes) { ValidateName(name); if (bytes is not null) ValidateLength(name, bytes.Length); }
        using var database = OpenValidated(true);
        if (checkProject && !Same(expectedProject, Read(database, ProjectName)))
            throw new ConflictException("小说已被其他窗口修改，请重新打开后再保存。");
        if (checkChat && !Same(expectedChat, Read(database, ChatName)))
            throw new ConflictException("小说对话已被其他窗口修改，请重新打开后再保存。");
        ValidateUniqueNames(database, changes);
        foreach (var (name, bytes) in changes)
        {
            using var statement = database.Prepare(bytes is null
                ? "DELETE FROM files WHERE name=?1"
                : "INSERT INTO files(name,content) VALUES(?1,?2) ON CONFLICT(name) DO UPDATE SET content=excluded.content");
            statement.BindText(1, name);
            if (bytes is not null) statement.BindBlob(2, bytes);
            statement.Step();
        }
        database.Execute("COMMIT");
    }

    public static void CreateNew(string path, IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var destination = NewDestination(path, true);
        var temporary = ReserveTemporary(destination);
        try
        {
            using (var database = new NativeSqlite(temporary, true))
            {
                if (database.Text("PRAGMA journal_mode=DELETE") != "delete") throw new IOException("无法启用小说安全保存日志。");
                database.Execute("BEGIN IMMEDIATE");
                database.Execute("PRAGMA application_id=1297762353");
                database.Execute("PRAGMA user_version=1");
                database.Execute(TableSql);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, bytes) in files)
                {
                    ValidateName(name);
                    if (!names.Add(name)) throw new InvalidDataException("小说容器包含重复文件名。");
                    if (bytes is null) throw new InvalidDataException("小说容器文件内容不能为空引用。");
                    ValidateLength(name, bytes.Length);
                    using var statement = database.Prepare("INSERT INTO files(name,content) VALUES(?1,?2)");
                    statement.BindText(1, name); statement.BindBlob(2, bytes); statement.Step();
                }
                database.Execute("COMMIT");
            }
            Publish(temporary, destination);
        }
        finally { DeleteTemporary(temporary); }
    }

    public void CopyTo(string destination)
    {
        var target = NewDestination(destination, true);
        var temporary = ReserveTemporary(target);
        try
        {
            using (var source = OpenValidated(false))
            using (var copy = new NativeSqlite(temporary, true))
            {
                if (copy.Text("PRAGMA journal_mode=DELETE") != "delete") throw new IOException("无法启用小说安全保存日志。");
                // The source read transaction fixes one consistent version across all backup steps.
                source.BackupTo(copy);
                ValidateSchema(copy);
                ValidateContents(copy);
            }
            Publish(temporary, target);
        }
        finally { DeleteTemporary(temporary); }
    }

    public void ExportZip(string destination)
    {
        var target = NewDestination(destination, false);
        var temporary = ReserveTemporary(target);
        try
        {
            using (var source = OpenValidated(false))
            using (var output = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                using (var rows = source.Prepare("SELECT name,length(content),content FROM files ORDER BY name"))
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    while (rows.Step())
                    {
                        var name = rows.Text(0, 960); ValidateName(name);
                        if (!names.Add(name)) throw new InvalidDataException("小说容器包含重复文件名。");
                        ValidateLength(name, rows.Integer(1));
                        // Only the current row's BLOB is materialized, never the complete image library.
                        var bytes = rows.Blob(2, MaximumBytes(name));
                        using var entry = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                        entry.Write(bytes);
                    }
                }
                output.Flush(true);
            }
            File.Move(temporary, target, false);
        }
        finally { DeleteTemporary(temporary); }
    }

    private NativeSqlite OpenValidated(bool writable, bool recover = false)
    {
        if (!File.Exists(Path)) throw new FileNotFoundException("小说文件不存在。", Path);
        if (writable || recover) ValidateWritableHeader();
        var database = new NativeSqlite(Path, writable || recover);
        try
        {
            database.Execute(writable ? "BEGIN IMMEDIATE" : "BEGIN");
            ValidateSchema(database);
            return database;
        }
        catch (NativeSqlite.RecoveryRequiredException) when (!writable && !recover)
        {
            database.Dispose();
            // A normal read never writes. Only SQLite's explicit hot-journal error permits a
            // writable retry to restore the last committed version after process interruption.
            return OpenValidated(false, true);
        }
        catch { database.Dispose(); throw; }
    }

    private void ValidateWritableHeader()
    {
        // Check identity before allowing SQLite to recover a journal, which can modify disk
        // before SQL schema validation is possible. Header offsets are SQLite format constants.
        Span<byte> header = stackalloc byte[100];
        using var input = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.ReadAtLeast(header, header.Length, false) != header.Length ||
            !header[..16].SequenceEqual("SQLite format 3\0"u8) ||
            BinaryPrimitives.ReadInt32BigEndian(header[68..72]) != ApplicationId ||
            BinaryPrimitives.ReadInt32BigEndian(header[60..64]) != 1)
            throw new InvalidDataException("这不是受支持的墨舟单文件小说，未修改文件。");
    }

    private static void ValidateSchema(NativeSqlite database)
    {
        if (database.Integer("PRAGMA application_id") != ApplicationId) throw new InvalidDataException("这不是墨舟单文件小说。");
        if (database.Integer("PRAGMA user_version") != 1) throw new InvalidDataException("此小说容器版本不受支持。");
        if (database.Text("PRAGMA journal_mode") != "delete") throw new InvalidDataException("小说容器日志模式不受支持。");
        // v1 has exactly our table and its primary-key index. No triggers, views, virtual tables,
        // expression indexes, generated columns, or SQL supplied by the document are accepted.
        using var rows = database.Prepare("SELECT type,name,tbl_name,sql FROM sqlite_schema ORDER BY name");
        var table = false; var index = false;
        while (rows.Step())
        {
            var type = rows.Text(0, 32); var name = rows.Text(1, 128); var owner = rows.Text(2, 128);
            if (type == "table" && name == "files" && owner == "files" && !table && rows.Text(3, 4096) == TableSql) table = true;
            else if (type == "index" && name == "sqlite_autoindex_files_1" && owner == "files" && !index && rows.IsNull(3)) index = true;
            else throw new InvalidDataException("小说容器表结构无效或不受支持。");
        }
        if (!table || !index) throw new InvalidDataException("小说容器缺少必要数据表。");
    }

    private static byte[]? Read(NativeSqlite database, string name)
    {
        using var statement = database.Prepare("SELECT length(content),content FROM files WHERE name=?1");
        statement.BindText(1, name);
        if (!statement.Step()) return null;
        ValidateLength(name, statement.Integer(0));
        var bytes = statement.Blob(1, MaximumBytes(name));
        if (statement.Step()) throw new InvalidDataException("小说容器包含重复文件名。");
        return bytes;
    }

    private static void ValidateContents(NativeSqlite database)
    {
        using var rows = database.Prepare("SELECT name,length(content),typeof(content) FROM files");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (rows.Step())
        {
            var name = rows.Text(0, 960); ValidateName(name);
            if (!names.Add(name)) throw new InvalidDataException("小说容器包含重复文件名。");
            if (rows.Text(2, 16) != "blob") throw new InvalidDataException("小说容器内容字段不是二进制数据。");
            ValidateLength(name, rows.Integer(1));
        }
    }

    private static void ValidateUniqueNames(NativeSqlite database, IReadOnlyDictionary<string, byte[]?> changes)
    {
        // Root names are canonical. History and legacy image suffixes may preserve original casing.
        // Check only changed namespaces using the PK index; saving prose never loads image BLOBs.
        foreach (var prefix in new[] { "backups/", "assets/images/" })
        {
            var scoped = changes.Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (scoped.Length == 0) continue;
            var replaced = new HashSet<string>(scoped.Select(item => item.Key), StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var rows = database.Prepare("SELECT name FROM files WHERE name>=?1 AND name<?2");
            rows.BindText(1, prefix); rows.BindText(2, prefix[..^1] + "0");
            while (rows.Step())
            {
                var name = rows.Text(0, 960); ValidateName(name);
                if (!replaced.Contains(name) && !names.Add(name)) throw new InvalidDataException("小说容器包含重复文件名。");
            }
            foreach (var (name, bytes) in scoped)
                if (bytes is not null && !names.Add(name)) throw new InvalidDataException("小说容器包含重复文件名。");
        }
    }

    private static bool Same(byte[]? first, byte[]? second) => first is null ? second is null : second is not null && first.AsSpan().SequenceEqual(second);
    private static int MaximumBytes(string name) => name.StartsWith("assets/images/", StringComparison.Ordinal) ? MaximumImageBytes : MaximumJsonBytes;
    private static void ValidateLength(string name, long length)
    {
        if (length < 0 || length > MaximumBytes(name)) throw new InvalidDataException("小说容器中的单个文件超过大小限制（图片 25 MiB，JSON 64 MiB）。");
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 240 || name.Contains("..", StringComparison.Ordinal) || name.Contains('\\') || name.Contains('\0'))
            throw new InvalidDataException("小说内部文件名无效。");
        if (name is ProjectName or "project.mozhou.json.bak" or ChatName) return;
        if (name.StartsWith("backups/project-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
        {
            var suffix = name.AsSpan(16, name.Length - 16 - 5);
            if (suffix.Length > 0 && suffix.ToString().All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')) return;
        }
        if (name.StartsWith("assets/images/", StringComparison.Ordinal))
        {
            var filename = name[14..]; var dot = filename.IndexOf('.');
            if (dot == 32 && Guid.TryParseExact(filename.AsSpan(0, 32), "N", out _) && filename[dot..].ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif") return;
        }
        throw new InvalidDataException("小说内部文件名不受支持。");
    }

    private static string DocumentPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = System.IO.Path.GetFullPath(path);
        if (!string.Equals(System.IO.Path.GetExtension(full), ".mozhou", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("单文件小说必须使用 .mozhou 扩展名。");
        return full;
    }
    private static string NewDestination(string path, bool document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = System.IO.Path.GetFullPath(path);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("目标已存在，请选择新的文件名。");
        return document ? DocumentPath(full) : full;
    }
    private static string ReserveTemporary(string destination)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var reserve = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        return temporary;
    }
    private static void Publish(string temporary, string destination)
    {
        using (var file = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) file.Flush(true);
        File.Move(temporary, destination, false);
    }
    private static void DeleteTemporary(string temporary)
    {
        // Only our uniquely reserved private temporary file and its SQLite sidecars are eligible.
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            if (File.Exists(temporary + suffix)) File.Delete(temporary + suffix);
    }
}
