# RPC123 transport checks

Run the offline protocol/lifetime harness:

```powershell
dotnet run --project tests/RpcTransport/RpcTransport.csproj -c Release -p:CheckEolTargetFramework=false
```

Compile the actual transport against the local IL2CPP interop APIs without loading the game:

```powershell
dotnet build tests/RpcTransport/NativeCompile/NativeCompile.csproj -c Release
```

The harness links the production payload snapshot and writer-lease helpers. It checks signed int32 targets (including broadcast), byte IDs, detached immutable payloads, serialization callback mutation, double Finish, stale wrappers after native-pointer reuse, and teardown recycling. NativeCompile uses a small enum stand-in and never initializes IL2CPP.

These checks do not prove runtime class injection, native GC/virtual dispatch, actual network delivery, recipient authority, or in-game ordering. The main thread must call `CustomRpcTransport.Register()` before sending and `Reset()` at teardown. Every custom send enters the official reliable/unreliable queue and immediately flushes with the original packetizer; no root packet or alternate packetizer is constructed. Transport delivery does not apply local gameplay state.
