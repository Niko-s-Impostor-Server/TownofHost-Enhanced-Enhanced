using AmongUs.GameOptions;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TOHE;

[HarmonyPatch(typeof(TaskAdderGame), nameof(TaskAdderGame.ShowFolder))]
public static class ShowFolderPatch
{
    public const int MaxPageItems = 6;
    private static TaskFolder CustomRolesFolder;
    private static TaskAdderGame owner;
    private static readonly Dictionary<string, CustomRoles[]> Pages = [];
    internal static readonly Dictionary<TaskAddButton, CustomRoles> CustomButtons = [];
    public static bool TryGetCustomRole(TaskAddButton button, out CustomRoles role)
        => CustomButtons.TryGetValue(button, out role);

    internal static void Release(TaskAdderGame instance)
    {
        if (owner != instance) return;
        // Destroy is deferred. Prevent late Start/Update callbacks from treating
        // these custom buttons as native task buttons after their mapping is gone.
        foreach (var button in CustomButtons.Keys)
        {
            if (button == null) continue;
            button.gameObject.SetActive(false);
            if (ControllerManager.Instance != null && button.Button != null)
                ControllerManager.Instance.RemoveSelectableUiElement(button.Button);
        }
        owner = null;
        CustomRolesFolder = null;
        CustomButtons.Clear();
        Pages.Clear();
    }

    internal static void Prefix(TaskAdderGame __instance, [HarmonyArgument(0)] TaskFolder taskFolder)
    {
        if (!GameStates.IsFreePlay || !FreeplayInitialization.IsReady) return;

        // New native abilities need their own mod state/HUD integration. Do not
        // expose a button that changes the native role while retaining a different
        // custom role (the existing Detective/Judge are unrelated mod roles).
        for (int index = taskFolder.RoleChildren.Count - 1; index >= 0; index--)
            if (!AddTaskButtonPatch.TryMapNativeRole(taskFolder.RoleChildren[index].Role, out _))
                taskFolder.RoleChildren.RemoveAt(index);

        // Native ShowFolder destroys old items at end of frame. Retire their controller entries now.
        foreach (var item in __instance.ActiveItems)
        {
            if (item == null) continue;
            foreach (var element in item.GetComponentsInChildren<UiElement>(true))
                ControllerManager.Instance.RemoveSelectableUiElement(element);
        }
        foreach (var button in CustomButtons.Keys)
            if (button != null) button.gameObject.SetActive(false);
        CustomButtons.Clear();
        if (owner != __instance)
        {
            owner = __instance;
            CustomRolesFolder = null;
            Pages.Clear();
        }

        if (__instance.Root == taskFolder && CustomRolesFolder == null)
        {
            TaskFolder rolesFolder = Object.Instantiate(
                __instance.RootFolderPrefab,
                __instance.transform
            );
            rolesFolder.gameObject.SetActive(false);
            rolesFolder.FolderName = Main.ModName;
            CustomRolesFolder = rolesFolder;
            // Keep the mod entry on the first native row even when the ship exposes many task folders.
            __instance.Root.SubFolders.Insert(0, rolesFolder);
            var roles = CustomRolesHelper.AllRoles.Where(role => role != CustomRoles.NotAssigned).ToArray();
            AddPages(__instance, rolesFolder, roles, 0, roles.Length);
        }
    }

    private static void AddPages(TaskAdderGame instance, TaskFolder folder, CustomRoles[] roles, int start, int count)
    {
        folder.SubFolders = new();
        folder.TaskChildren = new();
        folder.RoleChildren = new();
        if (count <= MaxPageItems)
        {
            Pages[folder.FolderName] = roles.Skip(start).Take(count).ToArray();
            return;
        }

        // A bounded native folder tree: directory pages and role pages both contain at most six items.
        int groupSize = MaxPageItems;
        while ((long)groupSize * MaxPageItems < count) groupSize *= MaxPageItems;
        for (int offset = 0; offset < count; offset += groupSize)
        {
            int childCount = Math.Min(groupSize, count - offset);
            var child = Object.Instantiate(instance.RootFolderPrefab, instance.transform);
            child.gameObject.SetActive(false);
            child.FolderName = $"{start + offset + 1:000}-{start + offset + childCount:000}";
            AddPages(instance, child, roles, start + offset, childCount);
            folder.SubFolders.Add(child);
        }
    }

    internal static void Postfix(TaskAdderGame __instance, [HarmonyArgument(0)] TaskFolder taskFolder)
    {
        if (!GameStates.IsFreePlay || !FreeplayInitialization.IsReady) return;

        Logger.Info("Opened " + taskFolder.FolderName, "TaskFolder");
        float xCursor = 0f;
        float yCursor = 0f;
        float maxHeight = 0f;
        if (owner == __instance && Pages.TryGetValue(taskFolder.FolderName, out var roles))
        {
            foreach (var cRole in roles)
            {
                TaskAddButton button = Object.Instantiate(__instance.RoleButton);
                button.Text.text = Utils.GetRoleName(cRole);
                button.Text.enableWordWrapping = false;
                button.Text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                button.Text.ForceMeshUpdate();
                button.SafePositionWorld = __instance.SafePositionWorld;
                __instance.AddFileAsChild(taskFolder, button, ref xCursor, ref yCursor, ref maxHeight);
                CustomButtons[button] = cRole;
                button.Overlay.sprite = button.CheckImage;
                var roleColor = Utils.GetRoleColor(cRole);

                button.FileImage.color = roleColor;
                button.RolloverHandler.OutColor = roleColor;
                button.RolloverHandler.OverColor = new Color(roleColor.r * 0.5f, roleColor.g * 0.5f, roleColor.b * 0.5f);
                if (button.Button != null)
                    ControllerManager.Instance.AddSelectableUiElement(button.Button);
            }
        }
        if (ControllerManager.Instance.CurrentUiState?.MenuName == __instance.name &&
            ControllerManager.Instance.CurrentUiState.CurrentSelection == null && __instance.ActiveItems.Count > 0)
        {
            var first = __instance.ActiveItems[0].GetComponent<UiElement>();
            if (first != null) ControllerManager.Instance.SetCurrentSelected(first);
        }
    }
}

[HarmonyPatch(typeof(TaskAdderGame), nameof(TaskAdderGame.OnDisable))]
public static class TaskAdderClosePatch
{
    public static void Postfix(TaskAdderGame __instance) => ShowFolderPatch.Release(__instance);
}

[HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.Update))]
class TaskAddButtonUpdatePatch
{
    public static bool Prefix(TaskAddButton __instance)
    {
        if (!GameStates.IsFreePlay) return true;

        if (ShowFolderPatch.CustomButtons.TryGetValue(__instance, out var customRole))
        {
            var player = PlayerControl.LocalPlayer;
            __instance.Overlay.enabled = player != null && Main.PlayerStates.TryGetValue(player.PlayerId, out var state)
                && (customRole < CustomRoles.NotAssigned ? state.MainRole == customRole : state.SubRoles.Contains(customRole));
            __instance.Overlay.sprite = __instance.CheckImage;
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.Start))]
class TaskAddButtonStartPatch
{
    public static bool Prefix(TaskAddButton __instance) => TaskAddButtonUpdatePatch.Prefix(__instance);
}
[HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.AddTask))]
class AddTaskButtonPatch
{
    internal static bool TryMapNativeRole(RoleTypes nativeRole, out CustomRoles role)
    {
        role = nativeRole switch
        {
            RoleTypes.Crewmate or RoleTypes.CrewmateGhost => CustomRoles.Crewmate,
            RoleTypes.Impostor or RoleTypes.ImpostorGhost => CustomRoles.Impostor,
            RoleTypes.Engineer => CustomRoles.Engineer,
            RoleTypes.Scientist => CustomRoles.Scientist,
            RoleTypes.GuardianAngel => CustomRoles.GuardianAngel,
            RoleTypes.Shapeshifter => CustomRoles.Shapeshifter,
            RoleTypes.Noisemaker => CustomRoles.Noisemaker,
            RoleTypes.Phantom => CustomRoles.Phantom,
            RoleTypes.Tracker => CustomRoles.Tracker,
            _ => CustomRoles.NotAssigned
        };
        return role != CustomRoles.NotAssigned;
    }

    public static bool Prefix(TaskAddButton __instance, out bool __state)
    {
        __state = false;
        if (!GameStates.IsFreePlay) return true;

        if (ShowFolderPatch.CustomButtons.TryGetValue(__instance, out var customRole))
        {
            var player = PlayerControl.LocalPlayer;
            if (!FreeplayInitialization.IsReady || player == null
                || !Main.PlayerStates.TryGetValue(player.PlayerId, out var state)) return false;
            if (customRole == CustomRoles.NotAssigned) return false;
            if (customRole > CustomRoles.NotAssigned)
            {
                if (state.SubRoles.Contains(customRole)) state.RemoveSubRole(customRole);
                else state.SetSubRole(customRole, player);
                return false;
            }
            // Keep the native role button's task recovery and safe position behavior.
            if (PlayerTask.DestroyTasksOfType<ImportantTextTask>(player)
                && !PlayerTask.PlayerHasTaskOfType<NormalPlayerTask>(player)) ShipStatus.Instance.Begin();
            FreeplayInitialization.ChangeRole(player, customRole, setNativeRole: true);
            player.transform.position = __instance.SafePositionWorld;
            return false;
        }
        if (__instance.Role != null)
        {
            if (!FreeplayInitialization.IsReady || !TryMapNativeRole(__instance.Role.Role, out _)) return false;
            __state = true;
        }
        return true;
    }

    public static void Postfix(TaskAddButton __instance, bool __state)
    {
        if (!__state || !GameStates.IsFreePlay || __instance.Role == null) return;
        if (!TryMapNativeRole(__instance.Role.Role, out var role)) return;
        FreeplayInitialization.ChangeRole(PlayerControl.LocalPlayer, role, setNativeRole: false);
    }
}
