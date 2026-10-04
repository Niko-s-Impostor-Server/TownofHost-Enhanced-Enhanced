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
    static void Reset() { Utils.Players.Clear(); LateTask.Tasks.Clear(); GameStates.IsInTask = true; global::Main.AllPlayerKillCooldown.Clear(); TargetArrow.Arrows.Clear(); PlayerControl.MurderHook = null; global::Main.AllPlayerSpeed.Clear(); ReportDeadBodyPatch.CanReport.Clear(); GameEndCheckerForNormal.ShouldNotCheck = false; GameStates.IsMeeting = false; }
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

        Reset(); AmongUsClient.Instance.AmHost = true; owner = Player(1); a = Player(2); b = Player(3); c = Player(4);
        var shroud = new Shroud { _state = new() { PlayerId = 1 } };
        var otherShroud = new Shroud { _state = new() { PlayerId = 4 } };
        shroud.Init(); shroud.Add(1); otherShroud.Add(4);
        Shroud.ShroudList[2] = 1; Shroud.ShroudList[3] = 4;
        owner.Data.IsDead = true; shroud.OnPlayerExiled(owner, null);
        Check(!Shroud.ShroudList.ContainsKey(2) && Shroud.ShroudList.GetValueOrDefault((byte)3) == 4, "dead original Shroud cannot clear inherited owner's mark");
        owner.Data.IsDead = false; Shroud.ShroudList[2] = 1;
        shroud.Remove(1);
        Check(!Shroud.ShroudList.ContainsKey(2) && Shroud.ShroudList.ContainsKey(3) && CustomRoleManager.OnFixedUpdateOthers.Count == 1, "Shroud removal clears only own marks and unregisters callback");
        Shroud.ShroudList[99] = 4; otherShroud.AfterMeetingTasks();
        Check(b.Data.IsDead && b.RealKiller == c && !Shroud.ShroudList.ContainsKey(99), "meeting consumes own Shroud marks with correct killer and missing target safety");
        Shroud.ShroudList[2] = 1; Shroud.ShroudList[3] = 4; a.Data.IsDead = false; b.Data.IsDead = false;
        shroud.AfterMeetingTasks();
        Check(a.Data.IsDead && a.RealKiller == owner && !b.Data.IsDead && Shroud.ShroudList.ContainsKey(3), "Shroud meeting cannot consume another owner's target");
        b.Data.IsDead = true; CustomRoleManager.OnFixedUpdateOthers.Clear(); shroud.Add(1); otherShroud.Add(4);
        CustomRoleManager.OnFixedUpdateOthers.First()(b, false, 0);
        Check(Shroud.ShroudList.ContainsKey(3), "foreign Shroud fixed update cannot process another owner's mark");
        CustomRoleManager.OnFixedUpdateOthers.Last()(b, false, 0);
        Check(!Shroud.ShroudList.ContainsKey(3), "own Shroud fixed update consumes invalid target");
        a.Data.IsDead = false; b.Data.IsDead = false; a.Murders = 0; b.Murders = 0;
        Shroud.ShroudList[2] = 1; Shroud.ShroudList[3] = 1;
        PlayerControl.MurderHook = () => shroud.AfterMeetingTasks(); shroud.AfterMeetingTasks();
        Check(a.Murders == 1 && b.Murders == 1 && Shroud.ShroudList.Count == 0, "Shroud meeting consumes marks before reentrant death callbacks");

        Reset(); owner = Player(1); a = Player(2); b = Player(3); c = Player(4); PlayerControl.LocalPlayer = c;
        var pelican = new Pelican(); pelican.Init();
        Pelican.eatenList[1] = [2, 99]; Pelican.eatenList[4] = [3];
        Pelican.originalSpeed[2] = 1.5f; Pelican.originalSpeed[99] = 2f; global::Main.AllPlayerSpeed[2] = .5f; ReportDeadBodyPatch.CanReport[2] = false;
        owner.Position = new(7, 8); pelican.OnFixedUpdate(owner, true, 0); Utils.Players.Remove(1);
        a.TeleportHook = () => pelican.Remove(1); pelican.Remove(1);
        Check(a.Teleports == 1 && a.LastPosition.Equals(new Vector2(7, 8)) && global::Main.AllPlayerSpeed[2] == 1.5f && ReportDeadBodyPatch.CanReport[2], "missing Pelican owner releases target at last position and restores controls once");
        Check(!Pelican.eatenList.ContainsKey(1) && Pelican.eatenList[4].SetEquals(new byte[] { 3 }) && !Pelican.originalSpeed.ContainsKey(99), "Pelican release clears only its owner and drops departed target speed state");
        Check(!GameEndCheckerForNormal.ShouldNotCheck, "Pelican release restores game-end checker state");
        owner = Player(1); owner.Position = new(9, 10); Pelican.eatenList[1] = [2]; Pelican.originalSpeed[2] = 1.5f; global::Main.AllPlayerSpeed[2] = .5f; a.TeleportHook = null;
        GameEndCheckerForNormal.ShouldNotCheck = true; pelican.Remove(1);
        Check(a.LastPosition.Equals(owner.Position) && GameEndCheckerForNormal.ShouldNotCheck, "role removal releases at current Pelican position and preserves enclosing checker suppression");
        Pelican.eatenList[1] = [2]; Pelican.originalSpeed[2] = 1.5f; global::Main.AllPlayerSpeed[2] = .5f; owner.Data.Disconnected = true;
        pelican.OnMurderPlayerAsTarget(owner, owner, true, true);
        Check(!Pelican.eatenList.ContainsKey(1), "Pelican disconnect callback releases targets even during meeting");
        Check((byte)CustomRPC.SyncRoleSkill == 115 && (byte)CustomRPC.SyncFFANameNotify == 185 && (byte)CustomRPC.ClearPelicanOwner == 186, "new Pelican clear RPC preserves existing wire IDs and fits byte range");
        Pelican.eatenList[1] = [2]; Pelican.lastKnownPosition[1] = new(7, 8); Utils.Players.Remove(1);
        int sendsBefore = AmongUsClient.Instance.Sent; bool detachedAtSend = false;
        AmongUsClient.Instance.StartHook = () => detachedAtSend = !Pelican.eatenList.ContainsKey(1); pelican.Remove(1); AmongUsClient.Instance.StartHook = null;
        Check(detachedAtSend && AmongUsClient.Instance.Sent == sendsBefore + 1 && AmongUsClient.Instance.LastCall == (byte)CustomRPC.ClearPelicanOwner && AmongUsClient.Instance.LastTarget == -1 && AmongUsClient.Instance.LastWriter.Payload.SequenceEqual(new byte[] { 1 }) && AmongUsClient.Instance.LastWriter.NetObjects == 0, "missing owner release sends independent clear after detaching owner collection");
        AmongUsClient.Instance.AmHost = false; sendsBefore = AmongUsClient.Instance.Sent; Invoke(typeof(Pelican), "SendOwnerClear", (byte)4);
        Check(AmongUsClient.Instance.Sent == sendsBefore, "non-host Pelican clear sender refuses authoritative sync");
        c.IsHostPlayer = true; Pelican.eatenList[1] = [2]; Pelican.eatenList[4] = [3];
        Pelican.ReceiveOwnerClear(new([1]), c);
        Check(!Pelican.IsEaten(2) && Pelican.IsEaten(3) && !Pelican.eatenList.ContainsKey(1), "ID-based receiver clears departed owner without touching another owner's target");
        Pelican.ReceiveOwnerClear(new([1]), c);
        Check(Pelican.eatenList[4].SetEquals(new byte[] { 3 }), "repeated Pelican owner clear is idempotent");
        Pelican.eatenList[1] = [2]; var rejectedReader = new MessageReader([1]); Pelican.ReceiveOwnerClear(rejectedReader, a);
        Check(Pelican.eatenList.ContainsKey(1) && rejectedReader.Reads == 0 && !RPCHandlerPatch.TrustedRpc((byte)CustomRPC.ClearPelicanOwner), "Pelican clear receiver checks host before reading or mutating and remains outside request whitelist");
        EAC.Cancel = false; var rpcReader = new MessageReader([1]);
        bool permitted = RPCHandlerPatch.Prefix(c, (byte)CustomRPC.ClearPelicanOwner, rpcReader, out bool handlerState);
        Check(permitted && handlerState && MessageReader.LastClone.Recycled && rpcReader.Reads == 0, "accepted host RPC shares permitted state and recycles reader clone");
        int dispatchesBefore = RPCHandlerPatch.Dispatches; RPCHandlerPatch.Postfix(c, (byte)CustomRPC.ClearPelicanOwner, rpcReader, handlerState);
        Check(RPCHandlerPatch.Dispatches == dispatchesBefore + 1 && !Pelican.eatenList.ContainsKey(1), "accepted RPC reaches actual Pelican Postfix dispatch and clears owner state");
        EAC.Cancel = true; var previousClone = MessageReader.LastClone;
        permitted = RPCHandlerPatch.Prefix(c, (byte)CustomRPC.ClearPelicanOwner, rpcReader, out handlerState); RPCHandlerPatch.Postfix(c, (byte)CustomRPC.ClearPelicanOwner, rpcReader, handlerState);
        Check(!permitted && !handlerState && ReferenceEquals(previousClone, MessageReader.LastClone) && RPCHandlerPatch.Dispatches == dispatchesBefore + 1, "EAC cancellation creates no clone and cannot reach Postfix dispatch");
        EAC.Cancel = false; Pelican.eatenList[1] = [2];
        permitted = RPCHandlerPatch.Prefix(a, (byte)CustomRPC.ClearPelicanOwner, rpcReader, out handlerState); RPCHandlerPatch.Postfix(a, (byte)CustomRPC.ClearPelicanOwner, rpcReader, handlerState);
        Check(!permitted && !handlerState && MessageReader.LastClone.Recycled && Pelican.eatenList.ContainsKey(1) && RPCHandlerPatch.Dispatches == dispatchesBefore + 1, "authority rejection remains rejected across Prefix and Postfix in local handler harness");
        EAC.Cancel = false; ChatCommands.Cancel = true;
        permitted = RPCHandlerPatch.Prefix(c, (byte)RpcCalls.SendChat, new([]), out handlerState); RPCHandlerPatch.Postfix(c, (byte)CustomRPC.ClearPelicanOwner, rpcReader, handlerState);
        Check(!permitted && !handlerState && MessageReader.LastClone.Recycled && RPCHandlerPatch.Dispatches == dispatchesBefore + 1, "canceled chat recycles clone and cannot reach Postfix dispatch");
        ChatCommands.Cancel = false; Logger.ThrowOnInfo = true;
        try { RPCHandlerPatch.Prefix(c, (byte)CustomRPC.ClearPelicanOwner, rpcReader, out _); } catch (InvalidOperationException) { }
        Logger.ThrowOnInfo = false;
        Check(MessageReader.LastClone.Recycled, "reader clone is recycled even when RPC validation throws");
        Check((byte)CustomRPC.ClearPelicanOwner == 186 && (byte)CustomRPC.ClearShroudOwner == 187, "Shroud clear RPC appends without changing Pelican or previous IDs");
        AmongUsClient.Instance.AmHost = true; Shroud.ShroudList.Clear(); Shroud.ShroudList[2] = 1; Shroud.ShroudList[3] = 4;
        sendsBefore = AmongUsClient.Instance.Sent; detachedAtSend = false;
        AmongUsClient.Instance.StartHook = () => detachedAtSend = Shroud.ShroudList.Values.All(id => id != 1); shroud.Remove(1); AmongUsClient.Instance.StartHook = null;
        Check(detachedAtSend && AmongUsClient.Instance.Sent == sendsBefore + 1 && AmongUsClient.Instance.LastCall == (byte)CustomRPC.ClearShroudOwner && AmongUsClient.Instance.LastWriter.Payload.SequenceEqual(new byte[] { 1 }) && AmongUsClient.Instance.LastWriter.NetObjects == 0 && Shroud.ShroudList.ContainsKey(3), "missing Shroud owner sends independent clear after removing only own marks");
        AmongUsClient.Instance.AmHost = false; sendsBefore = AmongUsClient.Instance.Sent; Invoke(typeof(Shroud), "SendOwnerClear", (byte)1);
        Check(AmongUsClient.Instance.Sent == sendsBefore, "non-host Shroud clear sender refuses authoritative sync");
        Shroud.ShroudList[2] = 1; Shroud.ReceiveOwnerClear(new([1]), c);
        Check(!Shroud.ShroudList.ContainsKey(2) && Shroud.ShroudList.GetValueOrDefault((byte)3) == 4, "Shroud receiver clears departed owner while preserving other owner's marks");
        Shroud.ReceiveOwnerClear(new([1]), c);
        Check(Shroud.ShroudList.Count == 1, "repeated Shroud owner clear is idempotent");
        Shroud.ShroudList[2] = 1; rejectedReader = new([1]); Shroud.ReceiveOwnerClear(rejectedReader, a);
        Check(Shroud.ShroudList.ContainsKey(2) && rejectedReader.Reads == 0 && !RPCHandlerPatch.TrustedRpc((byte)CustomRPC.ClearShroudOwner), "Shroud receiver checks host before reading or changing state and remains outside request whitelist");
        rpcReader = new([1]); permitted = RPCHandlerPatch.Prefix(c, (byte)CustomRPC.ClearShroudOwner, rpcReader, out handlerState); RPCHandlerPatch.Postfix(c, (byte)CustomRPC.ClearShroudOwner, rpcReader, handlerState);
        Check(permitted && handlerState && !Shroud.ShroudList.ContainsKey(2) && Shroud.ShroudList.ContainsKey(3), "accepted host RPC reaches actual Shroud Postfix dispatch without owner object");
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
    public bool inVent, inMovingPlat; public PlayerControl RealKiller; public int Teleports; public Vector2 Position, LastPosition; public Action TeleportHook;
    public bool IsDisconnected() => Data.Disconnected;
    public bool IsHostPlayer; public bool IsHost() => IsHostPlayer; public int GetClientId() => PlayerId;
    public bool RpcCheckAndMurder(PlayerControl target, bool check) => false;
    public bool IsTransformedNeutralApocalypse() => false;
    public Vector2 GetCustomPosition() => Position;
    public void RpcTeleport(Vector2 position, bool sendInfoInLogs = true) { Teleports++; LastPosition = position; TeleportHook?.Invoke(); }
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
    public static void SetRealKiller(this PlayerControl p, PlayerControl killer) => p.RealKiller = killer;
    public static long GetTimeStamp() => Now;
    public static string GetString(string s) => s + " {0}"; public static float GetDistance(Vector3 a, Vector3 b) => 10; public static void MarkEveryoneDirtySettings() { }
    public static float GetDistance(Vector2 a, Vector2 b) => 10;
    public static void NotifyRoles(PlayerControl SpecifySeer = null, PlayerControl SpecifyTarget = null, bool ForceLoop = false) { }
    public static CustomRoles GetCustomRole(this PlayerControl p) => p.CustomRole;
    public static CustomRoles GetCustomRole(this NetworkedPlayerInfo p) => p.CustomRole;
    public static bool IsCrewmate(this CustomRoles role) => role == CustomRoles.Crewmate;
    public static T RandomElement<T>(this List<T> list) => list.First();
    public static string RemoveHtmlTags(this string s) => s;
    public static IEnumerable<T> GetRoleBasesByType<T>() where T : new() => [new T()];
    public static void ThrowException(Exception e) => throw e;
}
static class GameStates { public static bool IsInTask = true; public static bool IsMeeting; }
static class Main { public static Dictionary<byte, float> AllPlayerKillCooldown = []; public static NormalOptions NormalOptions = new(); public static int AliveImpostorCount; public static IEnumerable<PlayerControl> AllPlayerControls => Utils.Players.Values; public static IEnumerable<PlayerControl> AllAlivePlayerControls => Utils.Players.Values.Where(p => p.IsAlive()); public static Dictionary<byte, float> AllPlayerSpeed = []; }
class NormalOptions { public int KillDistance; }
static class Logger { public static bool ThrowOnInfo; public static void Info(string s, string label) { if (ThrowOnInfo) throw new InvalidOperationException(); } public static void Warn(string s, string label) { } public static void SendInGame(string s) { } }
static class RPC { public static void PlaySoundRPC(byte id, Sounds sound) { } public static string GetRpcName(byte callId) => callId.ToString(); }
enum Sounds { KillSound, TaskComplete }
enum CustomRoles { Crewmate, Bait, Pestilence, Mastermind, Trapper, Reach, Pelican, GM, Impostor, Shroud }
class PlayerState { public byte PlayerId; public enum DeathReason { Bombed, Bite, Poison, Suicide, Retribution, Shrouded } }
static class CustomRoleManager { public static HashSet<Action<PlayerControl, bool, long>> OnFixedUpdateOthers = []; }
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
enum SendOption { Reliable }
class MessageWriter { public List<byte> Payload = []; public int NetObjects; public void Write(byte n) => Payload.Add(n); public void Write(int n) { } public void WriteNetObject(PlayerControl p) => NetObjects++; }
class MessageReader(byte[] payload) { private readonly byte[] data = payload; public static MessageReader LastClone; public int Reads; public bool Recycled; public int BytesRemaining => data.Length - Reads; public static MessageReader Get(MessageReader reader) => LastClone = new(reader.data); public void Recycle() => Recycled = true; public byte ReadByte() => data[Reads++]; public uint ReadUInt32() => 0; public ushort ReadUInt16() => 0; public string ReadString() => "text"; public bool ReadBoolean() => false; }
class AmongUsClient { public static AmongUsClient Instance = new(); public bool AmHost; public int Sent; public byte LastCall; public int LastTarget; public MessageWriter LastWriter; public Action StartHook; public MessageWriter StartRpcImmediately(uint netId, byte call, SendOption option, int target) { Sent++; LastCall = call; LastTarget = target; StartHook?.Invoke(); return LastWriter = new(); } public void FinishRpcImmediately(MessageWriter writer) { } public void KickPlayer(int id, bool ban) { } }
class HarmonyArgument(int index) : Attribute { public int Index = index; }
enum RpcCalls : byte { SetName = 6, SetRole = 44, SendChat = 13, SendQuickChat = 33, StartMeeting = 14 }
enum RoleTypes { Crewmate }
static class EAC { public static bool Cancel; public static bool PlayerControlReceiveRpc(PlayerControl player, byte callId, MessageReader reader) => Cancel; }
static class ChatCommands { public static bool Cancel; public static void OnReceiveChat(PlayerControl player, string text, out bool canceled) => canceled = Cancel; }
namespace UnityEngine { record struct Vector2(float x, float y); struct Vector3 { } static class Mathf { public static int Clamp(int n, int min, int max) => Math.Clamp(n, min, max); } static class Time { public static float fixedDeltaTime = .02f; } }
namespace AmongUs.GameOptions { enum FloatOptionNames { CrewLightMod, ImpostorLightMod } interface IGameOptions { void SetVision(bool b); void SetFloat(FloatOptionNames key, float value); } static class NormalGameOptionsV11 { public static float[] KillDistances = [1, 2, 3]; } }
