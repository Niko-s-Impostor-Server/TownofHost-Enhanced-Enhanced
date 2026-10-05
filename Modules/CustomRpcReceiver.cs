using System;
using Hazel;

namespace TOHE;

internal static class CustomRpcReceiver
{
    internal static void Receive(PlayerControl sender, MessageReader original)
    {
        var client = AmongUsClient.Instance;
        if (client == null || sender == null || original == null) return;
        // HandleRpc exposes the addressed object, not the transport connection.
        // Nmpostor must enforce connection ownership; this checks object consistency.
        var owner = Utils.GetClientById(sender.OwnerId);
        if (owner?.Character == null || owner.Character.Pointer != sender.Pointer ||
            sender.OwnerId < 0 || sender.Data == null || sender.Data.Disconnected) return;

        var reader = MessageReader.Get(original);
        try
        {
            if (reader.BytesRemaining < 5) return;
            var target = reader.ReadInt32();
            var rpc = reader.ReadByte();
            if (target < -1 || !CustomRpcTransport.IsValidRpcId(rpc)) return;
            var recipient = target == -1 || target == client.ClientId;
            if (!recipient && !client.AmHost) return;

            // Permission is determined by the INNER message, never outer ID 123.
            if (sender.OwnerId != client.HostId && !RPCHandlerPatch.TrustedRpc(rpc))
            {
                Logger.Warn($"Rejected non-host custom RPC {rpc} from owner {sender.OwnerId}", "CustomRPC");
                return;
            }
            RPCHandlerPatch.DispatchCustomRpc(sender, rpc, reader, recipient);
        }
        catch (Exception error)
        {
            // A bad or stale packet must not abort the native receive loop.
            Logger.Warn($"Rejected custom RPC from owner {sender.OwnerId}: {error.GetType().Name}", "CustomRPC");
        }
        finally { reader.Recycle(); }
    }
}
