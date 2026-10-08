using System;
using System.IO;

namespace TOHE;

// Pure managed framing shared with the external protocol harness.
internal static class PackedRpcCodec
{
    internal const int ChildBudget = 200;
    internal const int MaxPayload = 65536;
    internal const int FragmentHeader = 16;
    internal static int PackedUIntLength(uint value)
    {
        int count = 1;
        while ((value >>= 7) != 0) count++;
        return count;
    }
    internal static int HeaderLength(uint netId) => 3 + PackedUIntLength(netId) + 2;
    internal static int EntryLength(RpcPayloadSnapshot entry) => 10 + entry.Length;
    internal static byte[] Encode(IReadOnlyList<RpcPayloadSnapshot> entries, uint netId)
    {
        int size = 1 + entries.Sum(EntryLength);
        if (entries.Count is < 1 or > 255 || size + HeaderLength(netId) - 1 > ChildBudget)
            throw new InvalidDataException("Packed custom RPC exceeds child budget");
        using var stream = new MemoryStream(size);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)entries.Count);
        foreach (var entry in entries)
        {
            writer.Write((ushort)(8 + entry.Length));
            writer.Write(entry.Target);
            writer.Write(entry.RpcId);
            writer.Write(entry.CopyPayload());
        }
        return stream.ToArray();
    }
    internal static List<RpcPayloadSnapshot> Decode(byte[] bytes, uint netId, Func<uint, bool> validRpc)
    {
        if (bytes.Length < 1 || bytes.Length + HeaderLength(netId) - 1 > ChildBudget)
            throw new InvalidDataException("Invalid packed custom RPC size");
        using var reader = new BinaryReader(new MemoryStream(bytes, false));
        int count = reader.ReadByte();
        if (count == 0) throw new InvalidDataException("Empty packed custom RPC");
        var entries = new List<RpcPayloadSnapshot>(count);
        for (int index = 0; index < count; index++)
        {
            if (reader.BaseStream.Length - reader.BaseStream.Position < 10)
                throw new InvalidDataException("Truncated packed custom RPC entry");
            int length = reader.ReadUInt16();
            if (length < 8 || reader.BaseStream.Length - reader.BaseStream.Position < length)
                throw new InvalidDataException("Invalid packed custom RPC entry length");
            int target = reader.ReadInt32();
            uint id = reader.ReadUInt32();
            entries.Add(new RpcPayloadSnapshot(target, id, reader.ReadBytes(length - 8), validRpc));
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("Packed custom RPC contains trailing bytes");
        return entries;
    }
    internal static IEnumerable<RpcPayloadSnapshot> Fragment(RpcPayloadSnapshot entry, uint transferId, uint netId)
    {
        if (entry.Length > MaxPayload) throw new InvalidDataException("Custom RPC payload exceeds bounded transfer size");
        int chunk = ChildBudget - HeaderLength(netId) - 10 - FragmentHeader;
        var bytes = entry.CopyPayload();
        int count = (bytes.Length + chunk - 1) / chunk;
        for (int index = 0; index < count; index++)
        {
            int offset = index * chunk;
            int length = Math.Min(chunk, bytes.Length - offset);
            using var stream = new MemoryStream(FragmentHeader + length);
            using var writer = new BinaryWriter(stream);
            writer.Write(entry.RpcId);
            writer.Write(transferId);
            writer.Write(bytes.Length);
            writer.Write((ushort)index);
            writer.Write((ushort)count);
            writer.Write(bytes, offset, length);
            yield return new RpcPayloadSnapshot(entry.Target, (uint)CustomRPC.Fragment, stream.ToArray(), CustomRpcTransport.IsValidRpcId);
        }
    }
}

internal sealed class RpcFragmentAssembler
{
    private sealed class Transfer(uint rpc, int target, int total, int count, double now)
    {
        internal readonly uint Rpc = rpc;
        internal readonly int Target = target;
        internal readonly int Total = total;
        internal readonly byte[][] Parts = new byte[count][];
        internal readonly double Created = now;
        internal int Bytes;
        internal int Received;
    }
    private readonly Dictionary<(int Owner, uint Id), Transfer> transfers = new();
    private readonly Dictionary<(int Owner, uint Id), double> completed = new();
    internal void Clear() { transfers.Clear(); completed.Clear(); }
    internal RpcPayloadSnapshot Add(int owner, RpcPayloadSnapshot fragment, double now, Func<uint, bool> permitted)
    {
        foreach (var expired in transfers.Where(pair => now - pair.Value.Created > 10).Select(pair => pair.Key).ToArray()) transfers.Remove(expired);
        foreach (var expired in completed.Where(pair => now - pair.Value > 10).Select(pair => pair.Key).ToArray()) completed.Remove(expired);
        var payload = fragment.CopyPayload();
        if (payload.Length <= PackedRpcCodec.FragmentHeader) throw new InvalidDataException("Empty custom RPC fragment");
        using var reader = new BinaryReader(new MemoryStream(payload, false));
        uint rpc = reader.ReadUInt32(), id = reader.ReadUInt32();
        int total = reader.ReadInt32(), index = reader.ReadUInt16(), count = reader.ReadUInt16();
        if (rpc == (uint)CustomRPC.Fragment || !permitted(rpc) || total is < 1 or > PackedRpcCodec.MaxPayload ||
            count is < 1 or > 512 || count > total || index >= count || payload.Length - PackedRpcCodec.FragmentHeader > total)
            throw new InvalidDataException("Invalid or unauthorized custom RPC fragment");
        var key = (owner, id);
        if (completed.ContainsKey(key)) return null;
        if (!transfers.TryGetValue(key, out var transfer))
        {
            if (transfers.Count(pair => pair.Key.Owner == owner) >= 4 ||
                transfers.Sum(pair => pair.Value.Total) + total > 1024 * 1024)
                throw new InvalidDataException("Custom RPC fragment memory limit");
            transfer = new Transfer(rpc, fragment.Target, total, count, now);
            transfers.Add(key, transfer);
        }
        if (transfer.Rpc != rpc || transfer.Target != fragment.Target || transfer.Total != total || transfer.Parts.Length != count)
            throw new InvalidDataException("Conflicting custom RPC fragment metadata");
        var bytes = reader.ReadBytes(payload.Length - PackedRpcCodec.FragmentHeader);
        if (transfer.Parts[index] != null)
        {
            if (!transfer.Parts[index].SequenceEqual(bytes)) throw new InvalidDataException("Conflicting duplicate fragment");
            return null;
        }
        if (transfer.Bytes + bytes.Length > total) throw new InvalidDataException("Custom RPC fragment exceeds declared size");
        transfer.Parts[index] = bytes;
        transfer.Bytes += bytes.Length;
        transfer.Received++;
        if (transfer.Received != count) return null;
        transfers.Remove(key);
        if (transfer.Bytes != total) throw new InvalidDataException("Custom RPC fragment size mismatch");
        var result = new byte[total];
        int offset = 0;
        foreach (var part in transfer.Parts) { part.CopyTo(result, offset); offset += part.Length; }
        if (completed.Count >= 4096) throw new InvalidDataException("Custom RPC completed transfer limit");
        completed[key] = now;
        return new RpcPayloadSnapshot(fragment.Target, rpc, result, CustomRpcTransport.IsValidRpcId);
    }
}
