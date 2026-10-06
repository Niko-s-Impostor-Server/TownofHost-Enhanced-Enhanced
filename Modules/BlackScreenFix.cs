using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using System;
using TOHE.Modules;
using TOHE.Roles.Neutral;
using UnityEngine;

namespace TOHE;

// Based on EHR's /fix: https://github.com/Gurge44/EndlessHostRoles
// Native role resync before the first meeting; a two-stage vanilla-client repair afterwards.
internal static class BlackScreenFix
{
    private sealed record Context(uint Generation, nint Client, int GameId, int HostId,
        nint Ship, object PlayerStates)
    {
        internal bool Current()
        {
            var client = AmongUsClient.Instance;
            return client && client.AmConnected && client.AmHost && client.Pointer == Client
                && client.GameId == GameId && client.HostId == HostId
                && OnGameJoinedPatch.Generation == Generation && ShipStatus.Instance
                && ShipStatus.Instance.Pointer == Ship && ReferenceEquals(Main.PlayerStates, PlayerStates);
        }
    }

    private sealed record Actor(PlayerControl Player, byte Id, int Owner, nint Pointer)
    {
        internal bool Current()
        {
            if (!Player || Player.Data == null || Player.Data.Disconnected
                || Player.PlayerId != Id || Player.OwnerId != Owner || Player.Pointer != Pointer) return false;
            var current = Utils.GetPlayerById(Id);
            var client = Utils.GetClientById(Owner);
            return current && current.Pointer == Pointer && client?.Character != null
                && client.Character.Pointer == Pointer;
        }
    }

    private sealed class Pending(Context context, Actor target)
    {
        internal readonly Context Context = context;
        internal readonly Actor Target = target;
        internal readonly Vector2 RequestedPosition = target.Player.GetCustomPosition();
        internal float ReadyAt = float.PositiveInfinity;
        internal Actor Ghost;
        internal SystemTypes System;
        internal Vector2 TargetPosition, GhostPosition;
        internal float KillTimer;
        internal bool TargetMoved, GhostMoved;
        internal bool Started;
    }

    private static readonly Dictionary<byte, Pending> Requests = [];
    private static Actor Capture(PlayerControl player) => new(player, player.PlayerId, player.OwnerId, player.Pointer);
    private static bool InGame() => AmongUsClient.Instance && AmongUsClient.Instance.AmConnected
        && AmongUsClient.Instance.AmHost && GameStates.IsInGame && !GameStates.IsEnded && ShipStatus.Instance;

    internal static string Request(PlayerControl target)
    {
        Tick();
        if (!InGame()) return "BlackScreenFixNotInGame";
        if (!target || target.Data == null || target.OwnerId < 0 || !Capture(target).Current()) return "FeatureInvalidTarget";
        if (Requests.ContainsKey(target.PlayerId) || IsRepairing(target)) return "BlackScreenFixPending";

        // Use the role this client was assigned to see, including desynchronized killer roles.
        if (MeetingStates.FirstMeeting)
        {
            RoleTypes role;
            if (RpcSetRoleReplacer.RoleMap.TryGetValue((target.PlayerId, target.PlayerId), out var mapped)) role = mapped.Item1;
            else if (RpcSetRoleReplacer.StoragedData == null
                || !RpcSetRoleReplacer.StoragedData.TryGetValue(target.PlayerId, out role))
                return "BlackScreenFixRoleUnavailable";
            try
            {
                target.RpcSetRoleDesync(role, target.OwnerId);
                return "BlackScreenFixRoleResent";
            }
            catch (Exception error)
            {
                Logger.Warn($"Role resync failed for player {target.PlayerId}: {error.GetType().Name}", "BlackScreenFix");
                return "BlackScreenFixFailed";
            }
        }
        if (target.IsModded()) return "BlackScreenFixModded";

        var client = AmongUsClient.Instance;
        var context = new Context(OnGameJoinedPatch.Generation, client.Pointer, client.GameId,
            client.HostId, ShipStatus.Instance.Pointer, Main.PlayerStates);
        var request = new Pending(context, Capture(target));
        Requests.Add(target.PlayerId, request);
        if (Ready(target) && FindGhost() is { } ghost)
        {
            try
            {
                Begin(request, ghost);
                return "BlackScreenFixStarted";
            }
            catch (Exception error)
            {
                Fail(request, error);
                return "BlackScreenFixFailed";
            }
        }
        return FindGhost() == null ? "FixBlackScreenWaitForDead" : "BlackScreenFixWaiting";
    }

    private static bool Ready(PlayerControl player) => GameStates.IsInTask && !ExileController.Instance
        && !AntiBlackout.SkipTasks && !AntiBlackout.IsCached && !player.inVent && !player.walkingToVent
        && !player.onLadder && !player.inMovingPlat && player.MyPhysics != null
        && !player.MyPhysics.Animations.IsPlayingEnterVentAnimation()
        && !player.MyPhysics.Animations.IsPlayingAnyLadderAnimation();

    private static PlayerControl FindGhost() => Main.AllPlayerControls.FirstOrDefault(player =>
        player && player.Data != null && player.Data.IsDead && !player.IsAlive()
        && player.OwnerId >= 0 && Capture(player).Current() && !IsRepairing(player));

    internal static bool IsRepairing(PlayerControl player) => player && Requests.Values.Any(request =>
        request.Started && (request.Target.Pointer == player.Pointer || request.Ghost?.Pointer == player.Pointer));

    internal static void CancelWaiting(PlayerControl player)
    {
        if (player && Requests.TryGetValue(player.PlayerId, out var request) && !request.Started
            && request.Target.Pointer == player.Pointer)
        {
            Requests.Remove(player.PlayerId);
            Logger.Info($"Canceled queued black screen repair for player {player.PlayerId} after activity", "BlackScreenFix");
        }
    }

    // Called from HUD update, so waits use real time and always recheck the room and player identities.
    internal static void Tick()
    {
        foreach (var request in Requests.Values.ToArray())
        {
            // A previous room/round must never teleport an object reused by the next round.
            if (!request.Context.Current()) { Requests.Remove(request.Target.Id); continue; }
            if (!InGame())
            {
                try { RestorePositions(request); }
                catch (Exception error) { Logger.Warn(error.GetType().Name, "BlackScreenFix.GameEnd"); }
                finally { Requests.Remove(request.Target.Id); }
                continue;
            }
            if (!request.Target.Current())
            {
                try { if (request.Started) RestorePositions(request); }
                catch (Exception error) { Logger.Warn(error.GetType().Name, "BlackScreenFix.Disconnect"); }
                finally { Requests.Remove(request.Target.Id); }
                continue;
            }
            try
            {
                if (request.Started)
                {
                    if (Time.realtimeSinceStartup >= request.ReadyAt)
                    {
                        Finish(request);
                        Requests.Remove(request.Target.Id);
                    }
                    continue;
                }
                if (request.Target.Player.IsModded())
                {
                    Requests.Remove(request.Target.Id);
                    continue;
                }
                if (IsRepairing(request.Target.Player))
                {
                    request.ReadyAt = float.PositiveInfinity;
                    continue;
                }
                if ((request.Target.Player.GetCustomPosition() - request.RequestedPosition).sqrMagnitude >= 0.0025f)
                {
                    CancelWaiting(request.Target.Player);
                    continue;
                }
                var ghost = FindGhost();
                if (!Ready(request.Target.Player) || ghost == null)
                {
                    request.ReadyAt = float.PositiveInfinity;
                    continue;
                }
                if (float.IsPositiveInfinity(request.ReadyAt))
                    request.ReadyAt = Time.realtimeSinceStartup + (request.Target.Player.IsAlive() ? 1f : 3f);
                if (Time.realtimeSinceStartup >= request.ReadyAt) Begin(request, ghost);
            }
            catch (Exception error)
            {
                Fail(request, error);
            }
        }
    }

    private static void Fail(Pending request, Exception error)
    {
        Logger.Warn($"Black screen repair failed for player {request.Target.Id}: {error.GetType().Name}", "BlackScreenFix");
        try { if (request.Started && request.Target.Current()) Finish(request, notify: false); else RestorePositions(request); }
        catch (Exception cleanupError) { Logger.Warn(cleanupError.GetType().Name, "BlackScreenFix.Cleanup"); }
        finally { Requests.Remove(request.Target.Id); }
    }

    private static void Begin(Pending request, PlayerControl ghost)
    {
        var target = request.Target.Player;
        request.Ghost = Capture(ghost);
        request.System = Utils.GetActiveMapId() switch
        {
            2 => SystemTypes.Laboratory,
            4 => SystemTypes.HeliSabotage,
            _ => SystemTypes.Reactor
        };
        request.TargetPosition = target.GetCustomPosition();
        request.GhostPosition = ghost.GetCustomPosition();
        request.KillTimer = Math.Max(KillTimerManager.AllKillTimers.GetValueOrDefault(target.PlayerId), 0.1f);
        request.Started = true;
        request.ReadyAt = Time.realtimeSinceStartup + 1f + AmongUsClient.Instance.Ping / 1000f;
        target.RpcDesyncUpdateSystem(request.System, 128);
        if (target.IsAlive())
        {
            var position = Pelican.GetBlackRoomPSForPelican();
            request.TargetMoved = true;
            target.RpcTeleport(position, isRandomSpawn: true, sendInfoInLogs: false);
            SendMurderAnimation(target, ghost);
            request.GhostMoved = true;
            ghost.RpcTeleport(position, isRandomSpawn: true, sendInfoInLogs: false);
        }
        else SendMurderAnimation(target, target);
        Logger.Info($"Started black screen repair for player {target.PlayerId}", "BlackScreenFix");
    }

    private static void SendMurderAnimation(PlayerControl target, PlayerControl victim)
    {
        // Only the affected vanilla client receives this replay; host death/role/task state stays authoritative.
        var client = AmongUsClient.Instance;
        var writer = client.StartRpcImmediately(target.NetId, (byte)RpcCalls.MurderPlayer, SendOption.Reliable, target.OwnerId);
        writer.WriteNetObject(victim);
        writer.Write((int)MurderResultFlags.Succeeded);
        client.FinishRpcImmediately(writer);
    }

    private static void RestorePositions(Pending request)
    {
        Exception failure = null;
        if (request.TargetMoved)
        {
            try
            {
                if (request.Target.Current()) request.Target.Player.RpcTeleport(request.TargetPosition, isRandomSpawn: true, sendInfoInLogs: false);
                request.TargetMoved = false;
            }
            catch (Exception error) { failure = error; }
        }
        if (request.GhostMoved)
        {
            try
            {
                if (request.Ghost.Current()) request.Ghost.Player.RpcTeleport(request.GhostPosition, isRandomSpawn: true, sendInfoInLogs: false);
                request.GhostMoved = false;
            }
            catch (Exception error) { failure ??= error; }
        }
        if (failure != null) throw failure;
    }

    private static void Finish(Pending request, bool notify = true)
    {
        var target = request.Target.Player;
        try
        {
            target.RpcDesyncUpdateSystem(request.System, 16);
            if (request.System == SystemTypes.HeliSabotage) target.RpcDesyncUpdateSystem(request.System, 17);
        }
        finally { RestorePositions(request); }
        if (target.IsAlive()) RestoreKillTimer(target, request.KillTimer);
        AfkMonitor.RecordActivity(target);
        if (notify)
        {
            target.Notify(Translator.GetString("BlackScreenFixCompleteNotify"));
            Logger.Info($"Finished black screen repair for player {target.PlayerId}", "BlackScreenFix");
        }
    }

    private static void RestoreKillTimer(PlayerControl target, float timer)
    {
        if (!target.HasImpKillButton(considerVanillaShift: true)) return;
        // Reset the native timer without invoking role-specific SetKillCooldown hooks or Observer effects.
        var hadCooldown = Main.AllPlayerKillCooldown.TryGetValue(target.PlayerId, out var cooldown);
        try
        {
            Main.AllPlayerKillCooldown[target.PlayerId] = timer * 2f;
            target.SyncSettings();
            target.RpcGuardAndKill(forObserver: true, fromSetKCD: true);
            KillTimerManager.AllKillTimers[target.PlayerId] = timer;
        }
        finally
        {
            if (hadCooldown) Main.AllPlayerKillCooldown[target.PlayerId] = cooldown;
            else Main.AllPlayerKillCooldown.Remove(target.PlayerId);
            target.MarkDirtySettings();
        }
    }
}
