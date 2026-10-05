using System.Text;
using System.Text.Json;
using TOHE.Modules;

var assertions = 0;
void Check(bool condition, string scenario)
{
    if (!condition) throw new Exception("FAIL: " + scenario);
    assertions++;
}
bool Parse(string json, out LocalPlayerTagsConfig config) =>
    LocalPlayerTagsConfig.TryParse(Encoding.UTF8.GetBytes(json), out config, out _);
const string valid = """
{"version":1,"players":{"sampleuser#1234":{"tag":{"text":"A😀中","startColor":"#FF0000","endColor":"0000FF"},"permissions":["moderate","chat"],"gameMaster":true},"otheruser#0000":{"permissions":["end","execute"]}}}
""";
Check(Parse(valid, out var config) && config.Count == 2, "valid document loads atomically");
Check(LocalPlayerTagsConfig.TryParse([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(valid)], out var withBom, out _) && withBom.Count == 2, "UTF-8 editor BOM accepted");
Check(config.HasPermission("sampleuser#1234", LocalPlayerPermission.Moderate), "explicit moderation grant");
Check(config.HasPermission("sampleuser#1234", LocalPlayerPermission.Chat), "explicit chat grant");
Check(!config.HasPermission("sampleuser#1234", LocalPlayerPermission.End), "chat/moderation cannot end games");
Check(!config.HasPermission("sampleuser#1234", LocalPlayerPermission.Execute), "GM does not grant execute");
Check(config.HasPermission("otheruser#0000", LocalPlayerPermission.Execute), "independent execute grant");
Check(config.IsGameMaster("sampleuser#1234") && !config.IsGameMaster("otheruser#0000"), "independent GM flag");
Check(!config.HasPermission("sampleuser#1234", LocalPlayerPermission.None) && !config.HasPermission("sampleuser#1234", (LocalPlayerPermission)99), "unknown enums fail closed");
Check(!config.HasPermission("Sampleuser#1234", LocalPlayerPermission.Chat) && !config.HasPermission("user#1234", LocalPlayerPermission.Chat), "exact identity matches only");
Check(config.TryGetTag("sampleuser#1234", false, out var flat) && flat == "<color=#FF0000>A😀中</color>", "gradient-off uses start color");
Check(config.TryGetTag("sampleuser#1234", true, out var gradient) && gradient == "<color=#FF0000>A</color><color=#800080>😀</color><color=#0000FF>中</color>", "gradient preserves runes and both endpoints");
Check(!config.TryGetTag("otheruser#0000", true, out var missing) && missing == "", "permission-only entry adds no tag");

foreach (string invalid in new[]
{
    "{", "null", "{}", "[]", "{\"version\":1,\"players\":{},\"url\":\"ignored\"}",
    valid.Replace("\"version\":1", "\"version\":2"),
    valid.Replace("\"version\":1", "\"version\":true"),
    valid.Replace("\"version\":1", "\"version\":1,\"version\":1"),
    valid.Replace("\"players\":{", "\"players\":{\"sampleuser#1234\":{},"),
    valid.Replace("\"gameMaster\":true", "\"gameMaster\":\"true\""),
    valid.Replace("\"gameMaster\":true", "\"gameMaster\":true,\"gameMaster\":false"),
    valid.Replace("\"moderate\",\"chat\"", "\"admin\""),
    valid.Replace("\"moderate\",\"chat\"", "\"chat\",\"chat\""),
    valid.Replace("\"moderate\",\"chat\"", "true"),
    valid.Replace("\"permissions\":[\"moderate\",\"chat\"]", "\"permissions\":\"chat\""),
    valid.Replace("\"gameMaster\":true", "\"gameMaster\":true,\"permissionLevel\":5"),
    valid.Replace("\"text\":\"A😀中\"", "\"text\":\"A😀中\",\"text\":\"changed\""),
    valid.Replace("#FF0000", "FFFF"), valid.Replace("0000FF", "<red>"),
    valid.Replace("sampleuser#1234", "../user#1234"),
    valid.Replace("sampleuser#1234", "Sampleuser#1234"),
    valid.Replace("sampleuser#1234", "sampleuser#１２３４"),
    valid.Replace("sampleuser#1234", "sampleuser#12345"),
    valid.Replace("sampleuser#1234", "sampleuser#1234 "),
    valid.Replace("sampleuser#1234", ""),
    valid.Replace("A😀中", "A\\nB"), valid.Replace("A😀中", "A\\u202eB"),
    valid.Replace("A😀中", "A\\u2028B"), valid.Replace("A😀中", " "),
    valid.Replace("A😀中", new string('a', 49)), valid.Replace("A😀中", "\\ud800")
})
{
    Check(!Parse(invalid, out var rejected) && rejected.Count == 0 && !rejected.HasPermission("sampleuser#1234", LocalPlayerPermission.Chat), "invalid document revokes every entry");
}
var markup = valid.Replace("A😀中", "<b>A&amp;</b>");
Check(Parse(markup, out var sanitized) && sanitized.TryGetTag("sampleuser#1234", false, out var safe) && safe == "<color=#FF0000>＜b＞A＆amp;＜/b＞</color>", "only generated markup survives");
Check(Parse(valid.Replace("A😀中", new string('中', 48)), out var maximum) && maximum.TryGetTag("sampleuser#1234", true, out var bounded) && bounded.Length <= 1200, "48-rune output remains bounded");
Check(Parse(valid.Replace("A😀中", "😀"), out var single) && single.TryGetTag("sampleuser#1234", true, out var one) && one == "<color=#FF0000>😀</color>", "single-rune gradient has defined start endpoint");
var playerEntries = new Dictionary<string, object>();
for (int index = 0; index < 129; index++) playerEntries.Add("sampleuser#" + index.ToString("D4"), new { permissions = new[] { "chat" } });
Check(!Parse(JsonSerializer.Serialize(new { version = 1, players = playerEntries }), out var excessive) && excessive.Count == 0, "entry count is bounded");
Check(!LocalPlayerTagsConfig.TryParse(new byte[LocalPlayerTagsConfig.MaxFileBytes + 1], out _, out _), "byte count is bounded");
Check(Parse(LocalPlayerTagsConfig.EmptyDocument, out var empty) && !empty.HasPermission("sampleuser#1234", LocalPlayerPermission.Chat), "empty config has no default grants");

var originalDirectory = Directory.GetCurrentDirectory();
var temporaryDirectory = Path.Combine(Path.GetTempPath(), "TOHE-LocalPlayerTags-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryDirectory);
Directory.SetCurrentDirectory(temporaryDirectory);
try
{
    Check(LocalPlayerTags.Reload(out _) && File.Exists("TOHE-DATA/LocalPlayerTags.json") && LocalPlayerTags.EntryCount == 0, "missing file creates empty local template");
    File.WriteAllText("TOHE-DATA/LocalPlayerTags.json", valid);
    var actualSender = new PlayerControl();
    Check(LocalPlayerTags.Reload(out _) && LocalPlayerTags.HasPermission(actualSender, LocalPlayerPermission.Chat), "host loads local file and authorizes Data identity");
    actualSender.Data.FriendCode = "unlisted#1234";
    actualSender.FriendCode = "sampleuser#1234";
    Check(!LocalPlayerTags.HasPermission(actualSender, LocalPlayerPermission.Chat), "player friend-code property cannot spoof Data identity");
    actualSender.Data.FriendCode = "sampleuser#1234";
    actualSender.Data.Disconnected = true;
    Check(!LocalPlayerTags.HasPermission(actualSender, LocalPlayerPermission.Chat) && !LocalPlayerTags.IsDesignatedGameMaster(actualSender), "disconnected sender denied");
    actualSender.Data.Disconnected = false;
    AmongUsClient.Instance.AmHost = false;
    Check(!LocalPlayerTags.HasPermission(actualSender, LocalPlayerPermission.Chat) && !LocalPlayerTags.TryGetRenderedTag(actualSender, true, out _) && !LocalPlayerTags.IsDesignatedGameMaster(actualSender), "non-host cannot use cached grants or tags");
    Check(!LocalPlayerTags.Reload(out _) && LocalPlayerTags.EntryCount == 0, "non-host reload clears cache");
    AmongUsClient.Instance.AmHost = true;
    Check(LocalPlayerTags.Reload(out _) && LocalPlayerTags.IsDesignatedGameMaster(actualSender), "host GM query is passive");
    File.WriteAllText("TOHE-DATA/LocalPlayerTags.json", "{broken");
    Check(!LocalPlayerTags.Reload(out _) && !LocalPlayerTags.HasPermission(actualSender, LocalPlayerPermission.Chat) && LocalPlayerTags.EntryCount == 0, "failed reload revokes previous grants");
    Check(File.ReadAllText("TOHE-DATA/LocalPlayerTags.json") == "{broken", "reload preserves damaged file");
    File.WriteAllText("TOHE-DATA/LocalPlayerTags.json", valid);
    Check(LocalPlayerTags.Reload(out _) && !LocalPlayerTags.HasPermission(null, LocalPlayerPermission.Chat), "null sender denied");
    var missingData = new PlayerControl { Data = null };
    Check(!LocalPlayerTags.HasPermission(missingData, LocalPlayerPermission.Chat), "missing Data denied");
    using (File.Open("TOHE-DATA/LocalPlayerTags.json", FileMode.Open, FileAccess.Read, FileShare.None))
        Check(!LocalPlayerTags.Reload(out _) && LocalPlayerTags.EntryCount == 0, "unreadable configuration revokes grants");
    LocalPlayerTags.Reset();
    Check(LocalPlayerTags.EntryCount == 0, "session reset revokes grants");
}
finally
{
    Directory.SetCurrentDirectory(originalDirectory);
    Directory.Delete(temporaryDirectory, recursive: true);
}
Console.WriteLine($"Local player tags: {assertions} assertions passed.");
