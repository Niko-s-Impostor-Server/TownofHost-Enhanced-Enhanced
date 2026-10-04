using System;
using System.Collections.Generic;
using System.Linq;
using static Utils;
using UnityEngine;
using AmongUs.GameOptions;

class Program
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static void Invoke(Type type, string name, params object[] args) => type.GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, args);
    static PlayerControl Player(byte id) => Utils.Players[id] = new() { PlayerId = id };
    static void Reset() { Utils.Players.Clear(); LateTask.Tasks.Clear(); GameStates.IsInTask = true; global::Main.AllPlayerKillCooldown.Clear(); TargetArrow.Arrows.Clear(); PlayerControl.MurderHook = null; }
    static void Main()
    {
        var ag = new Agitater(); Reset(); ag.Init(); Agitater.playerIdList.Add(1); var owner = Player(1); var a = Player(2); var b = Player(3);
        ag.OnCheckMurderAsKiller(owner, a); var old = LateTask.Tasks[0]; Agitater.ResetBomb(); ag.OnCheckMurderAsKiller(owner, b); old(); Check(!b.Data.IsDead && Agitater.CurrentBombedPlayer == 3, "old bomb callback cannot explode replacement");
        var pending = LateTask.Tasks[1]; ag.Init(); Agitater.playerIdList.Add(1); ag.OnCheckMurderAsKiller(owner, b); pending(); Check(!b.Data.IsDead && Agitater.AgitaterHasBombed, "old round callback cannot explode new round bomb");
        var passed = LateTask.Tasks.Last(); Agitater.CurrentBombedPlayer = 2; passed(); Check(a.Data.IsDead && !b.Data.IsDead && !Agitater.AgitaterHasBombed, "bomb follows current carrier and resets on expiry");
        a.Data.IsDead = false; ag.OnCheckMurderAsKiller(owner, a); Utils.Players.Remove(2); LateTask.Tasks.Last()(); Check(!Agitater.AgitaterHasBombed, "missing bomb target expires safely");
        Reset(); owner = Player(1); a = Player(2); b = Player(3); var c = Player(4); var dp = new Deathpact(); dp.Add(1); Invoke(typeof(Deathpact), "DoDeathpact", owner, a); Invoke(typeof(Deathpact), "DoDeathpact", owner, b); long deadline = Deathpact.DeathpactTime[1]; Utils.Now += 5; Invoke(typeof(Deathpact), "DoDeathpact", owner, c); Check(Deathpact.PlayersInDeathpact[1].Count == 2 && Deathpact.DeathpactTime[1] == deadline, "active pact rejects extra targets without extending timer");
        dp.Remove(1); Check(!Deathpact.ActiveDeathpacts.Contains(1) && !Deathpact.PlayersInDeathpact.ContainsKey(1) && TargetArrow.Arrows.Count == 0, "role removal clears pact and arrows");
        dp.Add(1); Invoke(typeof(Deathpact), "DoDeathpact", owner, a); Invoke(typeof(Deathpact), "DoDeathpact", owner, b); owner.Data.IsDead = true; dp.OnReportDeadBody(c, null); Check(!Deathpact.ActiveDeathpacts.Any() && Deathpact.PlayersInDeathpact[1].Count == 0 && !a.Data.IsDead && !b.Data.IsDead, "meeting clears pact even when issuer is dead");
        owner.Data.IsDead = false; Invoke(typeof(Deathpact), "DoDeathpact", owner, a); Invoke(typeof(Deathpact), "DoDeathpact", owner, b); Deathpact.PlayersInDeathpact[1].Add(null); Invoke(typeof(Deathpact), "CheckCancelDeathpact", owner); Check(!Deathpact.ActiveDeathpacts.Any(), "vanished pact member cancels without null dereference");
        Reset(); owner = Player(1); a = Player(2); b = Player(3); global::Main.AllPlayerKillCooldown[2] = 20; Mastermind.ManipulatedPlayers[2] = 0; Mastermind.TempKCDs[2] = 7; PlayerControl.MurderHook = () => Mastermind.TempKCDs.Clear(); new Mastermind().CheckMurderOnOthersTarget(a, b); LateTask.Tasks.Last()(); Check(a.LastCooldown == 27, "cooldown captured before murder clears pending state");
        b.Data.IsDead = false; Mastermind.ManipulatedPlayers[2] = 0; Mastermind.TempKCDs[2] = 9; new Mastermind().CheckMurderOnOthersTarget(a, b); float before = a.LastCooldown; GameStates.IsInTask = false; LateTask.Tasks.Last()(); Check(a.LastCooldown == before, "meeting cancels deferred cooldown restore");
        GameStates.IsInTask = true; b.Data.IsDead = false; Mastermind.ManipulatedPlayers[2] = 0; Mastermind.TempKCDs[2] = 11; new Mastermind().CheckMurderOnOthersTarget(a, b); before = a.LastCooldown; a.Role = new object(); LateTask.Tasks.Last()(); Check(a.LastCooldown == before, "role change cancels deferred cooldown restore");
        Reset(); owner = Player(1); var vampire = new Vampire(); Vampire.BittenPlayers[99] = new(1, 10); vampire.OnFixedUpdate(owner, false, 0); Check(Vampire.BittenPlayers.Count == 0, "departed bitten target safely consumed");
        a = Player(2); Vampire.BittenPlayers[2] = new(1, 10); bool consumed = false; PlayerControl.MurderHook = () => { consumed = Vampire.BittenPlayers.Count == 0; vampire.OnReportDeadBody(owner, null); }; vampire.OnReportDeadBody(owner, null); Check(consumed && a.Murders == 1, "bite state consumed before reentrant death callbacks");
        PlayerControl.MurderHook = null; a.Data.IsDead = false; var poisoner = new Poisoner(); Poisoner.PoisonedPlayers[2] = new(1, 10); consumed = false; PlayerControl.MurderHook = () => { consumed = Poisoner.PoisonedPlayers.Count == 0; poisoner.OnReportDeadBody(owner, null); }; poisoner.OnReportDeadBody(owner, null); Check(consumed && a.Murders == 2, "poison state consumed before reentrant death callbacks");
        Reset(); owner = Player(1); PlayerControl.LocalPlayer = owner;
        global::Main.AliveImpostorCount = 1;
        Fireworker.state[1] = Fireworker.FireworkerState.WaitTime;
        Fireworker.nowFireworkerCount[1] = 0;
        AmongUsClient.Instance.AmHost = false;
        new Fireworker().GetLowerText(owner, owner, isForHud: true);
        Check(Fireworker.state[1] == Fireworker.FireworkerState.WaitTime && AmongUsClient.Instance.Sent == 0, "non-host HUD cannot advance or send authoritative role state");
        Invoke(typeof(Fireworker), "SendRPC", (byte)1);
        Check(AmongUsClient.Instance.Sent == 0, "non-host Fireworker sender refuses state sync");
        AmongUsClient.Instance.AmHost = true;
        new Fireworker().GetLowerText(owner, owner, isForHud: true);
        Check(Fireworker.state[1] == Fireworker.FireworkerState.ReadyFire && AmongUsClient.Instance.Sent == 1, "host advances Fireworker and sends state once");
        new Fireworker().GetLowerText(owner, owner, isForHud: true);
        Check(AmongUsClient.Instance.Sent == 1, "repeated HUD rendering cannot repeat state transition");
        Check(new Fireworker().GetLowerText(null) == string.Empty, "missing Fireworker seer returns empty text");

        Reset(); owner = Player(1); a = Player(2); b = Player(3);
        a.CustomRole = CustomRoles.Impostor;
        MeetingHud.Instance.playerStates = [new() { PlayerId = 2, VotedForId = 9 }, new() { PlayerId = 99, VotedForId = 9 }, new() { PlayerId = 3, VotedForId = 9 }];
        var instigator = new Instigator { _Player = owner };
        instigator.OnPlayerExiled(owner, new() { PlayerId = 9 });
        Check(CheckForEndVotingPatch.Deaths.SequenceEqual(new byte[] { 3 }) && instigator.AbilityLimit == 0, "invalid and non-crew voters do not suppress eligible Instigator targets");

        Reset(); owner = Player(1); owner.CustomRole = CustomRoles.Pelican;
        Check(!Pelican.CanEat(owner, 99), "departed Pelican target is rejected before Penguin lookup");
        Check(!Pelican.CanEat(null, 99), "missing Pelican owner is rejected");
        typeof(Pelican).GetMethod("ReturnEatenPlayerBack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(new Pelican(), new object[] { null });
        Check(!GameEndCheckerForNormal.ShouldNotCheck, "missing Pelican return caller is safe");
        Console.WriteLine($"{checks} lifecycle checks passed against extracted repository methods.");
    }
}
class PlayerControl
{
    public static PlayerControl LocalPlayer;
    public uint NetId;
    public CustomRoles CustomRole = CustomRoles.Crewmate;
    public byte PlayerId; public NetworkedPlayerInfo Data = new(); public string name = "Player"; public object Role = new(); public Transform transform = new(); public float LastCooldown; public int Murders; public static Action MurderHook;
    public void Notify(string s, float duration = 0) { }
    public void RPCPlayCustomSound(string s) { }
    public void ResetKillCooldown() { }
    public void SetKillCooldown(float time = 0) => LastCooldown = time; public void RpcGuardAndKill(PlayerControl p) { }
    public bool CheckDoubleTrigger(PlayerControl p, Action a) { a(); return false; }
    public void RpcMurderPlayer(PlayerControl target) { target.Data.IsDead = true; target.Murders++; MurderHook?.Invoke(); }
    public void TrapperKilled(PlayerControl p) { }
    public string GetRealName() => name; public string GetNameWithRole() => name; public object GetRoleClass() => Role;
    public bool CanBeTeleported() => true;
    public bool IsTransformedNeutralApocalypse() => false;
    public Vector2 GetCustomPosition() => new();
    public void RpcTeleport(Vector2 position) { }
    public void SyncSettings() { }
}
class NetworkedPlayerInfo { public bool Disconnected, IsDead; public byte PlayerId; public string PlayerName = "Player"; public CustomRoles CustomRole = CustomRoles.Crewmate; }
class Transform { public Vector3 position; }
class OptionItem(float n) { public float Value = n; public float GetFloat() => Value; public int GetInt() => (int)Value; public bool GetBool() => Value != 0; }
class LateTask { public static List<Action> Tasks = []; public LateTask(Action action, float delay, string label) { Tasks.Add(action); } }
static class Utils
{
    public static Dictionary<int, PlayerControl> Players = []; public static long Now = 100; public static PlayerControl GetPlayerById(int id) => Players.GetValueOrDefault(id); public static PlayerControl GetPlayer(this byte id) => GetPlayerById(id);
    public static bool IsAlive(this PlayerControl p) => p != null && !p.Data.IsDead && !p.Data.Disconnected; public static bool Is(this PlayerControl p, CustomRoles r) => p != null && p.CustomRole == r; public static void SetDeathReason(this byte id, PlayerState.DeathReason r) { }
    public static void SetDeathReason(this PlayerControl p, PlayerState.DeathReason r) { }
    public static void SetRealKiller(this PlayerControl p, PlayerControl killer) { }
    public static long GetTimeStamp() => Now;
    public static string GetString(string s) => s + " {0}"; public static float GetDistance(Vector3 a, Vector3 b) => 10; public static void MarkEveryoneDirtySettings() { }
    public static void NotifyRoles(PlayerControl SpecifySeer = null) { }
    public static CustomRoles GetCustomRole(this PlayerControl p) => p.CustomRole;
    public static CustomRoles GetCustomRole(this NetworkedPlayerInfo p) => p.CustomRole;
    public static bool IsCrewmate(this CustomRoles role) => role == CustomRoles.Crewmate;
    public static T RandomElement<T>(this List<T> list) => list.First();
    public static IEnumerable<T> GetRoleBasesByType<T>() where T : new() => [new T()];
    public static void ThrowException(Exception e) => throw e;
}
static class GameStates { public static bool IsInTask = true; public static bool IsMeeting; }
static class Main { public static Dictionary<byte, float> AllPlayerKillCooldown = []; public static NormalOptions NormalOptions = new(); public static int AliveImpostorCount; public static IEnumerable<PlayerControl> AllPlayerControls => Utils.Players.Values; public static Dictionary<byte, float> AllPlayerSpeed = []; }
class NormalOptions { public int KillDistance; }
static class Logger { public static void Info(string s, string label) { } }
static class RPC { public static void PlaySoundRPC(byte id, Sounds sound) { } }
enum Sounds { KillSound, TaskComplete }
enum CustomRoles { Crewmate, Bait, Pestilence, Mastermind, Trapper, Reach, Pelican, GM, Impostor }
class PlayerState { public enum DeathReason { Bombed, Bite, Poison, Suicide, Retribution } }
static class Reach { public static CustomRoles IsReach => CustomRoles.Reach; }
static class TargetArrow { public static HashSet<(byte, byte)> Arrows = []; public static void Add(byte a, byte b) => Arrows.Add((a, b)); public static void Remove(byte a, byte b) => Arrows.Remove((a, b)); }
class Penguin { public PlayerControl AbductVictim; }
static class Medic { public static bool IsProtected(byte id) => false; }
static class Scavenger { public static HashSet<byte> KilledPlayersId = []; }
static class ReportDeadBodyPatch { public static Dictionary<byte, bool> CanReport = []; }
static class GameEndCheckerForNormal { public static bool ShouldNotCheck; }
class PlayerVoteArea { public byte PlayerId, VotedForId; }
class MeetingHud { public static MeetingHud Instance = new(); public PlayerVoteArea[] playerStates = []; }
static class CheckForEndVotingPatch { public static List<byte> Deaths = []; public static void TryAddAfterMeetingDeathPlayers(PlayerState.DeathReason reason, params byte[] ids) => Deaths.AddRange(ids); }
enum CustomRPC { SendFireworkerState }
enum SendOption { Reliable }
class MessageWriter { public void Write(byte n) { } public void Write(int n) { } }
class AmongUsClient { public static AmongUsClient Instance = new(); public bool AmHost; public int Sent; public MessageWriter StartRpcImmediately(uint netId, byte call, SendOption option, int target) { Sent++; return new(); } public void FinishRpcImmediately(MessageWriter writer) { } }
namespace UnityEngine { struct Vector2 { } struct Vector3 { } static class Mathf { public static int Clamp(int n, int min, int max) => Math.Clamp(n, min, max); } static class Time { public static float fixedDeltaTime = .02f; } }
namespace AmongUs.GameOptions { enum FloatOptionNames { CrewLightMod, ImpostorLightMod } interface IGameOptions { void SetVision(bool b); void SetFloat(FloatOptionNames key, float value); } static class NormalGameOptionsV11 { public static float[] KillDistances = [1, 2, 3]; } }
