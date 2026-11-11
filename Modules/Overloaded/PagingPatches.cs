using HarmonyLib;

namespace TOHE.Modules;

// Paging implementation from Overloaded; see Resources/Notices/Overloaded.LICENSE.txt.
internal static class PagingPatches
{
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    public static class MeetingHudStartPatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            __instance.gameObject.AddComponent<MeetingHudPagingBehaviour>().meetingHud = __instance;
            Logger.Info("Added paging behaviour to meeting hud!", "Overloaded");
        }
    }

    [HarmonyPatch(typeof(ShapeshifterMinigame), nameof(ShapeshifterMinigame.Begin))]
    public static class ShapeshifterMinigameBeginPatch
    {
        public static void Postfix(ShapeshifterMinigame __instance)
        {
            __instance.gameObject.AddComponent<ShapeShifterPagingBehaviour>().shapeshifterMinigame = __instance;
            Logger.Info("Added paging behaviour to meeting shifter!", "Overloaded");
        }
    }

    [HarmonyPatch(typeof(VitalsMinigame), nameof(VitalsMinigame.Begin))]
    public static class VitalsMinigameBeginPatch
    {
        public static void Postfix(VitalsMinigame __instance)
        {
            __instance.gameObject.AddComponent<VitalsPagingBehaviour>().vitalsMinigame = __instance;
            Logger.Info("Added vitals paging behaviour to meeting vitals!", "Overloaded");
        }
    }
}
