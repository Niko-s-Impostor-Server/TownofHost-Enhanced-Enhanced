using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    static class Time { public static float realtimeSinceStartup; }
    struct Color { public static Color white => default; }
}

enum SendOption { Reliable }
enum DisconnectReasons { Error }

sealed class CoroutineRunner
{
    readonly List<IEnumerator> active = [];
    public int Count => active.Count;
    public void StartCoroutine(IEnumerator coroutine)
    {
        if (coroutine.MoveNext()) active.Add(coroutine);
        else (coroutine as IDisposable)?.Dispose();
    }
    public void Tick()
    {
        foreach (var coroutine in active.ToArray())
        {
            if (coroutine.MoveNext()) continue;
            active.Remove(coroutine);
            (coroutine as IDisposable)?.Dispose();
        }
    }
    public void Clear()
    {
        foreach (var coroutine in active) (coroutine as IDisposable)?.Dispose();
        active.Clear();
    }
}

sealed class ClientData
{
    public int Id;
    public IntPtr Pointer;
}

sealed class PlayerControl
{
    public static PlayerControl LocalPlayer;
    public static readonly List<PlayerControl> AllPlayerControls = [];
    public int ClientId;
    public uint NetId;
    public byte PlayerId;
    public bool AmOwner = true;
    public bool MushroomMixup;
    public PlayerData Data = new();
    public RoleBase Role = new UnShapeShiftRole();
    public Cosmetics cosmetics = new();
    public PlayerOutfitType CurrentOutfitType;
    public readonly List<byte> ShiftTargets = [];
    public int Rejects, OutfitResets;
    public int GetClientId() => ClientId;
    public bool HasDesyncRole() => false;
    public RoleBase GetRoleClass() => Role;
    public bool IsMushroomMixupActive() => MushroomMixup;
    public string GetRealName() => "fixture";
    public void RpcShapeshift(PlayerControl target, bool shouldAnimate)
    {
        if (target == null) throw new Exception("Null shapeshift target");
        ShiftTargets.Add(target.PlayerId);
        CurrentOutfitType = PlayerOutfitType.Shapeshifted;
    }
    public void RpcRejectShapeshift() => Rejects++;
    public void ResetPlayerOutfit(bool force = false, bool setNamePlate = false) => OutfitResets++;
}
enum PlayerOutfitType { Default, Shapeshifted }
class RoleBase { public virtual void UnShapeShiftButton(PlayerControl player) { } }
sealed class UnShapeShiftRole : RoleBase { public override void UnShapeShiftButton(PlayerControl player) { } }
sealed class NativeRole { public bool AffectedByLightAffectors, CanBeKilled; public UnityEngine.Color NameColor; }
sealed class PlayerData { public bool Disconnected; public NativeRole Role = new(); }
sealed class Cosmetics { public void SetNameColor(UnityEngine.Color color) { } }
sealed class GameManager { public static GameManager Instance; }
static class GameStates { public static bool IsInGame, IsInTask, IsLobby; }
static class AntiBlackout { public static bool SkipTasks; }
static class PlayerExtensions
{
    public static IEnumerable<PlayerControl> GetFastEnumerator(this List<PlayerControl> players) => players;
    public static PlayerControl GetPlayer(this byte id) => Utils.GetPlayerById(id);
}
sealed class LateTask
{
    public static readonly List<Action> Pending = [];
    public LateTask(Action action, float seconds, string name) => Pending.Add(action);
    public static void RunAll()
    {
        var pending = Pending.ToArray();
        Pending.Clear();
        foreach (var action in pending) action();
    }
}

sealed class MessageWriter
{
    public uint NetId;
    public byte CallId;
    public int TargetId;
    public SendOption SendOption;
    public readonly List<object> Values = [];
    public void Write(string value) => Values.Add(value);
    public void Write(bool value) => Values.Add(value);
}

sealed class AmongUsClient
{
    public static AmongUsClient Instance;
    public int HostId;
    public int GameId = 100;
    public bool AmHost = true;
    public bool IsGameOver;
    public bool AmConnected;
    public int Initializations;
    public int Exits;
    public bool FailFinish;
    public readonly Dictionary<int, ClientData> Clients = [];
    public readonly List<MessageWriter> Started = [];
    public readonly List<MessageWriter> Finished = [];
    public ClientData GetHost() => Clients.GetValueOrDefault(HostId);
    public MessageWriter StartRpcImmediately(uint netId, byte callId, SendOption option, int targetId = -1)
    {
        var writer = new MessageWriter { NetId = netId, CallId = callId, TargetId = targetId, SendOption = option };
        Started.Add(writer);
        return writer;
    }
    public void FinishRpcImmediately(MessageWriter writer)
    {
        if (FailFinish) throw new InvalidOperationException("Injected writer failure");
        Finished.Add(writer);
    }
    public void ExitGame(DisconnectReasons reason) { Exits++; AmConnected = false; }
}

sealed class PlayerVersion
{
    public Version version;
    public string tag;
    public string forkId;
    public PlayerVersion(string value, string tagValue, string forkValue)
    {
        version = Version.Parse(value); tag = tagValue; forkId = forkValue;
    }
}

sealed class BoolConfig { public bool Value; }
static class Main
{
    public static readonly CoroutineRunner Instance = new();
    public static readonly Dictionary<int, PlayerVersion> playerVersion = [];
    public static readonly BoolConfig VersionCheat = new();
    public static readonly HashSet<byte> UnShapeShifter = [];
    public static Dictionary<byte, object> PlayerStates = [];
    public static readonly Dictionary<byte, bool> CheckShapeshift = [];
    public static bool GameIsLoaded;
    public static PlayerControl[] AllPlayerControls => PlayerControl.AllPlayerControls.ToArray();
    public const string PluginVersion = "2026.8.18";
    public const string ForkId = "offline-test";
}
static class Options { public static bool IsLoaded; }
static partial class Utils
{
    public static ClientData GetClientById(int id) => AmongUsClient.Instance?.Clients.GetValueOrDefault(id);
    public static PlayerControl GetPlayerById(byte id) => PlayerControl.AllPlayerControls.Find(p => p.PlayerId == id);
    public static void NotifyRoles(PlayerControl SpecifyTarget) { }
}
static class ThisAssembly
{
    public static class Git { public const string Commit = "fixture"; public const string Branch = "fixture"; }
}
static class Logger
{
    public static readonly List<string> Warnings = [];
    public static readonly List<string> Errors = [];
    public static void Warn(string message, string category) => Warnings.Add(message);
    public static void Error(string message, string category) => Errors.Add(message);
    public static void Info(string message, string category) { }
}
