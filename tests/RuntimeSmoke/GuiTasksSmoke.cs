using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using TOHE;
using TOHE.Roles.Core;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace TOHEE.RuntimeSmoke;

// Traverse the real native folder UI, including bounded pages and controller registrations.
internal sealed class GuiTasksSmoke
{
    private TaskAdderGame? owner;
    private readonly List<object> checks = new();
    private readonly Dictionary<CustomRoles, TaskAddButton> buttons = new();
    private readonly List<TaskAddButton> retiredButtons = new();
    private readonly Dictionary<CustomRoles, string[]> rolePaths = new();
    private readonly HashSet<CustomRoles> visited = new();
    private readonly List<DirectoryCursor> cursors = new();
    private readonly Dictionary<byte, int> nativeTaskCounts = new();
    private static readonly RoleTypes[] UnsupportedNativeRoles = [RoleTypes.Detective, RoleTypes.Viper, RoleTypes.Judge];
    private readonly Dictionary<byte, string> rejectionTasks = new();
    private GameObject? rejectionOwner;
    private CustomRoles rejectionCustomRole;
    private RoleTypes rejectionNativeRole;
    private RoleBase? rejectionRoleInstance;
    private int rejectionNativeInstance;
    private int[] rejectionItems = [];
    private int[] rejectionSelectables = [];
    private readonly long errorsAtStart = SmokeErrorCounter.Count;
    private CustomRoles originalRole;
    private CustomRoles previousRole;
    private RoleBase? previousInstance;
    private bool roleChanged;
    private int firstOwnerId;
    private int stage;
    private int pass;
    private int frames;
    private int pageCount;
    private int directoryCount;
    private bool restored;
    private string state = "running";
    internal string? Failure { get; private set; }

    internal void Begin()
    {
        if (!GameStates.IsFreePlay || !TOHE.Main.GameIsLoaded || TOHE.Main.RealOptionsData == null || GameStates.IsLobby || GameStates.IsEnded || !GameStates.IsModHost)
            throw new InvalidOperationException("TOHE FreePlay readiness assertions failed");
        originalRole = TOHE.Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId].MainRole;
        if (originalRole >= CustomRoles.NotAssigned) throw new InvalidOperationException("Original role must be restorable");
        int dummyTasks = 0, dummyPlayers = 0;
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (!player || player.Data == null) continue;
            nativeTaskCounts[player.PlayerId] = player.Data.Tasks.Count;
            if (player.isDummy) { dummyTasks += player.Data.Tasks.Count; dummyPlayers++; }
        }
        checks.Add(new { check = "freeplay_readiness_and_native_tasks", local_tasks = PlayerControl.LocalPlayer.Data.Tasks.Count,
            dummy_players = dummyPlayers, dummy_tasks = dummyTasks });
        Open();
    }

    private void Open()
    {
        if (Minigame.Instance || !Camera.main) throw new InvalidOperationException("Minigame or camera not ready");
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
            if (pass == 0) firstOwnerId = owner.GetInstanceID();
            else if (owner.GetInstanceID() == firstOwnerId) throw new InvalidOperationException("TaskAdder owner reused");
            rolePaths.Clear(); visited.Clear(); cursors.Clear(); buttons.Clear();
            pageCount = directoryCount = frames = 0;
            checks.Add(new { check = "native_layout", pass, file_width = owner.fileWidth,
                folder_width = owner.folderWidth, line_width = owner.lineWidth });
            return;
        }
        throw new InvalidOperationException("Loaded TaskAdder console unavailable");
    }

    internal bool Tick()
    {
        if (SmokeErrorCounter.Count != errorsAtStart) return Fail("logged_error_during_tasks");
        if (!GameStates.IsFreePlay) return Fail("offline_freeplay_context_lost");
        if (stage == 6)
        {
            if (owner != null && owner) return false;
            if (Minigame.Instance) return false;
            checks.Add(new { check = "task_owner_destroyed", pass, restored });
            owner = null;
            if (pass++ == 0) { Open(); stage = 0; return false; }
            state = "succeeded";
            return true;
        }
        if (owner == null || !owner || Minigame.Instance != owner) return Fail("task_owner_lost");
        if (stage == 0)
        {
            if (owner.amOpening) return false;
            if (++frames < 2) return false;
            var entry = FindFolder(TOHE.Main.ModName);
            if (entry == null || !entry) return Fail("custom_roles_entry_missing");
            if (!CheckClickable(entry.Button)) return false;
            entry.OnClick();
            stage = 1; frames = 0;
            return false;
        }
        if (stage == 1)
        {
            // One Update after each native folder transition lets Start and end-of-frame destruction run.
            if (++frames < 1) return false;
            if (!InspectPage()) return false;
            var folders = CurrentFolders();
            if (folders.Count > 0)
            {
                cursors.Add(new DirectoryCursor(folders.Select(folder => folder.FolderName).ToArray()));
                folders[0].OnClick();
                cursors[^1].Next = 1;
                frames = 0;
                return false;
            }
            pageCount++;
            foreach (var pair in buttons)
            {
                if (!visited.Add(pair.Key)) return Fail("role_appears_in_multiple_pages");
                rolePaths[pair.Key] = owner.Hierarchy.ToArray().Skip(1).Select(folder => folder.FolderName).ToArray();
            }
            if (AdvanceTraversal()) { frames = 0; return false; }
            var expected = CustomRolesHelper.AllRoles.Where(role => role != CustomRoles.NotAssigned).ToHashSet();
            if (!visited.SetEquals(expected)) return Fail("selectable_role_coverage_incomplete");
            checks.Add(new { check = "all_pages_unique_complete_and_bounded", pass, roles = visited.Count,
                pages = pageCount, directories = directoryCount, max_items = ShowFolderPatch.MaxPageItems });
            Select(CustomRoles.CrewmateTOHE);
            stage = 2; frames = 0;
            return false;
        }
        if (stage is 2 or 3 or 7)
        {
            if (++frames < 3) return false;
            var target = stage == 3 ? CustomRoles.EngineerTOHE : CustomRoles.CrewmateTOHE;
            if (!InspectPage() || !CheckRole(target)) return false;
            checks.Add(new { check = "role_and_overlay_after_updates", pass, role = target.ToString() });
            if (stage == 2) { Select(CustomRoles.EngineerTOHE); stage = 3; }
            else if (stage == 3) { Select(CustomRoles.CrewmateTOHE); stage = 7; }
            else
            {
                retiredButtons.Clear(); retiredButtons.AddRange(buttons.Values);
                Navigate(CustomRoles.CrewmateTOHE);
                stage = 5;
            }
            frames = 0;
            return false;
        }
        if (stage == 5)
        {
            if (++frames < 3) return false;
            foreach (var old in retiredButtons) if (old) return Fail("retired_task_button_survived");
            if (!InspectPage() || !CheckRole(CustomRoles.CrewmateTOHE)) return false;
            checks.Add(new { check = "root_reentry_complete", pass, page_controls = buttons.Count });
            // Check native compatibility once; both owners still traverse every custom page.
            if (pass != 0) { RestoreAndClose(); return false; }
            OpenNativeRoleFolder(RoleTeamTypes.Crewmate);
            stage = 8; frames = 0;
            return false;
        }
        if (stage == 8)
        {
            if (++frames < 2) return false;
            if (!InspectNativeRolePage(RoleTeamTypes.Crewmate) || !BeginRejectedNativeSelections()) return false;
            stage = 9; frames = 0;
            return false;
        }
        if (stage == 9)
        {
            if (++frames < 2) return false;
            if (!CheckRejectedNativeSelections()) return false;
            ReleaseRejectionOwner();
            SelectNativeRole(RoleTypes.Engineer, CustomRoles.Engineer);
            stage = 10; frames = 0;
            return false;
        }
        if (stage is 10 or 11)
        {
            if (++frames < 3) return false;
            var native = stage == 10 ? RoleTypes.Engineer : RoleTypes.Crewmate;
            var custom = stage == 10 ? CustomRoles.Engineer : CustomRoles.Crewmate;
            if (!CheckNativeRoleSelection(native, custom)) return false;
            checks.Add(new { check = "supported_native_button_role_and_lifecycle", pass, role = native.ToString() });
            if (stage == 10) { SelectNativeRole(RoleTypes.Crewmate, CustomRoles.Crewmate); stage = 11; }
            else { OpenNativeRoleFolder(RoleTeamTypes.Impostor); stage = 12; }
            frames = 0;
            return false;
        }
        if (stage == 12)
        {
            if (++frames < 2) return false;
            if (!InspectNativeRolePage(RoleTeamTypes.Impostor)) return false;
            RestoreAndClose();
            return false;
        }
        return false;
    }

    private void RestoreAndClose()
    {
        Select(originalRole);
        restored = TOHE.Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId].MainRole == originalRole &&
            PlayerControl.LocalPlayer.Data.Role.Role == originalRole.GetRoleTypes();
        if (!restored) { Fail("original_role_restore_failed"); return; }
        owner!.ForceClose(); stage = 6;
    }

    private void OpenNativeRoleFolder(RoleTeamTypes team)
    {
        owner!.GoToRoot();
        TaskFolder? selected = null;
        foreach (var item in owner.ActiveItems)
        {
            var folder = item.GetComponent<TaskFolder>();
            if (!folder || !folder.RoleChildren.ToArray().Any(role => role && role.TeamType == team)) continue;
            if (selected != null) throw new InvalidOperationException("Duplicate native role team folder");
            selected = folder;
        }
        if (selected == null || !selected || !CheckClickable(selected.Button))
            throw new InvalidOperationException("Native role team folder unavailable");
        selected.OnClick();
    }

    private bool InspectNativeRolePage(RoleTeamTypes team)
    {
        var roles = new HashSet<RoleTypes>();
        foreach (var item in owner!.ActiveItems)
        {
            var button = item.GetComponent<TaskAddButton>();
            if (!button || !button.Role || ShowFolderPatch.TryGetCustomRole(button, out _) || button.Role.TeamType != team ||
                !roles.Add(button.Role.Role)) return Fail("native_role_page_contents");
            if (UnsupportedNativeRoles.Contains(button.Role.Role)) return Fail("unsupported_native_role_exposed");
            if (!CheckClickable(button.Button)) return false;
        }
        if (roles.Count == 0 || owner.ActiveItems.Count > ShowFolderPatch.MaxPageItems) return Fail("native_role_page_empty_or_unbounded");
        if (ControllerManager.Instance.CurrentUiState.BackButton != owner.FolderBackButton) return Fail("native_role_folder_back_button");
        checks.Add(new { check = "native_role_page_excludes_unsupported", pass, team = team.ToString(), controls = roles.Count });
        return true;
    }

    private bool BeginRejectedNativeSelections()
    {
        var player = PlayerControl.LocalPlayer;
        var playerState = TOHE.Main.PlayerStates[player.PlayerId];
        rejectionCustomRole = playerState.MainRole;
        rejectionNativeRole = player.Data.Role.Role;
        rejectionRoleInstance = playerState.RoleClass;
        rejectionNativeInstance = player.Data.Role.GetInstanceID();
        rejectionItems = owner!.ActiveItems.ToArray().Select(item => item.GetInstanceID()).ToArray();
        rejectionSelectables = ControllerManager.Instance.CurrentUiState.SelectableUiElements.ToArray().Select(item => item.GetInstanceID()).ToArray();
        rejectionTasks.Clear();
        foreach (var current in PlayerControl.AllPlayerControls)
            if (current && current.Data != null) rejectionTasks.Add(current.PlayerId, TaskFingerprint(current));

        // An inactive private parent prevents Start/OnEnable, rendering and controller registration.
        // Use the actual native RoleButton prefab and role prototypes; invoke only fixed AddTask.
        rejectionOwner = new GameObject("TOHEE_RuntimeSmoke_RejectedNativeRoles");
        rejectionOwner.SetActive(false);
        rejectionOwner.transform.SetParent(owner.transform, false);
        foreach (var native in UnsupportedNativeRoles)
        {
            var prototype = DestroyableSingleton<RoleManager>.Instance.GetRole(native);
            if (!prototype || prototype.Role != native) return Fail("unsupported_native_role_prototype_missing");
            var button = UObject.Instantiate(owner.RoleButton, rejectionOwner.transform);
            button.gameObject.SetActive(false);
            button.Role = prototype;
            button.SafePositionWorld = owner.SafePositionWorld;
            button.AddTask();
            if (!RejectionStateUnchanged()) return Fail("unsupported_native_role_not_rejected_" + native);
        }
        return true;
    }

    private bool RejectionStateUnchanged()
    {
        var player = PlayerControl.LocalPlayer;
        var playerState = TOHE.Main.PlayerStates[player.PlayerId];
        if (playerState.MainRole != rejectionCustomRole || player.Data.Role.Role != rejectionNativeRole ||
            player.Data.Role.GetInstanceID() != rejectionNativeInstance || !ReferenceEquals(playerState.RoleClass, rejectionRoleInstance)) return false;
        foreach (var current in PlayerControl.AllPlayerControls)
            if (current && current.Data != null && (!rejectionTasks.TryGetValue(current.PlayerId, out var before) || before != TaskFingerprint(current))) return false;
        return rejectionItems.SequenceEqual(owner!.ActiveItems.ToArray().Select(item => item.GetInstanceID())) &&
            rejectionSelectables.SequenceEqual(ControllerManager.Instance.CurrentUiState.SelectableUiElements.ToArray().Select(item => item.GetInstanceID()));
    }

    private bool CheckRejectedNativeSelections()
    {
        if (rejectionOwner == null || !rejectionOwner || rejectionOwner.activeInHierarchy || !RejectionStateUnchanged())
            return Fail("rejected_native_roles_changed_state_after_updates");
        checks.Add(new { check = "three_native_addtask_calls_rejected_without_role_task_or_ui_mutation", pass,
            roles = UnsupportedNativeRoles.Select(role => role.ToString()).ToArray(), native_and_custom_role_unchanged = true,
            task_id_type_completion_and_instances_unchanged = true, ui_unchanged = true });
        return true;
    }

    private static string TaskFingerprint(PlayerControl player)
        => string.Join(";", player.Data.Tasks.ToArray().Select(task => $"{task.Id},{task.TypeId},{task.Complete}")) + "|" +
            string.Join(";", player.myTasks.ToArray().Select(task => $"{task.GetInstanceID()},{task.Id},{task.TaskType},{task.IsComplete}"));

    private void SelectNativeRole(RoleTypes native, CustomRoles custom)
    {
        var selected = owner!.ActiveItems.ToArray().Select(item => item.GetComponent<TaskAddButton>())
            .SingleOrDefault(button => button && button.Role && button.Role.Role == native);
        if (selected == null || !selected || !CheckClickable(selected.Button)) throw new InvalidOperationException("Supported native button unavailable");
        var playerState = TOHE.Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId];
        previousRole = playerState.MainRole; previousInstance = playerState.RoleClass; roleChanged = previousRole != custom;
        selected.AddTask();
    }

    private bool CheckNativeRoleSelection(RoleTypes native, CustomRoles custom)
    {
        var player = PlayerControl.LocalPlayer;
        var playerState = TOHE.Main.PlayerStates[player.PlayerId];
        if (playerState.MainRole != custom || player.Data.Role.Role != native || !playerState.RoleClass.IsEnable ||
            !ReferenceEquals(playerState.RoleClass._state, playerState)) return Fail("supported_native_role_state_or_lifecycle");
        if (roleChanged && (ReferenceEquals(previousInstance, playerState.RoleClass) || previousInstance!.IsEnable))
            return Fail("supported_native_previous_role_not_removed");
        foreach (var current in PlayerControl.AllPlayerControls)
            if (current && current.Data != null && nativeTaskCounts.TryGetValue(current.PlayerId, out int count) && current.Data.Tasks.Count != count)
                return Fail("native_tasks_changed_after_native_crew_selection");
        foreach (var item in owner!.ActiveItems)
        {
            var button = item.GetComponent<TaskAddButton>();
            if (!button || !button.Role || !button.Overlay || button.Overlay.enabled != (button.Role.Role == native))
                return Fail("supported_native_role_overlay");
        }
        return true;
    }

    private void ReleaseRejectionOwner()
    {
        if (rejectionOwner != null && rejectionOwner) UObject.Destroy(rejectionOwner);
        rejectionOwner = null;
    }

    private TaskFolder? FindFolder(string name)
    {
        TaskFolder? found = null;
        foreach (var item in owner!.ActiveItems)
        {
            var folder = item.GetComponent<TaskFolder>();
            if (!folder || folder.Parent != owner || folder.FolderName != name) continue;
            if (found != null) throw new InvalidOperationException("Duplicate native folder");
            found = folder;
        }
        return found;
    }

    private List<TaskFolder> CurrentFolders()
    {
        var result = new List<TaskFolder>();
        foreach (var item in owner!.ActiveItems)
        {
            var folder = item.GetComponent<TaskFolder>();
            if (folder) result.Add(folder);
        }
        return result;
    }

    private bool AdvanceTraversal()
    {
        while (cursors.Count > 0)
        {
            owner!.GoUpOne();
            var cursor = cursors[^1];
            if (cursor.Next < cursor.Names.Length)
            {
                var folder = FindFolder(cursor.Names[cursor.Next++]);
                if (folder == null || !folder) throw new InvalidOperationException("Native traversal sibling missing");
                folder.OnClick();
                return true;
            }
            cursors.RemoveAt(cursors.Count - 1);
        }
        return false;
    }

    private bool InspectPage()
    {
        buttons.Clear();
        if (owner!.ActiveItems.Count is < 1 or > ShowFolderPatch.MaxPageItems) return Fail("page_item_count_unbounded");
        var ui = ControllerManager.Instance.CurrentUiState;
        if (ui == null || ui.BackButton != owner.FolderBackButton) return Fail("task_folder_back_button");
        int folders = 0;
        var ids = new HashSet<int>();
        foreach (var item in owner.ActiveItems)
        {
            if (!item || !ids.Add(item.GetInstanceID())) return Fail("duplicate_page_item");
            var folder = item.GetComponent<TaskFolder>();
            if (folder)
            {
                folders++;
                if (folder.Parent != owner) return Fail("directory_owner_mismatch");
                if (!CheckClickable(folder.Button)) return false;
            }
            else
            {
                var button = item.GetComponent<TaskAddButton>();
                if (!button || !ShowFolderPatch.TryGetCustomRole(button, out var role) ||
                    role == CustomRoles.NotAssigned || !buttons.TryAdd(role, button)) return Fail("role_page_mapping");
                if (!CheckClickable(button.Button)) return false;
            }
            foreach (var renderer in item.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && !BoundsVisible(renderer.bounds)) return Fail("page_renderer_outside_viewport");
        }
        if (folders > 0 && buttons.Count > 0) return Fail("mixed_role_and_directory_page");
        if (folders > 0) directoryCount++;
        foreach (var selectable in ui.SelectableUiElements)
            if (!selectable || !selectable.gameObject.activeInHierarchy) return Fail("stale_task_selectable");
        return true;
    }

    private bool CheckClickable(UiElement element)
    {
        if (!element || !element.enabled || !element.gameObject.activeInHierarchy ||
            !ControllerManager.Instance.CurrentUiState.SelectableUiElements.Contains(element)) return Fail("page_controller_registration");
        int colliders = 0;
        foreach (var collider in element.GetComponentsInChildren<Collider2D>())
        {
            if (!collider.enabled) continue;
            colliders++;
            if (!BoundsVisible(collider.bounds)) return Fail("page_hitbox_outside_viewport");
        }
        if (colliders == 0) return Fail("page_click_hitbox_missing");
        return true;
    }

    private static bool BoundsVisible(Bounds bounds)
    {
        var camera = Camera.main;
        if (!camera || bounds.size.x <= 0 || bounds.size.y <= 0) return false;
        var min = bounds.min; var max = bounds.max;
        foreach (var point in new[] { new Vector3(min.x, min.y, bounds.center.z), new Vector3(min.x, max.y, bounds.center.z),
            new Vector3(max.x, min.y, bounds.center.z), new Vector3(max.x, max.y, bounds.center.z) })
        {
            var viewport = camera.WorldToViewportPoint(point);
            if (viewport.z < camera.nearClipPlane || viewport.z > camera.farClipPlane ||
                viewport.x < 0.015f || viewport.x > 0.985f || viewport.y < 0.015f || viewport.y > 0.985f) return false;
        }
        return true;
    }

    private void Navigate(CustomRoles role)
    {
        if (!rolePaths.TryGetValue(role, out var path)) throw new InvalidOperationException("Role page was not traversed");
        owner!.GoToRoot();
        foreach (string name in path)
        {
            var folder = FindFolder(name);
            if (folder == null || !folder) throw new InvalidOperationException("Role page native path missing");
            folder.OnClick();
        }
        buttons.Clear();
        foreach (var item in owner.ActiveItems)
        {
            var button = item.GetComponent<TaskAddButton>();
            if (button && ShowFolderPatch.TryGetCustomRole(button, out var currentRole)) buttons.Add(currentRole, button);
        }
    }

    private void Select(CustomRoles role)
    {
        Navigate(role);
        if (!buttons.TryGetValue(role, out var button)) throw new InvalidOperationException("Fixed role button unavailable");
        var playerState = TOHE.Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId];
        previousRole = playerState.MainRole; previousInstance = playerState.RoleClass; roleChanged = previousRole != role;
        button.AddTask();
    }

    private bool CheckRole(CustomRoles target)
    {
        var player = PlayerControl.LocalPlayer;
        if (!player || player.Data == null || !player.Data.Role ||
            !TOHE.Main.PlayerStates.TryGetValue(player.PlayerId, out var playerState) ||
            playerState.MainRole != target || player.Data.Role.Role != target.GetRoleTypes()) return Fail("task_role_state");
        if (!playerState.RoleClass.IsEnable || !ReferenceEquals(playerState.RoleClass._state, playerState)) return Fail("task_role_onadd_state");
        if (roleChanged && (ReferenceEquals(previousInstance, playerState.RoleClass) || previousInstance!.IsEnable))
            return Fail("task_previous_role_instance_not_removed");
        bool previousStillUsed = TOHE.Main.PlayerStates.Values.Any(current => current.MainRole == previousRole);
        if (roleChanged && !previousStillUsed && previousRole.GetStaticRoleClass().IsEnable) return Fail("task_previous_static_role_still_enabled");
        foreach (var current in PlayerControl.AllPlayerControls)
            if (current && current.Data != null && nativeTaskCounts.TryGetValue(current.PlayerId, out int count) && current.Data.Tasks.Count != count)
                return Fail("native_tasks_changed_after_crew_role_selection");
        if (!buttons.ContainsKey(target)) return Fail("selected_role_not_on_current_page");
        foreach (var pair in buttons)
        {
            if (pair.Key >= CustomRoles.NotAssigned) continue;
            var button = pair.Value;
            if (!button.Overlay || button.Overlay.enabled != (pair.Key == target) || button.Overlay.sprite != button.CheckImage)
                return Fail("task_role_overlay");
        }
        return true;
    }

    private bool Fail(string label) { Failure = label; state = "failed"; return false; }
    internal void Stop(string stoppedState)
    {
        state = stoppedState;
        try
        {
            ReleaseRejectionOwner();
            if (GameStates.IsFreePlay && owner != null && owner && Minigame.Instance == owner)
            {
                if (rolePaths.ContainsKey(originalRole))
                {
                    Select(originalRole);
                    restored = TOHE.Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId].MainRole == originalRole &&
                        PlayerControl.LocalPlayer.Data.Role.Role == originalRole.GetRoleTypes();
                }
                owner.ForceClose();
            }
        }
        catch (Exception ex) { Failure ??= "cleanup_" + ex.GetType().Name; }
    }
    internal object Result() => new { state, stage, pass, failure = Failure, restored_original_role = restored,
        roles_traversed = visited.Count, pages = pageCount, directories = directoryCount,
        error_delta = SmokeErrorCounter.Count - errorsAtStart, checks };
    private sealed class DirectoryCursor(string[] names)
    {
        internal string[] Names { get; } = names;
        internal int Next;
    }
}
