namespace TOHE.Roles.Core.AssignManager;

internal static class LoversAssignment
{
    private static readonly HashSet<CustomRoles> ExcludedRoles =
    [
        CustomRoles.GM, CustomRoles.Dictator, CustomRoles.God, CustomRoles.Hater,
        CustomRoles.Sunnyboy, CustomRoles.Bomber, CustomRoles.Provocateur,
        CustomRoles.RuthlessRomantic, CustomRoles.Romantic, CustomRoles.VengefulRomantic,
        CustomRoles.Workaholic, CustomRoles.Solsticer, CustomRoles.Mini,
        CustomRoles.NiceMini, CustomRoles.EvilMini
    ];

    internal static bool CanAssign(PlayerControl player)
    {
        var role = player.GetCustomRole();
        return !ExcludedRoles.Contains(role) && !player.Is(CustomRoles.Lovers)
            && player.GetCustomSubRoles().Count < Options.NoLimitAddonsNumMax.GetInt()
            && (!role.IsCrewmate() || Options.CrewCanBeInLove.GetBool())
            && (!role.IsNeutral() || Options.NeutralCanBeInLove.GetBool())
            && (!role.IsImpostor() || Options.ImpCanBeInLove.GetBool());
    }
}
