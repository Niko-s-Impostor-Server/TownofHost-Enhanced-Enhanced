using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BepInEx.Logging;
using TOHE;
using UnityEngine;
using UObject = UnityEngine.Object;
using ModTab = TOHE.TabGroup;

namespace TOHEE.RuntimeSmoke;

// Count severities only. Never retain log messages or exception payloads.
internal sealed class SmokeErrorCounter : ILogListener
{
    private static long count;
    internal static long Count => Interlocked.Read(ref count);
    public LogLevel LogLevelFilter => LogLevel.Error | LogLevel.Fatal;
    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0) Interlocked.Increment(ref count);
    }
    public void Dispose() { }
}

// A fixed native UI sequence. It accepts no method, object, tab, or option arguments.
internal sealed class GuiLocalSmoke
{
    private readonly string directory;
    private int capturedTab = -1;
    internal GuiLocalSmoke(string directory) => this.directory = directory;
    private GameSettingMenu? owner;
    private GameSettingMenu? retiredOwner;
    private readonly List<ModTab> tabs = new();
    private readonly List<object> checks = new();
    private readonly Dictionary<ModTab, (int Controls, int Headers)> firstCounts = new();
    private readonly HashSet<int> retiredControls = new();
    private readonly long errorsAtStart = SmokeErrorCounter.Count;
    private int stage;
    private int tabIndex;
    private int pass;
    private int firstOwnerId;
    private int stableFrames;
    private string state = "running";
    internal string? Failure { get; private set; }

    internal void Begin() => DestroyableSingleton<GameStartManager>.Instance.ClickEdit();

    internal bool Tick()
    {
        if (SmokeErrorCounter.Count != errorsAtStart) return Fail("logged_error_during_gui");
        if (stage == 0)
        {
            owner = GameSettingMenu.Instance;
            if (owner == null || !owner) return false;
            int id = owner.GetInstanceID();
            if (pass == 0) firstOwnerId = id;
            else if (id == firstOwnerId) return Fail("reopen_reused_owner");
            tabs.Clear();
            foreach (var tab in Enum.GetValues<ModTab>())
            {
                int menuCount = 0;
                foreach (var menu in owner.GetComponentsInChildren<GameOptionsMenu>(true))
                    if (menu.name == "tab_" + tab) menuCount++;
                if (menuCount != 1) return Fail("mod_menu_count_" + (int)tab);
                foreach (var button in owner.GetComponentsInChildren<PassiveButton>(true))
                    if (button.name == "Button_" + tab && button.gameObject.activeInHierarchy)
                    { tabs.Add(tab); break; }
            }
            if (tabs.Count == 0 || tabs[0] != ModTab.SystemSettings) return Fail("visible_system_tab_missing");
            checks.Add(new { check = "owner_and_menu_instances", pass, visible_tabs = tabs.Count });
            if (pass == 1)
            {
                // This owner has never built SystemSettings: close during its asynchronous build.
                owner.ChangeTab(3, false);
                RetireOwner();
                checks.Add(new { check = "close_requested_during_build", pass });
                stage = 3;
                return false;
            }
            // Start a build, interrupt it in the same frame, and resume on the next Update.
            owner.ChangeTab(3, false);
            owner.ChangeTab(1, false);
            stage = 1;
            return false;
        }
        if (stage == 1)
        {
            if (owner == null || !owner || GameSettingMenu.Instance != owner) return Fail("settings_owner_lost");
            owner.ChangeTab(3, false);
            tabIndex = 0;
            stableFrames = 0;
            stage = 2;
            return false;
        }
        if (stage == 2)
        {
            if (owner == null || !owner || GameSettingMenu.Instance != owner) return Fail("settings_owner_lost");
            var tab = tabs[tabIndex];
            GameOptionsMenu? menu = null;
            foreach (var candidate in owner.GetComponentsInChildren<GameOptionsMenu>(true))
                if (candidate.name == "tab_" + tab) { menu = candidate; break; }
            if (menu == null || !menu || !menu.gameObject.activeInHierarchy) return Fail("active_tab_missing_" + (int)tab);
            if (!CountsReady(menu, tab, out int controls, out int headers)) { stableFrames = 0; return false; }
            var controller = ControllerManager.Instance;
            var ui = controller ? controller.CurrentUiState : null;
            if (ui == null || ui.MenuName != menu.name) { stableFrames = 0; return false; }
            if (++stableFrames < 2) return false;
            if (!CheckMenu(menu, tab)) return false;
            if (pass == 0 && tab is ModTab.SystemSettings or ModTab.CrewmateRoles && capturedTab != (int)tab)
            {
                capturedTab = (int)tab;
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "settings-" + tab + ".png"));
                return false; // Capture this tab at end of frame before switching away.
            }
            var counts = (controls, headers);
            if (pass == 0) firstCounts[tab] = counts;
            else if (!firstCounts.TryGetValue(tab, out var previous) || previous != counts)
                return Fail("reopen_changed_counts_" + (int)tab);
            checks.Add(new { check = "tab_complete", pass, tab = (int)tab, controls, headers });
            if (++tabIndex < tabs.Count)
            {
                owner.ChangeTab((int)tabs[tabIndex] + 3, false);
                stableFrames = 0;
                return false;
            }
            if (!CheckHelpIcons(owner)) return false;
            RetireOwner();
            stage = 3;
            return false;
        }
        if (stage == 3)
        {
            // Reached on a later Update, after Unity's end-of-frame destruction.
            if (retiredOwner != null && retiredOwner) return false;
            if (GameSettingMenu.Instance) return Fail("owner_survived_close");
            var pairs = ModGameOptionsMenu.OptionList.GetEnumerator();
            while (pairs.MoveNext())
                if (!pairs.Current.Key || retiredControls.Contains(pairs.Current.Key.GetInstanceID()))
                    return Fail("stale_option_registry");
            if (ModGameOptionsMenu.OptionList.Count != 0 || ModGameOptionsMenu.BehaviourList.Count != 0 ||
                ModGameOptionsMenu.CategoryHeaderList.Count != 0) return Fail("registries_not_cleared");
            checks.Add(new { check = "close_cleared_owner_and_registries", pass });
            owner = null;
            retiredOwner = null;
            if (pass < 2)
            {
                pass++;
                DestroyableSingleton<GameStartManager>.Instance.ClickEdit();
                stage = 0;
                return false;
            }
            state = "succeeded";
            return true;
        }
        return false;
    }

    private void RetireOwner()
    {
        retiredControls.Clear();
        var pairs = ModGameOptionsMenu.OptionList.GetEnumerator();
        while (pairs.MoveNext())
            if (pairs.Current.Key) retiredControls.Add(pairs.Current.Key.GetInstanceID());
        retiredOwner = owner;
        owner!.Close();
    }

    private static bool CountsReady(GameOptionsMenu menu, ModTab tab, out int controls, out int headers)
    {
        controls = 0;
        headers = 0;
        for (int index = 0; index < OptionItem.AllOptions.Count; index++)
        {
            var definition = OptionItem.AllOptions[index];
            if (definition.Tab != tab) continue;
            if (definition is TextOptionItem)
            {
                if (!ModGameOptionsMenu.CategoryHeaderList.TryGetValue(index, out var header) || !header ||
                    !header.transform.IsChildOf(menu.transform)) return false;
                headers++;
            }
            else
            {
                if (!ModGameOptionsMenu.BehaviourList.TryGetValue(index, out var row) || !row ||
                    !row.transform.IsChildOf(menu.transform) || !ModGameOptionsMenu.OptionList.TryGetValue(row, out int actual) ||
                    actual != index) return false;
                controls++;
            }
        }
        return true;
    }

    private bool CheckMenu(GameOptionsMenu menu, ModTab tab)
    {
        var ids = new HashSet<int>();
        var indices = new HashSet<int>();
        foreach (var row in menu.GetComponentsInChildren<OptionBehaviour>(true))
        {
            // Native inactive prefab origins are intentionally excluded from registered controls.
            if (!ModGameOptionsMenu.OptionList.TryGetValue(row, out int index)) continue;
            if (index < 0 || index >= OptionItem.AllOptions.Count || OptionItem.AllOptions[index].Tab != tab ||
                !ids.Add(row.GetInstanceID()) || !indices.Add(index)) return Fail("duplicate_or_wrong_control_" + (int)tab);
        }
        int expected = 0;
        foreach (var definition in OptionItem.AllOptions)
            if (definition.Tab == tab && definition is not TextOptionItem) expected++;
        if (ids.Count != expected || menu.Children.Count != expected) return Fail("owned_control_count_" + (int)tab);
        var headerIds = new HashSet<int>();
        for (int index = 0; index < OptionItem.AllOptions.Count; index++)
        {
            var definition = OptionItem.AllOptions[index];
            if (definition.Tab != tab || definition is not TextOptionItem) continue;
            if (!ModGameOptionsMenu.CategoryHeaderList.TryGetValue(index, out var header) || !header ||
                !headerIds.Add(header.GetInstanceID())) return Fail("duplicate_or_missing_header_" + (int)tab);
        }
        var ui = ControllerManager.Instance.CurrentUiState;
        if (!ui.CurrentSelection || !ui.CurrentSelection.gameObject.activeInHierarchy ||
            !ui.SelectableUiElements.Contains(ui.CurrentSelection) || ui.BackButton != menu.BackButton)
            return Fail("controller_selection_or_back_" + (int)tab);
        foreach (var selectable in ui.SelectableUiElements)
            if (!selectable || !selectable.gameObject.activeInHierarchy) return Fail("hidden_controller_selectable_" + (int)tab);
        foreach (var row in menu.Children)
        {
            foreach (var element in row.GetComponentsInChildren<UiElement>(true))
            {
                if (!row.gameObject.activeInHierarchy)
                {
                    if (ui.SelectableUiElements.Contains(element)) return Fail("hidden_row_in_navigation_" + (int)tab);
                    continue;
                }
                if (!element.gameObject.activeInHierarchy) continue;
                if (!ui.SelectableUiElements.Contains(element)) return Fail("visible_row_not_selectable_" + (int)tab);
                var navigation = element.ControllerNav;
                if (!ValidNeighbour(navigation.selectOnUp, menu) || !ValidNeighbour(navigation.selectOnDown, menu))
                    return Fail("navigation_outside_active_menu_" + (int)tab);
            }
        }
        return true;
    }

    private static bool ValidNeighbour(UiElement? element, GameOptionsMenu menu)
        => element == null || !element || (element.gameObject.activeInHierarchy && element.transform.IsChildOf(menu.transform));

    private bool CheckHelpIcons(GameSettingMenu menu)
    {
        int tested = 0;
        foreach (var row in menu.GetComponentsInChildren<OptionBehaviour>(true))
        {
            if (!ModGameOptionsMenu.OptionList.ContainsKey(row)) continue;
            var option = row.TryCast<StringOption>();
            if (option == null || !option || CountIcons(option) == 0) continue;
            if (CountIcons(option) != 1) return Fail("duplicate_help_icon_before_initialize");
            option.Initialize(); option.Initialize(); option.Initialize();
            if (CountIcons(option) != 1) return Fail("duplicate_help_icon_after_initialize");
            tested++;
        }
        if (tabs.Contains(ModTab.CrewmateRoles) && tested == 0) return Fail("no_role_help_icon_tested");
        checks.Add(new { check = "help_icon_initialize_three_times", pass, rows = tested });
        return true;
    }

    private static int CountIcons(StringOption option)
    {
        int count = 0;
        for (int index = 0; index < option.transform.childCount; index++)
            if (option.transform.GetChild(index).name.EndsWith("HelpIcon", StringComparison.Ordinal)) count++;
        return count;
    }

    private bool Fail(string label) { Failure = label; state = "failed"; return false; }

    internal void Stop(string stoppedState)
    {
        state = stoppedState;
        try { if (owner != null && owner && GameSettingMenu.Instance == owner) owner.Close(); }
        catch (Exception ex) { Failure ??= "cleanup_" + ex.GetType().Name; }
    }

    internal object Result() => new { state, stage, pass, failure = Failure,
        error_delta = SmokeErrorCounter.Count - errorsAtStart, checks };
}
