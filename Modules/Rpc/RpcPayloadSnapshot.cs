using System;

namespace TOHE;

// Pure managed protocol/lifetime helpers, shared with the offline harness.
internal sealed class RpcPayloadSnapshot
{
    internal const byte OuterCallId = 123;
    internal const byte PackedOuterCallId = 124;
    private readonly byte[] payload;
    internal int Target { get; }
    internal uint RpcId { get; }
    internal int Length => payload.Length;
    internal byte[] CopyPayload() => (byte[])payload.Clone();

    internal static void ValidateTarget(int target)
    {
        if (target < -1) throw new ArgumentOutOfRangeException(nameof(target));
    }

    internal RpcPayloadSnapshot(int target, uint rpcId, byte[] payload, Func<uint, bool> validRpc)
    {
        ValidateTarget(target);
        if (!validRpc(rpcId)) throw new ArgumentOutOfRangeException(nameof(rpcId));
        ArgumentNullException.ThrowIfNull(payload);
        Target = target; RpcId = rpcId;
        this.payload = (byte[])payload.Clone();
    }

    internal void Write(Action<int> writeTarget, Action<uint> writeId, Action<byte[]> writePayload)
    {
        writeTarget(Target);
        writeId(RpcId);
        // The callback never receives our retained buffer.
        writePayload((byte[])payload.Clone());
    }
}

internal sealed class RpcBuilderLeases<TWriter, TState>(Func<TWriter, IntPtr> getKey) where TWriter : class
{
    private readonly Dictionary<IntPtr, (TWriter Writer, TState State)> leases = new();
    internal void Add(TWriter writer, TState state)
    {
        if (!leases.TryAdd(getKey(writer), (writer, state))) throw new InvalidOperationException("RPC builder already leased");
    }
    internal TState Take(TWriter writer)
    {
        if (!leases.TryGetValue(getKey(writer), out var lease) || !ReferenceEquals(lease.Writer, writer))
            throw new InvalidOperationException("RPC builder is foreign, stale, or already finished");
        leases.Remove(getKey(writer));
        return lease.State;
    }
    internal bool Cancel(TWriter writer)
    {
        if (!leases.TryGetValue(getKey(writer), out var lease) || !ReferenceEquals(lease.Writer, writer)) return false;
        leases.Remove(getKey(writer));
        return true;
    }
    internal void Clear(Action<TWriter> recycle)
    {
        var writers = new List<TWriter>();
        foreach (var lease in leases.Values) writers.Add(lease.Writer);
        leases.Clear();
        foreach (var writer in writers) recycle(writer);
    }
}
