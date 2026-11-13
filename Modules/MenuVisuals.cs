using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace TOHE;

// Shared primitives for the custom sound and HUD toolbox overlays.
internal sealed class MenuVisuals(TextMeshPro labelTemplate)
{
    private static Sprite white, nativeCheckmark;
    internal static Sprite White => white ? white : white = Sprite.Create(Texture2D.whiteTexture,
        new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);

    internal SpriteRenderer Surface(Transform parent, string name, Vector3 position, Vector2 size, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        var renderer = obj.AddComponent<SpriteRenderer>();
        renderer.sprite = White;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = size;
        renderer.color = color;
        return renderer;
    }

    internal TextMeshPro Label(Transform parent, string name, string text, Vector3 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        var label = Object.Instantiate(labelTemplate, parent);
        label.name = name;
        label.DestroyTranslator();
        var anchor = label.GetComponent<AspectPosition>();
        if (anchor != null)
        {
            anchor.enabled = false;
            Object.Destroy(anchor);
        }
        label.transform.localPosition = position;
        label.transform.localScale = Vector3.one;
        label.rectTransform.sizeDelta = size;
        label.margin = Vector4.zero;
        label.alignment = alignment;
        label.enableAutoSizing = true;
        label.fontSize = label.fontSizeMax = fontSize;
        label.fontSizeMin = 1f;
        label.color = Color.white;
        label.text = text;
        label.gameObject.SetActive(true);
        return label;
    }

    internal PassiveButton Button(Transform parent, string name, Vector3 position, Vector2 size, string caption, Action action)
    {
        var obj = new GameObject(name);
        obj.SetActive(false);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        var renderer = obj.AddComponent<SpriteRenderer>();
        renderer.sprite = White;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = size;
        renderer.color = new Color32(65, 91, 96, 255);
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.size = size;
        var button = obj.AddComponent<PassiveButton>();
        button.OnMouseOver = new();
        button.OnMouseOut = new();
        button.OnClick = new();
        button.OnClick.AddListener((UnityAction)(() => action()));
        Label(obj.transform, "Label", caption, new Vector3(0f, 0f, -0.2f), size, 1.8f, TextAlignmentOptions.Center);
        obj.SetActive(true);
        return button;
    }

    internal PassiveButton Checkbox(Transform parent, string name, Vector3 position, bool value, bool enabled, Action onClick)
    {
        var button = Button(parent, name, position, new Vector2(0.43f, 0.43f), "", onClick);
        var outline = button.GetComponent<SpriteRenderer>();
        outline.color = enabled ? new Color32(145, 177, 178, 255) : new Color32(91, 98, 100, 255);
        outline.sortingOrder = 3;
        var box = Surface(button.transform, "Box", new Vector3(0f, 0f, -0.05f), new Vector2(0.35f, 0.35f), new Color32(29, 43, 47, 255));
        box.sortingOrder = 4;
        if (value)
        {
            if (!nativeCheckmark)
                nativeCheckmark = Il2CppHelper.FindObjectsOfTypeAll<ToggleOption>().FirstOrDefault(x => x.CheckMark != null && x.CheckMark.sprite != null)?.CheckMark.sprite;
            if (nativeCheckmark)
            {
                var mark = new GameObject("Checkmark");
                mark.transform.SetParent(button.transform, false);
                mark.transform.localPosition = new Vector3(0f, 0f, -0.1f);
                var bounds = nativeCheckmark.bounds.size;
                mark.transform.localScale = new Vector3(0.31f / bounds.x, 0.31f / bounds.y, 1f);
                var renderer = mark.AddComponent<SpriteRenderer>();
                renderer.sprite = nativeCheckmark;
                renderer.sortingOrder = 5;
            }
            else Label(button.transform, "Checkmark", "✓", new Vector3(0f, 0f, -0.1f), new Vector2(0.38f, 0.38f), 1.5f, TextAlignmentOptions.Center).sortingOrder = 6;
        }
        if (!enabled) button.GetComponent<BoxCollider2D>().enabled = false;
        foreach (var item in button.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = parent.gameObject.layer;
        return button;
    }
}
