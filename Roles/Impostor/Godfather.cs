using TOHE.Roles.Core;
using static TOHE.Translator;
using static TOHE.Utils;

namespace TOHE.Roles.Impostor;

internal class Godfather : RoleBase, IMeetingTargetAbility
{
    //===========================SETUP================================\\
    private const int Id = 3400;
    private static readonly HashSet<byte> PlayerIds = [];
    public static bool HasEnabled => PlayerIds.Any();

    public override CustomRoles ThisRoleBase => CustomRoles.Impostor;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.ImpostorSupport;
    //==================================================================\\

    private static OptionItem GodfatherChangeOpt;
    private static OptionItem VanillaMeetingAbilityTriggerOpt;

    private static readonly HashSet<byte> GodfatherTarget = [];
    private bool Didvote = false;
    public MeetingAbilityTrigger Trigger => (MeetingAbilityTrigger)VanillaMeetingAbilityTriggerOpt.GetValue();

    private enum GodfatherChangeModeList
    {
        GodfatherCount_Refugee,
        GodfatherCount_Madmate
    }

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.ImpostorRoles, CustomRoles.Godfather);
        GodfatherChangeOpt = StringOptionItem.Create(Id + 2, "GodfatherTargetCountMode", EnumHelper.GetAllNames<GodfatherChangeModeList>(), 0, TabGroup.ImpostorRoles, false)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Godfather]);
        VanillaMeetingAbilityTriggerOpt = MeetingAbilities.CreateTriggerOption(Id + 3, CustomRoles.Godfather, TabGroup.ImpostorRoles);
    }

    public override void Init()
    {
        PlayerIds.Clear();
        GodfatherTarget.Clear();
    }
    public override void Add(byte playerId)
    {
        PlayerIds.Add(playerId);

        if (AmongUsClient.Instance.AmHost)
        {
            CustomRoleManager.CheckDeadBodyOthers.Add(CheckDeadBody);
        }
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => GodfatherTarget.Clear();
    private void CheckDeadBody(PlayerControl killer, PlayerControl target, bool inMeeting)
    {
        if (GodfatherTarget.Contains(target.PlayerId) && !(killer.GetCustomRole().IsImpostor() || killer.GetCustomRole().IsMadmate() || killer.Is(CustomRoles.Madmate)))
        {
            if (GodfatherChangeOpt.GetValue() == 0) killer.RpcSetCustomRole(CustomRoles.Refugee);
            else killer.RpcSetCustomRole(CustomRoles.Madmate);
        }
    }
    public override void AfterMeetingTasks() => Didvote = false;

    public bool CanUseMeetingAbility(PlayerControl actor) => actor && actor.Is(CustomRoles.Godfather) && !Didvote;

    public bool CanTargetMeetingAbility(PlayerControl actor, PlayerControl target) => target && target.IsAlive();

    public bool UseMeetingAbility(PlayerControl actor, PlayerControl target)
    {
        Didvote = true;
        GodfatherTarget.Add(target.PlayerId);
        if (!MeetingAbilities.UsesSeparateAbility(actor))
            SendMessage(GetString("VoteHasReturned"), actor.PlayerId, title: ColorString(GetRoleColor(CustomRoles.Godfather), string.Format(GetString("VoteAbilityUsed"), GetString("Godfather"))));
        return true;
    }

    public override bool CheckVote(PlayerControl voter, PlayerControl target) => !MeetingAbilities.TryUse(voter, target);
}
