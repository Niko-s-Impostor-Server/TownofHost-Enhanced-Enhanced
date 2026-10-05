using System;
using System.IO;
using TOHE;

int assertions = 0;
void Require(bool condition, string label) { if (!condition) throw new Exception(label); assertions++; }
void Throws(Action action, string label)
{
    try { action(); } catch (ArgumentException) { assertions++; return; } catch (InvalidOperationException) { assertions++; return; }
    throw new Exception(label);
}

foreach (int target in new[] { -1, 0, 65536, int.MaxValue })
foreach (uint rpcId in new[] { 80u, 0x01020304u, uint.MaxValue })
{
    byte[] pooledBuffer = [7, 9, 11];
    var snapshot = new RpcPayloadSnapshot(target, rpcId, pooledBuffer, id => id == rpcId);
    Array.Fill(pooledBuffer, (byte)0);
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    snapshot.Write(value => writer.Write(value), value => writer.Write(value), bytes => { writer.Write(bytes); Array.Fill(bytes, (byte)99); });
    byte[] encoded = stream.ToArray();
    Require(encoded.Length == 11 && BitConverter.ToInt32(encoded, 0) == target && BitConverter.ToUInt32(encoded, 4) == rpcId &&
        encoded[8] == 7 && encoded[9] == 9 && encoded[10] == 11, "fixed int32 target/uint32 id/byte payload encoding without truncation");
    Require(encoded[4] == (byte)rpcId && encoded[5] == (byte)(rpcId >> 8) && encoded[6] == (byte)(rpcId >> 16) && encoded[7] == (byte)(rpcId >> 24),
        "inner ID occupies exactly four little-endian bytes");
    using var repeated = new MemoryStream();
    using var repeatedWriter = new BinaryWriter(repeated);
    snapshot.Write(value => repeatedWriter.Write(value), value => repeatedWriter.Write(value), bytes => repeatedWriter.Write(bytes));
    Require(Convert.ToHexString(encoded) == Convert.ToHexString(repeated.ToArray()), "snapshot survives pool reuse and serialization callback mutation");
}
Throws(() => new RpcPayloadSnapshot(-2, 80, [], _ => true), "target below broadcast sentinel rejected");
Throws(() => new RpcPayloadSnapshot(-1, 81, [], id => id == 80), "unknown custom id rejected");
Throws(() => new RpcPayloadSnapshot(-1, 0x01000050u, [], id => id == 80u), "high unknown ID cannot alias its valid low byte");
Require(RpcPayloadSnapshot.OuterCallId == (byte)123, "native outer RPC remains byte 123");
var leases = new RpcBuilderLeases<FakeWriter, int>(writer => writer.Pointer);
var original = new FakeWriter((IntPtr)17);
leases.Add(original, 123);
Require(leases.Take(original) == 123, "lease metadata returned");
Throws(() => leases.Take(original), "double finish rejected");
var rerented = new FakeWriter(original.Pointer);
leases.Add(rerented, 456);
Throws(() => leases.Take(original), "old wrapper cannot finish a recycled native writer");
Require(!leases.Cancel(original), "stale cancel cannot recycle current rental");
Require(leases.Cancel(rerented) && !leases.Cancel(rerented), "exception cleanup consumes lease once");
int recycled = 0;
leases.Add(new FakeWriter((IntPtr)18), 1);
leases.Add(new FakeWriter((IntPtr)19), 2);
leases.Clear(_ => recycled++);
leases.Clear(_ => recycled++);
Require(recycled == 2, "teardown recycles abandoned builders exactly once");
Console.WriteLine($"RPC_TRANSPORT_PASS ({assertions} assertions; fixed uint32 inner IDs, immutable payload and writer lifetime)");
sealed record FakeWriter(IntPtr Pointer);
