using System;
using AmongUs.GameOptions;
using AmongUs.HTTP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Hazel;
using InnerNet;
using TOHE.Modules;

namespace TOHE.Patches;

[HarmonyPatch]
internal static class OfficialAnticheatLifecyclePatch
{
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    [HarmonyPostfix, HarmonyPriority(Priority.First)]
    private static void Joined()
    {
        OfficialSessionContext.BeginLobby();
        OfficialAnticheatPolicy.BeginLobby();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    [HarmonyPrefix]
    private static void Disconnected()
    {
        OfficialSessionContext.Reset();
        OfficialAnticheatPolicy.Reset();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGame))]
    [HarmonyPostfix, HarmonyPriority(Priority.First)]
    private static void StartRound(ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result == null) return;
        __result = BeginRoundOnFirstAdvance(__result, OfficialSessionContext.Capture()).WrapToIl2Cpp();
    }

    private static System.Collections.IEnumerator BeginRoundOnFirstAdvance(
        Il2CppSystem.Collections.IEnumerator original, OfficialSessionContext context)
    {
        // CoStartGame only constructs its native iterator. Freeze when Unity
        // actually advances it, and ignore an unstarted iterator from an old room.
        if (!context.IsCurrent()) yield break;
        OfficialAnticheatPolicy.FreezeRound();
        OfficialSessionContext.BeginRound();
        while (original.MoveNext()) yield return original.Current;
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void EndRound()
    {
        OfficialSessionContext.EndRound();
        OfficialAnticheatPolicy.EndRound();
        CustomRpcTransport.Reset();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnBecomeHost))]
    [HarmonyPrefix]
    private static void HostChanged()
    {
        OfficialSessionContext.Reset();
        CustomRpcTransport.Reset();
    }

    // Both host and guest receive OnPlayerLeft after HostId is updated.
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    [HarmonyPostfix]
    private static void AfterPlayerLeft()
    {
        if (OfficialSessionContext.ObserveHostMigration()) CustomRpcTransport.Reset();
    }

    [HarmonyPatch(typeof(GameOptionsManager), nameof(GameOptionsManager.GameHostOptions), MethodType.Setter)]
    [HarmonyPostfix]
    private static void HostOptions(IGameOptions value)
    {
        if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
            OfficialAnticheatPolicy.CaptureCanonicalOptions(value);
    }

    [HarmonyPatch(typeof(GameOptionsManager), nameof(GameOptionsManager.CurrentGameOptions), MethodType.Setter)]
    [HarmonyPostfix]
    private static void CurrentOptions(IGameOptions value) => OfficialAnticheatPolicy.CaptureCanonicalOptions(value);

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSyncSettings))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void SyncLobbyOptions() =>
        OfficialAnticheatPolicy.CaptureCanonicalOptions(GameOptionsManager.Instance?.CurrentGameOptions);

    // Unity invokes this substantial lobby callback every frame. It also covers
    // setters/getters that IL2CPP may inline into native settings editors.
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
    [HarmonyPostfix]
    private static void LobbyUpdate() => SyncLobbyOptions();

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.FinallyBegin))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void BeforeHostStart() => SyncLobbyOptions();

    // Native HandleMessage sets Started before constructing CoStartGame. Read
    // the canonical lobby options at that boundary, while they are still lobby
    // settings, rather than depending on an inlinable property setter alone.
    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HandleMessage))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void BeforeNetworkStart(MessageReader reader)
    {
        if (reader.Tag == 2) SyncLobbyOptions();
    }

    // The request URL is captured before cross-region lookup restores the menu
    // selection. Authentication retries do not overwrite the game request.
    [HarmonyPatch(typeof(HttpMatchmakerManager), nameof(HttpMatchmakerManager.CoSendRequest),
        typeof(RetryableWebRequest), typeof(string), typeof(int), typeof(Il2CppSystem.Action<HttpMatchmakerManager.MatchmakerFailure>))]
    [HarmonyPostfix]
    private static void MatchmakerRequest(RetryableWebRequest request, string context,
        ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result == null || context is not ("request gamecode server" or "find host server")) return;
        __result = ServerRegionCoroutine.BeforeStart(__result,
            () => ServerRegion.RememberMatchmakerSource(request.Url)).WrapToIl2Cpp();
    }
}
