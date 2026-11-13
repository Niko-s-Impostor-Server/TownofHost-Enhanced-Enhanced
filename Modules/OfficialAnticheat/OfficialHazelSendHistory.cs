using Hazel;
using Hazel.Udp;
using InnerNet;
using System;
using System.Text.Json;
using TOHE.Modules;

namespace TOHE;

internal static class OfficialHazelSendHistory
{
    private sealed record Entry(long Timestamp, DateTime AtUtc, byte[] Bytes, bool Received);
    private static readonly Queue<Entry> Messages = new();
    private static readonly object Sync = new();

    internal static void Reset()
    {
        lock (Sync) Messages.Clear();
    }

    private static void Trim(long now)
    {
        while (Messages.Count > 0 && now - Messages.Peek().Timestamp > 10_000)
            Messages.Dequeue();
    }

    internal static void OnDisconnect(DisconnectReasons reason)
    {
        if (reason == DisconnectReasons.NewConnection) return;
        if ((reason is DisconnectReasons.Hacking or DisconnectReasons.Custom) &&
            AmongUsClient.Instance.NetworkMode == NetworkModes.OnlineGame)
            Dump(clear: true);
        else
            Reset();
    }

    internal static void Dump(bool clear = false)
    {
        Entry[] snapshot;
        lock (Sync)
        {
            Trim(Environment.TickCount64);
            snapshot = Messages.ToArray();
            if (clear) Messages.Clear();
        }

        // Keep the original send fields so existing dump readers can still parse them.
        string capture = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            messages = snapshot.Where(message => !message.Received)
                .Select(message => new { sentAtUtc = message.AtUtc, bytes = message.Bytes }),
            receivedMessages = snapshot.Where(message => message.Received)
                .Select(message => new { receivedAtUtc = message.AtUtc, bytes = message.Bytes })
        }));
#if ANDROID
        // Keep each logcat line below Android's entry size limit.
        const int chunkSize = 3000;
        if (capture.Length > chunkSize)
        {
            var id = Guid.NewGuid();
            int total = (capture.Length + chunkSize - 1) / chunkSize;
            for (int index = 0; index < total; index++)
            {
                int offset = index * chunkSize;
                string chunk = capture.Substring(offset, Math.Min(chunkSize, capture.Length - offset));
                Logger.Warn($"Hazel message logger captured message {id:N} part {index + 1}/{total}: {chunk}",
                    nameof(OfficialHazelSendHistory));
            }
            return;
        }
#endif
        Logger.Warn($"Hazel message logger captured message: {capture}", nameof(OfficialHazelSendHistory));
    }

    private static bool IsGameConnection(Connection connection)
    {
        var client = AmongUsClient.Instance;
        return client != null && client.NetworkMode == NetworkModes.OnlineGame &&
            client.connection != null && client.connection.Pointer == connection.Pointer;
    }

    private static void Record(byte[] bytes, bool received)
    {
        lock (Sync)
        {
            long now = Environment.TickCount64;
            Trim(now);
            Messages.Enqueue(new(now, DateTime.UtcNow, bytes, received));
        }
    }

    // Both plain UDP and DTLS clients inherit this Send implementation.
    // Capture successful MessageWriter submissions once, before caller recycling.
    // Bytes include the writer header, before transport encryption/retransmission.
    [HarmonyPatch(typeof(UdpConnection), nameof(UdpConnection.Send), typeof(MessageWriter))]
    private static class SendPatch
    {
        private static void Postfix(UdpConnection __instance, [HarmonyArgument(0)] MessageWriter message, SendErrors __result)
        {
            if (__result != SendErrors.None || !IsGameConnection(__instance)) return;
            Record(OfficialPacketBuilder.Snapshot(message), received: false);
        }
    }

    [HarmonyPatch(typeof(Connection), nameof(Connection.InvokeDataReceived), typeof(MessageReader), typeof(SendOption))]
    private static class ReceivePatch
    {
        private static void Prefix(Connection __instance, [HarmonyArgument(0)] MessageReader message)
        {
            if (!IsGameConnection(__instance)) return;

            // Hazel has already decrypted the packet. Offset skips the Hazel header,
            // which is still in the buffer. Copy it too, before the game recycles the reader.
            var buffer = message.Buffer;
            var bytes = new byte[message.Offset + message.Length];
            for (int index = 0; index < bytes.Length; index++) bytes[index] = buffer[index];
            Record(bytes, received: true);
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.Update))]
    private static class UpdatePatch
    {
        private static void Postfix()
        {
            lock (Sync) Trim(Environment.TickCount64);
        }
    }
}
