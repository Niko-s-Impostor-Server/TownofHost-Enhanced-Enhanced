namespace TOHE;

public class OptionBehaviour { public Action<OptionBehaviour> OnValueChanged; public int NativeUpdateCalls; }
public class ToggleOption : OptionBehaviour { public Renderer CheckMark = new(); public bool GetBool() => CheckMark.enabled; }
public class NumberOption : OptionBehaviour
{
    public float Value, Increment;
    public FloatRange ValidRange;
    public int GetInt() => (int)Value;
    public float GetFloat() => Value;
    public void UpdateValue() => NativeUpdateCalls++;
}
public class StringOption : OptionBehaviour
{
    public int Value;
    public int[] Values = [0, 1, 2];
    public int GetInt() => Value;
    public void UpdateValue() => NativeUpdateCalls++;
}
public class Renderer { public bool enabled; }
public struct FloatRange(float minimum, float maximum)
{
    public float min = minimum, max = maximum;
    public float Clamp(float value) => Math.Clamp(value, min, max);
}
public class OptionItem
{
    public static List<OptionItem> AllOptions = new();
    public int CurrentValue, SetterCalls;
    public string Name = "Ordinary";
    public virtual void SetValue(int value) { CurrentValue = value; SetterCalls++; }
    public bool GetBool() => CurrentValue != 0;
    public int GetInt() => CurrentValue;
    public virtual float GetFloat() => CurrentValue;
}
public class IntegerOptionItem : OptionItem
{
    public ValueRule Rule = new(2);
    public override float GetFloat() => CurrentValue * Rule.Step;
}
public class FloatOptionItem : OptionItem
{
    public ValueRule Rule = new(0.5f);
    public override float GetFloat() => CurrentValue * Rule.Step;
}
public class ValueRule(float step)
{
    public float Step = step;
    public int GetNearestIndex(float value) => (int)Math.Round(value / Step);
}
public class StringOptionItem : OptionItem { }
public class PresetOptionItem : OptionItem { }
public static class ModGameOptionsMenu { public static Dictionary<OptionBehaviour, int> OptionList = new(); }
public static class GameOptionsMenuPatch
{
    public static bool CanEdit = true;
    public static int ReopenCalls;
    public static void ReOpenSettings(int tab) => ReopenCalls++;
}
public static class NotificationPopperPatch
{
    public static int Calls;
    public static void AddSettingsChangeMessage(int index, OptionItem item, bool sound) => Calls++;
}
public static class Options { public static OptionItem GameMode = new(); }
public static class GameStates { public static bool IsHideNSeek; }
public enum KeyCode { LeftShift, RightShift, LeftControl, RightControl }
public static class Input
{
    public static bool Shift, Control;
    public static bool GetKey(KeyCode code) => code is KeyCode.LeftShift or KeyCode.RightShift ? Shift : Control;
}
