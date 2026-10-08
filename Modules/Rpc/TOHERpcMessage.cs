using AmongUs.InnerNet.GameDataMessages;
using Hazel;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using TOHE.Modules;

namespace TOHE;

// Only this concrete class is registered in IL2CPP. Its native callback delegates
// to the managed payload subclass, retained by DerivedConstructorBody's GC bridge.
internal class TOHERpcMessage : BaseGameDataMessage
{
    private readonly uint senderNetId;
    private readonly int gameId;
    private readonly int ownerId;
    private readonly int playerInstanceId;
    private readonly SendOption option;
    private readonly OfficialSessionContext context;
    [HideFromIl2Cpp] internal long Sequence { get; set; }
    [HideFromIl2Cpp] internal virtual byte OuterId => RpcPayloadSnapshot.OuterCallId;
    [HideFromIl2Cpp] internal uint SenderNetId => senderNetId;

    public TOHERpcMessage(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal TOHERpcMessage(uint netId, int gameId, int ownerId, int playerInstanceId, SendOption option)
        : base(ClassInjector.DerivedConstructorPointer<TOHERpcMessage>())
    {
        ClassInjector.DerivedConstructorBody(this);
        senderNetId = netId; this.gameId = gameId; this.ownerId = ownerId;
        this.playerInstanceId = playerInstanceId; this.option = option;
        context = OfficialSessionContext.Capture();
    }

    public override GameDataTypes GameDataType => GameDataTypes.RpcFlag;
    public override void SerializeValues(MessageWriter writer)
    {
        ValidateContext(AmongUsClient.Instance, PlayerControl.LocalPlayer);
        writer.WritePacked(senderNetId);
        writer.Write(OuterId);
        SerializeManagedPayload(writer);
        CustomRpcTransport.RecordSerialized(writer, this);
    }

    [HideFromIl2Cpp]
    protected virtual void SerializeManagedPayload(MessageWriter writer)
        => throw new InvalidOperationException("A managed RPC payload subclass is required");

    [HideFromIl2Cpp]
    internal void ValidateContext(AmongUsClient client, PlayerControl player)
    {
        if (client == null || player == null || !context.IsCurrent() || client.GameId != gameId || client.ClientId != ownerId || player.OwnerId != ownerId ||
            player.NetId != senderNetId || player.GetInstanceID() != playerInstanceId)
            throw new InvalidOperationException("RPC belongs to an expired local player or room");
    }

    [HideFromIl2Cpp]
    internal SendOption GetSendOption() => option;
}

internal sealed class CustomRpcPayloadMessage : TOHERpcMessage
{
    private readonly RpcPayloadSnapshot snapshot;
    [HideFromIl2Cpp] internal RpcPayloadSnapshot Snapshot => snapshot;
    [HideFromIl2Cpp]
    internal CustomRpcPayloadMessage(uint netId, int gameId, int ownerId, int playerInstanceId,
        CustomRPC rpc, int target, SendOption option, byte[] payload)
        : base(netId, gameId, ownerId, playerInstanceId, option)
        => snapshot = new RpcPayloadSnapshot(target, (uint)rpc, payload, CustomRpcTransport.IsValidRpcId);

    [HideFromIl2Cpp]
    protected override void SerializeManagedPayload(MessageWriter writer)
        => snapshot.Write(value => writer.Write(value), value => writer.Write(value),
            bytes => writer.Write(new Il2CppStructArray<byte>(bytes)));
}

internal sealed class PackedTOHERpcMessage : TOHERpcMessage
{
    private readonly byte[] bytes;
    [HideFromIl2Cpp] internal override byte OuterId => RpcPayloadSnapshot.PackedOuterCallId;
    [HideFromIl2Cpp]
    internal PackedTOHERpcMessage(uint netId, int gameId, int ownerId, int playerInstanceId,
        IReadOnlyList<RpcPayloadSnapshot> snapshots)
        : base(netId, gameId, ownerId, playerInstanceId, SendOption.Reliable)
        => bytes = PackedRpcCodec.Encode(snapshots, netId);

    [HideFromIl2Cpp]
    protected override void SerializeManagedPayload(MessageWriter writer)
        => writer.Write(new Il2CppStructArray<byte>(bytes));
}
