using System;
using Hazel;
using InnerNet;

namespace TOHE;

internal static class OfficialImmediateRpc
{
    private sealed record ImmediateLease(OfficialPacketBuilder.BoundedWriter Owner, OfficialSessionContext Context);
    private static readonly Dictionary<IntPtr, ImmediateLease> Immediate = new();

    internal static MessageWriter StartImmediate(this InnerNetClient client, uint targetNetId, byte callId,
        SendOption sendOption, int targetClientId = -1)
    {
        if (!OfficialAnticheatPolicy.Enabled) return client.StartRpcImmediately(targetNetId, callId, sendOption, targetClientId);
        if (targetClientId < -1) throw new ArgumentOutOfRangeException(nameof(targetClientId));
        var owner = new OfficialPacketBuilder.BoundedWriter(sendOption);
        try
        {
            var writer = owner.Writer;
            writer.StartMessage(targetClientId < 0 ? (byte)5 : (byte)6);
            writer.Write(client.GameId);
            if (targetClientId >= 0) writer.WritePacked(targetClientId);
            writer.StartMessage(2);
            writer.WritePacked(targetNetId);
            writer.Write(callId);
            Immediate.Add(writer.Pointer, new(owner, OfficialSessionContext.Capture()));
            return writer;
        }
        catch { owner.Dispose(); throw; }
    }

    internal static void FinishImmediate(this InnerNetClient client, MessageWriter writer)
    {
        if (!OfficialAnticheatPolicy.Enabled) { client.FinishRpcImmediately(writer); return; }
        Immediate.Remove(writer.Pointer, out var lease);
        try
        {
            if (lease != null && !lease.Context.IsCurrent()) throw new InvalidOperationException("Immediate RPC belongs to an expired room");
            writer.EndMessage();
            writer.EndMessage();
            OfficialPacketBuilder.SendCompleted(writer);
        }
        catch
        {
            // A rejected old-room lease must not disconnect the new room.
            if (lease == null || lease.Context.IsCurrent()) OfficialNetworkSend.Abort(client, writer, "immediate-rpc");
            throw;
        }
        finally
        {
            if (lease != null) lease.Owner.Dispose();
            else writer.Recycle();
        }
    }

    internal static void CancelImmediate()
    {
        var leases = Immediate.Values.ToArray();
        Immediate.Clear();
        foreach (var lease in leases) lease.Owner.Dispose();
    }

}
