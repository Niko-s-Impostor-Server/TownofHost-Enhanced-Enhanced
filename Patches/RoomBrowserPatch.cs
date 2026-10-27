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
    private const float MapLabelGap = 0.08f;
    private const float MaxMapLabelWidth = 1.8f;

    [HarmonyPostfix]
    private static void AfterList(FindAGameManager __instance)
    {
        foreach (var container in __instance.gameContainers)
        {
            if (!container || !container.gameObject.activeSelf || !container.capacity || !container.tag1) continue;

            var mapLogo = container.transform.Find("Container/MapLogo");
            var mapRenderer = mapLogo ? mapLogo.GetComponent<Renderer>() : null;
            var tagRenderer = container.tag1.transform.parent.GetComponent<Renderer>();
            if (!mapLogo || !mapRenderer || !tagRenderer) continue;

            var parent = mapLogo.parent;
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

            Bounds mapBounds = mapRenderer.bounds;
            Bounds tagBounds = tagRenderer.bounds;
            float labelLeft = mapBounds.max.x + MapLabelGap;
            float labelRight = tagBounds.min.x - MapLabelGap;
            float width = Mathf.Min(MaxMapLabelWidth, labelRight - labelLeft);
            if (width <= 0f)
            {
                label.gameObject.SetActive(false);
                continue;
            }

            label.gameObject.SetActive(true);
            Vector2 size = label.rectTransform.sizeDelta;
            size.x = width / Mathf.Abs(label.transform.lossyScale.x);
            label.rectTransform.sizeDelta = size;
            label.transform.position = new Vector3(
                labelLeft + width * 0.5f,
                mapLogo.position.y,
                container.capacity.transform.position.z);
        }
    }

    private static TextMeshPro CreateLabel(GameContainer container, Transform parent)
    {
        // Clone the native TMP label so the row keeps its font and material.
        var label = Object.Instantiate(container.capacity, parent);
        label.name = LabelName;
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
