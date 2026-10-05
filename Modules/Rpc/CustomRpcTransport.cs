using AmongUs.InnerNet.GameDataMessages;
using Hazel;
using Il2CppInterop.Runtime.Injection;
using System;

namespace TOHE;

// Main-thread transport. No raw root packet generation or alternate packetizer.
internal static class CustomRpcTransport
{
    private sealed record BuilderState(CustomRPC Rpc, int Target, SendOption Option, int Offset,
        uint NetId, int GameId, int OwnerId, int PlayerInstanceId);
    private static readonly RpcBuilderLeases<MessageWriter, BuilderState> Builders = new(writer => writer.Pointer);
    private static bool registered;
    private static int mainThread;
    private static bool flushing;

    internal static void Register()
    {
        if (registered) { AssertMainThread(); return; }
        ClassInjector.RegisterTypeInIl2Cpp<TOHERpcMessage>();
        mainThread = Environment.CurrentManagedThreadId;
        registered = true;
    }

    internal static bool IsValidRpcId(uint rpc) => Enum.IsDefined(typeof(CustomRPC), rpc);

    private static void AssertMainThread()
    {
        if (!registered) throw new InvalidOperationException("CustomRpcTransport.Register must run before use");
        if (Environment.CurrentManagedThreadId != mainThread) throw new InvalidOperationException("RPC transport requires its registration thread");
    }

    private static (AmongUsClient Client, PlayerControl Player) GetContext()
    {
        AssertMainThread();
        var client = AmongUsClient.Instance;
        var player = PlayerControl.LocalPlayer;
        if (!client || !client.AmConnected || client.ClientId < 0 || !player || !player.AmOwner ||
            player.OwnerId != client.ClientId || player.NetId == uint.MaxValue)
            throw new InvalidOperationException("Connected locally owned PlayerControl required");
        return (client, player);
    }

    private static void ValidateOption(SendOption option)
    {
        if (option != SendOption.Reliable && option != SendOption.None) throw new ArgumentOutOfRangeException(nameof(option));
    }

    internal static MessageWriter Start(CustomRPC rpc, SendOption option = SendOption.Reliable, int target = -1)
    {
        RpcPayloadSnapshot.ValidateTarget(target);
        if (!IsValidRpcId((uint)rpc)) throw new ArgumentOutOfRangeException(nameof(rpc));
        ValidateOption(option);
        var (client, player) = GetContext();
        var writer = MessageWriter.Get(option);
        try
        {
            Builders.Add(writer, new BuilderState(rpc, target, option, writer.Position,
                player.NetId, client.GameId, player.OwnerId, player.GetInstanceID()));
            return writer;
        }
        catch { writer.Recycle(); throw; }
    }

    internal static void Finish(MessageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        AssertMainThread();
        var state = Builders.Take(writer);
        byte[] payload;
        try
        {
            if (writer.Length < state.Offset || writer.Position != writer.Length)
                throw new InvalidOperationException("RPC builder has an invalid payload cursor");
            payload = new byte[writer.Length - state.Offset];
            var buffer = writer.Buffer;
            for (int index = 0; index < payload.Length; index++) payload[index] = buffer[state.Offset + index];
        }
        finally { writer.Recycle(); }
        var (client, player) = GetContext();
        if (client.GameId != state.GameId || player.OwnerId != state.OwnerId || player.NetId != state.NetId ||
            player.GetInstanceID() != state.PlayerInstanceId) throw new InvalidOperationException("RPC builder belongs to an expired room");
        Send(new CustomRpcPayloadMessage(state.NetId, state.GameId, state.OwnerId, state.PlayerInstanceId,
            state.Rpc, state.Target, state.Option, payload));
    }

    internal static void Send(CustomRPC rpc, Action<MessageWriter> serialize, int target = -1, SendOption option = SendOption.Reliable)
    {
        ArgumentNullException.ThrowIfNull(serialize);
        var writer = Start(rpc, option, target);
        try { serialize(writer); Finish(writer); }
        catch
        {
            if (Builders.Cancel(writer)) writer.Recycle();
            throw;
        }
    }

    internal static void Send(TOHERpcMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (client, player) = GetContext();
        if (message.GetType() == typeof(TOHERpcMessage)) throw new ArgumentException("A payload subclass is required", nameof(message));
        if (flushing) throw new InvalidOperationException("RPC serialization must not recursively send messages");
        message.ValidateContext(client, player);
        var option = message.GetSendOption();
        ValidateOption(option);
        var nativeMessage = message.Cast<IGameDataMessage>();
        if (option == SendOption.Reliable) client.LateBroadcastReliableMessage(nativeMessage);
        else client.LateBroadcastUnreliableMessage(nativeMessage);
        var queue = option == SendOption.Reliable ? client.reliableMessageQueue : client.unreliableMessageQueue;
        flushing = true;
        try
        {
            // Ignore the out flag: this game's implementation leaves it false even
            // when MTU packing stops with messages remaining. Require real progress.
            while (queue.Count > 0)
            {
                int before = queue.Count;
                client.PackAndSendQueuedMessages(queue, option, out _);
                if (queue.Count >= before) throw new InvalidOperationException("Native RPC queue made no progress");
            }
        }
        catch
        {
            // Do not leave this failed custom send for a later frame or room.
            // Rotate only to remove our entry; preserve all other native messages.
            int remaining = queue.Count;
            for (int index = 0; index < remaining; index++)
            {
                var pending = queue.Dequeue();
                if (pending.Pointer != nativeMessage.Pointer) queue.Enqueue(pending);
            }
            throw;
        }
        finally { flushing = false; GC.KeepAlive(message); }
    }

    // Invoke at room/plugin teardown to release abandoned builders. Native queues
    // are owned by InnerNetClient and cleared by its JoinGame lifecycle.
    internal static void Reset()
    {
        AssertMainThread();
        Builders.Clear(writer => writer.Recycle());
    }
}
