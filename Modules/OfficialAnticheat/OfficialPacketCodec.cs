using System;
using System.Buffers.Binary;
using System.IO;

namespace TOHE;

// Network framing only. Business payloads (including nested voting messages) are opaque.
internal static class OfficialPacketCodec
{
    internal const int SoftLimit = 1000;
    internal const int HardLimit = 1200;
    internal sealed class Record
    {
        internal int Target { get; }
        private readonly byte[] child;
        internal ReadOnlySpan<byte> Child => child;
        internal Record(int target, byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            Target = target;
            child = (byte[])bytes.Clone();
        }
        internal void Validate()
        {
            if (Target < -1 || child.Length < 3 || child.Length > HardLimit ||
                ReadFrame(child, 0, child.Length, out _) != child.Length)
                throw new InvalidDataException("Record must contain exactly one bounded child");
        }
    }

    private static int PackedLength(uint value)
    {
        int result = 1;
        while (value >= 128) { result++; value >>= 7; }
        return result;
    }

    internal static int MaxChildLength(int gameId, int target, int headerBytes)
        => HardLimit - headerBytes - (target < 0 ? 7 :
            3 + PackedLength(unchecked((uint)gameId)) + 7 + PackedLength((uint)target));

    internal static byte[] Frame(byte tag, byte[] body)
    {
        if (body.Length > ushort.MaxValue) throw new InvalidDataException("Hazel child exceeds uint16 framing");
        var result = new byte[body.Length + 3];
        BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)body.Length);
        result[2] = tag;
        body.CopyTo(result, 3);
        return result;
    }

    internal static void Packed(Stream stream, uint value)
    {
        while (value >= 128) { stream.WriteByte((byte)(value | 128)); value >>= 7; }
        stream.WriteByte((byte)value);
    }

    private static byte[] Root(int gameId, List<Record> records, int limit)
    {
        using var body = new MemoryStream(HardLimit);
        bool broadcast = records[0].Target < 0;
        if (broadcast)
        {
            body.Write(BitConverter.GetBytes(gameId));
            foreach (var record in records) body.Write(record.Child);
            return Frame(5, body.ToArray());
        }
        Packed(body, unchecked((uint)gameId));
        for (int index = 0; index < records.Count;)
        {
            int target = records[index].Target;
            using var inner = new MemoryStream(HardLimit);
            inner.Write(BitConverter.GetBytes(gameId));
            Packed(inner, (uint)target);
            int count = 0;
            do { inner.Write(records[index++].Child); count++; }
            while (index < records.Count && records[index].Target == target && count < limit);
            body.Write(Frame(6, inner.ToArray()));
        }
        return Frame(26, body.ToArray());
    }

    // The returned arrays start at the Hazel root, excluding the send-option header.
    internal static List<byte[]> Pack(int gameId, IEnumerable<Record> input, int headerBytes, int limit)
    {
        if (limit < 1 || headerBytes is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(limit));
        var output = new List<byte[]>();
        var pending = new List<Record>();
        foreach (var record in input)
        {
            record.Validate();
            if (pending.Count > 0 && (pending[0].Target < 0) != (record.Target < 0)) Flush();
            pending.Add(record);
            var trial = Root(gameId, pending, limit);
            if (trial.Length + headerBytes > HardLimit || CountContainers(pending, limit) > limit)
            {
                pending.RemoveAt(pending.Count - 1);
                Flush();
                pending.Add(record);
                trial = Root(gameId, pending, limit);
                if (trial.Length + headerBytes > HardLimit)
                    throw new InvalidDataException("Single vanilla child cannot fit the 1200-byte packet contract");
            }
            if (trial.Length + headerBytes > SoftLimit || (record.Target < 0 && pending.Count >= limit)) Flush();
        }
        Flush();
        return output;

        void Flush()
        {
            if (pending.Count == 0) return;
            var root = Root(gameId, pending, limit);
            Validate(root, 0, root.Length, headerBytes, limit);
            output.Add(root);
            pending.Clear();
        }
    }

    private static int CountContainers(List<Record> records, int limit)
    {
        if (records[0].Target < 0) return records.Count;
        int result = 0, last = -2, children = 0;
        foreach (var record in records)
        {
            if (record.Target != last || children == limit) { result++; children = 0; last = record.Target; }
            children++;
        }
        return result;
    }

    internal static int ReadFrame(byte[] buffer, int offset, int end, out byte tag)
    {
        if (offset < 0 || end > buffer.Length || offset > end - 3) throw new InvalidDataException("Truncated Hazel frame");
        int next = offset + 3 + BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2));
        if (next > end) throw new InvalidDataException("Truncated Hazel payload");
        tag = buffer[offset + 2];
        return next;
    }

    private static int SkipPacked(byte[] bytes, int offset, int end)
    {
        for (int index = 0; index < 5 && offset < end; index++)
        {
            byte value = bytes[offset++];
            if (index == 4 && value > 15) throw new InvalidDataException("Invalid packed uint32");
            if ((value & 128) == 0) return offset;
        }
        throw new InvalidDataException("Truncated packed uint32");
    }

    private static uint ReadPacked(byte[] bytes, ref int offset, int end)
    {
        int after = SkipPacked(bytes, offset, end);
        uint value = 0;
        for (int index = offset; index < after; index++) value |= (uint)(bytes[index] & 127) << (7 * (index - offset));
        offset = after;
        return value;
    }

    internal static List<Record> ReadRecords(byte[] bytes, int offset, int end, int gameId)
    {
        var records = new List<Record>();
        while (offset < end)
        {
            int next = ReadFrame(bytes, offset, end, out byte tag);
            if (tag is not (5 or 6)) throw new InvalidDataException("Expected GameData or GameDataTo root");
            int cursor = offset + 3;
            if (cursor > next - 4 || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor, 4)) != gameId)
                throw new InvalidDataException("Stale GameId in completed writer");
            cursor += 4;
            int target = -1;
            if (tag == 6)
            {
                int after = SkipPacked(bytes, cursor, next);
                uint value = 0;
                for (int index = cursor; index < after; index++) value |= (uint)(bytes[index] & 127) << (7 * (index - cursor));
                if (value > int.MaxValue) throw new InvalidDataException("Invalid recipient");
                target = (int)value;
                cursor = after;
            }
            while (cursor < next)
            {
                int childEnd = ReadFrame(bytes, cursor, next, out _);
                records.Add(new Record(target, bytes.AsSpan(cursor, childEnd - cursor).ToArray()));
                cursor = childEnd;
            }
            offset = next;
        }
        return records;
    }

    internal static void Validate(byte[] buffer, int offset, int end, int headerBytes, int limit)
    {
        if (end - offset + headerBytes > HardLimit) throw new InvalidDataException("Packet exceeds 1200 bytes");
        int rootEnd = ReadFrame(buffer, offset, end, out byte tag);
        if (rootEnd != end) throw new InvalidDataException("A writer must contain exactly one root");
        int cursor = offset + 3;
        if (tag == 26)
        {
            uint gameId = ReadPacked(buffer, ref cursor, end);
            int count = 0;
            while (cursor < end)
            {
                int next = ReadFrame(buffer, cursor, end, out byte inner);
                if (inner != 6 || ++count > limit) throw new InvalidDataException("Invalid PackedGameDataTo container");
                if (cursor + 3 > next - 4 || BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(cursor + 3, 4)) != gameId)
                    throw new InvalidDataException("Packed GameId does not match its inner GameDataTo");
                ValidateGameData(buffer, cursor + 3, next, true, limit);
                cursor = next;
            }
            if (count == 0) throw new InvalidDataException("Empty packed root");
        }
        else if (tag is 5 or 6) ValidateGameData(buffer, cursor, end, tag == 6, limit);
    }

    private static void ValidateGameData(byte[] buffer, int offset, int end, bool targeted, int limit)
    {
        if (offset > end - 4) throw new InvalidDataException("Missing GameId");
        offset += 4;
        if (targeted && ReadPacked(buffer, ref offset, end) > int.MaxValue)
            throw new InvalidDataException("Invalid recipient");
        int count = 0;
        while (offset < end)
        {
            offset = ReadFrame(buffer, offset, end, out _);
            if (++count > limit) throw new InvalidDataException("GameData child packing limit exceeded");
        }
        if (count == 0) throw new InvalidDataException("Empty game-data root");
    }
}
