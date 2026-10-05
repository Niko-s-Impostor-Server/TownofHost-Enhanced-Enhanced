using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InnerNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace TOHEE.RuntimeSmoke;

// Fixed native menu observation. Never selects a server or confirms a game.
internal sealed class ServerDropdownSmoke(string directory)
{
    private readonly List<object> checks = new();
    private MainMenuManager? menu;
    private CreateGameOptions? owner;
    private ServerDropdown? dropdown;
    private IntPtr clientPointer;
    private string? originalRegion;
    private long errorsAtStart;
    private double started;
    private double stageStarted;
    private int stage;
    private int frames;
    private int firstCount;
    private Vector2 originalSize;
    private Vector3 originalBackgroundPosition;
    private Vector3 originalPosition;
    private Vector3 originalScale;
    private Vector3[] firstPositions = [];
    private bool geometrySaved;
    private bool ownsCreation;
    private bool cleanupRequested;
    internal bool Busy { get; private set; }
    internal string Outcome { get; private set; } = "idle";
    internal string Detail { get; private set; } = "";

    internal bool Begin(double now, out string rejection)
    {
        rejection = "";
        if (Busy) { rejection = "server_ui_operation_busy"; return false; }
        if (Constants.GetPlatformType() != Platforms.StandaloneItch || !DisconnectedMenu())
        { rejection = "requires_disconnected_itch_main_menu"; return false; }
        menu = Unique<MainMenuManager>();
        owner = Unique<CreateGameOptions>();
        if (!menu || owner != null && owner && owner.gameObject.activeInHierarchy)
        { rejection = "requires_unique_main_menu_with_creation_ui_closed"; return false; }
        if (!DestroyableSingleton<ServerManager>.InstanceExists || Camera.main == null)
        { rejection = "server_manager_or_camera_unavailable"; return false; }
        checks.Clear();
        dropdown = null;
        clientPointer = AmongUsClient.Instance.Pointer;
        originalRegion = DestroyableSingleton<ServerManager>.Instance.CurrentRegion?.Name;
        errorsAtStart = SmokeErrorCounter.Count;
        started = stageStarted = now;
        stage = frames = firstCount = 0;
        geometrySaved = ownsCreation = cleanupRequested = false;
        firstPositions = [];
        Busy = true;
        Outcome = "running";
        Detail = "fixed_native_dropdown_observation";
        return true;
    }

    internal bool Tick(double now)
    {
        if (!Busy) return true;
        try
        {
            if (now - started >= 8)
                return Finish("timed_out", stage < 3 ? "native_creation_ui_unavailable_or_permission_gate" : "eight_second_server_ui_timeout_no_retry");
            if (!OwnsDisconnectedContext()) return Finish("failed", "disconnected_menu_or_original_region_changed");
            if (SmokeErrorCounter.Count != errorsAtStart) return Finish("failed", "logged_error_during_server_ui");
            if (stage == 0)
            {
                menu!.OpenOnlineMenu();
                Advance(1, now);
            }
            else if (stage == 1 && now - stageStarted >= 0.5)
            {
                // Opening this native settings page does not confirm or request a room.
                menu!.OpenCreateGame();
                ownsCreation = true;
                Advance(2, now);
            }
            else if (stage == 2)
            {
                owner = Unique<CreateGameOptions>();
                if (!CreationReady()) return false;
                dropdown = owner!.serverDropdown;
                if (!dropdown || dropdown.gameObject.scene.name != "MainMenu" || dropdown.gameObject.activeInHierarchy || !dropdown.background)
                    return Finish("failed", "native_dropdown_missing_or_already_open");
                originalSize = dropdown.background.size;
                originalBackgroundPosition = dropdown.background.transform.localPosition;
                originalPosition = dropdown.transform.localPosition;
                originalScale = dropdown.transform.localScale;
                geometrySaved = true;
                owner.OpenServerDropdown();
                Advance(3, now);
            }
            else if (stage is 3 or 5)
            {
                if (++frames < 2) return false;
                if (!InspectDropdown(stage == 5)) return Finish("failed", Detail);
                if (stage == 3)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(directory, "server-dropdown.png"));
                    Advance(7, now);
                    return false;
                }
                dropdown!.Close();
                if (stage == 3) Advance(4, now);
                else
                {
                    owner!.Close();
                    cleanupRequested = true;
                    Advance(6, now);
                }
            }
            else if (stage == 7)
            {
                // Native capture runs at end of frame; keep the dropdown open
                // until that frame has rendered before beginning close.
                if (++frames < 2) return false;
                dropdown!.Close();
                Advance(4, now);
            }
            else if (stage == 4)
            {
                if (++frames < 2) return false;
                if (!CheckClosedGeometry()) return Finish("failed", "native_dropdown_close_did_not_release_geometry_and_navigation");
                checks.Add(new { check = "first_close_restores_geometry_and_navigation", passed = true });
                owner!.OpenServerDropdown();
                Advance(5, now);
            }
            else if (stage == 6 && owner != null && owner && !owner.gameObject.activeInHierarchy)
            {
                if (!CheckClosedGeometry()) return Finish("failed", "reopened_dropdown_close_did_not_release_geometry_and_navigation");
                checks.Add(new { check = "native_creation_close_and_original_region_preserved", passed = true });
                return Finish("succeeded", "native_dropdown_reopen_layout_navigation_and_cleanup_observed");
            }
            return false;
        }
        catch (Exception ex) { return Finish("failed", "native_server_ui_" + ex.GetType().Name); }
    }

    private bool InspectDropdown(bool reopened)
    {
        if (!CreationReady() || dropdown == null || !dropdown || !dropdown.gameObject.activeInHierarchy || !dropdown.background ||
            !dropdown.background.enabled || dropdown.serverSetAction == null || dropdown.closeAction == null || !dropdown.firstOption)
            return CheckFailure("native_dropdown_not_initialized");
        if (!dropdown.firstOption.enabled || !dropdown.firstOption.gameObject.activeInHierarchy)
            return CheckFailure("native_dropdown_current_option_inactive");
        foreach (var renderer in dropdown.firstOption.GetComponentsInChildren<Renderer>())
            if (renderer.enabled && (!Finite(renderer.bounds) || !Visible(renderer.bounds)))
                return CheckFailure("native_dropdown_current_option_outside_camera");
        var controller = ControllerManager.Instance;
        if (!controller || controller.CurrentUiState == null || !dropdown.BackButton ||
            controller.CurrentUiState.BackButton != dropdown.BackButton || !dropdown.defaultButtonSelected ||
            !controller.CurrentUiState.SelectableUiElements.Contains(dropdown.defaultButtonSelected))
            return CheckFailure("native_dropdown_controller_overlay_missing");
        var elements = dropdown.controllerSelectable.ToArray();
        var ids = new HashSet<int>();
        var bounds = new List<Bounds>();
        var visibleBackgrounds = new List<Bounds>();
        var positions = new List<Vector3>();
        var columns = new List<float>();
        int backgroundAbsent = 0, backgroundDisabled = 0;
        int available = DestroyableSingleton<ServerManager>.Instance.AvailableRegions.Length;
        if (elements.Length == 0 || elements.Length > available) return CheckFailure("native_dropdown_alternative_count_invalid");
        foreach (var element in elements)
        {
            if (!element || !element.enabled || !element.gameObject.activeInHierarchy || !ids.Add(element.GetInstanceID()) ||
                !controller.CurrentUiState.SelectableUiElements.Contains(element))
                return CheckFailure("native_dropdown_stale_duplicate_or_unregistered_button");
            var button = element.GetComponentInParent<ServerListButton>();
            checks.Add(new { check = "native_pooled_button_structure", index = positions.Count,
                pooled_component_present = (bool)button,
                button_reference_matches_navigation = button && button.Button == element,
                text_present = button && (bool)button.Text,
                text_enabled = button && button.Text && button.Text.enabled,
                text_active = button && button.Text && button.Text.gameObject.activeInHierarchy,
                optional_background_present = button && (bool)button.Background,
                optional_background_enabled = button && button.Background && button.Background.enabled });
            // Native SetSelected checks Background only when one exists. FillServerOptions
            // guarantees Button/Text but does not require or enable that optional renderer.
            if (!button || button.Button != element || !button.Text || !button.Text.enabled || !button.Text.gameObject.activeInHierarchy)
                return CheckFailure("native_dropdown_pooled_button_structure_invalid");
            bool collider = false;
            Bounds area = default;
            foreach (var item in element.GetComponentsInChildren<Collider2D>())
            {
                if (!item.enabled || !item.gameObject.activeInHierarchy) continue;
                if (!Finite(item.bounds) || !Visible(item.bounds))
                    return CheckFailure("native_dropdown_button_collider_outside_camera");
                if (!collider) area = item.bounds;
                else area.Encapsulate(item.bounds);
                collider = true;
            }
            if (!collider) return CheckFailure("native_dropdown_button_without_active_collider");
            if (!Finite(button.Text.bounds) || !Visible(button.Text.bounds))
                return CheckFailure("native_dropdown_text_outside_camera");
            if (!button.Background) backgroundAbsent++;
            else if (!button.Background.enabled) backgroundDisabled++;
            else
            {
                Bounds visualArea = button.Background.bounds;
                if (!Finite(visualArea) || !Visible(visualArea))
                    return CheckFailure("native_dropdown_enabled_button_background_outside_camera");
                if (visibleBackgrounds.Any(previous => Overlap(previous, visualArea)))
                    return CheckFailure("native_dropdown_enabled_button_backgrounds_overlap");
                visibleBackgrounds.Add(visualArea);
            }
            if (bounds.Any(previous => Overlap(previous, area))) return CheckFailure("native_dropdown_button_hit_areas_overlap");
            bounds.Add(area);
            Vector3 position = element.transform.localPosition;
            if (!Finite(position)) return CheckFailure("native_dropdown_nonfinite_button_position");
            positions.Add(position);
            if (!columns.Any(x => Math.Abs(x - position.x) < 0.01f)) columns.Add(position.x);
        }
        if (columns.Count is < 1 or > 4 || elements.Length > 6 && columns.Count < 2)
            return CheckFailure("native_dropdown_column_count_invalid");
        if (!Finite(dropdown.background.bounds) || !Visible(dropdown.background.bounds))
            return CheckFailure("native_dropdown_background_outside_camera");
        if (reopened && (elements.Length != firstCount || positions.Count != firstPositions.Length ||
            positions.Where((position, index) => Vector3.Distance(position, firstPositions[index]) > 0.001f).Any()))
            return CheckFailure("native_dropdown_reopen_count_or_positions_changed");
        if (!reopened) { firstCount = elements.Length; firstPositions = positions.ToArray(); }
        checks.Add(new { check = reopened ? "reopened_native_dropdown_layout" : "first_native_dropdown_layout", passed = true,
            alternative_count = elements.Length, available_region_count = available, columns = columns.Count,
            unique_active_registered_buttons = ids.Count, callbacks_initialized = true,
            optional_background_absent_count = backgroundAbsent, optional_background_disabled_count = backgroundDisabled,
            active_collider_bounds_checked = true,
            background_width = dropdown.background.bounds.size.x, background_height = dropdown.background.bounds.size.y });
        return true;
    }

    private bool CheckClosedGeometry() => dropdown != null && dropdown && !dropdown.gameObject.activeInHierarchy && geometrySaved &&
        dropdown.controllerSelectable.Count == 0 && !dropdown.defaultButtonSelected &&
        Vector2.Distance(dropdown.background.size, originalSize) < 0.001f &&
        Vector3.Distance(dropdown.background.transform.localPosition, originalBackgroundPosition) < 0.001f &&
        Vector3.Distance(dropdown.transform.localPosition, originalPosition) < 0.001f &&
        Vector3.Distance(dropdown.transform.localScale, originalScale) < 0.001f;

    private bool CreationReady() => owner != null && owner && owner.gameObject.activeInHierarchy && ControllerManager.Instance &&
        ControllerManager.Instance.CurrentUiState != null &&
        (ControllerManager.Instance.CurrentUiState.MenuName == owner.name || dropdown != null && dropdown && dropdown.gameObject.activeInHierarchy);
    private static bool DisconnectedMenu() => AmongUsClient.Instance && !AmongUsClient.Instance.AmConnected &&
        AmongUsClient.Instance.GameState == InnerNetClient.GameStates.NotJoined && SceneManager.GetActiveScene().name == "MainMenu";
    private bool OwnsDisconnectedContext() => DisconnectedMenu() && AmongUsClient.Instance.Pointer == clientPointer &&
        DestroyableSingleton<ServerManager>.InstanceExists &&
        DestroyableSingleton<ServerManager>.Instance.CurrentRegion?.Name == originalRegion;
    private static T? Unique<T>() where T : Component
    {
        T? found = null;
        foreach (var candidate in UObject.FindObjectsOfType<T>(true))
        {
            if (candidate.gameObject.scene.name != "MainMenu") continue;
            if (found != null) return null;
            found = candidate;
        }
        return found;
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    private static bool Finite(Bounds value) => Finite(value.min) && Finite(value.max) && value.size.x > 0 && value.size.y > 0;
    private static bool Visible(Bounds area)
    {
        if (!Camera.main) return false;
        Vector3 min = Camera.main.WorldToViewportPoint(area.min), max = Camera.main.WorldToViewportPoint(area.max);
        return Finite(min) && Finite(max) && min.x >= -0.001f && min.y >= -0.001f && max.x <= 1.001f && max.y <= 1.001f;
    }
    private static bool Overlap(Bounds a, Bounds b) => a.min.x < b.max.x - 0.001f && a.max.x > b.min.x + 0.001f &&
        a.min.y < b.max.y - 0.001f && a.max.y > b.min.y + 0.001f;
    private bool CheckFailure(string detail) { Detail = detail; return false; }
    private void Advance(int next, double now) { stage = next; stageStarted = now; frames = 0; }
    internal void Stop(string outcome, string detail) => Finish(outcome, detail);
    private bool Finish(string outcome, string detail)
    {
        if (outcome != "succeeded" && ownsCreation && OwnsDisconnectedContext())
        {
            try
            {
                if (dropdown != null && dropdown && dropdown.gameObject.activeInHierarchy && dropdown.closeAction != null) dropdown.Close();
                if (owner != null && owner && owner.gameObject.activeInHierarchy) owner.Close();
                cleanupRequested = true;
            }
            catch (Exception ex) { detail += "_cleanup_" + ex.GetType().Name; outcome = "failed"; }
        }
        Busy = false;
        Outcome = outcome;
        Detail = detail;
        return true;
    }
    internal object Result() => new { state = Outcome, detail = Detail, stage, checks,
        error_delta = SmokeErrorCounter.Count - errorsAtStart, cleanup_requested = cleanupRequested,
        native_creation_closed = owner != null && owner && !owner.gameObject.activeInHierarchy,
        dropdown_closed_geometry_restored = geometrySaved && CheckClosedGeometry(),
        original_region_unchanged = DestroyableSingleton<ServerManager>.InstanceExists &&
            DestroyableSingleton<ServerManager>.Instance.CurrentRegion?.Name == originalRegion,
        region_selection_tested = false, public_browser_tested = false, button_click_bindings_tested = false,
        screenshot_completion_verified = false };
}
