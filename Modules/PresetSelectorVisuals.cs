using UnityEngine;

namespace TOHE;

// Small, reusable nine-slice surfaces drawn once in memory. Corners and borders
// stay the same size when the preset row stretches to fit the settings sidebar.
internal static class PresetSelectorVisuals
{
    private static Sprite panel, normal, hover, line;

    public static Sprite Panel => panel ? panel : panel = DrawSurface(
        new Color32(27, 37, 42, 250), new Color32(62, 83, 89, 255));

    public static void StyleArrow(PassiveButton button, bool plus, float size)
    {
        if (!normal) normal = DrawSurface(new Color32(43, 65, 71, 255), new Color32(76, 107, 114, 255));
        if (!hover) hover = DrawSurface(new Color32(57, 91, 98, 255), new Color32(139, 202, 204, 255));
        if (!line)
        {
            line = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
            line.hideFlags = HideFlags.HideAndDontSave;
        }

        // Remove the cloned tab's visual layout from the equation entirely.
        foreach (var renderer in button.GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = false;
        foreach (var label in button.GetComponentsInChildren<TMPro.TextMeshPro>(true)) label.gameObject.SetActive(false);
        button.buttonText = null;
        button.inactiveSprites = State("Normal", normal);
        button.activeSprites = State("Hover", hover);
        button.selectedSprites = State("Selected", hover);
        button.selectedInactiveSprites = State("SelectedIdle", hover);
        button.disabledSprites = State("Disabled", normal);
        button.onClickSprites = State("Pressed", hover);
        button.SetPassiveButtonHoverStateInactive();

        // Both symbols share exactly the same center, stroke and arm length.
        Stroke("Horizontal", new Vector3(0.14f, 0.022f, 1f));
        if (plus) Stroke("Vertical", new Vector3(0.022f, 0.14f, 1f));

        GameObject State(string name, Sprite sprite)
        {
            var obj = new GameObject(name) { layer = button.gameObject.layer };
            obj.transform.SetParent(button.transform, false);
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = new Vector2(size, size);
            obj.SetActive(false);
            return obj;
        }

        void Stroke(string name, Vector3 scale)
        {
            var obj = new GameObject(name) { layer = button.gameObject.layer };
            obj.transform.SetParent(button.transform, false);
            obj.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            obj.transform.localScale = scale;
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = line;
            renderer.color = new Color32(221, 243, 241, 255);
        }
    }

    private static Sprite DrawSurface(Color fill, Color border)
    {
        const int resolution = 64;
        const float radius = 7f;
        var pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float dx = Mathf.Abs(x + 0.5f - resolution / 2f) - (resolution / 2f - 1f - radius);
            float dy = Mathf.Abs(y + 0.5f - resolution / 2f) - (resolution / 2f - 1f - radius);
            float distance = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude
                + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
            var color = Color.Lerp(border, fill, Mathf.Clamp01(-distance - 0.75f));
            color.a *= Mathf.Clamp01(0.5f - distance);
            pixels[y * resolution + x] = color;
        }
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, resolution, resolution),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
