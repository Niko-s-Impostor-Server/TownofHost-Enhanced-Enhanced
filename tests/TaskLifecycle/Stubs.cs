global using HarmonyLib;
using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
    }
}

namespace TOHE
{
    enum CustomRoles { Crewmate, Impostor, Solsticer, Workhorse, NotAssigned = 100 }
    sealed class AmongUsClient
    {
        public static AmongUsClient Instance;
        public bool AmHost;
    }
    static class GameStates
    {
        public static bool IsModHost, IsNormalGame = true, IsHideNSeek, IsFreePlay;
    }
    sealed class NativeRole { }
    sealed class NetworkedPlayerInfo
    {
        public sealed class TaskInfo { public uint Id; public bool Complete; }
        public byte PlayerId;
        public IntPtr Pointer;
        public bool Disconnected;
        public PlayerControl Object;
        public NativeRole Role = new();
        public List<TaskInfo> Tasks = [];
        // Eligibility is an explicit fixture input; Utils role rules are outside this test.
        public bool RoleHasTasks = true;
        public bool ContributesToTaskWin = true;
    }
    sealed class PlayerControl
    {
        public NetworkedPlayerInfo Data;
        public string GetNameWithRole() => "offline-fixture";
    }
    sealed class PlayerState
    {
        public CustomRoles MainRole = CustomRoles.Crewmate;
        public readonly TaskState TaskState = new();
        public int InitCalls;
        public void InitTask(PlayerControl player) { InitCalls++; TaskState.Init(player); }
    }
    static class Main
    {
        public static readonly Dictionary<byte, PlayerState> PlayerStates = [];
    }
    static class Utils
    {
        public static bool HasTasks(NetworkedPlayerInfo data, bool ForRecompute = true)
            => data.RoleHasTasks && (!ForRecompute || data.ContributesToTaskWin);
        public static string RemoveHtmlTags(this string value) => value;
    }
    static class Logger { public static void Info(string value, string category) { } }
    sealed class GameData
    {
        public static GameData Instance;
        public readonly List<NetworkedPlayerInfo> AllPlayers = [];
        public int TotalTasks, CompletedTasks, NativePasses, Recomputes;
        public void RecomputeTaskCounts()
        {
            Recomputes++;
            if (CustomTaskCountsPatch.Prefix(this)) NativePasses++;
        }
    }
    sealed class ShipStatus { public void Begin() { } }
}
