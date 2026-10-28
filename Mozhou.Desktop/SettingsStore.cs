using System.IO;
using System.Text.Json;
namespace Mozhou.Desktop;
public sealed class UserSettings
{
    public string? LastProjectPath { get; set; }
    public string? CodexPath { get; set; }
    public string? Model { get; set; }
}
public static class SettingsStore
{
    public static string DataRoot => AppContext.BaseDirectory;
    private static string FilePath => Path.Combine(DataRoot, "mozhou-settings.json");
    public static UserSettings Read()
    {
        try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    public static void Write(UserSettings value)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
