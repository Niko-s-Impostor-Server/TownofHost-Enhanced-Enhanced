using System;

namespace TOHE;

// Captures real presentation fields once. Business PlayerStates are never masked.
internal sealed class StartDisconnectedMask
{
    private sealed class State(NetworkedPlayerInfo info)
    {
        internal readonly NetworkedPlayerInfo Info = info;
        internal readonly nint Identity = info.Pointer;
        internal bool Disconnected = info.Disconnected;
        internal bool IsDead = info.IsDead;
        internal bool Restored;
    }
    private readonly Dictionary<byte, State> states = [];
    private bool begun;
    internal bool Active { get; private set; }
    internal bool RestoreComplete => states.Values.All(state => state.Restored);

    internal void Begin(IEnumerable<NetworkedPlayerInfo> players)
    {
        if (begun) return;
        begun = true;
        foreach (var info in players.OrderBy(info => info.PlayerId)) states.Add(info.PlayerId, new(info));
        Active = true;
        foreach (var state in states.Values)
        {
            state.Info.Disconnected = true;
            state.Info.IsDead = false;
        }
    }

    internal void RecordDeparture(byte id)
    {
        if (!states.TryGetValue(id, out var state)) return;
        state.Disconnected = true;
        // A real departure must survive cancellation and repeated restores.
        if (Current(state)) state.Info.Disconnected = true;
    }

    private static bool Current(State state) => state.Info != null && state.Info.Pointer == state.Identity &&
        GameData.Instance != null && GameData.Instance.AllPlayers.ToArray()
            .Any(info => info != null && info.Pointer == state.Identity);

    internal NetworkedPlayerInfo Restore(byte id)
    {
        if (!states.TryGetValue(id, out var state) || state.Restored) return null;
        if (!Current(state)) { state.Restored = true; return null; }
        state.Info.Disconnected = state.Disconnected;
        state.Info.IsDead = state.IsDead;
        return state.Info;
    }

    // Advance only after the corresponding actual network send succeeds.
    internal void CommitRestore(byte id)
    {
        if (states.TryGetValue(id, out var state)) state.Restored = true;
        if (RestoreComplete) Active = false;
    }

    internal void Cancel()
    {
        foreach (var id in states.Keys.OrderBy(id => id))
        {
            Restore(id);
            CommitRestore(id);
        }
        Active = false;
    }
}
