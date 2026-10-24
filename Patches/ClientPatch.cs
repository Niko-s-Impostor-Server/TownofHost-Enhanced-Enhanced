using InnerNet;
using TOHE.Modules;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE;

[HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.MakePublic))]
internal class MakePublicPatch
{
    public static bool Prefix(/*GameStartManager __instance*/)
    {
        // 定数設定による公開ルームブロック
        if (!Main.AllowPublicRoom)
        {
            var message = GetString("DisabledByProgram");
            Logger.Info(message, "MakePublicPatch");
            Logger.SendInGame(message);
            return false;
        }
        if (ModUpdater.isBroken || (ModUpdater.hasUpdate && ModUpdater.forceUpdate) || !VersionChecker.IsSupported)
        {
            var message = "";
            if (!VersionChecker.IsSupported) message = GetString("UnsupportedVersion");
            if (ModUpdater.isBroken) message = GetString("ModBrokenMessage");
            if (ModUpdater.hasUpdate) message = GetString("CanNotJoinPublicRoomNoLatest");
            Logger.Info(message, "MakePublicPatch");
            Logger.SendInGame(message);
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(MMOnlineManager), nameof(MMOnlineManager.Start))]
internal class MMOnlineManagerStartPatch
{
    public static void Postfix(/*MMOnlineManager __instance*/)
    {
        if (!((ModUpdater.hasUpdate && ModUpdater.forceUpdate) || ModUpdater.isBroken || !VersionChecker.IsSupported)) return;
        var obj = GameObject.Find("FindGameButton");
        if (obj)
        {
            obj?.SetActive(false);
            var parentObj = obj.transform.parent.gameObject;
            var textObj = Object.Instantiate(obj.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>());
            textObj.transform.position = new Vector3(1f, -0.3f, 0);
            textObj.name = "CanNotJoinPublic";

            string message = "";
            if (!VersionChecker.IsSupported)
            {
                message = GetString("UnsupportedVersion");
            }
            else if (ModUpdater.isBroken)
            {
                message = GetString("ModBrokenMessage");
            }
            else if (ModUpdater.hasUpdate)
            {
                message = GetString("CanNotJoinPublicRoomNoLatest");
            }
            _ = new LateTask(() => { textObj.text = $"<size=2>{Utils.ColorString(Color.red, message)}</size>"; }, 0.01f, "Can Not Join Public");
        }
    }
}
[HarmonyPatch(typeof(SplashManager), nameof(SplashManager.Update))]
internal class SplashLogoAnimatorPatch
{
    public static void Prefix(SplashManager __instance)
    {
        if (DebugModeManager.AmDebugger)
        {
            __instance.sceneChanger.AllowFinishLoadingScene();
            __instance.startedSceneLoad = true;
        }
    }
}
[HarmonyPatch(typeof(BanMenu), nameof(BanMenu.SetVisible))]
internal class BanMenuSetVisiblePatch
{
    public static void Postfix(BanMenu __instance)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        // Keep the game's menu visibility handling; only host moderation controls
        // must remain visible, including while the host is dead.
        __instance.BanButton.gameObject.SetActive(true);
        __instance.KickButton.gameObject.SetActive(true);
    }
}
[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.CanBan))]
internal class InnerNetClientCanBanPatch
{
    public static bool Prefix(InnerNetClient __instance, ref bool __result)
    {
        __result = __instance.AmHost;
        return false;
    }
}
[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.KickPlayer))]
internal class KickPlayerPatch
{
    // Rate-limit a connection, not an account: a banned account may reconnect immediately.
    public static Dictionary<int, int> AttemptedKickPlayerList = [];
    public static bool Prefix(InnerNetClient __instance, [HarmonyArgument(0)] int clientId, [HarmonyArgument(1)] bool ban)
    {
        if (!__instance.AmHost) return true;
        if (__instance.ClientId == clientId)
        {
            Logger.SendInGame(string.Format("Game Attempting to {0} Host, Blocked the attempt.", ban ? "Ban" : "Kick"));
            Logger.Info("How the fuck host are kicking it self", "KickPlayerPatch");
            return false;
        }

        if (!AttemptedKickPlayerList.ContainsKey(clientId))
            AttemptedKickPlayerList.Add(clientId, 0);
        else if (AttemptedKickPlayerList[clientId] < 10)
        {
            Logger.Fatal($"Kick player Request too fast! Canceled.", "KickPlayerPatch");
            return false;
        }
        if (ban)
        {
            var player = __instance.GetClient(clientId) ?? __instance.GetRecentClient(clientId);
            BanManager.AddSessionBan(player);
            BanManager.AddBanPlayer(player);
        }

        return true;
    }
}
//[HarmonyPatch(typeof(ResolutionManager), nameof(ResolutionManager.SetResolution))]
//class SetResolutionManager
//{
//    public static void Postfix()
//    {
//        if (MainMenuManagerPatch.qqButton != null)
//            MainMenuManagerPatch.qqButton.transform.localPosition = Vector3.Reflect(MainMenuManagerPatch.template.transform.localPosition, Vector3.left);
//        if (MainMenuManagerPatch.discordButton != null)
//            MainMenuManagerPatch.discordButton.transform.localPosition = Vector3.Reflect(MainMenuManagerPatch.template.transform.localPosition, Vector3.left);
//        if (MainMenuManagerPatch.updateButton != null)
//            MainMenuManagerPatch.updateButton.transform.localPosition = MainMenuManagerPatch.template.transform.localPosition + new Vector3(0.25f, 0.75f);
//    }
//}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendAllStreamedObjects))]
internal class InnerNetObjectSerializePatch
{
    public static void Prefix()
    {
        // This code is sometimes called before the lobby has been created
        try
        {
            if (AmongUsClient.Instance.AmHost)
                GameOptionsSender.SendAllGameOptions();
        }
        catch
        { }
    }
}

//[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.SendClientReady))]
//internal class SendClientReadyPatch
//{
//    public static void Postfix()
//    {
//        if (!AmongUsClient.Instance.AmHost)
//        {
//            if (PlayerControl.LocalPlayer)
//            {
//                RPC.RpcVersionCheck();
//            }
//        }
//    }
//}
