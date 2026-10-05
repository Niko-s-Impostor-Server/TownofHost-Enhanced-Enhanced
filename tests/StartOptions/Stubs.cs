using System;
using System.Collections.Generic;
using System.Linq;

namespace AmongUs.GameOptions
{
    enum FloatOptionNames { KillCooldown, ShapeshifterCooldown, GuardianAngelCooldown }
    enum BoolOptionNames { ImpostorsCanSeeProtect }
    enum RoleTypes { Scientist, Engineer, GuardianAngel, Shapeshifter, Noisemaker, Phantom, Tracker }
    interface IGameOptions
    {
        RoleCollection RoleOptions { get; }
        void SetFloat(FloatOptionNames name, float value);
        void SetBool(BoolOptionNames name, bool value);
    }
    sealed class RoleCollection
    {
        internal Dictionary<RoleTypes, (int Count, int Chance)> Rates = [];
        public void SetRoleRate(RoleTypes role, int count, int chance) => Rates[role] = (count, chance);
    }
    sealed class NormalGameOptionsV11 : IGameOptions
    {
        public NormalGameOptionsV11(Hazel.ILogger logger) { }
        public byte MapId;
        public Dictionary<FloatOptionNames, float> Floats = [];
        public float KillCooldown { get => Floats[FloatOptionNames.KillCooldown]; set => Floats[FloatOptionNames.KillCooldown] = value; }
        public RoleCollection RoleOptions { get; } = new();
        public void SetFloat(FloatOptionNames name, float value) => Floats[name] = value;
        public void SetBool(BoolOptionNames name, bool value) { }
        public T Cast<T>() => (T)(object)this;
    }
    sealed class HideNSeekOptions { public byte MapId; public T Cast<T>() => (T)(object)this; }
}
namespace InnerNet
{
    class InnerNetClient { public enum GameStates { Joined, Started } }
}
namespace Il2CppSystem.Collections { class IEnumerator { } }
namespace Hazel { interface ILogger { } }

static class Trace { public static readonly List<string> Events = []; }
sealed class UnityLogger : Hazel.ILogger { public T Cast<T>() => (T)(object)this; }
sealed class WaitForSeconds(float seconds) { public readonly float Seconds = seconds; }
sealed class AmongUsClient
{
    public static AmongUsClient Instance;
    public bool AmHost;
    public InnerNet.InnerNetClient.GameStates GameState;
    public void SendClientReady() => Trace.Events.Add("ready");
}
static class GameStates { public static bool IsModHost = true, IsNormalGame = true, IsFreePlay, IsCountDown; public static bool IsHideNSeek => !IsNormalGame; }
sealed class GameStartManager { public object gameStartSound = new(); }
static class GameStartManagerPatch
{
    public static class GameStartManagerUpdatePatch { public static bool AlredyBegin = true; }
}
sealed class SoundManager
{
    public static SoundManager Instance = new();
    public void StopSound(object sound) { }
}
static class OnGameJoinedPatch
{
    public static uint Generation;
    public static bool IsCurrentSession(uint generation) => Generation == generation;
}
sealed class Flag { public bool Value; public bool GetBool() => Value; }
static class Options
{
    public static Flag NoGameEnd = new(), RandomMapsMode = new(), DisableVanillaRoles = new();
    public static float DefaultKillCooldown;
}
static class CreateOptionsPickerPatch { public static bool SetDleks; }
static class AprilFoolsMode { public static bool IsAprilFoolsModeToggledOn = false; }
static class Translator { public static string GetString(string key) => key; }
// Extracted method uses the original global static translator import.
static class Logger { public static void SendInGame(string value) { } }
static class RPC { public static void RpcVersionCheck() { } }
sealed class GameManager { public static GameManager Instance = new(); }
sealed class Factory
{
    public AmongUs.GameOptions.IGameOptions ToBytes(AmongUs.GameOptions.IGameOptions options, bool april) => options;
}
sealed class GameOptionsManager
{
    public static GameOptionsManager Instance = new();
    public Factory gameOptionsFactory = new();
    public AmongUs.GameOptions.IGameOptions CurrentGameOptions;
}
static class Main
{
    public static AmongUs.GameOptions.NormalGameOptionsV11 NormalOptions;
    public static AmongUs.GameOptions.HideNSeekOptions HideNSeekOptions = new();
    public static OptionBackupData RealOptionsData;
}
sealed class OptionBackupData
{
    public readonly Dictionary<AmongUs.GameOptions.FloatOptionNames, float> Saved;
    private readonly Dictionary<AmongUs.GameOptions.RoleTypes, (int Count, int Chance)> rates;
    public OptionBackupData(AmongUs.GameOptions.NormalGameOptionsV11 original)
    {
        Saved = new(original.Floats);
        rates = new(original.RoleOptions.Rates);
    }
    public AmongUs.GameOptions.IGameOptions Restore(AmongUs.GameOptions.IGameOptions options)
    {
        var normal = (AmongUs.GameOptions.NormalGameOptionsV11)options;
        normal.Floats = new(Saved);
        normal.RoleOptions.Rates = new(rates);
        return options;
    }
}
sealed class PlayerData { public bool Disconnected; }
sealed class PlayerControl
{
    public static PlayerControl LocalPlayer;
    public static List<PlayerControl> AllPlayerControls = [];
    public PlayerData Data = new();
    public bool AmOwner;
    public float LobbySyncKillCooldown;
    public void RpcSyncSettings(AmongUs.GameOptions.IGameOptions options) =>
        LobbySyncKillCooldown = ((AmongUs.GameOptions.NormalGameOptionsV11)options).KillCooldown;
}
class PlayerGameOptionsSender(PlayerControl player)
{
    public virtual AmongUs.GameOptions.IGameOptions BuildGameOptions() => throw new Exception("Role options builder must not run before assignment");
    public void SendGameOptions()
    {
        var clone = (AmongUs.GameOptions.NormalGameOptionsV11)BuildGameOptions();
        Trace.Events.Add(player.AmOwner ? "local-options" : "remote-options");
        if (player.AmOwner) GameOptionsManager.Instance.CurrentGameOptions = clone;
        if (clone.KillCooldown != 0) throw new Exception("Initial clone did not contain zero cooldown");
    }
}
static class Extensions
{
    public static IEnumerable<T> GetFastEnumerator<T>(this List<T> list) => list;
    public static Il2CppSystem.Collections.IEnumerator WrapToIl2Cpp(this System.Collections.IEnumerator value) => new();
}
