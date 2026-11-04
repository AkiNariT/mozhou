using Mozhou.Core;

namespace Mozhou.Desktop;

internal static class CharacterLooks
{
    internal static (string Hairstyle, string Outfit) SharedTraits(IEnumerable<EntryImage> images, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ("", "");
        var group = images.Where(image => string.Equals(image.LookName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        return (Shared(group.Select(image => image.Hairstyle)), Shared(group.Select(image => image.Outfit)));
    }

    private static string Shared(IEnumerable<string> values)
    {
        var distinct = values.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return distinct.Length == 1 ? distinct[0] : "";
    }
}
