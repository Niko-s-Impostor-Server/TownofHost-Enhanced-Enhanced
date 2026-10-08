using System;
using System.Collections.Generic;
using AmongUs.InnerNet.GameDataMessages;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using UnityEngine;
using NativeQueue = Il2CppSystem.Collections.Generic.Queue<AmongUs.InnerNet.GameDataMessages.IGameDataMessage>;
using NativeObjects = Il2CppSystem.Collections.Generic.IReadOnlyList<InnerNet.InnerNetObject>;
using BoundedWriter = TOHE.OfficialPacketBuilder.BoundedWriter;

namespace TOHE;

// These prefixes replace native entry points. A transpiler of the generated
// interop wrapper would only change the managed trampoline, not the game loop.
internal static class OfficialNativePacking
{
    internal const int SoftLimit = 1000;
    internal const int HardLimit = 1200;
    private static readonly HashSet<IntPtr> FailedClients = new();

    internal static void Reset() => FailedClients.Clear();
    internal static bool Failed(InnerNetClient client) => FailedClients.Contains(client.Pointer);

    private static void Abort(InnerNetClient client, string source, Exception error)
    {
        if (!FailedClients.Add(client.Pointer)) return;
        // Exception messages can contain serialized player data. Log only type/source.
        Logger.Error($"Native packet operation aborted: source={source}, error={error.GetType().Name}", "OfficialNativePacking");
        client.EnqueueDisconnect(DisconnectReasons.Error, "Official compatibility packet could not be serialized safely.");
    }

    // Known native serializers that consume state during Serialize must be
    // transactional too; restoring only DirtyBits loses movement on overflow.
    private sealed class ObjectState
    {
        private readonly InnerNetObject obj;
        private readonly uint dirty;
        private readonly PlayerControl player;
        private readonly bool serialized;
        private readonly CustomNetworkTransform transform;
        private readonly ushort sequence;
        private readonly Vector2 position;
        private readonly Vector2[] movements;

        internal ObjectState(InnerNetObject obj)
        {
            this.obj = obj;
            dirty = obj.DirtyBits;
            player = obj.TryCast<PlayerControl>();
            if (player) serialized = player.hasBeenSerialized;
            transform = obj.TryCast<CustomNetworkTransform>();
            if (!transform) return;
            sequence = transform.lastSequenceId;
            position = transform.lastPosSent;
            if (transform.sendQueue.Count > HardLimit)
                throw new InvalidOperationException("Native movement snapshot exceeds bounded capacity");
            movements = new Vector2[transform.sendQueue.Count];
            int index = 0;
            foreach (var movement in transform.sendQueue) movements[index++] = movement;
        }

        internal void Restore()
        {
            if (!obj) return;
            obj.DirtyBits = dirty;
            if (player) player.hasBeenSerialized = serialized;
            if (!transform) return;
            transform.lastSequenceId = sequence;
            transform.lastPosSent = position;
            transform.sendQueue.Clear();
            foreach (var movement in movements) transform.sendQueue.Enqueue(movement);
        }
    }

    private sealed class SerializationState
    {
        private readonly List<ObjectState> states = new();
        internal SerializationState(InnerNetObject obj) => states.Add(new ObjectState(obj));
        internal SerializationState(IGameDataMessage message)
        {
            var spawn = message.TryCast<SpawnGameDataMessage>();
            if (spawn == null) return;
            foreach (var obj in spawn.childNetObjects)
                if (obj) states.Add(new ObjectState(obj));
        }
        internal void Restore()
        {
            foreach (var state in states) state.Restore();
        }
    }

    private static MessageWriter StartRoot(BoundedWriter owner, InnerNetClient client, int target = -1)
    {
        var writer = owner.Writer;
        writer.StartMessage(target < 0 ? (byte)5 : (byte)6);
        writer.Write(client.GameId);
        if (target >= 0) writer.WritePacked(target);
        return writer;
    }

    private static void Send(InnerNetClient client, MessageWriter writer, int gameId, int hostId)
    {
        if (!client.AmConnected || client.GameId != gameId || client.HostId != hostId || Failed(client))
            throw new InvalidOperationException("Native packet context expired");
        writer.EndMessage();
        if (writer.Length > HardLimit) throw new InvalidOperationException("Native packet exceeds MTU");
        OfficialNetworkSend.Send(client, writer);
        // The bridge already checked the synchronous SendErrors result.
        if (!client.AmConnected || client.GameId != gameId || client.HostId != hostId)
            throw new InvalidOperationException("Native packet send failed");
    }

    private static bool Append(MessageWriter packet, MessageWriter child, int childOffset, bool first)
    {
        int length = child.Length - childOffset;
        if (length <= 0) throw new InvalidOperationException("Native message serialized no child");
        if (packet.Length + length > HardLimit)
        {
            if (first) throw new InvalidOperationException("Indivisible native child exceeds MTU");
            return false;
        }
        packet.Write(child.Buffer, childOffset, length);
        CustomRpcTransport.CommitNativeChild(child, packet);
        return true;
    }

    private static bool AppendOrRestore(MessageWriter packet, MessageWriter child, int offset, bool first, SerializationState before)
    {
        try
        {
            if (Append(packet, child, offset, first)) return true;
        }
        catch { before.Restore(); throw; }
        before.Restore();
        return false;
    }

    internal static void PackQueue(InnerNetClient client, NativeQueue queue, SendOption option, out bool remaining)
    {
        remaining = queue.Count > 0;
        if (!remaining || Failed(client)) return;
        int gameId = client.GameId, hostId = client.HostId;
        var pending = new List<IGameDataMessage>();
        var rollback = new List<SerializationState>();
        using var root = new BoundedWriter(option);
        var writer = StartRoot(root, client);
        try
        {
            // Snapshot enumeration leaves the real FIFO intact until send succeeds.
            foreach (var message in queue)
            {
                if (pending.Count >= OfficialAnticheatPolicy.PackingLimit) break;
                using var trial = new BoundedWriter(option);
                int offset = trial.Writer.Position;
                var before = new SerializationState(message);
                try { message.Serialize(trial.Writer); }
                catch { before.Restore(); throw; }
                if (!AppendOrRestore(writer, trial.Writer, offset, pending.Count == 0, before))
                {
                    break;
                }
                pending.Add(message);
                rollback.Add(before);
                if (writer.Length > SoftLimit) break;
            }
            if (pending.Count == 0) return;
            Send(client, writer, gameId, hostId);
            foreach (var message in pending)
            {
                if (queue.Count == 0 || queue.Peek().Pointer != message.Pointer)
                    throw new InvalidOperationException("Native FIFO changed during packet send");
                queue.Dequeue();
                message.TryCast<SpawnGameDataMessage>()?.ClearOrDecrementChildObjectDirt();
            }
            rollback.Clear();
        }
        catch (Exception error)
        {
            foreach (var state in rollback) state.Restore();
            Abort(client, "queued", error);
        }
        finally { remaining = queue.Count > 0; }
    }

    internal static void PackDirty(InnerNetClient client, NativeObjects objects, SendOption option, ref int trackingIndex)
    {
        int objectCount = objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<InnerNetObject>>().Count;
        if (objectCount == 0 || Failed(client)) return;
        int originalIndex = trackingIndex;
        int nextIndex = trackingIndex >= objectCount || trackingIndex < 0 ? 0 : trackingIndex;
        int gameId = client.GameId, hostId = client.HostId;
        var pending = new List<InnerNetObject>();
        var rollback = new List<SerializationState>();
        using var root = new BoundedWriter(option);
        var writer = StartRoot(root, client);
        try
        {
            for (int scanned = 0; scanned < objectCount && pending.Count < OfficialAnticheatPolicy.PackingLimit; scanned++)
            {
                int currentIndex = nextIndex;
                var obj = objects[currentIndex];
                nextIndex = (nextIndex + 1) % objectCount;
                if (!obj || !obj.IsDirty || !(obj.AmOwner || obj.OwnerId == -2 && client.AmHost)) continue;
                using var trial = new BoundedWriter(option);
                int offset = trial.Writer.Position;
                var before = new SerializationState(obj);
                bool wrote;
                try
                {
                    trial.Writer.StartMessage(1);
                    trial.Writer.WritePacked(obj.NetId);
                    wrote = obj.Serialize(trial.Writer, false);
                    if (wrote) trial.Writer.EndMessage();
                }
                catch { before.Restore(); throw; }
                // Native false means there is no network update (for example an
                // inactive transform clears its own obsolete movement dirt).
                if (!wrote) continue;
                if (!AppendOrRestore(writer, trial.Writer, offset, pending.Count == 0, before))
                {
                    nextIndex = currentIndex;
                    break;
                }
                pending.Add(obj);
                rollback.Add(before);
                if (writer.Length > SoftLimit) break;
            }
            if (pending.Count > 0)
            {
                Send(client, writer, gameId, hostId);
                foreach (var obj in pending) if (obj) obj.ClearOrDecrementDirt();
                rollback.Clear();
            }
            trackingIndex = nextIndex;
        }
        catch (Exception error)
        {
            foreach (var state in rollback) state.Restore();
            trackingIndex = originalIndex;
            Abort(client, "dirty", error);
        }
    }

    internal static void SendInitial(InnerNetClient client, int target)
    {
        if (Failed(client)) return;
        try
        {
            client.sendInitialDataSpawnGameDataMessages.Clear();
            var seen = new HashSet<IntPtr>();
            var objects = client.allObjects.AllObjects;
            int objectCount = objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<InnerNetObject>>().Count;
            for (int i = 0; i < objectCount; i++)
            {
                var obj = objects[i];
                if (!obj || obj.OwnerId == -4 && !client.AmModdedHost || !seen.Add(obj.gameObject.Pointer)) continue;
                var manager = obj.TryCast<GameManager>();
                if (manager != null)
                {
                    SendManager(client, target, manager);
                    if (Failed(client) || OfficialNetworkSend.Failed(client)) return;
                    continue;
                }
                client.sendInitialDataSpawnGameDataMessages.Add(client.CreateSpawnMessage(obj, obj.OwnerId, obj.SpawnFlags));
            }
            int index = 0;
            while (index < client.sendInitialDataSpawnGameDataMessages.Count)
            {
                int gameId = client.GameId, hostId = client.HostId;
                using var root = new BoundedWriter(SendOption.Reliable);
                var writer = StartRoot(root, client, target);
                var pending = new List<SpawnGameDataMessage>();
                var rollback = new List<SerializationState>();
                try
                {
                    while (index < client.sendInitialDataSpawnGameDataMessages.Count && pending.Count < OfficialAnticheatPolicy.PackingLimit)
                    {
                        var message = client.sendInitialDataSpawnGameDataMessages[index];
                        using var trial = new BoundedWriter(SendOption.Reliable);
                        int offset = trial.Writer.Position;
                        var before = new SerializationState(message.Cast<IGameDataMessage>());
                        try { message.Serialize(trial.Writer); }
                        catch { before.Restore(); throw; }
                        if (!AppendOrRestore(writer, trial.Writer, offset, pending.Count == 0, before))
                        {
                            break;
                        }
                        pending.Add(message);
                        rollback.Add(before);
                        index++;
                        if (writer.Length > SoftLimit) break;
                    }
                    Send(client, writer, gameId, hostId);
                    foreach (var message in pending) message.ClearOrDecrementChildObjectDirt();
                    rollback.Clear();
                }
                finally { foreach (var state in rollback) state.Restore(); }
            }
        }
        catch (Exception error) { Abort(client, "initial-spawn", error); }
    }

    internal static void SendManager(InnerNetClient client, int target, GameManager manager)
    {
        if (Failed(client)) return;
        SerializationState before = null;
        try
        {
            int gameId = client.GameId, hostId = client.HostId;
            var message = client.CreateSpawnMessage(manager, manager.OwnerId, manager.SpawnFlags);
            before = new SerializationState(message.Cast<IGameDataMessage>());
            using var root = new BoundedWriter(SendOption.Reliable);
            var writer = StartRoot(root, client, target);
            message.Serialize(writer);
            Send(client, writer, gameId, hostId);
            message.ClearOrDecrementChildObjectDirt();
        }
        catch (Exception error) { before?.Restore(); Abort(client, "initial-manager", error); }
    }
}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.GetMaxMessagePackingLimit))]
internal static class OfficialPackingLimitPatch
{
    public static bool Prefix(ref int __result)
    {
        if (!OfficialAnticheatPolicy.Enabled) return true;
        __result = OfficialAnticheatPolicy.PackingLimit;
        return false;
    }
}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.PackAndSendQueuedMessages))]
internal static class OfficialQueuedMessagesPatch
{
    public static bool Prefix(InnerNetClient __instance, [HarmonyArgument(0)] NativeQueue queue,
        [HarmonyArgument(1)] SendOption sendOption, [HarmonyArgument(2)] ref bool thereAreQueuedMessagesLeft)
    {
        if (!OfficialAnticheatPolicy.Enabled) return true;
        OfficialNativePacking.PackQueue(__instance, queue, sendOption, out thereAreQueuedMessagesLeft);
        return false;
    }
}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.PackAndSendDirtyStreamedObjects))]
internal static class OfficialDirtyObjectsPatch
{
    public static bool Prefix(InnerNetClient __instance, [HarmonyArgument(0)] NativeObjects streamCollection,
        [HarmonyArgument(1)] SendOption sendOption, [HarmonyArgument(2)] ref int trackingIndex)
    {
        if (!OfficialAnticheatPolicy.Enabled) return true;
        // During the start mask only its immutable reliable snapshots may send.
        if (RoleDistribution.IsMaskActive) return false;
        OfficialNativePacking.PackDirty(__instance, streamCollection, sendOption, ref trackingIndex);
        return false;
    }
}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendInitialData))]
internal static class OfficialInitialSpawnPatch
{
    public static bool Prefix(InnerNetClient __instance, [HarmonyArgument(0)] int clientId)
    {
        if (!OfficialAnticheatPolicy.Enabled) return true;
        OfficialNativePacking.SendInitial(__instance, clientId);
        return false;
    }
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
internal static class OfficialNativePackingResetPatch
{
    public static void Prefix()
    {
        OfficialNativePacking.Reset();
        OfficialNetworkSend.Reset();
    }
}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendGameManager))]
internal static class OfficialInitialManagerPatch
{
    public static bool Prefix(InnerNetClient __instance, [HarmonyArgument(0)] int clientId,
        [HarmonyArgument(1)] GameManager gameManager)
    {
        if (!OfficialAnticheatPolicy.Enabled) return true;
        OfficialNativePacking.SendManager(__instance, clientId, gameManager);
        return false;
    }
}
