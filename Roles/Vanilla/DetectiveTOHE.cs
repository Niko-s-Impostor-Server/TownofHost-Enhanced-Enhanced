using AmongUs.GameOptions;

namespace TOHE.Roles.Vanilla;

internal sealed class DetectiveTOHE : RoleBase
{
    private static OptionItem suspectLimit;
    public override CustomRoles ThisRoleBase => CustomRoles.DetectiveVanilla;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.CrewmateVanilla;

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(32200, TabGroup.CrewmateRoles, CustomRoles.DetectiveTOHE);
        suspectLimit = IntegerOptionItem.Create(32202, "DetectiveSuspectLimit", new(2, 4, 1), 2, TabGroup.CrewmateRoles, false)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.DetectiveTOHE]).SetValueFormat(OptionFormat.Players);
    }

    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
        => opt.SetFloat(FloatOptionNames.DetectiveSuspectLimit, suspectLimit.GetFloat());
}
