namespace TOHE;

[HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.SendAllStreamedObjects))]
internal static class CustomRpcSealBeforeNativeCyclePatch
{
    public static void Prefix()
    {
        if (OfficialAnticheatPolicy.Enabled) CustomRpcTransport.FlushPending();
    }
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
internal static class CustomRpcJoinPatch
{
    public static void Prefix() => CustomRpcTransport.Reset();
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
internal static class CustomRpcDisconnectPatch
{
    public static void Prefix() => CustomRpcTransport.Reset();
}
