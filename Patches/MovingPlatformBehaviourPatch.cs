namespace TOHE;

// https://github.com/tukasa0001/TownOfHost/pull/1274/commits/164d1463e46f0ec453e136c7a2f28a8039cd7fc4

[HarmonyPatch(typeof(MovingPlatformBehaviour))]
public static class MovingPlatformBehaviourPatch
{
    // Initial-state deserialization can run before Unity Start, including after
    // a previous lobby used a different value. Never cache this across ships.
    private static bool isDisabled => Options.DisableAirshipMovingPlatform.GetBool();

    [HarmonyPatch(nameof(MovingPlatformBehaviour.Start)), HarmonyPrefix]
    public static void Start_Prefix(MovingPlatformBehaviour __instance)
    {
        if (isDisabled)
        {
            __instance.transform.localPosition = __instance.DisabledPosition;
            ShipStatus.Instance.Cast<AirshipStatus>().outOfOrderPlat.SetActive(true);
            __instance.MarkClean();
        }
    }
    // IsDirty is a field accessor which native callers may inline. Prevent the
    // state mutations instead, including initial-state deserialization's SetTarget.
    [HarmonyPatch(nameof(MovingPlatformBehaviour.SetTarget)), HarmonyPrefix]
    public static bool SetTarget_Prefix() => !isDisabled;
    [HarmonyPatch(nameof(MovingPlatformBehaviour.SetSide)), HarmonyPrefix]
    public static bool SetSide_Prefix(MovingPlatformBehaviour __instance)
    {
        if (isDisabled) __instance.MarkClean();
        return !isDisabled;
    }
    [HarmonyPatch(nameof(MovingPlatformBehaviour.Use), typeof(PlayerControl)), HarmonyPrefix]
    public static bool Use_Prefix() => !isDisabled;
}
