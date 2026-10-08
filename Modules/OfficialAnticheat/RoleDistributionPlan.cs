using AmongUs.GameOptions;
using System;

namespace TOHE;

// Keys always mean (recipient, subject); transport must never transpose them.
internal sealed class RoleDistributionPlan
{
    internal sealed record Actor(byte Id, RoleTypes NativeRole, bool Desync, RoleTypes SelfRole, bool Noisemaker);
    internal readonly Dictionary<(byte Recipient, byte Subject), RoleTypes> DesiredNativeRole = [];
    internal readonly byte[] PlayerIds;
    internal readonly byte HostPlayerId;

    internal RoleDistributionPlan(IEnumerable<Actor> actors, byte hostPlayerId)
    {
        var ordered = actors.OrderBy(actor => actor.Id).ToArray();
        if (ordered.Select(actor => actor.Id).Distinct().Count() != ordered.Length ||
            !ordered.Any(actor => actor.Id == hostPlayerId))
            throw new ArgumentException("Role distribution requires unique players and a host");
        PlayerIds = ordered.Select(actor => actor.Id).ToArray();
        HostPlayerId = hostPlayerId;
        foreach (var recipient in ordered)
        foreach (var subject in ordered)
        {
            RoleTypes role;
            if (recipient.Id == hostPlayerId)
            {
                role = recipient.Id == subject.Id && recipient.Desync
                    ? recipient.SelfRole == RoleTypes.Shapeshifter ? RoleTypes.Shapeshifter : RoleTypes.Crewmate
                    : subject.Desync ? RoleTypes.Scientist : subject.NativeRole;
            }
            else if (recipient.Desync)
            {
                role = recipient.Id == subject.Id
                    ? recipient.SelfRole
                    : subject.Noisemaker ? RoleTypes.Noisemaker
                        : RoleTypes.Scientist;
            }
            else role = subject.Desync
                ? subject.Id == hostPlayerId ? RoleTypes.Crewmate : RoleTypes.Scientist
                : subject.NativeRole;
            DesiredNativeRole.Add((recipient.Id, subject.Id), role);
        }
    }
}
