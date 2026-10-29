using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using TOHE.Roles.Core;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE;

internal enum MeetingAbilityTrigger { Vote, JudgeButton }

internal interface IMeetingTargetAbility
{
    MeetingAbilityTrigger Trigger { get; }
    bool CanUseMeetingAbility(PlayerControl actor);
    bool CanTargetMeetingAbility(PlayerControl actor, PlayerControl target);
    // The shared dispatcher has already checked the actor, target and availability.
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
    private static OptionItem enabled;
    private static OptionItem impostorsEnabled;

    internal static void SetupOptions()
    {
        enabled = BooleanOptionItem.Create(60748, "EnableVanillaJudgeMeetings", true, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Standard);
        impostorsEnabled = BooleanOptionItem.Create(60749, "EnableVanillaImpostorJudge", false, TabGroup.ModSettings, false)
            .SetParent(enabled).SetGameMode(CustomGameMode.Standard);
    }

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
        internal readonly List<GameObject> Buttons = [];
        internal bool Ready, Available;
        internal uint NextNonce, PendingNonce;
        internal byte PendingTarget;
        internal float PendingSince;
        internal float ReadySince = Time.realtimeSinceStartup;
        internal float LastReady = Time.realtimeSinceStartup;
    }

    internal static OptionItem CreateTriggerOption(int id, CustomRoles role, TabGroup tab) =>
        StringOptionItem.Create(id, "VanillaMeetingAbilityTrigger", ["MeetingAbilityVote", "MeetingAbilityJudgeButton"], 0, tab, false)
            .SetParent(Options.CustomRoleSpawnChances[role]);

    // Resolve this per player, before initial role publication. Mod clients keep their own UI.
    internal static RoleTypes GetNativeRole(CustomRoles role, PlayerControl actor, RoleTypes original) =>
        enabled.GetBool() && !actor.IsModded() && original == RoleTypes.Crewmate && role.GetStaticRoleClass() is IMeetingTargetAbility { Trigger: MeetingAbilityTrigger.JudgeButton }
            ? RoleTypes.Judge : original;

    internal static bool NativeButtonConfigured(PlayerControl actor) => enabled.GetBool() && actor && !actor.IsModded() &&
        actor.GetRoleClass() is IMeetingTargetAbility { Trigger: MeetingAbilityTrigger.JudgeButton } &&
        (actor.GetCustomRole().GetRoleTypes() == RoleTypes.Crewmate ||
            impostorsEnabled.GetBool() && actor.Is(Custom_Team.Impostor));

    internal static bool NeedsTemporaryRole(PlayerControl actor) => NativeButtonConfigured(actor) &&
        actor.Is(Custom_Team.Impostor) && actor.GetRoleClass() is IMeetingTargetAbility ability &&
        ability.CanUseMeetingAbility(actor);

    internal static bool UsesSeparateAbility(PlayerControl actor) => actor && actor.GetRoleClass() is IMeetingTargetAbility &&
        (actor.IsModded() || NativeButtonConfigured(actor) && !Utils.IsActive(SystemTypes.Comms));

    internal static void ApplyGameOptions(PlayerControl actor, IGameOptions options)
    {
        if (!NativeButtonConfigured(actor)) return;
        AURoleOptions.JudgeTaskRequirementPercentage = 0f;
        options.SetInt(Int32OptionNames.NumEmergencyMeetings, System.Math.Max(0,
            options.GetInt(Int32OptionNames.NumEmergencyMeetings) - VanillaMeetingRoles.EmergenciesUsed(actor.PlayerId)));
    }

    internal static bool TryUse(PlayerControl actor, PlayerControl target) =>
        AmongUsClient.Instance.AmHost && GameStates.IsVoting && Alive(actor) && Alive(target) &&
        actor.GetRoleClass() is IMeetingTargetAbility ability && ability.CanUseMeetingAbility(actor) &&
        ability.CanTargetMeetingAbility(actor, target) && ability.UseMeetingAbility(actor, target);

    internal static bool HandleNativeJudge(MeetingHud instance, PlayerId actorId, PlayerId targetId, ushort nonce)
    {
        var actor = Utils.GetPlayerById(actorId.Value);
        if (!NativeButtonConfigured(actor)) return false;
        var vote = instance.playerStates.FirstOrDefault(area => area.PlayerId.Value == actorId.Value);
        bool used = instance == meeting && Context() && UsesSeparateAbility(actor) && nonce != 0 && vote && !vote.DidVote &&
            TryUse(actor, Utils.GetPlayerById(targetId.Value));
        if (!used && Alive(actor)) Utils.SendMessage(GetString("MeetingAbilityRejected"), actor.PlayerId);
        // TryOverrule has already locked the vanilla UI. ClearVote also calls
        // ClearOverrule, so both a cancellation and a successful skill return the real vote.
        // Its nonce is deliberately reusable: vanilla retries keep the same nonce.
        if (Alive(actor) && VotingPhase() && vote && !vote.DidVote)
            instance.RpcClearVoteDelay(actor.GetClientId(), onlyIfUncast: true);
        return true;
    }

    private static bool LocalBound() => local != null && AmongUsClient.Instance && local.Actor && local.Actor.AmOwner &&
        local.Actor == PlayerControl.LocalPlayer && local.Actor.OwnerId == local.Owner &&
        ReferenceEquals(local.Actor.GetRoleClass(), local.Ability) && CanonicalOwner(local.Actor);

    private static bool Bound(HostSlot slot, PlayerControl actor) => actor && actor == slot.Actor &&
        actor.OwnerId == slot.Owner && ReferenceEquals(actor.GetRoleClass(), slot.Ability) && CanonicalOwner(actor);

    private static bool CanonicalOwner(PlayerControl actor)
    {
        if (!AmongUsClient.Instance || !actor || actor.Data == null || actor.OwnerId < 0 || actor.GetClientId() != actor.OwnerId) return false;
        var client = AmongUsClient.Instance.FindClientById(actor.OwnerId);
        return client != null && client.Character && client.Character.Pointer == actor.Pointer;
    }

    private static bool CompatiblePeer(PlayerControl actor) => CanonicalOwner(actor) && RpcCompatibility.IsCurrentClient(actor);

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

    private static bool VotingPhase() => Context() && meeting.CurrentState is MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted;

    private static bool Alive(PlayerControl actor) => actor && actor.Data != null &&
        !actor.Data.IsDead && !actor.Data.Disconnected && actor.IsAlive();

    internal static void BeginMeeting(MeetingHud instance)
    {
        if (!instance || !AmongUsClient.Instance || !GameStates.IsNormalGame || !CompatibleHost()) return;
        EndMeeting(meeting);
        meeting = instance;
        gameId = AmongUsClient.Instance.GameId;
        replay.Begin(gameId, instance.NetId);
        if (AmongUsClient.Instance.AmHost) VanillaMeetingRoles.Begin();
        var actor = PlayerControl.LocalPlayer;
        local = Alive(actor) && actor.GetRoleClass() is IMeetingTargetAbility ? new LocalSlot(actor) : null;
        if (AmongUsClient.Instance.AmHost)
        {
            foreach (var player in Main.AllAlivePlayerControls)
            {
                if (CompatiblePeer(player) && player.GetRoleClass() is IMeetingTargetAbility ability)
                    hostSlots[player.PlayerId] = new HostSlot(player, ability);
                else if (NativeButtonConfigured(player))
                    Utils.SendMessage(GetString(Utils.IsActive(SystemTypes.Comms) ? "MeetingAbilityCommsFallback" : "MeetingAbilityNativeHelp"), player.PlayerId);
            }
        }
        if (local == null) return;
        if (AmongUsClient.Instance.AmHost) Register(actor);
        else Send(Kind.Ready, 255, 0, 0, AmongUsClient.Instance.HostId);
    }

    private static void Register(PlayerControl actor)
    {
        if (!Context() || !AmongUsClient.Instance.AmHost || !CompatiblePeer(actor) || !Alive(actor)) return;
        if (!hostSlots.TryGetValue(actor.PlayerId, out var slot) || !Bound(slot, actor)) return;
        slot.Ready = true;
        Reply(actor, 255, 0, Result.Ready, slot.Ability.CanUseMeetingAbility(actor));
    }

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
            if (sender.OwnerId != AmongUsClient.Instance.HostId || !sender.IsHost() || !CompatiblePeer(sender)) return;
            ReceiveResult(target, nonce, result);
        }
        else if (AmongUsClient.Instance.AmHost && CompatiblePeer(sender) && result == 0)
        {
            if (kind == Kind.Ready && target == 255 && nonce == 0) Register(sender);
            else if (kind == Kind.Use && nonce != 0 && target < 252) Execute(sender, target, nonce);
        }
    }

    private static void CreateButtons()
    {
        // These roles already own their meeting buttons and RPC paths.
        if (local.Actor.Is(CustomRoles.Judge) || local.Actor.Is(CustomRoles.DoubleAgent)) return;
        foreach (var area in meeting.playerStates)
        {
            byte targetId = area.PlayerId.Value;
            if (targetId == local.Actor.PlayerId &&
                !((IMeetingTargetAbility)local.Ability).CanTargetMeetingAbility(local.Actor, local.Actor)) continue;
            var buttonObject = Object.Instantiate(area.Buttons.transform.Find("CancelButton").gameObject, area.transform);
            buttonObject.name = "MeetingSkillButton";
            buttonObject.transform.localPosition = new Vector3(-0.35f, 0.03f, -1.31f);
            buttonObject.GetComponent<SpriteRenderer>().sprite = CustomButton.Get("JudgeIcon");
            var button = buttonObject.GetComponent<PassiveButton>();
            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => Click(targetId)));
            local.Buttons.Add(buttonObject);
        }
    }

    private static void Click(byte targetId)
    {
        if (!LocalBound() || !local.Ready || !local.Available || local.PendingNonce != 0 || !VotingPhase() || !Alive(local.Actor)) return;
        uint nonce = ++local.NextNonce;
        if (nonce == 0) return;
        local.PendingNonce = nonce;
        local.PendingTarget = targetId;
        local.PendingSince = Time.realtimeSinceStartup;
        if (AmongUsClient.Instance.AmHost) Execute(local.Actor, targetId, nonce);
        else Send(Kind.Use, targetId, nonce, 0, AmongUsClient.Instance.HostId);
    }

    private static void Execute(PlayerControl actor, byte targetId, uint nonce)
    {
        if (!hostSlots.TryGetValue(actor.PlayerId, out var slot) || !Bound(slot, actor)) return;
        bool valid = slot.Ready && VotingPhase() && Alive(actor);
        if (!replay.Accept(gameId, meeting.NetId, actor.OwnerId, nonce, valid))
        {
            Reply(actor, targetId, nonce, Result.Rejected, slot.Ability.CanUseMeetingAbility(actor));
            return;
        }
        bool used = TryUse(actor, Utils.GetPlayerById(targetId));
        Reply(actor, targetId, nonce, used ? Result.Accepted : Result.Rejected, Alive(actor) && slot.Ability.CanUseMeetingAbility(actor));
    }

    private static void Reply(PlayerControl actor, byte target, uint nonce, Result result, bool available)
    {
        byte flags = (byte)((byte)result | (available ? AvailableFlag : 0));
        if (actor.AmOwner) ReceiveResult(target, nonce, flags);
        else Send(Kind.Result, target, nonce, flags, actor.OwnerId);
    }

    private static void ReceiveResult(byte target, uint nonce, byte flags)
    {
        if (!LocalBound() || !Context() || !Alive(local.Actor)) return;
        var result = (Result)(flags & ~AvailableFlag);
        if (result == Result.Ready)
        {
            if (target != 255 || nonce != 0 || local.Ready) return;
            local.Ready = true;
            CreateButtons();
        }
        else if (result is Result.Accepted or Result.Rejected)
        {
            if (nonce == 0 || nonce != local.PendingNonce || target != local.PendingTarget) return;
            local.PendingNonce = 0;
            if (result == Result.Rejected) local.Actor.ShowInfoMessage(true, GetString("MeetingAbilityRejected"));
        }
        else return;
        local.Available = (flags & AvailableFlag) != 0;
    }

    private static void Send(Kind kind, byte target, uint nonce, byte result, int receiver) =>
        CustomRpcTransport.Send(CustomRPC.MeetingAbilityRequest, writer =>
        {
            writer.Write(Protocol); writer.Write((byte)kind); writer.Write(gameId); writer.Write(meeting.NetId);
            writer.Write(target); writer.Write(nonce); writer.Write(result);
        }, receiver);

    internal static void Tick()
    {
        if (meeting && (!Context() || !CompatibleHost())) { EndMeeting(meeting); return; }
        if (local == null) return;
        bool alive = LocalBound() && Alive(local.Actor);
        foreach (var button in local.Buttons)
        {
            if (!button) continue;
            var area = button.transform.parent.GetComponent<PlayerVoteArea>();
            button.SetActive(alive && local.Ready && local.Available && local.PendingNonce == 0 && VotingPhase() &&
                area && !area.AmDead);
        }
        if (!alive) { local.Available = false; local.PendingNonce = 0; return; }
        if (!local.Ready && !AmongUsClient.Instance.AmHost && Context() &&
            Time.realtimeSinceStartup - local.ReadySince < 8 && Time.realtimeSinceStartup - local.LastReady >= 1)
        {
            local.LastReady = Time.realtimeSinceStartup;
            Send(Kind.Ready, 255, 0, 0, AmongUsClient.Instance.HostId);
        }
        if (local.PendingNonce != 0 && Time.realtimeSinceStartup - local.PendingSince >= 8)
        {
            local.PendingNonce = 0;
            local.Available = false;
        }
    }

    internal static void EndMeeting(MeetingHud instance)
    {
        if (instance == null || meeting == null || instance.Pointer != meeting.Pointer) return;
        if (AmongUsClient.Instance && AmongUsClient.Instance.AmHost) VanillaMeetingRoles.RestoreAll();
        if (local != null)
            foreach (var button in local.Buttons) if (button) Object.Destroy(button);
        local = null;
        hostSlots.Clear();
        replay.Clear();
        meeting = null;
    }
}
