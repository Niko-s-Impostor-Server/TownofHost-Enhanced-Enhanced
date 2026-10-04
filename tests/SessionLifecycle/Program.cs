using System;
using System.Linq;

static class Program
{
    static int assertions;
    internal static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        assertions++;
    }
    static AmongUsClient Reset(bool loaded = true)
    {
        global::Main.Instance.Clear();
        Check(RPC.PendingCountForTest == 0, "fixture disposal releases pending requests");
        UnityEngine.Time.realtimeSinceStartup = 0;
        PlayerControl.LocalPlayer = null;
        global::Main.playerVersion.Clear();
        global::Main.VersionCheat.Value = false;
        Logger.Warnings.Clear();
        Logger.Errors.Clear();
        Options.IsLoaded = loaded;
        var client = new AmongUsClient { HostId = 1, AmConnected = true };
        client.Clients[1] = new ClientData { Id = 1, Pointer = new IntPtr(100) };
        AmongUsClient.Instance = client;
        OnGameJoinedPatch.Postfix(client);
        return client;
    }
    static void Ready() => PlayerControl.LocalPlayer = new PlayerControl { ClientId = 7, NetId = 42 };
    static void At(float seconds)
    {
        UnityEngine.Time.realtimeSinceStartup = seconds;
        global::Main.Instance.Tick();
    }

    static void GenerationCancels()
    {
        var client = Reset();
        var oldGeneration = OnGameJoinedPatch.Generation;
        Check(OnGameJoinedPatch.IsCurrentSession(oldGeneration), "connected current generation accepted");
        RPC.RpcVersionCheck();
        Check(global::Main.Instance.Count == 1 && RPC.PendingCountForTest == 1, "not-ready request waits");
        var replacement = new AmongUsClient { HostId = 1, AmConnected = true };
        replacement.Clients[1] = client.Clients[1];
        AmongUsClient.Instance = replacement;
        OnGameJoinedPatch.Postfix(replacement);
        Ready();
        At(0.1f);
        Check(!OnGameJoinedPatch.IsCurrentSession(oldGeneration), "old generation rejected after new join");
        Check(global::Main.Instance.Count == 0 && RPC.PendingCountForTest == 0, "new join cancels and releases old wait");
        Check(client.Started.Count == 0 && replacement.Started.Count == 0, "old request cannot send into either room");
        RPC.RpcVersionCheck();
        Check(replacement.Finished.Count == 1, "new generation can exchange independently");
    }

    static void DisconnectCancels()
    {
        var client = Reset();
        var generation = OnGameJoinedPatch.Generation;
        RPC.RpcRequestRetryVersionCheck();
        client.AmConnected = false;
        Ready();
        At(0.1f);
        Check(!OnGameJoinedPatch.IsCurrentSession(generation), "disconnected session rejected");
        Check(client.Started.Count == 0 && global::Main.Instance.Count == 0 && RPC.PendingCountForTest == 0,
            "disconnect cancels wait without allocating a writer");
        client.AmConnected = true;
        At(0.2f);
        Check(client.Finished.Count == 0, "terminated wait never resumes on reconnect");
        AmongUsClient.Instance = null;
        Check(!OnGameJoinedPatch.IsCurrentSession(generation), "missing client rejected");
    }

    static void HostTransferCancels()
    {
        var client = Reset();
        RPC.RpcRequestRetryVersionCheck();
        client.HostId = 2;
        client.Clients[2] = new ClientData { Id = 2, Pointer = new IntPtr(200) };
        Ready();
        At(0.1f);
        Check(client.Started.Count == 0 && global::Main.Instance.Count == 0 && RPC.PendingCountForTest == 0,
            "host transfer cancels old destination and releases key");
        RPC.RpcRequestRetryVersionCheck();
        Check(client.Finished.Count == 1 && client.Finished[0].TargetId == 2,
            "fresh retry targets current host after transfer");
    }

    static void TimeoutStops()
    {
        var client = Reset();
        RPC.RpcVersionCheck();
        At(7.99f);
        Check(global::Main.Instance.Count == 1 && client.Started.Count == 0, "request waits before eight-second deadline");
        Ready();
        At(8f);
        Check(client.Started.Count == 0 && global::Main.Instance.Count == 0 && RPC.PendingCountForTest == 0,
            "deadline rejects even newly-ready data without sending");
        Check(Logger.Warnings.Count == 1 && Logger.Warnings[0].Contains("timed out"), "timeout reported once");
        At(100f);
        Check(Logger.Warnings.Count == 1 && client.Started.Count == 0, "timeout schedules no hidden retry");
        RPC.RpcVersionCheck();
        Check(client.Finished.Count == 1, "timeout frees key for an explicit new request");
    }

    static void ReadySendsOnceAndSessionIdentity()
    {
        var client = Reset();
        var generation = OnGameJoinedPatch.Generation;
        var original = client.Clients[1];
        Check(OnGameJoinedPatch.IsCurrentClient(original, generation), "current native client identity accepted");
        client.Clients[1] = new ClientData { Id = 1, Pointer = new IntPtr(101) };
        Check(!OnGameJoinedPatch.IsCurrentClient(original, generation), "reused numeric client id cannot match old pointer");
        Check(!OnGameJoinedPatch.IsCurrentClient(null, generation), "missing captured client rejected");
        Ready();
        RPC.RpcVersionCheck();
        var writer = client.Finished.Single();
        Check(writer.CallId == (byte)CustomRPC.VersionCheck && writer.TargetId == -1 && writer.NetId == 42
            && writer.SendOption == SendOption.Reliable, "ready version uses correct RPC, source, destination and reliability");
        Check(writer.Values.SequenceEqual(new object[] { global::Main.PluginVersion, "fixture(fixture)", global::Main.ForkId, false }),
            "normal version payload contains the four expected fields in order");
        Check(global::Main.playerVersion[7].version.ToString() == global::Main.PluginVersion, "successful path records local version");
        At(20f);
        Check(client.Finished.Count == 1 && RPC.PendingCountForTest == 0, "finished ready request sends exactly once");

        client = Reset(loaded: false);
        Check(global::Main.Instance.Count == 1 && client.Initializations == 0, "join waits while options are unavailable");
        Options.IsLoaded = true;
        At(1f);
        At(2f);
        Check(client.Initializations == 1 && global::Main.Instance.Count == 0, "current join initializes once after options become ready");
        client = Reset(loaded: false);
        At(10f);
        Check(client.Initializations == 0 && client.Exits == 1 && Logger.Errors.Count == 1,
            "options-load deadline exits only this waiting session");
    }

    static void DeduplicationAndRelease()
    {
        var client = Reset();
        RPC.RpcVersionCheck();
        RPC.RpcVersionCheck();
        Check(global::Main.Instance.Count == 1 && RPC.PendingCountForTest == 1, "same session/type has only one pending wait");
        RPC.RpcRequestRetryVersionCheck();
        RPC.RpcRequestRetryVersionCheck();
        Check(global::Main.Instance.Count == 2 && RPC.PendingCountForTest == 2, "retry type waits independently and also deduplicates");
        Ready();
        At(0.1f);
        Check(client.Finished.Count == 2 && client.Finished.Select(w => w.CallId).Distinct().Count() == 2,
            "one version and one retry are sent when pending types become ready");
        Check(client.Finished.Single(w => w.CallId == (byte)CustomRPC.RequestRetryVersionCheck).Values.Count == 0,
            "retry payload has no version fields");
        Check(global::Main.Instance.Count == 0 && RPC.PendingCountForTest == 0, "completion releases both keys");
        RPC.RpcVersionCheck();
        Check(client.Finished.Count == 3, "completed key allows a subsequent explicit exchange");
        client.FailFinish = true;
        RPC.RpcVersionCheck();
        Check(RPC.PendingCountForTest == 0 && Logger.Warnings.Count == 1 && global::Main.Instance.Count == 0,
            "writer failure also releases key without scheduling retries");
        client.FailFinish = false;
        RPC.RpcVersionCheck();
        Check(client.Finished.Count == 4, "request remains usable after a failed writer");
    }

    public static void Main()
    {
        GenerationCancels();
        DisconnectCancels();
        HostTransferCancels();
        TimeoutStops();
        ReadySendsOnceAndSessionIdentity();
        DeduplicationAndRelease();
        UnShapeShiftRegression.Run();
        global::Main.Instance.Clear();
        Console.WriteLine($"SESSION_LIFECYCLE_PASS ({assertions} assertions; extracted production methods, offline stubs)");
    }
}
