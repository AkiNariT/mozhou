using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Mozhou.Core;

// Windows supplies SQLite; all SQL passed to this internal wrapper is application-owned.
// C API contracts: https://www.sqlite.org/c3ref/intro.html
internal sealed class NativeSqlite : IDisposable
{
    private const int Ok = 0, Row = 100, Done = 101;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly DatabaseHandle handle;

    internal NativeSqlite(string path, bool writable)
    {
        var result = Native.Open(Utf8.GetBytes(path + '\0'), out var pointer,
            (writable ? 2 : 1) | 0x10000 | 0x40000, IntPtr.Zero); // FULLMUTEX, PRIVATECACHE; never implicit CREATE or URI.
        handle = new DatabaseHandle(pointer);
        try
        {
            Check(result);
            Check(Native.BusyTimeout(handle, 3000));
            Configure(1002, 0); // ENABLE_TRIGGER
            Configure(1005, 0); // ENABLE_LOAD_EXTENSION (both SQL and C API)
            Configure(1010, 1); // DEFENSIVE
            Configure(1015, 0); // ENABLE_VIEW
            Configure(1017, 0); // TRUSTED_SCHEMA
            Native.Limit(handle, 0, 64 * 1024 * 1024 + 4096); // Per value/row, not per database.
            Native.Limit(handle, 1, 16 * 1024);
            Native.Limit(handle, 2, 32);
            Native.Limit(handle, 3, 32);
            Native.Limit(handle, 7, 0);
            Native.Limit(handle, 9, 8);
            Execute("PRAGMA trusted_schema=OFF");
            Execute("PRAGMA cell_size_check=ON");
            Execute("PRAGMA mmap_size=0");
            if (writable) Execute("PRAGMA synchronous=FULL");
        }
        catch { handle.Dispose(); throw; }
    }

    private void Configure(int option, int value) => Check(Native.Configure(handle, option, value, IntPtr.Zero));

    internal Statement Prepare(string sql)
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        var bytes = Utf8.GetBytes(sql + '\0');
        var result = Native.Prepare(handle, bytes, bytes.Length, out var pointer, IntPtr.Zero);
        var statement = new Statement(this, pointer);
        try { Check(result); return statement; }
        catch { statement.Dispose(); throw; }
    }

    internal void Execute(string sql)
    {
        using var statement = Prepare(sql);
        while (statement.Step()) { }
    }

    internal long Integer(string sql)
    {
        using var statement = Prepare(sql);
        if (!statement.Step()) throw new InvalidDataException("小说容器缺少必要信息。");
        var result = statement.Integer(0);
        if (statement.Step()) throw new InvalidDataException("小说容器包含重复信息。");
        return result;
    }

    internal string Text(string sql)
    {
        using var statement = Prepare(sql);
        if (!statement.Step()) throw new InvalidDataException("小说容器缺少必要信息。");
        var result = statement.Text(0, 4096);
        if (statement.Step()) throw new InvalidDataException("小说容器包含重复信息。");
        return result;
    }

    internal void BackupTo(NativeSqlite destination)
    {
        var main = "main\0"u8.ToArray();
        var backup = Native.BackupInit(destination.handle, main, handle, main);
        if (backup == IntPtr.Zero) { destination.Check(Native.ErrorCode(destination.handle)); throw new IOException("无法创建小说快照。"); }
        var finished = false;
        try
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                var result = Native.BackupStep(backup, 256);
                if (result == Done) break;
                if (result == Ok) continue;
                if ((result == 5 || result == 6) && timer.ElapsedMilliseconds < 3000) { Thread.Sleep(25); continue; }
                destination.Check(result);
            }
            var finalResult = Native.BackupFinish(backup);
            finished = true;
            destination.Check(finalResult);
        }
        finally { if (!finished) Native.BackupFinish(backup); GC.KeepAlive(this); GC.KeepAlive(destination); }
    }

    private void Check(int result)
    {
        if (result == Ok) return;
        var detail = handle.IsInvalid ? "无法打开数据库" : Marshal.PtrToStringUTF8(Native.ErrorMessage(handle));
        var message = $"小说容器访问失败（SQLite {result}）：{detail}";
        if ((result & 255) == 8 && !handle.IsInvalid && Native.ExtendedErrorCode(handle) == 776)
            throw new RecoveryRequiredException(message); // SQLITE_READONLY_ROLLBACK
        if ((result & 255) is 1 or 11 or 17 or 18 or 26) throw new InvalidDataException(message);
        throw new IOException(message);
    }

    internal sealed class RecoveryRequiredException(string message) : IOException(message);

    public void Dispose() => handle.Dispose(); // close_v2 also rolls back an unfinished transaction.

    internal sealed class Statement : IDisposable
    {
        private readonly NativeSqlite database;
        private readonly StatementHandle handle;
        internal Statement(NativeSqlite database, IntPtr pointer) { this.database = database; handle = new StatementHandle(pointer); }

        internal void BindText(int index, string value)
        {
            var bytes = Utf8.GetBytes(value + '\0');
            database.Check(Native.BindText(handle, index, bytes, bytes.Length - 1, new IntPtr(-1))); // SQLITE_TRANSIENT
        }
        internal void BindBlob(int index, byte[] value)
        {
            // A null data pointer means SQL NULL, even at length zero. zeroblob preserves an empty BLOB.
            database.Check(value.Length == 0 ? Native.BindZeroBlob(handle, index, 0) : Native.BindBlob(handle, index, value, value.Length, new IntPtr(-1)));
        }
        internal bool Step()
        {
            var result = Native.Step(handle);
            if (result == Row) return true;
            if (result == Done) return false;
            database.Check(result); return false;
        }
        internal bool IsNull(int column) => Native.ColumnType(handle, column) == 5;
        internal long Integer(int column)
        {
            if (Native.ColumnType(handle, column) != 1) throw new InvalidDataException("小说容器字段类型无效。");
            return Native.ColumnInt64(handle, column);
        }
        internal string Text(int column, int maximumBytes)
        {
            if (Native.ColumnType(handle, column) != 3) throw new InvalidDataException("小说容器文字字段类型无效。");
            var pointer = Native.ColumnText(handle, column);
            var count = Native.ColumnBytes(handle, column);
            if (pointer == IntPtr.Zero) throw new IOException("无法读取小说容器文字字段。");
            CheckLength(count, maximumBytes);
            var bytes = new byte[count]; Marshal.Copy(pointer, bytes, 0, count); GC.KeepAlive(handle);
            try { return Utf8.GetString(bytes); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("小说容器包含无效 UTF-8 路径。", ex); }
        }
        internal byte[] Blob(int column, int maximumBytes)
        {
            if (Native.ColumnType(handle, column) != 4) throw new InvalidDataException("小说容器内容字段不是二进制数据。");
            var count = Native.ColumnBytes(handle, column);
            CheckLength(count, maximumBytes);
            if (count == 0) return [];
            var pointer = Native.ColumnBlob(handle, column);
            if (pointer == IntPtr.Zero) throw new IOException("无法读取小说容器内容。");
            var bytes = new byte[count]; Marshal.Copy(pointer, bytes, 0, count); GC.KeepAlive(handle); return bytes;
        }
        private static void CheckLength(int count, int maximumBytes)
        {
            if (count < 0 || count > maximumBytes) throw new InvalidDataException("小说容器中的单个文件超过大小限制。");
        }
        public void Dispose() => handle.Dispose(); // Step reports execution errors before finalization.
    }

    private sealed class DatabaseHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal DatabaseHandle(IntPtr pointer) : base(true) => SetHandle(pointer);
        protected override bool ReleaseHandle() => Native.Close(handle) == Ok;
    }
    private sealed class StatementHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal StatementHandle(IntPtr pointer) : base(true) => SetHandle(pointer);
        protected override bool ReleaseHandle() { Native.Finalize(handle); return true; }
    }

    private static class Native
    {
        private const string Library = "winsqlite3.dll";
        [DllImport(Library, EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int Open(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Library, EntryPoint = "sqlite3_close_v2", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int Close(IntPtr db);
        [DllImport(Library, EntryPoint = "sqlite3_db_config", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int Configure(DatabaseHandle db, int option, int value, IntPtr result);
        [DllImport(Library, EntryPoint = "sqlite3_busy_timeout", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int BusyTimeout(DatabaseHandle db, int milliseconds);
        [DllImport(Library, EntryPoint = "sqlite3_limit", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int Limit(DatabaseHandle db, int category, int value);
        [DllImport(Library, EntryPoint = "sqlite3_errmsg", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern IntPtr ErrorMessage(DatabaseHandle db);
        [DllImport(Library, EntryPoint = "sqlite3_errcode", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int ErrorCode(DatabaseHandle db);
        [DllImport(Library, EntryPoint = "sqlite3_extended_errcode", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int ExtendedErrorCode(DatabaseHandle db);
        [DllImport(Library, EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int Prepare(DatabaseHandle db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
        [DllImport(Library, EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int Step(StatementHandle statement);
        [DllImport(Library, EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int Finalize(IntPtr statement);
        [DllImport(Library, EntryPoint = "sqlite3_bind_text", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int BindText(StatementHandle statement, int index, byte[] bytes, int length, IntPtr destructor);
        [DllImport(Library, EntryPoint = "sqlite3_bind_blob", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int BindBlob(StatementHandle statement, int index, byte[] bytes, int length, IntPtr destructor);
        [DllImport(Library, EntryPoint = "sqlite3_bind_zeroblob", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int BindZeroBlob(StatementHandle statement, int index, int length);
        [DllImport(Library, EntryPoint = "sqlite3_column_type", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int ColumnType(StatementHandle statement, int column);
        [DllImport(Library, EntryPoint = "sqlite3_column_bytes", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int ColumnBytes(StatementHandle statement, int column);
        [DllImport(Library, EntryPoint = "sqlite3_column_blob", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern IntPtr ColumnBlob(StatementHandle statement, int column);
        [DllImport(Library, EntryPoint = "sqlite3_column_text", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern IntPtr ColumnText(StatementHandle statement, int column);
        [DllImport(Library, EntryPoint = "sqlite3_column_int64", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern long ColumnInt64(StatementHandle statement, int column);
        [DllImport(Library, EntryPoint = "sqlite3_backup_init", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern IntPtr BackupInit(DatabaseHandle destination, byte[] destinationName, DatabaseHandle source, byte[] sourceName);
        [DllImport(Library, EntryPoint = "sqlite3_backup_step", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int BackupStep(IntPtr backup, int pages);
        [DllImport(Library, EntryPoint = "sqlite3_backup_finish", CallingConvention = CallingConvention.Cdecl), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int BackupFinish(IntPtr backup);
    }
}
