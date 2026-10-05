# Offline custom RPC receiver regression

Run `dotnet run --project tests/RpcReceiver/RpcReceiver.csproj` from the repository root.

The harness links the real `Modules/CustomRpcReceiver.cs` and `RpcPayloadSnapshot.cs`; the build extracts the current custom enum, `TrustedRpc`, native prefix and ID validation from production sources. Hazel reader, client/player lookup and final dispatch are deterministic offline stubs. Checks cover full int32 target routing, broadcast, host observation with `recipient=false`, non-recipient skip, inner-message permissions, object/character consistency, malformed messages, exceptions and reader recycling. The real prefix is exercised for the outer 123 / legacy SetFriendCode 123 collision and native fallback.

The inner header is signed int32 target plus fixed uint32 message ID (eight bytes). Tests reject every 0..7-byte header, unknown high IDs with a valid low byte, and a former byte-ID envelope without using an alternate decoder. Payload player IDs remain byte. Native RPC arguments and the outer 123 marker also remain byte.

This verifies managed branch behavior and cleanup. It does not prove IL2CPP runtime reader behavior, final RPC business handlers, Nmpostor forwarding or connection authentication. Native `PlayerControl.HandleRpc(byte, MessageReader)` supplies an addressed object and reader, without the originating connection. Matching `OwnerId` to `Character.Pointer` checks object consistency; connection ownership must be enforced by Nmpostor. No network packets are sent.
