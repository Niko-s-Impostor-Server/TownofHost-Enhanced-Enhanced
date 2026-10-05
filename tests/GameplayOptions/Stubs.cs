using System.Text;

namespace TOHE;

enum CustomRoles
{
    NotAssigned, LastImpostor, Crewmate, Overclocked, Watcher, Madmate, Recruit,
    Charmed, Soulless, Infected, Contagious, Admired, Jester, Sunnyboy, Arrogance, Bard
}
enum CustomGameMode { Standard, FFA }
enum Platforms { Standalone, Playstation, Xbox, Switch }
record struct Color(string Name) { public static readonly Color white = new("white"); }
record struct Color32(byte R, byte G, byte B, byte A)
{
    public static implicit operator Color(Color32 _) => Color.white;
}
sealed class OptionItem
{
    public int Value;
    public float FloatValue;
    public bool GetBool() => Value != 0;
    public int GetValue() => Value;
    public float GetFloat() => FloatValue;
}
static partial class Options
{
    public static OptionItem FixFirstKillCooldown = new() { Value = 1 };
    public static OptionItem ChangeFirstKillCooldown = new() { Value = 1 };
    public static OptionItem FixKillCooldownValue = new() { FloatValue = 15 };
    public static OptionItem DisableHiddenRoles = new();
    public static OptionItem ShowShortNamesForAddOns = new();
    public static OptionItem NameDisplayAddons = new() { Value = 1 };
    public static OptionItem AddBracketsToAddons = new() { Value = 1 };
    public static CustomGameMode CurrentGameMode;
}
sealed class PlayerState
{
    public CustomRoles MainRole = CustomRoles.Crewmate;
    public List<CustomRoles> SubRoles = [];
}
sealed class ClientData
{
    public PlatformData PlatformData = new();
}
sealed class PlatformData { public Platforms Platform; }
sealed class PlayerControl(byte id)
{
    public byte PlayerId = id;
    public ClientData Client = new();
    public float RoleCooldown = 30;
    public float? StartingCooldown;
    public int Resets;
    public HashSet<CustomRoles> HiddenSubRoles = [];
    public ClientData GetClient() => Client;
    public bool ShowSubRoleTarget(PlayerControl target, CustomRoles role) => !HiddenSubRoles.Contains(role);
    public void ResetKillCooldown() { Resets++; Main.AllPlayerKillCooldown[PlayerId] = RoleCooldown; }
    public void SetKillCooldown(float value) => StartingCooldown = value;
}
static class Main
{
    public static readonly Dictionary<byte, PlayerState> PlayerStates = [];
    public static readonly Dictionary<byte, float> AllPlayerKillCooldown = [];
    public static readonly Dictionary<byte, PlayerControl> Players = [];
}
static class GameStates
{
    public static bool IsMeeting;
    public static bool IsHideNSeek;
}
static class DollMaster
{
    public static bool IsControllingPlayer;
    public static PlayerControl DollMasterTarget;
    public static PlayerControl controllingTarget;
}
static class LastImpostor { public static byte currentId = byte.MaxValue; }
static class Translator
{
    public static readonly Dictionary<string, string> Translations = [];
    public static string GetString(string key) => Translations.TryGetValue(key, out var value) ? value : $"<INVALID:{key}>";
    public static string GetRoleString(string key) => GetString(key);
}
static partial class Utils
{
    public static string GetRoleName(CustomRoles role, bool forUser = true) => Translator.GetString(role.ToString());
    public static Color GetRoleColor(CustomRoles role) => new(role.ToString());
    public static PlayerControl GetPlayerById(byte id) => Main.Players.GetValueOrDefault(id);
    public static string ColorString(Color color, string text) => $"<color={color.Name}>{text}</color>";
    public static string ColorStringWithoutEnding(Color color, string text) => $"<color={color.Name}>{text}";
}
static partial class ExtendedPlayerControl
{
    public static bool ShouldBeDisplayed(this CustomRoles role) => role != CustomRoles.NotAssigned;
}
sealed class LateTask
{
    public static readonly List<Action> Pending = [];
    public LateTask(Action callback, float delay, string name) => Pending.Add(callback);
    public static void Run() { foreach (var callback in Pending.ToArray()) callback(); Pending.Clear(); }
}
static class Sunnyboy
{
    public static int Rolls;
    public static bool Spawn = true;
    public static bool CheckSpawn() { Rolls++; return Spawn; }
}
static class Bard
{
    public static int Rolls;
    public static bool Spawn = true;
    public static bool CheckSpawn() { Rolls++; return Spawn; }
}
