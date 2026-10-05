using TOHE;
using TOHE.Modules;
using TOHE.Patches;

var assertions = 0;
void Check(bool value, string description)
{
    if (!value) throw new Exception("FAIL: " + description);
    assertions++;
}
void NoMutation(PlayerControl sender, string command, bool handled = true)
{
    var before = Effects.Snapshot();
    var dead = Main.AllPlayerControls.Select(player => player.Data?.IsDead).ToArray();
    var stateDead = Main.PlayerStates.Values.Select(state => state.IsDead).ToArray();
    Check(FeatureChatCommands.TryHandle(sender, command) == handled, "expected route selection: " + command);
    Check(Effects.Snapshot() == before && dead.SequenceEqual(Main.AllPlayerControls.Select(player => player.Data?.IsDead)) && stateDead.SequenceEqual(Main.PlayerStates.Values.Select(state => state.IsDead)), "no denied side effect: " + command);
}
var host = new PlayerControl { OwnerId = 10, PlayerId = 0, AmOwner = true, Data = new() { FriendCode = "hostuser#0000" } };
var remote = new PlayerControl { OwnerId = 11, PlayerId = 1, Data = new() { FriendCode = "allgrants#0001" } };
var chatOnly = new PlayerControl { OwnerId = 12, PlayerId = 2, Data = new() { FriendCode = "chatonly#0002" } };
var gmOnly = new PlayerControl { OwnerId = 13, PlayerId = 3, Data = new() { FriendCode = "gmonly#0003" } };
var normal = new PlayerControl { OwnerId = 14, PlayerId = 4, Data = new() { FriendCode = "normaluser#0004" } };
var legacy = new PlayerControl { OwnerId = 15, PlayerId = 5, Data = new() { FriendCode = "legacymod#0005" } };
var protectedFile = new PlayerControl { OwnerId = 16, PlayerId = 6, Data = new() { FriendCode = "protectedmod#0006" } };
Main.AllPlayerControls = [host, remote, chatOnly, gmOnly, normal, legacy, protectedFile];
foreach (var player in Main.AllPlayerControls) Main.PlayerStates.Add(player.PlayerId, new());
Utils.LegacyModerators.Add(legacy.Data.FriendCode);
const string json = """
{"version":1,"players":{"allgrants#0001":{"permissions":["moderate","chat","end","execute"]},"chatonly#0002":{"permissions":["chat"]},"gmonly#0003":{"gameMaster":true},"protectedmod#0006":{"permissions":["moderate"]}}}
""";
var original = Directory.GetCurrentDirectory();
var temporary = Path.Combine(Path.GetTempPath(), "TOHE-FeatureChat-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
Directory.SetCurrentDirectory(temporary);
try
{
    Directory.CreateDirectory("TOHE-DATA");
    File.WriteAllText("TOHE-DATA/LocalPlayerTags.json", json);
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.HasPermission(remote, LocalPlayerPermission.Execute), "session loads actual service grants");
    legacy.FriendCode = legacy.Data.FriendCode;
    Check(ExtractedLegacyModeration.Ban(protectedFile) && ExtractedLegacyModeration.Kick(protectedFile), "legacy target guards also protect file moderators");
    Check(ExtractedLegacyModeration.Ban(legacy) && ExtractedLegacyModeration.Kick(legacy), "legacy target guards preserve legacy moderator protection");
    Check(!ExtractedLegacyModeration.Ban(normal) && !ExtractedLegacyModeration.Kick(normal), "ordinary target is not accidentally protected");
    foreach (var command in new[] { "/save remote", "/load remote", "/tags reload", "/afk exempt 4", "/fixblack 4" }) NoMutation(remote, command);
    Check(!File.Exists("preset-write-sentinel") && !File.Exists("preset-load-sentinel"), "remote host-context requests cannot reach filesystem stub");
    remote.AmOwner = true;
    NoMutation(remote, "/save owner-spoof");
    remote.AmOwner = false;
    host.AmOwner = false;
    NoMutation(host, "/load remote-host-object");
    host.AmOwner = true;
    Check(FeatureChatCommands.TryHandle(host, "/save local") && Effects.Saves == 1 && File.ReadAllText("preset-write-sentinel") == "local", "local host can reach preset save");
    Check(FeatureChatCommands.TryHandle(host, "/load local") && Effects.Loads == 1 && File.ReadAllText("preset-load-sentinel") == "local", "local host can reach preset load");
    File.Delete("preset-write-sentinel"); File.Delete("preset-load-sentinel");
    foreach (var sender in new[] { normal, gmOnly, chatOnly })
        foreach (var command in new[] { "/end", "/exe 4" }) NoMutation(sender, command);
    Check(FeatureChatCommands.TryHandle(remote, "/say hello world") && Utils.Messages.Last().Message == "hello world" && Utils.Messages.Last().Title == "Player", "explicit chat grant broadcasts sanitized sender title");
    NoMutation(normal, "/say hello", handled: false);
    NoMutation(host, "/say hello", handled: false);
    NoMutation(legacy, "/kick 4 reason", handled: false);
    NoMutation(normal, "/ban 4 reason", handled: false);
    foreach (var command in new[] { "/kick 0 reason", "/ban 5 reason", "/kick 6 reason", "/ban 99 reason", "/kick 4" }) NoMutation(remote, command);
    normal.Data.Disconnected = true;
    NoMutation(remote, "/ban 4 reason");
    normal.Data.Disconnected = false;
    Check(FeatureChatCommands.TryHandle(remote, "/kick 4 reason") && Effects.LastKick == (normal.OwnerId, false), "granted moderation kicks by native owner");
    Check(FeatureChatCommands.TryHandle(remote, "/ban 4 reason") && Effects.LastKick == (normal.OwnerId, true), "granted moderation bans by native owner");
    NoMutation(remote, "/exe 0");
    normal.Data.Disconnected = true;
    NoMutation(remote, "/exe 4");
    normal.Data.Disconnected = false;
    var targetData = normal.Data;
    normal.Data = null;
    NoMutation(remote, "/exe 4");
    normal.Data = targetData;
    var targetState = Main.PlayerStates[normal.PlayerId];
    Main.PlayerStates.Remove(normal.PlayerId);
    NoMutation(remote, "/exe 4");
    Main.PlayerStates.Add(normal.PlayerId, targetState);
    GameStates.IsInGame = false; GameStates.IsLobby = true;
    NoMutation(remote, "/end"); NoMutation(remote, "/exe 4");
    GameStates.IsInGame = true; GameStates.IsLobby = false;
    Check(FeatureChatCommands.TryHandle(remote, "/end") && Effects.Winner == 1 && Effects.EndChecks == 1, "end grant applies winner and end check exactly once");
    Check(FeatureChatCommands.TryHandle(remote, "/exe 4") && normal.Data.IsDead && Main.PlayerStates[normal.PlayerId].IsDead && normal.LastKiller == remote && Effects.Exiles == 1 && Effects.AfterDeath == 1, "execute grant completes death state and uses actual sender");
    NoMutation(remote, "/exe 4");
    NoMutation(remote, "/exe 99");
    remote.Data.FriendCode = "unlisted#0001"; remote.FriendCode = "allgrants#0001";
    NoMutation(remote, "/end"); NoMutation(remote, "/exe 2");
    remote.Data.FriendCode = "allgrants#0001";
    remote.Data.Disconnected = true;
    NoMutation(remote, "/end", handled: false);
    remote.Data.Disconnected = false;
    NoMutation(null, "/end", handled: false);
    AmongUsClient.Instance.AmConnected = false;
    NoMutation(host, "/end", handled: false);
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.EntryCount == 0, "disconnect revokes snapshot");
    AmongUsClient.Instance.AmConnected = true;
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.HasPermission(remote, LocalPlayerPermission.End), "reconnect reloads grants");
    AmongUsClient.Instance.AmHost = false;
    NoMutation(host, "/save client", handled: false);
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.EntryCount == 0, "host loss revokes snapshot");
    AmongUsClient.Instance.AmHost = true;
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.EntryCount == 4, "host regain reloads snapshot");
    Options.IsLoaded = false;
    AmongUsClient.Instance.HostId = 20;
    NoMutation(remote, "/end");
    Check(LocalPlayerTags.EntryCount == 0, "command before next HUD tick revokes stale host-context grants");
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.EntryCount == 0, "host-ID change revokes before options readiness");
    Options.IsLoaded = true;
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.EntryCount == 4, "new host context loads when ready");
    Options.IsLoaded = false;
    OnGameJoinedPatch.Generation++;
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.EntryCount == 0, "generation change revokes before options readiness");
    Options.IsLoaded = true;
    File.WriteAllText("TOHE-DATA/LocalPlayerTags.json", "{broken");
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.EntryCount == 0 && Logger.Warnings > 0, "new-session invalid config grants nothing");
    File.WriteAllText("TOHE-DATA/LocalPlayerTags.json", json);
    AmongUsClient.Instance.HostId = host.OwnerId;
    FeatureChatCommands.UpdateSession();
    Check(LocalPlayerTags.EntryCount == 4, "restored host loads valid file");
    File.WriteAllText("TOHE-DATA/LocalPlayerTags.json", "{broken");
    Check(FeatureChatCommands.TryHandle(host, "/tags reload") && LocalPlayerTags.EntryCount == 0, "explicit local reload revokes invalid snapshot");
    Check(!File.Exists("preset-write-sentinel") && !File.Exists("preset-load-sentinel"), "denied requests never created preset markers");
}
finally
{
    LocalPlayerTags.Reset();
    Directory.SetCurrentDirectory(original);
    Directory.Delete(temporary, recursive: true);
}
Console.WriteLine($"Feature chat commands: {assertions} assertions passed.");
