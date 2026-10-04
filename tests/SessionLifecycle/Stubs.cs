using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    static class Time { public static float realtimeSinceStartup; }
}

enum SendOption { Reliable }
enum DisconnectReasons { Error }

sealed class CoroutineRunner
{
    readonly List<IEnumerator> active = [];
    public int Count => active.Count;
    public void StartCoroutine(IEnumerator coroutine)
    {
        if (coroutine.MoveNext()) active.Add(coroutine);
        else (coroutine as IDisposable)?.Dispose();
    }
    public void Tick()
    {
        foreach (var coroutine in active.ToArray())
        {
            if (coroutine.MoveNext()) continue;
            active.Remove(coroutine);
            (coroutine as IDisposable)?.Dispose();
        }
    }
    public void Clear()
    {
        foreach (var coroutine in active) (coroutine as IDisposable)?.Dispose();
        active.Clear();
    }
}

sealed class ClientData
{
    public int Id;
    public IntPtr Pointer;
}

sealed class PlayerControl
{
    public static PlayerControl LocalPlayer;
    public int ClientId;
    public uint NetId;
    public int GetClientId() => ClientId;
}

sealed class MessageWriter
{
    public uint NetId;
    public byte CallId;
    public int TargetId;
    public SendOption SendOption;
    public readonly List<object> Values = [];
    public void Write(string value) => Values.Add(value);
    public void Write(bool value) => Values.Add(value);
}

sealed class AmongUsClient
{
    public static AmongUsClient Instance;
    public int HostId;
    public bool AmConnected;
    public int Initializations;
    public int Exits;
    public bool FailFinish;
    public readonly Dictionary<int, ClientData> Clients = [];
    public readonly List<MessageWriter> Started = [];
    public readonly List<MessageWriter> Finished = [];
    public ClientData GetHost() => Clients.GetValueOrDefault(HostId);
    public MessageWriter StartRpcImmediately(uint netId, byte callId, SendOption option, int targetId = -1)
    {
        var writer = new MessageWriter { NetId = netId, CallId = callId, TargetId = targetId, SendOption = option };
        Started.Add(writer);
        return writer;
    }
    public void FinishRpcImmediately(MessageWriter writer)
    {
        if (FailFinish) throw new InvalidOperationException("Injected writer failure");
        Finished.Add(writer);
    }
    public void ExitGame(DisconnectReasons reason) { Exits++; AmConnected = false; }
}

sealed class PlayerVersion
{
    public Version version;
    public string tag;
    public string forkId;
    public PlayerVersion(string value, string tagValue, string forkValue)
    {
        version = Version.Parse(value); tag = tagValue; forkId = forkValue;
    }
}

sealed class BoolConfig { public bool Value; }
static class Main
{
    public static readonly CoroutineRunner Instance = new();
    public static readonly Dictionary<int, PlayerVersion> playerVersion = [];
    public static readonly BoolConfig VersionCheat = new();
    public const string PluginVersion = "2026.8.18";
    public const string ForkId = "offline-test";
}
static class Options { public static bool IsLoaded; }
static class Utils
{
    public static ClientData GetClientById(int id) => AmongUsClient.Instance?.Clients.GetValueOrDefault(id);
}
static class ThisAssembly
{
    public static class Git { public const string Commit = "fixture"; public const string Branch = "fixture"; }
}
static class Logger
{
    public static readonly List<string> Warnings = [];
    public static readonly List<string> Errors = [];
    public static void Warn(string message, string category) => Warnings.Add(message);
    public static void Error(string message, string category) => Errors.Add(message);
}
