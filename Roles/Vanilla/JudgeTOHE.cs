using AmongUs.GameOptions;
using TOHE.Modules;

namespace TOHE.Roles.Vanilla;

internal sealed class JudgeTOHE : RoleBase
{
    private const int Id = 32210;
    private static OptionItem taskRequirement;
    private static OptionItem overruleLimit;
    private static OptionItem canTrialMadmate;
    private static OptionItem canTrialRascal;
    private static OptionItem canTrialCharmed;
    private static OptionItem canTrialSidekick;
    private static OptionItem canTrialInfected;
    private static OptionItem canTrialContagious;
    private static OptionItem canTrialCrewKilling;
    private static OptionItem canTrialNeutralB;
    private static OptionItem canTrialNeutralK;
    private static OptionItem canTrialNeutralE;
    private static OptionItem canTrialNeutralC;
    private static OptionItem canTrialNeutralA;
    internal static int TaskRequirementPercentage => taskRequirement.GetInt();

    public override CustomRoles ThisRoleBase => CustomRoles.JudgeVanilla;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.CrewmateVanilla;

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.CrewmateRoles, CustomRoles.JudgeTOHE);
        var parent = Options.CustomRoleSpawnChances[CustomRoles.JudgeTOHE];
        taskRequirement = IntegerOptionItem.Create(Id + 2, "JudgeTaskRequirement", new(0, 100, 5), 50, TabGroup.CrewmateRoles, false)
            .SetParent(parent)
            .SetValueFormat(OptionFormat.Percent);
        overruleLimit = IntegerOptionItem.Create(Id + 3, "JudgeTrialLimitPerGame", new(1, 30, 1), 1, TabGroup.CrewmateRoles, false)
            .SetParent(parent)
            .SetValueFormat(OptionFormat.Times);

        canTrialMadmate = TargetOption(4, "JudgeCanTrialMadmate");
        canTrialRascal = TargetOption(5, "JudgeCanTrialRascal");
        canTrialCharmed = TargetOption(6, "JudgeCanTrialCharmed");
        canTrialSidekick = TargetOption(7, "JudgeCanTrialSidekick");
        canTrialInfected = TargetOption(8, "JudgeCanTrialInfected");
        canTrialContagious = TargetOption(9, "JudgeCanTrialContagious");
        canTrialCrewKilling = TargetOption(10, "JudgeCanTrialnCrewKilling");
        canTrialNeutralB = TargetOption(11, "JudgeCanTrialNeutralB");
        canTrialNeutralK = TargetOption(12, "JudgeCanTrialNeutralK");
        canTrialNeutralE = TargetOption(13, "JudgeCanTrialNeutralE");
        canTrialNeutralC = TargetOption(14, "JudgeCanTrialNeutralC");
        canTrialNeutralA = TargetOption(15, "JudgeCanTrialNeutralA");

        OptionItem TargetOption(int offset, string key) =>
            BooleanOptionItem.Create(Id + offset, key, false, TabGroup.CrewmateRoles, false).SetParent(parent);
    }

    public override void Add(byte playerId) => AbilityLimit = overruleLimit.GetInt();

    public override string GetProgressText(byte playerId, bool comms) =>
        Utils.ColorString(AbilityLimit >= 1 ? Utils.GetRoleColor(CustomRoles.JudgeTOHE) : UnityEngine.Color.gray, $"({AbilityLimit})");

    internal static bool CanOverruleTarget(PlayerControl target)
    {
        var role = target.GetCustomRole();
        return role.IsImpostor()
            || (canTrialMadmate.GetBool() && (role.IsMadmate() || target.Is(CustomRoles.Madmate)))
            || (canTrialRascal.GetBool() && target.Is(CustomRoles.Rascal))
            || (canTrialCharmed.GetBool() && target.Is(CustomRoles.Charmed))
            || (canTrialSidekick.GetBool() && (target.Is(CustomRoles.Sidekick) || target.Is(CustomRoles.Recruit)))
            || (canTrialInfected.GetBool() && target.Is(CustomRoles.Infected))
            || (canTrialContagious.GetBool() && target.Is(CustomRoles.Contagious))
            || (canTrialCrewKilling.GetBool() && role.IsCrewKiller())
            || (canTrialNeutralB.GetBool() && role.IsNB())
            || (canTrialNeutralK.GetBool() && role.IsNK())
            || (canTrialNeutralE.GetBool() && role.IsNE())
            || (canTrialNeutralC.GetBool() && role.IsNC())
            || (canTrialNeutralA.GetBool() && role.IsNA());
    }

    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
        => opt.SetFloat(FloatOptionNames.JudgeTaskRequirementPercentage, taskRequirement.GetFloat());

    public override void AfterMeetingTasks()
    {
        var player = _Player;
        if (AbilityLimit >= 1 || player == null || !player.IsAlive() || player.IsModded()) return;

        var key = (player.PlayerId, player.PlayerId);
        if (!RpcSetRoleReplacer.RoleMap.TryGetValue(key, out var view) || view.roleType != RoleTypes.Judge) return;

        RpcSetRoleReplacer.RoleMap[key] = (RoleTypes.Crewmate, view.customRole);
        new PlayerGameOptionsSender(player).SendGameOptions();
        player.RpcSetRoleDesync(RoleTypes.Crewmate, player.GetClientId());
    }
}
