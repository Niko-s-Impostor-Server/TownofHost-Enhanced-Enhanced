using Hazel;
using UnityEngine;

namespace TOHE;

// A targeted presentation repair. Never reconstruct roles, kill, move or revive a player.
internal static class PresentationRecovery
{
    private static readonly Dictionary<int, float> nextRequest = [];
    private static uint generation;

    internal static string TryRequest(PlayerControl target)
    {
        var client = AmongUsClient.Instance;
        if (!client || !client.AmConnected || !client.AmHost || !target || target.Data == null || target.Data.Disconnected) return "FeatureInvalidTarget";
        if (!Ready(target)) return "RecoveryNotReady";
        if (!RpcCompatibility.IsCurrentClient(target)) return "RecoveryNeedsMod";
        if (generation != OnGameJoinedPatch.Generation) { generation = OnGameJoinedPatch.Generation; nextRequest.Clear(); }
        float now = Time.realtimeSinceStartup;
        if (nextRequest.TryGetValue(target.OwnerId, out var next) && now < next) return "RecoveryCooldown";
        if (target.AmOwner)
        {
            if (!RecoverLocal()) return "RecoveryNotReady";
            nextRequest[target.OwnerId] = now + 5f;
            return "RecoveryCompleted";
        }
        nextRequest[target.OwnerId] = now + 5f;
        var writer = CustomRpcTransport.Start(CustomRPC.RecoverPresentation, SendOption.Reliable, target.OwnerId);
        CustomRpcTransport.Finish(writer);
        return "RecoveryRequested";
    }

    private static bool Ready(PlayerControl player) => GameStates.IsInTask && !GameStates.IsEnded && Main.IntroDestroyed &&
        ShipStatus.Instance && !ExileController.Instance && !player.inVent && !player.walkingToVent && !player.onLadder && !player.inMovingPlat &&
        player.MyPhysics != null && !player.MyPhysics.Animations.IsPlayingEnterVentAnimation() &&
        !player.MyPhysics.Animations.IsPlayingAnyLadderAnimation() &&
        player.Data.Role && Main.PlayerStates.TryGetValue(player.PlayerId, out var state) && !state.IsBlackOut;

    internal static bool RecoverLocal()
    {
        var player = PlayerControl.LocalPlayer;
        if (!player || player.Data == null || !Ready(player) || Minigame.Instance || !HudManager.Instance || HudManager.Instance.IsIntroDisplayed) return false;
        var hud = HudManager.Instance;
        var camera = hud.PlayerCam;
        // Do not end another role's deliberate remote-camera observation.
        if (!camera || (camera.Target && camera.Target.Pointer != player.Pointer)) return false;
        if (hud.FullScreen && hud.FullScreen.enabled && hud.FullScreen.color.a > 0.01f)
        {
            var color = hud.FullScreen.color;
            if (color.a < 0.98f || color.r > 0.02f || color.g > 0.02f || color.b > 0.02f) return false;
        }
        camera.Target = player;
        camera.Locked = false;
        camera.SnapToTarget();
        if (hud.FullScreen) { hud.FullScreen.color = Color.clear; hud.FullScreen.enabled = false; }
        hud.SetHudActive(true);
        hud.taskDirtyTimer = 0.25f;
        return true;
    }
}
