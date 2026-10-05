using AmongUs.GameOptions;
using InnerNet;

namespace TOHE;

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
internal static class MeetingAbilityStartPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(MeetingHud __instance) => MeetingAbilities.BeginMeeting(__instance);
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
internal static class MeetingAbilityEndPatch
{
    [HarmonyPrefix]
    private static void Prefix(MeetingHud __instance) => MeetingAbilities.EndMeeting(__instance);
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
internal static class MeetingAbilityTickPatch
{
    [HarmonyPostfix]
    private static void Postfix() => MeetingAbilities.Tick();
}

// Reuse the Judge UI handler, but never run native TryOverrule for a protocol skill.
[HarmonyPatch(typeof(PlayerVoteArea), nameof(PlayerVoteArea.JudgeOverruleVote))]
internal static class MeetingAbilityClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayerVoteArea __instance)
    {
        if (!MeetingAbilities.OwnsJudgePresentation(PlayerControl.LocalPlayer)) return true;
        MeetingAbilities.ClickJudge(__instance);
        return false;
    }
}

// Fail closed even if another local UI path invokes the native command directly.
[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CmdQueueOverruleVotes))]
internal static class MeetingAbilityNativeQueuePatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !MeetingAbilities.OwnsJudgePresentation(PlayerControl.LocalPlayer);
}

[HarmonyPatch(typeof(JudgeRole), nameof(JudgeRole.TryOverrule))]
internal static class MeetingAbilityNativeTryPatch
{
    [HarmonyPrefix]
    private static bool Prefix(JudgeRole __instance, ref bool __result)
    {
        if (!MeetingAbilities.OwnsJudgePresentation(__instance.Player)) return true;
        __result = false;
        return false;
    }
}

// The generic skill has its own host-validated rules; native Judge task gating
// belongs only to the genuine vanilla Judge ability.
[HarmonyPatch(typeof(JudgeRole), nameof(JudgeRole.IsBlockedByTasks))]
internal static class MeetingAbilityTaskGatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(JudgeRole __instance, ref bool __result)
    {
        if (!MeetingAbilities.OwnsJudgePresentation(__instance.Player)) return true;
        __result = MeetingAbilities.NativeJudgeBlocked(__instance.Player);
        return false;
    }
}

// ImpostorRole.Deinitialize destroys its header task. Eraser's temporary local
// Judge presentation must keep that exact task object along with real task state.
[HarmonyPatch(typeof(ImpostorRole), nameof(ImpostorRole.Deinitialize))]
internal static class MeetingAbilityPreserveHeaderPatch
{
    [HarmonyPrefix]
    private static bool Prefix([HarmonyArgument(0)] PlayerControl targetPlayer) =>
        !MeetingAbilities.PreservingTasks(targetPlayer);
}
