using System;
using InnerNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace TOHEE.RuntimeSmoke;

// Test-only, fixed native UI actions for an already owned two-player loopback game.
internal sealed class MeetingSmoke(Func<bool> ownsLocalSession)
{
    private string command = "";
    private int stage;
    private double started, stageStarted;
    private IntPtr clientPointer, playerPointer, meetingPointer;
    private int clientId, hostId;
    private SystemConsole? emergencyConsole;
    private EmergencyMinigame? emergency;
    private MeetingHud? meeting;
    private bool skipSelected, confirmClicked, resultsSeen, proceedClicked, exited;
    private int observedSkipVotes, observedPlayerVotes;

    internal bool Busy { get; private set; }
    internal string Outcome { get; private set; } = "idle";
    internal string Detail { get; private set; } = "";
    internal static bool IsCommand(string value) => value is "local_meeting" or "local_meeting_request" or "local_skip";

    internal bool Begin(string value, double now, out string rejection)
    {
        rejection = "";
        if (Busy) { rejection = "meeting_operation_busy"; return false; }
        if (!IsCommand(value)) { rejection = "invalid_meeting_command"; return false; }
        if (!ownsLocalSession() || !ContextReady())
        { rejection = "requires_owned_two_alive_player_started_loopback_local_game"; return false; }
        var client = AmongUsClient.Instance;
        if (value != "local_skip")
        {
            if (client.AmHost || MeetingHud.Instance || ExileController.Instance || Minigame.Instance ||
                !PlayerControl.LocalPlayer.CanMove)
            { rejection = "requires_nonhost_gameplay_without_open_ui"; return false; }
            if (ShipStatus.Instance.Timer < 15 || ShipStatus.Instance.EmergencyCooldown > 0 ||
                PlayerControl.LocalPlayer.RemainingEmergencies <= 0 || HasEmergencyTask())
            { rejection = "native_emergency_cooldown_crisis_or_remaining_count_blocks_meeting"; return false; }
            if (value == "local_meeting")
            {
                emergencyConsole = FindUsableEmergencyConsole();
                if (emergencyConsole == null)
                { rejection = "requires_native_emergency_console_in_role_allowed_use_range"; return false; }
            }
            else if (!TOHE.Main.PlayerStates.TryGetValue(PlayerControl.LocalPlayer.PlayerId, out var state) ||
                     state.MainRole != TOHE.CustomRoles.CrewmateTOHE)
            { rejection = "fixed_native_request_requires_ordinary_mod_crewmate"; return false; }
        }
        else
        {
            meeting = MeetingHud.Instance;
            if (!meeting || !meeting.gameObject.activeInHierarchy ||
                meeting.CurrentState is not (MeetingHud.MeetingStates.Animating or
                    MeetingHud.MeetingStates.Discussion or MeetingHud.MeetingStates.NotVoted))
            { rejection = "requires_unvoted_native_meeting"; return false; }
        }
        command = value;
        clientPointer = client.Pointer;
        clientId = client.ClientId;
        hostId = client.HostId;
        playerPointer = PlayerControl.LocalPlayer.Pointer;
        meetingPointer = value == "local_skip" ? meeting!.Pointer : IntPtr.Zero;
        emergency = null;
        stage = 0;
        started = stageStarted = now;
        skipSelected = confirmClicked = resultsSeen = proceedClicked = exited = false;
        observedSkipVotes = observedPlayerVotes = 0;
        Busy = true;
        Outcome = "running";
        Detail = "fixed_native_meeting_ui_sequence";
        return true;
    }

    internal bool Tick(double now)
    {
        if (!Busy) return true;
        try
        {
            if (!StillOwned()) return Finish("failed", "owned_two_player_local_session_lost_or_player_ejected");
            double limit = command != "local_skip" && stage == 2 || command == "local_skip" && stage == 3 ? 15 : 8;
            if (now - started >= 30 || now - stageStarted >= limit)
                return Finish("timed_out", "native_meeting_observation_timeout_no_retry");
            return command != "local_skip" ? CallMeeting(now) : SkipVote(now);
        }
        catch (Exception ex) { return Finish("failed", "native_meeting_sequence_" + ex.GetType().Name); }
    }

    private bool CallMeeting(double now)
    {
        if (AmongUsClient.Instance.AmHost) return Finish("failed", "meeting_caller_became_host");
        if (stage == 0)
        {
            if (command == "local_meeting_request")
            {
                // Client-request integration coverage only. This is the native
                // player request used by EmergencyMinigame; it is not a UI test.
                if (MeetingHud.Instance || Minigame.Instance || HasEmergencyTask() ||
                    ShipStatus.Instance.Timer < 15 || ShipStatus.Instance.EmergencyCooldown > 0 ||
                    PlayerControl.LocalPlayer.RemainingEmergencies <= 0)
                    return Finish("rejected", "native_emergency_request_conditions_changed");
                PlayerControl.LocalPlayer.CmdReportDeadBody(null);
                Advance(2, now);
                return false;
            }
            if (MeetingHud.Instance || Minigame.Instance || emergencyConsole == null || !emergencyConsole ||
                !ConsoleUsable(emergencyConsole))
                return Finish("failed", "native_emergency_console_no_longer_usable");
            // Same native handler as UseClosest: CanUse is checked again by SystemConsole.Use.
            emergencyConsole.Use();
            Advance(1, now);
        }
        else if (stage == 1)
        {
            emergency = Minigame.Instance ? Minigame.Instance.TryCast<EmergencyMinigame>() : null;
            if (emergency == null || !emergency) return false;
            if (emergency.amOpening) return false;
            if (!emergency.gameObject.activeInHierarchy || !emergency.ButtonActive || HasEmergencyTask() ||
                ShipStatus.Instance.Timer < 15 || ShipStatus.Instance.EmergencyCooldown > 0 ||
                PlayerControl.LocalPlayer.RemainingEmergencies <= 0)
                return Finish("rejected", "native_emergency_ui_not_ready_or_meeting_disabled");
            // Fixed native emergency button handler; it retains crisis/count/ButtonActive guards.
            emergency.CallMeeting();
            Advance(2, now);
        }
        else if (stage == 2 && MeetingHud.Instance)
        {
            meeting = MeetingHud.Instance;
            if (meetingPointer == IntPtr.Zero) meetingPointer = meeting.Pointer;
            if (meeting.Pointer != meetingPointer) return Finish("failed", "native_meeting_replaced");
            if (meeting.CurrentState is MeetingHud.MeetingStates.Discussion or MeetingHud.MeetingStates.NotVoted)
                return Finish("succeeded", "nonhost_native_emergency_meeting_ready_issue_local_skip_on_both_clients");
        }
        return false;
    }

    private bool SkipVote(double now)
    {
        if (MeetingHud.Instance && MeetingHud.Instance.Pointer != meetingPointer)
            return Finish("failed", "native_meeting_replaced");
        if (stage < 3 && (!meeting || MeetingHud.Instance != meeting))
            return Finish("failed", "native_meeting_lost_before_two_skip_results");
        if (stage == 0)
        {
            if (meeting!.CurrentState is MeetingHud.MeetingStates.Animating or MeetingHud.MeetingStates.Discussion)
                return false;
            if (meeting.CurrentState != MeetingHud.MeetingStates.NotVoted || !meeting.SkipVoteButton ||
                (byte)meeting.SkipVoteButton.PlayerId != 253 || meeting.SkipVoteButton.Parent != meeting)
                return Finish("rejected", "native_skip_not_available");
            var button = meeting.SkipVoteButton.PlayerButton?.TryCast<PassiveButton>();
            if (!ButtonReady(button)) return Finish("rejected", "native_skip_button_not_enabled");
            button!.ReceiveClickUp();
            skipSelected = true;
            Advance(1, now);
        }
        else if (stage == 1)
        {
            var skip = meeting!.SkipVoteButton;
            if (meeting.CurrentState != MeetingHud.MeetingStates.NotVoted || !skip || !skip.Buttons ||
                !skip.Buttons.activeInHierarchy)
                return Finish("rejected", "native_skip_selection_not_open");
            // Allow the normal selection animation to expose the confirm button.
            if (now - stageStarted < 0.5) return false;
            var confirm = skip.ConfirmButton?.TryCast<PassiveButton>();
            if (!ButtonReady(confirm)) return Finish("rejected", "native_skip_confirm_not_enabled");
            confirm!.ReceiveClickUp();
            confirmClicked = true;
            Advance(2, now);
        }
        else if (stage == 2)
        {
            if (meeting!.CurrentState != MeetingHud.MeetingStates.Results) return false;
            // VoteSpreader is populated from native VotingComplete's voter states. This
            // verifies the final result on each client, including anonymous voting.
            var skipVotes = meeting.SkippedVoting ? meeting.SkippedVoting.GetComponent<VoteSpreader>() : null;
            if (skipVotes == null || !skipVotes || skipVotes.Votes == null)
                return Finish("failed", "native_skip_result_spreader_unavailable");
            observedSkipVotes = skipVotes.Votes.Count;
            observedPlayerVotes = 0;
            int areas = 0;
            foreach (var area in meeting.GetComponentsInChildren<PlayerVoteArea>(true))
            {
                if (area == meeting.SkipVoteButton || area.Parent != meeting || (byte)area.PlayerId >= 252) continue;
                areas++;
                var votes = area.GetComponent<VoteSpreader>();
                if (votes && votes.Votes != null) observedPlayerVotes += votes.Votes.Count;
            }
            if (observedSkipVotes > 2 || observedPlayerVotes != 0)
                return Finish("failed", "native_results_are_not_exactly_two_skip_votes");
            // Results changes before the native staggered vote-icon animation
            // has populated both votes. Observe it without advancing the UI.
            if (areas != 2 || observedSkipVotes != 2) return false;
            resultsSeen = true;
            Advance(3, now);
        }
        else if (stage == 3)
        {
            if (MeetingHud.Instance && !proceedClicked && AmongUsClient.Instance.AmHost)
            {
                // LocalGame results require the host's ordinary Proceed button.
                if (MeetingHud.Instance.CurrentState != MeetingHud.MeetingStates.Results ||
                    !ButtonReady(MeetingHud.Instance.ProceedButton)) return false;
                MeetingHud.Instance.ProceedButton.ReceiveClickUp();
                proceedClicked = true;
            }
            if (!MeetingHud.Instance && !ExileController.Instance && !Minigame.Instance &&
                PlayerControl.LocalPlayer.CanMove && !HudManager.Instance.IsIntroDisplayed)
            {
                exited = true;
                return Finish("succeeded", "two_native_skip_votes_no_ejection_and_meeting_exited");
            }
        }
        return false;
    }

    private static bool LoopbackLocal()
    {
        var client = AmongUsClient.Instance;
        return client && client.NetworkMode == NetworkModes.LocalGame && client.GameId == 32 &&
            client.GetNetworkAddress() == "127.0.0.1" && client.GetNetworkPort() == 22023;
    }

    private static bool ContextReady()
    {
        var client = AmongUsClient.Instance;
        if (!LoopbackLocal() || !client.AmConnected || client.GameState != InnerNetClient.GameStates.Started ||
            SceneManager.GetActiveScene().name != "OnlineGame" || !GameData.Instance ||
            GameData.Instance.PlayerCount != 2 || !ShipStatus.Instance || !PlayerControl.LocalPlayer ||
            !DestroyableSingleton<HudManager>.InstanceExists || HudManager.Instance.IsIntroDisplayed ||
            !TOHE.GameStates.IsNormalGame || !GameManager.Instance || GameManager.Instance.LogicUsables == null)
            return false;
        int players = 0;
        foreach (var player in GameData.Instance.AllPlayers)
        {
            if (player == null || player.Disconnected || player.IsDead || player.WasEjected || !player.Object || !player.Role)
                return false;
            players++;
        }
        return players == 2;
    }

    private bool StillOwned() => ownsLocalSession() && ContextReady() &&
        AmongUsClient.Instance.Pointer == clientPointer && AmongUsClient.Instance.ClientId == clientId &&
        AmongUsClient.Instance.HostId == hostId && PlayerControl.LocalPlayer.Pointer == playerPointer;

    private static bool HasEmergencyTask()
    {
        foreach (var task in PlayerControl.LocalPlayer.myTasks)
            if (PlayerTask.TaskIsEmergency(task)) return true;
        return false;
    }

    private static bool ConsoleUsable(SystemConsole console)
    {
        if (!console || !console.isActiveAndEnabled || !console.gameObject.activeInHierarchy || console.FreeplayOnly ||
            !console.MinigamePrefab || console.MinigamePrefab.TryCast<EmergencyMinigame>() == null ||
            !GameManager.Instance.LogicUsables.CanUse(console.Cast<IUsable>(), PlayerControl.LocalPlayer)) return false;
        console.CanUse(PlayerControl.LocalPlayer.Data, out bool canUse, out bool couldUse);
        return canUse && couldUse;
    }

    private static SystemConsole? FindUsableEmergencyConsole()
    {
        SystemConsole? result = null;
        foreach (var console in UObject.FindObjectsOfType<SystemConsole>())
        {
            if (console.gameObject.scene.name != "OnlineGame" || !ConsoleUsable(console)) continue;
            if (result != null) return null;
            result = console;
        }
        return result;
    }

    private static bool ButtonReady(PassiveButton? button) => button != null && button &&
        button.isActiveAndEnabled && button.gameObject.activeInHierarchy && !button.HoldToUse;

    private void Advance(int next, double now) { stage = next; stageStarted = now; }
    internal void Stop(string outcome, string detail) => Finish(outcome, detail);
    private bool Finish(string outcome, string detail)
    {
        // Close only the emergency UI opened here, using its ordinary close handler.
        if (emergency != null && emergency && Minigame.Instance == emergency && StillOwned() && !MeetingHud.Instance)
            emergency.Close();
        Busy = false;
        Outcome = outcome;
        Detail = detail;
        return true;
    }

    internal object Snapshot() => new
    {
        state = Outcome, detail = Detail, command, stage = Busy ? (int?)stage : null,
        loopback_local = LoopbackLocal(), owned_local_session = ownsLocalSession(),
        two_alive_player_context = ContextReady(),
        local_is_host = AmongUsClient.Instance && AmongUsClient.Instance.AmHost,
        meeting_state = MeetingHud.Instance ? MeetingHud.Instance.CurrentState.ToString() : null,
        skip_selected = skipSelected, confirm_clicked = confirmClicked,
        results_observed = resultsSeen, skip_result_vote_count = observedSkipVotes,
        player_result_vote_count = observedPlayerVotes, native_host_proceed_clicked = proceedClicked,
        no_ejection_meeting_exit_observed = exited
    };
}
