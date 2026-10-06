using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TOHE.Modules;

[Flags]
public enum LocalPlayerPermission
{
    None = 0,
    Moderate = 1,
    Chat = 2,
    End = 4,
    Execute = 8,
    Rename = 16,
    Color = 32,
    Warn = 64,
    Start = 128,
    Kick = 256,
    Ban = 512
}

// This parser has no game, network, logging, or filesystem dependencies.
// A malformed document grants nothing, including entries parsed before the error.
internal sealed class LocalPlayerTagsConfig
{
    internal const int MaxFileBytes = 128 * 1024;
    internal const int MaxPlayers = 128;
    internal const int MaxTagRunes = 48;
    internal const string EmptyDocument = "{\n  \"version\": 1,\n  \"players\": {}\n}\n";
    internal static LocalPlayerTagsConfig Empty => new(new(StringComparer.Ordinal));
    private readonly Dictionary<string, Entry> players;

    private LocalPlayerTagsConfig(Dictionary<string, Entry> players) => this.players = players;
    internal int Count => players.Count;

    internal bool HasPermission(string friendCode, LocalPlayerPermission permission) =>
        permission != LocalPlayerPermission.None && Enum.IsDefined(permission) && Find(friendCode) is { } entry &&
        ((entry.Permissions & permission) != 0 || permission is LocalPlayerPermission.Kick or LocalPlayerPermission.Ban
            && (entry.Permissions & LocalPlayerPermission.Moderate) != 0);

    internal bool IsAdministrator(string friendCode) => Find(friendCode) is { Permissions: not LocalPlayerPermission.None };

    internal bool IsGameMaster(string friendCode) => Find(friendCode)?.GameMaster == true;

    internal bool TryGetTag(string friendCode, bool gradient, out string tag)
    {
        tag = string.Empty;
        var entry = Find(friendCode);
        if (entry?.Tag == null) return false;
        tag = entry.Tag.Render(gradient);
        return true;
    }

    private Entry Find(string friendCode) => IsValidFriendCode(friendCode) && players.TryGetValue(friendCode, out var entry) ? entry : null;

    // Canonical game friend codes use lowercase ASCII words and four ASCII digits.
    // Never trim, lowercase, match a substring, or use the code as a file path.
    internal static bool IsValidFriendCode(string code)
    {
        if (code == null || code.Length < 6 || code.Length > 32) return false;
        int separator = code.Length - 5;
        if (code[separator] != '#') return false;
        for (int i = 0; i < separator; i++)
            if (code[i] is < 'a' or > 'z') return false;
        for (int i = separator + 1; i < code.Length; i++)
            if (code[i] is < '0' or > '9') return false;
        return true;
    }

    internal static bool TryParse(byte[] json, out LocalPlayerTagsConfig config, out string error)
    {
        config = Empty;
        error = string.Empty;
        if (json == null || json.Length > MaxFileBytes) return Fail("Configuration exceeds the byte limit.", out error);
        try
        {
            ReadOnlyMemory<byte> payload = json;
            if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF) payload = payload[3..];
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
            var root = ReadObject(document.RootElement, "version", "players");
            if (!root.TryGetValue("version", out var version) || !version.TryGetInt32(out int number) || number != 1 ||
                !root.TryGetValue("players", out var playerObject) || playerObject.ValueKind != JsonValueKind.Object)
                return Fail("Expected version 1 and a players object.", out error);
            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var player in playerObject.EnumerateObject())
            {
                if (entries.Count >= MaxPlayers || !IsValidFriendCode(player.Name) || entries.ContainsKey(player.Name))
                    return Fail("Invalid, duplicate, or excessive player entries.", out error);
                var fields = ReadObject(player.Value, "tag", "permissions", "gameMaster");
                Tag tag = null;
                if (fields.TryGetValue("tag", out var tagValue))
                {
                    var tagFields = ReadObject(tagValue, "text", "startColor", "endColor");
                    if (!tagFields.TryGetValue("text", out var textValue) || textValue.ValueKind != JsonValueKind.String ||
                        !TrySanitizeText(textValue.GetString(), out string text))
                        return Fail("Invalid tag text.", out error);
                    string start = "FFFFFF", end = start;
                    if (tagFields.TryGetValue("startColor", out var startValue) && !TryColor(startValue, out start))
                        return Fail("Invalid tag color.", out error);
                    end = start;
                    if (tagFields.TryGetValue("endColor", out var endValue) && !TryColor(endValue, out end))
                        return Fail("Invalid tag color.", out error);
                    tag = new(text, start, end);
                }
                var permissions = LocalPlayerPermission.None;
                if (fields.TryGetValue("permissions", out var grants))
                {
                    if (grants.ValueKind != JsonValueKind.Array || grants.GetArrayLength() > 10)
                        return Fail("Invalid permission list.", out error);
                    foreach (var grant in grants.EnumerateArray())
                    {
                        if (grant.ValueKind != JsonValueKind.String) return Fail("Invalid permission.", out error);
                        var permission = grant.GetString() switch
                        {
                            "moderate" => LocalPlayerPermission.Moderate,
                            "chat" => LocalPlayerPermission.Chat,
                            "end" => LocalPlayerPermission.End,
                            "execute" => LocalPlayerPermission.Execute,
                            "rename" => LocalPlayerPermission.Rename,
                            "color" => LocalPlayerPermission.Color,
                            "warn" => LocalPlayerPermission.Warn,
                            "start" => LocalPlayerPermission.Start,
                            "kick" => LocalPlayerPermission.Kick,
                            "ban" => LocalPlayerPermission.Ban,
                            _ => LocalPlayerPermission.None
                        };
                        if (permission == LocalPlayerPermission.None || (permissions & permission) != 0)
                            return Fail("Unknown or duplicate permission.", out error);
                        permissions |= permission;
                    }
                }
                bool gameMaster = false;
                if (fields.TryGetValue("gameMaster", out var gm))
                {
                    if (gm.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return Fail("gameMaster must be a boolean.", out error);
                    gameMaster = gm.GetBoolean();
                }
                entries.Add(player.Name, new(tag, permissions, gameMaster));
            }
            config = new(entries);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            // Do not echo configuration content or exception messages into chat/logs.
            return Fail("Malformed configuration or unknown/duplicate fields.", out error);
        }
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException();
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (Array.IndexOf(allowed, field.Name) < 0 || !result.TryAdd(field.Name, field.Value)) throw new JsonException();
        return result;
    }

    private static bool Fail(string message, out string error) { error = message; return false; }

    private static bool TryColor(JsonElement value, out string color)
    {
        color = string.Empty;
        if (value.ValueKind != JsonValueKind.String) return false;
        var input = value.GetString();
        if (input.StartsWith('#')) input = input[1..];
        if (input.Length != 6) return false;
        foreach (char character in input)
            if (!Uri.IsHexDigit(character)) return false;
        color = input.ToUpperInvariant();
        return true;
    }

    private static bool TrySanitizeText(string text, out string sanitized)
    {
        sanitized = string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTagRunes * 2) return false;
        var output = new StringBuilder();
        int count = 0;
        // Decode explicitly: EnumerateRunes alone replaces invalid UTF-16 with U+FFFD.
        for (int index = 0; index < text.Length;)
        {
            if (!Rune.TryGetRuneAt(text, index, out var rune) || ++count > MaxTagRunes) return false;
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                return false;
            output.Append(rune.Value switch { '<' => "＜", '>' => "＞", '&' => "＆", _ => rune.ToString() });
            index += rune.Utf16SequenceLength;
        }
        sanitized = output.ToString();
        return true;
    }

    private sealed record Entry(Tag Tag, LocalPlayerPermission Permissions, bool GameMaster);

    private sealed record Tag(string Text, string StartColor, string EndColor)
    {
        internal string Render(bool gradient)
        {
            if (!gradient || StartColor == EndColor) return $"<color=#{StartColor}>{Text}</color>";
            var runes = new List<Rune>();
            foreach (var rune in Text.EnumerateRunes()) runes.Add(rune);
            int start = int.Parse(StartColor, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int end = int.Parse(EndColor, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var output = new StringBuilder();
            for (int index = 0; index < runes.Count; index++)
            {
                double fraction = runes.Count == 1 ? 0 : (double)index / (runes.Count - 1);
                int color = 0;
                foreach (int shift in new[] { 16, 8, 0 })
                {
                    int channel = (int)Math.Round(((start >> shift) & 255) * (1 - fraction) + ((end >> shift) & 255) * fraction);
                    color |= channel << shift;
                }
                output.Append("<color=#").Append(color.ToString("X6", CultureInfo.InvariantCulture)).Append('>').Append(runes[index].ToString()).Append("</color>");
            }
            return output.ToString();
        }
    }
}
