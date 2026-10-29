using AmongUs.GameOptions;
using TOHE.Modules;
using TOHE.Roles.Core;

namespace TOHE;

// Only the owner's native view changes. The role matrix and host team stay authoritative.
internal static class VanillaMeetingRoles
{
    private static readonly Dictionary<byte, PlayerControl> presented = [];
    private static readonly Dictionary<byte, int> manualEmergenciesUsed = [];

    internal static int ManualEmergenciesUsed(byte id) => manualEmergenciesUsed.GetValueOrDefault(id);

    internal static int EmergenciesRemaining(byte id) => System.Math.Max(0,
        Main.RealOptionsData.GetInt(Int32OptionNames.NumEmergencyMeetings) - ManualEmergenciesUsed(id));

    internal static bool CanCallEmergency(PlayerControl origin) => origin && EmergenciesRemaining(origin.PlayerId) > 0;

    internal static void RecordManualEmergency(PlayerControl origin)
        => manualEmergenciesUsed[origin.PlayerId] = ManualEmergenciesUsed(origin.PlayerId) + 1;

    internal static void ForgetDisconnected(byte id)
    {
        manualEmergenciesUsed.Remove(id);
    }

    internal static void SyncEmergencyView(PlayerControl reporter)
    {
        if (!reporter || reporter.Data == null || reporter.Data.IsDead || reporter.Data.Disconnected) return;
        if (!RpcSetRoleReplacer.RoleMap.TryGetValue((reporter.PlayerId, reporter.PlayerId), out var view)) return;
        new PlayerGameOptionsSender(reporter).SendGameOptions();
        reporter.RpcSetRoleDesync(view.roleType, reporter.GetClientId());
    }

    internal static void Reset()
    {
        presented.Clear();
        manualEmergenciesUsed.Clear();
    }

    internal static void HideExhaustedJudge(PlayerControl actor)
    {
        var key = (actor.PlayerId, actor.PlayerId);
        if (!RpcSetRoleReplacer.RoleMap.TryGetValue(key, out var view) || view.roleType != RoleTypes.Judge) return;
        RpcSetRoleReplacer.RoleMap[key] = (RoleTypes.Crewmate, view.customRole);
        new PlayerGameOptionsSender(actor).SendGameOptions();
        actor.RpcSetRoleDesync(RoleTypes.Crewmate, actor.GetClientId());
    }

    internal static void RefreshBorrowedRoles()
    {
        foreach (var actor in Main.AllAlivePlayerControls)
        {
            if (!MeetingAbilities.NativeButtonConfigured(actor) || actor.Is(Custom_Team.Impostor)) continue;
            var key = (actor.PlayerId, actor.PlayerId);
            if (!RpcSetRoleReplacer.RoleMap.TryGetValue(key, out var view)) continue;
            var role = ((IMeetingTargetAbility)actor.GetRoleClass()).CanUseMeetingAbility(actor)
                ? RoleTypes.Judge : RoleTypes.Crewmate;
            if (view.roleType == role) continue;
            RpcSetRoleReplacer.RoleMap[key] = (role, view.customRole);
            new PlayerGameOptionsSender(actor).SendGameOptions();
            actor.RpcSetRoleDesync(role, actor.GetClientId());
        }
    }

    internal static void Begin()
    {
        foreach (var actor in Main.AllAlivePlayerControls)
        {
            if (!MeetingAbilities.NeedsTemporaryRole(actor)) continue;
            presented[actor.PlayerId] = actor;
            // CoSetRole resets RemainingEmergencies from LogicOptions. Publish
            // the remaining allowance first, then install the temporary role.
            new PlayerGameOptionsSender(actor).SendGameOptions();
            actor.RpcSetRoleDesync(RoleTypes.Judge, actor.GetClientId());
        }
    }

    internal static void Forget(PlayerControl actor) => presented.Remove(actor.PlayerId);

    internal static void RestoreAll()
    {
        foreach (var actor in presented.Values.ToArray())
        {
            if (!actor || actor.Data == null || actor.Data.Disconnected) continue;
            var role = actor.IsAlive()
                ? RpcSetRoleReplacer.RoleMap.GetValueOrDefault((actor.PlayerId, actor.PlayerId),
                    (actor.GetCustomRole().GetRoleTypes(), actor.GetCustomRole())).Item1
                : GhostRoleView.ForRecipient(actor, actor);
            new PlayerGameOptionsSender(actor).SendGameOptions();
            actor.RpcSetRoleDesync(role, actor.GetClientId());
        }
        presented.Clear();
    }
}
