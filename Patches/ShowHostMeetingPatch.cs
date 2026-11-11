using AmongUs.Data;
using TMPro;
using UnityEngine;

namespace TOHE.Patches;

// Thanks TOU-R: https://github.com/eDonnes124/Town-Of-Us-R/blob/master/source/Patches/ShowHostMeetingPatch.cs
// Who skid this code? Original implementation really sucks

[HarmonyPatch]
public class ShowHostMeetingPatch
{

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
    [HarmonyPostfix]
    public static void Update_Postfix(MeetingHud __instance)
    {
        // Not display in local game, because it will be impossible to complete the meeting
        if (!GameStates.IsOnlineGame) return;

        NetworkedPlayerInfo host = GameData.Instance.GetHost();

        if (host == null) return;

        PlayerMaterial.SetColors(host.DefaultOutfit.ColorId, __instance.HostIcon);
        var hostName = AmongUsClient.Instance.AmHost ? DataManager.Player.Customization.Name : (Main.AllClientRealNames[host.ClientId] ?? "ERROR");
        __instance.ProceedButton.gameObject.GetComponentInChildren<TextMeshPro>().text = string.Format(Translator.GetString("HostIconInMeeting"), hostName);
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    [HarmonyPostfix]

    public static void Setup_Postfix(MeetingHud __instance)
    {
        if (!GameStates.IsOnlineGame) return;

        __instance.ProceedButton.gameObject.transform.localPosition = new(-2.5f, 2.2f, 0);
        __instance.ProceedButton.gameObject.GetComponent<SpriteRenderer>().enabled = false;
        __instance.ProceedButton.GetComponent<PassiveButton>().enabled = false;
        __instance.HostIcon.enabled = true;
        __instance.HostIcon.gameObject.SetActive(true);
        __instance.ProceedButton.gameObject.SetActive(true);
    }
}
