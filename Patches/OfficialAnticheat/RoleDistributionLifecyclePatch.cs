using BepInEx.Unity.IL2CPP.Utils.Collections;
using InnerNet;
using System.Collections;
using System.Reflection;
using TOHE.Roles.Core;

namespace TOHE;

[HarmonyPatch]
internal static class RoleDistributionLifecyclePatch
{
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Joined() => RoleDistribution.Reset();

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnBecomeHost))]
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Cancel() => RoleDistribution.Cancel("Room or round ended");

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void PlayerLeft(ClientData data)
    {
        if (data?.Character != null) RoleDistribution.OnPlayerLeft(data.Character.PlayerId);
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    [HarmonyPrefix]
    private static void Tick() => RoleDistribution.TickContext();
}

// These are managed mod entrypoints: skip their business work while the native
// presentation fields are masked, including the early InGame intro interval.
[HarmonyPatch]
internal static class RoleDistributionBusinessGate
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ExtendedPlayerControl), nameof(ExtendedPlayerControl.RpcChangeRoleBasis));
        yield return AccessTools.Method(typeof(AntiBlackout), nameof(AntiBlackout.SetIsDead));
        yield return AccessTools.Method(typeof(AntiBlackout), nameof(AntiBlackout.RestoreIsDead));
        yield return AccessTools.Method(typeof(AntiBlackout), nameof(AntiBlackout.SendGameData));
        yield return AccessTools.Method(typeof(AntiBlackout), nameof(AntiBlackout.SetRealPlayerRoles));
        yield return AccessTools.Method(typeof(BlackScreenFix), nameof(BlackScreenFix.Tick));
        yield return AccessTools.Method(typeof(FixedUpdateInNormalGamePatch), nameof(FixedUpdateInNormalGamePatch.Postfix));
        yield return AccessTools.Method(typeof(CustomRoleManager), nameof(CustomRoleManager.OnFixedUpdate));
        yield return AccessTools.Method(typeof(CustomRoleManager), nameof(CustomRoleManager.OnFixedAddonUpdate));
    }
    private static bool Prefix() => RoleDistribution.GameplayReady;
}

[HarmonyPatch]
internal static class RoleDistributionRepairGate
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(BlackScreenFix), nameof(BlackScreenFix.Request));
        yield return AccessTools.Method(typeof(BlackScreenFix), nameof(BlackScreenFix.RequestAutomatic));
    }
    private static bool Prefix(ref string __result)
    {
        if (RoleDistribution.GameplayReady) return true;
        __result = "BlackScreenFixWaiting";
        return false;
    }
}

[HarmonyPatch(typeof(LogicGameFlowNormal), nameof(LogicGameFlowNormal.CheckEndCriteria))]
internal static class RoleDistributionEndGate
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => RoleDistribution.GameplayReady;
}

[HarmonyPatch]
internal static class RoleDistributionSkillGate
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(PlayerControl), nameof(PlayerControl.CheckMurder));
        yield return AccessTools.Method(typeof(PlayerControl), nameof(PlayerControl.CheckProtect));
        yield return AccessTools.Method(typeof(PlayerControl), nameof(PlayerControl.CheckShapeshift));
        yield return AccessTools.Method(typeof(PlayerControl), nameof(PlayerControl.CheckVanish));
        yield return AccessTools.Method(typeof(PlayerControl), nameof(PlayerControl.CheckAppear));
        yield return AccessTools.Method(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody));
        yield return AccessTools.Method(typeof(PlayerControl), nameof(PlayerControl.CompleteTask));
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => RoleDistribution.GameplayReady;
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CoSetRole))]
internal static class RoleDistributionNativeRoleGate
{
    private static void Postfix(ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result == null) return;
        __result = Run(__result, RoleDistribution.IsAuthoritativeRoleCall, OfficialSessionContext.Capture()).WrapToIl2Cpp();
    }
    private static IEnumerator Run(Il2CppSystem.Collections.IEnumerator original, bool authoritative, OfficialSessionContext context)
    {
        // Check at execution, including an iterator constructed before startup.
        try
        {
            while (context.IsCurrent() && (authoritative || !RoleDistribution.BlocksOrdinaryRoleChanges) && original.MoveNext())
                yield return original.Current;
        }
        finally { original.TryCast<Il2CppSystem.IDisposable>()?.Dispose(); }
    }
}
