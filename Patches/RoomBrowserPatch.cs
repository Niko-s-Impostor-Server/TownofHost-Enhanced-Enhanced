using BepInEx.Unity.IL2CPP.Utils.Collections;
using InnerNet;
using System.Collections;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TOHE.Patches;

[HarmonyPatch(typeof(FindAGameManager))]
public static class RoomBrowserPatch
{
    private sealed class Row
    {
        internal GameContainer Container;
        internal TextMeshPro Label;
        internal Renderer[] Renderers;
        internal Collider2D[] Colliders;
        internal bool[] RendererStates;
        internal bool[] ColliderStates;
        internal bool Hidden;

        internal void Show(bool visible)
        {
            if (Hidden == !visible) return;
            if (!visible)
            {
                RendererStates = Renderers.Select(r => r && r.enabled).ToArray();
                ColliderStates = Colliders.Select(c => c && c.enabled).ToArray();
            }
            for (int i = 0; i < Renderers.Length; i++)
                if (Renderers[i]) Renderers[i].enabled = visible && RendererStates[i];
            for (int i = 0; i < Colliders.Length; i++)
                if (Colliders[i]) Colliders[i].enabled = visible && ColliderStates[i];
            Hidden = !visible;
        }
    }

    private sealed class Browser
    {
        internal FindAGameManager Owner;
        internal Scroller Scroller;
        internal Row[] Rows;
        internal int VisibleRows;
        internal float Spacing;
        internal float Limit;
        internal Coroutine Loop;
        internal UiElement LastSelected;
        internal NavigationExtension[] Navigation;
    }

    private sealed record NavigationExtension(PassiveButton Button, PassiveButton FirstAdded, UiElement OriginalDown);

    private static readonly Dictionary<int, Browser> Browsers = new();

    [HarmonyPatch(nameof(FindAGameManager.Start))]
    [HarmonyPrefix]
    public static void BeforeStart(FindAGameManager __instance)
    {
        int id = __instance.GetInstanceID();
        if (Browsers.ContainsKey(id)) return;
        var originals = __instance.gameContainers?.ToArray();
        if (originals == null || originals.Length < 2 || originals.Any(r => !r)) return;
        Transform parent = originals[0].transform.parent;
        if (!parent || originals.Any(r => r.transform.parent != parent)) return;

        var ordered = originals.OrderByDescending(r => r.transform.localPosition.y).ToArray();
        float spacing = ordered[0].transform.localPosition.y - ordered[1].transform.localPosition.y;
        // The target prefab is a single column. Fail closed for an unexpected
        // prefab rather than moving an unrelated or changed native layout.
        if (spacing <= 0.01f || ordered.Any(r => Mathf.Abs(r.transform.localPosition.x - ordered[0].transform.localPosition.x) > 0.01f)
            || originals.Where((r, i) => r != ordered[i]
                || Mathf.Abs(r.transform.localPosition.y - (ordered[0].transform.localPosition.y - i * spacing)) > 0.01f).Any()) return;

        var colliders = originals.SelectMany(r => r.GetComponentsInChildren<Collider2D>(true)).Where(c => c).ToArray();
        Vector3 lower = new(float.PositiveInfinity, float.PositiveInfinity, 0f);
        Vector3 upper = new(float.NegativeInfinity, float.NegativeInfinity, 0f);
        foreach (var collider in colliders)
        {
            var box = collider.TryCast<BoxCollider2D>();
            if (box)
            {
                // Collider.bounds is empty for inactive/disabled prefab rows.
                // Serialized box geometry is available before native Start.
                foreach (float x in new[] { -0.5f, 0.5f })
                foreach (float y in new[] { -0.5f, 0.5f })
                {
                    var point = box.transform.TransformPoint(new Vector3(box.offset.x + box.size.x * x, box.offset.y + box.size.y * y, 0f));
                    point = parent.InverseTransformPoint(point);
                    lower = Vector3.Min(lower, point);
                    upper = Vector3.Max(upper, point);
                }
            }
            else if (collider.bounds.size.x > 0f)
            {
                lower = Vector3.Min(lower, parent.InverseTransformPoint(collider.bounds.min));
                upper = Vector3.Max(upper, parent.InverseTransformPoint(collider.bounds.max));
            }
        }
        if (float.IsInfinity(lower.x) || upper.x <= lower.x || upper.y <= lower.y) return;

        // Native PassiveButtonManager sorts ascending world z. Put the drag
        // surface behind card buttons so it cannot capture their initial click.
        // A counter-offset in Inner keeps every original card's native depth.
        float dragDepth = originals.SelectMany(r => r.GetComponentsInChildren<PassiveButton>(true))
            .Select(b => parent.InverseTransformPoint(b.transform.position).z)
            .Concat(colliders.Select(c => parent.InverseTransformPoint(c.transform.position).z))
            .DefaultIfEmpty(0f).Max() + 0.01f;

        var viewport = new GameObject("TOHE.RoomListViewport");
        viewport.transform.SetParent(parent, false);
        viewport.transform.localPosition = new Vector3(0f, 0f, dragDepth);
        var hitbox = viewport.AddComponent<BoxCollider2D>();
        hitbox.offset = new Vector2((lower.x + upper.x) / 2f, (lower.y + upper.y) / 2f);
        hitbox.size = new Vector2(upper.x - lower.x, upper.y - lower.y);
        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        content.transform.localPosition = new Vector3(0f, 0f, -dragDepth);
        var scroller = viewport.AddComponent<Scroller>();
        scroller.Inner = content.transform;
        scroller.Colliders = new Collider2D[] { hitbox };
        scroller.ClickMask = hitbox;
        scroller.MouseMustBeOverToScroll = true;
        scroller.ScrollWheelSpeed = 0.3f;
        scroller.allowY = true;
        scroller.allowX = false;
        scroller.showY = scroller.showX = false;
        scroller.SetBounds(new FloatRange(0f, 0f), new FloatRange(0f, 0f));

        var containers = originals.ToList();
        foreach (var row in originals) row.transform.SetParent(content.transform, false);
        GameContainer template = ordered[^1];
        PassiveButton[] firstNewButtons = null;
        for (int i = originals.Length; i < RoomBrowserLayout.MinimumRoomRows; i++)
        {
            var row = Object.Instantiate(template, content.transform);
            row.transform.localPosition = template.transform.localPosition - new Vector3(0f, (i - originals.Length + 1) * spacing, 0f);
            // Clone before native Start wires OnMoreAction, so the native loop
            // attaches precisely one More callback to originals and clones.
            row.gameObject.SetActive(false);
            containers.Add(row);
            var newButtons = row.GetComponentsInChildren<PassiveButton>(true).ToArray();
            firstNewButtons ??= newButtons;
            foreach (var button in newButtons)
            {
                // A copied explicit link would point back to the template row.
                button.ControllerNav = new ControllerNavigation { mode = ControllerNavigation.Mode.Automatic };
                if (!__instance.ControllerSelectable.Contains(button)) __instance.ControllerSelectable.Add(button);
            }
        }
        List<NavigationExtension> navigationExtensions = new();
        if (firstNewButtons != null)
        {
            var lastNativeButtons = template.GetComponentsInChildren<PassiveButton>(true).ToArray();
            for (int i = 0; i < Mathf.Min(lastNativeButtons.Length, firstNewButtons.Length); i++)
            {
                var navigation = lastNativeButtons[i].ControllerNav;
                if (navigation.mode != ControllerNavigation.Mode.Explicit) continue;
                if (navigation.selectOnDown && navigation.selectOnDown.transform.IsChildOf(template.transform)) continue;
                // Extend this link only while the added row is active. Native
                // explicit navigation cannot traverse an inactive Automatic row.
                navigationExtensions.Add(new(lastNativeButtons[i], firstNewButtons[i], navigation.selectOnDown));
            }
        }
        __instance.gameContainers = containers.ToArray();

        var state = new Browser
        {
            Owner = __instance,
            Scroller = scroller,
            VisibleRows = originals.Length,
            Spacing = spacing,
            Navigation = navigationExtensions.ToArray(),
            Rows = containers.OrderByDescending(r => r.transform.localPosition.y).Select(r => CreateRow(r, hitbox)).ToArray()
        };
        Browsers[id] = state;
        state.Loop = __instance.StartCoroutine(Observe(state).WrapToIl2Cpp());
    }

    private static Row CreateRow(GameContainer container, Collider2D hitbox)
    {
        TextMeshPro label = null;
        if (container.capacity && container.tag1)
        {
            // Reuse the target's font/material and its capacity-column/tag-row
            // anchors instead of assuming a child called "Container" exists.
            label = Object.Instantiate(container.capacity, container.capacity.transform.parent);
            label.name = "TOHE.HostAndCode";
            var anchor = label.transform.localPosition;
            anchor.y = label.transform.parent.InverseTransformPoint(container.tag1.transform.position).y;
            label.transform.localPosition = anchor;
            label.rectTransform.sizeDelta = new Vector2(Mathf.Max(container.tag1.rectTransform.rect.width, label.rectTransform.rect.width), Mathf.Max(container.tag1.rectTransform.rect.height * 2f, label.rectTransform.rect.height));
            label.fontSize = 2.5f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 1.5f;
            label.fontSizeMax = 2.5f;
            label.alignment = TextAlignmentOptions.Center;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = false;
            label.text = string.Empty;
            var translator = label.GetComponent<TextTranslatorTMP>();
            if (translator) translator.enabled = false;
        }
        foreach (var button in container.GetComponentsInChildren<PassiveButton>(true)) button.ClickMask = hitbox;
        return new Row
        {
            Container = container,
            Label = label,
            Renderers = container.GetComponentsInChildren<Renderer>(true).ToArray(),
            Colliders = container.GetComponentsInChildren<Collider2D>(true).ToArray()
        };
    }

    [HarmonyPatch(nameof(FindAGameManager.HandleList))]
    [HarmonyPrefix]
    public static void BeforeList(FindAGameManager __instance)
    {
        // UDP refreshes do not run native ResetContainers. Clear old rows before
        // either response is displayed, including a smaller or empty result.
        if (!Browsers.TryGetValue(__instance.GetInstanceID(), out var browser)) return;
        foreach (var row in browser.Rows)
        {
            row.Show(true);
            row.Container.gameObject.SetActive(false);
            if (row.Label) row.Label.text = string.Empty;
        }
    }

    [HarmonyPatch(nameof(FindAGameManager.HandleList))]
    [HarmonyPostfix]
    public static void AfterList(FindAGameManager __instance)
    {
        if (!Browsers.TryGetValue(__instance.GetInstanceID(), out var browser)) return;
        int active = 0;
        foreach (var row in browser.Rows)
        {
            if (!row.Container.gameObject.activeSelf) continue;
            active++;
            GameListing listing = row.Container.gameListing;
            if (row.Label)
            {
                // V2's low ten bits can contain an out-of-alphabet value in a
                // malformed response; do not let its display break the menu.
                string code = listing.GameId < -1 && (listing.GameId & 0x3ff) >= GameCode.MaxGameNumber
                    ? string.Empty : GameCode.IntToGameName(listing.GameId);
                row.Label.text = RoomBrowserLayout.HostDisplay(listing.TrueHostName, listing.HostName) + "\n" + code;
            }
        }
        browser.Limit = RoomBrowserLayout.ScrollLimit(active, browser.VisibleRows, browser.Spacing);
        browser.Scroller.SetYBoundsMax(browser.Limit);
        browser.Scroller.ScrollToTop();
        browser.LastSelected = null;
        foreach (var extension in browser.Navigation)
        {
            var navigation = extension.Button.ControllerNav;
            navigation.selectOnDown = extension.FirstAdded.isActiveAndEnabled ? extension.FirstAdded : extension.OriginalDown;
            extension.Button.ControllerNav = navigation;
        }
        UpdateVisibility(browser);
    }

    private static IEnumerator Observe(Browser browser)
    {
        while (browser.Owner && browser.Scroller)
        {
            if (browser.Owner.gameObject.activeInHierarchy)
            {
                var controller = ControllerManager.Instance;
                var selection = controller ? controller.CurrentUiState?.CurrentSelection : null;
                bool joystick = ActiveInputManager.currentControlType == ActiveInputManager.InputType.Joystick;
                if (selection && (selection != browser.LastSelected || joystick) && controller.CurrentUiState.MenuName == browser.Owner.name)
                {
                    int index = System.Array.FindIndex(browser.Rows, r => selection.transform.IsChildOf(r.Container.transform));
                    if (index >= 0 && browser.Rows[index].Container.gameObject.activeSelf)
                    {
                        float current = browser.Scroller.Inner.localPosition.y;
                        float offset = RoomBrowserLayout.RevealOffset(index, browser.VisibleRows, browser.Spacing, current, browser.Limit);
                        browser.Scroller.ScrollRelative(new Vector2(0f, offset - current));
                    }
                }
                browser.LastSelected = selection;
                UpdateVisibility(browser);
            }
            yield return null;
        }
    }

    private static void UpdateVisibility(Browser browser)
    {
        float offset = browser.Scroller.Inner.localPosition.y / browser.Spacing;
        for (int i = 0; i < browser.Rows.Length; i++)
        {
            // Cull complete cards so text/sprites cannot spill over filters or
            // controls. UiElements stay enabled for native controller navigation;
            // only offscreen graphics and mouse colliders are suppressed.
            browser.Rows[i].Show(i >= offset - 0.01f && i <= offset + browser.VisibleRows - 1 + 0.01f);
        }
    }

    [HarmonyPatch(nameof(FindAGameManager.OnDestroy))]
    [HarmonyPrefix]
    public static void Release(FindAGameManager __instance)
    {
        if (!Browsers.Remove(__instance.GetInstanceID(), out var browser)) return;
        if (browser.Loop != null) __instance.StopCoroutine(browser.Loop);
        // The viewport, clones and labels are children of the native scene UI;
        // Unity owns their destruction together with their parent page.
    }
}
