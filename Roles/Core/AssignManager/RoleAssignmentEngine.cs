using System;

namespace TOHE.Roles.Core.AssignManager;

public enum AssignmentTeam { None, Impostor, NonKillingNeutral, NeutralKilling, NeutralApocalypse, Crewmate }

public sealed record RoleCandidate(CustomRoles Role, AssignmentTeam Team, int Chance, int Count);
public sealed record RoleAssignmentPlayer(byte PlayerId, CustomRoles? Preset = null, AssignmentTeam Team = AssignmentTeam.None);
public sealed record RoleReplacement(CustomRoles From, CustomRoles To, int Chance);

public sealed class RoleAssignmentRequest
{
    public int Seed { get; init; }
    public RoleAssignmentPlayer[] Players { get; init; } = [];
    public RoleCandidate[] Candidates { get; init; } = [];
    public Dictionary<AssignmentTeam, int> Quotas { get; init; } = [];
    public RoleReplacement[] Replacements { get; init; } = [];
}

// No Unity objects, global options, RPCs, or shared random state belong in this engine.
public static class RoleAssignmentEngine
{
    public static Dictionary<byte, CustomRoles> Assign(RoleAssignmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var players = request.Players.OrderBy(p => p.PlayerId).ToArray();
        if (players.Select(p => p.PlayerId).Distinct().Count() != players.Length)
            throw new ArgumentException("Duplicate PlayerId in assignment roster.", nameof(request));
        if (request.Candidates.Select(c => c.Role).Distinct().Count() != request.Candidates.Length)
            throw new ArgumentException("Duplicate role in assignment catalog.", nameof(request));

        var result = players.Where(p => p.Preset.HasValue).ToDictionary(p => p.PlayerId, p => p.Preset.GetValueOrDefault());
        var available = players.Where(p => !p.Preset.HasValue).Select(p => p.PlayerId).ToList();
        var assignedCounts = result.Values.GroupBy(role => role).ToDictionary(g => g.Key, g => g.Count());
        var random = new AssignmentRandom(request.Seed, AssignmentStreams.Roles);
        var selected = new List<CustomRoles>();
        // Numeric order is part of the versioned contract: impostors, NNK, NK, NA, crew.
        foreach (var team in Enum.GetValues<AssignmentTeam>().Where(t => t != AssignmentTeam.None))
        {
            int capacity = available.Count - selected.Count;
            int quota = team == AssignmentTeam.Crewmate ? capacity
                : Math.Max(0, request.Quotas.GetValueOrDefault(team) - players.Count(p => p.Preset.HasValue && p.Team == team));
            quota = Math.Min(quota, capacity);
            var pool = request.Candidates.Where(c => c.Team == team && c.Chance > 0 && c.Count > 0)
                .OrderBy(c => c.Role).ToArray();

            while (quota > 0)
            {
                var remaining = pool.Where(c => assignedCounts.GetValueOrDefault(c.Role) < c.Count).ToArray();
                bool guaranteed = remaining.Any(c => c.Chance >= 100);
                var choices = remaining.Where(c => !guaranteed || c.Chance >= 100).ToArray();
                if (choices.Length == 0) break;
                // Weight by remaining copies, without materializing duplicate ticket lists.
                var weights = choices.Select(c => checked((c.Count - assignedCounts.GetValueOrDefault(c.Role))
                    * (guaranteed ? 1 : Math.Clamp(c.Chance, 1, 100)))).ToArray();
                int ticket = random.Next(weights.Sum());
                int index = 0;
                while (ticket >= weights[index]) ticket -= weights[index++];
                var role = choices[index].Role;
                selected.Add(role);
                assignedCounts[role] = assignedCounts.GetValueOrDefault(role) + 1;
                quota--;
            }

            // The configured impostor quota must survive an empty/exhausted custom pool.
            if (team == AssignmentTeam.Impostor)
                while (quota-- > 0) selected.Add(CustomRoles.ImpostorTOHE);
        }

        var hidden = new AssignmentRandom(request.Seed, AssignmentStreams.HiddenRoles);
        foreach (var replacement in request.Replacements.OrderBy(r => r.From).ThenBy(r => r.To))
        {
            int index = selected.IndexOf(replacement.From);
            if (index >= 0 && hidden.Roll(replacement.Chance)) selected[index] = replacement.To;
        }
        while (selected.Count < available.Count) selected.Add(CustomRoles.CrewmateTOHE);
        new AssignmentRandom(request.Seed, AssignmentStreams.Players).Shuffle(selected);
        for (int i = 0; i < available.Count; i++) result.Add(available[i], selected[i]);
        return result.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value);
    }
}
