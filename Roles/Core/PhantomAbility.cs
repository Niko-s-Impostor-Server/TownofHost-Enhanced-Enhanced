using AmongUs.GameOptions;
using Hazel;
using UnityEngine;

namespace TOHE.Roles.Core;

/// <summary>Owner requests and host adjudication for visible Phantom-backed abilities.</summary>
public static class PhantomAbility
{
    public static bool IsEnabled(PlayerControl player)
        => player != null && player.Data != null && player.Data.Role != null
            && player.Data.Role.Role == RoleTypes.Phantom && player.GetRoleClass()?.UsesPhantomAbility == true;

    public static bool CanRequest(PlayerControl player)
        => AmongUsClient.Instance != null && AmongUsClient.Instance.AmConnected
            && IsEnabled(player) && player.AmOwner && player.IsAlive() && !player.Data.IsDead
            && !player.Data.Disconnected && GameStates.IsInTask && !AmongUsClient.Instance.IsGameOver
            && player.moveable && !player.walkingToVent && !player.inMovingPlat && !player.inVent
            && !Minigame.Instance;

    public static bool CanHandle(PlayerControl player, RoleTypes requiredRole = RoleTypes.Phantom)
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmConnected || !client.AmHost || client.IsGameOver
            || !GameStates.IsInTask || AntiBlackout.SkipTasks || player == null || player.Data == null
            || player.Data.Disconnected || player.Data.IsDead || !player.IsAlive()
            || player.Data.Role == null || player.Data.Role.Role != requiredRole
            || requiredRole is not (RoleTypes.Phantom or RoleTypes.Shapeshifter)
            || player.inVent || player.walkingToVent || player.inMovingPlat) return false;
        var role = player.GetRoleClass();
        return role != null && role.UsesPhantomAbility && role._Player == player
            && Main.PlayerStates.TryGetValue(player.PlayerId, out var state) && state == role._state;
    }

    public static void Request(PlayerControl player, float maxDuration)
    {
        if (!CanRequest(player)) return;
        var client = AmongUsClient.Instance;
        if (client.AmHost)
        {
            player.CheckVanish();
            return;
        }
        var writer = client.StartRpcImmediately(player.NetId, (byte)RpcCalls.CheckVanish, SendOption.Reliable, client.HostId);
        writer.Write(maxDuration);
        client.FinishRpcImmediately(writer);
    }

    public static bool TryActivate(PlayerControl player, RoleTypes requiredRole = RoleTypes.Phantom)
    {
        if (!CanHandle(player, requiredRole)) return false;
        var role = player.GetRoleClass();
        if (role.PhantomAbilityExecuting || Time.realtimeSinceStartup < role.PhantomAbilityReadyAt) return false;

        var previousDeadline = role.PhantomAbilityReadyAt;
        role.PhantomAbilityExecuting = true;
        // Reserve before role effects, so nested callbacks cannot consume the same press twice.
        role.PhantomAbilityReadyAt = Time.realtimeSinceStartup + Mathf.Max(0f, role.PhantomAbilityCooldown);
        try
        {
            if (role.OnPhantomAbility(player)) return true;
            role.PhantomAbilityReadyAt = previousDeadline;
            return false;
        }
        finally
        {
            role.PhantomAbilityExecuting = false;
        }
    }

    public static void RestoreVisibleButton(PlayerControl player)
    {
        if (!IsEnabled(player) || player.Data.Role is not PhantomRole phantom) return;
        phantom.SetInvisible(false);
        phantom.SetFading(false);
        phantom.SetServerApproval(false);
        player.ForcePhantomVisible();
        if (!player.AmOwner || !DestroyableSingleton<HudManager>.InstanceExists) return;
        var button = DestroyableSingleton<HudManager>.Instance.AbilityButton;
        button.SetFromSettings(phantom.Ability);
        phantom.SetCooldown();
        player.GetRoleClass().SetAbilityButtonText(DestroyableSingleton<HudManager>.Instance, player.PlayerId);
    }
}
