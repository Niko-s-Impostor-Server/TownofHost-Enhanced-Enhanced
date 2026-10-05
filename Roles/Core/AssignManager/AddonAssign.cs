using System;

namespace TOHE.Roles.Core.AssignManager;

public static class AddonAssign
{
    public static void Assign()
    {
        Main.LoversPlayers.Clear();
        Main.isLoversDead = false;
        if (Options.CurrentGameMode == CustomGameMode.FFA) return;
        int seed = RoundAssignment.CurrentSeed ?? throw new InvalidOperationException("Role assignment must start before addons.");
        var players = RoundAssignment.PlayerIds.Select(id => Utils.GetPlayerById(id))
            .Where(pc => pc != null && pc.Data != null && !pc.Data.Disconnected && pc.IsAlive())
            .ToDictionary(pc => pc.PlayerId);

        RoundAssignment.AddonInput = AddonAssignmentCatalog.Capture();
        RoundAssignment.AddonDecisions = AddonAssignmentEngine.Assign(seed, RoundAssignment.AddonInput, players.Keys,
            (role, id) => role == CustomRoles.Lovers ? LoversAssignment.CanAssign(players[id])
                : AddonEligibility.CanAssign(role, players[id], initialAssignment: true),
            id => Main.PlayerStates[id].SubRoles.Count,
            (role, id) =>
            {
                Main.PlayerStates[id].SetSubRole(role);
                if (role == CustomRoles.Lovers) Main.LoversPlayers.Add(players[id]);
                Logger.Info($"PlayerId={id} => {role}", "AddonAssign");
            });
        if (Main.LoversPlayers.Count > 0) RPC.SyncLoversPlayers();
        foreach (var decision in RoundAssignment.AddonDecisions)
            Logger.Info($"{decision.Role}: spawned={decision.Spawned}, eligible={decision.EligiblePlayers.Length}, assigned={decision.AssignedPlayers.Length}", "AddonAssign");
    }
}
