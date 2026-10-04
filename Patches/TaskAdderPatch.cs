using AmongUs.GameOptions;
using UnityEngine;

namespace TOHE;

[HarmonyPatch(typeof(TaskAdderGame), nameof(TaskAdderGame.ShowFolder))]
class ShowFolderPatch
{
    private static TaskFolder CustomRolesFolder;
    private static TaskAdderGame owner;
    internal static readonly Dictionary<TaskAddButton, CustomRoles> CustomButtons = [];
    public static void Prefix(TaskAdderGame __instance, [HarmonyArgument(0)] TaskFolder taskFolder)
    {
        if (GameStates.IsHideNSeek) return;

        foreach (var button in CustomButtons.Keys)
            if (button != null) button.gameObject.SetActive(false);
        CustomButtons.Clear();
        if (owner != __instance)
        {
            owner = __instance;
            CustomRolesFolder = null;
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
            __instance.Root.SubFolders.Add(rolesFolder);
        }
    }
    public static void Postfix(TaskAdderGame __instance, [HarmonyArgument(0)] TaskFolder taskFolder)
    {
        if (GameStates.IsHideNSeek) return;

        Logger.Info("Opened " + taskFolder.FolderName, "TaskFolder");
        float xCursor = 0f;
        float yCursor = 0f;
        float maxHeight = 0f;
        if (CustomRolesFolder != null && CustomRolesFolder.FolderName == taskFolder.FolderName)
        {
            foreach (var cRole in CustomRolesHelper.AllRoles)
            {
                /*if(cRole == CustomRoles.Crewmate ||
                cRole == CustomRoles.Impostor ||
                cRole == CustomRoles.Scientist ||
                cRole == CustomRoles.Engineer ||
                cRole == CustomRoles.GuardianAngel ||
                cRole == CustomRoles.Shapeshifter
                ) continue;*/

                TaskAddButton button = Object.Instantiate(__instance.RoleButton);
                button.Text.text = Utils.GetRoleName(cRole);
                button.SafePositionWorld = __instance.SafePositionWorld;
                __instance.AddFileAsChild(CustomRolesFolder, button, ref xCursor, ref yCursor, ref maxHeight);
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
    }
}

[HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.Update))]
class TaskAddButtonUpdatePatch
{
    public static bool Prefix(TaskAddButton __instance)
    {
        if (GameStates.IsHideNSeek) return true;

        if (ShowFolderPatch.CustomButtons.TryGetValue(__instance, out var customRole))
        {
            __instance.Overlay.enabled = PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.GetCustomRole() == customRole;
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
    public static bool Prefix(TaskAddButton __instance)
    {
        if (GameStates.IsHideNSeek) return true;

        if (ShowFolderPatch.CustomButtons.TryGetValue(__instance, out var customRole))
        {
            if (PlayerControl.LocalPlayer == null) return false;
            PlayerControl.LocalPlayer.RpcSetCustomRole(customRole);
            PlayerControl.LocalPlayer.RpcSetRole(customRole.GetRoleTypes(), true);
            return false;
        }
        return true;
    }
}
