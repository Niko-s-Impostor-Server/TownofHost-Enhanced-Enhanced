using System;
using AmongUs.GameOptions;
using InnerNet;
using UnityEngine.SceneManagement;

namespace TOHEE.RuntimeSmoke;

// Completion-callback integration coverage only; no minigame UI is exercised.
// The caller must prove ownership of the already joined fixed loopback session.
internal sealed class TaskCompletionSmoke(Func<bool> ownsLocalSession)
{
    private double started;
    private IntPtr clientPointer, playerPointer, dataPointer, gamePointer, shipPointer, taskInfoPointer;
    private int clientId, hostId;
    private NormalPlayerTask? task;
    private TOHE.TaskState? taskState;
    private uint taskId;
    private int initialPersonalTotal, initialPersonalCompleted, initialGlobalTotal, initialGlobalCompleted;
    private bool callbackInvoked;

    internal bool Busy { get; private set; }
    internal string Outcome { get; private set; } = "idle";
    internal string Detail { get; private set; } = "";
    internal static bool IsCommand(string value) => value == "local_task";

    internal bool Begin(double now, out string rejection)
    {
        rejection = "";
        if (Busy) { rejection = "task_completion_busy"; return false; }
        if (!ownsLocalSession() || !ContextReady())
        { rejection = "requires_owned_two_alive_player_nonhost_loopback_crewmate_gameplay"; return false; }

        var local = PlayerControl.LocalPlayer;
        var state = TOHE.Main.PlayerStates[local.PlayerId].TaskState;
        NormalPlayerTask? selected = null;
        foreach (var candidate in local.myTasks)
        {
            var normal = candidate ? candidate.TryCast<NormalPlayerTask>() : null;
            if (normal == null || !normal || !normal.Owner || normal.Owner.Pointer != local.Pointer ||
                normal.MaxStep != 1 || normal.TaskStep != 0 || normal.IsComplete) continue;
            var info = FindTaskInfo(local.Data, normal.Id);
            if (info == null || info.Complete) continue;
            selected = normal;
            break;
        }
        if (selected == null)
        { rejection = "no_owned_uncompleted_single_step_native_task_with_installed_info"; return false; }

        var client = AmongUsClient.Instance;
        clientPointer = client.Pointer;
        clientId = client.ClientId;
        hostId = client.HostId;
        playerPointer = local.Pointer;
        dataPointer = local.Data.Pointer;
        gamePointer = GameData.Instance.Pointer;
        shipPointer = ShipStatus.Instance.Pointer;
        task = selected;
        taskId = selected.Id;
        taskInfoPointer = FindTaskInfo(local.Data, taskId)!.Pointer;
        taskState = state;
        initialPersonalTotal = state.AllTasksCount;
        initialPersonalCompleted = state.CompletedTasksCount;
        initialGlobalTotal = GameData.Instance.TotalTasks;
        initialGlobalCompleted = GameData.Instance.CompletedTasks;
        started = now;
        callbackInvoked = false;
        Busy = true;
        Outcome = "running";
        Detail = "fixed_native_task_completion_callback_and_sync_observer";
        return true;
    }

    internal bool Tick(double now)
    {
        if (!Busy) return true;
        try
        {
            if (!StillOwned()) return Finish("failed", "owned_session_or_ordinary_crewmate_gameplay_changed");
            if (now - started >= 8) return Finish("timed_out", "native_task_completion_sync_timeout_no_retry");
            var local = PlayerControl.LocalPlayer;
            var info = FindTaskInfo(local.Data, taskId);
            if (info == null || !callbackInvoked && info.Pointer != taskInfoPointer || task == null || !task || !task.Owner ||
                task.Owner.Pointer != playerPointer || !InstalledTaskStillPresent())
                return Finish("failed", "selected_native_task_or_installed_info_replaced");
            if (taskState!.AllTasksCount != initialPersonalTotal || GameData.Instance.TotalTasks != initialGlobalTotal)
                return Finish("failed", "task_total_changed_during_completion");

            if (!callbackInvoked)
            {
                if (info.Complete || task.IsComplete || task.MaxStep != 1 || task.TaskStep != 0 ||
                    taskState.CompletedTasksCount != initialPersonalCompleted ||
                    GameData.Instance.CompletedTasks != initialGlobalCompleted)
                    return Finish("rejected", "task_or_baseline_changed_before_native_callback");
                // Exactly the normal callback used after a single-step minigame.
                // NextStep owns native CompleteTask/RPC handling. Never retry it.
                callbackInvoked = true;
                task.NextStep();
                return false;
            }

            if (taskState.CompletedTasksCount > initialPersonalCompleted + 1 ||
                GameData.Instance.CompletedTasks > initialGlobalCompleted + 1)
                return Finish("failed", "more_than_one_completion_observed");
            if (info.Complete && task.IsComplete && task.TaskStep == 1 &&
                taskState.CompletedTasksCount == initialPersonalCompleted + 1 &&
                GameData.Instance.CompletedTasks == initialGlobalCompleted + 1)
                return Finish("succeeded", "native_completion_callback_and_local_sync_observed_compare_host_snapshot");
            return false;
        }
        catch (Exception ex) { return Finish("failed", "native_task_completion_" + ex.GetType().Name); }
    }

    private static bool ContextReady()
    {
        var client = AmongUsClient.Instance;
        var local = PlayerControl.LocalPlayer;
        if (!client || client.NetworkMode != NetworkModes.LocalGame || client.GameId != 32 ||
            client.GetNetworkAddress() != "127.0.0.1" || client.GetNetworkPort() != 22023 ||
            !client.AmConnected || client.AmHost || client.GameState != InnerNetClient.GameStates.Started ||
            SceneManager.GetActiveScene().name != "OnlineGame" || !GameData.Instance ||
            GameData.Instance.PlayerCount != 2 || !ShipStatus.Instance || !local || !local.AmOwner ||
            local.Data == null || local.Data.Tasks == null || !local.Data.Role ||
            local.Data.Role.Role != RoleTypes.Crewmate || !local.CanMove ||
            MeetingHud.Instance || ExileController.Instance || Minigame.Instance ||
            !DestroyableSingleton<HudManager>.InstanceExists || HudManager.Instance.IsIntroDisplayed ||
            !TOHE.Main.IntroDestroyed || TOHE.Main.RealOptionsData == null ||
            !TOHE.GameStates.IsModHost || !TOHE.GameStates.IsNormalGame || TOHE.GameStates.IsHideNSeek ||
            !TOHE.Main.PlayerStates.TryGetValue(local.PlayerId, out var state) ||
            state.MainRole != TOHE.CustomRoles.CrewmateTOHE || state.SubRoles.Count != 0 ||
            !state.TaskState.hasTasks || state.TaskState.AllTasksCount <= 0 ||
            state.TaskState.AllTasksCount != local.Data.Tasks.Count ||
            state.TaskState.CompletedTasksCount < 0 ||
            state.TaskState.CompletedTasksCount > state.TaskState.AllTasksCount ||
            GameData.Instance.TotalTasks <= 0 || GameData.Instance.CompletedTasks < 0 ||
            GameData.Instance.CompletedTasks > GameData.Instance.TotalTasks) return false;
        int alive = 0;
        foreach (var player in GameData.Instance.AllPlayers)
        {
            if (player == null || player.Disconnected || player.IsDead || player.WasEjected ||
                !player.Object || !player.Role) return false;
            alive++;
        }
        return alive == 2;
    }

    private bool StillOwned() => ownsLocalSession() && ContextReady() &&
        AmongUsClient.Instance.Pointer == clientPointer && AmongUsClient.Instance.ClientId == clientId &&
        AmongUsClient.Instance.HostId == hostId && PlayerControl.LocalPlayer.Pointer == playerPointer &&
        PlayerControl.LocalPlayer.Data.Pointer == dataPointer && GameData.Instance.Pointer == gamePointer &&
        ShipStatus.Instance.Pointer == shipPointer &&
        ReferenceEquals(TOHE.Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId].TaskState, taskState);

    private bool InstalledTaskStillPresent()
    {
        foreach (var candidate in PlayerControl.LocalPlayer.myTasks)
            if (candidate && task != null && candidate.Pointer == task.Pointer && candidate.Id == taskId) return true;
        return false;
    }

    private static NetworkedPlayerInfo.TaskInfo? FindTaskInfo(NetworkedPlayerInfo data, uint id)
    {
        NetworkedPlayerInfo.TaskInfo? result = null;
        foreach (var info in data.Tasks)
        {
            if (info == null || info.Id != id) continue;
            if (result != null) return null; // Ambiguous metadata cannot select a task.
            result = info;
        }
        return result;
    }

    internal void Stop(string outcome, string detail) => Finish(outcome, detail);
    private bool Finish(string outcome, string detail)
    {
        Busy = false;
        Outcome = outcome;
        Detail = detail;
        return true; // A completed native task is intentionally never undone.
    }

    internal object Snapshot()
    {
        var local = PlayerControl.LocalPlayer;
        var state = local && TOHE.Main.PlayerStates.TryGetValue(local.PlayerId, out var owner) ? owner.TaskState : null;
        var info = local && local.Data != null && local.Data.Tasks != null ? FindTaskInfo(local.Data, taskId) : null;
        return new
        {
            state = Outcome, detail = Detail, busy = Busy, callback_invoked = callbackInvoked,
            native_task_complete = callbackInvoked && task != null && task && task.IsComplete,
            installed_task_complete = callbackInvoked && info != null && info.Complete,
            initial_local_total = initialPersonalTotal, initial_local_completed = initialPersonalCompleted,
            initial_global_total = initialGlobalTotal, initial_global_completed = initialGlobalCompleted,
            local_task_total = state?.AllTasksCount, local_task_completed = state?.CompletedTasksCount,
            global_task_total = GameData.Instance ? (int?)GameData.Instance.TotalTasks : null,
            global_task_completed = GameData.Instance ? (int?)GameData.Instance.CompletedTasks : null,
            player_count = GameData.Instance ? GameData.Instance.PlayerCount : 0,
            gameplay_ready = ContextReady()
        };
    }
}
