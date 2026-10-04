namespace TOHE;

[HarmonyPatch(typeof(GameData), nameof(GameData.RecomputeTaskCounts))]
class CustomTaskCountsPatch
{
    internal static bool UsesCustomCounts => AmongUsClient.Instance != null && GameStates.IsModHost &&
        GameStates.IsNormalGame && !GameStates.IsHideNSeek && !GameStates.IsFreePlay;

    public static bool Prefix(GameData __instance)
    {
        if (!UsesCustomCounts) return true;

        __instance.TotalTasks = 0;
        __instance.CompletedTasks = 0;
        foreach (var p in __instance.AllPlayers)
        {
            if (p == null || p.Disconnected || p.Tasks == null) continue;
            // TaskState tracks role progress (including cumulative Workhorse tasks).
            // The actual installed list owns the shared task bar, even when the
            // intro observed an empty list before ShipStatus.Begin assigned it.
            if (Utils.HasTasks(p))
            {
                foreach (var task in p.Tasks)
                {
                    __instance.TotalTasks++;
                    if (task.Complete) __instance.CompletedTasks++;
                }
            }
        }

        return false;
    }
}

[HarmonyPatch(typeof(NetworkedPlayerInfo), "SetTasks")]
class InstalledTaskStatePatch
{
    internal static bool HasPendingTask(PlayerControl player, uint taskId)
    {
        if (player == null || player.Data == null || player.Data.Tasks == null) return false;
        foreach (var task in player.Data.Tasks)
            if (task.Id == taskId) return !task.Complete;
        return false;
    }

    public static void Postfix(NetworkedPlayerInfo __instance)
    {
        if (!CustomTaskCountsPatch.UsesCustomCounts || __instance == null || __instance.Disconnected ||
            __instance.Tasks == null || __instance.Object == null || __instance.Role == null ||
            GameData.Instance == null ||
            !Main.PlayerStates.TryGetValue(__instance.PlayerId, out var state) ||
            state.MainRole >= CustomRoles.NotAssigned) return;

        var player = __instance.Object;
        if (player.Data == null || player.Data.Pointer != __instance.Pointer) return;
        var taskState = state.TaskState;
        // Init does not reset CompletedTasksCount, but it replaces AllTasksCount.
        // Only repair the early empty/uninitialized observation; a later task
        // redistribution must preserve accumulated role progress and totals.
        if (taskState.AllTasksCount <= 0 && taskState.CompletedTasksCount == 0)
        {
            taskState.hasTasks = false;
            state.InitTask(player);
        }
        GameData.Instance.RecomputeTaskCounts();
    }
}

[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Begin))]
class InitialInstalledTaskCountsPatch
{
    public static void Postfix()
    {
        if (!CustomTaskCountsPatch.UsesCustomCounts || !AmongUsClient.Instance.AmHost ||
            GameData.Instance == null) return;
        // Begin has synchronously installed the full first batch through
        // NetworkedPlayerInfo.SetTasks before returning on the host.
        GameData.Instance.RecomputeTaskCounts();
        TaskState.InitialTotalTasks = GameData.Instance.TotalTasks;
    }
}
