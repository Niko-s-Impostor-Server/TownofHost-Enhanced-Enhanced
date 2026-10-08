using Hazel;
using System;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TOHE.Modules;

namespace TOHE;

internal static class CustomRpcReceiver
{
    private static readonly RpcFragmentAssembler Fragments = new();
    private static OfficialSessionContext? context;
    internal static void Reset() { Fragments.Clear(); context = null; }
    private static bool Permitted(PlayerControl sender, uint rpc)
        => CustomRpcTransport.IsValidRpcId(rpc) && rpc != (uint)CustomRPC.Fragment &&
            (sender.OwnerId == AmongUsClient.Instance.HostId || RPCHandlerPatch.TrustedRpc(rpc));

    internal static void Receive(PlayerControl sender, MessageReader original, bool packed = false)
    {
        var client = AmongUsClient.Instance;
        if (client == null || sender == null || original == null) return;
        // HandleRpc exposes the addressed object, not the transport connection.
        // Nmpostor must enforce connection ownership; this checks object consistency.
        var owner = Utils.GetClientById(sender.OwnerId);
        if (owner?.Character == null || owner.Character.Pointer != sender.Pointer ||
            sender.OwnerId < 0 || sender.Data == null) return;

        var reader = MessageReader.Get(original);
        try
        {
            if (context.HasValue && !context.Value.IsCurrent()) Reset();
            context ??= OfficialSessionContext.Capture();
            if (packed)
            {
                if (!RpcCompatibility.SupportsPackedRpc(sender)) throw new InvalidDataException("Undeclared packed custom RPC capability");
                if (reader.BytesRemaining > PackedRpcCodec.ChildBudget) throw new InvalidDataException("Oversized packed custom RPC");
                var bytes = ReadRemaining(reader);
                var entries = PackedRpcCodec.Decode(bytes, sender.NetId, CustomRpcTransport.IsValidRpcId);
                // Validate all framing and privileges before invoking any handler.
                foreach (var entry in entries)
                {
                    if (entry.RpcId == (uint)CustomRPC.Fragment)
                    {
                        var fragmentBytes = entry.CopyPayload();
                        if (fragmentBytes.Length <= PackedRpcCodec.FragmentHeader || !Permitted(sender, BitConverter.ToUInt32(fragmentBytes, 0)))
                            throw new InvalidDataException("Unauthorized custom RPC fragment");
                    }
                    else if (!Permitted(sender, entry.RpcId)) throw new InvalidDataException("Unauthorized custom RPC entry");
                }
                foreach (var entry in entries)
                {
                    if (entry.RpcId == (uint)CustomRPC.Fragment)
                    {
                        if (entry.Target != -1 && entry.Target != client.ClientId && !client.AmHost) continue;
                        var assembled = Fragments.Add(sender.OwnerId, entry, UnityEngine.Time.realtimeSinceStartupAsDouble, rpc => Permitted(sender, rpc));
                        if (assembled != null) Dispatch(sender, assembled);
                    }
                    else Dispatch(sender, entry);
                }
                return;
            }
            if (reader.BytesRemaining < 8) return;
            var target = reader.ReadInt32();
            var rpc = reader.ReadUInt32();
            if (target < -1 || !CustomRpcTransport.IsValidRpcId(rpc)) return;
            if (rpc == (uint)CustomRPC.Fragment) throw new InvalidDataException("Fragment requires packed framing");
            var recipient = target == -1 || target == client.ClientId;
            if (!recipient && !client.AmHost) return;

            // Permission is determined by the INNER message, never outer ID 123.
            if (!Permitted(sender, rpc))
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

    private static byte[] ReadRemaining(MessageReader reader)
    {
        var bytes = new byte[reader.BytesRemaining];
        for (int index = 0; index < bytes.Length; index++) bytes[index] = reader.ReadByte();
        return bytes;
    }

    private static void Dispatch(PlayerControl sender, RpcPayloadSnapshot snapshot)
    {
        var client = AmongUsClient.Instance;
        bool recipient = snapshot.Target == -1 || snapshot.Target == client.ClientId;
        if (!recipient && !client.AmHost) return;
        var reader = MessageReader.Get(new Il2CppStructArray<byte>(snapshot.CopyPayload()));
        try { RPCHandlerPatch.DispatchCustomRpc(sender, snapshot.RpcId, reader, recipient); }
        finally { reader.Recycle(); }
    }
}
