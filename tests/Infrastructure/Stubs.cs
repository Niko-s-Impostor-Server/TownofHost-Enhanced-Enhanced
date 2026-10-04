namespace UnityEngine
{
    public struct Color { public static Color white => default; }
}

namespace TOHE
{
    public enum CustomGameMode { All, Standard, FFA, HidenSeekTOHE }
    public enum CustomRoles { Example }
    public static class Options
    {
        public enum SpawnChance { Zero, Five, Ten }
        public static Dictionary<CustomRoles, OptionItem> CustomRoleSpawnChances = [];
        public static CustomGameMode CurrentGameMode = CustomGameMode.Standard;
    }
    public static class EnumHelper
    {
        public static string[] GetAllNames<T>() where T : Enum => Enum.GetNames(typeof(T));
    }
    public static class Translator
    {
        public static string GetString(string name, Dictionary<string, string> replacements = null, bool console = false, bool vanilla = false) => name;
    }
    public static class Utils
    {
        public static string ColorString(UnityEngine.Color color, string text) => text;
        public static UnityEngine.Color GetRoleColor(CustomRoles role) => default;
    }
    public static class Logger
    {
        public static List<string> Errors = [];
        public static void Info(string message, string tag) { }
        public static void Warn(string message, string tag) { }
        public static void Error(string message, string tag, bool show = true) => Errors.Add(message);
        public static void Exception(Exception exception, string tag) => Errors.Add(exception.Message);
    }
    public class PresetName
    {
        public string Value = "Default";
        public object DefaultValue = "Default";
    }
    public static class Main
    {
        public static object[] AllPlayerControls = [new(), new()];
        public static PresetName Preset1 = new(), Preset2 = new(), Preset3 = new(), Preset4 = new(), Preset5 = new();
    }
    public static class RPC
    {
        public static int SyncCalls;
        public static int SyncedPreset;
        public static void SyncCustomSettingsRPC(int targetId)
        {
            SyncCalls++;
            SyncedPreset = OptionItem.CurrentPreset;
        }
    }
}

public class AmongUsClient
{
    public static AmongUsClient Instance = new();
    public bool AmHost = true;
}
public class PlayerControl { public static PlayerControl LocalPlayer = new(); }
public class OptionBehaviour { }
public class StringOption : OptionBehaviour
{
    public class Text { public string text; }
    public Text TitleText = new(), ValueText = new();
    public int oldValue, Value;
}
