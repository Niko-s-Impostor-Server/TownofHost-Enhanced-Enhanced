using AmongUs.GameOptions;

namespace TOHE.Roles.Vanilla;

internal sealed class ViperTOHE : RoleBase
{
    private static OptionItem dissolveTime;
    public override CustomRoles ThisRoleBase => CustomRoles.Viper;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.ImpostorVanilla;

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(35000, TabGroup.ImpostorRoles, CustomRoles.ViperTOHE);
        dissolveTime = IntegerOptionItem.Create(35002, "ViperDissolveTime", new(1, 180, 1), 15, TabGroup.ImpostorRoles, false)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.ViperTOHE]).SetValueFormat(OptionFormat.Seconds);
    }

    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
        => opt.SetFloat(FloatOptionNames.ViperDissolveTime, dissolveTime.GetFloat());
}
