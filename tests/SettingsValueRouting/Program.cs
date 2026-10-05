using TOHE;

int assertions = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); assertions++; }
void Reset(OptionBehaviour row, OptionItem item)
{
    ModGameOptionsMenu.OptionList.Clear();
    OptionItem.AllOptions = [item];
    ModGameOptionsMenu.OptionList.Add(row, 0);
    NotificationPopperPatch.Calls = GameOptionsMenuPatch.ReopenCalls = 0;
    GameOptionsMenuPatch.CanEdit = true;
    Input.Shift = Input.Control = false;
}

foreach (bool initial in new[] { false, true })
{
    var row = new ToggleOption(); row.CheckMark.enabled = initial;
    var item = new OptionItem { CurrentValue = initial ? 1 : 0 };
    Reset(row, item); int callbacks = 0; row.OnValueChanged = _ => callbacks++;
    Check(!ToggleOptionPatch.TogglePrefix(row), "mod toggle owns the native action before its inlined update");
    Check(item.CurrentValue == (initial ? 0 : 1) && item.SetterCalls == 1 && callbacks == 1 && NotificationPopperPatch.Calls == 1,
        "toggle changes actual item and notifies once");
    GameOptionsMenuPatch.CanEdit = false;
    Check(!ToggleOptionPatch.TogglePrefix(row) && item.SetterCalls == 1 && callbacks == 1 && row.GetBool() == !initial,
        "nonhost toggle has no local or item mutation");
    ModGameOptionsMenu.OptionList.Clear();
    Check(ToggleOptionPatch.TogglePrefix(row), "unregistered toggle retains native original");
}

foreach (bool floating in new[] { false, true })
foreach (bool increase in new[] { false, true })
foreach (bool boundary in new[] { false, true })
{
    float step = floating ? 0.5f : 2;
    var row = new NumberOption { Value = boundary ? increase ? 10 : 0 : 4, Increment = step, ValidRange = new(0, 10) };
    OptionItem item = floating ? new FloatOptionItem() : new IntegerOptionItem();
    item.CurrentValue = (int)(row.Value / step);
    Reset(row, item); int callbacks = 0; row.OnValueChanged = _ => callbacks++;
    float before = row.Value, expected = boundary ? increase ? 0 : 10 : before + (increase ? step : -step);
    bool allowNative = increase ? NumberOptionPatch.IncreasePrefix(row) : NumberOptionPatch.DecreasePrefix(row);
    Check(!allowNative && row.NativeUpdateCalls == 0, "mod number avoids every native UpdateValue path including bounds");
    Check(row.Value == expected && item.GetFloat() == expected && item.SetterCalls == 1 && callbacks == 1 && NotificationPopperPatch.Calls == 1,
        "numeric UI and item agree and callback/notification dispatch once");
    GameOptionsMenuPatch.CanEdit = false;
    Check(!(increase ? NumberOptionPatch.IncreasePrefix(row) : NumberOptionPatch.DecreasePrefix(row)) &&
        row.Value == expected && item.SetterCalls == 1 && callbacks == 1, "nonhost number does not mutate");
}

foreach (bool increase in new[] { false, true })
foreach (int initial in new[] { 0, 1, 2 })
{
    var row = new StringOption { Value = initial };
    var item = new StringOptionItem { CurrentValue = initial };
    Reset(row, item); int callbacks = 0; row.OnValueChanged = _ => callbacks++;
    int expected = (initial + (increase ? 1 : 2)) % 3;
    Check(!(increase ? StringOptionPatch.IncreasePrefix(row) : StringOptionPatch.DecreasePrefix(row)) && row.NativeUpdateCalls == 0,
        "mod string owns ordinary and wrap native actions");
    Check(row.Value == expected && item.CurrentValue == expected && item.SetterCalls == 1 && callbacks == 1 && NotificationPopperPatch.Calls == 1,
        "string UI and item agree with one callback/notification");
    GameOptionsMenuPatch.CanEdit = false;
    Check(!(increase ? StringOptionPatch.IncreasePrefix(row) : StringOptionPatch.DecreasePrefix(row)) &&
        row.Value == expected && item.SetterCalls == 1 && callbacks == 1, "nonhost string does not mutate");
}

var nativeNumber = new NumberOption { Value = 4, Increment = 2, ValidRange = new(0, 10) };
nativeNumber.OnValueChanged = _ => { };
Reset(nativeNumber, new IntegerOptionItem()); ModGameOptionsMenu.OptionList.Clear();
Check(!NumberOptionPatch.IncreasePrefix(nativeNumber) && nativeNumber.Value == 6 && nativeNumber.NativeUpdateCalls == 1,
    "vanilla numeric augmentation still calls its native update once");
var nativeString = new StringOption { Value = 1 };
Check(StringOptionPatch.IncreasePrefix(nativeString) && StringOptionPatch.DecreasePrefix(nativeString) && nativeString.NativeUpdateCalls == 0,
    "ordinary vanilla string actions fall through unchanged");
nativeString.Value = 2;
Check(!StringOptionPatch.IncreasePrefix(nativeString) && nativeString.Value == 0 && nativeString.NativeUpdateCalls == 1,
    "vanilla string endpoint wrapping retains its existing native update");
var presetRow = new StringOption { Value = 1 };
var preset = new PresetOptionItem { Name = "Preset", CurrentValue = 1 };
Reset(presetRow, preset);
Check(!StringOptionPatch.IncreasePrefix(presetRow) && preset.CurrentValue == 2 && GameOptionsMenuPatch.ReopenCalls == 1,
    "preset retains its production reopen side effect");
foreach (var scenario in new (bool Increase, float Before, bool Shift, bool Control, float Expected)[]
{
    (true, 4, true, false, 14), (false, 16, true, false, 6),
    (true, 16, true, false, 18), (false, 4, true, false, 2),
    (true, 4, false, true, 6), (false, 16, false, true, 14),
})
{
    var row = new NumberOption { Value = scenario.Before, Increment = 2, ValidRange = new(0, 20) };
    var item = new IntegerOptionItem { CurrentValue = (int)(scenario.Before / 2) };
    Reset(row, item); Input.Shift = scenario.Shift; Input.Control = scenario.Control;
    Check(!(scenario.Increase ? NumberOptionPatch.IncreasePrefix(row) : NumberOptionPatch.DecreasePrefix(row)) &&
        row.Value == scenario.Expected && item.GetFloat() == scenario.Expected && row.NativeUpdateCalls == 0 && item.SetterCalls == 1,
        "numeric modifier and near-endpoint behavior retain existing semantics without native writes");
}
Console.WriteLine($"SETTINGS_VALUE_ROUTING_PASS ({assertions} assertions; extracted production click prefixes and update bodies; native option writes/rendering/network stubbed)");
