using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TOHE.Modules;

// Backward-compatible display-only reader for Tags/{FriendCode}.txt files.
// Legacy permission fields are deliberately ignored; grants come from LocalPlayerTags.json.
internal static class LegacyLobbyTags
{
    private static readonly string[] ReservedDirectories = ["MOD_TAGS", "VIP_TAGS", "SPONSOR_TAGS"];
    private static Dictionary<string, LegacyTag> tags = new(StringComparer.Ordinal);
    private static bool loaded;

    internal static void Reset()
    {
        tags = new(StringComparer.Ordinal);
        loaded = false;
    }

    internal static void Reload()
    {
#if ANDROID
        Reload(Path.Combine(UnityEngine.Application.persistentDataPath, "TOHE-DATA", "Tags"));
#else
        Reload(Path.Combine("TOHE-DATA", "Tags"));
#endif
    }

    internal static void Reload(string tagsDirectory)
    {
        var loadedTags = new Dictionary<string, LegacyTag>(StringComparer.Ordinal);
        try
        {
            if (Directory.Exists(tagsDirectory))
            {
                // Root entries win, then ordinary subdirectories in stable order.
                ReadDirectory(tagsDirectory, loadedTags);
                foreach (string directory in Directory.GetDirectories(tagsDirectory)
                    .Where(path => !ReservedDirectories.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    ReadDirectory(directory, loadedTags);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Logger.Warn("Legacy lobby tag files could not be read", "TagManager");
        }

        tags = loadedTags;
        loaded = true;
    }

    internal static bool TryGetRenderedTag(string friendCode, bool gradient, out string tag)
    {
        tag = string.Empty;
        if (!loaded || !LocalPlayerTagsConfig.IsValidFriendCode(friendCode) || !tags.TryGetValue(friendCode, out var value)) return false;
        tag = value.Render(gradient);
        return true;
    }

    private static void ReadDirectory(string directory, Dictionary<string, LegacyTag> destination)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*.txt").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string friendCode = Path.GetFileNameWithoutExtension(path);
            if (!LocalPlayerTagsConfig.IsValidFriendCode(friendCode) || destination.ContainsKey(friendCode)) continue;

            try
            {
                if (new FileInfo(path).Length > 16 * 1024) continue;
                string tagName = string.Empty;
                string tagColor = string.Empty;
                foreach (string line in File.ReadLines(path))
                {
                    if (tagName.Length == 0 && line.StartsWith("TagName:", StringComparison.Ordinal)) tagName = line[8..].Trim();
                    else if (tagColor.Length == 0 && line.StartsWith("TagColor:", StringComparison.Ordinal)) tagColor = line[9..].Trim();
                }
                if (tagName.Length > 0 && tagName.Length <= 128) destination.Add(friendCode, new(tagName, tagColor));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Skip one unreadable entry and continue loading other players' tags.
            }
        }
    }

    private static bool IsColor(string value) => Regex.IsMatch(value, @"^[0-9A-Fa-f]{6}$");
    private static bool IsGradient(string value) => Regex.IsMatch(value, @"^[0-9A-Fa-f]{6}\s[0-9A-Fa-f]{6}$");

    private sealed record LegacyTag(string Name, string Color)
    {
        internal string Render(bool gradient)
        {
            if (gradient && IsGradient(Color))
            {
                string[] colors = Regex.Split(Color, @"\s+");
                return colors[0] == colors[1]
                    ? $"<color=#{colors[0]}>{Name}</color>"
                    : Utils.GradientColorText(colors[0], colors[1], Name);
            }
            string color = IsColor(Color) ? Color : "ffff00";
            return $"<color=#{color}>{Name}</color>";
        }
    }
}
