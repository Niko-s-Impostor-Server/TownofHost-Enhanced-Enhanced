using Hazel;
using InnerNet;
using System;

namespace TOHE;

internal static class ForeignVersionCheck
{
    private static readonly Dictionary<int, (ClientData Client, uint Generation)> Incompatible = [];
    private static readonly Dictionary<int, (ClientData Client, uint Generation)> LotusCandidates = [];

    internal static void Reset() { Incompatible.Clear(); LotusCandidates.Clear(); }
    internal static void Remove(int clientId) { Incompatible.Remove(clientId); LotusCandidates.Remove(clientId); }
    internal static bool IsIncompatible(int clientId)
        => Incompatible.TryGetValue(clientId, out var pending) && OnGameJoinedPatch.IsCurrentClient(pending.Client, pending.Generation);

    internal static void ReceiveEhrVersion(PlayerControl sender, MessageReader original)
    {
        if (!AmongUsClient.Instance.AmHost || Main.VersionCheat.Value || sender == null || sender.AmOwner) return;
        var reader = MessageReader.Get(original);
        try
        {
            var version = reader.ReadString();
            var tag = reader.ReadString();
            var fork = reader.ReadString();
            if (reader.BytesRemaining != 0 || !Version.TryParse(version, out _) || string.IsNullOrWhiteSpace(tag) || fork != "EHR") return;
            Register(sender, "EHR", version);
        }
        catch (Exception error)
        {
            Logger.Warn($"Rejected malformed EHR version check: {error.GetType().Name}", "Version Check");
        }
        finally { reader.Recycle(); }
    }

    internal static void ReceiveLotusRpc(PlayerControl sender, MessageReader original)
    {
        if (!AmongUsClient.Instance.AmHost || Main.VersionCheat.Value || sender == null || sender.AmOwner) return;
        var reader = MessageReader.Get(original);
        try
        {
            uint call = reader.ReadUInt32();
            byte receiver = reader.ReadByte();
            if (reader.ReadPackedUInt32() != sender.NetId) return;
            var client = sender.GetClient();
            var generation = OnGameJoinedPatch.Generation;
            if (!OnGameJoinedPatch.IsCurrentClient(client, generation) || client.Character == null
                || client.Character.Pointer != sender.Pointer) return;

            // Lotus sends its addon list on join. The Vent version handshake is
            // shared by other mods, so only request it after this Lotus signal.
            if (call == 0 && receiver == 1)
            {
                int count = reader.ReadUInt16();
                if (count > reader.BytesRemaining / 16) return;
                for (int i = 0; i < count; i++)
                {
                    reader.ReadUInt64();
                    for (int field = 0; field < 4; field++) reader.ReadString();
                    reader.ReadInt32();
                }
                if (reader.BytesRemaining != 0 || LotusCandidates.ContainsKey(client.Id)) return;
                LotusCandidates[client.Id] = (client, generation);
                var local = PlayerControl.LocalPlayer;
                if (local == null) return;
                var writer = AmongUsClient.Instance.StartImmediate(local.NetId, 203, SendOption.Reliable, client.Id);
                writer.Write((uint)1017);
                writer.Write((byte)2);
                writer.WritePacked(local.NetId);
                AmongUsClient.Instance.FinishImmediate(writer);
            }
            else if (call == 1017 && receiver == 3 && LotusCandidates.TryGetValue(client.Id, out var candidate)
                && candidate.Client.Pointer == client.Pointer && OnGameJoinedPatch.IsCurrentClient(candidate.Client, candidate.Generation))
            {
                if (reader.ReadByte() != 1) return; // Lotus uses VentLib's GitVersion.
                string major = reader.ReadString();
                string minor = reader.ReadString();
                string patch = reader.ReadString();
                for (int field = 0; field < 4; field++) reader.ReadString();
                string version = $"{major}.{minor}.{patch}";
                if (reader.BytesRemaining == 0 && Version.TryParse(version, out _)) Register(sender, "Project Lotus", version);
            }
        }
        catch (Exception error)
        {
            Logger.Warn($"Rejected malformed Lotus RPC: {error.GetType().Name}", "Version Check");
        }
        finally { reader.Recycle(); }
    }

    private static void Register(PlayerControl sender, string mod, string version)
    {
        var client = sender.GetClient();
        var generation = OnGameJoinedPatch.Generation;
        if (!OnGameJoinedPatch.IsCurrentClient(client, generation) || client.Character == null
            || client.Character.Pointer != sender.Pointer || IsIncompatible(client.Id)) return;
        Incompatible[client.Id] = (client, generation);
        Logger.Info($"Detected {mod} {version} from client {client.Id}", "Version Check");
        RPCHandlerPatch.ScheduleVersionMismatchKick(sender);
    }
}
