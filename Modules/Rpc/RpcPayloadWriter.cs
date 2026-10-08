using System;
using System.IO;
using System.Text;

namespace TOHE;

// Large logical payloads never enter a Hazel buffer. Every write checks the
// transfer bound before allocating or extending its managed stream.
internal sealed class RpcPayloadWriter : IDisposable
{
    private readonly MemoryStream stream = new();
    private readonly BinaryWriter writer;
    internal RpcPayloadWriter() => writer = new BinaryWriter(stream, Encoding.UTF8, true);
    private void Reserve(int count)
    {
        if (count < 0 || stream.Length + count > PackedRpcCodec.MaxPayload)
            throw new InvalidDataException("Custom RPC exceeds the bounded logical payload size");
    }
    internal void Write(byte value) { Reserve(1); writer.Write(value); }
    internal void Write(bool value) { Reserve(1); writer.Write(value); }
    internal void Write(ushort value) { Reserve(2); writer.Write(value); }
    internal void Write(int value) { Reserve(4); writer.Write(value); }
    internal void Write(uint value) { Reserve(4); writer.Write(value); }
    internal void WritePacked(int value) => WritePacked(unchecked((uint)value));
    internal void WritePacked(uint value)
    {
        Reserve(PackedRpcCodec.PackedUIntLength(value));
        while (value >= 128) { writer.Write((byte)(value | 128)); value >>= 7; }
        writer.Write((byte)value);
    }
    internal void Write(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int count = Encoding.UTF8.GetByteCount(value);
        Reserve(PackedRpcCodec.PackedUIntLength((uint)count) + count);
        WritePacked(count);
        writer.Write(Encoding.UTF8.GetBytes(value));
    }
    internal byte[] ToArray() => stream.ToArray();
    public void Dispose() { writer.Dispose(); stream.Dispose(); }
}
