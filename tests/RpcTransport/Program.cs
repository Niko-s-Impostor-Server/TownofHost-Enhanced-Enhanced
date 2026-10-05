using System;
using System.IO;
using TOHE;

static void Require(bool condition, string label) { if (!condition) throw new Exception(label); }
static void Throws(Action action, string label)
{
    try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
    throw new Exception(label);
}

foreach (int target in new[] { -1, 0, 65536, int.MaxValue })
{
    byte[] pooledBuffer = [7, 9, 11];
    var snapshot = new RpcPayloadSnapshot(target, 80, pooledBuffer, id => id == 80);
    Array.Fill(pooledBuffer, (byte)0);
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    snapshot.Write(value => writer.Write(value), value => writer.Write(value), bytes => { writer.Write(bytes); Array.Fill(bytes, (byte)99); });
    byte[] encoded = stream.ToArray();
    Require(encoded.Length == 8 && BitConverter.ToInt32(encoded, 0) == target && encoded[4] == 80 &&
        encoded[5] == 7 && encoded[6] == 9 && encoded[7] == 11, "int32 target/byte id/payload encoding");
    using var repeated = new MemoryStream();
    using var repeatedWriter = new BinaryWriter(repeated);
    snapshot.Write(value => repeatedWriter.Write(value), value => repeatedWriter.Write(value), bytes => repeatedWriter.Write(bytes));
    Require(Convert.ToHexString(encoded) == Convert.ToHexString(repeated.ToArray()), "snapshot survives pool reuse and serialization callback mutation");
}
Throws(() => new RpcPayloadSnapshot(-2, 80, [], _ => true), "target below broadcast sentinel rejected");
Throws(() => new RpcPayloadSnapshot(-1, 81, [], id => id == 80), "unknown custom id rejected");
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
Console.WriteLine("RPC envelope encoding, target/id validation, immutable payload, writer reuse and teardown checks passed.");
sealed record FakeWriter(IntPtr Pointer);
