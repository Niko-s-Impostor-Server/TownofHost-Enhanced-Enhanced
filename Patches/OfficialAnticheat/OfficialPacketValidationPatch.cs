using System;
using Hazel;
using InnerNet;

namespace TOHE;

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendOrDisconnect))]
internal static class OfficialPacketValidationPatch
{
    internal static bool Prefix(InnerNetClient __instance, MessageWriter msg)
    {
        if (!OfficialAnticheatPolicy.Enabled) return true;
        try { OfficialNetworkSend.Send(__instance, msg, "SendOrDisconnect"); }
        catch (Exception) { /* The bridge records failure and cancels dependents. */ }
        return false;
    }
}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.StartRpcImmediately))]
internal static class OfficialImmediateStartPatch
{
    internal static bool Prefix(InnerNetClient __instance, uint targetNetId, byte callId, SendOption sendOption,
        int targetClientId, ref MessageWriter __result)
    {
        if (!OfficialAnticheatPolicy.Enabled) return true;
        __result = __instance.StartImmediate(targetNetId, callId, sendOption, targetClientId);
        return false;
    }
}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.FinishRpcImmediately))]
internal static class OfficialImmediateFinishPatch
{
    internal static bool Prefix(InnerNetClient __instance, MessageWriter msg)
    {
        if (!OfficialAnticheatPolicy.Enabled) return true;
        try { __instance.FinishImmediate(msg); }
        catch (Exception) { /* Explicit bridge failure survives a swallowed callback. */ }
        return false;
    }
}
