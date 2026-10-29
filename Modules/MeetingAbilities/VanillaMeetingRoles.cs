using AmongUs.GameOptions;
using TOHE.Modules;

namespace TOHE;

// Only the owner's native view changes. The role matrix and host team stay authoritative.
internal static class VanillaMeetingRoles
{
    private static readonly Dictionary<byte, PlayerControl> presented = [];
    private static readonly Dictionary<byte, int> emergenciesUsed = [];

    internal static int EmergenciesUsed(byte id) => emergenciesUsed.GetValueOrDefault(id);

    internal static void RecordEmergency(PlayerControl reporter)
        => emergenciesUsed[reporter.PlayerId] = EmergenciesUsed(reporter.PlayerId) + 1;

    internal static void Reset()
    {
        presented.Clear();
        emergenciesUsed.Clear();
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
