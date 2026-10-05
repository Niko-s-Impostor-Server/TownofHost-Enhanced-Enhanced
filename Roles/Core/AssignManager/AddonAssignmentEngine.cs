using System;

namespace TOHE.Roles.Core.AssignManager;

public enum AddonAssignmentStage { Lovers, Alignment, Guesser, Ordinary }
public sealed record AddonCandidate(CustomRoles Role, int Chance, int Count,
    AddonAssignmentStage Stage = AddonAssignmentStage.Ordinary, int MinimumCount = 1);
public sealed record AddonAssignmentDecision(CustomRoles Role, bool Spawned, byte[] EligiblePlayers, byte[] AssignedPlayers);

// Runtime eligibility and application are explicit callbacks. Re-evaluate after each
// add-on: Guesser, conversion and capacity change the eligibility of later add-ons.
public static class AddonAssignmentEngine
{
    public static AddonAssignmentDecision[] Assign(int seed, IEnumerable<AddonCandidate> catalog,
        IEnumerable<byte> roster, Func<CustomRoles, byte, bool> canAssign,
        Func<byte, int> addonCount, Action<CustomRoles, byte> apply)
    {
        var players = roster.OrderBy(id => id).ToArray();
        if (players.Distinct().Count() != players.Length) throw new ArgumentException("Duplicate PlayerId in addon roster.");
        var candidates = catalog.OrderBy(c => c.Role).ToList();
        if (candidates.Select(c => c.Role).Distinct().Count() != candidates.Count)
            throw new ArgumentException("Duplicate addon in assignment catalog.");
        new AssignmentRandom(seed, AssignmentStreams.AddonOrder).Shuffle(candidates);
        // Stable sorting retains the seeded tie order; prerequisites precede dependants.
        candidates = candidates.OrderBy(c => c.Stage).ThenByDescending(c => c.Chance >= 90).ToList();
        var decisions = new List<AddonAssignmentDecision>();
        foreach (var candidate in candidates)
        {
            bool spawned = candidate.Count > 0
                && new AssignmentRandom(seed, AssignmentStreams.AddonChance + (uint)candidate.Role).Roll(candidate.Chance);
            var eligible = spawned ? players.Where(id => canAssign(candidate.Role, id)).ToArray() : [];
            var targets = eligible.ToList();
            new AssignmentRandom(seed, AssignmentStreams.AddonPlayers + (uint)candidate.Role).Shuffle(targets);
            // Spread limited slots across players; randomized ties remain unbiased.
            targets = targets.OrderBy(addonCount).Take(Math.Max(0, candidate.Count)).ToList();
            if (targets.Count < Math.Max(1, candidate.MinimumCount)) targets.Clear();
            foreach (byte id in targets) apply(candidate.Role, id);
            decisions.Add(new(candidate.Role, spawned, eligible, targets.ToArray()));
        }
        return decisions.ToArray();
    }
}
