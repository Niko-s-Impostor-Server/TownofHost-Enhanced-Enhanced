using TOHE;
using System.Reflection;

int checks = 0;
void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new Exception($"FAIL: {name}");
}
void Throws(Action action, string name)
{
    bool thrown = false;
    try { action(); } catch (InvalidOperationException) { thrown = true; }
    Check(thrown, name);
}

foreach (var (text, expected) in new (string, string)[]
{
    ("/cmd help", "/help"), ("/cmd /help", "/help"),
    ("/cmd\tguess\t1 Crewmate", "/guess 1 Crewmate"),
    ("/cmd  /guess   1 Crewmate  ", "/guess 1 Crewmate"),
    ("/cmd 未知参数", "/未知参数"), ("/cmd /unknown", "/unknown"),
})
{
    Check(HostOnlyChatCommand.IsEnvelope(text), $"envelope {text}");
    Check(HostOnlyChatCommand.TryGetCommand(text, out var command) && command == expected, $"parse {text}");
}
foreach (string text in new[] { "/cmd", "/cmd ", "/cmd /", "/cmdfoo help", "/cmd\nhelp", "/cmd help\r", "/cmd help\n/x" })
{
    Check(HostOnlyChatCommand.IsEnvelope(text), $"malformed consumed {text}");
    Check(!HostOnlyChatCommand.TryGetCommand(text, out var command) && command == "", $"malformed rejected {text}");
}
foreach (string text in new string[] { null, "", " /cmd help", "\n/cmd help", "/CMD help", "/Cmd help", "/help", "chat" })
{
    Check(!HostOnlyChatCommand.IsEnvelope(text), $"ordinary envelope {text}");
    Check(!HostOnlyChatCommand.TryGetCommand(text, out _), $"ordinary parser {text}");
}

Check(!HostOnlyChatCommand.IsActive, "initial scope inactive");
using (HostOnlyChatCommand.Enter())
{
    Check(HostOnlyChatCommand.IsActive, "outer scope");
    var inner = HostOnlyChatCommand.Enter();
    inner.Dispose(); inner.Dispose();
    Check(HostOnlyChatCommand.IsActive, "idempotent inner dispose preserves outer");
    bool otherThreadActive = true;
    var thread = new Thread(() => otherThreadActive = HostOnlyChatCommand.IsActive);
    thread.Start(); thread.Join();
    Check(!otherThreadActive, "scope isolated by thread");
}
Check(!HostOnlyChatCommand.IsActive, "nested cleanup");
Throws(() => { using var scope = HostOnlyChatCommand.Enter(); throw new InvalidOperationException(); }, "exception raised");
Check(!HostOnlyChatCommand.IsActive, "exception scope cleanup");

foreach (string text in new[] { "/cmd", "/cmdfoo guess", "/cmd guess\n1", "/cmd /" })
{
    Sink.Reset(); ChatCommands.OnReceiveChat(PlayerControl.LocalPlayer, text, out bool canceled);
    Check(canceled && Sink.CoreVisits.Count == 0, $"malformed receive consumed {text}");
    Check(ChatManager.HistoryCount == 0 && Sink.Spam == 0, "malformed no history or spam");
}
Sink.Reset(); AmongUsClient.Instance.AmHost = false;
ChatCommands.OnReceiveChat(PlayerControl.LocalPlayer, "/cmd guess 1", out bool nonhostCanceled);
Check(nonhostCanceled && Sink.CoreVisits.Count == 0, "nonhost receive no execute");

foreach (string text in new[] { "/cmd guess 1", "normal text" })
{
    foreach (string invalidState in new[] { "disconnected client", "disconnected player", "null Data", "null player" })
    {
        Sink.Reset();
        var sender = PlayerControl.LocalPlayer;
        switch (invalidState)
        {
            case "disconnected client": AmongUsClient.Instance.AmConnected = false; break;
            case "disconnected player": sender.Data.Disconnected = true; break;
            case "null Data": sender.Data = null; break;
            case "null player": sender = null; break;
        }
        ChatCommands.OnReceiveChat(sender, text, out bool invalidCanceled);
        Check(invalidCanceled == HostOnlyChatCommand.IsEnvelope(text), $"invalid sender retains envelope privacy: {invalidState}");
        Check(Sink.CoreVisits.Count == 0 && Sink.FeatureCalls == 0 && Sink.Activity == 0,
            $"invalid sender never reaches role, feature, or AFK dispatch: {invalidState}");
        Check(ChatManager.HistoryCount == 0 && Sink.Spam == 0 && Sink.Writers.Count == 0,
            $"invalid sender causes no public history or network side effect: {invalidState}");
        Check(!HostOnlyChatCommand.IsActive, $"invalid sender cleans private scope: {invalidState}");
    }
}

Sink.Reset(); ChatCommands.OnReceiveChat(PlayerControl.LocalPlayer, "/cmd start", out bool unknownCanceled);
Check(unknownCanceled, "unknown private consumed despite core canceled false");
Check(Sink.CoreVisits.SequenceEqual(new[] { ("/start", true) }), "receive normalized under scope");
Check(ChatManager.HistoryCount == 0 && Sink.Spam == 0, "unknown private skips history and public spam");
Check(!HostOnlyChatCommand.IsActive, "receive cleanup");
Sink.Reset(); Sink.ThrowCore = true;
Throws(() => ChatCommands.OnReceiveChat(PlayerControl.LocalPlayer, "/cmd help", out _), "receive exception");
Check(!HostOnlyChatCommand.IsActive, "receive exception cleanup");
Sink.Reset(); Sink.HandleRole = true;
ChatCommands.OnReceiveChat(PlayerControl.LocalPlayer, "/cmd guess 1 Crewmate", out bool roleCanceled);
Check(roleCanceled && Sink.CoreVisits.Count == 1, "role receive dispatch once");
Check(Sink.Hides == 0 && Sink.Replays == 0 && Sink.Echoes.Count == 0, "role no hide replay or echo");

Sink.Reset(); ChatCommands.OnReceiveChat(PlayerControl.LocalPlayer, "normal text", out bool normalCanceled);
Check(!normalCanceled && Sink.CoreVisits.Single() == ("normal text", false), "normal receive unchanged");
Check(ChatManager.HistoryCount == 1 && ChatManager.LastMessage == "normal text" && Sink.Spam == 1, "normal history and spam");
Sink.Reset(); AmongUsClient.Instance.AmHost = false;
ChatCommands.OnReceiveChat(PlayerControl.LocalPlayer, "normal text", out normalCanceled);
Check(!normalCanceled && Sink.CoreVisits.Count == 0, "normal nonhost unchanged");

foreach (string text in new[] { "/cmd help", "/cmdfoo", "/cmd guess\n1" })
{
    Sink.Reset(); AmongUsClient.Instance.AmHost = false;
    bool result = false;
    bool allowOriginal = RpcSendChatPatch.Prefix(PlayerControl.LocalPlayer, text, ref result);
    var sent = Sink.Writers.Single();
    Check(!allowOriginal && result, "private outbound replaces original");
    Check(sent.Text == text && sent.Target == AmongUsClient.Instance.HostId && sent.Option == SendOption.Reliable,
        "owned outbound exact prefix reliable host target");
    Check(sent.NetId == PlayerControl.LocalPlayer.NetId && sent.Rpc == (byte)RpcCalls.SendChat, "outbound vanilla chat rpc");
    Check(Sink.Visible.Count == 0 && Sink.CoreVisits.Count == 0 && Sink.Telemetry == 0, "outbound no local echo or execution");
}
Sink.Reset(); AmongUsClient.Instance.AmHost = false;
bool htmlResult = false;
RpcSendChatPatch.Prefix(PlayerControl.LocalPlayer, "/cmd <b>help</b>", ref htmlResult);
Check(Sink.Writers.Single().Text == "/cmd help", "html removed without formatting newline");
foreach (bool owned in new[] { false, true })
{
    Sink.Reset(); PlayerControl.LocalPlayer.AmOwner = owned; AmongUsClient.Instance.AmConnected = !owned;
    bool result = true;
    Check(!RpcSendChatPatch.Prefix(PlayerControl.LocalPlayer, "/cmd help", ref result) && !result && Sink.Writers.Count == 0,
        "unowned or disconnected private blocked");
}
Sink.Reset(); bool hostResult = false;
Check(!RpcSendChatPatch.Prefix(PlayerControl.LocalPlayer, "/cmd help", ref hostResult) && hostResult,
    "host outbound handled locally");
Check(Sink.Writers.Count == 0 && Sink.CoreVisits.Single() == ("/help", true), "host outbound exactly one local dispatch");
Sink.Reset(); bool ordinaryResult = false;
RpcSendChatPatch.Prefix(PlayerControl.LocalPlayer, "who is here", ref ordinaryResult);
Check(ordinaryResult && Sink.Writers.Single().Text == "\n\nwho is here" && Sink.Writers.Single().Target == -1,
    "ordinary outbound formatting broadcast preserved");
Check(Sink.Visible.Single() == "\n\nwho is here" && Sink.Telemetry == 1, "ordinary local echo telemetry preserved");
Sink.Reset(); GameStates.IsModHost = false;
bool vanillaResult = true;
Check(RpcSendChatPatch.Prefix(PlayerControl.LocalPlayer, "/cmd help", ref vanillaResult) && Sink.Writers.Count == 0,
    "vanilla host passthrough preserved");
Sink.Reset(); bool emptyResult = true;
Check(!RpcSendChatPatch.Prefix(PlayerControl.LocalPlayer, " \t", ref emptyResult) && !emptyResult, "empty outbound rejected");

foreach (string text in new[] { "/cmd help", "/cmd /unknown", "/cmdfoo" })
{
    Sink.Reset(); AmongUsClient.Instance.AmHost = false;
    var chat = new ChatController(); chat.freeChatField.textArea.text = text;
    Check(ChatCommands.Prefix(chat) && Sink.LocalVisits.Count == 0 && chat.freeChatField.textArea.text == text,
        "nonhost prefix leaves envelope for transport once");
}
Sink.Reset(); var local = new ChatController(); local.freeChatField.textArea.text = "/cmd /unknown";
Check(!ChatCommands.Prefix(local) && local.freeChatField.textArea.text == "", "unknown local remains canceled even sink resets canceled");
Check(Sink.LocalVisits.Single() == ("/unknown", true), "host prefix normalized in scope");
Check(ChatCommands.ChatHistory.Single() == "/cmd /unknown" && ChatManager.HistoryCount == 0, "private recall raw and public history excluded");
Check(!HostOnlyChatCommand.IsActive, "local prefix cleanup");
local.freeChatField.textArea.text = "/cmd /unknown";
ChatCommands.Prefix(local);
Check(ChatCommands.ChatHistory.Count == 1, "raw recall deduplicated");
Sink.Reset(); local.freeChatField.textArea.text = "/cmdfoo";
Check(!ChatCommands.Prefix(local) && Sink.LocalVisits.Count == 0 && local.freeChatField.textArea.text == "", "malformed local consumed");
Sink.Reset(); local.freeChatField.textArea.text = "/cmd help"; Sink.ThrowLocal = true;
Throws(() => ChatCommands.Prefix(local), "local exception");
Check(!HostOnlyChatCommand.IsActive, "local exception cleanup");
Sink.Reset(); local.freeChatField.textArea.text = "normal text";
Check(ChatCommands.Prefix(local) && Sink.LocalVisits.Single() == ("normal text", false), "ordinary local passes through");

foreach (string text in new[] { "/cmd help", "/cmdfoo", "/cmd guess\n1" })
{
    Sink.Reset(); ChatManager.SendMessage(PlayerControl.LocalPlayer, text);
    Check(ChatManager.HistoryCount == 0 && !ChatManager.cancel, "raw envelope history guard");
}
Sink.Reset(); Options.HideExileChat.Enabled = true; GameStates.IsExilling = true;
using (HostOnlyChatCommand.Enter()) ChatManager.SendMessage(PlayerControl.LocalPlayer, "/unknown");
Check(Sink.Delayed == 0 && ChatManager.HistoryCount == 0, "scope blocks exile replay scheduling");
ChatManager.SendMessage(PlayerControl.LocalPlayer, "normal text");
Check(Sink.Delayed == 1, "ordinary exile replay scheduling retained");

var hides = typeof(Effects).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name.StartsWith("Hide")).ToArray();
var echoes = typeof(Effects).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name.StartsWith("Echo")).ToArray();
Check(hides.Length == 4 && echoes.Length == 8, "all four hide guards and eight role echo sites extracted");
foreach (var hide in hides)
{
    Sink.Reset(); using (HostOnlyChatCommand.Enter()) hide.Invoke(null, null);
    Check(Sink.Hides == 0, $"{hide.Name} scoped suppress");
    hide.Invoke(null, null);
    Check(Sink.Hides == 1, $"{hide.Name} ordinary retained");
}
foreach (var echo in echoes)
{
    Sink.Reset(); object[] arguments = { PlayerControl.LocalPlayer, "/role command", false };
    using (HostOnlyChatCommand.Enter()) echo.Invoke(null, arguments);
    Check(Sink.Echoes.Count == 0, $"{echo.Name} scoped suppress");
    echo.Invoke(null, arguments);
    Check(Sink.Echoes.Single() == ("/role command", (byte)255), $"{echo.Name} normal public echo retained");
    Sink.Echoes.Clear(); PlayerControl.LocalPlayer.AmOwner = false;
    echo.Invoke(null, arguments);
    Check(Sink.Echoes.Count == 0, $"{echo.Name} nonowner echo blocked");
    if (echo.Name.StartsWith("EchoGuessManager") || echo.Name.StartsWith("EchoSwapper"))
    {
        PlayerControl.LocalPlayer.AmOwner = true; arguments[2] = true;
        echo.Invoke(null, arguments);
        Check(Sink.Echoes.Count == 0, $"{echo.Name} UI echo blocked");
    }
}
Sink.Reset(); using (HostOnlyChatCommand.Enter()) ChatManager.SendPreviousMessagesToAll();
Check(Sink.Replays == 0, "scope replay blocked");
ChatManager.SendPreviousMessagesToAll();
Check(Sink.Replays == 1, "host ordinary replay preserved");
AmongUsClient.Instance.AmHost = false; ChatManager.SendPreviousMessagesToAll();
Check(Sink.Replays == 1, "nonhost replay blocked");

Console.WriteLine($"PASS: {checks} HostOnlyChatCommand offline regression checks (production parser, source-extracted routes/guards; native and role outcomes stubbed).");
