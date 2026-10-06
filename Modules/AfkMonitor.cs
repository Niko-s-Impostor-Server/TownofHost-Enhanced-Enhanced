using System;
using System.Text;
using Hazel;
using UnityEngine;

namespace TOHE;

public static class AfkMonitor
{
    private const double GraceSeconds = 15d;
    private const double PollSeconds = 0.5d;
    private static readonly AfkInactivityPolicy Policy = new();
    private static OptionItem enabled, threshold, consequence, minimumAlive, activateOnStart, exemptAdministrators;
    private static uint generation;
    private static nint clientPointer, gamePointer;
    private static int gameId, hostId;
    private static object playerStates;
    private static double nextPoll;
    private static bool collectiveFailure;
    private static readonly Dictionary<byte, string> displayedStatus = [];
    private static readonly Dictionary<AfkIdentity, string> remoteStatus = [];

    public static void SetupCustomOptions()
    {
        enabled = BooleanOptionItem.Create(61050, "AfkMonitor.Enabled", false, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Standard).SetHeader(true);
        threshold = IntegerOptionItem.Create(61051, "AfkMonitor.Threshold", new(30, 600, 15), 180, TabGroup.ModSettings, false)
            .SetParent(enabled).SetValueFormat(OptionFormat.Seconds);
        consequence = StringOptionItem.Create(61052, "AfkMonitor.Consequence",
            ["AfkMonitor.WarningMode", "AfkMonitor.ShieldMode", "AfkMonitor.KickMode", "AfkMonitor.SuicideMode"], 0, TabGroup.ModSettings, false)
            .SetParent(enabled);
        minimumAlive = IntegerOptionItem.Create(61053, "AfkMonitor.MinimumAlive", new(2, 15, 1), 3, TabGroup.ModSettings, false)
            .SetParent(enabled).SetValueFormat(OptionFormat.Players);
        activateOnStart = BooleanOptionItem.Create(61054, "AfkMonitor.ActivateOnStart", false, TabGroup.ModSettings, false).SetParent(enabled);
        exemptAdministrators = BooleanOptionItem.Create(61055, "AfkMonitor.ExemptAdministrators", false, TabGroup.ModSettings, false).SetParent(enabled);
    }

    public static void Reset()
    {
        Policy.Reset();
        generation = 0;
        clientPointer = gamePointer = 0;
        gameId = hostId = 0;
        playerStates = null;
        nextPoll = 0;
        collectiveFailure = false;
        displayedStatus.Clear();
        remoteStatus.Clear();
    }

    private static void EnsureSession()
    {
        var client = AmongUsClient.Instance;
        var currentGame = GameManager.Instance;
        var pointer = currentGame == null ? IntPtr.Zero : currentGame.Pointer;
        if (clientPointer == client.Pointer && generation == OnGameJoinedPatch.Generation
            && gameId == client.GameId && hostId == client.HostId && gamePointer == pointer
            && ReferenceEquals(playerStates, Main.PlayerStates)) return;
        Reset();
        clientPointer = client.Pointer;
        generation = OnGameJoinedPatch.Generation;
        gameId = client.GameId;
        hostId = client.HostId;
        gamePointer = pointer;
        playerStates = Main.PlayerStates;
    }

    private static bool HasHost() => AmongUsClient.Instance != null && AmongUsClient.Instance.AmConnected
        && AmongUsClient.Instance.AmHost;

    private static bool CanMonitor() => HasHost() && enabled?.GetBool() == true
        && GameStates.IsNormalGame && Options.CurrentGameMode == CustomGameMode.Standard
        && GameStates.IsInTask && !GameStates.IsEnded && Main.IntroDestroyed && Time.timeScale > 0f
        && !GameStates.IsExilling && !AntiBlackout.SkipTasks && !AntiBlackout.IsCached
        && (activateOnStart.GetBool() || !MeetingStates.FirstMeeting)
        && Main.AllAlivePlayerControls.Length >= minimumAlive.GetInt();

    private static bool CanonicalPlayer(PlayerControl player)
    {
        if (!HasHost() || player == null || player.Data == null || player.Data.Disconnected || player.OwnerId < 0) return false;
        var current = Utils.GetPlayerById(player.PlayerId);
        if (current == null || current.Pointer != player.Pointer || current.OwnerId != player.OwnerId) return false;
        var owner = AmongUsClient.Instance.FindClientById(player.OwnerId);
        return owner != null && owner.Character != null && owner.Character.Pointer == player.Pointer;
    }

    private static AfkIdentity Identity(PlayerControl player) => new(player.PlayerId, player.OwnerId, player.Pointer);

    private static bool IsPaused(PlayerControl player) => player.inVent || player.walkingToVent
        || player.onLadder || player.inMovingPlat || !player.moveable || player.MyPhysics == null
        || player.MyPhysics.Animations.IsPlayingEnterVentAnimation()
        || player.MyPhysics.Animations.IsPlayingAnyLadderAnimation();

    // Called from the host HUD update. No private coroutine or repeated delayed callbacks.
    public static void Tick()
    {
        if (!HasHost())
        {
            if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmConnected || !GameStates.IsInGame) Reset();
            else EnsureSession();
            return;
        }
        EnsureSession();
        var now = (double)Time.realtimeSinceStartup;
        if (now < nextPoll) return;
        nextPoll = now + PollSeconds;
        if (!CanMonitor())
        {
            Policy.ResetTimers(now, GraceSeconds);
            foreach (var id in displayedStatus.Keys.ToArray()) BroadcastStatus(id, "");
            displayedStatus.Clear();
            return;
        }

        HashSet<AfkIdentity> live = [];
        var players = Main.AllPlayerControls.Where(player => CanonicalPlayer(player) && player.IsAlive()).ToArray();
        bool refreshNames = false;
        void UpdateDisplay(PlayerControl player)
        {
            var suffix = GetSuffix(player, player);
            if (displayedStatus.GetValueOrDefault(player.PlayerId, "") == suffix) return;
            displayedStatus[player.PlayerId] = suffix;
            BroadcastStatus(player.PlayerId, suffix);
            refreshNames = true;
        }
        // Observe everybody before any destructive action changes the roster.
        foreach (var player in players)
        {
            var identity = Identity(player);
            live.Add(identity);
            if (BlackScreenFix.IsRepairing(player)) { Policy.Freeze(identity, now); UpdateDisplay(player); continue; }
            var position = player.GetTruePosition();
            var tasks = player.GetPlayerTaskState()?.CompletedTasksCount ?? 0;
            Policy.TryGetStatus(identity, out var previous);
            var pending = BlackScreenFix.IsPendingOrRepairing(player);
            bool paused = IsPaused(player) || exemptAdministrators.GetBool()
                && (player.OwnerId == AmongUsClient.Instance.HostId || ManagementCommands.IsAdministrator(player));
            bool newlyAfk = Policy.Observe(identity, position.x, position.y, tasks, now,
                paused, GraceSeconds, threshold.GetInt(), frozen: pending);
            Policy.TryGetStatus(identity, out var current);
            if (!paused && previous.IsAfk && !current.IsAfk) BlackScreenFix.CancelWaiting(player);
            if (newlyAfk)
            {
                var key = consequence.GetValue() switch
                {
                    1 => "AfkMonitor.ShieldNotice",
                    2 => "AfkMonitor.KickWarning",
                    3 => "AfkMonitor.SuicideWarning",
                    _ => "AfkMonitor.Warning"
                };
                player.Notify(Translator.GetString(key), 10f, sendInLog: false);
            }
            UpdateDisplay(player);
        }
        Policy.RemoveMissing(live);
        foreach (var id in displayedStatus.Keys.Where(id => !live.Any(identity => identity.PlayerId == id)).ToArray())
            displayedStatus.Remove(id);

        var warningCount = players.Count(player => Policy.TryGetStatus(Identity(player), out var state)
            && state.IsAfk && !state.Exempt && !state.ConsequenceSent);
        if (!collectiveFailure && AfkInactivityPolicy.CollectiveFailure(players.Length, warningCount))
        {
            collectiveFailure = true;
            Utils.SendMessage(Translator.GetString("AfkMonitor.CollectiveFailure"));
            Logger.Warn("AFK automatic penalties suspended for this round after collective inactivity", "AfkMonitor");
        }
        foreach (var player in players)
        {
            var identity = Identity(player);
            if (!Policy.TryGetStatus(identity, out var status) || !status.IsAfk || status.Exempt) continue;
            if (!status.RepairAttempted && !MeetingStates.FirstMeeting && !player.IsModded() && !IsPaused(player))
            {
                Policy.MarkRepairAttempted(identity);
                BlackScreenFix.RequestAutomatic(player);
                UpdateDisplay(player);
            }
            if (collectiveFailure || BlackScreenFix.IsPendingOrRepairing(player) || IsPaused(player) || status.ConsequenceSent
                || status.IdleSeconds < threshold.GetInt() + GraceSeconds || player.OwnerId == AmongUsClient.Instance.HostId || player.AmOwner) continue;
            if ((AfkConsequence)consequence.GetValue() is not (AfkConsequence.Kick or AfkConsequence.Suicide)) continue;
            Policy.MarkConsequenceSent(identity);
            if ((AfkConsequence)consequence.GetValue() == AfkConsequence.Kick) AmongUsClient.Instance.KickPlayer(player.OwnerId, false);
            else
            {
                player.SetDeathReason(PlayerState.DeathReason.Suicide);
                player.SetRealKiller(player);
                player.RpcMurderPlayer(player);
            }
        }
        if (refreshNames) Utils.NotifyRoles(NoCache: true);
    }

    public static string GetSuffix(PlayerControl seer, PlayerControl target)
    {
        if (!HasHost())
        {
            if (!GameStates.IsInTask || !seer || !target || target.Data == null || target.Data.Disconnected || !target.IsAlive()
                || !remoteStatus.TryGetValue(Identity(target), out var suffix) || suffix.Length == 0) return "";
            return seer.PlayerId == target.PlayerId ? suffix : "<color=#ffb347>AFK</color>";
        }
        if (!CanMonitor() || !seer || !CanonicalPlayer(target) || !target.IsAlive()
            || !Policy.TryGetStatus(Identity(target), out var status) || !status.IsAfk || status.Exempt) return "";
        if (seer.PlayerId != target.PlayerId) return "<color=#ffb347>AFK</color>";
        if (BlackScreenFix.IsPendingOrRepairing(target)) return Translator.GetString("AfkMonitor.Repairing");
        var remaining = Math.Max(0, (int)Math.Ceiling(threshold.GetInt() + GraceSeconds - status.IdleSeconds));
        return remaining > 0 ? string.Format(Translator.GetString("AfkMonitor.Countdown"), remaining) : "<color=#ffb347>AFK</color>";
    }

    private static void BroadcastStatus(byte id, string suffix)
    {
        if (!ShipStatus.Instance || !Main.AllPlayerControls.Any(player => player.IsNonHostModdedClient())) return;
        var writer = CustomRpcTransport.Start(CustomRPC.SyncAfkState);
        writer.Write(AmongUsClient.Instance.GameId);
        writer.Write(ShipStatus.Instance.NetId);
        writer.Write(id);
        writer.Write(suffix);
        CustomRpcTransport.Finish(writer);
    }

    internal static void ReceiveState(PlayerControl sender, MessageReader reader)
    {
        var client = AmongUsClient.Instance;
        if (!client || client.AmHost || !sender || sender.OwnerId != client.HostId || !GameStates.IsInGame || !ShipStatus.Instance) return;
        if (reader.ReadInt32() != client.GameId || reader.ReadUInt32() != ShipStatus.Instance.NetId) return;
        var player = Utils.GetPlayerById(reader.ReadByte());
        var suffix = reader.ReadString();
        if (!player || player.Data == null || player.Data.Disconnected || suffix.Length > 256) return;
        EnsureSession();
        if (suffix.Length == 0) remoteStatus.Remove(Identity(player));
        else remoteStatus[Identity(player)] = suffix;
    }

    public static void RecordActivity(PlayerControl player)
    {
        if (!CanonicalPlayer(player)) return;
        BlackScreenFix.CancelWaiting(player);
        EnsureSession();
        Policy.RecordActivity(Identity(player), Time.realtimeSinceStartup);
        if (displayedStatus.Remove(player.PlayerId))
        {
            BroadcastStatus(player.PlayerId, "");
            Utils.NotifyRoles(NoCache: true);
        }
    }

    public static bool SetExempt(PlayerControl player, bool exempt)
    {
        if (!CanonicalPlayer(player)) return false;
        EnsureSession();
        Policy.SetExempt(Identity(player), exempt, Time.realtimeSinceStartup, GraceSeconds);
        if (exempt) BlackScreenFix.CancelWaiting(player);
        Utils.NotifyRoles(NoCache: true);
        return true;
    }

    public static bool IsShielded(PlayerControl player)
    {
        if (!CanMonitor() || consequence.GetValue() != 1 || !CanonicalPlayer(player)
            || !player.IsAlive() || IsPaused(player)) return false;
        EnsureSession();
        return Policy.TryGetStatus(Identity(player), out var status) && status.IsAfk && !status.Exempt;
    }

    public static string GetStatus(PlayerControl player)
    {
        if (!CanonicalPlayer(player)) return Translator.GetString("AfkMonitor.Unavailable");
        EnsureSession();
        Policy.TryGetStatus(Identity(player), out var status);
        return Translator.GetString("AfkMonitor.Status")
            .Replace("{seconds}", ((int)status.IdleSeconds).ToString())
            .Replace("{state}", Translator.GetString(status.Exempt ? "AfkMonitor.Exempt"
                : status.IsAfk ? "AfkMonitor.Idle" : "AfkMonitor.Active"));
    }

    public static string GetStatus()
    {
        if (!HasHost()) return Translator.GetString("AfkMonitor.Unavailable");
        StringBuilder text = new();
        foreach (var player in Main.AllPlayerControls)
            if (CanonicalPlayer(player) && player.IsAlive())
                text.AppendLine($"{player.PlayerId}: {GetStatus(player)}");
        return text.ToString().TrimEnd();
    }
}
