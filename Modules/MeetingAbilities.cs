using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using TOHE.Roles.Core;
using UnityEngine;

namespace TOHE;

// A role implements its existing skill rules here; voting remains a separate action.
internal interface IMeetingTargetAbility
{
    bool CanUseMeetingAbility(PlayerControl actor);
    bool CanTargetMeetingAbility(PlayerControl actor, PlayerControl target);
    bool UseMeetingAbility(PlayerControl actor, PlayerControl target);
}

internal static class MeetingAbilities
{
    private enum Kind : byte { Ready = 1, Use = 2, Result = 3 }
    private enum Result : byte { Ready = 1, Accepted = 2, Rejected = 3 }
    private const byte Protocol = 1;
    private const byte AvailableFlag = 128;
    private const int PacketLength = 16;
    private static MeetingHud meeting;
    private static int gameId;
    private static readonly Dictionary<byte, HostSlot> hostSlots = [];
    private static readonly MeetingAbilityReplayWindow replay = new();
    private static LocalSlot local;
    private static bool changingBase;

    private sealed class HostSlot(PlayerControl actor, IMeetingTargetAbility ability)
    {
        internal readonly PlayerControl Actor = actor;
        internal readonly int Owner = actor.OwnerId;
        internal readonly IMeetingTargetAbility Ability = ability;
        internal bool Ready;
    }

    private sealed class LocalSlot(PlayerControl actor)
    {
        internal readonly PlayerControl Actor = actor;
        internal readonly int Owner = actor.OwnerId;
        internal readonly RoleBase Ability = actor.GetRoleClass();
        internal readonly RoleTypes OriginalBase = actor.Data.Role.Role;
        internal readonly RoleTypes OriginalRoleType = actor.Data.RoleType;
        internal readonly Il2CppSystem.Nullable<RoleTypes> OriginalRoleWhenAlive = actor.Data.RoleWhenAlive;
        internal bool Ready, Available, Ending, Presented;
        internal uint NextNonce, PendingNonce;
        internal byte PendingTarget;
        internal float PendingSince;
        internal float ReadySince = Time.realtimeSinceStartup;
        internal float LastReady = Time.realtimeSinceStartup;
    }

    internal static bool UsesJudgeButton(PlayerControl actor)
    {
        if (!AmongUsClient.Instance || !actor || !meeting || MeetingHud.Instance != meeting || AmongUsClient.Instance.GameId != gameId) return false;
        if (actor.AmOwner) return LocalBound() && local.Ready && !local.Ending;
        return AmongUsClient.Instance.AmHost && hostSlots.TryGetValue(actor.PlayerId, out var slot) && Bound(slot, actor);
    }

    private static bool LocalBound() => local != null && AmongUsClient.Instance && local.Actor && local.Actor.AmOwner &&
        local.Actor == PlayerControl.LocalPlayer && local.Actor.OwnerId == local.Owner &&
        ReferenceEquals(local.Actor.GetRoleClass(), local.Ability) && CanonicalOwner(local.Actor);

    // This remains true while the presentation is owned, including pending/invalidated
    // state, so a native path cannot turn a failed skill into a real Judge overrule.
    internal static bool OwnsJudgePresentation(PlayerControl actor) => local != null && local.Presented &&
        AmongUsClient.Instance && actor && actor == local.Actor && actor.AmOwner;

    private static bool Bound(HostSlot slot, PlayerControl actor) => actor && actor == slot.Actor &&
        actor.OwnerId == slot.Owner && ReferenceEquals(actor.GetRoleClass(), slot.Ability) && CanonicalOwner(actor);

    private static bool CanonicalOwner(PlayerControl actor)
    {
        if (!AmongUsClient.Instance || !actor || actor.Data == null || actor.OwnerId < 0 || actor.GetClientId() != actor.OwnerId) return false;
        var client = AmongUsClient.Instance.FindClientById(actor.OwnerId);
        return client != null && client.Character && client.Character.Pointer == actor.Pointer;
    }

    private static bool CompatiblePeer(PlayerControl actor) =>
        CanonicalOwner(actor) && RpcCompatibility.IsCurrentClient(actor);

    private static bool CompatibleHost()
    {
        var client = AmongUsClient.Instance;
        if (!client) return false;
        if (client.AmHost) return true;
        var host = client.FindClientById(client.HostId)?.Character;
        return host && host.OwnerId == client.HostId && CompatiblePeer(host);
    }

    private static bool Context() => AmongUsClient.Instance && AmongUsClient.Instance.AmConnected &&
        AmongUsClient.Instance.GameState == InnerNetClient.GameStates.Started && GameStates.IsNormalGame &&
        GameStates.IsModHost && meeting && MeetingHud.Instance == meeting && AmongUsClient.Instance.GameId == gameId;

    private static bool VotingPhase() => Context() && meeting.CurrentState is
        MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted;

    private static bool Alive(PlayerControl actor) => actor && actor.Data != null &&
        !actor.Data.IsDead && !actor.Data.Disconnected && actor.IsAlive();

    internal static void BeginMeeting(MeetingHud instance)
    {
        if (!instance || !AmongUsClient.Instance || !GameStates.IsNormalGame || !CompatibleHost()) return;
        EndMeeting(meeting);
        meeting = instance;
        gameId = AmongUsClient.Instance.GameId;
        replay.Begin(gameId, instance.NetId);
        hostSlots.Clear();
        var actor = PlayerControl.LocalPlayer;
        local = actor && actor.Data != null && actor.Data.Role && Alive(actor) &&
            actor.GetRoleClass() is IMeetingTargetAbility ? new LocalSlot(actor) : null;
        if (AmongUsClient.Instance.AmHost)
        {
            // Enroll before the handshake: an early ordinary vote from a compatible
            // client must never invoke its legacy CheckVote skill while Ready is in flight.
            foreach (var player in Main.AllAlivePlayerControls)
                if (Alive(player) && CompatiblePeer(player) && player.GetRoleClass() is IMeetingTargetAbility ability)
                    hostSlots[player.PlayerId] = new HostSlot(player, ability);
        }
        if (local == null) return;
        if (AmongUsClient.Instance.AmHost) Register(actor);
        else Send(Kind.Ready, 255, 0, 0, AmongUsClient.Instance.HostId);
    }

    private static void Register(PlayerControl actor)
    {
        if (!Context() || !AmongUsClient.Instance.AmHost || !CompatiblePeer(actor) || !Alive(actor) ||
            actor.GetRoleClass() is not IMeetingTargetAbility) return;
        if (!hostSlots.TryGetValue(actor.PlayerId, out var slot) || !Bound(slot, actor)) return;
        slot.Ready = true;
        Reply(actor, 255, 0, Result.Ready, slot.Ability.CanUseMeetingAbility(actor));
    }

    // Called by the central RPC dispatcher with its canonical PlayerControl sender.
    // The payload never declares an actor; transport/net-object ownership is a separate boundary.
    internal static void ReceiveRequest(PlayerControl sender, MessageReader reader)
    {
        if (!Context() || !sender || !CanonicalOwner(sender) || reader.BytesRemaining != PacketLength) return;
        byte protocol = reader.ReadByte();
        var kind = (Kind)reader.ReadByte();
        int requestGame = reader.ReadInt32();
        uint requestMeeting = reader.ReadUInt32();
        byte target = reader.ReadByte();
        uint nonce = reader.ReadUInt32();
        byte result = reader.ReadByte();
        if (protocol != Protocol || requestGame != gameId || requestMeeting != meeting.NetId) return;
        if (kind == Kind.Result)
        {
            // Trusted RPC classification never substitutes for this host identity check.
            if (sender.OwnerId != AmongUsClient.Instance.HostId || !sender.IsHost() || !CompatiblePeer(sender)) return;
            ReceiveResult(target, nonce, result);
            return;
        }
        if (!AmongUsClient.Instance.AmHost || !CompatiblePeer(sender) || result != 0) return;
        if (kind == Kind.Ready && target == 255 && nonce == 0) Register(sender);
        else if (kind == Kind.Use && nonce != 0 && target < 252) Execute(sender, target, nonce);
    }

    internal static void ClickJudge(PlayerVoteArea area)
    {
        var actor = PlayerControl.LocalPlayer;
        if (!area || !UsesJudgeButton(actor) || !VotingPhase() || local == null || !local.Available ||
            local.PendingNonce != 0 || meeting.CurrentState != MeetingHud.MeetingStates.NotVoted ||
            area.Parent != meeting || !Alive(actor) || !area.gameObject.activeInHierarchy ||
            !meeting.playerStates.Any(current => current && current.Pointer == area.Pointer)) return;
        var target = Utils.GetPlayerById(area.PlayerId.Value);
        if (!Alive(target) || target == actor) return;
        uint nonce = ++local.NextNonce;
        if (nonce == 0) return;
        local.PendingNonce = nonce;
        local.PendingTarget = target.PlayerId;
        local.PendingSince = Time.realtimeSinceStartup;
        ControllerManager.Instance.CloseOverlayMenu(area.name);
        area.ClearButtons(); // Close only the selection UI; leave real vote state untouched.
        if (AmongUsClient.Instance.AmHost) Execute(actor, target.PlayerId, nonce);
        else Send(Kind.Use, target.PlayerId, nonce, 0, AmongUsClient.Instance.HostId);
    }

    private static void Execute(PlayerControl actor, byte targetId, uint nonce)
    {
        if (!hostSlots.TryGetValue(actor.PlayerId, out var slot) || !Bound(slot, actor)) return;
        var target = Utils.GetPlayerById(targetId);
        var voteArea = meeting.playerStates.FirstOrDefault(area => area.PlayerId.Value == actor.PlayerId);
        bool valid = slot.Ready && VotingPhase() && Alive(actor) && Alive(target) && target != actor &&
            voteArea && !voteArea.DidVote &&
            slot.Ability.CanUseMeetingAbility(actor) &&
            slot.Ability.CanTargetMeetingAbility(actor, target);
        if (!replay.Accept(gameId, meeting.NetId, actor.OwnerId, nonce, valid))
        {
            Reply(actor, targetId, nonce, Result.Rejected, slot.Ability.CanUseMeetingAbility(actor));
            return;
        }
        bool used = slot.Ability.UseMeetingAbility(actor, target);
        bool remaining = Alive(actor) && slot.Ability.CanUseMeetingAbility(actor);
        Reply(actor, targetId, nonce, used ? Result.Accepted : Result.Rejected, remaining);
    }

    private static void Reply(PlayerControl actor, byte target, uint nonce, Result result, bool available)
    {
        byte flags = (byte)((byte)result | (available ? AvailableFlag : 0));
        if (actor.AmOwner) ReceiveResult(target, nonce, flags);
        else Send(Kind.Result, target, nonce, flags, actor.OwnerId);
    }

    private static void ReceiveResult(byte target, uint nonce, byte flags)
    {
        if (!LocalBound() || local.Ending || !Context() || !Alive(local.Actor)) return;
        var result = (Result)(flags & ~AvailableFlag);
        if (result == Result.Ready)
        {
            if (target != 255 || nonce != 0 || local.Ready) return;
            local.Ready = true;
        }
        else if (result is Result.Accepted or Result.Rejected)
        {
            if (nonce == 0 || nonce != local.PendingNonce || target != local.PendingTarget) return;
            local.PendingNonce = 0;
        }
        else return;
        local.Available = (flags & AvailableFlag) != 0;
        if (local.Available && (!local.Presented || result == Result.Accepted))
        {
            local.Presented = true;
            SetOwnerBase(RoleTypes.Judge);
        }
        meeting.UpdateJudgeAbilityIndicator(local.Available, 0);
        // Do not call ClearVote, SetVoteComplete, CmdCastVote or the native Judge queue.
        foreach (var area in meeting.playerStates) area.ClearButtons();
    }

    private static void Send(Kind kind, byte target, uint nonce, byte result, int receiver)
    {
        CustomRpcTransport.Send(CustomRPC.MeetingAbilityRequest, writer =>
        {
            writer.Write(Protocol); writer.Write((byte)kind); writer.Write(gameId); writer.Write(meeting.NetId);
            writer.Write(target); writer.Write(nonce); writer.Write(result);
        }, receiver);
    }

    private static void SetOwnerBase(RoleTypes role)
    {
        var actor = local.Actor;
        int emergencies = actor.RemainingEmergencies;
        float killTimer = actor.killTimer;
        var tasks = actor.myTasks.ToArray();
        var team = actor.Data.Role.TeamType;
        var ghost = actor.Data.Role.DefaultGhostRole;
        changingBase = true;
        try
        {
            // Presentation is owner-local. CoSetRole rejects assigned living roles even
            // with canOverride, and resets emergency/stat state. No SetRole packet is sent.
            DestroyableSingleton<RoleManager>.Instance.SetRole(actor, role);
            if (role == RoleTypes.Judge)
            {
                // Judge is a GUI presentation, not a team conversion. Native death
                // assignment reads these fields rather than RoleType metadata.
                actor.Data.Role.TeamType = team;
                actor.Data.Role.DefaultGhostRole = ghost;
            }
        }
        finally
        {
            actor.RemainingEmergencies = emergencies;
            actor.killTimer = killTimer;
            actor.Data.RoleType = local.OriginalRoleType;
            actor.Data.RoleWhenAlive = local.OriginalRoleWhenAlive;
            actor.myTasks.Clear();
            foreach (var task in tasks) if (task) actor.myTasks.Add(task);
            changingBase = false;
            if (meeting && MeetingHud.Instance == meeting && DestroyableSingleton<HudManager>.InstanceExists)
                HudManager.Instance.SetHudActive(false);
        }
    }

    internal static bool PreservingTasks(PlayerControl actor) => local != null && changingBase &&
        AmongUsClient.Instance && actor && local.Actor == actor && actor.AmOwner;

    internal static bool NativeJudgeBlocked(PlayerControl actor) => !UsesJudgeButton(actor) || !VotingPhase() ||
        !Alive(actor) || !local.Available || local.PendingNonce != 0 ||
        meeting.CurrentState != MeetingHud.MeetingStates.NotVoted;

    internal static void Tick()
    {
        if (meeting && (!Context() || !CompatibleHost()))
        {
            EndMeeting(meeting);
            return;
        }
        if (local != null)
        {
            if (!LocalBound() || !Alive(local.Actor))
            {
                local.Available = false;
                local.PendingNonce = 0;
                return;
            }
            if (!local.Ready && !AmongUsClient.Instance.AmHost && Context() &&
                Time.realtimeSinceStartup - local.ReadySince < 8 &&
                Time.realtimeSinceStartup - local.LastReady >= 1)
            {
                local.LastReady = Time.realtimeSinceStartup;
                Send(Kind.Ready, 255, 0, 0, AmongUsClient.Instance.HostId);
            }
            if (local.PendingNonce != 0 && Time.realtimeSinceStartup - local.PendingSince >= 8)
            {
                local.PendingNonce = 0;
                local.Available = false; // Stop; no request retry after an unobserved result.
            }
        }
    }

    internal static void EndMeeting(MeetingHud instance)
    {
        if (instance == null || meeting == null || instance.Pointer != meeting.Pointer) return;
        if (local != null)
        {
            local.Ending = true;
            local.Available = false;
            local.PendingNonce = 0;
            if (AmongUsClient.Instance && AmongUsClient.Instance.GameId == gameId &&
                LocalBound() && local.Presented && Alive(local.Actor) &&
                local.Actor.Data.Role && local.Actor.Data.Role.Role == RoleTypes.Judge)
                SetOwnerBase(local.OriginalBase);
        }
        local = null;
        hostSlots.Clear();
        replay.Clear();
        meeting = null;
    }
}
