namespace TOHE;

internal sealed class Cop : RoleBase
{
    public override CustomRoles ThisRoleBase => CustomRoles.Shapeshifter;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.ImpostorKilling;

    public override bool CanUseImpostorVentButton(PlayerControl pc) => false;
    public override bool CanUseSabotage(PlayerControl pc) => false;
    public override void SetKillCooldown(byte id) => CopsAndRobbersManager.CaptureCooldown(Utils.GetPlayerById(id));
    public override void UnShapeShiftButton(PlayerControl shapeshifter) => CopsAndRobbersManager.UnShapeShiftButton(shapeshifter);
}

internal sealed class Robber : RoleBase
{
    public override CustomRoles ThisRoleBase => CustomRoles.Engineer;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.CrewmateBasic;

    public override bool CanUseKillButton(PlayerControl pc) => false;
    public override bool CanUseImpostorVentButton(PlayerControl pc) => false;
    public override bool CanUseSabotage(PlayerControl pc) => false;
}
