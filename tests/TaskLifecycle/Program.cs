using System;
using TOHE;

static class Program
{
    static int assertions;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        assertions++;
    }
    static GameData Reset()
    {
        TOHE.Main.PlayerStates.Clear();
        AmongUsClient.Instance = new AmongUsClient { AmHost = true };
        GameStates.IsModHost = GameStates.IsNormalGame = true;
        GameStates.IsFreePlay = GameStates.IsHideNSeek = false;
        TaskState.InitialTotalTasks = 0;
        return GameData.Instance = new GameData();
    }
    static (NetworkedPlayerInfo Data, PlayerState State) Player(GameData game, byte id, int tasks)
    {
        var data = new NetworkedPlayerInfo { PlayerId = id, Pointer = new IntPtr(id + 1) };
        data.Object = new PlayerControl { Data = data };
        for (int i = 0; i < tasks; i++) data.Tasks.Add(new NetworkedPlayerInfo.TaskInfo());
        var state = new PlayerState();
        TOHE.Main.PlayerStates[id] = state;
        game.AllPlayers.Add(data);
        return (data, state);
    }
    static void EarlyEmptyObservation()
    {
        var game = Reset();
        var (data, state) = Player(game, 0, 0);
        state.InitTask(data.Object); // Real extracted Init models the early ShowRole observation.
        Check(state.TaskState.AllTasksCount == 0, "early Init observes an empty installed list");
        data.Tasks.Add(new NetworkedPlayerInfo.TaskInfo());
        data.Tasks.Add(new NetworkedPlayerInfo.TaskInfo());
        game.RecomputeTaskCounts();
        Check(game.TotalTasks == 2 && state.TaskState.AllTasksCount == 0,
            "task bar counts real list even before repairing stale zero cache");
        InstalledTaskStatePatch.Postfix(data);
        Check(state.InitCalls == 2 && state.TaskState.AllTasksCount == 2 && state.TaskState.hasTasks,
            "actual SetTasks postfix repairs early zero using production Init");
        Check(state.TaskState.CompletedTasksCount == 0 && game.CompletedTasks == 0,
            "first installed assignment has zero completed tasks");
        InitialInstalledTaskCountsPatch.Postfix();
        Check(TaskState.InitialTotalTasks == 2, "ShipStatus.Begin postfix records complete batch total");
    }
    static void PendingCompletionValidation()
    {
        var game = Reset();
        var (data, _) = Player(game, 0, 1);
        data.Tasks[0].Id = 7;
        Check(InstalledTaskStatePatch.HasPendingTask(data.Object, 7), "an installed pending task can complete");
        Check(!InstalledTaskStatePatch.HasPendingTask(data.Object, 8), "an unknown task cannot grant role progress");
        data.Tasks[0].Complete = true;
        Check(!InstalledTaskStatePatch.HasPendingTask(data.Object, 7), "duplicate completed task cannot grant role progress");
        Check(!InstalledTaskStatePatch.HasPendingTask(null, 7), "missing player has no pending task");
    }
    static void PreserveRoleProgress()
    {
        var game = Reset();
        var (data, state) = Player(game, 0, 3);
        state.MainRole = CustomRoles.Workhorse;
        state.TaskState.AllTasksCount = 9;
        state.TaskState.CompletedTasksCount = 6;
        state.TaskState.hasTasks = true;
        data.Tasks[0].Complete = true;
        InstalledTaskStatePatch.Postfix(data);
        game.RecomputeTaskCounts();
        Check(state.InitCalls == 0 && state.TaskState.AllTasksCount == 9 && state.TaskState.CompletedTasksCount == 6,
            "redistribution preserves cumulative Workhorse total and completed progress");
        Check(game.TotalTasks == 3 && game.CompletedTasks == 1,
            "shared task bar reads actual task flags rather than cumulative role progress");
        InstalledTaskStatePatch.Postfix(data);
        Check(state.InitCalls == 0 && state.TaskState.CompletedTasksCount == 6,
            "repeated installation observation does not reset completed progress");
        state.TaskState.AllTasksCount = 0;
        InstalledTaskStatePatch.Postfix(data);
        Check(state.InitCalls == 0 && state.TaskState.CompletedTasksCount == 6,
            "zero total with existing progress still cannot trigger initialization");
    }
    static void CountOnlyTaskWinRoles()
    {
        var game = Reset();
        var crew = Player(game, 0, 3);
        crew.Data.Tasks[0].Complete = true;
        var impostor = Player(game, 1, 7);
        impostor.Data.RoleHasTasks = false;
        var personal = Player(game, 2, 5);
        personal.Data.ContributesToTaskWin = false; // E.g. Solsticer: personal tasks, no shared task win.
        personal.State.MainRole = CustomRoles.Solsticer;
        var disconnected = Player(game, 3, 11);
        disconnected.Data.Disconnected = true;
        InstalledTaskStatePatch.Postfix(personal.Data);
        Check(personal.State.TaskState.hasTasks && personal.State.TaskState.AllTasksCount == 5,
            "personal task eligibility uses HasTasks(false) independently of task-win eligibility");
        game.RecomputeTaskCounts();
        Check(game.TotalTasks == 3 && game.CompletedTasks == 1,
            "fake tasks, personal-only tasks and disconnected players do not contribute");
        InstalledTaskStatePatch.Postfix(disconnected.Data);
        Check(disconnected.State.InitCalls == 0, "native rejected disconnected assignment is not initialized");
    }
    static void NativePassThrough()
    {
        foreach (var scenario in new[] { "unmodded_host", "freeplay", "hide_n_seek", "non_normal_mode", "no_client" })
        {
            var game = Reset();
            var pair = Player(game, 0, 4);
            game.TotalTasks = 73; game.CompletedTasks = 12;
            TaskState.InitialTotalTasks = 73;
            if (scenario == "unmodded_host") GameStates.IsModHost = false;
            if (scenario == "freeplay") GameStates.IsFreePlay = true;
            if (scenario == "hide_n_seek") GameStates.IsHideNSeek = true;
            if (scenario == "non_normal_mode") GameStates.IsNormalGame = false;
            if (scenario == "no_client") AmongUsClient.Instance = null;
            Check(CustomTaskCountsPatch.Prefix(game), scenario + " returns true for native recomputation");
            InstalledTaskStatePatch.Postfix(pair.Data);
            InitialInstalledTaskCountsPatch.Postfix();
            Check(game.TotalTasks == 73 && game.CompletedTasks == 12 && TaskState.InitialTotalTasks == 73 &&
                pair.State.InitCalls == 0 && game.Recomputes == 0, scenario + " leaves native counts and task state untouched");
        }
    }
    static void MissingAndStaleData()
    {
        var game = Reset();
        var pair = Player(game, 0, 2);
        pair.Data.Object = null;
        InstalledTaskStatePatch.Postfix(pair.Data);
        Check(pair.State.InitCalls == 0 && game.Recomputes == 0, "missing native player defers initialization");
        pair.Data.Object = new PlayerControl { Data = new NetworkedPlayerInfo { Pointer = new IntPtr(999) } };
        InstalledTaskStatePatch.Postfix(pair.Data);
        Check(pair.State.InitCalls == 0 && game.Recomputes == 0, "stale data object is not attached to current player state");
        pair.Data.Object.Data = pair.Data;
        TOHE.Main.PlayerStates.Clear();
        InstalledTaskStatePatch.Postfix(pair.Data);
        Check(game.Recomputes == 0, "missing mod state defers initialization without exception");
        TOHE.Main.PlayerStates[0] = pair.State;
        pair.State.MainRole = CustomRoles.NotAssigned;
        InstalledTaskStatePatch.Postfix(pair.Data);
        Check(pair.State.InitCalls == 0 && game.Recomputes == 0, "unassigned custom role defers initialization");
        pair.State.MainRole = CustomRoles.Crewmate;
        pair.Data.Role = null;
        InstalledTaskStatePatch.Postfix(pair.Data);
        Check(pair.State.InitCalls == 0, "missing native role defers initialization");
        InstalledTaskStatePatch.Postfix(null);
        Check(game.Recomputes == 0, "missing task data cannot trigger global recomputation");
    }
    static void Main()
    {
        EarlyEmptyObservation();
        PendingCompletionValidation();
        PreserveRoleProgress();
        CountOnlyTaskWinRoles();
        NativePassThrough();
        MissingAndStaleData();
        Console.WriteLine($"TASK_LIFECYCLE_PASS ({assertions} assertions; linked production patch and extracted TaskState.Init)");
    }
}
