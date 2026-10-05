using Hazel;
using TOHE;

int checks = 0;
NetworkedPlayerInfo info = null;
PlayerControl player = null, host = null;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void Fixture(bool amHost = true)
{
    OnGameJoinedPatch.Generation++;
    AmongUsClient.Instance = new() { Pointer = 1, AmHost = amHost };
    MeetingHud.Instance = new() { Pointer = 2 };
    info = new() { Pointer = 3, PlayerId = 1, ClientId = 11, PlayerName = "Alice" };
    player = new() { Pointer = 4, OwnerId = 11, Data = info };
    host = new() { Pointer = 5, OwnerId = 10, AmOwner = amHost };
    Utils.Players = new() { [1] = player };
    Main.PlayerStates = new() { [1] = new() };
    Main.playerVersion = new() { [10] = new() { version = Main.version, forkId = Main.ForkId,
        tag = $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})" } };
    CustomRpcTransport.Payloads.Clear();
    ExileTextMeetingStartPatch.Prefix();
}
byte[] Payload(string text, bool antiBlackout = false, int game = 123, uint meeting = 55, byte id = 1, bool trailing = false)
{
    var writer = new MessageWriter();
    writer.Write(game); writer.Write(meeting); writer.Write(id); writer.Write(antiBlackout); writer.Write(text);
    if (trailing) writer.Write((byte)99);
    return writer.Bytes();
}
ExileController Begin(NetworkedPlayerInfo exiled, bool tie = false)
{
    var controller = new ExileController { Pointer = 6 };
    controller.Text.gameObject.activeSelf = false;
    ExileTextBeginPatch.Postfix(controller, exiled, tie);
    return controller;
}

Fixture();
const string roleText = "Alice was <color=red>Arrogance</color>.\nOne Impostor remains.";
ExileText.Publish(info, roleText + "<size=0>", "Alice", false);
Check(info.PlayerName == "Alice" && player.SentNames.Count == 0, "Voting results must retain real names until actual close");
var published = CustomRpcTransport.Payloads.Single();
var reader = new MessageReader(published);
Check(reader.ReadInt32() == 123 && reader.ReadUInt32() == 55 && reader.ReadByte() == 1 && !reader.ReadBoolean()
    && reader.ReadString() == roleText && reader.BytesRemaining == 0,
    "Message payload must bind room/meeting/exile, carry explicit anti-blackout flag, and omit hidden name suffix");
// A host may hold the LocalGame Proceed button arbitrarily long. There is no vote-relative restore timer.
ExileTextMeetingClosePatch.Prefix(MeetingHud.Instance);
Check(info.PlayerName == roleText + "<size=0>" && player.SentNames.Count == 1, "Actual close must send vanilla name before native snapshots it");
ExileTextMeetingClosePatch.Prefix(MeetingHud.Instance);
Check(player.SentNames.Count == 1, "Repeated close must not duplicate compatibility name mutation");
ExileText.Publish(info, roleText + "<size=0>", "Alice", false);
Check(player.SentNames.Count == 1 && CustomRpcTransport.Payloads.Count == 1,
    "Duplicate publication must not reset an owned name or resend the same policy");
var controller = Begin(info);
Check(controller.completeString == roleText && controller.Text.text == "", "Native initialization must receive clean custom text while hidden text keeps native reveal timing");
Check(!controller.ImpostorText.gameObject.activeSelf && !controller.initData.confirmImpostor,
    "Custom counts must suppress separate native count label without changing global options");
ExileTextRestorePatch.Postfix();
Check(info.PlayerName == "Alice" && player.SentNames.Count == 2, "Actual exile lifecycle must restore real name");
ExileTextRestorePatch.Postfix();
Check(player.SentNames.Count == 2, "Restoration must be idempotent");

Fixture(); ExileText.Publish(info, "Alice was ejected.<size=0>", "Alice", false);
controller = Begin(info);
Check(controller.completeString == "Alice was ejected.", "Anonymous confirmation mode must preserve the host's anonymous text");
Fixture(); ExileText.Publish(info, "Alice belonged to the Neutral team.<size=0>", "Alice", false);
controller = Begin(info);
Check(controller.completeString == "Alice belonged to the Neutral team.", "Team mode must preserve generated team text");
Fixture(); ExileText.Publish(info, roleText, "Alice", false);
controller = Begin(null, true);
Check(controller.completeString == "native vanilla" && controller.initData.confirmImpostor,
    "Ordinary tie/no-exile must never inherit pending role text");
Fixture(); ExileText.Publish(info, roleText, "Alice", true);
controller = Begin(null, true);
Check(controller.completeString == roleText, "Explicit host anti-blackout presentation may override native null/tie placeholder");

Fixture(); ExileText.Publish(info, roleText, "Alice", false);
ExileText.BeforeClose(MeetingHud.Instance);
ExileTextMeetingStartPatch.Prefix();
Check(info.PlayerName == "Alice" && player.SentNames.Count == 2, "Starting the next meeting must restore and clear previous owned name");
controller = Begin(info);
Check(controller.completeString == "native vanilla", "Next meeting must not reuse previous text");
Fixture(); ExileText.Publish(info, roleText, "Alice", false);
MeetingHud.Instance = new() { Pointer = 99, NetId = 55 };
ExileText.BeforeClose(MeetingHud.Instance);
controller = Begin(info);
Check(info.PlayerName == "Alice" && controller.completeString == "native vanilla", "Reused meeting net ID with another native object must not inherit pending context");
Fixture(); ExileText.Publish(info, roleText, "Alice", false);
OnGameJoinedPatch.Generation++;
controller = Begin(info);
Check(controller.completeString == "native vanilla", "Session generation must invalidate pending text");
Fixture(); ExileText.Publish(info, roleText, "Alice", false);
Main.PlayerStates = new() { [1] = new() };
controller = Begin(info);
Check(controller.completeString == "native vanilla", "New round state must invalidate pending text");
Fixture(); ExileText.Publish(info, roleText, "Alice", false);
AmongUsClient.Instance.GameId++;
controller = Begin(info);
Check(controller.completeString == "native vanilla", "Another room must invalidate pending text");
Fixture(); ExileText.Publish(info, roleText, "Alice", false);
AmongUsClient.Instance.HostId++;
controller = Begin(info);
Check(controller.completeString == "native vanilla", "Host migration must invalidate pending text");
Fixture(); ExileText.Publish(info, roleText, "Alice", false);
player.Data = new() { Pointer = 44, PlayerId = 1, ClientId = 11, PlayerName = "Replacement" };
ExileText.BeforeClose(MeetingHud.Instance);
Check(player.SentNames.Count == 0, "Reused player ID must not rename another native player info");

Fixture(false);
ExileText.Publish(info, roleText, "Alice", false);
Check(CustomRpcTransport.Payloads.Count == 0, "Non-host must not publish custom exile policy");
ExileText.Receive(host, new(Payload(roleText)));
controller = Begin(info);
Check(controller.completeString == roleText, "Remote mod recipient must apply authenticated host message");
Check(player.SentNames.Count == 0, "Recipient must never broadcast compatibility names");
Fixture(false); controller = Begin(info);
controller.Text.gameObject.activeSelf = true;
MeetingHud.Instance = null;
ExileText.Receive(host, new(Payload(roleText)));
Check(controller.completeString == roleText && controller.Text.text == roleText,
    "Message arriving after local cutscene start must update the active cached text");
Fixture(false); controller = Begin(null, true); MeetingHud.Instance = null;
ExileText.Receive(host, new(Payload(roleText, antiBlackout: true)));
Check(controller.completeString == roleText, "Late anti-blackout text must match captured cutscene context");

void Reject(byte[] payload, Action arrange = null)
{
    Fixture(false); arrange?.Invoke();
    ExileText.Receive(host, new(payload));
    var result = Begin(info);
    Check(result.completeString == "native vanilla", "Invalid or unbound host payload must not alter exile text");
}
Reject(Payload(roleText), () => host.OwnerId = 50);
Reject(Payload(roleText), () => Main.playerVersion[10].tag = "old-build");
Reject(Payload(roleText, game: 456));
Reject(Payload(roleText, meeting: 99));
Reject(Payload(roleText, id: 254));
Reject(Payload(""));
Reject(Payload(new string('X', 4097)));
Reject(Payload(roleText, trailing: true));
Reject([1, 2, 3]);
Reject(Payload(roleText), () => Main.PlayerStates.Clear());
Fixture(); ExileText.Publish(info, new string('X', 4097), "Alice", false);
Check(CustomRpcTransport.Payloads.Count == 0 && player.SentNames.Count == 0, "Oversized message must be refused without name mutation");
Fixture(); ExileText.Publish(info, roleText, "Alice", false);
ExileText.BeforeClose(MeetingHud.Instance);
ExileTextDisconnectedPatch.Prefix();
controller = Begin(info);
Check(player.SentNames.Count == 1 && controller.completeString == "native vanilla",
    "Session teardown must clear context without sending old-room restoration RPC");
Fixture(); ExileText.Publish(info, roleText, "Alice", false);
ExileTextJoinedPatch.Prefix();
controller = Begin(info);
Check(controller.completeString == "native vanilla", "Room join lifecycle must clear prior pending context");

Console.WriteLine($"PASS: {checks} linked-production exile text payload/lifecycle checks.");
