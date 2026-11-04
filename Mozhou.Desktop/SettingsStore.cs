using System.IO;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;
using Mozhou.Core;

namespace Mozhou.Desktop;
public sealed class UserSettings
{
    public string? LastProjectPath { get; set; }
    public string? CodexPath { get; set; }
    public string? Model { get; set; }
}
public static class SettingsStore
{
    private const string InstalledMarker = "mozhou-installed.marker";
    private const string SettingsFile = "mozhou-settings.json";
    private const string RegistryPath = @"HKEY_CURRENT_USER\Software\Mozhou";
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, InstalledMarker));
    public static string DataRoot { get; private set; } = AppContext.BaseDirectory;

    internal static void Initialize(Func<Exception, bool> retryImport)
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(local) || !Path.IsPathFullyQualified(local))
            local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DataRoot = PrepareProfile(AppContext.BaseDirectory, local,
            () => Registry.GetValue(RegistryPath, "LegacyPortablePath", null) as string,
            path => Registry.SetValue(RegistryPath, "PortablePath", path), retryImport);
    }

    internal static string PrepareProfile(string applicationDirectory, string localAppData, Func<string?> readLegacyPath, Action<string> rememberPortable,
        Func<Exception, bool>? retryImport = null)
    {
        if (!File.Exists(Path.Combine(applicationDirectory, InstalledMarker)))
        {
            // Registration is only a hint for a future installer. Portable use also works without registry access.
            try { rememberPortable(Path.TrimEndingDirectorySeparator(applicationDirectory)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException) { }
            return applicationDirectory;
        }

        if (!Path.IsPathFullyQualified(localAppData)) throw new IOException("本机用户资料目录不可用。");
        var root = Directory.CreateDirectory(Path.Combine(localAppData, "Mozhou", "UserData")).FullName;
        var settings = Path.Combine(root, SettingsFile);
        var shelf = new BookshelfStore(root);
        var receipt = Path.Combine(root, ".portable-import-complete");
        var skipped = Path.Combine(root, ".portable-import-skipped");
        while (!File.Exists(receipt) && !File.Exists(skipped) && !(File.Exists(settings) && shelf.Exists))
        {
            try
            {
                var source = readLegacyPath();
                if (string.IsNullOrWhiteSpace(source)) break;
                if (!Path.IsPathFullyQualified(source)) throw new IOException("旧版资料目录必须是绝对路径。");
                // A disconnected portable drive must not look like a successfully imported, empty bookshelf.
                if (!Directory.Exists(source)) throw new DirectoryNotFoundException("旧版资料目录不可用：" + source);
                CopyMissing(Path.Combine(source, SettingsFile), settings);
                CopyMissing(Path.Combine(source, "bookshelf.json"), shelf.FilePath);
                if (!shelf.Exists) BookshelfStartup.Seed(shelf, source, Read(settings).LastProjectPath);
                File.Open(receipt, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite).Dispose();
                break;
            }
            catch (Exception ex) when (retryImport != null && ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
            {
                if (retryImport(ex)) continue;
                File.Open(skipped, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite).Dispose();
                break;
            }
        }
        return root;
    }

    private static void CopyMissing(string source, string destination)
    {
        if (File.Exists(destination)) return;
        FileStream input;
        try { input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (FileNotFoundException) { return; }
        using (input)
        {
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(true);
                }
                try { File.Move(temp, destination); }
                catch (IOException) when (File.Exists(destination)) { } // Another instance published the file first.
            }
            finally { File.Delete(temp); }
        }
    }

    public static UserSettings Read() => Read(Path.Combine(DataRoot, SettingsFile));
    private static UserSettings Read(string path)
    {
        try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }
    public static void Write(UserSettings value)
    {
        var path = Path.Combine(DataRoot, SettingsFile);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }
}
