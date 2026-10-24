using AmongUs.GameOptions;
using static TOHE.Options;

namespace TOHE.Roles._Ghosts_.Crewmate;

internal class SpiritGuideTOHE : RoleBase
{
    private const int Id = 32300;

    public override CustomRoles ThisRoleBase => CustomRoles.SpiritGuide;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.CrewmateVanillaGhosts;

    private static OptionItem AbilityCooldown;

    public override void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.CrewmateRoles, CustomRoles.SpiritGuideTOHE);
        AbilityCooldown = FloatOptionItem.Create(Id + 10, "SpiritGuideBase_Cooldown", new(10f, 120f, 5f), 20f, TabGroup.CrewmateRoles, false)
            .SetParent(CustomRoleSpawnChances[CustomRoles.SpiritGuideTOHE])
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
    {
        opt.SetFloat(FloatOptionNames.SpiritGuideCooldownSeconds, AbilityCooldown.GetFloat());
    }
}
