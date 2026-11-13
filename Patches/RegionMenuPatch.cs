using UnityEngine;

namespace TOHE.Patches;

[HarmonyPatch(typeof(RegionMenu))]
public static class RegionMenuPatch
{
    [HarmonyPatch(nameof(RegionMenu.OnEnable))]
    [HarmonyPostfix]
    public static void AdjustButtonPositions_Postfix(RegionMenu __instance)
    {
        const int maxColumns = 4;
        int buttonsPerColumn = 6;
        float buttonSpacing = 0.6f;
        float buttonSpacingSide = 2.25f;

        List<UiElement> buttons = __instance.controllerSelectable.ToArray().ToList();

        int columnCount = (buttons.Count + buttonsPerColumn - 1) / buttonsPerColumn;

        while (columnCount > maxColumns)
        {
            buttonsPerColumn++;
            columnCount = (buttons.Count + buttonsPerColumn - 1) / buttonsPerColumn;
        }

        float totalWidth = (columnCount - 1) * buttonSpacingSide;
        float totalHeight = (buttonsPerColumn - 1) * buttonSpacing;

        Vector3 startPosition = new Vector3(-totalWidth / 2, totalHeight / 2, 0f);

        for (int i = 0; i < buttons.Count; i++)
        {
            int col = i / buttonsPerColumn;
            int row = i % buttonsPerColumn;
            buttons[i].transform.localPosition = startPosition + new Vector3(col * buttonSpacingSide, -row * buttonSpacing, 0f);
        }
    }
}

#if !ANDROID
// Ported from Starlight AuthFix's ServerDropdownPatch.
[HarmonyPatch(typeof(ServerDropdown), nameof(ServerDropdown.FillServerOptions))]
public static class ServerDropdownLayoutPatch
{
    [HarmonyPrepare]
    private static bool Prepare(System.Reflection.MethodBase original)
    {
        // Check before this class installs its own patch.
        return original != null || Harmony.GetPatchInfo(AccessTools.Method(typeof(ServerDropdown), nameof(ServerDropdown.FillServerOptions)))?.Owners.Any() != true;
    }

    private static bool RegionEquals(IRegionInfo region, IRegionInfo other)
    {
        return region.Name == other.Name && region.TranslateName == other.TranslateName &&
               region.PingServer == other.PingServer && region.TargetServer == other.TargetServer &&
               region.Servers.All(server => other.Servers.Any(candidate => candidate.Equals(server)));
    }

    [HarmonyPrefix]
    private static bool Prefix(ServerDropdown __instance)
    {
        var serverManager = DestroyableSingleton<ServerManager>.Instance;
        if (serverManager.AvailableRegions.Count <= 3) return true;

        int index = 0;
        __instance.background.size = new Vector2(8.4f, 4.8f);
        foreach (var region in serverManager.AvailableRegions)
        {
            bool isFindGame = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "FindAGame";
            if (RegionEquals(serverManager.CurrentRegion, region))
            {
                __instance.defaultButtonSelected = __instance.firstOption;
                __instance.firstOption.ChangeButtonText(DestroyableSingleton<TranslationController>.Instance.GetStringWithDefault(region.TranslateName, region.Name));
                continue;
            }

            var button = __instance.ButtonPool.Get<ServerListButton>();
            int x = index % 2 == 0 ? -2 : 2;
            if (isFindGame) x += 2;
            button.transform.localPosition = new Vector3(x, __instance.y_posButton - 0.55f * (index / 2f), -1f);
            button.transform.localScale = Vector3.one;
            button.Text.text = DestroyableSingleton<TranslationController>.Instance.GetStringWithDefault(region.TranslateName, region.Name);
            button.Text.ForceMeshUpdate(false, false);
            button.Button.OnClick.RemoveAllListeners();
            button.Button.OnClick.AddListener((UnityEngine.Events.UnityAction)(System.Action)(() => __instance.ChooseOption(region)));
            __instance.controllerSelectable.Add(button.Button);
            __instance.background.transform.localPosition = new Vector3(isFindGame ? 2f : 0f, __instance.initialYPos - 0.3f * (index / 2f), 0f);
            __instance.background.size = new Vector2(__instance.background.size.x, 1.2f + 0.6f * (index / 2f));
            index++;
        }
        return false;
    }
}
#endif
