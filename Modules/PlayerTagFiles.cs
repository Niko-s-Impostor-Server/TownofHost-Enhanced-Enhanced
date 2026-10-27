using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TOHE.Modules;

// TOHE's per-friend-code files own both appearance and command grants.
internal static class PlayerTagFiles
{
#if ANDROID
    private static string TagsDirectory => Path.Combine(UnityEngine.Application.persistentDataPath, "TOHE-DATA", "Tags");
#else
    private static string TagsDirectory => Path.Combine("TOHE-DATA", "Tags");
#endif
    private static readonly string[] ReservedDirectories = ["MOD_TAGS", "VIP_TAGS", "SPONSOR_TAGS"];
    private static Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    internal static void Reset() => entries = new(StringComparer.Ordinal);

    internal static bool Reload()
    {
        var loaded = new Dictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            Directory.CreateDirectory(TagsDirectory);
            string template = Path.Combine(TagsDirectory, "Tag_Template.txt");
            if (!File.Exists(template))
                File.WriteAllText(template, "TagName: \nTagColor: \nPermissionLevel: 0\nSayCommandAccess: false\nEndCommandAccess: false\nExecuteCommandAccess: false\nAssignGameMaster: false\n");

            foreach (string directory in Directory.GetDirectories(TagsDirectory)
                .Where(path => !ReservedDirectories.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                ReadDirectory(directory, loaded);
            ReadDirectory(TagsDirectory, loaded);
            entries = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Reset();
            Logger.Warn("Local player tag files could not be loaded", "PlayerTagFiles");
            return false;
        }
    }

    private static void ReadDirectory(string directory, Dictionary<string, Entry> loaded)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*.txt").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string friendCode = Path.GetFileNameWithoutExtension(path);
            if (!PlayerFriendCode.IsValid(friendCode) || loaded.ContainsKey(friendCode) || new FileInfo(path).Length > 16 * 1024) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("TagName:", StringComparison.Ordinal)) fields.TryAdd("TagName", line[8..].TrimStart());
                else if (line.StartsWith("TagColor:", StringComparison.Ordinal)) fields.TryAdd("TagColor", line[9..].Trim());
                else if (line.StartsWith("PermissionLevel:", StringComparison.Ordinal)) fields.TryAdd("PermissionLevel", line[16..].Trim());
                else if (line.StartsWith("SayCommandAccess:", StringComparison.Ordinal)) fields.TryAdd("SayCommandAccess", line[17..].Trim());
                else if (line.StartsWith("EndCommandAccess:", StringComparison.Ordinal)) fields.TryAdd("EndCommandAccess", line[17..].Trim());
                else if (line.StartsWith("ExecuteCommandAccess:", StringComparison.Ordinal)) fields.TryAdd("ExecuteCommandAccess", line[21..].Trim());
                else if (line.StartsWith("AssignGameMaster:", StringComparison.Ordinal)) fields.TryAdd("AssignGameMaster", line[17..].Trim());
            }
            string name = fields.GetValueOrDefault("TagName") ?? string.Empty;
            string color = fields.GetValueOrDefault("TagColor")?.Trim() ?? string.Empty;
            string level = fields.GetValueOrDefault("PermissionLevel")?.Trim() ?? string.Empty;
            string say = fields.GetValueOrDefault("SayCommandAccess")?.Trim() ?? string.Empty;
            string end = fields.GetValueOrDefault("EndCommandAccess")?.Trim() ?? string.Empty;
            string execute = fields.GetValueOrDefault("ExecuteCommandAccess")?.Trim() ?? string.Empty;
            string gm = fields.GetValueOrDefault("AssignGameMaster")?.Trim() ?? string.Empty;
            if (name.Length > 128) name = string.Empty;
            if (!int.TryParse(level, out int permissionLevel) || permissionLevel is < 0 or > 5) permissionLevel = 0;
            loaded.Add(friendCode, new(name, color, permissionLevel, IsTrue(say), IsTrue(end), IsTrue(execute), IsTrue(gm)));
        }
    }

    private static bool IsTrue(string value) => value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("y", StringComparison.OrdinalIgnoreCase)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("t", StringComparison.OrdinalIgnoreCase)
        || value == "1";

    private static Entry Find(PlayerControl player) => entries.GetValueOrDefault(PlayerFriendCode.FromOwner(player));

    internal static int PermissionLevel(PlayerControl player) => Find(player)?.Level ?? -1;
    internal static bool CanSay(PlayerControl player) => Find(player)?.Say == true;
    internal static bool CanEnd(PlayerControl player) => Find(player)?.End == true;
    internal static bool CanExecute(PlayerControl player) => Find(player)?.Execute == true;
    internal static bool IsGameMaster(PlayerControl player) => Find(player)?.GameMaster == true;
    internal static bool IsAdministrator(PlayerControl player) => Find(player) is { } entry
        && (entry.Level >= 2 || entry.Say || entry.End || entry.Execute);

    internal static bool TryGetRenderedTag(PlayerControl player, bool gradient, out string tag)
    {
        tag = string.Empty;
        var entry = Find(player);
        if (entry == null || entry.Name.Length == 0) return false;
        tag = entry.Render(gradient);
        return true;
    }

    private sealed record Entry(string Name, string Color, int Level, bool Say, bool End, bool Execute, bool GameMaster)
    {
        internal string Render(bool gradient)
        {
            if (Color.Length == 0) return Name;
            if (gradient && Regex.IsMatch(Color, @"^[0-9A-Fa-f]{6}\s[0-9A-Fa-f]{6}$"))
            {
                string[] colors = Regex.Split(Color, @"\s+");
                return colors[0] == colors[1]
                    ? $"<color=#{colors[0]}>{Name}</color>"
                    : Utils.GradientColorText(colors[0], colors[1], Name);
            }
            string color = Utils.CheckColorHex(Color) ? Color : "ffff00";
            return $"<color=#{color}>{Name}</color>";
        }
    }
}
