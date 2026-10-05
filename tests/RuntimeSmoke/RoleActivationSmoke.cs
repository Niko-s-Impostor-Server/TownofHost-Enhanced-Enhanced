using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HarmonyLib;
using TOHE;
using TOHE.Roles.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace TOHEE.RuntimeSmoke;

// Test-only fixed scenario. Selection always uses the ordinary native folder/button path.
internal sealed class RoleActivationSmoke(string smokeDirectory)
{
    internal const double TimeoutSeconds = 25;
    private const double SelectionTimeoutSeconds = 2;
    private static readonly CustomRoles[] Scenarios =
    [
        CustomRoles.CrewmateTOHE, CustomRoles.EngineerTOHE, CustomRoles.ScientistTOHE,
        CustomRoles.TrackerTOHE, CustomRoles.NoisemakerTOHE, CustomRoles.DetectiveTOHE, CustomRoles.Doctor,
        CustomRoles.Detective, CustomRoles.Mayor, CustomRoles.Sheriff, CustomRoles.Snitch,
        CustomRoles.ImpostorTOHE, CustomRoles.PhantomTOHE, CustomRoles.ShapeshifterTOHE, CustomRoles.ViperTOHE,
        CustomRoles.Blackmailer, CustomRoles.EvilTracker, CustomRoles.Camouflager, CustomRoles.BountyHunter,
        CustomRoles.Jester, CustomRoles.Opportunist, CustomRoles.Amnesiac, CustomRoles.Arsonist,
        CustomRoles.Jackal, CustomRoles.Maverick, CustomRoles.Pursuer, CustomRoles.Innocent
    ];
    private readonly string screenshot = Path.Combine(Path.GetFullPath(smokeDirectory), "roles-sheriff.png");
    private readonly List<object> results = new();
    private readonly Dictionary<CustomRoles, string[]> paths = new();
    private readonly List<DirectoryCursor> cursors = new();
    private readonly long errorsAtStart = SmokeErrorCounter.Count;
    private TaskAdderGame? owner;
    private CustomRoles originalRole;
    private CustomRoles target;
    private CustomRoles previousRole;
    private RoleBase? previousInstance;
    private RoleBase? activeInstance;
    private string[]? navigation;
    private int navigationIndex;
    private int scenarioIndex;
    private int stage;
    private int openingFrames;
    private int selectedFrame;
    private long selectedHudFrame;
    private double selectedAt;
    private int headerMatchedFrame = -1;
    private double started;
    private bool restoring;
    private bool restored;
    private bool screenshotRequested;
    private string state = "running";
    internal string? Failure { get; private set; }

    internal void Begin()
    {
        started = Time.realtimeSinceStartup;
        if (!ContextReady() || Minigame.Instance) throw new InvalidOperationException("Ready offline FreePlay with no minigame required");
        var playerState = TOHE.Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId];
        originalRole = playerState.MainRole;
        if (originalRole >= CustomRoles.NotAssigned) throw new InvalidOperationException("A restorable original main role is required");
        RoleActivationHudFrames.Install();
        foreach (var console in UObject.FindObjectsOfType<SystemConsole>())
        {
            if (console.gameObject.scene.name != "Tutorial" || !console.MinigamePrefab) continue;
            var prefab = console.MinigamePrefab.TryCast<TaskAdderGame>();
            if (prefab == null || !prefab) continue;
            PlayerControl.LocalPlayer.NetTransform.Halt();
            owner = UObject.Instantiate(prefab);
            owner.SafePositionWorld = console.SafePositionLocal + (Vector2)console.transform.position;
            owner.transform.SetParent(Camera.main.transform, false);
            owner.transform.localPosition = new Vector3(0, 0, -50);
            owner.Begin(null);
            return;
        }
        throw new InvalidOperationException("Native TaskAdder prefab unavailable");
    }

    internal bool Tick()
    {
        try
        {
            if (Time.realtimeSinceStartup - started >= TimeoutSeconds)
                return Fail("25_second_total_timeout", "timed_out");
            if (SmokeErrorCounter.Count != errorsAtStart) return Fail("logged_error_during_role_activation");
            if (!ContextReady()) return Fail("offline_freeplay_context_lost");
            if (stage == 5)
            {
                if (owner != null && owner || Minigame.Instance) return false;
                restored = Matches(originalRole);
                if (!restored) return Fail("original_role_not_restored_after_close");
                RoleActivationHudFrames.Remove();
                state = "succeeded";
                return true;
            }
            if (owner == null || !owner || Minigame.Instance != owner) return Fail("owned_taskadder_lost");
            if (stage == 0)
            {
                if (owner.amOpening || ++openingFrames < 2) return false;
                var folder = FindFolder(TOHE.Main.ModName);
                if (folder == null || !folder) return Fail("custom_role_root_missing");
                folder.OnClick();
                stage = 1;
                return false;
            }
            if (stage == 1)
            {
                // Discover real role-to-page mappings through native clicks, without changing any role.
                var folders = new List<TaskFolder>();
                foreach (var item in owner.ActiveItems)
                {
                    var folder = item.GetComponent<TaskFolder>();
                    if (folder) folders.Add(folder);
                    else
                    {
                        var button = item.GetComponent<TaskAddButton>();
                        if (!button || !ShowFolderPatch.TryGetCustomRole(button, out var role))
                            return Fail("unregistered_role_page_button");
                        if (!paths.TryAdd(role, owner.Hierarchy.ToArray().Skip(1).Select(part => part.FolderName).ToArray()))
                            return Fail("role_occurs_in_multiple_pages");
                    }
                }
                if (folders.Count > 0)
                {
                    cursors.Add(new DirectoryCursor(folders.Select(folder => folder.FolderName).ToArray()) { Next = 1 });
                    folders[0].OnClick();
                    return false;
                }
                if (AdvanceDiscovery()) return false;
                if (Scenarios.Any(role => !paths.ContainsKey(role)) || !paths.ContainsKey(originalRole))
                    return Fail("fixed_scenario_or_restore_page_missing");
                stage = 2;
                return false;
            }
            if (stage == 2)
            {
                target = restoring ? originalRole : Scenarios[scenarioIndex];
                navigation = paths[target];
                navigationIndex = 0;
                owner.GoToRoot();
                stage = 3;
                return false;
            }
            if (stage == 3)
            {
                if (navigationIndex < navigation!.Length)
                {
                    var folder = FindFolder(navigation[navigationIndex++]);
                    if (folder == null || !folder) return Fail("native_role_navigation_missing");
                    folder.OnClick();
                    return false;
                }
                TaskAddButton? selected = null;
                foreach (var item in owner.ActiveItems)
                {
                    var button = item.GetComponent<TaskAddButton>();
                    if (button && ShowFolderPatch.TryGetCustomRole(button, out var role) && role == target)
                    { if (selected != null) return Fail("duplicate_fixed_role_button"); selected = button; }
                }
                if (selected == null || !selected || !selected.Button || !selected.Button.enabled ||
                    !selected.gameObject.activeInHierarchy ||
                    !ControllerManager.Instance.CurrentUiState.SelectableUiElements.Contains(selected.Button))
                    return Fail("fixed_role_button_not_selectable");
                var playerState = TOHE.Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId];
                previousRole = playerState.MainRole;
                previousInstance = playerState.RoleClass;
                selected.AddTask();
                activeInstance = playerState.RoleClass;
                selectedFrame = Time.frameCount;
                selectedHudFrame = RoleActivationHudFrames.Count;
                selectedAt = Time.realtimeSinceStartup;
                headerMatchedFrame = -1;
                stage = 4;
                return false;
            }
            if (stage == 4)
            {
                if (!Matches(target)) return Fail("custom_or_native_role_mismatch");
                var playerState = TOHE.Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId];
                if (playerState.IsDead || PlayerControl.LocalPlayer.Data.IsDead || PlayerControl.LocalPlayer.Data.Disconnected)
                    return Fail("role_activation_killed_or_disconnected_local_player");
                if (!ReferenceEquals(activeInstance, playerState.RoleClass) || !playerState.RoleClass.IsEnable ||
                    !ReferenceEquals(playerState.RoleClass._state, playerState)) return Fail("role_onadd_instance_state");
                if (previousRole != target && (ReferenceEquals(previousInstance, activeInstance) || previousInstance!.IsEnable))
                    return Fail("previous_local_role_instance_not_removed");
                if (previousRole != target && !TOHE.Main.PlayerStates.Values.Any(item => item.MainRole == previousRole) &&
                    previousRole.GetStaticRoleClass().IsEnable) return Fail("unused_previous_static_role_still_enabled");
                long hudFrames = RoleActivationHudFrames.Count - selectedHudFrame;
                // Native vanilla roles have no TOHE task header. All fixed scenarios do.
                bool needsHeader = !restoring || !target.IsVanilla();
                bool headerMatches = !needsHeader || TaskPanelHeaderMatches();
                if (!headerMatches) headerMatchedFrame = -1;
                else if (headerMatchedFrame < 0) headerMatchedFrame = Time.frameCount;
                bool headerConfirmed = headerMatches && Time.frameCount > headerMatchedFrame;
                if (hudFrames < 5 || Time.frameCount - selectedFrame < 5 || !headerConfirmed)
                {
                    if (Time.realtimeSinceStartup - selectedAt >= SelectionTimeoutSeconds)
                        return Fail((headerConfirmed ? "native_hud_frame_timeout_" : "native_taskpanel_header_timeout_") + target,
                            "timed_out");
                    return false;
                }
                if (restoring)
                {
                    restored = true;
                    owner.ForceClose();
                    stage = 5;
                    return false;
                }
                results.Add(new { role = target.ToString(), native_role = PlayerControl.LocalPlayer.Data.Role.Role.ToString(),
                    active_hud_frames = hudFrames, local_role_lifecycle = true, taskpanel_header_confirmed = true,
                    selection_wait_ms = (int)((Time.realtimeSinceStartup - selectedAt) * 1000) });
                if (target == CustomRoles.Sheriff && !screenshotRequested)
                {
                    ScreenCapture.CaptureScreenshot(screenshot);
                    screenshotRequested = true;
                }
                // Keep the selected page/role for this frame, including the Sheriff capture end-of-frame.
                if (++scenarioIndex == Scenarios.Length) restoring = true;
                stage = 2;
                return false;
            }
            return false;
        }
        catch (Exception ex) { return Fail("role_smoke_" + ex.GetType().Name); }
    }

    private static bool ContextReady()
    {
        var client = AmongUsClient.Instance;
        var player = PlayerControl.LocalPlayer;
        return client && client.NetworkMode == NetworkModes.FreePlay && SceneManager.GetActiveScene().name == "Tutorial" &&
            player && player.Data != null && player.Data.Role && !player.Data.IsDead && !player.Data.Disconnected &&
            ShipStatus.Instance && Camera.main && TOHE.Main.GameIsLoaded && TOHE.Main.RealOptionsData != null &&
            !GameStates.IsLobby && !GameStates.IsEnded && GameStates.IsModHost &&
            TOHE.Main.PlayerStates.ContainsKey(player.PlayerId);
    }

    private static bool Matches(CustomRoles role)
    {
        var player = PlayerControl.LocalPlayer;
        return player && player.Data != null && player.Data.Role &&
            TOHE.Main.PlayerStates.TryGetValue(player.PlayerId, out var playerState) &&
            playerState.MainRole == role && player.Data.Role.Role == role.GetRoleTypes();
    }

    private static bool TaskPanelHeaderMatches()
    {
        if (!DestroyableSingleton<HudManager>.InstanceExists) return false;
        var panel = DestroyableSingleton<HudManager>.Instance.TaskPanel;
        if (!panel || !panel.gameObject.activeInHierarchy || !panel.taskText || !panel.taskText.enabled ||
            !panel.taskText.gameObject.activeInHierarchy) return false;
        var player = PlayerControl.LocalPlayer;
        string expected = Utils.GetDisplayRoleAndSubName(player.PlayerId, player.PlayerId, false).RemoveHtmlTags().Trim() + ":";
        // Match the first line, not a role name mentioned somewhere in stale instructions.
        // Parsed text also waits for TMP's own mesh update; never force an update or write the label.
        return FirstLine(panel.taskText.text) == expected && FirstLine(panel.taskText.GetParsedText()) == expected;
    }

    private static string FirstLine(string? text)
    {
        string plain = (text ?? string.Empty).RemoveHtmlTags();
        int end = plain.IndexOfAny(new[] { '\r', '\n' });
        return (end < 0 ? plain : plain[..end]).Trim();
    }

    private TaskFolder? FindFolder(string name)
    {
        TaskFolder? found = null;
        foreach (var item in owner!.ActiveItems)
        {
            var folder = item.GetComponent<TaskFolder>();
            if (!folder || folder.Parent != owner || folder.FolderName != name) continue;
            if (found != null) throw new InvalidOperationException("Duplicate native page folder");
            found = folder;
        }
        return found;
    }

    private bool AdvanceDiscovery()
    {
        while (cursors.Count > 0)
        {
            owner!.GoUpOne();
            var cursor = cursors[^1];
            if (cursor.Next < cursor.Names.Length)
            {
                var folder = FindFolder(cursor.Names[cursor.Next++]);
                if (folder == null || !folder) throw new InvalidOperationException("Native sibling page missing");
                folder.OnClick();
                return true;
            }
            cursors.RemoveAt(cursors.Count - 1);
        }
        return false;
    }

    private bool Fail(string label, string failedState = "failed")
    {
        Failure = label;
        Stop(failedState);
        return false;
    }

    internal void Stop(string stoppedState)
    {
        if (state != "timed_out") state = stoppedState;
        try
        {
            // Refuse all role mutation after leaving the original offline test context.
            if (ContextReady() && owner != null && owner && Minigame.Instance == owner)
            {
                if (!Matches(originalRole) && paths.TryGetValue(originalRole, out var path))
                {
                    owner.GoToRoot();
                    foreach (string name in path)
                    {
                        var folder = FindFolder(name);
                        if (folder == null || !folder) throw new InvalidOperationException("Restore page missing");
                        folder.OnClick();
                    }
                    foreach (var item in owner.ActiveItems)
                    {
                        var button = item.GetComponent<TaskAddButton>();
                        if (button && ShowFolderPatch.TryGetCustomRole(button, out var role) && role == originalRole)
                        { button.AddTask(); break; }
                    }
                }
                restored = Matches(originalRole);
                owner.ForceClose();
            }
        }
        catch (Exception ex) { Failure ??= "cleanup_" + ex.GetType().Name; }
        finally { RoleActivationHudFrames.Remove(); }
    }

    internal object Result() => new
    {
        state, stage, failure = Failure, planned_roles = Scenarios.Length, completed_roles = results.Count,
        restored_original_role = restored, sheriff_capture_requested = screenshotRequested,
        error_delta = SmokeErrorCounter.Count - errorsAtStart,
        skill_functionality_tested = false, scenarios = results
    };

    private sealed class DirectoryCursor(string[] names)
    {
        internal string[] Names { get; } = names;
        internal int Next;
    }
}

// One fixed typed Harmony target, no user-selected method or reflective invocation interface.
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
internal static class RoleActivationHudFrames
{
    private static Harmony? hook;
    private static long count;
    private static int lastFrame = -1;
    internal static long Count => Interlocked.Read(ref count);
    internal static void Install()
    {
        if (hook != null) return;
        lastFrame = -1;
        hook = Harmony.CreateAndPatchAll(typeof(RoleActivationHudFrames), "local.tohee.runtime-smoke.role-hud-frames");
    }
    internal static void Remove() { hook?.UnpatchSelf(); hook = null; }
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(HudManager __instance, bool __runOriginal)
    {
        var client = AmongUsClient.Instance;
        if (!__runOriginal || !client || client.NetworkMode != NetworkModes.FreePlay ||
            !__instance || !__instance.TaskPanel || !__instance.TaskPanel.gameObject.activeInHierarchy ||
            !DestroyableSingleton<HudManager>.InstanceExists || __instance != DestroyableSingleton<HudManager>.Instance ||
            Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        Interlocked.Increment(ref count);
    }
}
