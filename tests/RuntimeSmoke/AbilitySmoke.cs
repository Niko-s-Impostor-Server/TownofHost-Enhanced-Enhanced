using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AmongUs.GameOptions;
using InnerNet;
using TOHE;
using TOHE.Roles.Core;
using TOHE.Roles.Core.AssignManager;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameStates = TOHE.GameStates;

namespace TOHEE.RuntimeSmoke;

// Fixed local-only integration scenarios. No reflection, synthetic packet, disk
// option save, native cooldown reset, or direct call into the meeting protocol.
internal sealed class AbilitySmoke(Func<bool> ownsSession)
{
    private string command = "";
    private int stage;
    private double started, stageStarted;
    private IntPtr clientPointer, localPointer, meetingPointer;
    private int clientId, hostId;
    private bool prepared, restorePending, restoreFailed, startVerified, postMeetingRestored;
    private IntPtr preparedClient;
    private int preparedClientId, preparedHostId, originalPreset;
    private float lobbyKillCooldown;
    private readonly List<OptionBackup> optionBackups = new();
    private readonly Dictionary<byte, CustomRoles> originalSelections = new();
    private readonly List<byte> selectedPlayers = new();
    private readonly List<PlayerBaseline> players = new();
    private readonly List<VoteBaseline> votes = new();
    private RoleBase? localRole;
    private PlayerVoteArea? targetArea;
    private PhantomRole? phantom;
    private int globalTotal, globalCompleted, usesBefore, usesAfter;
    private int teleportCount, observationFrames, animationCount, remoteVersionCount;
    private float cooldownBefore, cooldownAfter;
    private float invisibilityAlphaBefore;
    private bool invoked, judgeExpanded, cooldownObserved, visibilityPreserved, tasksPreserved, votesPreserved;
    private bool meetingObserved;
    private string comparisonFailure = "";
    private int? localEmergenciesBefore, remoteEmergenciesBefore, localEmergenciesObserved, remoteEmergenciesObserved;
    private int observedGlobalTotal, observedGlobalCompleted;
    private bool nativeEmergencyConsumptionObserved, meetingEmergencyBaselineRebased;
    private int maxObservedTaskInfoReplacements;
    private bool postMeetingGameplayReady, postMeetingCustomRolesMatch, postMeetingBaselineChecked, postMeetingBaselinePreserved;
    private string postMeetingComparisonFailure = "";

    private sealed record OptionBackup(OptionItem Option, int Value);
    private sealed record TaskInfoBaseline(IntPtr Pointer, uint Id, bool Complete);
    private sealed record NativeTaskBaseline(NormalPlayerTask Task, int Step, bool Complete);
    private sealed record PlayerBaseline(PlayerControl Player, IntPtr Data, RoleBase Role, Vector2 Position,
        int Emergencies, int Total, int Completed, List<TaskInfoBaseline> Tasks, List<NativeTaskBaseline> NativeTasks);
    private sealed record VoteBaseline(PlayerVoteArea Area, bool DidVote, byte VotedFor);

    internal bool Busy { get; private set; }
    internal string Outcome { get; private set; } = "idle";
    internal string Detail { get; private set; } = "";
    internal static bool IsCommand(string value) => value is "local_prepare_abilities" or "local_phantom" or
        "local_ability_meeting" or "local_judge";

    internal bool Begin(string value, double now, out string rejection)
    {
        rejection = "";
        if (Busy || !IsCommand(value)) { rejection = "ability_busy_or_invalid_command"; return false; }
        if (!ownsSession() || !Loopback() || !TwoAlivePlayers())
        { rejection = "requires_helper_owned_two_alive_player_loopback_session"; return false; }
        var client = AmongUsClient.Instance;
        var local = PlayerControl.LocalPlayer;
        if (value == "local_prepare_abilities")
        {
            if (!client.AmHost || !LobbyReady() || prepared || restorePending || !Options.IsLoaded ||
                Main.AutoStart.Value || Main.AutoRehost.Value || Main.EnableGM.Value ||
                !Options.CustomRoleSpawnChances.ContainsKey(CustomRoles.Keeper) ||
                !Options.CustomRoleSpawnChances.ContainsKey(CustomRoles.Disperser) ||
                !Options.CustomRoleCounts.ContainsKey(CustomRoles.Keeper) ||
                !Options.CustomRoleCounts.ContainsKey(CustomRoles.Disperser) || Main.NormalOptions == null)
            { rejection = "requires_owned_normal_host_lobby_with_options_and_no_automatic_start_or_gm"; return false; }
            if (CompatibleRemoteCount() != 1)
            { rejection = "requires_known_matching_mod_version_on_the_remote_player"; return false; }
        }
        else
        {
            if (!StartedReady() || !FixedRolesMatch())
            { rejection = "requires_started_normal_keeper_disperser_two_player_game"; return false; }
            if (value == "local_phantom")
            {
                phantom = local.Data.Role.TryCast<PhantomRole>();
                if (client.AmHost || !GameplayReady() || Main.PlayerStates[local.PlayerId].MainRole != CustomRoles.Disperser ||
                    local.Data.Role.Role != RoleTypes.Phantom || phantom == null || !phantom ||
                    !PhantomAbility.CanRequest(local) || phantom.IsCoolingDown || phantom.fading || phantom.isInvisible ||
                    !local.Visible || local.shouldAppearInvisible || local.currentRoleAnimations.Count != 0)
                { rejection = "requires_owned_nonhost_visible_disperser_native_phantom_with_ready_cooldown"; return false; }
                foreach (var player in PlayerControl.AllPlayerControls)
                    if (!NativeTeleportReady(player))
                    { rejection = "both_alive_players_must_allow_native_teleport"; return false; }
            }
            else if (value == "local_ability_meeting")
            {
                if (!client.AmHost || !GameplayReady() || Main.PlayerStates[local.PlayerId].MainRole != CustomRoles.Keeper ||
                    local.Data.Role.Role != RoleTypes.Crewmate || !EmergencyReady())
                { rejection = "requires_alive_host_keeper_gameplay_and_native_emergency_conditions"; return false; }
            }
            else
            {
                var meeting = MeetingHud.Instance;
                if (!client.AmHost || Main.PlayerStates[local.PlayerId].MainRole != CustomRoles.Keeper || !meeting ||
                    !meeting.gameObject.activeInHierarchy || meeting.CurrentState is not
                        (MeetingHud.MeetingStates.Animating or MeetingHud.MeetingStates.Discussion or MeetingHud.MeetingStates.NotVoted))
                { rejection = "requires_owned_host_keeper_unvoted_meeting"; return false; }
                if (meeting.judgeOverrulesQueue == null || meeting.judgeOverrulesQueue.Count != 0 || meeting.wasOverruled)
                { rejection = "requires_empty_native_judge_queue_without_overrule"; return false; }
            }
        }
        command = value;
        stage = 0;
        started = stageStarted = now;
        clientPointer = client.Pointer;
        clientId = client.ClientId;
        hostId = client.HostId;
        localPointer = local.Pointer;
        meetingPointer = value == "local_judge" ? MeetingHud.Instance.Pointer : IntPtr.Zero;
        invoked = judgeExpanded = cooldownObserved = false;
        visibilityPreserved = tasksPreserved = votesPreserved = true;
        usesBefore = usesAfter = teleportCount = observationFrames = animationCount = 0;
        cooldownBefore = cooldownAfter = 0;
        targetArea = null;
        comparisonFailure = "";
        localEmergenciesBefore = remoteEmergenciesBefore = localEmergenciesObserved = remoteEmergenciesObserved = null;
        nativeEmergencyConsumptionObserved = meetingEmergencyBaselineRebased = false;
        maxObservedTaskInfoReplacements = 0;
        postMeetingBaselineChecked = postMeetingBaselinePreserved = false;
        postMeetingComparisonFailure = "";
        localRole = value == "local_prepare_abilities" ? null : local.GetRoleClass();
        players.Clear();
        votes.Clear();
        if (value != "local_prepare_abilities") CapturePlayers();
        if (value == "local_phantom")
        {
            cooldownBefore = phantom!.cooldownSecondsRemaining;
            invisibilityAlphaBefore = local.invisibilityAlpha;
        }
        if (value == "local_judge") CaptureVotes();
        Busy = true;
        Outcome = "running";
        Detail = "fixed_local_native_ability_scenario";
        return true;
    }

    internal bool Tick(double now)
    {
        if (!Busy) return true;
        try
        {
            if (!StillBound()) return Finish("failed", "owned_two_player_local_session_changed");
            if (now - started >= 30 || now - stageStarted >= 8)
                return Finish("timed_out", "ability_eight_second_stage_or_thirty_second_total_timeout_no_retry");
            if (command == "local_prepare_abilities") return Prepare(now);
            if (!StartedReady() || !FixedRolesMatch()) return Finish("failed", "fixed_game_roles_or_alive_context_changed");
            observationFrames++;
            tasksPreserved &= PlayersUnchanged(allowNativeEmergencyConsumption: command == "local_ability_meeting" && invoked);
            if (!tasksPreserved) return Finish("failed", "ability_baseline_" + comparisonFailure);
            return command switch
            {
                "local_phantom" => Phantom(now),
                "local_ability_meeting" => CallMeeting(now),
                _ => Judge(now)
            };
        }
        catch (Exception ex) { return Finish("failed", "native_ability_sequence_" + ex.GetType().Name); }
    }

    private bool Prepare(double now)
    {
        if (!LobbyReady()) return Finish("failed", "owned_host_lobby_lost_before_role_preparation");
        if (stage == 0)
        {
            originalPreset = OptionItem.CurrentPreset;
            preparedClient = clientPointer;
            preparedClientId = clientId;
            preparedHostId = hostId;
            lobbyKillCooldown = Main.NormalOptions.KillCooldown;
            remoteVersionCount = CompatibleRemoteCount();
            optionBackups.Clear();
            originalSelections.Clear();
            selectedPlayers.Clear();
            // Mark before mutating so every exception can restore partially changed state.
            restorePending = true;
            foreach (var player in PlayerControl.AllPlayerControls)
            {
                selectedPlayers.Add(player.PlayerId);
                if (RoleAssign.SetRoles.TryGetValue(player.PlayerId, out var old)) originalSelections[player.PlayerId] = old;
            }
            foreach (var role in new[] { CustomRoles.Keeper, CustomRoles.Disperser })
            {
                var spawn = Options.CustomRoleSpawnChances[role];
                var count = Options.CustomRoleCounts[role];
                optionBackups.Add(new(spawn, spawn.CurrentValue));
                optionBackups.Add(new(count, count.CurrentValue));
                spawn.SetValue(spawn.Selections.Length - 1, doSave: false, doSync: false);
                count.SetValue(0, doSave: false, doSync: false); // Standard rule's index zero is one player.
                if (spawn.GetChance() != 100 || count.GetInt() != 1)
                    return Finish("failed", "fixed_role_options_do_not_have_expected_native_values");
            }
            foreach (var player in PlayerControl.AllPlayerControls)
                RoleAssign.SetRoles[player.PlayerId] = player.AmOwner ? CustomRoles.Keeper : CustomRoles.Disperser;
            OptionItem.SyncAllOptions();
            prepared = true;
            startVerified = postMeetingRestored = meetingObserved = false;
            Advance(1, now);
        }
        else
        {
            if (CompatibleRemoteCount() != 1 || Main.NormalOptions.KillCooldown != lobbyKillCooldown)
                return Finish("failed", "peer_compatibility_or_lobby_kill_cooldown_changed");
            foreach (var player in PlayerControl.AllPlayerControls)
                if (!RoleAssign.SetRoles.TryGetValue(player.PlayerId, out var role) ||
                    role != (player.AmOwner ? CustomRoles.Keeper : CustomRoles.Disperser))
                    return Finish("failed", "fixed_preselected_roles_changed");
            return Finish("succeeded", "unsaved_keeper_host_disperser_remote_prepared_issue_existing_local_start");
        }
        return false;
    }

    private bool Phantom(double now)
    {
        var local = PlayerControl.LocalPlayer;
        if (AmongUsClient.Instance.AmHost || !GameplayReady() || local.Data.Role.Pointer != phantom!.Pointer)
            return Finish("failed", "nonhost_native_phantom_gameplay_lost");
        animationCount = Math.Max(animationCount, local.currentRoleAnimations.Count);
        visibilityPreserved &= local.Visible && !local.shouldAppearInvisible && !phantom.isInvisible && !phantom.fading &&
            local.invisibilityAlpha == invisibilityAlphaBefore && animationCount == 0;
        if (!visibilityPreserved) return Finish("failed", "native_phantom_visibility_fading_or_role_animation_changed");
        if (stage == 0)
        {
            if (!PhantomAbility.CanRequest(local) || phantom.IsCoolingDown)
                return Finish("rejected", "native_phantom_cooldown_or_use_conditions_changed");
            invoked = true;
            // The actual role input handler, exactly once. It owns the host request.
            phantom.UseAbility();
            Advance(1, now);
        }
        else
        {
            cooldownAfter = phantom.cooldownSecondsRemaining;
            cooldownObserved |= phantom.IsCoolingDown && cooldownAfter > cooldownBefore;
            teleportCount = 0;
            foreach (var before in players)
            {
                // RpcTeleport/SnapTo writes transform position; GetTruePosition
                // includes the collider offset and is not that wire destination.
                Vector2 position = before.Player.transform.position;
                if (Vector2.Distance(position, before.Position) > 0.5f && AtNativeVentDestination(position)) teleportCount++;
            }
            if (teleportCount == 2 && cooldownObserved && observationFrames >= 5 && now - stageStarted >= 0.75)
                return Finish("succeeded", "native_phantom_handler_two_vent_teleports_visible_no_fade_animation_cooldown_started_tasks_unchanged");
        }
        return false;
    }

    private bool CallMeeting(double now)
    {
        if (!AmongUsClient.Instance.AmHost) return Finish("failed", "keeper_host_changed");
        if (stage == 0)
        {
            if (!GameplayReady() || !EmergencyReady())
                return Finish("rejected", "native_emergency_cooldown_crisis_or_remaining_count_changed");
            invoked = true;
            // StartMeeting(null) consumes exactly one emergency for its AmOwner
            // caller. Preserve that native behavior; never write the count here.
            PlayerControl.LocalPlayer.CmdReportDeadBody(null);
            Advance(1, now);
        }
        else if (MeetingHud.Instance)
        {
            var meeting = MeetingHud.Instance;
            if (meetingPointer == IntPtr.Zero) meetingPointer = meeting.Pointer;
            if (meeting.Pointer != meetingPointer) return Finish("failed", "keeper_meeting_replaced");
            if (meeting.CurrentState is MeetingHud.MeetingStates.Discussion or MeetingHud.MeetingStates.NotVoted)
            {
                if (localEmergenciesBefore == null || PlayerControl.LocalPlayer.RemainingEmergencies != localEmergenciesBefore - 1)
                {
                    comparisonFailure = "native_emergency_consumption_not_exactly_one";
                    return Finish("failed", "ability_baseline_" + comparisonFailure);
                }
                nativeEmergencyConsumptionObserved = true;
                // Subsequent Judge/skip observers must preserve the post-meeting
                // count strictly. Only this expected caller count is rebased.
                for (int i = 0; i < players.Count; i++)
                    if (players[i].Player.Pointer == localPointer)
                        players[i] = players[i] with { Emergencies = PlayerControl.LocalPlayer.RemainingEmergencies };
                meetingEmergencyBaselineRebased = true;
                meetingObserved = true;
                return Finish("succeeded", "native_host_keeper_meeting_ready_tasks_unchanged_one_native_emergency_consumed_issue_local_judge");
            }
        }
        return false;
    }

    private bool Judge(double now)
    {
        var meeting = MeetingHud.Instance;
        var local = PlayerControl.LocalPlayer;
        if (!meeting || meeting.Pointer != meetingPointer || !AmongUsClient.Instance.AmHost)
            return Finish("failed", "native_keeper_meeting_lost");
        votesPreserved &= VotesUnchanged();
        if (!votesPreserved || meeting.judgeOverrulesQueue == null || meeting.judgeOverrulesQueue.Count != 0 || meeting.wasOverruled)
            return Finish("failed", "ordinary_votes_or_native_judge_queue_changed");
        if (stage == 0)
        {
            if (meeting.CurrentState is MeetingHud.MeetingStates.Animating or MeetingHud.MeetingStates.Discussion) return false;
            if (meeting.CurrentState != MeetingHud.MeetingStates.NotVoted || local.Data.Role.Role != RoleTypes.Judge) return false;
            usesBefore = RemainingKeeperUses(localRole!, local.PlayerId);
            if (usesBefore <= 0) return Finish("rejected", "keeper_progress_has_no_remaining_uses");
            foreach (var area in meeting.playerStates)
                if (area && area.Parent == meeting && (byte)area.PlayerId < 252 && (byte)area.PlayerId != local.PlayerId)
                {
                    if (targetArea != null) return Finish("failed", "native_meeting_target_is_not_unique");
                    targetArea = area;
                }
            if (targetArea == null || !targetArea) return Finish("failed", "native_remote_vote_area_missing");
            targetArea.Select();
            Advance(1, now);
        }
        else if (stage == 1)
        {
            if (!targetArea || targetArea!.Parent != meeting || !targetArea.Buttons || !targetArea.Buttons.activeInHierarchy)
                return Finish("failed", "native_player_vote_area_selection_not_expanded");
            if (now - stageStarted < 0.5) return false;
            var button = targetArea.JudgeOverruleButton?.TryCast<PassiveButton>();
            if (button == null || !button || !button.isActiveAndEnabled || !button.gameObject.activeInHierarchy || button.HoldToUse)
                return Finish("rejected", "native_judge_button_not_enabled");
            judgeExpanded = true;
            invoked = true;
            // Exact native Judge button binding, intercepted by the installed product patch.
            targetArea.JudgeOverruleVote();
            Advance(2, now);
        }
        else
        {
            usesAfter = RemainingKeeperUses(localRole!, local.PlayerId);
            if (usesAfter < usesBefore - 1) return Finish("failed", "more_than_one_keeper_use_consumed");
            if (usesAfter == usesBefore - 1 && observationFrames >= 5 && now - stageStarted >= 0.5)
                return Finish("succeeded", "native_judge_button_keeper_use_minus_one_no_votes_tasks_emergencies_or_native_overrule_issue_local_skip_on_both");
        }
        return false;
    }

    // Called even between commands: late ownership loss still restores unsaved options.
    internal void Observe()
    {
        if (restorePending && !restoreFailed && !PreparedSessionBound())
        {
            try { RestorePrepared(); }
            catch { restoreFailed = true; throw; }
        }
        if (ownsSession() && StartedReady() && FixedRolesMatch() && MeetingHud.Instance) meetingObserved = true;
        if (meetingObserved && GameplayReady() && FixedRolesMatch())
            postMeetingRestored = NativeBasesMatch();
    }

    internal bool BeforeStart(out string rejection)
    {
        rejection = "";
        if (!prepared) return true;
        if (!PreparedSessionBound() || !LobbyReady() || OptionItem.CurrentPreset != originalPreset ||
            Main.NormalOptions.KillCooldown != lobbyKillCooldown || CompatibleRemoteCount() != 1)
        { rejection = "prepared_ability_lobby_preset_cooldown_or_peer_changed"; return false; }
        foreach (var player in PlayerControl.AllPlayerControls)
            if (!RoleAssign.SetRoles.TryGetValue(player.PlayerId, out var role) ||
                role != (player.AmOwner ? CustomRoles.Keeper : CustomRoles.Disperser))
            { rejection = "prepared_ability_role_selections_changed"; return false; }
        foreach (var role in new[] { CustomRoles.Keeper, CustomRoles.Disperser })
            if (Options.CustomRoleSpawnChances[role].GetChance() != 100 || Options.CustomRoleCounts[role].GetInt() != 1)
            { rejection = "prepared_ability_spawn_or_count_options_changed"; return false; }
        return true;
    }

    internal bool VerifyStart(out string failure)
    {
        failure = "";
        if (!prepared) return true;
        startVerified = PreparedSessionBound() && StartedReady() && FixedRolesMatch() && NativeBasesMatch() &&
            Main.RealOptionsData != null && Main.RealOptionsData.GetFloat(FloatOptionNames.KillCooldown) == lobbyKillCooldown &&
            CompatibleRemoteCount() == 1;
        if (!startVerified) failure = "prepared_ability_roles_native_bases_peer_version_or_original_kill_cooldown_backup_not_observed";
        return startVerified;
    }

    internal bool VerifyAfterSkip(out string failure)
    {
        failure = "";
        if (!meetingObserved) return true;
        postMeetingGameplayReady = GameplayReady();
        postMeetingCustomRolesMatch = postMeetingGameplayReady && FixedRolesMatch();
        postMeetingRestored = postMeetingCustomRolesMatch && NativeBasesMatch();
        postMeetingBaselineChecked = false;
        postMeetingBaselinePreserved = false;
        postMeetingComparisonFailure = "";
        if (!postMeetingGameplayReady) failure = "fixed_ability_gameplay_not_ready_after_meeting";
        else if (!postMeetingCustomRolesMatch) failure = "fixed_ability_custom_roles_changed_after_meeting";
        else if (!postMeetingRestored) failure = "fixed_ability_native_role_bases_not_restored_after_meeting";
        else
        {
            postMeetingBaselineChecked = players.Count != 0;
            postMeetingBaselinePreserved = !postMeetingBaselineChecked || PlayersUnchanged();
            if (!postMeetingBaselinePreserved)
            {
                postMeetingComparisonFailure = comparisonFailure;
                failure = "fixed_ability_post_meeting_baseline_" + comparisonFailure;
            }
        }
        return failure.Length == 0;
    }

    internal void RestorePrepared()
    {
        if (!restorePending) return;
        restoreFailed = false;
        int preset = OptionItem.CurrentPreset;
        try
        {
            if (preset != originalPreset) OptionItem.SwitchPreset(originalPreset, doSync: false);
            foreach (var backup in optionBackups) backup.Option.SetValue(backup.Value, doSave: false, doSync: false);
            // Role selections belong to this room. Do not overwrite reused IDs if a
            // different connected session replaced it before the observer ran.
            bool restoreSelections = PreparedSessionBound() || !AmongUsClient.Instance || !AmongUsClient.Instance.AmConnected;
            foreach (byte id in restoreSelections ? selectedPlayers : new List<byte>())
            {
                RoleAssign.SetRoles.Remove(id);
                if (originalSelections.TryGetValue(id, out var role)) RoleAssign.SetRoles[id] = role;
            }
            restorePending = prepared = false;
        }
        finally { if (OptionItem.CurrentPreset != preset) OptionItem.SwitchPreset(preset, doSync: false); }
        if (ownsSession() && Loopback() && AmongUsClient.Instance.AmHost) OptionItem.SyncAllOptions();
    }

    private void CapturePlayers()
    {
        globalTotal = GameData.Instance.TotalTasks;
        globalCompleted = GameData.Instance.CompletedTasks;
        observedGlobalTotal = globalTotal;
        observedGlobalCompleted = globalCompleted;
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            var infos = new List<TaskInfoBaseline>();
            foreach (var task in player.Data.Tasks) infos.Add(new(task.Pointer, task.Id, task.Complete));
            var native = new List<NativeTaskBaseline>();
            foreach (var task in player.myTasks)
            {
                var normal = task ? task.TryCast<NormalPlayerTask>() : null;
                if (normal != null && normal) native.Add(new(normal, normal.TaskStep, normal.IsComplete));
            }
            var state = Main.PlayerStates[player.PlayerId].TaskState;
            players.Add(new(player, player.Data.Pointer, player.GetRoleClass(), player.transform.position,
                player.RemainingEmergencies, state.AllTasksCount, state.CompletedTasksCount, infos, native));
            if (player.Pointer == localPointer)
                localEmergenciesBefore = localEmergenciesObserved = player.RemainingEmergencies;
            else
                remoteEmergenciesBefore = remoteEmergenciesObserved = player.RemainingEmergencies;
        }
    }

    private bool PlayersUnchanged(bool allowNativeEmergencyConsumption = false)
    {
        comparisonFailure = "";
        int replacedTaskInfos = 0;
        observedGlobalTotal = GameData.Instance.TotalTasks;
        observedGlobalCompleted = GameData.Instance.CompletedTasks;
        if (observedGlobalTotal != globalTotal || observedGlobalCompleted != globalCompleted)
            return BaselineMismatch("shared_task_totals_or_progress_changed");
        foreach (var before in players)
        {
            var player = before.Player;
            if (!player || player.Data == null || player.Data.Pointer != before.Data)
                return BaselineMismatch("player_or_native_data_replaced");
            bool caller = player.Pointer == localPointer;
            if (caller) localEmergenciesObserved = player.RemainingEmergencies;
            else remoteEmergenciesObserved = player.RemainingEmergencies;
            bool expectedEmergencyCount = player.RemainingEmergencies == before.Emergencies ||
                allowNativeEmergencyConsumption && caller && player.RemainingEmergencies == before.Emergencies - 1;
            if (!expectedEmergencyCount) return BaselineMismatch(caller ? "local_emergency_count_unexpected" : "remote_emergency_count_changed");
            if (!ReferenceEquals(player.GetRoleClass(), before.Role)) return BaselineMismatch("custom_role_instance_changed");
            if (!Main.PlayerStates.TryGetValue(player.PlayerId, out var state)) return BaselineMismatch("custom_player_state_lost");
            if (state.TaskState.AllTasksCount != before.Total || state.TaskState.CompletedTasksCount != before.Completed)
                return BaselineMismatch("mod_task_totals_or_progress_changed");
            if (player.Data.Tasks == null || player.Data.Tasks.Count != before.Tasks.Count)
                return BaselineMismatch("native_task_info_count_changed");
            for (int i = 0; i < before.Tasks.Count; i++)
            {
                var task = player.Data.Tasks[i];
                var old = before.Tasks[i];
                if (task == null || task.Id != old.Id)
                    return BaselineMismatch("native_task_info_id_or_order_changed");
                if (task.Complete != old.Complete) return BaselineMismatch("native_task_completion_changed");
                if (task.Pointer != old.Pointer)
                {
                    // Native NetworkedPlayerInfo.Deserialize clears Tasks and
                    // allocates TaskInfo(Id, Complete) again for each snapshot.
                    // This value-preserving replacement is normal on clients;
                    // it does not replace PlayerControl.myTasks/NormalPlayerTask.
                    if (AmongUsClient.Instance.AmHost) return BaselineMismatch("host_native_task_info_replaced");
                    replacedTaskInfos++;
                }
            }
            foreach (var old in before.NativeTasks)
            {
                if (!old.Task) return BaselineMismatch("native_task_object_lost");
                if (old.Task.TaskStep != old.Step || old.Task.IsComplete != old.Complete)
                    return BaselineMismatch("native_task_step_or_completion_changed");
                bool present = false;
                foreach (var task in player.myTasks) if (task && task.Pointer == old.Task.Pointer) present = true;
                if (!present) return BaselineMismatch("native_task_object_no_longer_installed");
            }
        }
        maxObservedTaskInfoReplacements = Math.Max(maxObservedTaskInfoReplacements, replacedTaskInfos);
        return true;
    }

    private bool BaselineMismatch(string label) { comparisonFailure = label; return false; }

    private void CaptureVotes()
    {
        foreach (var area in MeetingHud.Instance.playerStates)
            if (area && area.Parent == MeetingHud.Instance && (byte)area.PlayerId < 252)
                votes.Add(new(area, area.DidVote, (byte)area.VotedForId));
        if (votes.Count != 2) throw new InvalidOperationException("two_native_vote_states_required");
    }

    private bool VotesUnchanged()
    {
        foreach (var before in votes)
            if (!before.Area || before.Area.Parent != MeetingHud.Instance || before.Area.DidVote != before.DidVote ||
                (byte)before.Area.VotedForId != before.VotedFor) return false;
        return votes.Count == 2;
    }

    private static int RemainingKeeperUses(RoleBase role, byte playerId)
    {
        string text = Regex.Replace(role.GetProgressText(playerId, false), "<[^>]*>", "");
        var match = Regex.Match(text, @"-\s*(\d+)\s*$");
        return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : -1;
    }

    private static bool AtNativeVentDestination(Vector2 position)
    {
        foreach (var vent in ShipStatus.Instance.AllVents)
            if (vent && Vector2.Distance(position, (Vector2)vent.transform.position + new Vector2(0, 0.3636f)) < 0.3f) return true;
        return false;
    }

    private static bool NativeTeleportReady(PlayerControl player) => player && player.Data != null &&
        !player.Data.Disconnected && !player.Data.IsDead && !Main.MeetingIsStarted && !player.inVent &&
        !player.walkingToVent && !player.inMovingPlat && !player.onLadder && player.MyPhysics &&
        !player.MyPhysics.Animations.IsPlayingEnterVentAnimation() && !player.MyPhysics.Animations.IsPlayingAnyLadderAnimation();

    private static bool Loopback() => AmongUsClient.Instance && AmongUsClient.Instance.AmConnected &&
        AmongUsClient.Instance.NetworkMode == NetworkModes.LocalGame && AmongUsClient.Instance.GameId == 32 &&
        AmongUsClient.Instance.GetNetworkAddress() == "127.0.0.1" && AmongUsClient.Instance.GetNetworkPort() == 22023;

    private static bool TwoAlivePlayers()
    {
        if (!GameData.Instance || GameData.Instance.PlayerCount != 2 || !PlayerControl.LocalPlayer ||
            !PlayerControl.LocalPlayer.AmOwner || PlayerControl.LocalPlayer.Data == null) return false;
        int count = 0;
        foreach (var player in GameData.Instance.AllPlayers)
        {
            if (player == null || player.Disconnected || player.IsDead || player.WasEjected || !player.Object) return false;
            count++;
        }
        return count == 2 && PlayerControl.AllPlayerControls.Count == 2;
    }

    private static bool LobbyReady() => Loopback() && TwoAlivePlayers() && AmongUsClient.Instance.AmHost &&
        AmongUsClient.Instance.GameState == InnerNetClient.GameStates.Joined && GameStates.IsNormalGame &&
        SceneManager.GetActiveScene().name == "OnlineGame" && !ShipStatus.Instance && !GameSettingMenu.Instance &&
        DestroyableSingleton<GameStartManager>.InstanceExists;

    private static bool StartedReady() => Loopback() && TwoAlivePlayers() && GameStates.IsNormalGame &&
        AmongUsClient.Instance.GameState == InnerNetClient.GameStates.Started && ShipStatus.Instance &&
        SceneManager.GetActiveScene().name == "OnlineGame" && Main.IntroDestroyed &&
        DestroyableSingleton<HudManager>.InstanceExists && !HudManager.Instance.IsIntroDisplayed;

    private static bool GameplayReady() => StartedReady() && GameStates.IsInTask &&
        !MeetingHud.Instance && !ExileController.Instance && !Minigame.Instance && PlayerControl.LocalPlayer.CanMove;

    private static bool EmergencyReady()
    {
        if (ShipStatus.Instance.Timer < 15 || ShipStatus.Instance.EmergencyCooldown > 0 ||
            PlayerControl.LocalPlayer.RemainingEmergencies <= 0) return false;
        foreach (var task in PlayerControl.LocalPlayer.myTasks) if (PlayerTask.TaskIsEmergency(task)) return false;
        return true;
    }

    private static bool FixedRolesMatch()
    {
        int count = 0;
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (!player || player.Data == null || !player.Data.Role || !Main.PlayerStates.TryGetValue(player.PlayerId, out var state) ||
                state.MainRole != (player.OwnerId == AmongUsClient.Instance.HostId ? CustomRoles.Keeper : CustomRoles.Disperser) ||
                player.GetRoleClass() == null) return false;
            count++;
        }
        return count == 2;
    }

    private static bool NativeBasesMatch()
    {
        foreach (var player in PlayerControl.AllPlayerControls)
            if (player.Data.Role.Role != (player.OwnerId == AmongUsClient.Instance.HostId ? RoleTypes.Crewmate : RoleTypes.Phantom)) return false;
        return PlayerControl.AllPlayerControls.Count == 2;
    }

    private static int CompatibleRemoteCount()
    {
        int count = 0;
        foreach (var player in PlayerControl.AllPlayerControls)
            if (player && !player.AmOwner && Main.playerVersion.TryGetValue(player.OwnerId, out var version) &&
                version.version == Main.version && version.forkId == Main.ForkId) count++;
        return count;
    }

    private bool StillBound() => ownsSession() && Loopback() && TwoAlivePlayers() &&
        AmongUsClient.Instance.Pointer == clientPointer && AmongUsClient.Instance.ClientId == clientId &&
        AmongUsClient.Instance.HostId == hostId && PlayerControl.LocalPlayer.Pointer == localPointer;

    private bool PreparedSessionBound() => ownsSession() && Loopback() && AmongUsClient.Instance.AmHost &&
        AmongUsClient.Instance.Pointer == preparedClient && AmongUsClient.Instance.ClientId == preparedClientId &&
        AmongUsClient.Instance.HostId == preparedHostId;

    private void Advance(int next, double now) { stage = next; stageStarted = now; }
    internal void Stop(string outcome, string detail) => Finish(outcome, detail);
    private bool Finish(string outcome, string detail)
    {
        if (command == "local_prepare_abilities" && outcome != "succeeded")
        {
            try { RestorePrepared(); }
            catch (Exception ex) { outcome = "failed"; detail = "ability_option_restore_" + ex.GetType().Name; }
        }
        Busy = false;
        Outcome = outcome;
        Detail = detail;
        return true;
    }

    internal object Snapshot() => new
    {
        state = Outcome, detail = Detail, command, stage = Busy ? (int?)stage : null,
        local_owned = ownsSession(), loopback_local = Loopback(), prepared, option_restore_pending = restorePending,
        option_restore_failed_requires_explicit_leave_retry = restoreFailed,
        fixed_role_start_verified = startVerified, lobby_original_kill_cooldown = lobbyKillCooldown,
        current_backup_kill_cooldown = Main.RealOptionsData?.GetFloat(FloatOptionNames.KillCooldown),
        compatible_remote_count_at_prepare = remoteVersionCount,
        native_handler_invoked_once = invoked, observation_frame_count = observationFrames,
        teleported_player_count = teleportCount, visibility_preserved = visibilityPreserved,
        observed_role_animation_max_count = animationCount, native_cooldown_before = cooldownBefore,
        native_cooldown_after = cooldownAfter, native_cooldown_started = cooldownObserved,
        tasks_and_expected_emergency_state_preserved = tasksPreserved, judge_selection_expanded = judgeExpanded,
        baseline_comparison_failure = comparisonFailure,
        local_emergencies_before = localEmergenciesBefore, local_emergencies_observed = localEmergenciesObserved,
        remote_emergencies_before = remoteEmergenciesBefore, remote_emergencies_observed = remoteEmergenciesObserved,
        native_emergency_consumption_observed = nativeEmergencyConsumptionObserved,
        meeting_emergency_baseline_rebased = meetingEmergencyBaselineRebased,
        shared_tasks_before = globalTotal, shared_tasks_observed = observedGlobalTotal,
        shared_completed_before = globalCompleted, shared_completed_observed = observedGlobalCompleted,
        native_task_info_identity_required = AmongUsClient.Instance && AmongUsClient.Instance.AmHost,
        native_task_info_value_preserving_replacement_max_count = maxObservedTaskInfoReplacements,
        native_task_objects_steps_and_completion_identity_required = true,
        keeper_uses_before = usesBefore, keeper_uses_after = usesAfter, ordinary_votes_preserved = votesPreserved,
        native_judge_queue_count = MeetingHud.Instance?.judgeOverrulesQueue?.Count,
        native_meeting_overruled = MeetingHud.Instance?.wasOverruled == true,
        post_meeting_gameplay_ready = postMeetingGameplayReady,
        post_meeting_custom_roles_match = postMeetingCustomRolesMatch,
        post_meeting_native_bases_restore_observed = postMeetingRestored,
        post_meeting_baseline_checked = postMeetingBaselineChecked,
        post_meeting_baseline_preserved = postMeetingBaselinePreserved,
        post_meeting_baseline_failure = postMeetingComparisonFailure
    };
}
