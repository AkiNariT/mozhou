using System.IO;
using System.Text.Json;

namespace Mozhou.Desktop;

internal sealed record ReleaseUpdate(Version Latest, string? ActionUrl, bool IsInstaller)
{
    private const string Repository = "https://github.com/AkiNariT/mozhou";
    internal const string LatestApi = "https://api.github.com/repos/AkiNariT/mozhou/releases/latest";

    public static ReleaseUpdate Read(string json, Version current, bool installed)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        string? tag = Text(release, "tag_name");
        string? number = tag?.StartsWith('v') == true ? tag[1..] : tag;
        if (!Version.TryParse(number, out var latest) || latest.Build < 0 || latest.Revision >= 0
            || latest.ToString() != number || !IsFalse(release, "draft") || !IsFalse(release, "prerelease"))
            throw new InvalidDataException("发布信息中缺少有效的正式版本号。");
        if (latest <= current) return new(latest, null, false);

        string filename = $"Mozhou-Setup-{latest}-x64.exe";
        string download = $"{Repository}/releases/download/{tag}/{filename}";
        if (installed && release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
            && assets.EnumerateArray().Any(asset => Text(asset, "name") == filename && Text(asset, "state") == "uploaded"
                && Text(asset, "browser_download_url") == download))
            return new(latest, download, true);
        return new(latest, $"{Repository}/releases/tag/{tag}", false);
    }

    private static string? Text(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    private static bool IsFalse(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.False;
}
