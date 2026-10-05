using TMPro;
using UnityEngine;

namespace TOHE;

// Owned by the current HUD/lobby pair. Use the existing native HUD update,
// rather than registering another managed MonoBehaviour in IL2CPP.
public static class LobbySettingsPreview
{
    public static TextMeshPro Text { get; private set; }
    private static HudManager owner;
    private static LobbyBehaviour lobby;
    private static Camera layoutCamera;
    private static Rect layoutSafeArea, layoutViewport;
    private static bool layoutReady;
    private static int layoutLanguage;
    private static bool layoutOwnLanguage;

    public static void Update(HudManager hud)
    {
        if (!hud || !AmongUsClient.Instance || !GameStates.IsLobby || !GameStates.IsModHost ||
            !LobbyBehaviour.Instance || !PlayerControl.LocalPlayer || !Options.IsLoaded || Options.HideGameSettings == null)
        {
            Dispose();
            return;
        }
        if (owner != hud || lobby != LobbyBehaviour.Instance)
        {
            Dispose();
            owner = hud;
            lobby = LobbyBehaviour.Instance;
        }
        if (!CanShow(hud))
        {
            if (Text && Text.gameObject.activeSelf) Text.gameObject.SetActive(false);
            return;
        }
        if (!Text)
        {
            var source = hud.AbilityButton ? hud.AbilityButton.cooldownTimerText : null;
            if (!source) return;
            Text = Object.Instantiate(source, hud.transform);
            Text.name = "TOHEELobbySettingsPreview";
            Text.text = string.Empty;
            Text.enabled = true;
            Text.DestroyTranslator();
            Text.transform.localScale = Vector3.one;
            Text.color = Color.white;
            Text.outlineColor = Color.black;
            Text.outlineWidth = 0.15f;
            Text.alignment = TextAlignmentOptions.TopLeft;
            Text.rectTransform.pivot = new Vector2(0f, 1f);
            Text.margin = Vector4.zero;
            Text.enableWordWrapping = true;
            Text.enableAutoSizing = false;
            Text.fontSize = Text.fontSizeMax = 1.05f;
            Text.fontSizeMin = 0.75f;
            Text.autoSizeTextContainer = false;
            Text.overflowMode = TextOverflowModes.Overflow;
            Text.richText = true;
            layoutReady = false;
        }
        if (!Text.gameObject.activeSelf) Text.gameObject.SetActive(true);
        Position(hud);
        OptionShower.GetTextNoFresh();
        if (Application.isFocused)
        {
            if (Input.GetKeyDown(KeyCode.Tab)) OptionShower.Next();
            for (int i = 0; i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) OptionShower.SelectPage(i);
        }
        var value = OptionShower.GetTextNoFresh();
        if (Text.text != value) Text.text = value;
    }

    private static bool CanShow(HudManager hud)
    {
        if (!hud.gameObject.activeInHierarchy || !lobby.gameObject.activeInHierarchy ||
            hud.IsIntroDisplayed || (hud.Chat && (!hud.Chat.IsClosedOrClosing || hud.Chat.IsAnimating)) ||
            (hud.GameMenu && hud.GameMenu.gameObject.activeInHierarchy) ||
            (GameSettingMenu.Instance && GameSettingMenu.Instance.gameObject.activeInHierarchy)) return false;
        var controller = ControllerManager.Instance;
        // Scene navigation itself does not obscure the lobby; every overlay does.
        var state = controller ? controller.CurrentUiState : null;
        return state == null || state.IsScene || string.IsNullOrEmpty(state.MenuName);
    }

    private static void Position(HudManager hud)
    {
        var camera = hud.UICamera;
        if (!camera) return;
        var safe = Screen.safeArea;
        var viewport = camera.pixelRect;
        int language = TranslationController.InstanceExists ? (int)TranslationController.Instance.currentLanguage.languageID : -1;
        bool ownLanguage = Main.ForceOwnLanguage.Value;
        if (layoutReady && camera == layoutCamera && safe == layoutSafeArea && viewport == layoutViewport &&
            layoutLanguage == language && layoutOwnLanguage == ownLanguage) return;
        layoutReady = true;
        layoutCamera = camera;
        layoutSafeArea = safe;
        layoutViewport = viewport;
        layoutLanguage = language;
        layoutOwnLanguage = ownLanguage;
        float left = Mathf.Max(safe.xMin, viewport.xMin), right = Mathf.Min(safe.xMax, viewport.xMax);
        float bottom = Mathf.Max(safe.yMin, viewport.yMin), top = Mathf.Min(safe.yMax, viewport.yMax);
        if (right <= left || top <= bottom) return;
        // Leave the room-code/start row below and the native toolbar/status area
        // on the right. Pixel-safe-area positioning also supports narrow windows.
        float depth = camera.WorldToScreenPoint(hud.transform.position).z;
        var lower = camera.ScreenToWorldPoint(new Vector3(left, bottom, depth));
        var upper = camera.ScreenToWorldPoint(new Vector3(right, top, depth));
        float width = upper.x - lower.x, height = upper.y - lower.y;
        // Keep the preview against the visible screen edge at every resolution;
        // world-unit padding grows noticeably on narrower windows.
        var anchor = camera.ScreenToWorldPoint(new Vector3(left + 4f, top - 4f, depth));
        Text.transform.position = new Vector3(anchor.x, anchor.y, hud.transform.position.z - 0.1f);
        Text.rectTransform.sizeDelta = new Vector2(Mathf.Min(5.8f, width * 0.58f), Mathf.Max(1f, height - 1.4f));
        // TMP measures wrapping at the final fixed font size. Reserve the footer
        // before splitting pages so narrow windows cannot truncate its shortcut.
        string footer = "\n\n" + Translator.GetString("PressTabToNextPage") + " (999/999)";
        var size = Text.rectTransform.sizeDelta;
        OptionShower.ConfigurePagination(value => Text &&
            Text.GetPreferredValues(value + footer, size.x, float.PositiveInfinity).y <= size.y);
    }

    public static void Dispose()
    {
        if (!Text && !owner && !lobby) return;
        if (Text)
        {
            Text.gameObject.SetActive(false);
            Object.Destroy(Text.gameObject);
        }
        Text = null;
        owner = null;
        lobby = null;
        layoutReady = false;
        OptionShower.Reset();
    }

    internal static void Dispose(HudManager hud)
    {
        if (owner == hud) Dispose();
    }

    internal static void Dispose(LobbyBehaviour currentLobby)
    {
        if (lobby == currentLobby) Dispose();
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
internal static class LobbySettingsPreviewUpdatePatch
{
    public static void Postfix(HudManager __instance) => LobbySettingsPreview.Update(__instance);
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.OnDestroy))]
internal static class LobbySettingsPreviewHudDestroyPatch
{
    public static void Prefix(HudManager __instance) => LobbySettingsPreview.Dispose(__instance);
}

[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.OnDestroy))]
internal static class LobbySettingsPreviewLobbyDestroyPatch
{
    public static void Prefix(LobbyBehaviour __instance) => LobbySettingsPreview.Dispose(__instance);
}
