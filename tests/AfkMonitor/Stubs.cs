namespace UnityEngine
{
    static class Time { public static float realtimeSinceStartup; public static float timeScale = 1; }
    public record struct Vector2(float x, float y);
}

namespace TOHE
{
    enum TabGroup { ModSettings }
    enum CustomGameMode { Standard, FFA }
    enum OptionFormat { Seconds, Players }
    record struct IntegerValueRule(int Min, int Max, int Step);
    class OptionItem
    {
        public static readonly Dictionary<int, OptionItem> Items = [];
        public int Value;
        public bool GetBool() => Value != 0;
        public int GetInt() => Value;
        public int GetValue() => Value;
        public OptionItem SetGameMode(CustomGameMode _) => this;
        public OptionItem SetParent(OptionItem _) => this;
        public OptionItem SetHeader(bool _) => this;
        public OptionItem SetValueFormat(OptionFormat _) => this;
        public static OptionItem Add(int id, int value) => Items[id] = new() { Value = value };
    }
    static class BooleanOptionItem
    {
        public static OptionItem Create(int id, string name, bool value, TabGroup tab, bool single) => OptionItem.Add(id, value ? 1 : 0);
    }
    static class IntegerOptionItem
    {
        public static OptionItem Create(int id, string name, IntegerValueRule rule, int value, TabGroup tab, bool single) => OptionItem.Add(id, value);
    }
    static class StringOptionItem
    {
        public static OptionItem Create(int id, string name, string[] values, int value, TabGroup tab, bool single) => OptionItem.Add(id, value);
    }
    static class Options { public static CustomGameMode CurrentGameMode; }
    static class GameStates
    {
        public static bool IsNormalGame = true, IsInTask = true, IsEnded, IsExilling;
    }
    static class AntiBlackout { public static bool SkipTasks; }
    static class OnGameJoinedPatch { public static uint Generation = 1; }
    sealed class GameManager
    {
        public static GameManager Instance = new();
        public nint Pointer = 1;
    }
    sealed class ClientData { public PlayerControl Character; }
    sealed class AmongUsClient
    {
        public static AmongUsClient Instance = new();
        public bool AmConnected = true, AmHost = true;
        public nint Pointer = 1;
        public int GameId = 100, HostId = 0;
        public Dictionary<int, ClientData> Clients = [];
        public List<int> Kicks = [];
        public ClientData FindClientById(int id) => Clients.GetValueOrDefault(id);
        public void KickPlayer(int id, bool ban)
        {
            if (ban) throw new Exception("AFK must never ban");
            Kicks.Add(id);
        }
    }
    public sealed class PlayerData { public bool Disconnected, IsDead; }
    public sealed class TaskState { public int CompletedTasksCount; }
    public sealed class Animations
    {
        public bool Vent, Ladder;
        public bool IsPlayingEnterVentAnimation() => Vent;
        public bool IsPlayingAnyLadderAnimation() => Ladder;
    }
    public sealed class Physics { public Animations Animations = new(); }
    public sealed class PlayerControl(byte id, int owner)
    {
        public byte PlayerId = id;
        public int OwnerId = owner;
        public nint Pointer = id + 1;
        public bool AmOwner, inVent, walkingToVent, onLadder, inMovingPlat;
        public bool moveable = true;
        public PlayerData Data = new();
        public Physics MyPhysics = new();
        public TaskState Tasks = new();
        public UnityEngine.Vector2 Position;
        public int Warnings;
        public bool IsAlive() => !Data.IsDead;
        public UnityEngine.Vector2 GetTruePosition() => Position;
        public TaskState GetPlayerTaskState() => Tasks;
        public void Notify(string text, float time, bool sendInLog) => Warnings++;
    }
    static class Main
    {
        public static object PlayerStates = new();
        public static bool IntroDestroyed = true;
        public static List<PlayerControl> Players = [];
        public static PlayerControl[] AllPlayerControls => Players.ToArray();
        public static PlayerControl[] AllAlivePlayerControls => Players.Where(x => !x.Data.Disconnected && x.IsAlive()).ToArray();
    }
    static class Translator
    {
        public static string GetString(string key) => key == "AfkMonitor.Status" ? "{state}: {seconds}s" : key;
    }
    static class Utils
    {
        public static PlayerControl GetPlayerById(int id) => Main.Players.FirstOrDefault(player => player.PlayerId == id);
    }
}
