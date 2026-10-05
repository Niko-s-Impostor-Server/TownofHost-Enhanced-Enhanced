using System;

namespace TOHE;

internal readonly record struct AfkIdentity(byte PlayerId, int OwnerId, nint Pointer);
internal readonly record struct AfkStatus(double IdleSeconds, bool IsAfk, bool Exempt, bool KickSent);

// Counts observed task-stage time. A host stall cannot become an instant AFK penalty.
internal sealed class AfkInactivityPolicy
{
    private sealed class Entry(AfkIdentity identity, double now, double grace)
    {
        internal readonly AfkIdentity Identity = identity;
        internal double LastObserved = now;
        internal double Grace = grace;
        internal double Idle;
        internal float X, Y;
        internal int Tasks;
        internal bool HasBaseline, Afk, Exempt, KickSent;

        internal void Restart(double now, double grace)
        {
            LastObserved = now;
            Grace = grace;
            Idle = 0;
            Afk = KickSent = HasBaseline = false;
        }
        internal void Activity(double now)
        {
            LastObserved = now;
            Idle = 0;
            Afk = KickSent = false;
        }
    }

    private readonly Dictionary<byte, Entry> entries = [];

    private Entry Ensure(AfkIdentity identity, double now, double grace)
    {
        if (!entries.TryGetValue(identity.PlayerId, out var entry) || entry.Identity != identity)
            entries[identity.PlayerId] = entry = new(identity, now, grace);
        return entry;
    }

    internal bool Observe(AfkIdentity identity, float x, float y, int tasks, double now,
        bool paused, double grace, double threshold)
    {
        var entry = Ensure(identity, now, grace);
        if (paused || entry.Exempt || now < entry.LastObserved)
        {
            entry.Restart(now, grace);
            return false;
        }
        if (!entry.HasBaseline)
        {
            entry.X = x; entry.Y = y; entry.Tasks = tasks;
            entry.LastObserved = now;
            entry.HasBaseline = true;
            return false;
        }

        var dx = x - entry.X;
        var dy = y - entry.Y;
        if (dx * dx + dy * dy >= 0.0025f || tasks != entry.Tasks)
        {
            entry.X = x; entry.Y = y; entry.Tasks = tasks;
            entry.Activity(now);
            return false;
        }

        // Polling every 0.5 seconds may arrive a little late; cap gaps at one second.
        var elapsed = Math.Min(1d, Math.Max(0d, now - entry.LastObserved));
        entry.LastObserved = now;
        var graceElapsed = Math.Min(entry.Grace, elapsed);
        entry.Grace -= graceElapsed;
        entry.Idle += elapsed - graceElapsed;
        if (entry.Afk || entry.Idle < threshold) return false;
        entry.Afk = true;
        return true;
    }

    internal void RecordActivity(AfkIdentity identity, double now)
    {
        if (entries.TryGetValue(identity.PlayerId, out var entry) && entry.Identity == identity)
            entry.Activity(now);
    }

    internal void SetExempt(AfkIdentity identity, bool exempt, double now, double grace)
    {
        var entry = Ensure(identity, now, grace);
        entry.Exempt = exempt;
        entry.Restart(now, grace);
    }

    internal bool TryGetStatus(AfkIdentity identity, out AfkStatus status)
    {
        status = default;
        if (!entries.TryGetValue(identity.PlayerId, out var entry) || entry.Identity != identity) return false;
        status = new(entry.Idle, entry.Afk, entry.Exempt, entry.KickSent);
        return true;
    }

    internal void MarkKickSent(AfkIdentity identity)
    {
        if (entries.TryGetValue(identity.PlayerId, out var entry) && entry.Identity == identity)
            entry.KickSent = true;
    }

    internal void RemoveMissing(HashSet<AfkIdentity> live)
    {
        List<byte> remove = [];
        foreach (var entry in entries.Values)
            if (!live.Contains(entry.Identity)) remove.Add(entry.Identity.PlayerId);
        foreach (var id in remove) entries.Remove(id);
    }

    internal void ResetTimers(double now, double grace)
    {
        foreach (var entry in entries.Values) entry.Restart(now, grace);
    }

    internal void Reset() => entries.Clear();
}
