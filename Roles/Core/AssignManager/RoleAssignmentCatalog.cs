using AmongUs.GameOptions;
using System;
using TOHE.Roles.Double;
using TOHE.Roles.Impostor;
using TOHE.Roles.Neutral;

namespace TOHE.Roles.Core.AssignManager;

internal static class RoleAssignmentCatalog
{
    internal static AssignmentTeam TeamOf(CustomRoles role) => role switch
    {
        CustomRoles.GM => AssignmentTeam.None,
        CustomRoles.EvilMini => AssignmentTeam.Impostor,
        CustomRoles.NiceMini => AssignmentTeam.Crewmate,
        _ when role.IsImpostor() => AssignmentTeam.Impostor,
        _ when role.IsNK() => AssignmentTeam.NeutralKilling,
        _ when role.IsNA() => AssignmentTeam.NeutralApocalypse,
        _ when role.IsNonNK() => AssignmentTeam.NonKillingNeutral,
        _ => AssignmentTeam.Crewmate
    };

    internal static RoleAssignmentRequest Capture(int seed, PlayerControl[] players)
    {
        if (BanManager.CheckEACList(PlayerControl.LocalPlayer.FriendCode, PlayerControl.LocalPlayer.GetClient().GetHashedPuid()))
            Main.EnableGM.Value = true;
        var roster = players.Select(pc =>
        {
            CustomRoles? preset = null;
            if ((pc.AmOwner && Main.EnableGM.Value) || TOHE.Modules.LocalPlayerTags.IsDesignatedGameMaster(pc))
                preset = CustomRoles.GM;
            else if (RoleAssign.SetRoles.TryGetValue(pc.PlayerId, out var role) && IsValidPreset(role)) preset = role;
            return new RoleAssignmentPlayer(pc.PlayerId, preset, preset.HasValue ? TeamOf(preset.Value) : AssignmentTeam.None);
        }).ToArray();

        Mini.SelectVariant(new AssignmentRandom(seed, AssignmentStreams.Mini));
        // Mini has one shared variant in the gameplay implementation.
        if (roster.Any(p => p.Preset == CustomRoles.EvilMini)) Mini.IsEvilMini = true;
        else if (roster.Any(p => p.Preset == CustomRoles.NiceMini)) Mini.IsEvilMini = false;
        roster = roster.Select(p => p.Preset == CustomRoles.Mini
            ? new RoleAssignmentPlayer(p.PlayerId, Mini.IsEvilMini ? CustomRoles.EvilMini : CustomRoles.NiceMini,
                Mini.IsEvilMini ? AssignmentTeam.Impostor : AssignmentTeam.Crewmate) : p).ToArray();

        var candidates = new List<RoleCandidate>();
        foreach (var role in CustomRolesHelper.AllRoles.OrderBy(role => role))
        {
            int chance = role.GetMode();
            if (chance <= 0 || role.IsVanilla() || role.IsAdditionRole() || role.IsGhostRole()) continue;
            if (role is CustomRoles.VengefulRomantic or CustomRoles.RuthlessRomantic or CustomRoles.GM
                or CustomRoles.NotAssigned or CustomRoles.NiceMini or CustomRoles.EvilMini) continue;
            if (role == CustomRoles.Stalker && GameStates.FungleIsActive) continue;
            if (role == CustomRoles.Doctor && Options.EveryoneCanSeeDeathReason.GetBool()) continue;
            var resolved = role == CustomRoles.Mini
                ? (Mini.IsEvilMini ? CustomRoles.EvilMini : CustomRoles.NiceMini) : role;
            candidates.Add(new(resolved, TeamOf(resolved), chance, role.GetCount()));
        }

        var random = new AssignmentRandom(seed, AssignmentStreams.NeutralCounts);
        int Count(OptionItem min, OptionItem max)
        {
            int lower = Math.Max(0, min.GetInt()), upper = Math.Max(0, max.GetInt());
            return upper < lower ? 0 : lower + random.Next(upper - lower + 1);
        }
        var quotas = new Dictionary<AssignmentTeam, int>
        {
            [AssignmentTeam.Impostor] = Main.RealOptionsData.GetInt(Int32OptionNames.NumImpostors),
            [AssignmentTeam.NonKillingNeutral] = Count(Options.NonNeutralKillingRolesMinPlayer, Options.NonNeutralKillingRolesMaxPlayer),
            [AssignmentTeam.NeutralKilling] = Count(Options.NeutralKillingRolesMinPlayer, Options.NeutralKillingRolesMaxPlayer),
            [AssignmentTeam.NeutralApocalypse] = Count(Options.NeutralApocalypseRolesMinPlayer, Options.NeutralApocalypseRolesMaxPlayer)
        };
        return new()
        {
            Seed = seed,
            Players = roster,
            Candidates = candidates.ToArray(),
            Quotas = quotas,
            Replacements = Options.DisableHiddenRoles.GetBool() ? [] :
            [new(CustomRoles.Jester, CustomRoles.Sunnyboy, Jester.SunnyboyChance.GetInt()),
             new(CustomRoles.Arrogance, CustomRoles.Bard, Arrogance.BardChance.GetInt())]
        };
    }

    private static bool IsValidPreset(CustomRoles role) => Enum.IsDefined(typeof(CustomRoles), role)
        && role != CustomRoles.NotAssigned && !role.IsAdditionRole() && !role.IsGhostRole();
}
