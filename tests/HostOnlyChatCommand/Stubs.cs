namespace TOHE;

// This suite isolates envelope routing; feature authorization has its own linked-production suite.
internal static class AfkMonitor { public static void RecordActivity(PlayerControl player) => Sink.Activity++; }
internal static class FeatureChatCommands
{
    public static bool TryHandle(PlayerControl player, string text) { Sink.FeatureCalls++; return false; }
}

// Native/network boundary sinks only. Routing and privacy guards are generated
// from production; role outcomes/permissions are deliberately not simulated.
enum RpcCalls : byte { SendChat = 13 }
enum SendOption { None, Reliable }
sealed class MessageWriter
{
    public uint NetId;
    public byte Rpc;
    public SendOption Option;
    public int Target;
    public string Text;
    public void Write(string text) => Text = text;
}
sealed class AmongUsClient
{
    public static AmongUsClient Instance = new();
    public static implicit operator bool(AmongUsClient client) => client != null;
    public bool AmHost = true, AmConnected = true, AmClient = true;
    public int HostId = 42;
    public MessageWriter StartRpcImmediately(uint netId, byte rpc, SendOption option, int target = -1)
        => new() { NetId = netId, Rpc = rpc, Option = option, Target = target };
    public void FinishRpcImmediately(MessageWriter writer) => Sink.Writers.Add(writer);
}
sealed class PlayerControl
{
    public static PlayerControl LocalPlayer = new();
    public static implicit operator bool(PlayerControl player) => player != null;
    public PlayerData Data = new();
    public bool AmOwner = true, Alive = true;
    public byte PlayerId = 1;
    public uint NetId = 200;
    public string name = "player\nname\n";
    public object Role = null;
    public bool IsAlive() => Alive;
    public object GetRoleClass() => Role;
    public string GetRealName() => "player";
    public string GetNameWithRole() => "player-role";
}
sealed class PlayerData { public bool Disconnected; }
static class GameStates
{
    public static bool IsModHost = true, IsInGame = true, IsExilling;
}
sealed class TextArea
{
    public string text = "";
    public void Clear() => text = "";
    public void SetText(string value) => text = value;
}
sealed class ChatField
{
    public bool visible = false;
    public TextArea textArea = new();
    public void Clear() => textArea.Clear();
}
sealed class ChatMenu { public void Clear() { } }
sealed class ChatController
{
    public ChatField quickChatField = new(), freeChatField = new();
    public ChatMenu quickChatMenu = new();
    public float timeSinceLastMessage;
    public void AddChat(PlayerControl player, string text, bool ignored = false) => Sink.Visible.Add(text);
}
sealed class HudManager
{
    public ChatController Chat = new();
    public static implicit operator bool(HudManager manager) => manager != null;
}
static class DestroyableSingleton<T> where T : new() { public static T Instance = new(); }
sealed class UnityTelemetry { public void SendWho() => Sink.Telemetry++; }
static class ChatControllerUpdatePatch { public static int CurrentHistorySelection; }
static class Logger { public static void Info(string text, string scope) { } }
static class Blackmailer { public static bool CheckBlackmaile(PlayerControl player) => false; }
sealed class Option { public bool Enabled; public bool GetBool() => Enabled; }
static class Options { public static Option HideExileChat = new(); }
sealed class LateTask
{
    public LateTask(Action action, float delay, string name) => Sink.Delayed++;
}
static class Utils
{
    public static void SendMessage(string message, byte target = 255, string title = "")
        => Sink.Echoes.Add((message, target));
}
static class SpamManager
{
    public static bool CheckSpam(PlayerControl player, string text) { Sink.Spam++; return false; }
}
static class GuessManager
{
    public static bool GuesserMsg(PlayerControl player, string text)
    {
        Sink.CoreVisits.Add((text, HostOnlyChatCommand.IsActive));
        if (Sink.ThrowCore) throw new InvalidOperationException("fixture core failure");
        if (Sink.HandleRole)
        {
            Effects.HideGuess();
            ChatManager.SendPreviousMessagesToAll();
            Effects.EchoGuessManager0(player, text);
        }
        return Sink.HandleRole;
    }
}
sealed class Judge { public bool TrialMsg(PlayerControl p, string t) => false; }
static class President { public static bool EndMsg(PlayerControl p, string t) => false; }
static class Inspector { public static bool InspectCheckMsg(PlayerControl p, string t) => false; }
static class Pirate { public static bool DuelCheckMsg(PlayerControl p, string t) => false; }
sealed class Councillor { public bool MurderMsg(PlayerControl p, string t) => false; }
sealed class Swapper { public bool SwapMsg(PlayerControl p, string t) => false; }
static class Medium { public static bool MsMsg(PlayerControl p, string t) => false; }
static class Nemesis { public static bool NemesisMsgCheck(PlayerControl p, string t) => false; }
static class Retributionist { public static bool RetributionistMsgCheck(PlayerControl p, string t) => false; }
static class Sink
{
    public static readonly List<MessageWriter> Writers = [];
    public static readonly List<string> Visible = [];
    public static readonly List<(string Text, bool Private)> CoreVisits = [];
    public static readonly List<(string Text, byte Target)> Echoes = [];
    public static readonly List<(string Text, bool Private)> LocalVisits = [];
    public static bool ThrowCore, HandleRole, ThrowLocal, LocalCanceled;
    public static int Spam, Hides, Replays, Delayed, Telemetry, Activity, FeatureCalls;
    public static bool LocalCommand(string text)
    {
        LocalVisits.Add((text, HostOnlyChatCommand.IsActive));
        if (ThrowLocal) throw new InvalidOperationException("fixture local failure");
        ChatManager.SendMessage(PlayerControl.LocalPlayer, text);
        return LocalCanceled;
    }
    public static void Reset()
    {
        if (HostOnlyChatCommand.IsActive) throw new Exception("Leaked scope before reset");
        AmongUsClient.Instance = new();
        PlayerControl.LocalPlayer = new();
        GameStates.IsModHost = true; GameStates.IsInGame = true; GameStates.IsExilling = false;
        Writers.Clear(); Visible.Clear(); CoreVisits.Clear(); Echoes.Clear(); LocalVisits.Clear();
        ThrowCore = HandleRole = ThrowLocal = LocalCanceled = false;
        Spam = Hides = Replays = Delayed = Telemetry = Activity = FeatureCalls = 0;
        Options.HideExileChat.Enabled = false;
        ChatManager.Reset(); ChatCommands.ChatHistory.Clear();
    }
}
