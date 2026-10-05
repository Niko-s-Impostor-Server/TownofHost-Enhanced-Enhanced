using System.Text.Json;
using System.Text.Json.Nodes;
using TOHE;
using TOHE.Modules;

var previousDirectory = Environment.CurrentDirectory;
var temporaryDirectory = Path.Combine(Path.GetTempPath(), "TOHEE-presets-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryDirectory);
Environment.CurrentDirectory = temporaryDirectory;
int checks = 0;
try
{
    var preset = PresetOptionItem.Create(2, TabGroup.ModSettings);
    var parent = BooleanOptionItem.Create(1, "Parent", false, TabGroup.ModSettings, false);
    var boolean = BooleanOptionItem.Create(2, "Child", true, TabGroup.ModSettings, false);
    boolean.SetParent(parent);
    var integer = IntegerOptionItem.Create(3, "Integer", (10, 30, 5), 15, TabGroup.ModSettings, false);
    var number = FloatOptionItem.Create(4, "Float", (0.1f, 1.1f, 0.1f), 0.4f, TabGroup.ModSettings, false);
    var choice = StringOptionItem.Create(5, "Choice", ["Off", "On", "Advanced"], 1, TabGroup.ModSettings, false);
    var global = BooleanOptionItem.Create(6, "Global", true, TabGroup.ModSettings, true);
    TextOptionItem.Create(7, "Heading", TabGroup.ModSettings);
    OptionSaver.Initialize();

    var saved = PresetSharing.Save("测试预设");
    Check(saved.Success && saved.OptionCount == 6, "save exports all editable options, excluding selector/headings");
    var presetPath = Path.Combine(temporaryDirectory, "TOHE-DATA", "Presets", "测试预设.preset");
    var savedJson = File.ReadAllText(presetPath);
    var source = JsonNode.Parse(savedJson).AsObject();
    Check(Entry(source, 2)["value"].GetValue<bool>() && !boolean.GetBool(), "child stores own true value under disabled parent");
    Check(Entry(source, 6)["scope"].GetValue<string>() == "global", "global scope is explicit");
    var otherSlots = (int[])integer.AllValues.Clone();
    boolean.SetValue(0, false, false);
    integer.SetValue(4, false, false);
    number.SetValue(1, false, false);
    choice.SetValue(2, false, false);
    global.SetValue(0, false, false);
    RPC.SyncCalls = 0;
    Check(PresetSharing.Load("测试预设.preset").Success, "valid snapshot imports");
    Check(boolean.CurrentValue == 1 && integer.GetInt() == 15 && number.CurrentValue == 3 && choice.CurrentValue == 1 && global.CurrentValue == 1,
        "all supported types and global values restore");
    Check(OptionItem.CurrentPreset == 2 && preset.SingleValue == 2 && integer.AllValues[0] == otherSlots[0] && integer.AllValues[4] == otherSlots[4],
        "import preserves selector and inactive slots");
    Check(RPC.SyncCalls == 1, "successful commit broadcasts once");
    Check(File.ReadAllText(Path.Combine(temporaryDirectory, "TOHE-DATA", "SaveData", "Options.json")).Contains("\"SingleOptions\""), "successful import persists Options.json");

    Invalid("unknown ID", data => Entry(data, 6)["id"] = 99999);
    Invalid("duplicate ID", data => Entry(data, 6)["id"] = 5);
    Invalid("missing option", data => data["options"].AsArray().RemoveAt(5));
    Invalid("preset selector injection", data => Entry(data, 6)["id"] = 0);
    Invalid("wrong option type", data => Entry(data, 3)["type"] = "float");
    Invalid("renamed option", data => Entry(data, 3)["name"] = "Different");
    Invalid("changed option rule", data => Entry(data, 3)["step"] = 1);
    Invalid("changed choices", data => Entry(data, 5)["choices"][1] = "Altered");
    Invalid("wrong scope", data => Entry(data, 6)["scope"] = "preset");
    Invalid("integer coercion", data => Entry(data, 3)["value"] = "15");
    Invalid("boolean coercion", data => Entry(data, 2)["value"] = 1);
    Invalid("float outside range", data => Entry(data, 4)["value"] = 200f);
    Invalid("negative index", data => Entry(data, 3)["index"] = -1);
    Invalid("wrapped index", data => Entry(data, 3)["index"] = 100);
    Invalid("inconsistent index/value", data => Entry(data, 3)["index"] = 0);
    Invalid("unknown semantic choice", data => Entry(data, 5)["value"] = "Unknown");
    Invalid("format version", data => data["version"] = 100);
    Invalid("option format version", data => data["optionVersion"] = 100);
    Invalid("mod version", data => data["modVersion"] = "other");
    Invalid("game version", data => data["gameVersion"] = "2024.10.29");
    Invalid("unknown metadata", data => data["mystery"] = true);
    Invalid("unknown option property", data => Entry(data, 3)["extra"] = true);
    Invalid("malformed root", data => data["options"] = null);
    InvalidJson("duplicate JSON property", savedJson.Replace("\"version\": 1", "\"version\": 1, \"version\": 1"));
    InvalidJson("malformed JSON", "{");
    InvalidJson("null document", "null");
    InvalidJson("oversized file", new string(' ', PresetSharing.MaximumFileBytes + 1));

    foreach (var name in new[] { "../escape", "..\\escape", "C:\\escape", "name:stream", "CON", "LPT1", "NUL.preset", "x.txt", "", new string('x', 65) })
        Check(PresetSharing.Save(name).Error == PresetSharing.Error.InvalidName && PresetSharing.Load(name).Error == PresetSharing.Error.InvalidName,
            "unsafe file name rejected: " + name);
    Check(PresetSharing.Load("missing").Error == PresetSharing.Error.NotFound, "missing import fails without creating a file");
    Check(!File.Exists(Path.Combine(temporaryDirectory, "TOHE-DATA", "Presets", "missing.preset")), "missing file is preserved as absent");

    File.WriteAllText(presetPath, savedJson);
    AmongUsClient.Instance.AmHost = false;
    Check(PresetSharing.Save("denied").Error == PresetSharing.Error.NotHost && PresetSharing.Load("测试预设").Error == PresetSharing.Error.NotHost,
        "client cannot save/load host presets");
    AmongUsClient.Instance.AmHost = true;
    GameStates.IsLobby = false;
    Check(PresetSharing.Load("测试预设").Error == PresetSharing.Error.LobbyOnly && PresetSharing.Save("denied").Error == PresetSharing.Error.LobbyOnly,
        "in-game commands are rejected");
    GameStates.IsLobby = true;
    GameStates.IsCoStartGame = true;
    Check(PresetSharing.Load("测试预设").Error == PresetSharing.Error.LobbyOnly, "game-start transition rejects import");
    GameStates.IsCoStartGame = false;

    // Force commit failure after application: destination is a directory, so atomic file rename fails.
    integer.SetValue(4, false, false);
    global.SetValue(0, false, false);
    var beforeDiskFailure = State();
    var optionsPath = Path.Combine(temporaryDirectory, "TOHE-DATA", "SaveData", "Options.json");
    File.Delete(optionsPath);
    Directory.CreateDirectory(optionsPath);
    RPC.SyncCalls = 0;
    Check(PresetSharing.Load("测试预设").Error == PresetSharing.Error.IoError, "persistence failure reported");
    Check(State() == beforeDiskFailure && RPC.SyncCalls == 0, "failed disk commit rolls back all values without broadcasting");
    Check(!Directory.EnumerateFiles(Path.GetDirectoryName(optionsPath), "*.tmp").Any(), "failed commit cleans temporary file");
    Directory.Delete(optionsPath);

    // A UI refresh failure after mutating an earlier option must also restore the entire snapshot.
    integer.OptionBehaviour = new StringOption();
    StringOption.FailRefresh = true;
    var beforeApplyFailure = State();
    Check(PresetSharing.Load("测试预设").Error == PresetSharing.Error.ApplyFailed, "application error reported");
    Check(State() == beforeApplyFailure && RPC.SyncCalls == 0, "application error restores earlier and throwing option");
    StringOption.FailRefresh = false;
    Console.WriteLine($"PASS: {checks} preset sharing checks");

    void Invalid(string name, Action<JsonObject> mutate)
    {
        var data = JsonNode.Parse(savedJson).AsObject();
        // An earlier valid changed setting proves validation never partially applies a bad later entry.
        Entry(data, 1)["index"] = 1;
        Entry(data, 1)["value"] = true;
        mutate(data);
        InvalidJson(name, data.ToJsonString());
    }
    void InvalidJson(string name, string json)
    {
        var before = State();
        var disk = File.ReadAllBytes(Path.Combine(temporaryDirectory, "TOHE-DATA", "SaveData", "Options.json"));
        File.WriteAllText(presetPath, json);
        RPC.SyncCalls = 0;
        Check(!PresetSharing.Load("测试预设").Success, name + " rejected");
        Check(State() == before && RPC.SyncCalls == 0 && disk.SequenceEqual(File.ReadAllBytes(Path.Combine(temporaryDirectory, "TOHE-DATA", "SaveData", "Options.json"))),
            name + " leaves memory, persistent file and network unchanged");
        Check(File.ReadAllText(presetPath) == json, name + " preserves imported file");
    }
    string State() => JsonSerializer.Serialize(OptionItem.AllOptions.Select(option => new { option.Id, option.SingleValue, option.AllValues })) + OptionItem.CurrentPreset;
    void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
}
finally
{
    Environment.CurrentDirectory = previousDirectory;
    Directory.Delete(temporaryDirectory, recursive: true);
}

static JsonObject Entry(JsonObject data, int id) => data["options"].AsArray().Select(entry => entry.AsObject()).Single(entry => entry["id"].GetValue<int>() == id);
