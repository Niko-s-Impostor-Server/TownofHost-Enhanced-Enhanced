using System;
using System.Text;
using UnityEngine;

namespace TOHE;

public static class AfkMonitor
{
    private const double GraceSeconds = 15d;
    private const double PollSeconds = 0.5d;
    private static readonly AfkInactivityPolicy Policy = new();
    private static OptionItem enabled, threshold, consequence, minimumAlive;
    private static uint generation;
    private static nint clientPointer, gamePointer;
    private static int gameId, hostId;
    private static object playerStates;
    private static double nextPoll;

    public static void SetupCustomOptions()
    {
        enabled = BooleanOptionItem.Create(61050, "AfkMonitor.Enabled", false, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Standard).SetHeader(true);
        threshold = IntegerOptionItem.Create(61051, "AfkMonitor.Threshold", new(30, 600, 15), 180, TabGroup.ModSettings, false)
            .SetParent(enabled).SetValueFormat(OptionFormat.Seconds);
        consequence = StringOptionItem.Create(61052, "AfkMonitor.Consequence",
            ["AfkMonitor.WarningMode", "AfkMonitor.ShieldMode", "AfkMonitor.KickMode"], 0, TabGroup.ModSettings, false)
            .SetParent(enabled);
        minimumAlive = IntegerOptionItem.Create(61053, "AfkMonitor.MinimumAlive", new(2, 15, 1), 3, TabGroup.ModSettings, false)
            .SetParent(enabled).SetValueFormat(OptionFormat.Players);
    }

    public static void Reset()
    {
        Policy.Reset();
        generation = 0;
        clientPointer = gamePointer = 0;
        gameId = hostId = 0;
        playerStates = null;
        nextPoll = 0;
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
        && !GameStates.IsExilling && !AntiBlackout.SkipTasks
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

    private static bool IsPaused(PlayerControl player) => BlackScreenFix.IsRepairing(player) || player.inVent || player.walkingToVent
        || player.onLadder || player.inMovingPlat || !player.moveable || player.MyPhysics == null
        || player.MyPhysics.Animations.IsPlayingEnterVentAnimation()
        || player.MyPhysics.Animations.IsPlayingAnyLadderAnimation();

    // Called from the host HUD update. No private coroutine or repeated delayed callbacks.
    public static void Tick()
    {
        if (!HasHost()) { Reset(); return; }
        EnsureSession();
        var now = (double)Time.realtimeSinceStartup;
        if (now < nextPoll) return;
        nextPoll = now + PollSeconds;
        if (!CanMonitor())
        {
            Policy.ResetTimers(now, GraceSeconds);
            return;
        }

        HashSet<AfkIdentity> live = [];
        foreach (var player in Main.AllPlayerControls)
        {
            if (!CanonicalPlayer(player) || !player.IsAlive()) continue;
            var identity = Identity(player);
            live.Add(identity);
            var position = player.GetTruePosition();
            var tasks = player.GetPlayerTaskState()?.CompletedTasksCount ?? 0;
            bool newlyAfk = Policy.Observe(identity, position.x, position.y, tasks, now,
                IsPaused(player), GraceSeconds, threshold.GetInt());
            if (newlyAfk)
            {
                var key = consequence.GetValue() switch
                {
                    1 => "AfkMonitor.ShieldNotice",
                    2 => "AfkMonitor.KickWarning",
                    _ => "AfkMonitor.Warning"
                };
                player.Notify(Translator.GetString(key), 10f, sendInLog: false);
            }
            if (consequence.GetValue() == 2 && Policy.TryGetStatus(identity, out var status)
                && status.IsAfk && !status.KickSent && status.IdleSeconds >= threshold.GetInt() + GraceSeconds
                && player.OwnerId != AmongUsClient.Instance.HostId && !player.AmOwner)
            {
                // Kick is opt-in, never bans, and never targets the host.
                Policy.MarkKickSent(identity);
                AmongUsClient.Instance.KickPlayer(player.OwnerId, false);
            }
        }
        Policy.RemoveMissing(live);
    }

    public static void RecordActivity(PlayerControl player)
    {
        if (!CanonicalPlayer(player)) return;
        BlackScreenFix.CancelWaiting(player);
        EnsureSession();
        Policy.RecordActivity(Identity(player), Time.realtimeSinceStartup);
    }

    public static bool SetExempt(PlayerControl player, bool exempt)
    {
        if (!CanonicalPlayer(player)) return false;
        EnsureSession();
        Policy.SetExempt(Identity(player), exempt, Time.realtimeSinceStartup, GraceSeconds);
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
