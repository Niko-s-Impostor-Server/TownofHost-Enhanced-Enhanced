global using HarmonyLib;

using System.Reflection;
using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using TOHE.Roles.Core;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch(Type type, string method) : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPriority(int priority) : Attribute { }
    [AttributeUsage(AttributeTargets.Parameter)] public sealed class HarmonyArgument(int argument) : Attribute { }
    public static class Priority { public const int Last = 0; }
}
namespace AmongUs.GameOptions
{
    public enum RoleTypes { Crewmate, Impostor, Judge, CrewmateGhost, ImpostorGhost }
    public enum RoleTeamTypes { Crewmate, Impostor }
}
namespace Il2CppSystem
{
    public readonly struct Nullable<T>(T value) where T : struct { public readonly T Value = value; }
}
namespace InnerNet
{
    public readonly struct PlayerId(byte value) { public readonly byte Value = value; }
    public class InnerNetClient { public enum GameStates { Started, Joined } }
}
namespace UnityEngine
{
    public class Object
    {
        static int next;
        public readonly IntPtr Pointer = new(++next);
        public string name = "fixture object";
        public bool Destroyed;
        public static implicit operator bool(Object value) => value is not null && !value.Destroyed;
        public static bool operator !(Object value) => !(bool)value;
    }
    public class GameObject { public bool activeInHierarchy = true; }
    public readonly struct Color { public Color ShadeColor(float value) => this; }
    public sealed class SpriteRenderer { public void set_color_Injected(ref Color color) { } }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace Hazel
{
    public enum SendOption { Reliable }
    public sealed class MessageWriter
    {
        readonly MemoryStream stream = new();
        BinaryWriter Writer => new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        public byte[] Bytes => stream.ToArray();
        public void Write(byte value) => Writer.Write(value);
        public void Write(int value) => Writer.Write(value);
        public void Write(uint value) => Writer.Write(value);
    }
    public sealed class MessageReader(byte[] bytes)
    {
        readonly BinaryReader reader = new(new MemoryStream(bytes));
        public int BytesRemaining => (int)(reader.BaseStream.Length - reader.BaseStream.Position);
        public byte ReadByte() => reader.ReadByte();
        public int ReadInt32() => reader.ReadInt32();
        public uint ReadUInt32() => reader.ReadUInt32();
    }
}
namespace TOHE.Roles.Core
{
    public class RoleBase
    {
        public bool HasVoted;
        public virtual bool CheckVote(PlayerControl actor, PlayerControl target) => true;
        public bool IsMethodOverridden(string name) => this is TestAbility;
    }
    public static class RoleExtensions
    {
        public static RoleBase GetRoleClass(this PlayerControl actor) => actor.CustomRole;
    }
}
namespace TOHE
{
    public sealed class ClientData { public PlayerControl Character; }
    public sealed class PlayerData
    {
        public RoleBehaviour Role;
        public RoleTypes RoleType;
        public Il2CppSystem.Nullable<RoleTypes> RoleWhenAlive;
        public bool Disconnected, IsDead;
    }
    public class PlayerTask : UnityEngine.Object { public bool IsComplete; }
    public class RoleBehaviour : UnityEngine.Object
    {
        public RoleTypes Role;
        public RoleTeamTypes TeamType;
        public RoleTypes DefaultGhostRole = RoleTypes.CrewmateGhost;
        public bool IsImpostor => TeamType == RoleTeamTypes.Impostor;
        public PlayerControl Player;
        public virtual void AdjustTasks(PlayerControl actor) { }
    }
    public sealed class ImpostorRole : RoleBehaviour
    {
        public ImpostorRole()
        {
            Role = RoleTypes.Impostor;
            TeamType = RoleTeamTypes.Impostor;
            DefaultGhostRole = RoleTypes.ImpostorGhost;
        }
        public void Deinitialize(PlayerControl actor)
        {
            if (actor.myTasks.Count == 0) return;
            actor.myTasks[0].Destroyed = true;
            actor.myTasks.RemoveAt(0);
        }
    }
    public sealed class JudgeRole : RoleBehaviour
    {
        public JudgeRole() => Role = RoleTypes.Judge;
        public bool TryOverrule(PlayerId target) => throw new Exception("Native TryOverrule reached");
        public bool IsBlockedByTasks() => false;
    }
    public class PlayerControl : UnityEngine.Object
    {
        public static PlayerControl LocalPlayer;
        public byte PlayerId;
        public uint NetId;
        public int OwnerId;
        public bool Owned = true, roleAssigned = true;
        public bool ModDead;
        public bool AmOwner => AmongUsClient.Instance is null ? throw new NullReferenceException("AmOwner before client") : Owned;
        public PlayerData Data = new();
        public RoleBase CustomRole;
        public int RemainingEmergencies;
        public float killTimer;
        public readonly List<PlayerTask> myTasks = [];
        public bool IsAlive() => !ModDead;
        public bool IsHost() => OwnerId == AmongUsClient.Instance.HostId;
        public int GetClientId() => OwnerId;
        public string GetRealName() => "fixture player";
        public CustomRoles GetCustomRole() => CustomRoles.Test;
        public bool Is(CustomRoles role) => false;
    }
    public sealed class AmongUsClient : UnityEngine.Object
    {
        public static AmongUsClient Instance;
        public bool AmHost = true, AmConnected = true;
        public int GameId = 1234, HostId = 11;
        public InnerNetClient.GameStates GameState = InnerNetClient.GameStates.Started;
        public readonly Dictionary<int, ClientData> Clients = [];
        public ClientData FindClientById(int id) => Clients.GetValueOrDefault(id);
    }
    public sealed class PlayerVoteArea : UnityEngine.Object
    {
        public PlayerId PlayerId;
        public MeetingHud Parent;
        public bool DidVote;
        public byte VotedForId = 255;
        public int Clears;
        public SpriteRenderer ThumbsDown = new();
        public GameObject gameObject = new();
        public void ClearButtons() => Clears++;
        public void JudgeOverruleVote() => throw new Exception("Native vote handler reached");
    }
    public sealed class MeetingHud : UnityEngine.Object
    {
        public enum MeetingStates { Discussion, NotVoted, Voted, Results, Proceeding, Animating }
        public static MeetingHud Instance;
        public uint NetId = 80;
        public MeetingStates CurrentState = MeetingStates.NotVoted;
        public readonly List<PlayerVoteArea> playerStates = [];
        public bool Indicator;
        public int ClearedVotes;
        public void Start() { }
        public void OnDestroy() { }
        public void UpdateJudgeAbilityIndicator(bool available, int tasks) => Indicator = available;
        public void RpcClearVoteDelay(int owner) => ClearedVotes++;
        public void CmdQueueOverruleVotes(PlayerId judge, PlayerId target, ushort nonce) => throw new Exception("Native queue reached");
    }
    public sealed class HudManager : UnityEngine.Object
    {
        public static HudManager Instance = new();
        public bool Active = true;
        public void Update() { }
        public void SetHudActive(bool active) => Active = active;
    }
    public sealed class ControllerManager
    {
        public static ControllerManager Instance = new();
        public void CloseOverlayMenu(string name) { }
    }
    public class DestroyableSingleton<T> where T : new()
    {
        public static T Instance = new();
        public static bool InstanceExists = true;
    }
    public sealed class RoleManager
    {
        public int Changes, GuardedHeaders;
        public void SetRole(PlayerControl actor, RoleTypes type)
        {
            Changes++;
            if (actor.Data.Role is ImpostorRole previous)
            {
                if ((bool)PatchCall.Invoke(typeof(MeetingAbilityPreserveHeaderPatch), "Prefix", actor)) previous.Deinitialize(actor);
                else GuardedHeaders++;
            }
            actor.Data.Role = type switch { RoleTypes.Judge => new JudgeRole(), RoleTypes.Impostor => new ImpostorRole(), _ => new RoleBehaviour { Role = type } };
            actor.Data.Role.Player = actor;
            actor.Data.RoleType = type;
            actor.Data.RoleWhenAlive = new(type);
            actor.killTimer = type == RoleTypes.Impostor ? 10f : actor.killTimer;
            actor.Data.Role.AdjustTasks(actor); // Native 2026.8.18 is empty; no global patch.
        }
    }
    public static class PatchCall
    {
        public static object Invoke(Type patch, string method, params object[] args) =>
            patch.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }
    public sealed record PlayerVersion(string forkId, Version version, string tag);
    public static class ThisAssembly
    {
        public static class Git
        {
            public const string Commit = "current-commit";
            public const string Branch = "codex/current";
        }
    }
    public static class Main
    {
        public static readonly Version version = new(2, 1, 1);
        public const string ForkId = "test";
        public static readonly Dictionary<int, PlayerVersion> playerVersion = [];
        public static PlayerControl[] AllAlivePlayerControls => Utils.Players.Values.Where(p => p.IsAlive()).ToArray();
    }
    public static class GameStates { public static bool IsNormalGame = true, IsModHost = true; }
    public static class Utils
    {
        public static readonly Dictionary<byte, PlayerControl> Players = [];
        public static PlayerControl GetPlayerById(byte id) => Players.GetValueOrDefault(id);
        public static void SendMessage(string value, byte playerId = 255, string title = "") { }
        public static Color GetRoleColor(CustomRoles role) => new();
    }
    public static class Translator { public static string GetString(string value) => value; }
    public static class Logger { public static void Info(string message, string category) { } }
    public static class Swapper { public static void CheckSwapperTarget(byte id) { } }
    public enum CustomRoles { Test, Dictator, Solsticer }
    public enum CustomRPC : uint { MeetingAbilityRequest = 188 }
    public sealed record Packet(CustomRPC Rpc, byte[] Bytes, int Target, SendOption Option);
    public static class CustomRpcTransport
    {
        public static readonly List<Packet> Sent = [];
        public static void Send(CustomRPC rpc, Action<MessageWriter> write, int target = -1, SendOption option = SendOption.Reliable)
        {
            MessageWriter writer = new();
            write(writer);
            Sent.Add(new(rpc, writer.Bytes, target, option));
        }
    }
    public sealed class TestAbility : RoleBase, IMeetingTargetAbility
    {
        public int Uses = 3, Calls;
        public int LegacyCalls;
        public bool Used, TargetsAllowed = true, OnePerMeeting = true;
        public bool CanUseMeetingAbility(PlayerControl actor) => (!OnePerMeeting || !Used) && Uses > 0;
        public bool CanTargetMeetingAbility(PlayerControl actor, PlayerControl target) => TargetsAllowed;
        public override bool CheckVote(PlayerControl actor, PlayerControl target)
        {
            LegacyCalls++;
            return false;
        }
        public bool UseMeetingAbility(PlayerControl actor, PlayerControl target)
        {
            if (!CanUseMeetingAbility(actor) || !TargetsAllowed) return false;
            Used = true;
            Uses--;
            Calls++;
            return true;
        }
    }
}
