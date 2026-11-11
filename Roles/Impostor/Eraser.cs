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

    private static readonly HashSet<byte> didVote = [];
    private static readonly HashSet<byte> PlayerToErase = [];
    private static readonly HashSet<byte> PlayerToNotify = [];
    public static readonly Dictionary<byte, CustomRoles> ErasedRoleStorage = [];

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.ImpostorRoles, CustomRoles.Eraser);
        EraseLimitOpt = IntegerOptionItem.Create(Id + 10, "EraseLimit", new(1, 15, 1), 2, TabGroup.ImpostorRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Eraser])
            .SetValueFormat(OptionFormat.Times);
    }
    public override void Init()
    {
        PlayerToErase.Clear();
        PlayerToNotify.Clear();
        didVote.Clear();
        ErasedRoleStorage.Clear();
    }
    public override void Add(byte playerId)
    {
        AbilityLimit = EraseLimitOpt.GetInt();
    }
    public override string GetProgressText(byte playerId, bool comms)
        => Utils.ColorString(AbilityLimit >= 1 ? Utils.GetRoleColor(CustomRoles.Eraser) : Color.gray, $"({AbilityLimit})");

    public bool CanUseMeetingAbility(PlayerControl actor) => actor && actor.Is(CustomRoles.Eraser) &&
        AbilityLimit >= 1 && !didVote.Contains(actor.PlayerId);

    public bool CanTargetMeetingAbility(PlayerControl actor, PlayerControl target) => actor && target &&
        actor != target && target.IsAlive() && !target.Is(CustomRoles.Eraser) &&
        !target.GetCustomRole().IsTasklessCrewmate() && !target.GetCustomRole().IsNeutral() &&
        !Main.TasklessCrewmate.Contains(target.PlayerId) && !CopyCat.playerIdList.Contains(target.PlayerId) &&
        !target.Is(CustomRoles.Stubborn);

    public bool UseMeetingAbility(PlayerControl actor, PlayerControl target) =>
        CanUseMeetingAbility(actor) && CanTargetMeetingAbility(actor, target) && !CheckVote(actor, target);

    public override bool CheckVote(PlayerControl player, PlayerControl target)
    {
        if (!HasEnabled) return true;
        if (player == null || target == null) return true;
        if (target.Is(CustomRoles.Eraser)) return true;
        if (AbilityLimit < 1) return true;

        if (didVote.Contains(player.PlayerId)) return true;
        didVote.Add(player.PlayerId);

        Logger.Info($"{player.GetCustomRole()} votes for {target.GetCustomRole()}", "Vote Eraser");

        if (target.PlayerId == player.PlayerId)
        {
            Utils.SendMessage(GetString("EraserEraseSelf"), player.PlayerId, Utils.ColorString(Utils.GetRoleColor(CustomRoles.Eraser), GetString("EraserEraseMsgTitle")));
            return true;
        }

        var targetRole = target.GetCustomRole();
        if (targetRole.IsTasklessCrewmate() || targetRole.IsNeutral() || Main.TasklessCrewmate.Contains(target.PlayerId) || CopyCat.playerIdList.Contains(target.PlayerId) || target.Is(CustomRoles.Stubborn))
        {
            Logger.Info($"Cannot erase role because is Impostor Based or Neutral or ect", "Eraser");
            Utils.SendMessage(string.Format(GetString("EraserEraseBaseImpostorOrNeutralRoleNotice"), target.GetRealName()), player.PlayerId, Utils.ColorString(Utils.GetRoleColor(CustomRoles.Eraser), GetString("EraserEraseMsgTitle")));
            return true;
        }

        AbilityLimit--;
        SendSkillRPC();

        if (!PlayerToErase.Contains(target.PlayerId))
            PlayerToErase.Add(target.PlayerId);

        Utils.SendMessage(string.Format(GetString("EraserEraseNotice"), target.GetRealName()), player.PlayerId, Utils.ColorString(Utils.GetRoleColor(CustomRoles.Eraser), GetString("EraserEraseMsgTitle")));

        Utils.NotifyRoles(SpecifySeer: player);
        return false;
    }
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
        didVote.Clear();
        PlayerToErase.Clear();
        PlayerToNotify.Clear();
    }
    public override void NotifyAfterMeeting()
    {
        foreach (var pc in PlayerToNotify)
        {
            var player = Utils.GetPlayerById(pc);
            if (player == null || player.Data == null || player.Data.Disconnected) continue;

            player.RPCPlayCustomSound("Oiiai");
            player.Notify(GetString("LostRoleByEraser"));
        }
        PlayerToNotify.Clear();
    }
    public override void AfterMeetingTasks()
    {
        if (PlayerToErase.Count == 0) return;
        foreach (var pc in PlayerToErase.ToArray())
        {
            var player = Utils.GetPlayerById(pc);
            if (player == null || player.Data == null || player.Data.Disconnected) continue;
            if (player.HasGhostRole())
            {
                Logger.Info($"Canceled {player.GetNameWithRole()} because player have ghost role", "Eraser");
                continue;
            }
            var oldRole = player.GetCustomRole();
            if (!ErasedRoleStorage.TryAdd(player.PlayerId, oldRole))
            {
                Logger.Info($"Skipped {player.GetNameWithRole()} because their role was already erased.", "Eraser");
                continue;
            }

            var erasedRole = oldRole == CustomRoles.JudgeTOHE
                ? CustomRoles.CrewmateTOHE
                : GetErasedRole(oldRole.GetRoleTypes(), oldRole);
            if (erasedRole != oldRole)
            {
                if (oldRole == CustomRoles.JudgeTOHE)
                {
                    if (player.Data.IsDead || !player.IsAlive())
                    {
                        // Installing a living role on a dead player would revive them.
                        foreach (var seer in Main.AllPlayerControls)
                        {
                            var key = (seer.PlayerId, player.PlayerId);
                            if (!RpcSetRoleReplacer.RoleMap.TryGetValue(key, out var view)) continue;
                            var roleType = view.roleType == RoleTypes.Judge ? RoleTypes.Crewmate : view.roleType;
                            RpcSetRoleReplacer.RoleMap[key] = (roleType, erasedRole);
                        }
                        player.Data.RoleWhenAlive = new Il2CppSystem.Nullable<RoleTypes>(RoleTypes.Crewmate);
                        player.Data.MarkDirty();
                    }
                    else player.RpcChangeRoleBasis(erasedRole);
                }

                player.GetRoleClass()?.OnRemove(player.PlayerId);
                player.RpcSetCustomRole(erasedRole);
                player.GetRoleClass()?.OnAdd(player.PlayerId);
            }
            PlayerToNotify.Add(player.PlayerId);
            player.ResetKillCooldown();
            player.SetKillCooldown();
            Logger.Info($"{player.GetNameWithRole()} Erase by Eraser", "Eraser");
        }
        PlayerToErase.Clear();
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
