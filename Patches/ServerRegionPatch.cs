using BepInEx.Unity.IL2CPP.Utils.Collections;
using InnerNet;
using TOHE.Modules;

namespace TOHE.Patches;

[HarmonyPatch(typeof(HttpMatchmakerManager), nameof(HttpMatchmakerManager.CoFindGameInfo))]
internal static class FindGameRegionPatch
{
    public static void Prefix(HttpMatchmakerManager __instance,
        ref Il2CppSystem.Action<HttpMatchmakerManager.FindGameByCodeResponse, string> onGameInfo)
    {
        var callback = onGameInfo;
        onGameInfo = (System.Action<HttpMatchmakerManager.FindGameByCodeResponse, string>)((response, token) =>
        {
            // This callback runs before CoFindGameInfo restores the menu region.
            ServerRegion.RememberFoundGame(response);
            // CoSendRequest can refresh after an auth error. It updates the HTTP
            // header/cache, but CoFindGameInfo's captured token still has the old
            // value. Joining must receive the valid token from that refresh.
            if (__instance.TryReadCachedToken(out var currentToken))
                token = currentToken;
            callback?.Invoke(response, token);
        });
    }

    public static void Postfix(ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result == null) return;
        __result = ServerRegionCoroutine.BeforeStart(__result, ServerRegion.ClearLookup).WrapToIl2Cpp();
    }
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoConnectToGameServer))]
internal static class ConnectGameRegionPatch
{
    public static void Postfix(AmongUsClient __instance, MatchMakerModes mode, string ipAddress, ushort port,
        ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result == null) return;
        __result = ServerRegionCoroutine.BeforeStart(__result, () =>
        {
            // A preview can leave GameId pointing at the found room. Creating a
            // new room must use its own selected region even on the same endpoint.
            if (mode == MatchMakerModes.HostAndClient)
                ServerRegion.ClearLookup(false);

            // Resolve before the original's first MoveNext calls SetEndpoint/
            // CoConnect, rather than when its iterator is merely constructed.
            ServerRegion.BeginConnection(__instance.GameId, ipAddress, port);
        }).WrapToIl2Cpp();
    }
}

internal static class ServerRegionCoroutine
{
    public static System.Collections.IEnumerator BeforeStart(Il2CppSystem.Collections.IEnumerator original,
        System.Action onStart)
    {
        // Iterator state keeps this action outside the per-yield loop: it runs
        // once on first advancement, and never for an unstarted coroutine.
        onStart();
        while (original.MoveNext())
            yield return original.Current;
    }
}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.DisconnectInternal))]
internal static class DisconnectGameRegionPatch
{
    public static void Postfix(DisconnectReasons reason)
    {
        // CoConnect disconnects the old socket after a UDP redirect. The new
        // endpoint still belongs to the same region and needs its auth decision.
        if (reason != DisconnectReasons.NewConnection)
            ServerRegion.ClearConnection();
    }
}
