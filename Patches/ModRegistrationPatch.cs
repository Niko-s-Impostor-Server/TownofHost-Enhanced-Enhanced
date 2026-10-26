using AmongUs.Matchmaking;
using InnerNet;
using System;
using TOHE.Modules;

namespace TOHE.Patches;

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HostGame))]
internal static class LocalHostModRegistrationPatch
{
    public static void Prefix(InnerNetClient __instance, out string __state)
    {
        __state = __instance.NetworkMode is NetworkModes.LocalGame or NetworkModes.FreePlay
            ? ModRegistration.SuspendForLocalHost()
            : null;
    }

    public static Exception Finalizer(string __state, Exception __exception)
    {
        ModRegistration.RestoreAfterLocalHost(__state);
        return __exception;
    }
}

// Build 7489's GameFilter.ResolveSubFilter omits "mod", so saved mod filters
// reload with a null SubFilter. The game's helper refuses to update that entry.
// Remove only the malformed entry so the native helper can add our GUID.
[HarmonyPatch(typeof(CurrentModRegistration), nameof(CurrentModRegistration.UpdateFilterSetWithModRegistrationSettings))]
internal static class ModFilterRegistrationPatch
{
    [HarmonyPrefix]
    private static void RemoveInvalidSavedFilter([HarmonyArgument(0)] GameFilterSet filterSet)
    {
        var filters = filterSet?.Filters;
        if (filters == null || !CurrentModRegistration.TryGetModRegistrationGuid(out _)) return;

        for (int i = filters.Count - 1; i >= 0; i--)
        {
            var filter = filters[i];
            if (filter == null || filter.Key != "mod" || filter.SubFilter != null) continue;

            filters.RemoveAt(i);
            Main.Logger.LogWarning("Removed a saved mod filter with no SubFilter; the game will rebuild it from the registered GUID.");
        }
    }
}
