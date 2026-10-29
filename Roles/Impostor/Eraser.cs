using AmongUs.GameOptions;
using TOHE.Modules;
using TOHE.Roles.Core;
using TOHE.Roles.Crewmate;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE.Roles.Impostor;

internal class Eraser : RoleBase, IMeetingTargetAbility
{
    //===========================SETUP================================\\
    private const int Id = 24200;
    public static bool HasEnabled => CustomRoleManager.HasEnabled(CustomRoles.Eraser);
    public override CustomRoles ThisRoleBase => CustomRoles.Impostor;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.ImpostorHindering;
    //==================================================================\\

    private static OptionItem EraseLimitOpt;
    private static OptionItem VanillaMeetingAbilityTriggerOpt;

    private static readonly HashSet<byte> didVote = [];
    private static readonly HashSet<byte> PlayerToErase = [];
    private static int TempEraseLimit;
    public static readonly Dictionary<byte, CustomRoles> ErasedRoleStorage = [];
    public MeetingAbilityTrigger Trigger => (MeetingAbilityTrigger)VanillaMeetingAbilityTriggerOpt.GetValue();

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.ImpostorRoles, CustomRoles.Eraser);
        EraseLimitOpt = IntegerOptionItem.Create(Id + 10, "EraseLimit", new(1, 15, 1), 2, TabGroup.ImpostorRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Eraser])
            .SetValueFormat(OptionFormat.Times);
        VanillaMeetingAbilityTriggerOpt = MeetingAbilities.CreateTriggerOption(Id + 11, CustomRoles.Eraser, TabGroup.ImpostorRoles);
    }
    public override void Init()
    {
        PlayerToErase.Clear();
        didVote.Clear();
        ErasedRoleStorage.Clear();
    }
    public override void Add(byte playerId)
    {
        AbilityLimit = EraseLimitOpt.GetInt();
    }
    public override string GetProgressText(byte playerId, bool comms)
        => Utils.ColorString(AbilityLimit >= 1 ? Utils.GetRoleColor(CustomRoles.Eraser) : Color.gray, $"({AbilityLimit})");

    public bool CanUseMeetingAbility(PlayerControl actor) => HasEnabled && actor && actor.Is(CustomRoles.Eraser) &&
        AbilityLimit >= 1 && !didVote.Contains(actor.PlayerId);

    public bool CanTargetMeetingAbility(PlayerControl actor, PlayerControl target) => actor && target &&
        actor != target && target.IsAlive() && !target.Is(CustomRoles.Eraser) &&
        !target.GetCustomRole().IsTasklessCrewmate() && !target.GetCustomRole().IsNeutral() &&
        !Main.TasklessCrewmate.Contains(target.PlayerId) && !CopyCat.playerIdList.Contains(target.PlayerId) &&
        !target.Is(CustomRoles.Stubborn);

    public bool UseMeetingAbility(PlayerControl player, PlayerControl target)
    {
        didVote.Add(player.PlayerId);

        Logger.Info($"{player.GetCustomRole()} votes for {target.GetCustomRole()}", "Vote Eraser");
        AbilityLimit--;
        SendSkillRPC();

        if (!PlayerToErase.Contains(target.PlayerId))
            PlayerToErase.Add(target.PlayerId);

        Utils.SendMessage(string.Format(GetString("EraserEraseNotice"), target.GetRealName()), player.PlayerId, Utils.ColorString(Utils.GetRoleColor(CustomRoles.Eraser), GetString("EraserEraseMsgTitle")));

        Utils.NotifyRoles(SpecifySeer: player);
        return true;
    }
    public override bool CheckVote(PlayerControl voter, PlayerControl target) => !MeetingAbilities.TryUse(voter, target);
    public override string GetGuessRoleError(PlayerControl guesser, PlayerControl target, CustomRoles role)
    {
        if (PlayerToErase.Contains(target.PlayerId) && !role.IsAdditionRole())
        {
            return "EraserTryingGuessErasedPlayer";
        }
        return null;
    }
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        TempEraseLimit = (int)AbilityLimit;
        didVote.Clear();
    }
    public override void NotifyAfterMeeting()
    {
        foreach (var pc in PlayerToErase.ToArray())
        {
            var player = Utils.GetPlayerById(pc);
            if (player == null) continue;

            player.RPCPlayCustomSound("Oiiai");
            player.Notify(GetString("LostRoleByEraser"));
        }
    }
    public override void AfterMeetingTasks()
    {
        foreach (var pc in PlayerToErase.ToArray())
        {
            var player = Utils.GetPlayerById(pc);
            if (player == null) continue;
            if (!ErasedRoleStorage.ContainsKey(player.PlayerId))
            {
                ErasedRoleStorage.Add(player.PlayerId, player.GetCustomRole());
                Logger.Info($"Added {player.GetNameWithRole()} to ErasedRoleStorage", "Eraser");
            }
            else
            {
                Logger.Info($"Canceled {player.GetNameWithRole()} Eraser bcz already erased.", "Eraser");
                return;
            }
            if (player.HasGhostRole())
            {
                Logger.Info($"Canceled {player.GetNameWithRole()} because player have ghost role", "Eraser");
                return;
            }
            var erasedRole = GetErasedRole(player.GetCustomRole().GetRoleTypes(), player.GetCustomRole());
            if (erasedRole != player.GetCustomRole() &&
                RpcSetRoleReplacer.RoleMap.TryGetValue((player.PlayerId, player.PlayerId), out var selfView) &&
                selfView.roleType == RoleTypes.Judge)
                player.RpcChangeRoleBasis(erasedRole);
            player.RpcSetCustomRole(erasedRole);
            player.ResetKillCooldown();
            player.SetKillCooldown();
            Logger.Info($"{player.GetNameWithRole()} Erase by Eraser", "Eraser");
        }
        Utils.MarkEveryoneDirtySettings();
    }

    // Erased RoleType - Impostor, Shapeshifter, Crewmate, Engineer, Scientist (Not Neutrals)
    public static CustomRoles GetErasedRole(RoleTypes roleType, CustomRoles role)
    {
        return role.IsVanilla()
            ? role
            : roleType switch
            {
                RoleTypes.Crewmate => CustomRoles.CrewmateTOHE,
                RoleTypes.Scientist => CustomRoles.ScientistTOHE,
                RoleTypes.Engineer => CustomRoles.EngineerTOHE,
                RoleTypes.Impostor => CustomRoles.ImpostorTOHE,
                RoleTypes.Shapeshifter => CustomRoles.ShapeshifterTOHE,
                _ => role,
            };
    }
}
