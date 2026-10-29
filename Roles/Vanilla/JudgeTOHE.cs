using AmongUs.GameOptions;
using TOHE.Modules;

namespace TOHE.Roles.Vanilla;

internal sealed class JudgeTOHE : RoleBase
{
    private static OptionItem taskRequirement;
    internal static int TaskRequirementPercentage => taskRequirement.GetInt();

    public override CustomRoles ThisRoleBase => CustomRoles.JudgeVanilla;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.CrewmateVanilla;

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(32210, TabGroup.CrewmateRoles, CustomRoles.JudgeTOHE);
        taskRequirement = IntegerOptionItem.Create(32212, "JudgeTaskRequirement", new(0, 100, 5), 50, TabGroup.CrewmateRoles, false)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.JudgeTOHE])
            .SetValueFormat(OptionFormat.Percent);
    }

    public override void Add(byte playerId) => AbilityLimit = 1;

    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
    {
        AURoleOptions.JudgeTaskRequirementPercentage = taskRequirement.GetFloat();
    }

    public override void AfterMeetingTasks()
    {
        if (AbilityLimit < 1 && _Player && _Player.IsAlive())
            VanillaMeetingRoles.HideExhaustedJudge(_Player);
    }
}
