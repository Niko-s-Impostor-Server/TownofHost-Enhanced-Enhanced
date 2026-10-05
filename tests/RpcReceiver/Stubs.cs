using System;
using System.Collections.Generic;
using System.IO;

namespace Hazel
{
    // Deterministic managed reader fixture: no network, native allocation or packet send.
    internal sealed class MessageReader(byte[] bytes)
    {
        internal readonly byte[] Bytes = bytes;
        internal int Position;
        internal bool ThrowOnRead;
        internal int Recycles;
        internal static readonly List<MessageReader> Rentals = [];
        internal int BytesRemaining => Bytes.Length - Position;
        internal static MessageReader Get(MessageReader original)
        {
            var copy = new MessageReader(original.Bytes) { Position = original.Position, ThrowOnRead = original.ThrowOnRead };
            Rentals.Add(copy);
            return copy;
        }
        internal int ReadInt32()
        {
            if (ThrowOnRead) throw new InvalidDataException("fixture read failure");
            if (BytesRemaining < 4) throw new EndOfStreamException();
            var value = BitConverter.ToInt32(Bytes, Position);
            Position += 4;
            return value;
        }
        internal byte ReadByte()
        {
            if (BytesRemaining < 1) throw new EndOfStreamException();
            return Bytes[Position++];
        }
        internal uint ReadUInt32()
        {
            if (ThrowOnRead) throw new InvalidDataException("fixture read failure");
            if (BytesRemaining < 4) throw new EndOfStreamException();
            var value = BitConverter.ToUInt32(Bytes, Position);
            Position += 4;
            return value;
        }
        internal void Recycle() => Recycles++;
    }
}
namespace TOHE
{
    internal enum RpcCalls : byte { NativeFixtureCall = 1, SendChat = 13, SetRole = 44 }
    internal sealed class PlayerData { internal bool Disconnected; }
    internal sealed class PlayerControl
    {
        internal int OwnerId;
        internal IntPtr Pointer;
        internal PlayerData Data = new();
    }
    internal sealed class ClientData { internal PlayerControl Character; }
    internal sealed class AmongUsClient
    {
        internal static AmongUsClient Instance;
        internal int ClientId, HostId;
        internal bool AmHost;
    }
    internal static class Utils
    {
        internal static readonly Dictionary<int, ClientData> Clients = [];
        internal static ClientData GetClientById(int id) => Clients.GetValueOrDefault(id);
    }
    internal static class Logger
    {
        internal static int Warnings;
        internal static void Warn(string message, string tag) => Warnings++;
    }
    internal static class EAC
    {
        internal static int Calls;
        internal static bool PlayerControlReceiveRpc(PlayerControl player, byte id, Hazel.MessageReader reader) { Calls++; return false; }
    }
    internal static partial class RPCHandlerPatch
    {
        internal sealed record Dispatch(PlayerControl Sender, uint Id, bool Recipient, byte Payload);
        internal static readonly List<Dispatch> Calls = [];
        internal static bool ThrowOnDispatch;
        internal static int NativeValidations;
        private static bool ValidateRpc(PlayerControl player, byte id, Hazel.MessageReader reader) { NativeValidations++; return true; }
        internal static void DispatchCustomRpc(PlayerControl sender, uint id, Hazel.MessageReader reader, bool recipient)
        {
            if (ThrowOnDispatch) throw new InvalidDataException("fixture dispatch failure");
            Calls.Add(new(sender, id, recipient, reader.ReadByte()));
        }
    }
}
