using AmongUs.GameOptions;
using Hazel;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object value) => value is not null && !value.Destroyed;
    }
}

namespace AmongUs.GameOptions
{
    public interface ILogger { }
    public interface IGameOptions { byte[] Bytes { get; } bool TryClearAprilFoolsMode(); }
    public enum TaskBarMode { Normal }
    public class NormalGameOptionsV11 : IGameOptions
    {
        public float KillCooldown;
        public byte MapId;
        public int DetectiveRate, ViperRate, JudgeRate;
        public int DiscussionTime, VotingTime, EmergencyCooldown, NumEmergencyMeetings, TaskBarMode;
        public bool VisualTasks, AnonymousVotes, ConfirmImpostor;
        public byte[] Bytes => BitConverter.GetBytes(KillCooldown)
            .Concat(new[] { MapId })
            .Concat(BitConverter.GetBytes(DetectiveRate))
            .Concat(BitConverter.GetBytes(ViperRate))
            .Concat(BitConverter.GetBytes(JudgeRate)).ToArray();
        public bool TryClearAprilFoolsMode() => false;
    }
    public class HideNSeekGameOptionsV11 : IGameOptions
    {
        public byte MapId;
        public byte[] Bytes => new[] { MapId };
        public bool TryClearAprilFoolsMode() => false;
    }
    public class GameOptionsFactory(ILogger logger)
    {
        public static readonly List<IGameOptions> Encoded = [];
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> ToBytes(IGameOptions options, bool april)
        {
            _ = logger;
            Encoded.Add(options);
            return new(options.Bytes);
        }
        public IGameOptions FromNetworkMessageWithSize(MessageReader reader) => reader.Options;
    }
}

namespace Hazel
{
    public enum SendOption { None, Reliable }
    public class MessageReader { public IGameOptions Options; }
    public class MessageWriter
    {
        public readonly List<byte> Tags = [];
        public readonly List<byte[]> OptionBytes = [];
        public bool Recycled;
        public static MessageWriter Get(SendOption option) => new();
        public void StartMessage(byte tag) => Tags.Add(tag);
        public void EndMessage() { }
        public void Write(int value) { }
        public void WritePacked(int value) { }
        public void WritePacked(uint value) { }
        public void WriteBytesAndSize(byte[] bytes) => OptionBytes.Add(bytes.ToArray());
        public void WriteBytesAndSize(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes) => WriteBytesAndSize(bytes.Values);
        public void Recycle() => Recycled = true;
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T>(T[] values) { public T[] Values = values; }
}
namespace Il2CppSystem { }
namespace InnerNet
{
    public static class Tags { public const byte GameData = 5, GameDataTo = 6; }
}

public class UnityLogger : ILogger
{
    public T Cast<T>() => (T)(object)this;
}
public enum RpcCalls { None }
public abstract class NativeObject : UnityEngine.Object
{
    public virtual bool IsDirty => false;
    public virtual bool Serialize(MessageWriter writer, bool initialState) => false;
}
public partial class GameManager : NativeObject
{
    public static GameManager Instance;
    public LogicOptions LogicOptions;
    public uint NetId = 7;
    public List<GameLogicComponent> LogicComponents = [];
}
public class AmongUsClient : UnityEngine.Object
{
    public static AmongUsClient Instance;
    public bool AmConnected = true, AmHost = true;
    public int GameId = 32, LegacyCalls;
    public readonly List<MessageWriter> Sent = [];
    public MessageWriter StartRpcImmediately(uint netId, byte callId, SendOption option)
    {
        if (callId == 2) LegacyCalls++;
        return MessageWriter.Get(option);
    }
    public void FinishRpcImmediately(MessageWriter writer) => Sent.Add(writer);
    public void SendOrDisconnect(MessageWriter writer) => Sent.Add(writer);
}
public class PlayerControl : UnityEngine.Object
{
    public static PlayerControl LocalPlayer = new();
    public int SettingsCalls;
    public void RpcSyncSettings(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes)
    {
        SettingsCalls++;
        if (RpcSyncSettingsPatch.Prefix()) new NativeLegacyRpcFixture().RpcSyncSettings(bytes.Values);
    }
}
public class GameOptionsManager
{
    public static GameOptionsManager Instance = new();
    public GameOptionsFactory gameOptionsFactory = new(new UnityLogger());
    private IGameOptions current;
    public NormalGameOptionsV11 currentNormalGameOptions;
    public HideNSeekGameOptionsV11 currentHideNSeekGameOptions;
    public IGameOptions CurrentGameOptions
    {
        get => current;
        set
        {
            current = value;
            if (value is NormalGameOptionsV11 normal) currentNormalGameOptions = normal;
            if (value is HideNSeekGameOptionsV11 hns) currentHideNSeekGameOptions = hns;
        }
    }
}
public static class AprilFoolsMode { public static bool IsAprilFoolsModeToggledOn; }
public static class GameStates
{
    public static bool IsFreePlay, IsHideNSeek, IsCountDown;
    public static bool IsNormalGame => !IsHideNSeek;
}
public static class Main
{
    public static PlayerControl[] AllPlayerControls = [new(), new()];
    public static NormalGameOptionsV11 NormalOptions => GameOptionsManager.Instance.currentNormalGameOptions;
    public static HideNSeekGameOptionsV11 HideNSeekOptions => GameOptionsManager.Instance.currentHideNSeekGameOptions;
}
public class TutorialManager { }
public static class DestroyableSingleton<T> { public static bool InstanceExists; }
public class GameStartManager { public object gameStartSound = new(); }
public static class GameStartManagerPatch
{
    public static class GameStartManagerUpdatePatch { public static bool AlredyBegin; }
}
public class SoundManager
{
    public static SoundManager Instance = new();
    public int Stops;
    public void StopSound(object sound) => Stops++;
}
public static class Extensions
{
    public static IEnumerable<T> GetFastEnumerator<T>(this IEnumerable<T> source) => source;
    public static bool TryCast<T>(this object source, out T value) where T : class
    {
        value = source as T;
        return value != null;
    }
    public static T Cast<T>(this object source) => (T)source;
}
public static class Logger
{
    public static void Warn(string message, string source) => throw new Exception(source + ": " + message);
    public static void Fatal(string message, string source) => throw new Exception(source + ": " + message);
}
public static class RPC
{
    public static int Syncs;
    public static void SyncCustomSettingsRPC(int targetId) => Syncs++;
}

// This non-options component proves the real sender uses the correct component
// index and clears only option dirt, leaving the native stream's other work.
public class OtherLogic(GameManager manager) : GameLogicComponent(manager)
{
    public int Serializations;
    public override void OnGameStart() { }
    public override void OnGameEnd() { }
    public override void FixedUpdate() { }
    public override void OnDestroy() { }
    public override bool Serialize(MessageWriter writer) { Serializations++; return true; }
    public override void Deserialize(MessageReader reader) { }
}

// HnS serialization uses the same extracted native base implementation. Native
// seeker selection/gameplay is deliberately outside this lobby test.
public class HnSLogic(GameManager manager) : LogicOptions(manager)
{
    private IGameOptions options = GameOptionsManager.Instance.CurrentGameOptions;
    protected override IGameOptions currentGameOptions => options;
    protected override void SetGameOptions(IGameOptions value) { options = value; SetDirty(); }
    public override void OnGameStart() { }
    public override void OnGameEnd() { }
    public override void FixedUpdate() { }
    public override void OnDestroy() { }
}
