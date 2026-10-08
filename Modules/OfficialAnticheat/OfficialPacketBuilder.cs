using System;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using System.IO;
using System.Runtime.CompilerServices;

namespace TOHE;

internal static class OfficialPacketBuilder
{
    internal sealed class BoundedWriter : IDisposable
    {
        internal readonly MessageWriter Writer;
        private readonly Il2CppStructArray<byte> pooledBuffer;
        private bool disposed;
        internal BoundedWriter(SendOption option, bool bounded = true)
        {
            Writer = MessageWriter.Get(option);
            pooledBuffer = Writer.Buffer;
            if (bounded)
            {
                Writer.Buffer = new Il2CppStructArray<byte>(OfficialPacketCodec.HardLimit);
                Writer.Clear(option);
                CustomRpcTransport.BeginNativePacket(Writer);
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CustomRpcTransport.DiscardNativePacket(Writer);
            Writer.Buffer = pooledBuffer;
            Writer.Recycle();
        }
    }

    // Adapter at explicit serialization call sites, before the final validation guard.
    // Ownership remains with the caller, exactly like SendOrDisconnect.
    internal static void SendCompleted(MessageWriter writer)
    {
        if (!OfficialAnticheatPolicy.Enabled) { AmongUsClient.Instance.SendOrDisconnect(writer); return; }
        int header = writer.SendOption == SendOption.Reliable ? 3 : 1;
        var bytes = Snapshot(writer);
        Send(OfficialPacketCodec.ReadRecords(bytes, header, bytes.Length, AmongUsClient.Instance.GameId), writer.SendOption);
    }

    internal static byte[] Snapshot(MessageWriter writer, int offset = 0)
    {
        var bytes = new byte[writer.Length - offset];
        for (int index = 0; index < bytes.Length; index++) bytes[index] = writer.Buffer[offset + index];
        return bytes;
    }

    internal static void Send(IEnumerable<OfficialPacketCodec.Record> records, SendOption option)
    {
        var client = AmongUsClient.Instance;
        var context = OfficialSessionContext.Capture();
        int gameId = client.GameId;
        var packets = OfficialPacketCodec.Pack(gameId, records, option == SendOption.Reliable ? 3 : 1,
            OfficialAnticheatPolicy.PackingLimit);
        foreach (var packet in packets)
        {
            if (!context.IsCurrent())
                throw new InvalidOperationException("Packet transaction belongs to an expired room");
            using var owner = new BoundedWriter(option);
            owner.Writer.Write(new Il2CppStructArray<byte>(packet));
            OfficialNetworkSend.Send(client, owner.Writer);
        }
    }

    internal static void SendChild(int target, SendOption option, Action<MessageWriter> serialize)
    {
        using var owner = new BoundedWriter(option);
        var writer = owner.Writer;
        int offset = writer.Position;
        byte[] child;
        serialize(writer);
        child = Snapshot(writer, offset);
        Send(new[] { new OfficialPacketCodec.Record(target, child) }, option);
    }
}

// A controlled send boundary also covers our direct paths when a native caller
// has inlined SendOrDisconnect. Completion means submission, never peer ACK.
internal static class OfficialNetworkSend
{
    private static readonly HashSet<IntPtr> Failures = new();
    internal static bool Failed(InnerNetClient client) => Failures.Contains(client.Pointer);
    internal static void Reset() => Failures.Clear();

    internal static void Send(InnerNetClient client, MessageWriter writer, [CallerMemberName] string source = null)
    {
        if (!OfficialAnticheatPolicy.Enabled) { client.SendOrDisconnect(writer); return; }
        try
        {
            if (Failed(client) || !client.AmConnected || client.connection == null)
                throw new IOException("Network transaction is unavailable");
            int header = writer.SendOption == SendOption.Reliable ? 3 : 1;
            if (writer.Length > OfficialPacketCodec.HardLimit || writer.Position != writer.Length)
                throw new InvalidDataException("Packet exceeds its bounded writer contract");
            var bytes = OfficialPacketBuilder.Snapshot(writer);
            OfficialPacketCodec.Validate(bytes, header, bytes.Length, header, OfficialAnticheatPolicy.PackingLimit);
            SendErrors error = client.connection.Cast<Connection>().Send(writer);
            if (error != SendErrors.None) throw new IOException("Network rejected the packet submission");
            CustomRpcTransport.NotifyPacketSent(writer);
        }
        catch (Exception)
        {
            Abort(client, writer, source);
            throw;
        }
    }

    internal static void Abort(InnerNetClient client, MessageWriter writer, string source)
    {
        CustomRpcTransport.DiscardNativePacket(writer);
        if (!Failures.Add(client.Pointer)) return;
        int header = writer.SendOption == SendOption.Reliable ? 3 : 1;
        int tag = writer.Length >= header + 3 ? writer.Buffer[header + 2] : -1;
        Logger.Error($"Packet operation failed: source={source}, bytes={writer.Length}, root={tag}, option={writer.SendOption}", "OfficialPacket");
        // A native-to-managed trampoline may swallow exceptions. Cancel
        // completion markers explicitly so no dependent role is released.
        CustomRpcTransport.Reset(preserveCapabilities: true);
        client.EnqueueDisconnect(DisconnectReasons.Error, "Official compatibility packet submission failed.");
    }
}
