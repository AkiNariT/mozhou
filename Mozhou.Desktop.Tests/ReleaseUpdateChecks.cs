using System.IO;
using System.Text.Json;
using Mozhou.Desktop;

internal static class ReleaseUpdateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var current = new Version(0, 8, 0);
        const string installer = "Mozhou-Setup-0.10.0-x64.exe";
        const string download = "https://github.com/AkiNariT/mozhou/releases/download/v0.10.0/" + installer;
        const string page = "https://github.com/AkiNariT/mozhou/releases/tag/v0.10.0";
        static string Release(string tag = "v0.10.0", string name = installer, string url = download,
            bool draft = false, bool preview = false, string state = "uploaded") =>
            JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease = preview, html_url = "file:///untrusted",
                assets = new[] { new { name, browser_download_url = url, state } } });

        var next = ReleaseUpdate.Read(Release(), current, installed: true);
        check(next.Latest == new Version(0, 10, 0) && next.ActionUrl == download && next.IsInstaller,
            "numeric version comparison offers the exact x64 installer for a newer release");
        var portable = ReleaseUpdate.Read(Release(), current, installed: false);
        check(portable.ActionUrl == page && !portable.IsInstaller, "portable update opens the official tagged release");
        foreach (var tag in new[] { "v0.8.0", "v0.7.9" })
            check(ReleaseUpdate.Read(Release(tag), current, true).ActionUrl == null, "no upgrade action for " + tag);
        foreach (var json in new[] { Release(url: "file:///C:/attacker.exe"), Release(url: "https://example.org/fake"),
                    Release(url: download.Replace("/AkiNariT/", "/Other/")), Release(url: download + "?x=1"),
                    Release(name: "Mozhou-Setup-0.9.0-x64.exe"), Release(state: "starter") })
        {
            var result = ReleaseUpdate.Read(json, current, true);
            check(result.ActionUrl == page && !result.IsInstaller, "unmatched asset falls back to the official tagged release");
        }
        foreach (var json in new[] { Release(draft: true), Release(preview: true), Release("v0.10.0-rc.1"),
                    Release("v1.0.0/../../Other/repo"), "{}" })
        {
            try { ReleaseUpdate.Read(json, current, true); }
            catch (InvalidDataException) { check(true, "invalid or unofficial release never offers an update"); continue; }
            throw new Exception("Expected to reject invalid or unofficial release: " + json);
        }
    }
}
