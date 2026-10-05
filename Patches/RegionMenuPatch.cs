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

[HarmonyPatch(typeof(ServerDropdown))]
public static class ServerDropdownLayoutPatch
{
    private sealed record OriginalGeometry(Vector2 Size, Vector3 BackgroundPosition, Vector3 Position, Vector3 Scale);
    private static readonly Dictionary<int, OriginalGeometry> Geometry = new();

    [HarmonyPatch(nameof(ServerDropdown.FillServerOptions))]
    [HarmonyPrefix]
    public static void BeforeFill(ServerDropdown __instance)
    {
        int id = __instance.GetInstanceID();
        if (!Geometry.ContainsKey(id))
            Geometry[id] = new(__instance.background.size, __instance.background.transform.localPosition, __instance.transform.localPosition, __instance.transform.localScale);

        // Native OnDisable reclaims the pool, but FillServerOptions never clears
        // its navigation list. A repeated open must not retain pooled buttons.
        __instance.controllerSelectable.Clear();
        __instance.defaultButtonSelected = null;
    }

    [HarmonyPatch(nameof(ServerDropdown.FillServerOptions))]
    [HarmonyPostfix]
    public static void AfterFill(ServerDropdown __instance)
    {
        var buttons = __instance.controllerSelectable.ToArray();
        int rows = RoomBrowserLayout.RowsPerColumn(buttons.Length);
        int columns = Mathf.Max(1, Mathf.CeilToInt(buttons.Length / (float)rows));
        const float columnSpacing = 4.15f;
        for (int i = 0; i < buttons.Length; i++)
        {
            // Keep native pool, order, texts, listeners, selection and z depth.
            Vector3 position = buttons[i].transform.localPosition;
            position.x = (i / rows - (columns - 1) / 2f) * columnSpacing;
            position.y = __instance.y_posButton - i % rows * 0.55f;
            buttons[i].transform.localPosition = position;
        }

        var original = Geometry[__instance.GetInstanceID()];
        int usedRows = Mathf.Min(rows, buttons.Length);
        __instance.background.size = new(original.Size.x + (columns - 1) * columnSpacing, 1.2f + 0.6f * Mathf.Max(0, usedRows - 1));
        Vector3 backgroundPosition = original.BackgroundPosition;
        backgroundPosition.y = __instance.initialYPos - 0.3f * Mathf.Max(0, usedRows - 1);
        __instance.background.transform.localPosition = backgroundPosition;

        // Dropdowns have different parent anchors in CreateGame and FindAGame.
        // Fit the expanded background to the current orthographic camera.
        Camera camera = Camera.main;
        if (camera && camera.orthographic)
        {
            Bounds bounds = __instance.background.bounds;
            float halfWidth = camera.orthographicSize * camera.aspect - 0.1f;
            float halfHeight = camera.orthographicSize - 0.1f;
            float fitScale = Mathf.Min(1f, halfWidth * 2f / Mathf.Max(0.01f, bounds.size.x), halfHeight * 2f / Mathf.Max(0.01f, bounds.size.y));
            __instance.transform.localScale *= fitScale;
            bounds = __instance.background.bounds;
            float left = camera.transform.position.x - halfWidth;
            float right = camera.transform.position.x + halfWidth;
            float bottom = camera.transform.position.y - halfHeight;
            float top = camera.transform.position.y + halfHeight;
            float shiftX = bounds.min.x < left ? left - bounds.min.x : bounds.max.x > right ? right - bounds.max.x : 0f;
            float shiftY = bounds.min.y < bottom ? bottom - bounds.min.y : bounds.max.y > top ? top - bounds.max.y : 0f;
            __instance.transform.position += new Vector3(shiftX, shiftY, 0f);
        }
        if (!__instance.defaultButtonSelected && buttons.Length > 0)
            __instance.defaultButtonSelected = buttons[0];
    }

    [HarmonyPatch(nameof(ServerDropdown.OnDisable))]
    [HarmonyPostfix]
    public static void RestoreGeometry(ServerDropdown __instance)
    {
        if (!Geometry.Remove(__instance.GetInstanceID(), out var original)) return;
        __instance.background.size = original.Size;
        __instance.background.transform.localPosition = original.BackgroundPosition;
        __instance.transform.localPosition = original.Position;
        __instance.transform.localScale = original.Scale;
        __instance.controllerSelectable.Clear();
        __instance.defaultButtonSelected = null;
    }
}
