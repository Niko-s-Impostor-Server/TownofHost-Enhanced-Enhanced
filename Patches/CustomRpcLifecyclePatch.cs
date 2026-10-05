namespace TOHE;

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
