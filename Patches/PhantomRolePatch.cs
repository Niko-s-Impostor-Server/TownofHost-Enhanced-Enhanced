using AmongUs.GameOptions;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TOHE.Roles.Core;
using UnityEngine;

namespace TOHE.Patches;

[HarmonyPatch(typeof(PlayerControl))]
public static class PhantomRolePatch
{
    private static readonly Il2CppSystem.Collections.Generic.List<PlayerControl> InvisibilityList = new();
    private static readonly Dictionary<byte, string> PetsList = [];
    private static readonly Dictionary<byte, uint> ViewEpochs = [];
    private static uint nextViewEpoch;

    private static uint BeginView(PlayerControl phantom)
    {
        ViewEpochs[phantom.PlayerId] = ++nextViewEpoch;
        return nextViewEpoch;
    }

    private static bool CurrentView(OfficialSessionContext context, uint epoch, PlayerControl phantom, PlayerControl seer)
        => context.IsCurrent() && AmongUsClient.Instance.AmHost && !GameStates.IsEnded
        && phantom != null && phantom.Data != null && phantom.IsAlive() && !phantom.Data.Disconnected
        && seer != null && seer.Data != null && !seer.Data.Disconnected && seer.GetClientId() >= 0
        && Main.AllPlayerControls.Contains(phantom) && Main.AllPlayerControls.Contains(seer)
        && ViewEpochs.TryGetValue(phantom.PlayerId, out var current) && current == epoch;

    /*
     *  InnerSloth is doing careless stuffs. They didnt put amModdedHost check in cmd check vanish appear
     *  We temporary need to patch the whole cmd function and wait for the next hotfix from them
    */
    [HarmonyPatch(nameof(PlayerControl.CmdCheckVanish)), HarmonyPrefix]
    private static bool CmdCheckVanish_Prefix(PlayerControl __instance, float maxDuration)
    {
        if (PhantomAbility.IsEnabled(__instance))
        {
            PhantomAbility.Request(__instance, maxDuration);
            return false;
        }
        if (AmongUsClient.Instance.AmHost)
        {
            __instance.CheckVanish();
            return false;
        }
        __instance.SetRoleInvisibility(true, true, false);
        MessageWriter messageWriter = AmongUsClient.Instance.StartImmediate(__instance.NetId, (byte)RpcCalls.CheckVanish, SendOption.Reliable, AmongUsClient.Instance.HostId);
        messageWriter.Write(maxDuration);
        AmongUsClient.Instance.FinishImmediate(messageWriter);
        return false;
    }

    [HarmonyPatch(nameof(PlayerControl.CmdCheckAppear)), HarmonyPrefix]
    private static bool CmdCheckAppear_Prefix(PlayerControl __instance, bool shouldAnimate)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            __instance.CheckAppear(shouldAnimate);
            return false;
        }
        MessageWriter messageWriter = AmongUsClient.Instance.StartImmediate(__instance.NetId, (byte)RpcCalls.CheckAppear, SendOption.Reliable, AmongUsClient.Instance.HostId);
        messageWriter.Write(shouldAnimate);
        AmongUsClient.Instance.FinishImmediate(messageWriter);
        return false;
    }
    // Called when Phantom press vanish button when visible
    [HarmonyPatch(nameof(PlayerControl.CheckVanish)), HarmonyPrefix]
    private static bool CheckVanish_Prefix(PlayerControl __instance)
    {
        if (PhantomAbility.IsEnabled(__instance))
        {
            if (PhantomAbility.CanHandle(__instance))
            {
                PhantomAbility.TryActivate(__instance);
                PhantomAbility.RestoreVisibleButton(__instance);
                // StartAppear is a host broadcast; it confirms a visible button without a vanish animation.
                __instance.RpcAppear(false);
            }
            return false;
        }
        if (!AmongUsClient.Instance.AmHost) return true;

        var phantom = __instance;
        var compatible = OfficialAnticheatPolicy.Enabled;
        var context = OfficialSessionContext.Capture();
        var epoch = compatible ? BeginView(phantom) : 0;
        Logger.Info($"Player: {phantom.GetRealName()}", "CheckVanish");
        if (phantom.IsAlive() && GameStates.IsInTask && phantom.Data.Role.Role == RoleTypes.Phantom)
            AfkMonitor.RecordActivity(phantom);

        foreach (var target in Main.AllPlayerControls)
        {
            if (!target.IsAlive() || phantom == target || target.AmOwner || !target.HasDesyncRole()) continue;

            // Set Phantom when his start vanish
            phantom.RpcSetRoleDesync(RoleTypes.Phantom, target.GetClientId());
            // Check vanish again for desync role
            phantom.RpcCheckVanishDesync(target);

            _ = new LateTask(() =>
            {
                if (Main.MeetingIsStarted || phantom == null) return;
                if (compatible && !CurrentView(context, epoch, phantom, target)) return;

                var petId = phantom.Data.DefaultOutfit.PetId;
                if (petId != "")
                {
                    PetsList[phantom.PlayerId] = petId;
                    phantom?.RpcSetPetDesync("", target);
                }
                phantom?.RpcExileDesync(target);
            }, 1.2f, $"Set Phantom invisible {target.PlayerId}", shoudLog: false);
        }
        InvisibilityList.Add(phantom);
        return true;
    }

    [HarmonyPatch(nameof(PlayerControl.HandleServerAppear)), HarmonyPrefix]
    private static bool HandleServerAppear_Prefix(PlayerControl __instance)
    {
        if (!PhantomAbility.IsEnabled(__instance)) return true;
        PhantomAbility.RestoreVisibleButton(__instance);
        return false;
    }
    // Called when Phantom press appear button when is invisible
    [HarmonyPatch(nameof(PlayerControl.CheckAppear)), HarmonyPrefix]
    private static void CheckAppear_Prefix(PlayerControl __instance, bool shouldAnimate)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        var phantom = __instance;
        var compatible = OfficialAnticheatPolicy.Enabled;
        var context = OfficialSessionContext.Capture();
        var epoch = compatible ? BeginView(phantom) : 0;
        Logger.Info($"Player: {phantom.GetRealName()} => shouldAnimate {shouldAnimate}", "CheckAppear");
        if (shouldAnimate && phantom.IsAlive() && GameStates.IsInTask) AfkMonitor.RecordActivity(phantom);

        if (phantom.inVent)
        {
            phantom.MyPhysics.RpcBootFromVent(Main.LastEnteredVent[phantom.PlayerId].Id);
        }

        foreach (var target in Main.AllPlayerControls)
        {
            if (!target.IsAlive() || phantom == target || target.AmOwner || !target.HasDesyncRole()) continue;

            var clientId = target.GetClientId();

            // Set Phantom when his end vanish
            phantom.RpcSetRoleDesync(RoleTypes.Phantom, clientId);

            _ = new LateTask(() =>
            {
                if (compatible && (Main.MeetingIsStarted || !CurrentView(context, epoch, phantom, target))) return;
                // Check appear again for desync role
                if (target != null)
                    phantom?.RpcCheckAppearDesync(shouldAnimate, target);
            }, 0.5f, $"Check Appear when vanish is over {target.PlayerId}", shoudLog: false);

            _ = new LateTask(() =>
            {
                if (Main.MeetingIsStarted || phantom == null) return;
                if (compatible && !CurrentView(context, epoch, phantom, target)) return;

                InvisibilityList.Remove(phantom);
                phantom?.RpcSetRoleDesync(RoleTypes.Scientist, clientId);

                if (PetsList.TryGetValue(phantom.PlayerId, out var petId))
                {
                    phantom?.RpcSetPetDesync(petId, target);
                }
            }, 1.8f, $"Set Scientist when vanish is over {target.PlayerId}", shoudLog: false);
        }
    }
    [HarmonyPatch(nameof(PlayerControl.SetRoleInvisibility)), HarmonyPrefix]
    private static void SetRoleInvisibility_Prefix(PlayerControl __instance, bool isActive, bool shouldAnimate, bool playFullAnimation)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        Logger.Info($"Player: {__instance.GetRealName()} => Is Active {isActive}, Animate:{shouldAnimate}, Full Animation:{playFullAnimation}", "SetRoleInvisibility");
    }

    public static void OnReportDeadBody(PlayerControl seer, bool force)
    {
        if (InvisibilityList.Count == 0 || !seer.IsAlive() || seer.Data.Role.Role is RoleTypes.Phantom || seer.AmOwner || !seer.HasDesyncRole()) return;

        foreach (var phantom in InvisibilityList.GetFastEnumerator())
        {
            if (!phantom.IsAlive())
            {
                InvisibilityList.Remove(phantom);
                continue;
            }

            Main.Instance.StartCoroutine(CoRevertInvisible(phantom, seer, force));
        }
    }
    private static bool InValid(PlayerControl phantom, PlayerControl seer) => seer.GetClientId() == -1 || phantom == null;
    private static System.Collections.IEnumerator CoRevertInvisible(PlayerControl phantom, PlayerControl seer, bool force)
    {
        var compatible = OfficialAnticheatPolicy.Enabled;
        var context = OfficialSessionContext.Capture();
        ViewEpochs.TryGetValue(phantom.PlayerId, out var epoch);
        bool Invalid() => InValid(phantom, seer) || compatible && !CurrentView(context, epoch, phantom, seer);
        // Set Scientist for meeting
        if (!force)
        {
            yield return new WaitForSeconds(0.0001f);
        }
        if (Invalid()) yield break;

        phantom?.RpcSetRoleDesync(RoleTypes.Scientist, seer.GetClientId());

        // Return Phantom in meeting
        yield return new WaitForSeconds(1f);
        {
            if (Invalid()) yield break;

            phantom?.RpcSetRoleDesync(RoleTypes.Phantom, seer.GetClientId());
        }
        // Revert invis for phantom
        yield return new WaitForSeconds(1f);
        {
            if (Invalid()) yield break;

            phantom?.RpcStartAppearDesync(false, seer);
        }
        // Set Scientist back
        yield return new WaitForSeconds(4f);
        {
            if (Invalid()) yield break;

            phantom?.RpcSetRoleDesync(RoleTypes.Scientist, seer.GetClientId());

            if (PetsList.TryGetValue(phantom.PlayerId, out var petId))
            {
                phantom?.RpcSetPetDesync(petId, seer);
            }
        }
        yield break;
    }
    internal static void ResetViews()
    {
        InvisibilityList.Clear();
        PetsList.Clear();
        ViewEpochs.Clear();
    }

    public static void AfterMeeting()
    {
        ResetViews();
        foreach (var player in Main.AllPlayerControls)
        {
            var role = player.GetRoleClass();
            if (role?.UsesPhantomAbility == true) role.PhantomAbilityReadyAt = 0f;
        }
    }
}
// Fixed vanilla bug for host (from TOH-Y)
[HarmonyPatch(typeof(PhantomRole), nameof(PhantomRole.UseAbility))]
public static class PhantomRoleUseAbilityPatch
{
    public static bool Prefix(PhantomRole __instance)
    {
        if (PhantomAbility.IsEnabled(__instance.Player))
        {
            if (PhantomAbility.CanRequest(__instance.Player) && !__instance.IsCoolingDown && !__instance.fading)
            {
                __instance.SetCooldown();
                __instance.Player.CmdCheckVanish(0f);
            }
            return false;
        }
        if (!AmongUsClient.Instance.AmHost) return true;

        if (__instance.Player.AmOwner && !__instance.Player.Data.IsDead && __instance.Player.moveable && !Minigame.Instance && !__instance.IsCoolingDown && !__instance.fading)
        {
            System.Func<RoleEffectAnimation, bool> roleEffectAnimation = x => x.effectType == RoleEffectAnimation.EffectType.Vanish_Charge;
            if (!__instance.Player.currentRoleAnimations.Find(roleEffectAnimation) && !__instance.Player.walkingToVent && !__instance.Player.inMovingPlat)
            {
                if (__instance.isInvisible)
                {
                    __instance.MakePlayerVisible(true, true);
                    return false;
                }
                DestroyableSingleton<HudManager>.Instance.AbilityButton.SetSecondImage(__instance.Ability);
                DestroyableSingleton<HudManager>.Instance.AbilityButton.OverrideText(DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.PhantomAbilityUndo, new Il2CppReferenceArray<Il2CppSystem.Object>(0)));
                __instance.Player.CmdCheckVanish(GameManager.Instance.LogicOptions.GetRoleFloat(AmongUs.GameOptions.FloatOptionNames.PhantomDuration));
                return false;
            }
        }
        return false;
    }
}
