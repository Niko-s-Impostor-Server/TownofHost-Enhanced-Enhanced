using System;
using System.IO;
using System.Linq;
using Hazel;
using TOHE;

static class Program
{
    private static int assertions;
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        assertions++;
    }
    private static PlayerControl Reset(bool host = false, int localId = 65536, int owner = 42)
    {
        AmongUsClient.Instance = new() { ClientId = localId, HostId = 42, AmHost = host };
        Utils.Clients.Clear();
        MessageReader.Rentals.Clear();
        RPCHandlerPatch.Calls.Clear();
        RPCHandlerPatch.ThrowOnDispatch = false;
        RPCHandlerPatch.NativeValidations = EAC.Calls = Logger.Warnings = 0;
        var player = new PlayerControl { OwnerId = owner, Pointer = (IntPtr)1001 };
        Utils.Clients[owner] = new() { Character = player };
        return player;
    }
    private static MessageReader Packet(int target, byte id, bool payload = true)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(target); writer.Write(id);
        if (payload) writer.Write((byte)73);
        return new(stream.ToArray());
    }
    private static void Receive(PlayerControl sender, MessageReader reader)
    {
        var cursor = reader.Position;
        CustomRpcReceiver.Receive(sender, reader);
        Check(reader.Position == cursor && reader.Recycles == 0, "caller reader remains owned and unconsumed by fixture clone");
        Check(MessageReader.Rentals.All(r => r.Recycles == 1), "every rented reader recycled exactly once");
    }
    private static void Main()
    {
        foreach (int target in new[] { -1, 0, 65536, int.MaxValue })
        {
            var sender = Reset(localId: target == -1 ? 65536 : target);
            Receive(sender, Packet(target, (byte)CustomRPC.SyncCustomSettings));
            var call = RPCHandlerPatch.Calls.Single();
            Check(call.Recipient && call.Id == (byte)CustomRPC.SyncCustomSettings && call.Payload == 73,
                "broadcast and full int32 target deliver correct inner ID and payload");
        }
        var hostSender = Reset(host: true, localId: 42);
        Receive(hostSender, Packet(65536, (byte)CustomRPC.PlaySound));
        Check(RPCHandlerPatch.Calls.Single().Recipient == false, "host receives other target with recipient=false");
        hostSender = Reset();
        Receive(hostSender, Packet(7, (byte)CustomRPC.PlaySound));
        Check(RPCHandlerPatch.Calls.Count == 0, "ordinary non-target client skips inner dispatch");
        var remote = Reset(host: true, localId: 42, owner: 7);
        Receive(remote, Packet(-1, (byte)CustomRPC.SyncCustomSettings));
        Check(RPCHandlerPatch.Calls.Count == 0 && Logger.Warnings == 1, "non-host addressed object cannot send host-only inner message");
        remote = Reset(host: true, localId: 42, owner: 7);
        Receive(remote, Packet(42, (byte)CustomRPC.MeetingAbilityRequest));
        Check(RPCHandlerPatch.Calls.Single().Recipient, "TrustedRpc request from non-host object reaches host dispatch");
        foreach (Action<PlayerControl> change in new Action<PlayerControl>[] {
            p => Utils.Clients.Clear(), p => Utils.Clients[p.OwnerId].Character = null,
            p => Utils.Clients[p.OwnerId].Character = new() { Pointer = (IntPtr)2002 },
            p => p.OwnerId = -1, p => p.Data = null, p => p.Data.Disconnected = true })
        {
            var sender = Reset(); change(sender);
            Receive(sender, Packet(-1, (byte)CustomRPC.VersionCheck));
            Check(RPCHandlerPatch.Calls.Count == 0 && MessageReader.Rentals.Count == 0,
                "owner/character inconsistency or invalid player skips before rental");
        }
        // IL2CPP wrappers can differ while naming the same native character pointer.
        hostSender = Reset();
        Utils.Clients[42].Character = new() { OwnerId = 42, Pointer = hostSender.Pointer };
        Receive(hostSender, Packet(-1, (byte)CustomRPC.VersionCheck));
        Check(RPCHandlerPatch.Calls.Count == 1, "same native pointer accepts different wrapper");
        foreach (var packet in new[] { new MessageReader([]), new MessageReader([0, 0, 0, 0]),
            Packet(-2, (byte)CustomRPC.VersionCheck), Packet(-1, 255) })
        {
            var sender = Reset(); Receive(sender, packet);
            Check(RPCHandlerPatch.Calls.Count == 0 && MessageReader.Rentals.Count == 1, "short packet invalid target and unknown inner ID fail closed");
        }
        hostSender = Reset();
        var failedRead = Packet(-1, (byte)CustomRPC.VersionCheck); failedRead.ThrowOnRead = true;
        Receive(hostSender, failedRead);
        Check(Logger.Warnings == 1 && RPCHandlerPatch.Calls.Count == 0, "read exception contained and reader recycled");
        hostSender = Reset(); RPCHandlerPatch.ThrowOnDispatch = true;
        Receive(hostSender, Packet(-1, (byte)CustomRPC.VersionCheck));
        Check(Logger.Warnings == 1, "dispatch exception contained and reader recycled");
        hostSender = Reset();
        Receive(hostSender, Packet(-1, (byte)CustomRPC.VersionCheck, payload: false));
        Check(Logger.Warnings == 1, "truncated inner payload exception contained");
        hostSender = Reset();
        Check((byte)CustomRPC.SetFriendCode == RpcPayloadSnapshot.OuterCallId, "legacy SetFriendCode shares outer 123");
        Check(!RPCHandlerPatch.Prefix(hostSender, 123, Packet(-1, (byte)CustomRPC.VersionCheck)), "outer 123 consumed by production prefix");
        Check(RPCHandlerPatch.Calls.Single().Id == (byte)CustomRPC.VersionCheck && EAC.Calls == 0 && RPCHandlerPatch.NativeValidations == 0,
            "outer123 unwrap precedes legacy ID handling and cannot become SetFriendCode");
        Check(MessageReader.Rentals.Single().Recycles == 1, "prefix envelope path recycles clone");
        hostSender = Reset();
        Check(!RPCHandlerPatch.Prefix(hostSender, (byte)CustomRPC.VersionCheck, new([])) && MessageReader.Rentals.Count == 0,
            "legacy unwrapped inner ID rejected before native validation");
        Check(RPCHandlerPatch.Prefix(hostSender, 1, new([])) && EAC.Calls == 1 && RPCHandlerPatch.NativeValidations == 1,
            "native ID keeps native validation route");
        Check(MessageReader.Rentals.Single().Recycles == 1, "native prefix path recycles clone");
        Console.WriteLine($"RPC_RECEIVER_PASS ({assertions} assertions; linked receiver and extracted production enum/permission/prefix, offline reader and dispatch stubs)");
    }
}
