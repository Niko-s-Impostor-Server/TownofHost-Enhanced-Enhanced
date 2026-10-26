using InnerNet;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TOHE.Patches;

// Adapted from EndlessHostRoles' FindAGameManagerPatch:
// https://github.com/Gurge44/EndlessHostRoles/blob/6ced0ff0880a4efd654c9d0734faf9c37f43ce5b/Patches/FindGameManagerPatch.cs
// EHR credits D1GQ/BetterAmongUs for the original FindAGameManager patch.
// Keep the game's row creation, layout, input and navigation intact. This is
// intentionally limited to decorating rows after the native list is populated.
[HarmonyPatch(typeof(FindAGameManager), nameof(FindAGameManager.HandleList))]
internal static class RoomBrowserPatch
{
    private const string LabelName = "TOHE.HostAndCode";

    [HarmonyPostfix]
    private static void AfterList(FindAGameManager __instance)
    {
        foreach (var container in __instance.gameContainers)
        {
            if (!container || !container.gameObject.activeSelf || !container.capacity || !container.tag1) continue;

            var parent = container.capacity.transform.parent;
            if (!parent) continue;

            var existing = parent.Find(LabelName);
            var label = existing ? existing.GetComponent<TextMeshPro>() : CreateLabel(container, parent);
            if (!label) continue;

            GameListing listing = container.gameListing;
            string code = listing.GameId < -1 && (listing.GameId & 0x3ff) >= GameCode.MaxGameNumber
                ? string.Empty : GameCode.IntToGameName(listing.GameId);
            string platform = GetPlatformName(listing.Platform);
            string details = string.IsNullOrEmpty(platform) ? code : $"{platform} ({code})";
            label.text = RoomBrowserLayout.HostDisplay(listing.TrueHostName, listing.HostName) + "\n" + details;
        }
    }

    private static TextMeshPro CreateLabel(GameContainer container, Transform parent)
    {
        // Match the row's own font/material while placing the extra text over
        // the tag area. The native prefab is not guaranteed to have a child
        // called "Container" as in EHR's layout.
        var label = Object.Instantiate(container.capacity, parent);
        label.name = LabelName;
        var anchor = label.transform.localPosition;
        anchor.y = parent.InverseTransformPoint(container.tag1.transform.position).y;
        label.transform.localPosition = anchor;
        label.rectTransform.sizeDelta = new Vector2(
            Mathf.Max(container.tag1.rectTransform.rect.width, label.rectTransform.rect.width),
            Mathf.Max(container.tag1.rectTransform.rect.height * 2f, label.rectTransform.rect.height));
        label.fontSize = 2.5f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1.5f;
        label.fontSizeMax = 2.5f;
        label.alignment = TextAlignmentOptions.Center;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false;
        var translator = label.GetComponent<TextTranslatorTMP>();
        if (translator) translator.enabled = false;
        return label;
    }

    private static string GetPlatformName(Platforms platform) => platform switch
    {
        (Platforms)112 => "Starlight",
        Platforms.StandaloneSteamPC => "Steam",
        Platforms.StandaloneEpicPC => "Epic Games",
        Platforms.StandaloneWin10 => "Microsoft Store",
        Platforms.StandaloneItch => "Itch.io",
        _ => string.Empty
    };
}
