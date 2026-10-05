using TOHE;
using UnityEngine;

int checks = 0;
PlayerControl local = null, remote = null;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void Fixture()
{
    OnGameJoinedPatch.Generation++;
    Time.realtimeSinceStartup = 100;
    AmongUsClient.Instance = new() { AmHost = true, AmConnected = true };
    GameStates.IsInTask = true;
    GameStates.IsEnded = false;
    Main.IntroDestroyed = true;
    ShipStatus.Instance = new();
    ExileController.Instance = null;
    Minigame.Instance = null;
    local = new() { Pointer = 111, PlayerId = 0, OwnerId = 10, AmOwner = true,
        moveable = false, KillCooldown = 37, Position = (8, -3), Tasks = [2, 7, 11] };
    local.Data.Role.RoleId = 88;
    local.Data.IsDead = true;
    remote = new() { Pointer = 222, PlayerId = 1, OwnerId = 11 };
    PlayerControl.LocalPlayer = local;
    Main.PlayerStates = new() { [0] = new(), [1] = new() };
    Main.playerVersion = new() { [11] = new() { version = Main.version, forkId = Main.ForkId,
        tag = $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})" } };
    HudManager.Instance = new() { PlayerCam = new() { Target = local, Locked = true },
        FullScreen = new() { enabled = true, color = new(0, 0, 0, 1) } };
    CustomRpcTransport.Requests.Clear();
    CustomRpcTransport.Finished = 0;
}
void Reject(Action arrange, string message)
{
    Fixture(); arrange();
    var originalColor = HudManager.Instance.FullScreen.color;
    var originalEnabled = HudManager.Instance.FullScreen.enabled;
    var originalTarget = HudManager.Instance.PlayerCam.Target;
    var originalLocked = HudManager.Instance.PlayerCam.Locked;
    Check(!PresentationRecovery.RecoverLocal(), message);
    Check(HudManager.Instance.PlayerCam.Snaps == 0 && !HudManager.Instance.Active
        && HudManager.Instance.FullScreen.color == originalColor && HudManager.Instance.FullScreen.enabled == originalEnabled
        && ReferenceEquals(HudManager.Instance.PlayerCam.Target, originalTarget) && HudManager.Instance.PlayerCam.Locked == originalLocked,
        "Rejection must preserve overlay, camera target, lock, and HUD");
}

Reject(() => Main.IntroDestroyed = false, "Intro must reject recovery");
Reject(() => GameStates.IsInTask = false, "Meeting must reject recovery");
Reject(() => GameStates.IsEnded = true, "Ended game must reject recovery");
Reject(() => ShipStatus.Instance = null, "Missing ship must reject recovery");
Reject(() => ExileController.Instance = new(), "Meeting/exile transition must reject recovery");
Reject(() => Minigame.Instance = new(), "Open native minigame must reject recovery");
Reject(() => HudManager.Instance.IsIntroDisplayed = true, "Displayed intro must reject recovery even if state already advanced");
Reject(() => Main.PlayerStates[0].IsBlackOut = true, "Role blackout must reject recovery");
Reject(() => local.onLadder = true, "Ladder state must reject recovery");
Reject(() => local.inVent = true, "Vent state must reject recovery");
Reject(() => local.walkingToVent = true, "Walking-to-vent transition must reject recovery");
Reject(() => local.MyPhysics.Animations.EnterVent = true, "Enter-vent animation must reject recovery");
Reject(() => local.MyPhysics.Animations.Ladder = true, "Ladder animation must reject recovery");
Reject(() => local.inMovingPlat = true, "Moving platform must reject recovery");
Reject(() => local.Data.Role = null, "Missing role must reject recovery");
Reject(() => Main.PlayerStates.Remove(0), "Uninitialized player state must reject recovery");
Reject(() => HudManager.Instance.FullScreen.color = new(0.4f, 0, 0, 1), "Opaque colored effect must remain intact");
Reject(() => HudManager.Instance.FullScreen.color = new(0, 0, 0, 0.7f), "Translucent black effect must remain intact");
Reject(() => HudManager.Instance.PlayerCam.Target = remote, "Another camera observation target must remain intact");

Fixture();
var taskList = local.Tasks;
var role = local.Data.Role;
var tasks = local.Tasks.ToArray();
var position = local.Position;
var cooldown = local.KillCooldown;
var movable = local.moveable;
var death = local.Data.IsDead;
Check(PresentationRecovery.RecoverLocal(), "Opaque stale black screen should be recoverable");
Check(HudManager.Instance.FullScreen.color == Color.clear && !HudManager.Instance.FullScreen.enabled,
    "Recovery must remove stale opaque black overlay");
Check(HudManager.Instance.PlayerCam.Target == local && !HudManager.Instance.PlayerCam.Locked
    && HudManager.Instance.PlayerCam.Snaps == 1 && HudManager.Instance.Active && HudManager.Instance.taskDirtyTimer == 0.25f,
    "Recovery must restore presentation camera/HUD and request task text refresh");
Check(ReferenceEquals(local.Tasks, taskList) && local.Tasks.SequenceEqual(tasks) && ReferenceEquals(local.Data.Role, role)
    && role.RoleId == 88 && local.Position == position && local.KillCooldown == cooldown
    && local.moveable == movable && local.Data.IsDead == death,
    "Presentation repair must preserve tasks, role, cooldown, position, immobility, and death");
Check(CustomRpcTransport.Requests.Count == 0, "Local repair must not send gameplay RPCs");

Fixture();
HudManager.Instance.PlayerCam.Target = null;
Check(PresentationRecovery.RecoverLocal() && HudManager.Instance.PlayerCam.Target == local,
    "An unassigned camera may be restored to local player");
Fixture();
HudManager.Instance.PlayerCam.Target = new() { Pointer = local.Pointer };
Check(PresentationRecovery.RecoverLocal(), "A second managed wrapper for the same native player must be accepted");

Fixture();
AmongUsClient.Instance.AmHost = false;
Check(PresentationRecovery.TryRequest(remote) == "FeatureInvalidTarget" && CustomRpcTransport.Requests.Count == 0,
    "Non-host must not send a recovery request");
Fixture();
AmongUsClient.Instance.AmConnected = false;
Check(PresentationRecovery.TryRequest(remote) == "FeatureInvalidTarget", "Disconnected host must not send requests");
Fixture(); remote.Data.Disconnected = true;
Check(PresentationRecovery.TryRequest(remote) == "FeatureInvalidTarget", "Disconnected recipient must be rejected");
Fixture(); Main.playerVersion.Clear();
Check(PresentationRecovery.TryRequest(remote) == "RecoveryNeedsMod", "Vanilla/unknown recipient must not receive custom recovery");
Fixture(); Main.playerVersion[11].forkId = "another-fork";
Check(PresentationRecovery.TryRequest(remote) == "RecoveryNeedsMod", "Another fork must not pass compatibility");
Fixture(); Main.playerVersion[11].version = new(1, 2, 4);
Check(PresentationRecovery.TryRequest(remote) == "RecoveryNeedsMod", "Another mod version must not pass compatibility");
Fixture(); Main.playerVersion[11].tag = "old-build";
Check(PresentationRecovery.TryRequest(remote) == "RecoveryNeedsMod", "Another build must not pass compatibility");
Fixture();
Check(PresentationRecovery.TryRequest(remote) == "RecoveryRequested" && CustomRpcTransport.Finished == 1
    && CustomRpcTransport.Requests.Single() == (CustomRPC.RecoverPresentation, remote.OwnerId),
    "Compatible recovery must send one targeted presentation request");
Check(PresentationRecovery.TryRequest(remote) == "RecoveryCooldown" && CustomRpcTransport.Requests.Count == 1,
    "Repeated recipient requests must be throttled without new RPC");
Check(PresentationRecovery.TryRequest(local) == "RecoveryCompleted", "Another recipient must have an independent throttle");
Time.realtimeSinceStartup += 5;
Check(PresentationRecovery.TryRequest(remote) == "RecoveryRequested", "Recipient throttle must expire after five seconds");
OnGameJoinedPatch.Generation++;
Check(PresentationRecovery.TryRequest(remote) == "RecoveryRequested", "New session generation must reset throttle");

Fixture(); Minigame.Instance = new();
Check(PresentationRecovery.TryRequest(local) == "RecoveryNotReady", "Local UI refusal must report not-ready");
Minigame.Instance = null;
Check(PresentationRecovery.TryRequest(local) == "RecoveryCompleted", "Failed local recovery must not consume throttle");

Console.WriteLine($"PASS: {checks} linked-production presentation recovery checks.");
