namespace Hazel
{
    enum SendOption { Reliable }
    sealed class MessageWriter
    {
        private readonly MemoryStream stream = new();
        private readonly BinaryWriter writer;
        internal MessageWriter() => writer = new(stream);
        public void Write(int value) => writer.Write(value);
        public void Write(uint value) => writer.Write(value);
        public void Write(byte value) => writer.Write(value);
        public void Write(bool value) => writer.Write(value);
        public void Write(string value) => writer.Write(value);
        internal byte[] Bytes() => stream.ToArray();
    }
    sealed class MessageReader
    {
        private readonly MemoryStream stream;
        private readonly BinaryReader reader;
        internal MessageReader(byte[] bytes) { stream = new(bytes); reader = new(stream); }
        public int BytesRemaining => (int)(stream.Length - stream.Position);
        public int ReadInt32() => reader.ReadInt32();
        public uint ReadUInt32() => reader.ReadUInt32();
        public byte ReadByte() => reader.ReadByte();
        public bool ReadBoolean() => reader.ReadBoolean();
        public string ReadString() => reader.ReadString();
    }
}

namespace TOHE
{
    class NativeObject
    {
        public nint Pointer;
        public static implicit operator bool(NativeObject value) => value != null;
    }
    sealed class HarmonyPatchAttribute(Type type, string method) : Attribute;
    sealed class HarmonyArgumentAttribute(int argument) : Attribute;
    enum CustomRPC { SyncExileText = 190 }
    static class CustomRpcTransport
    {
        public static List<byte[]> Payloads = [];
        public static Hazel.MessageWriter Start(CustomRPC rpc, Hazel.SendOption option) => new();
        public static void Finish(Hazel.MessageWriter writer) => Payloads.Add(writer.Bytes());
    }
    sealed class AmongUsClient : NativeObject
    {
        public static AmongUsClient Instance;
        public bool AmConnected = true, AmHost = true;
        public int GameId = 123, HostId = 10;
        public void OnGameJoined() { }
        public void OnDisconnected() { }
    }
    sealed class PlayerControl : NativeObject
    {
        public int OwnerId;
        public bool AmOwner;
        public NetworkedPlayerInfo Data;
        public List<string> SentNames = [];
        public void RpcSetName(string name) { SentNames.Add(name); Data.PlayerName = name; }
    }
    sealed class NetworkedPlayerInfo : NativeObject
    {
        public byte PlayerId;
        public int ClientId;
        public bool Disconnected;
        public string PlayerName;
        public void UpdateName(string name, object owner) => PlayerName = name;
    }
    sealed class PlayerState;
    sealed class MeetingHud : NativeObject
    {
        public static MeetingHud Instance;
        public uint NetId = 55;
        public void Start() { }
        public void Close() { }
    }
    sealed class ExileController : NativeObject
    {
        public string completeString = "native vanilla";
        public Text Text = new(), ImpostorText = new();
        public InitProperties initData = new();
        public void BeginForGameplay() { }
        public void ReEnableGameplay() { }
    }
    sealed class InitProperties { public bool confirmImpostor = true; }
    sealed class Text : NativeObject
    {
        public string text = "";
        public GameObject gameObject = new();
    }
    sealed class GameObject
    {
        public bool activeSelf = true;
        public void SetActive(bool active) => activeSelf = active;
    }
    static class OnGameJoinedPatch { public static uint Generation; }
    static class Main
    {
        public static Dictionary<byte, PlayerState> PlayerStates = [];
        public static Dictionary<int, PlayerVersion> playerVersion = [];
        public static Version version = new(1, 2, 3);
        public static string ForkId = "test-fork";
    }
    sealed class PlayerVersion
    {
        public Version version;
        public string forkId, tag;
    }
    static class ThisAssembly
    {
        public static class Git
        {
            public const string Commit = "abc123", Branch = "test";
        }
    }
    static class Utils
    {
        public static Dictionary<byte, PlayerControl> Players = [];
        public static PlayerControl GetPlayerById(byte id) => Players.GetValueOrDefault(id);
        public static object GetClientById(int id) => new();
    }
}
