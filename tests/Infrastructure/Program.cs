using System.Text.Json;
using TOHE;
using TOHE.Modules;

var originalDirectory = Directory.GetCurrentDirectory();
var temporaryDirectory = Path.Combine(Path.GetTempPath(), "TOHE-Infrastructure-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryDirectory);
Directory.SetCurrentDirectory(temporaryDirectory);
var assertions = 0;
void Check(bool condition, string scenario)
{
    if (!condition) throw new Exception("FAIL: " + scenario);
    assertions++;
}

try
{
    var preset = PresetOptionItem.Create(0, TabGroup.SystemSettings);
    var number = IntegerOptionItem.Create(101, "Number", new(10, 50, 10), 30, TabGroup.ModSettings, false);
    var toggle = BooleanOptionItem.Create(102, "Toggle", true, TabGroup.ModSettings, true);
    var choice = StringOptionItem.Create(103, "Choice", ["A", "B", "C"], 0, TabGroup.ModSettings, true);
    OptionSaver.Initialize();
    OptionSaver.Save();
    var optionsPath = Path.Combine(temporaryDirectory, "TOHE-DATA", "SaveData", "Options.json");

    var eventCount = 0;
    number.RegisterUpdateValueEvent((_, _) => eventCount++);
    var savedBeforeReset = File.ReadAllText(optionsPath);
    var syncBeforeReset = RPC.SyncCalls;
    number.SetValueNoRpc(4);
    Check(number.GetInt() == 50 && eventCount == 1, "reset applies once");
    Check(RPC.SyncCalls == syncBeforeReset, "no-RPC reset does not send settings");
    Check(File.ReadAllText(optionsPath) == savedBeforeReset, "no-RPC reset does not write settings");

    RPC.SyncCalls = 0;
    preset.SetValue(3);
    Check(OptionItem.CurrentPreset == 3 && preset.SingleValue == 3, "preset selector and active preset agree");
    Check(RPC.SyncCalls == 1 && RPC.SyncedPreset == 3, "preset sends exactly one update with selected preset");
    OptionItem.CurrentPreset = int.MaxValue;
    Check(number.GetInt() == 30 && OptionItem.CurrentPreset == 4, "external preset index cannot escape storage bounds");

    var shortValues = new[] { 1, 2 };
    number.SetAllValues(shortValues);
    shortValues[0] = 99;
    OptionItem.CurrentPreset = 0;
    Check(number.GetInt() == 20, "restored values are independent from source array");
    OptionItem.CurrentPreset = 4;
    Check(number.GetInt() == 30, "short stored presets retain defaults for missing slots");
    number.SetAllValues(null);
    Check(number.GetInt() == 30, "null preset array restores usable defaults");
    number.SetAllValues([0, 1, 2, 3, 4, 100]);
    Check(number.AllValues.Length == OptionItem.NumPresets, "long stored presets are truncated");

    foreach (var damagedJson in new[] { "{", "null", "{}", "{\"Version\":1,\"SingleOptions\":null,\"PresetOptions\":{}}" })
    {
        File.WriteAllText(optionsPath, damagedJson);
        OptionSaver.Load();
        Check(File.ReadAllText(optionsPath) == damagedJson, "damaged save does not interrupt loading or overwrite evidence");
    }
    var data = new OptionSaver.SerializableOptionsData
    {
        Version = 1,
        SingleOptions = new() { [0] = 4, [102] = 3, [103] = -2, [101] = 99 },
        PresetOptions = new() { [101] = [1, 2], [102] = [0], [99999] = [8] }
    };
    File.WriteAllText(optionsPath, JsonSerializer.Serialize(data));
    RPC.SyncCalls = 0;
    OptionSaver.Load();
    Check(OptionItem.CurrentPreset == 4 && number.GetInt() == 30, "saved preset and missing values load consistently");
    Check(toggle.SingleValue == 1 && choice.SingleValue == 1, "loaded scalar values follow option normalization");
    Check(RPC.SyncCalls == 0, "loading options does not send settings");
    Check(toggle.GetBool(), "entries stored in the wrong option group are ignored");

    OptionSaver.Save();
    var validSave = File.ReadAllText(optionsPath);
    using (File.Open(optionsPath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        number.SetValueNoRpc(1);
        OptionSaver.Save();
    }
    Check(File.ReadAllText(optionsPath) == validSave, "failed save replacement preserves previous settings");
    Check(Directory.GetFiles(Path.GetDirectoryName(optionsPath), "*.tmp").Length == 0, "failed save cleans temporary file");
    OptionSaver.Save();
    Check(JsonSerializer.Deserialize<OptionSaver.SerializableOptionsData>(File.ReadAllText(optionsPath)).PresetOptions[101][4] == 1, "subsequent save recovers and persists edited value");

    Check(new IntegerValueRule(0, 4, 1).RepeatIndex(-2) == 3, "integer negative indices wrap consistently");
    Check(new FloatValueRule(0, 4, 1).RepeatIndex(-2) == 3, "float negative indices wrap consistently");
    var parent = BooleanOptionItem.Create(104, "Parent", true, TabGroup.ModSettings, false).SetGameMode(CustomGameMode.FFA);
    number.SetParent(parent);
    Check(!number.IsHiddenOn(CustomGameMode.FFA), "parent visibility uses requested game mode");

    LateTask.Tasks.Clear();
    var runs = 0;
    _ = new LateTask(() => { runs++; LateTask.Update(0); }, 0, shoudLog: false);
    LateTask.Update(0);
    Check(runs == 1 && LateTask.Tasks.Count == 0, "nested scheduler update cannot repeat callback");
    _ = new LateTask(() => LateTask.Tasks.Clear(), 0, shoudLog: false);
    _ = new LateTask(() => runs++, 0, shoudLog: false);
    LateTask.Update(0);
    Check(runs == 1, "cleared pending tasks do not execute from old snapshot");
    _ = new LateTask(() => { _ = new LateTask(() => runs++, 0, shoudLog: false); }, 0, shoudLog: false);
    LateTask.Update(0);
    Check(runs == 1 && LateTask.Tasks.Count == 1, "new callback tasks wait for next scheduler tick");
    LateTask.Update(0);
    Check(runs == 2 && LateTask.Tasks.Count == 0, "new callback task executes on next tick");
    _ = new LateTask(() => throw new Exception("Expected callback failure"), 0, shoudLog: false);
    LateTask.Update(0);
    Check(LateTask.Tasks.Count == 0, "failed callback is removed");
    Console.WriteLine($"INFRASTRUCTURE_PASS ({assertions} assertions; linked production sources, isolated game/UI stubs)");
}
finally
{
    Directory.SetCurrentDirectory(originalDirectory);
    Directory.Delete(temporaryDirectory, recursive: true);
}
