namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object value) => value != null;
    }
}

public sealed class AmongUsClient : UnityEngine.Object
{
    public static AmongUsClient Instance { get; set; } = new();
    public bool AmHost { get; set; } = true;
    public bool AmConnected { get; set; } = true;
    public int HostId { get; set; } = 10;
    public void KickPlayer(int owner, bool ban) { TOHE.Effects.Kicks++; TOHE.Effects.LastKick = (owner, ban); }
}

public sealed class PlayerControl : UnityEngine.Object
{
    public PlayerData Data { get; set; } = new();
    public string FriendCode { get; set; } = "forged#1234";
    public int OwnerId { get; set; }
    public byte PlayerId { get; set; }
    public bool AmOwner { get; set; }
    public int DeathReasonCalls, KillerCalls, ExileCalls;
    public PlayerControl LastKiller;
    public string DisplayName { get; set; } = "<b>Player</b>";
    // Match the real helper: disconnected/null Data is not filtered by IsAlive.
    public bool IsAlive() => !TOHE.Main.PlayerStates.TryGetValue(PlayerId, out var state) || !state.IsDead;
    public void SetDeathReason(TOHE.PlayerState.DeathReason _) { DeathReasonCalls++; TOHE.Effects.DeathReason++; }
    public void SetRealKiller(PlayerControl player) { KillerCalls++; LastKiller = player; TOHE.Effects.Killer++; }
    public void RpcExileV2() { ExileCalls++; TOHE.Effects.Exiles++; }
    public string GetRealName() => DisplayName;
}

public sealed class PlayerData
{
    public string FriendCode { get; set; } = "unlisted#1234";
    public bool Disconnected { get; set; }
    public bool IsDead { get; set; }
}

public sealed class GameManager : UnityEngine.Object
{
    public static GameManager Instance { get; set; } = new();
    public Flow LogicFlow { get; set; } = new();
    public sealed class Flow { public void CheckEndCriteria() => TOHE.Effects.EndChecks++; }
}

namespace TOHE
{
    public static class Effects
    {
        public static int Saves, Loads, Kicks, Winner, EndChecks, DeathReason, Killer, Exiles, AfterDeath, Afk, Recovery, Suffix;
        public static (int Owner, bool Ban) LastKick;
        public static string Snapshot() => string.Join(",", Saves, Loads, Kicks, Winner, EndChecks, DeathReason, Killer, Exiles, AfterDeath, Afk, Recovery, Suffix);
        public static void Reset() { Saves = Loads = Kicks = Winner = EndChecks = DeathReason = Killer = Exiles = AfterDeath = Afk = Recovery = Suffix = 0; }
    }
    public static class Options { public static bool IsLoaded = true; }
    public static class GameStates { public static bool IsLobby, IsInGame = true, IsMeeting; }
    public static class Main
    {
        public static List<PlayerControl> AllPlayerControls = [];
        public static Dictionary<byte, PlayerState> PlayerStates = [];
    }
    public sealed class PlayerState
    {
        public enum DeathReason { etc }
        public bool IsDead;
        public void SetDead() => IsDead = true;
    }
    public enum CustomWinner { Draw }
    public static class CustomWinnerHolder { public static void ResetAndSetWinner(CustomWinner _) => Effects.Winner++; }
    public static class Translator
    {
        public static string GetString(string key) => key switch
        {
            "PresetSharingSaved" or "PresetSharingLoaded" => key + " {0} {1}",
            "Message.Executed" => key + " {0}",
            _ => key
        };
    }
    public static class Logger { public static int Warnings; public static void Warn(string _, string __) => Warnings++; }
    public static class Utils
    {
        public static List<(string Message, byte Recipient, string Title)> Messages = [];
        public static HashSet<string> LegacyModerators = [];
        public static void SendMessage(string text, byte sendTo = 255, string title = null, bool noReplay = false) => Messages.Add((text, sendTo, title));
        public static PlayerControl GetPlayerById(byte id) => Main.AllPlayerControls.Find(player => player.PlayerId == id);
        public static bool IsPlayerModerator(string code) => LegacyModerators.Contains(code);
        public static string RemoveHtmlTags(this string text) => System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        public static void ApplySuffix(PlayerControl _) => Effects.Suffix++;
    }
    public static class AfkMonitor
    {
        public static string GetStatus(PlayerControl player = null) => "status";
        public static void SetExempt(PlayerControl _, bool __) => Effects.Afk++;
    }
    public static class PresentationRecovery
    {
        public static string TryRequest(PlayerControl _) { Effects.Recovery++; return "RecoveryCompleted"; }
    }
}

namespace TOHE.Patches
{
    public static class OnGameJoinedPatch { public static uint Generation { get; set; } = 1; }
    public static class MurderPlayerPatch
    {
        public static void AfterPlayerDeathTasks(PlayerControl _, PlayerControl __, bool ___) => TOHE.Effects.AfterDeath++;
    }
}

namespace TOHE.Modules
{
    public static class PresetSharing
    {
        public record Result(bool Success = true, string FileName = "preset.json", int OptionCount = 2, string TranslationKey = "error");
        public static Result Save(string name) { TOHE.Effects.Saves++; File.WriteAllText("preset-write-sentinel", name); return new(); }
        public static Result Load(string name) { TOHE.Effects.Loads++; File.WriteAllText("preset-load-sentinel", name); return new(); }
    }
}
