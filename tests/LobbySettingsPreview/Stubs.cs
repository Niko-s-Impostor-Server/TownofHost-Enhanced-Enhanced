namespace UnityEngine
{
    public struct Color { public static Color white => new(); public Color ShadeColor(float value) => this; }
    public static class Time { public static float unscaledTime; }
}
public class AmongUsClient { public static AmongUsClient Instance = new(); public bool AmHost = true; }
public class GameData { public static GameData Instance = new(); public int PlayerCount = 10; public static implicit operator bool(GameData data) => data != null; }
public class GameOptionsManager
{
    public static GameOptionsManager Instance = new();
    public GameOptions CurrentGameOptions = new();
}
public class GameOptions { public string Value = "Vanilla"; public string ToHudString(int count) => Value + count; }
public class TranslationController
{
    public static bool InstanceExists = true;
    public static TranslationController Instance = new();
    public Language currentLanguage = new();
}
public class Language { public int languageID; }
namespace TOHE
{
    using UnityEngine;
    public enum CustomGameMode { Standard, All, FFA }
    public enum CustomRoles { Lovers, Crewmate, Addon }
    public class Config { public bool Value; }
    public static class Main
    {
        public static Config ForceOwnLanguage = new(), ForceOwnLanguageRoleName = new();
    }
    public class OptionItem
    {
        public static List<OptionItem> AllOptions = [];
        public static int CurrentPreset;
        public int CurrentValue;
        public int Id = 60001;
        public string Name = "Setting";
        public CustomGameMode GameMode = CustomGameMode.All;
        public bool IsText, Hidden;
        public OptionItem Parent;
        public List<OptionItem> Children = [];
        public OptionItem() => AllOptions.Add(this);
        public bool GetBool() => CurrentValue != 0;
        public int GetInt() => CurrentValue;
        public float GetFloat() => CurrentValue;
        public string GetString() => CurrentValue.ToString();
        public string GetName() => Name;
        public bool IsHiddenOn(CustomGameMode mode) => Hidden;
    }
    public static class Options
    {
        public static OptionItem HideGameSettings = new() { Name = "Hide" };
        public static OptionItem GameMode = new() { Name = "Mode" };
        public static OptionItem LoverSpawnChances = new() { Name = "Lovers" };
        public static CustomGameMode CurrentGameMode = CustomGameMode.Standard;
        public static Dictionary<CustomRoles, OptionItem> CustomRoleSpawnChances = [];
        public static Dictionary<CustomRoles, OptionItem> CustomAdtRoleSpawnRate = [];
    }
    public static class Translator { public static string GetString(string value) => value; }
    public static class Utils
    {
        public static string GetChance(float value) => value.ToString();
        public static Color GetRoleColor(CustomRoles role) => new();
        public static string GetRoleName(CustomRoles role) => role.ToString();
        public static string ColorString(Color color, string value) => value;
    }
    public static class RoleExtensions
    {
        public static bool IsAdditionRole(this CustomRoles role) => role == CustomRoles.Addon;
        public static int GetCount(this CustomRoles role) => 1;
        public static bool IsEnable(this CustomRoles role) => true;
    }
}
