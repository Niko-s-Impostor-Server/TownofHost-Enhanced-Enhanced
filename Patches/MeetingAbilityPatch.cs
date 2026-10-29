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