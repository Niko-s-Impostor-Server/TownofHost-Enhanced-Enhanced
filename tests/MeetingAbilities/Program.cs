using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using TOHE;
using TOHE.Roles.Core;
using UnityEngine;

static class Program
{
    static int checks;
    static PlayerControl Actor => PlayerControl.LocalPlayer;
    static MeetingHud Meeting => MeetingHud.Instance;
    static TestAbility Ability(PlayerControl player) => (TestAbility)player.CustomRole;
    static PlayerVoteArea Area(byte id) => Meeting.playerStates.Single(area => area.PlayerId.Value == id);
    static void Check(bool value, string description)
    {
        if (!value) throw new Exception("FAIL: " + description);
        checks++;
    }
    static PlayerControl Add(byte id, int owner, bool owned, bool ability = true, bool compatible = true, RoleTypes type = RoleTypes.Crewmate)
    {
        PlayerControl player = new()
        {
            PlayerId = id, NetId = (uint)(1000 + id), OwnerId = owner, Owned = owned,
            CustomRole = ability ? new TestAbility() : new RoleBase(), RemainingEmergencies = 1, killTimer = 17.5f
        };
        player.Data.Role = type == RoleTypes.Impostor ? new ImpostorRole() : new RoleBehaviour { Role = type };
        player.Data.Role.Player = player;
        player.Data.RoleType = type;
        player.Data.RoleWhenAlive = new(type);
        player.myTasks.Add(new() { IsComplete = true });
        player.myTasks.Add(new() { IsComplete = false });
        Utils.Players[id] = player;
        AmongUsClient.Instance.Clients[owner] = new() { Character = player };
        if (compatible) TOHE.Main.playerVersion[owner] = new(TOHE.Main.ForkId, TOHE.Main.version,
            $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})");
        Meeting.playerStates.Add(new() { Parent = Meeting, PlayerId = new(id) });
        return player;
    }
    static void Reset(bool host = true, bool ability = true, RoleTypes type = RoleTypes.Crewmate)
    {
        if (MeetingHud.Instance) MeetingAbilities.EndMeeting(MeetingHud.Instance);
        Utils.Players.Clear();
        TOHE.Main.playerVersion.Clear();
        CustomRpcTransport.Sent.Clear();
        AmongUsClient.Instance = new() { AmHost = host };
        MeetingHud.Instance = new();
        Time.realtimeSinceStartup = 0;
        GameStates.IsNormalGame = GameStates.IsModHost = true;
        DestroyableSingleton<RoleManager>.Instance = new();
        ActorDummyReset();
        PlayerControl.LocalPlayer = Add(1, host ? 11 : 12, owned: true, ability, type: type);
        if (!host) Add(3, 11, owned: false, ability: false);
        Add(2, 20, owned: false, ability: false);
    }
    static void ActorDummyReset() => HudManager.Instance.Active = true;
    static byte[] Packet(byte kind, byte target = 255, uint nonce = 0, byte result = 0, int? game = null, uint? meeting = null, byte protocol = 1)
    {
        MessageWriter writer = new();
        writer.Write(protocol); writer.Write(kind); writer.Write(game ?? AmongUsClient.Instance.GameId);
        writer.Write(meeting ?? Meeting.NetId); writer.Write(target); writer.Write(nonce); writer.Write(result);
        return writer.Bytes;
    }
    static void Receive(PlayerControl sender, byte kind, byte target = 255, uint nonce = 0, byte result = 0) =>
        MeetingAbilities.ReceiveRequest(sender, new(Packet(kind, target, nonce, result)));
    static void ReadyOwner() => Receive(Utils.GetPlayerById(3), 3, result: 129);

    static void LocalPresentationAndVoteIndependence()
    {
        Reset(type: RoleTypes.Impostor);
        var tasks = Actor.myTasks.ToArray();
        Area(2).DidVote = true;
        Area(2).VotedForId = 1;
        Meeting.CurrentState = MeetingHud.MeetingStates.Discussion;
        MeetingAbilities.BeginMeeting(Meeting);
        Check(Actor.roleAssigned && Actor.Data.Role is JudgeRole, "assigned native living roles get owner-local Judge presentation");
        Check(Actor.Data.RoleType == RoleTypes.Impostor && Actor.Data.RoleWhenAlive.Value == RoleTypes.Impostor,
            "native metadata retains original role and alive role");
        Check(Actor.Data.Role.IsImpostor && Actor.Data.Role.DefaultGhostRole == RoleTypes.ImpostorGhost,
            "Eraser presentation preserves native death-team classification");
        Check(Actor.RemainingEmergencies == 1 && Actor.killTimer == 17.5f, "presentation keeps remaining meetings and kill cooldown");
        Check(Actor.myTasks.SequenceEqual(tasks) && tasks[0].IsComplete && !tasks[1].IsComplete && !tasks.Any(task => task.Destroyed),
            "impostor header and task objects and completion state survive presentation");
        Check(DestroyableSingleton<RoleManager>.Instance.GuardedHeaders == 1, "nonempty impostor deinitializer is narrowly blocked");
        Check(!HudManager.Instance.Active && MeetingAbilities.UsesJudgeButton(Actor), "meeting presentation keeps task HUD hidden");
        Check(MeetingAbilities.NativeJudgeBlocked(Actor), "discussion phase disables skill");
        MeetingAbilities.ClickJudge(Area(2));
        Check(Ability(Actor).Calls == 0, "discussion click does not execute");
        Check(!(bool)PatchCall.Invoke(typeof(MeetingAbilityNativeQueuePatch), "Prefix"), "native queue is blocked for presentation");
        object[] nativeTry = [Actor.Data.Role, true];
        Check(!(bool)PatchCall.Invoke(typeof(MeetingAbilityNativeTryPatch), "Prefix", nativeTry) && !(bool)nativeTry[1],
            "direct native TryOverrule is blocked before it can change vote state");
        Meeting.CurrentState = MeetingHud.MeetingStates.NotVoted;
        Check(!MeetingAbilities.NativeJudgeBlocked(Actor), "voting phase enables available skill");
        Check(!(bool)PatchCall.Invoke(typeof(MeetingAbilityClickPatch), "Prefix", Area(2)), "GUI click skips native Judge handler");
        Check(Ability(Actor).Calls == 1 && Ability(Actor).Uses == 2, "host uses role action once and retains original limit semantics");
        Check(!Area(1).DidVote && Area(1).VotedForId == 255 && Area(2).DidVote && Area(2).VotedForId == 1,
            "skill does not clear, cast, complete or change any real vote");
        Check(MeetingAbilities.NativeJudgeBlocked(Actor) && !Meeting.Indicator, "one-per-meeting role disables remaining action UI");
        MeetingAbilities.ClickJudge(Area(2));
        Check(Ability(Actor).Calls == 1, "second click cannot repeat one-per-meeting role skill");
        MeetingAbilities.EndMeeting(new() { NetId = Meeting.NetId });
        Check(Actor.Data.Role is JudgeRole, "unrelated meeting object cannot end the presentation");
        MeetingAbilities.EndMeeting(Meeting);
        Check(Actor.Data.Role.Role == RoleTypes.Impostor && Actor.killTimer == 17.5f && Actor.RemainingEmergencies == 1,
            "end restores native role without native Initialize kill cooldown reset");
        Check(Actor.myTasks.SequenceEqual(tasks) && tasks[0].IsComplete, "restore preserves exact task instances");
        Check(CustomRpcTransport.Sent.Count == 0, "host-local skill and role presentation send no native or custom request packets");
        Check((bool)PatchCall.Invoke(typeof(MeetingAbilityNativeQueuePatch), "Prefix"), "normal native queue remains available outside presentation");
    }
    static void MultipleUsesRebuildPresentation()
    {
        Reset();
        Ability(Actor).OnePerMeeting = false;
        MeetingAbilities.BeginMeeting(Meeting);
        var first = Actor.Data.Role;
        MeetingAbilities.ClickJudge(Area(2));
        var second = Actor.Data.Role;
        Check(first != second && second is JudgeRole && !MeetingAbilities.NativeJudgeBlocked(Actor),
            "accepted use with remaining actions reconstructs local Judge presentation");
        MeetingAbilities.ClickJudge(Area(2));
        Check(Ability(Actor).Calls == 2 && Ability(Actor).Uses == 1 && Actor.Data.Role != second,
            "multi-use role performs second host-authorized action after reconstruction");
        Check(!Area(1).DidVote && Actor.killTimer == 17.5f, "multiple action reconstruction leaves votes and cooldown intact");
    }
    static void RemoteHostValidation()
    {
        Reset(ability: false);
        var remote = Add(4, 14, owned: false);
        var unmodded = Add(5, 15, owned: false, compatible: false);
        MeetingAbilities.BeginMeeting(Meeting);
        Check(MeetingAbilities.UsesJudgeButton(remote), "host enrolls compatible remote before Ready for ordinary-vote bypass");
        Check(!MeetingAbilities.UsesJudgeButton(unmodded), "unmodded player retains legacy voting skill path");
        Receive(remote, 2, 2, 1);
        Check(Ability(remote).Calls == 0, "Use requires successful meeting-ready handshake");
        Receive(remote, 1);
        Check(CustomRpcTransport.Sent.Last().Target == remote.OwnerId && CustomRpcTransport.Sent.Last().Bytes.Length == 16,
            "Ready response uses exact custom packet and owner-only target");
        Receive(remote, 2, 2, 1);
        Check(Ability(remote).Calls == 0, "rejected pre-ready nonce cannot later be reused");
        Receive(remote, 2, 2, 2);
        Check(Ability(remote).Calls == 1 && Ability(remote).Uses == 2, "registered compatible owner executes permitted action");
        Receive(remote, 2, 2, 2);
        Receive(remote, 2, 2, 3);
        Check(Ability(remote).Calls == 1, "replay and once-per-meeting checks reject duplicate actions");
        Check(remote.Data.Role.Role == RoleTypes.Crewmate && !Area(4).DidVote,
            "host never changes remote native base or real vote");
        Receive(unmodded, 1);
        Receive(unmodded, 2, 2, 1);
        Check(Ability(unmodded).Calls == 0, "unmodded peer cannot enter custom request protocol");
    }
    static void RejectInvalidContexts()
    {
        foreach (var phase in new[] { MeetingHud.MeetingStates.Discussion, MeetingHud.MeetingStates.Results, MeetingHud.MeetingStates.Proceeding, MeetingHud.MeetingStates.Animating })
        {
            Reset(ability: false);
            var remote = Add(4, 14, owned: false);
            MeetingAbilities.BeginMeeting(Meeting);
            Receive(remote, 1);
            Meeting.CurrentState = phase;
            Receive(remote, 2, 2, 1);
            Check(Ability(remote).Calls == 0, "host rejects request phase " + phase);
        }
        for (int failure = 0; failure < 10; failure++)
        {
            Reset(ability: false);
            var remote = Add(4, 14, owned: false);
            var original = Ability(remote);
            MeetingAbilities.BeginMeeting(Meeting);
            Receive(remote, 1);
            byte[] request = Packet(2, 2, 1);
            switch (failure)
            {
                case 0: request = Packet(2, 2, 1, game: AmongUsClient.Instance.GameId + 1); break;
                case 1: request = Packet(2, 2, 1, meeting: Meeting.NetId + 1); break;
                case 2: request = Packet(2, 2, 1, protocol: 2); break;
                case 3: request = [..request, 0]; break;
                case 4: Area(4).DidVote = true; break;
                case 5: Utils.GetPlayerById(2).Data.IsDead = true; break;
                case 6: remote.Data.IsDead = true; break;
                case 7: remote.CustomRole = new TestAbility(); break;
                case 8: AmongUsClient.Instance.Clients[14].Character = new(); break;
                case 9: remote.Data.Disconnected = true; break;
            }
            MeetingAbilities.ReceiveRequest(remote, new(request));
            Check(original.Calls == 0, "host rejects invalid identity/context " + failure);
        }
        Reset(ability: false);
        var voter = Add(4, 14, owned: false);
        MeetingAbilities.BeginMeeting(Meeting);
        Receive(voter, 1);
        Receive(voter, 2, 4, 1);
        Receive(voter, 2, 251, 2);
        Receive(voter, 2, 253, 3);
        Check(Ability(voter).Calls == 0, "self/missing/skip targets are rejected");
        Meeting.CurrentState = MeetingHud.MeetingStates.Voted; // host has voted; remote has not.
        Receive(voter, 2, 2, 4);
        Check(Ability(voter).Calls == 1, "host Voted state still permits an unvoted remote owner");
    }
    static void OwnerHandshakeAndTrustedResults()
    {
        Reset(host: false);
        MeetingAbilities.BeginMeeting(Meeting);
        Check(Actor.Data.Role.Role == RoleTypes.Crewmate && CustomRpcTransport.Sent.Single().Target == 11,
            "remote owner requests Ready before local Judge presentation");
        Receive(Utils.GetPlayerById(2), 3, result: 129);
        Check(Actor.Data.Role.Role == RoleTypes.Crewmate, "result from nonhost canonical peer cannot enable presentation");
        Time.realtimeSinceStartup = 1;
        MeetingAbilities.Tick();
        Check(CustomRpcTransport.Sent.Count == 2, "bounded Ready retry handles peer meeting Start ordering");
        ReadyOwner();
        Check(Actor.Data.Role is JudgeRole, "canonical matching-version host Ready enables native GUI");
        ReadyOwner();
        Check(DestroyableSingleton<RoleManager>.Instance.Changes == 1, "duplicate Ready cannot reconstruct or reenable ability");
        MeetingAbilities.ClickJudge(Area(2));
        var request = CustomRpcTransport.Sent.Last();
        Check(request.Rpc == CustomRPC.MeetingAbilityRequest && request.Target == 11 && request.Bytes.Length == 16 && request.Bytes[1] == 2,
            "GUI sends only custom skill Use request to host");
        Check(Ability(Actor).Calls == 0 && !Area(1).DidVote && MeetingAbilities.NativeJudgeBlocked(Actor),
            "owner does not execute locally and pending request blocks another click");
        Receive(Utils.GetPlayerById(3), 3, 2, 99, 130);
        Receive(Utils.GetPlayerById(3), 3, 1, 1, 130);
        Check(MeetingAbilities.NativeJudgeBlocked(Actor), "wrong nonce or target results cannot release pending use");
        Receive(Utils.GetPlayerById(3), 3, 2, 1, 130);
        Check(!MeetingAbilities.NativeJudgeBlocked(Actor) && DestroyableSingleton<RoleManager>.Instance.Changes == 2,
            "matching accepted result releases pending use and reconstructs for remaining actions");
        Receive(Utils.GetPlayerById(3), 3, 2, 1, 2);
        Check(!MeetingAbilities.NativeJudgeBlocked(Actor), "replayed old result cannot consume current availability");
        MeetingAbilities.ClickJudge(Area(2));
        Time.realtimeSinceStartup = 9;
        int sent = CustomRpcTransport.Sent.Count;
        MeetingAbilities.Tick();
        Check(MeetingAbilities.NativeJudgeBlocked(Actor) && CustomRpcTransport.Sent.Count == sent,
            "unobserved Use times out with no mutation request retry");
        Check(!Area(1).DidVote && Area(1).VotedForId == 255, "requests/results/timeouts never alter local real vote");
    }
    static void EndIdentityAndDeath()
    {
        Reset(type: RoleTypes.Impostor);
        MeetingAbilities.BeginMeeting(Meeting);
        Check(Actor.Data.Role.IsImpostor && Actor.Data.Role.DefaultGhostRole == RoleTypes.ImpostorGhost,
            "native AssignRoleOnDeath inputs stay impostor while Judge GUI is active");
        Actor.Data.IsDead = true; // Native death precedes mod PlayerState death update.
        Check(Actor.IsAlive(), "fixture models delayed mod death state");
        MeetingAbilities.Tick();
        Check(MeetingAbilities.NativeJudgeBlocked(Actor), "native dead flag blocks action despite delayed mod state");
        int changes = DestroyableSingleton<RoleManager>.Instance.Changes;
        MeetingAbilities.EndMeeting(Meeting);
        Check(Actor.Data.IsDead && DestroyableSingleton<RoleManager>.Instance.Changes == changes,
            "dead meeting actor is never restored through SetRole or revived");
        Reset();
        MeetingAbilities.BeginMeeting(Meeting);
        Actor.CustomRole = new TestAbility();
        changes = DestroyableSingleton<RoleManager>.Instance.Changes;
        MeetingAbilities.EndMeeting(Meeting);
        Check(DestroyableSingleton<RoleManager>.Instance.Changes == changes, "replaced custom role instance cannot receive stale restore");
        Reset();
        MeetingAbilities.BeginMeeting(Meeting);
        changes = DestroyableSingleton<RoleManager>.Instance.Changes;
        AmongUsClient.Instance.GameId++;
        MeetingAbilities.Tick();
        Check(DestroyableSingleton<RoleManager>.Instance.Changes == changes, "new game cannot receive previous meeting restoration");
        Reset();
        var noClient = Actor;
        AmongUsClient.Instance = null;
        Check(!MeetingAbilities.UsesJudgeButton(noClient) && !MeetingAbilities.OwnsJudgePresentation(noClient) &&
            !MeetingAbilities.PreservingTasks(noClient), "menu guards short-circuit before AmOwner when native client is absent");
        Check((bool)PatchCall.Invoke(typeof(MeetingAbilityPreserveHeaderPatch), "Prefix", noClient),
            "menu impostor deinitializer runs without native client");
        AmongUsClient.Instance = new();
    }
    static void ReplayPolicy()
    {
        MeetingAbilityReplayWindow replay = new();
        Check(!replay.Accept(0, 0, 0, 1, true), "inactive replay window denies requests");
        replay.Begin(1234, 80);
        Check(replay.Accept(1234, 80, 11, 1, true), "first authorized nonce accepted");
        Check(!replay.Accept(1234, 80, 11, 1, true) && !replay.Accept(1234, 80, 11, 0, true), "duplicate/zero nonce rejected");
        Check(!replay.Accept(1234, 80, 11, 2, false) && !replay.Accept(1234, 80, 11, 2, true),
            "invalid action consumes nonce and cannot be retargeted");
        Check(replay.Accept(1234, 80, 12, 1, true), "owner nonce sequences are independent");
        Check(!replay.Accept(1235, 80, 11, 3, true) && !replay.Accept(1234, 81, 11, 3, true) &&
            !replay.Accept(1234, 80, -1, 3, true), "wrong game/meeting/negative owner rejected");
        replay.Begin(1234, 81);
        Check(replay.Accept(1234, 81, 11, 1, true), "new meeting starts a fresh bounded sequence");
        replay.Clear();
        Check(!replay.Accept(1234, 81, 11, 2, true), "cleared meeting rejects stale requests");
    }
    static void OrdinaryVotesAndVanillaFallback()
    {
        Reset();
        var remote = Add(4, 14, owned: false);
        var unmodded = Add(5, 15, owned: false, compatible: false);
        MeetingAbilities.BeginMeeting(Meeting);
        Check(VoteFixture.Prefix(Meeting, new(1), new(2)) && Ability(Actor).LegacyCalls == 0 && !Ability(Actor).HasVoted,
            "real host CastVote prefix leaves modded ordinary vote separate from skill");
        Check(VoteFixture.Prefix(Meeting, new(1), new(253)) && Meeting.ClearedVotes == 0 && !Ability(Actor).HasVoted,
            "modded skip vote neither invokes ability-opt-out nor clears vote");
        Check(VoteFixture.Prefix(Meeting, new(4), new(2)) && Ability(remote).LegacyCalls == 0,
            "early compatible remote ordinary vote bypasses legacy skill before Ready response");
        Check(!VoteFixture.Prefix(Meeting, new(5), new(2)) && Ability(unmodded).LegacyCalls == 1 &&
            Ability(unmodded).HasVoted && Meeting.ClearedVotes == 1,
            "unmodded player's first targeted vote preserves legacy skill and vote-return behavior");
        Ability(unmodded).HasVoted = false;
        Check(!VoteFixture.Prefix(Meeting, new(5), new(253)) && Ability(unmodded).HasVoted && Meeting.ClearedVotes == 2,
            "unmodded skip preserves legacy opt-out behavior");
    }
    static void BuildTagCompatibility()
    {
        Reset(ability: false);
        var old = Add(4, 14, owned: false);
        var current = Add(5, 15, owned: false);
        TOHE.Main.playerVersion[old.OwnerId] = new(TOHE.Main.ForkId, TOHE.Main.version, "old-commit(codex/current)");
        MeetingAbilities.BeginMeeting(Meeting);
        Check(!MeetingAbilities.UsesJudgeButton(old), "same-version old commit is not enrolled into meeting protocol");
        Check(MeetingAbilities.UsesJudgeButton(current), "current commit/branch is enrolled into meeting protocol");
        Check(!VoteFixture.Prefix(Meeting, new(4), new(2)) && Ability(old).LegacyCalls == 1,
            "old build keeps legacy targeted-vote skill instead of silently losing it");
        Receive(old, 1);
        Receive(old, 2, 2, 1);
        Check(Ability(old).Calls == 0 && CustomRpcTransport.Sent.Count == 0,
            "old-tag custom Ready and Use are rejected without response or execution");
        Receive(current, 1);
        Receive(current, 2, 2, 1);
        Check(Ability(current).Calls == 1, "current tag Ready and Use execute normally");

        Reset(host: false);
        var host = Utils.GetPlayerById(3);
        TOHE.Main.playerVersion[host.OwnerId] = new(TOHE.Main.ForkId, TOHE.Main.version, "current-commit(old-branch)");
        MeetingAbilities.BeginMeeting(Meeting);
        Check(Actor.Data.Role.Role == RoleTypes.Crewmate && CustomRpcTransport.Sent.Count == 0,
            "same-version host with another branch cannot start owner presentation or Ready");
        Receive(host, 3, result: 129);
        Check(Actor.Data.Role.Role == RoleTypes.Crewmate, "old-tag host result cannot enable native Judge GUI");
        TOHE.Main.playerVersion[host.OwnerId] = new(TOHE.Main.ForkId, TOHE.Main.version,
            $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})");
        MeetingAbilities.BeginMeeting(Meeting);
        ReadyOwner();
        Check(Actor.Data.Role is JudgeRole, "current host character/tag enables owner presentation");

        Reset(host: false);
        AmongUsClient.Instance.Clients.Remove(AmongUsClient.Instance.HostId);
        MeetingAbilities.BeginMeeting(Meeting);
        Check(CustomRpcTransport.Sent.Count == 0 && Actor.Data.Role.Role == RoleTypes.Crewmate,
            "version record without canonical host character cannot authorize meeting protocol");
    }
    static void Main()
    {
        LocalPresentationAndVoteIndependence();
        MultipleUsesRebuildPresentation();
        RemoteHostValidation();
        RejectInvalidContexts();
        OwnerHandshakeAndTrustedResults();
        EndIdentityAndDeath();
        ReplayPolicy();
        OrdinaryVotesAndVanillaFallback();
        BuildTagCompatibility();
        Console.WriteLine($"Meeting abilities: {checks} assertions passed (linked production sources; simulated native/RPC dependencies).");
    }
}
