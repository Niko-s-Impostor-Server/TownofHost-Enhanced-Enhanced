using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Hazel;
using InnerNet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TOHEE.RuntimeSmoke;

// Fixed native two-crewmate vote/cutscene observer. Strings remain bounded in
// memory for equality comparisons and are never part of reports or exceptions.
internal sealed class ExileSmoke(Func<bool> ownsSession, string directory)
{
    private static ExileSmoke? active;
    private Harmony? hooks;
    private bool hooksInstalled, cleanupFailed;
    private string observerFailure = "";
    private double started, stageStarted, matchingStarted;
    private int stage, clientId, hostId, hostPlayerId;
    private IntPtr clientPointer, playerPointer, meetingPointer, exilePointer;
    private uint meetingNetId;
    private bool isHost;
    private MeetingHud? meeting;
    private PlayerVoteArea? target;
    private PlayerControl? hostPlayer;
    private readonly List<PlayerIdentity> players = new();
    private sealed record PlayerIdentity(PlayerControl Player, IntPtr Pointer, IntPtr Data, byte Id, int Owner);
    private string originalHostName = "", expectedText = "";
    private bool selected, confirmed, resultsSeen, delayElapsed, proceeded;
    private bool expectedObserved, completeMatched, tmpMatched, stableMatched, cutsceneSeen;
    private bool nameRestored, hostEjected, nativeExit;
    private bool revealPrefixObserved, captureRequested;
    private int hostVotes, otherVotes, skipVotes, matchingFrames, packetCount;
    private long errorsBefore;

    internal bool Busy { get; private set; }
    internal string Outcome { get; private set; } = "idle";
    internal string Detail { get; private set; } = "";
    internal static bool IsCommand(string value) => value == "local_exile";

    internal bool Begin(double now, out string rejection)
    {
        rejection = "";
        if (Busy || active != null || cleanupFailed)
        { rejection = "exile_observer_busy_or_requires_restart_after_cleanup_failure"; return false; }
        if (!ownsSession() || !Ready() || !TOHE.Options.IsLoaded || TOHE.Options.CEMode.GetInt() != 2)
        { rejection = "requires_owned_two_alive_mod_crewmates_started_loopback_and_existing_role_exile_mode"; return false; }
        var client = AmongUsClient.Instance;
        meeting = MeetingHud.Instance;
        if (!meeting || !meeting.gameObject.activeInHierarchy || ExileController.Instance ||
            meeting.CurrentState is not (MeetingHud.MeetingStates.Animating or
                MeetingHud.MeetingStates.Discussion or MeetingHud.MeetingStates.NotVoted))
        { rejection = "requires_unvoted_native_meeting_before_both_votes"; return false; }
        players.Clear(); hostPlayer = null;
        foreach (var data in GameData.Instance.AllPlayers)
        {
            var player = data.Object;
            if (!TOHE.Main.PlayerStates.TryGetValue(data.PlayerId, out var state) ||
                state.MainRole != TOHE.CustomRoles.CrewmateTOHE)
            { rejection = "requires_two_ordinary_mod_crewmates_without_role_vote_side_effects"; return false; }
            players.Add(new(player, player.Pointer, data.Pointer, data.PlayerId, player.OwnerId));
            if (player.OwnerId == client.HostId)
            {
                if (hostPlayer != null) { rejection = "native_host_player_not_unique"; return false; }
                hostPlayer = player;
            }
        }
        if (!hostPlayer || !TOHE.Main.AllPlayerNames.TryGetValue(hostPlayer!.PlayerId, out var realName) ||
            string.IsNullOrEmpty(realName) || realName.Length > 4096)
        { rejection = "native_host_real_name_baseline_unavailable"; return false; }
        originalHostName = realName;
        foreach (var area in meeting.playerStates)
            if (!area || area.DidVote)
            { rejection = "requires_both_native_vote_areas_unvoted_when_observer_starts"; return false; }

        clientPointer = client.Pointer; clientId = client.ClientId; hostId = client.HostId;
        playerPointer = PlayerControl.LocalPlayer.Pointer; hostPlayerId = hostPlayer.PlayerId;
        isHost = client.AmHost; meetingPointer = meeting.Pointer; meetingNetId = meeting.NetId;
        exilePointer = IntPtr.Zero; target = null; expectedText = ""; observerFailure = "";
        selected = confirmed = resultsSeen = delayElapsed = proceeded = false;
        expectedObserved = completeMatched = tmpMatched = stableMatched = cutsceneSeen = false;
        nameRestored = hostEjected = nativeExit = false;
        revealPrefixObserved = captureRequested = false;
        hostVotes = otherVotes = skipVotes = matchingFrames = packetCount = 0;
        started = stageStarted = now; matchingStarted = 0; stage = 0;
        errorsBefore = SmokeErrorCounter.Count;
        Outcome = "running"; Detail = "fixed_native_host_exile_vote_and_delayed_cutscene_observation";
        Busy = true;
        try
        {
            // A fixed attribute patch on the public native receive entry. It
            // copies the reader, never invokes a hidden production method.
            if (!isHost)
            {
                active = this;
                hooks = new Harmony("local.tohee.runtime-smoke.exile-text");
                hooksInstalled = true; // Record before setup for partial-failure cleanup.
                hooks.CreateClassProcessor(typeof(ExilePacketObserver)).Patch();
            }
            return true;
        }
        catch (Exception ex)
        {
            Finish("failed", "fixed_exile_observer_setup_" + ex.GetType().Name);
            rejection = Detail;
            return false;
        }
    }

    internal bool Tick(double now)
    {
        if (!Busy) return true;
        try
        {
            if (!StillOwned()) return Finish("failed", "owned_two_player_exile_context_identity_or_death_changed");
            if (observerFailure != "") return Finish("failed", observerFailure);
            if (SmokeErrorCounter.Count != errorsBefore) return Finish("failed", "error_severity_log_during_native_exile");
            double limit = stage switch { 0 => 30, 1 => 8, 2 => 20, 3 => 12, 4 => isHost ? 15 : 25, _ => 15 };
            if (now - started >= 60 || now - stageStarted >= limit)
                return Finish("timed_out", "bounded_native_exile_observation_no_retry");
            if (stage <= 3)
            {
                if (!MeetingHud.Instance || MeetingHud.Instance.Pointer != meetingPointer || !meeting)
                    return Finish("failed", "native_meeting_lost_before_delayed_host_proceed");
                return VoteAndProceed(now);
            }
            return ObserveCutscene(now);
        }
        catch (Exception ex) { return Finish("failed", "native_exile_sequence_" + ex.GetType().Name); }
    }

    private bool VoteAndProceed(double now)
    {
        if (stage == 0)
        {
            if (meeting!.CurrentState is MeetingHud.MeetingStates.Animating or MeetingHud.MeetingStates.Discussion)
                return false;
            if (meeting.CurrentState != MeetingHud.MeetingStates.NotVoted)
                return Finish("rejected", "native_vote_state_changed_before_fixed_selection");
            foreach (var area in meeting.playerStates)
                if (area && area.Parent == meeting && (byte)area.PlayerId == hostPlayerId)
                {
                    if (target != null) return Finish("failed", "native_host_vote_area_not_unique");
                    target = area;
                }
            if (!target || !ButtonReady(target!.PlayerButton?.TryCast<PassiveButton>()))
                return Finish("rejected", "native_host_vote_area_button_not_enabled");
            target.Select();
            selected = true;
            Advance(1, now);
        }
        else if (stage == 1)
        {
            if (meeting!.CurrentState != MeetingHud.MeetingStates.NotVoted || !target ||
                target!.Parent != meeting || !target.Buttons || !target.Buttons.activeInHierarchy)
                return Finish("rejected", "native_host_vote_selection_not_expanded");
            if (now - stageStarted < 0.5) return false;
            var confirm = target.ConfirmButton?.TryCast<PassiveButton>();
            if (!ButtonReady(confirm)) return Finish("rejected", "native_host_vote_confirm_not_enabled");
            confirm!.ReceiveClickUp();
            confirmed = true;
            Advance(2, now);
        }
        else if (stage == 2)
        {
            if (meeting!.CurrentState != MeetingHud.MeetingStates.Results) return false;
            hostVotes = otherVotes = skipVotes = 0;
            var skipped = meeting.SkippedVoting ? meeting.SkippedVoting.GetComponent<VoteSpreader>() : null;
            if (skipped && skipped!.Votes != null) skipVotes = skipped.Votes.Count;
            int areas = 0;
            foreach (var area in meeting.GetComponentsInChildren<PlayerVoteArea>(true))
            {
                if (area == meeting.SkipVoteButton || area.Parent != meeting || (byte)area.PlayerId >= 252) continue;
                areas++;
                var votes = area.GetComponent<VoteSpreader>();
                int count = votes && votes.Votes != null ? votes.Votes.Count : 0;
                if ((byte)area.PlayerId == hostPlayerId) hostVotes += count;
                else otherVotes += count;
            }
            if (hostVotes > 2 || otherVotes != 0 || skipVotes != 0)
                return Finish("failed", "native_results_not_exactly_two_host_votes");
            if (areas != 2 || hostVotes != 2) return false; // Native staggered vote icons.
            resultsSeen = true;
            Advance(3, now);
        }
        else
        {
            if (!isHost) { Advance(4, now); return false; }
            // The old implementation restored the name seven seconds after
            // publishing results. Wait naturally; never write native timers.
            if (now - stageStarted < 8) return false;
            delayElapsed = true;
            if (isHost)
            {
                if (meeting!.CurrentState != MeetingHud.MeetingStates.Results || !ButtonReady(meeting.ProceedButton))
                    return false;
                proceeded = true;
                meeting.ProceedButton.ReceiveClickUp();
            }
            Advance(4, now);
        }
        return false;
    }

    private bool ObserveCutscene(double now)
    {
        var exile = ExileController.Instance;
        if (stage == 4)
        {
            if (!exile && cutsceneSeen && stableMatched)
            { Advance(5, now); return ObserveCutscene(now); }
            if (!exile) return false;
            if (exilePointer == IntPtr.Zero) exilePointer = exile.Pointer;
            if (exile.Pointer != exilePointer) return Finish("failed", "native_exile_controller_replaced");
            cutsceneSeen = true;
            if (exile.initData == null || exile.initData.networkedPlayer == null ||
                exile.initData.networkedPlayer.PlayerId != hostPlayerId)
                return Finish("failed", "native_cutscene_did_not_exile_fixed_host");
            if (isHost && !expectedObserved)
            {
                // Public native data observes the host's actual pre-close rename,
                // not the inaccessible TempExileMsg or a rebuilt message policy.
                string candidate = hostPlayer!.Data.PlayerName;
                if (candidate.Length <= 4096 && candidate.EndsWith("<size=0>", StringComparison.Ordinal))
                {
                    expectedText = StripHiddenSuffix(candidate);
                    expectedObserved = expectedText.Contains("<color", StringComparison.Ordinal) && expectedText != originalHostName;
                }
            }
            if (!expectedObserved) return false;
            completeMatched = exile.completeString == expectedText;
            if (!completeMatched)
                return Finish("failed", "native_exile_complete_string_does_not_match_host_message");
            if (exile.Text && exile.Text.gameObject.activeInHierarchy)
            {
                tmpMatched = exile.Text.text == expectedText;
                if (!tmpMatched)
                {
                    // Native HandleText reveals completeString a substring at a
                    // time. Permit only actual expected prefixes during reveal.
                    if (!expectedText.StartsWith(exile.Text.text, StringComparison.Ordinal))
                        return Finish("failed", "native_visible_exile_tmp_is_not_expected_message_or_reveal_prefix");
                    revealPrefixObserved = true;
                    matchingFrames = 0; matchingStarted = 0; stableMatched = false;
                    return false;
                }
                if (matchingFrames == 0) matchingStarted = now;
                matchingFrames++;
                if (matchingFrames >= 5 && now - matchingStarted >= 0.75)
                {
                    stableMatched = true;
                    if (!captureRequested)
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(directory, "local-exile.png"));
                        captureRequested = true;
                    }
                }
            }
        }
        else
        {
            if (exile)
            {
                if (exile.Pointer != exilePointer || exile.completeString != expectedText ||
                    exile.Text && exile.Text.gameObject.activeInHierarchy && exile.Text.text != expectedText)
                    return Finish("failed", "native_exile_text_changed_after_stable_observation");
                return false;
            }
            if (MeetingHud.Instance || Minigame.Instance || HudManager.Instance.IsIntroDisplayed) return false;
            hostEjected = hostPlayer!.Data.IsDead && hostPlayer.Data.WasEjected;
            nameRestored = hostPlayer.Data.PlayerName == originalHostName;
            nativeExit = true;
            if (!hostEjected || !nameRestored)
                return Finish("failed", "native_host_ejection_or_real_name_restore_not_observed");
            if (!isHost && packetCount != 1)
                return Finish("failed", "expected_one_host_exile_text_sync_packet");
            return Finish("succeeded", "two_host_votes_delayed_native_proceed_stable_exile_text_and_name_restoration");
        }
        return false;
    }

    private void ObservePacket(PlayerControl sender, byte callId, MessageReader original)
    {
        MessageReader? reader = null;
        try
        {
            if (!Busy || isHost || !StillOwned() || callId != 123 || !sender ||
                sender.OwnerId != hostId || sender.Pointer != hostPlayer!.Pointer || original == null) return;
            reader = MessageReader.Get(original);
            if (reader.BytesRemaining < 8) return;
            int recipient = reader.ReadInt32();
            uint rpc = reader.ReadUInt32();
            if (rpc != 190 || recipient != -1 && recipient != clientId) return;
            if (reader.BytesRemaining < 11) { observerFailure = "host_exile_sync_packet_truncated"; return; }
            int game = reader.ReadInt32();
            uint meetingId = reader.ReadUInt32();
            byte player = reader.ReadByte();
            bool antiBlackout = reader.ReadBoolean();
            string text = reader.ReadString();
            if (reader.BytesRemaining != 0 || game != 32 || meetingId != meetingNetId || player != hostPlayerId ||
                antiBlackout || string.IsNullOrWhiteSpace(text) || text.Length > 4096 ||
                text.EndsWith("<size=0>", StringComparison.Ordinal))
            { observerFailure = "host_exile_sync_packet_context_or_message_invalid"; return; }
            packetCount++;
            if (expectedObserved && text != expectedText)
            { observerFailure = "host_exile_sync_message_changed"; return; }
            expectedText = text; expectedObserved = true;
        }
        catch (Exception ex) { observerFailure = "fixed_exile_packet_observer_" + ex.GetType().Name; }
        finally
        {
            try { reader?.Recycle(); }
            catch (Exception ex) { observerFailure = "fixed_exile_reader_cleanup_" + ex.GetType().Name; }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    private static class ExilePacketObserver
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] byte callId,
            [HarmonyArgument(1)] MessageReader reader) => active?.ObservePacket(__instance, callId, reader);
    }

    private static string StripHiddenSuffix(string text)
    {
        while (text.EndsWith("<size=0>", StringComparison.Ordinal)) text = text[..^8];
        return text;
    }
    private static bool Loopback() => AmongUsClient.Instance &&
        AmongUsClient.Instance.NetworkMode == NetworkModes.LocalGame && AmongUsClient.Instance.GameId == 32 &&
        AmongUsClient.Instance.GetNetworkAddress() == "127.0.0.1" && AmongUsClient.Instance.GetNetworkPort() == 22023;
    private static bool Ready()
    {
        var client = AmongUsClient.Instance;
        if (!Loopback() || !client.AmConnected || client.GameState != InnerNetClient.GameStates.Started ||
            SceneManager.GetActiveScene().name != "OnlineGame" || !GameData.Instance || GameData.Instance.PlayerCount != 2 ||
            !ShipStatus.Instance || !PlayerControl.LocalPlayer || !PlayerControl.LocalPlayer.AmOwner ||
            !DestroyableSingleton<HudManager>.InstanceExists || HudManager.Instance.IsIntroDisplayed || !TOHE.GameStates.IsNormalGame)
            return false;
        int count = 0;
        foreach (var data in GameData.Instance.AllPlayers)
        {
            if (data == null || data.Disconnected || data.IsDead || data.WasEjected || !data.Object || !data.Role) return false;
            count++;
        }
        return count == 2;
    }
    private bool StillOwned()
    {
        var client = AmongUsClient.Instance;
        if (!ownsSession() || !Loopback() || !client.AmConnected || client.Pointer != clientPointer ||
            client.ClientId != clientId || client.HostId != hostId || client.AmHost != isHost ||
            client.GameState != InnerNetClient.GameStates.Started || !GameData.Instance || GameData.Instance.PlayerCount != 2 ||
            !PlayerControl.LocalPlayer || PlayerControl.LocalPlayer.Pointer != playerPointer ||
            SceneManager.GetActiveScene().name != "OnlineGame" || !ShipStatus.Instance || !TOHE.GameStates.IsNormalGame)
            return false;
        int count = 0;
        foreach (var data in GameData.Instance.AllPlayers)
        {
            PlayerIdentity? baseline = null;
            foreach (var identity in players) if (identity.Id == data.PlayerId) baseline = identity;
            if (baseline == null || data.Disconnected || !data.Object || data.Pointer != baseline.Data ||
                data.Object.Pointer != baseline.Pointer || data.Object.OwnerId != baseline.Owner) return false;
            if (data.IsDead && !(resultsSeen && confirmed && data.PlayerId == hostPlayerId)) return false;
            // Native VotingComplete marks WasEjected before its staggered vote
            // icons finish. Permit only the native Results target; the two-icon
            // proof remains mandatory before proceeding or accepting death.
            bool nativeHostResult = confirmed && MeetingHud.Instance && MeetingHud.Instance.Pointer == meetingPointer &&
                MeetingHud.Instance.CurrentState == MeetingHud.MeetingStates.Results &&
                MeetingHud.Instance.exiledPlayer != null && MeetingHud.Instance.exiledPlayer.PlayerId == hostPlayerId;
            if (data.WasEjected && !(confirmed && data.PlayerId == hostPlayerId && (resultsSeen || nativeHostResult))) return false;
            count++;
        }
        return count == 2;
    }
    private static bool ButtonReady(PassiveButton? button) => button != null && button &&
        button.isActiveAndEnabled && button.gameObject.activeInHierarchy && !button.HoldToUse;
    private void Advance(int next, double now) { stage = next; stageStarted = now; }
    internal void Stop(string outcome, string detail) => Finish(outcome, detail);
    private bool Finish(string outcome, string detail)
    {
        Busy = false;
        active = null; // Any partially installed observer becomes inert first.
        if (hooksInstalled)
        {
            try { hooks!.UnpatchSelf(); hooksInstalled = false; }
            catch (Exception ex)
            {
                cleanupFailed = true;
                if (outcome == "succeeded") { outcome = "failed"; detail = "exile_observer_cleanup_" + ex.GetType().Name; }
            }
        }
        originalHostName = expectedText = "";
        Outcome = outcome; Detail = detail;
        return true;
    }
    internal object Snapshot() => new
    {
        state = Outcome, detail = Detail, stage = Busy ? (int?)stage : null,
        local_is_host = isHost, owned_loopback_session = ownsSession() && Loopback(),
        player_vote_selected = selected, confirm_clicked = confirmed, results_observed = resultsSeen,
        host_vote_count = hostVotes, other_player_vote_count = otherVotes, skip_vote_count = skipVotes,
        eight_second_result_delay_observed = delayElapsed, native_host_proceed_clicked = proceeded,
        native_exile_cutscene_observed = cutsceneSeen,
        expected_message_source = isHost ? "native_host_rename" : "received_host_sync_packet",
        expected_message_observed = expectedObserved, host_sync_packet_count = packetCount,
        direct_temp_exile_message_equality_tested = false,
        native_complete_string_matches_expected = completeMatched, visible_tmp_matches_expected = tmpMatched,
        stable_message_observed = stableMatched, matching_frame_count = matchingFrames,
        native_reveal_prefix_observed = revealPrefixObserved, auxiliary_capture_requested = captureRequested,
        intended_host_ejection_observed = hostEjected, original_host_name_restored = nameRestored,
        native_cutscene_exit_observed = nativeExit, observer_cleanup_failed = cleanupFailed,
        error_severity_delta = SmokeErrorCounter.Count - errorsBefore
    };
}
