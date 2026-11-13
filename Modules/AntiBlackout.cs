using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using System;
using TOHE.Modules;
using TOHE.Roles.Core;
using TOHE.Roles.Neutral;
using UnityEngine;

namespace TOHE;

public static class AntiBlackout
{
    // A three-player meeting must hide a real exile from vanilla clients so
    // their end-of-game check still sees one impostor and two crewmates.
    public static bool BlackOutIsActive { get; private set; }
    public static bool ProtectingVanilla => sentMask.Count > 0;

    //this is simply just called in less places, because antiblackout with role-basis changing is OP
    public static int ExilePlayerId = -1;
    public static bool SkipTasks = false;

    public static bool IsCached { get; private set; }
    private readonly static LogHandler logger = Logger.Handler("AntiBlackout");
    private static readonly HashSet<byte> bufferedPlayers = [];
    private static readonly HashSet<byte> hiddenRecipients = [];
    private static readonly Dictionary<(byte seer, byte target), RoleTypes> sentMask = [];
    private static readonly Dictionary<(byte seer, byte target), RoleTypes> deferredRoles = [];
    private static readonly Dictionary<byte, float> airshipSpawnReadyAt = [];
    private static HashSet<byte> airshipPendingPlayers;
    private static OfficialSessionContext meetingContext;
    private static uint meetingGeneration;
    private static bool votingResultPending;
    private static bool publishing;
    private static bool warnedUnavailable;

    public static bool IsBuffered(byte playerId) => bufferedPlayers.Contains(playerId);
    internal static bool TryGetSelfRoleMask(byte playerId, out RoleTypes role) => sentMask.TryGetValue((playerId, playerId), out role);
    internal static bool AirshipRecoveryInProgress => airshipPendingPlayers != null;
    private readonly record struct PlayerView(byte Id, RoleTypes Role, bool Impostor, bool Alive,
        int Margin, bool CanProtect, bool Dead, bool UncertainRole, int SnapshotCost);

    private static List<(PlayerView Player, bool Impostor)> FindProtection(List<PlayerView> view, byte seerId)
    {
        int startingMargin = view.Sum(player => player.Margin);
        if (startingMargin > 0 && view.Any(player => player.Alive && player.Impostor)) return [];

        // Keep one guaranteed impostor. Everyone else can only improve crew minus
        // impostors by becoming a living crewmate. Keeping a pending crew death
        // deferred costs no correction packet; the other changes cost one child.
        var crew = view.Where(player => player.CanProtect && player.Margin < 1)
            .OrderBy(player => player.Dead || player.Impostor || player.UncertainRole)
            .ThenBy(player => player.Margin)
            .ThenBy(player => player.Dead && !player.Impostor && !player.UncertainRole ? player.SnapshotCost : 0)
            .ThenBy(player => player.Id == seerId).ToArray();
        List<(PlayerView Player, bool Impostor)> best = null;
        (int Sends, int Snapshots, int Changes, bool Self) bestCost = (int.MaxValue, 0, 0, false);
        foreach (var impostor in view.Where(player => player.CanProtect))
        {
            int margin = startingMargin;
            int sends = 0, snapshots = 0;
            List<(PlayerView Player, bool Impostor)> changes = [];
            if (!impostor.Alive || !impostor.Impostor)
            {
                changes.Add((impostor, true));
                margin -= 1 + impostor.Margin;
                if (impostor.Dead || !impostor.Impostor || impostor.UncertainRole) sends++;
                if (impostor.Dead && impostor.Impostor && !impostor.UncertainRole) snapshots += impostor.SnapshotCost;
            }

            foreach (var player in crew)
            {
                if (margin > 0) break;
                if (player.Id == impostor.Id) continue;
                changes.Add((player, false));
                margin += 1 - player.Margin;
                if (player.Dead || player.Impostor || player.UncertainRole) sends++;
                if (player.Dead && !player.Impostor && !player.UncertainRole) snapshots += player.SnapshotCost;
            }
            var cost = (sends, snapshots, changes.Count, changes.Any(change => change.Player.Id == seerId));
            if (margin <= 0 || cost.CompareTo(bestCost) >= 0) continue;
            best = changes;
            bestCost = cost;
        }
        return best;
    }

    public static void PrepareVotingResult(NetworkedPlayerInfo exiled)
    {
        meetingGeneration++;
        meetingContext = OfficialSessionContext.Capture();
        votingResultPending = true;
        airshipSpawnReadyAt.Clear();
        warnedUnavailable = false;
        ExilePlayerId = exiled != null ? exiled.PlayerId : -1;
        var players = Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected && player.GetClientId() >= 0).ToArray();
        hiddenRecipients.Clear();
        // A visible exile leaves at most two connected players for the native check.
        if (exiled != null && players.Length == 3 && players.Any(player => player.PlayerId == ExilePlayerId))
            hiddenRecipients.UnionWith(players.Where(player => !player.IsModded()).Select(player => player.PlayerId));
        BlackOutIsActive = hiddenRecipients.Count > 0;
    }

    internal static bool TryBeginAirshipRecovery(NetworkedPlayerInfo exiled)
    {
        if (GameStates.IsEnded || AirshipRecoveryInProgress) return false;
        // Forced meeting closes can reach the picker without VotingComplete.
        if (!votingResultPending) PrepareVotingResult(exiled);
        if (!meetingContext.IsCurrent()) return false;
        var players = Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected && player.GetClientId() >= 0).ToArray();
        airshipPendingPlayers = players.Select(player => player.PlayerId).ToHashSet();
        foreach (var player in players)
            if (!player.AmOwner && Main.PlayerStates[player.PlayerId].HasSpawned) OnAirshipSpawn(player);
        return true;
    }

    internal static void OnAirshipSpawn(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || !votingResultPending || !meetingContext.IsCurrent()) return;
        // Remote SnapTo precedes the picker closing and three fixed updates.
        airshipSpawnReadyAt.TryAdd(player.PlayerId, Time.time + (player.AmOwner ? 0f : 0.25f));
    }

    public static void SetIsDead()
    {
        if (!votingResultPending || IsCached || GameStates.IsEnded) return;
        logger.Info("Prepare meeting protection");
        IsCached = true;
        SkipTasks = true;
        RefreshProtection();
    }

    private static void RefreshProtection(int leavingClientId = -1)
    {
        if (GameStates.IsEnded || !meetingContext.IsCurrent()) return;
        var players = Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected &&
            player.GetClientId() >= 0 && player.GetClientId() != leavingClientId).OrderBy(player => player.PlayerId).ToArray();
        var byId = players.ToDictionary(player => player.PlayerId);
        var roleManager = DestroyableSingleton<RoleManager>.Instance;
        var changes = new Dictionary<(byte seer, byte target), RoleTypes>();
        var snapshots = new HashSet<(byte seer, byte target)>();
        bool unavailable = false;
        bool compatible = OfficialAnticheatPolicy.Enabled;
        CustomRpcSender roleSender = null;
        publishing = true;
        try
        {
            foreach (var seer in players)
            {
                byte seerId = seer.PlayerId;
                if (seer.IsModded() || airshipSpawnReadyAt.ContainsKey(seerId)) continue;

                var view = new List<PlayerView>(players.Length);
                foreach (var target in players)
                {
                    byte id = target.PlayerId;
                    bool visibleExile = id == ExilePlayerId && !hiddenRecipients.Contains(seerId);
                    bool masked = sentMask.TryGetValue((seerId, id), out var role);
                    bool dead = target.Data.IsDead;
                    bool pendingDeath = id == ExilePlayerId || Main.AfterMeetingDeathPlayers.ContainsKey(id);
                    bool known = RpcSetRoleReplacer.RoleMap.TryGetValue((seerId, id), out var actual);
                    if (!masked && known) role = dead ? GhostRoleView.ForRecipient(target, seer) : actual.roleType;
                    var native = masked || known ? roleManager.GetRole(role) : null;
                    bool ghostPending = dead && !masked && GhostRoleView.IsPublicationPending(id);
                    if (native == null)
                    {
                        // An unknown living role can count as an impostor.
                        view.Add(new(id, role, false, false, !visibleExile && (!dead || ghostPending) ? -1 : 0, false, dead, false, 0));
                        continue;
                    }

                    // The queued ghost role may not have arrived yet. A snapshot
                    // alone cannot change the role instance the client counts.
                    bool wasImpostor = ghostPending && roleManager.GetRole(actual.roleType)?.IsImpostor != false;
                    bool uncertainRole = ghostPending && wasImpostor != native.IsImpostor;
                    bool alive = !visibleExile && (masked || !dead && !pendingDeath);
                    bool mayCountAsImpostor = !visibleExile && (native.IsImpostor && (masked || !dead) || wasImpostor);
                    int margin = mayCountAsImpostor ? -1 : alive ? 1 : 0;
                    // Prefer a shared snapshot when every recipient can use it.
                    int snapshotCost = snapshots.Any(key => key.target == id) ? 1 : 2;
                    view.Add(new(id, role, native.IsImpostor, alive, margin, !visibleExile, dead && !masked, uncertainRole, snapshotCost));
                }

                var plan = FindProtection(view, seerId);
                if (plan == null)
                {
                    unavailable = true;
                    continue;
                }
                foreach (var (entry, impostor) in plan)
                {
                    var key = (seerId, entry.Id);
                    if (entry.Impostor == impostor && !entry.UncertainRole)
                    {
                        changes[key] = entry.Role;
                        if (entry.Dead) snapshots.Add(key);
                        continue;
                    }

                    // A living SetRole also clears IsDead through the native Revive path.
                    var role = impostor ? RoleTypes.Impostor : RoleTypes.Crewmate;
                    changes[key] = role;
                    var target = byId[entry.Id];
                    if (compatible)
                        (roleSender ??= CustomRpcSender.Create("AntiBlackout protection")).RpcSetRole(target, role, seer.GetClientId());
                    else target.RpcSetRoleDesync(role, seer.GetClientId());
                }
            }
            if (roleSender is { RecordCount: > 0 }) roleSender.SendTargetedBatchWithSummary("SetRole");
            if (snapshots.Count > 0)
            {
                int packets = SendPlayerData(snapshots.Select(key => key.target).Distinct().Select(id => byId[id].Data), snapshots, leavingClientId);
                logger.Info($"Meeting protection: {snapshots.Count} PlayerInfo views in {packets} packets");
            }
            foreach (var (key, role) in changes) sentMask[key] = role;
        }
        finally
        {
            roleSender?.Dispose();
            publishing = false;
        }

        if (unavailable && !warnedUnavailable)
        {
            warnedUnavailable = true;
            ErrorText.Instance?.ShowAntiBlackoutWarning();
            foreach (var player in players.Where(player => player.IsNonHostModdedClient()))
                CustomRpcTransport.SendPayload(CustomRPC.AntiBlackoutUnavailable, _ => { }, player.GetClientId());
            logger.Warn("This meeting cannot construct a safe native roster for every vanilla recipient");
        }

        int mode = Options.AntiBlackoutBufferMode.GetInt();
        bool bufferChanged = bufferedPlayers.RemoveWhere(id => !byId.ContainsKey(id)) > 0;
        foreach (var player in players)
        {
            byte id = player.PlayerId;
            if (airshipPendingPlayers != null && !airshipPendingPlayers.Contains(id)) continue;
            if ((mode == 0 || mode == 1 && !player.IsModded() || mode == 2 && sentMask.Keys.Any(key => key.seer == id))
                && bufferedPlayers.Add(id))
            {
                player.MarkDirtySettings();
                bufferChanged = true;
            }
        }
        if (bufferChanged)
        {
            SyncBuffer();
            GameOptionsSender.SendAllGameOptions();
        }
    }

    private static int SendPlayerData(IEnumerable<NetworkedPlayerInfo> infos,
        HashSet<(byte seer, byte target)> corrections = null, int leavingClientId = -1)
    {
        var recipients = Main.AllPlayerControls.Where(player => !player.IsHost() && player.Data != null &&
            !player.Data.Disconnected && player.GetClientId() >= 0 && player.GetClientId() != leavingClientId).ToArray();
        List<(int ClientId, NetworkedPlayerInfo Info, bool KeepAlive)> snapshots = [];
        List<(byte, byte)> overwrittenNames = [];
        foreach (var info in infos)
        {
            if (info == null) continue;
            var targets = corrections == null ? recipients
                : recipients.Where(seer => corrections.Contains((seer.PlayerId, info.PlayerId))).ToArray();
            if (targets.Length == 0) continue;
            var masked = targets.Where(seer => corrections != null || sentMask.ContainsKey((seer.PlayerId, info.PlayerId))).ToArray();
            if (targets.Length == recipients.Length && (masked.Length == 0 || masked.Length == targets.Length))
                snapshots.Add((-1, info, masked.Length > 0));
            else
                foreach (var seer in targets) snapshots.Add((seer.GetClientId(), info, masked.Contains(seer)));

            foreach (var seer in targets)
            {
                var key = (info.PlayerId, seer.PlayerId);
                if (Main.LastNotifyNames.TryGetValue(key, out var sentName) && sentName != info.PlayerName)
                    overwrittenNames.Add(key);
            }
        }

        var batch = new OfficialPacketBuilder.Batch();
        foreach (var (clientId, info, keepAlive) in snapshots.OrderBy(snapshot => snapshot.ClientId))
        {
            batch.AddChild(clientId, writer =>
            {
                writer.StartMessage(1);
                writer.WritePacked(info.NetId);
                bool dead = info.IsDead;
                try
                {
                    if (keepAlive) info.IsDead = false;
                    info.Serialize(writer, true);
                }
                finally { info.IsDead = dead; }
                writer.EndMessage();
            });
        }
        int packets = batch.Send();
        // Player info also overwrites the client's default outfit name.
        foreach (var key in overwrittenNames) Main.LastNotifyNames.Remove(key);
        return packets;
    }

    public static void RestoreIsDead() => IsCached = false;

    // Hold actual role publications during business callbacks, and preserve an
    // existing mask until its single recovery transaction. RoleMap remains real.
    internal static bool DeferRole(PlayerControl target, RoleTypes role, int clientId)
    {
        if (publishing || !votingResultPending || !ProtectingVanilla) return false;
        var seer = Main.AllPlayerControls.FirstOrDefault(player => player.GetClientId() == clientId);
        if (seer == null || !sentMask.ContainsKey((seer.PlayerId, target.PlayerId))) return false;
        deferredRoles[(seer.PlayerId, target.PlayerId)] = role;
        return true;
    }

    internal static void OnDisconnect(ClientData client, byte playerId)
    {
        if (!AmongUsClient.Instance.AmHost || !votingResultPending || GameStates.IsEnded || !meetingContext.IsCurrent()) return;
        if (playerId == byte.MaxValue)
            playerId = GameData.Instance.GetPlayerByClient(client)?.PlayerId ?? byte.MaxValue;
        if (playerId == byte.MaxValue) return;
        foreach (var key in sentMask.Keys.Where(key => key.seer == playerId || key.target == playerId).ToArray())
            sentMask.Remove(key);
        foreach (var key in deferredRoles.Keys.Where(key => key.seer == playerId || key.target == playerId).ToArray())
            deferredRoles.Remove(key);
        airshipPendingPlayers?.Remove(playerId);
        airshipSpawnReadyAt.Remove(playerId);
        RefreshProtection(client.Id);
        Utils.NotifyRoles();
    }

    private static void SyncBuffer()
    {
        CustomRpcTransport.SendPayload(CustomRPC.AntiBlackoutBuffer, writer =>
        {
            writer.Write((byte)bufferedPlayers.Count);
            foreach (var playerId in bufferedPlayers) writer.Write(playerId);
        });
    }

    internal static void ReceiveBuffer(MessageReader reader)
    {
        bufferedPlayers.Clear();
        int count = reader.ReadByte();
        for (int i = 0; i < count; i++) bufferedPlayers.Add(reader.ReadByte());
    }

    public static void RpcVotingCompleteByRecipient(this MeetingHud __instance, MeetingHud.VoterState[] states, NetworkedPlayerInfo exiled, bool tie,
        bool wasOverruled = false, ushort overruleNonce = 0, byte? preserveJudgeUseFor = null)
    {
        wasOverruled &= exiled != null;
        if (!wasOverruled) overruleNonce = 0;
        if (AmongUsClient.Instance.AmClient)
        {
            __instance.VotingComplete(states, exiled, tie, wasOverruled, overruleNonce);
        }

        using var sender = CustomRpcSender.Create("RpcVotingComplete by recipient", SendOption.Reliable);
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.IsHost()) continue;
            if (pc.IsNonHostModdedClient() || !hiddenRecipients.Contains(pc.PlayerId)) // Only risky recipients hide this exile
            {
                sender.AutoStartRpc(__instance.NetId, (byte)RpcCalls.VotingComplete, pc.GetClientId());
                {
                    sender.WritePacked(states.Length);
                    foreach (MeetingHud.VoterState voterState in states)
                    {
                        sender.WriteMessageType(voterState.VoterId);
                        sender.Write(voterState.VotedForId);
                        sender.WriteEndMessage();
                    }
                    sender.Write(exiled != null ? exiled.PlayerId : byte.MaxValue);
                    sender.Write(tie);
                    // A vanilla Judge consumes its only native use when it
                    // receives its own overrule. The host still enforces the
                    // configured count and sends the actual exile here.
                    bool showOverrule = wasOverruled && pc.PlayerId != preserveJudgeUseFor;
                    sender.Write(showOverrule);
                    sender.Write(showOverrule ? overruleNonce : (ushort)0);
                    sender.EndRpc();
                }
            }
            else //For vanilla client show a tie
            {
                sender.AutoStartRpc(__instance.NetId, (byte)RpcCalls.VotingComplete, pc.GetClientId());
                {
                    sender.WritePacked(states.Length);
                    foreach (MeetingHud.VoterState voterState in states)
                    {
                        sender.WriteMessageType(voterState.VoterId);
                        sender.Write(voterState.VotedForId);
                        sender.WriteEndMessage();
                    }
                    sender.Write(byte.MaxValue);
                    sender.Write(true);
                    // A hidden exile must stay a plain tie: native VotingComplete dereferences it for an overrule.
                    sender.Write(false);
                    sender.Write((ushort)0);
                    sender.EndRpc();
                }
            }
        }
        sender.SendMessage();
    }
    public static void AfterMeetingTasks()
    {
        var timeNotify = 0f;

        if (CheckForEndVotingPatch.TempExileMsg != null && BlackOutIsActive)
        {
            timeNotify = 4f;
            foreach (var pc in Main.AllPlayerControls.Where(p => hiddenRecipients.Contains(p.PlayerId)).ToArray())
            {
                pc.Notify(CheckForEndVotingPatch.TempExileMsg, time: timeNotify);
            }
        }

        var context = meetingContext;
        var generation = meetingGeneration;
        _ = new LateTask(() =>
        {
            if (generation != meetingGeneration || !context.IsCurrent() || GameStates.IsEnded) return;
            foreach (var pc in Main.AllAlivePlayerControls) pc.GetRoleClass()?.NotifyAfterMeeting();
        }, timeNotify + 0.2f, "Notify AfterMeetingTasks");
    }

    internal static void CompleteAfterMeeting(Action settle)
    {
        if (!votingResultPending || GameStates.IsEnded) return;
        // CheckExile/OnPlayerExiled can add deaths after voting. Protect those
        // views before the delayed business callbacks publish any of the deaths.
        RefreshProtection();
        Utils.NotifyRoles();
        var generation = meetingGeneration;
        var context = meetingContext;
        Main.Instance.StartCoroutine(Complete());

        System.Collections.IEnumerator Complete()
        {
            // On Airship this starts after exile is applied, as the host enters the picker.
            yield return new UnityEngine.WaitForSeconds(1.2f);
            if (!Current()) yield break;
            settle();
            if (!Current()) yield break;
            RefreshProtection();
            Utils.NotifyRoles();
            // Fixed empirical allowance, not a receiver WrapUp acknowledgement.
            // The first 1.2 seconds above already belongs to this two-second hold.
            if (bufferedPlayers.Count > 0 || ProtectingVanilla && airshipPendingPlayers == null)
                yield return new UnityEngine.WaitForSeconds(0.8f);
            var barrier = CustomRpcTransport.CaptureBarrier();
            while (!barrier.IsComplete || GhostRoleView.HasPendingPublications)
            {
                if (!Current() || barrier.IsCancelled) yield break;
                yield return null;
            }
            if (!Current()) yield break;
            if (airshipPendingPlayers != null)
            {
                while (airshipPendingPlayers.Count > 0)
                {
                    if (!GhostRoleView.HasPendingPublications)
                    {
                        var connected = Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected
                            && player.GetClientId() >= 0 && airshipPendingPlayers.Contains(player.PlayerId)).ToArray();
                        airshipPendingPlayers.IntersectWith(connected.Select(player => player.PlayerId));
                        float now = Time.time;
                        // Modded GMs skip the picker and do not send its spawn SnapTo.
                        var ready = connected.Where(player => player.IsModded() && player.Is(CustomRoles.GM)
                            || airshipSpawnReadyAt.TryGetValue(player.PlayerId, out float readyAt)
                            && now >= readyAt).ToArray();
                        if (ready.Length > 0) RestoreAirshipPlayers(ready);
                    }
                    if (!Current()) yield break;
                    if (airshipPendingPlayers.Count == 0) break;
                    yield return null;
                    if (!Current()) yield break;
                }
                bufferedPlayers.Clear();
                sentMask.Clear();
                deferredRoles.Clear();
                ResetAfterMeeting();
                yield break;
            }

            var releasedPlayers = bufferedPlayers.ToHashSet();
            bufferedPlayers.Clear();
            SetRealPlayerRoles(releasedPlayers);
            foreach (var seer in Main.AllPlayerControls)
                if (seer.IsAlive() || seer.HasGhostRole()) seer.RpcResetAbilityCooldown();
            if (releasedPlayers.Count > 0) SyncBuffer();
            ResetAfterMeeting();
            Utils.SyncAllSettings();
            Utils.NotifyRoles();
        }

        bool Current() => generation == meetingGeneration && context.IsCurrent()
            && AmongUsClient.Instance.AmHost && !GameStates.IsEnded && votingResultPending;
    }

    private static void RestoreAirshipPlayers(PlayerControl[] recipients)
    {
        var restoredIds = recipients.Select(player => player.PlayerId).ToHashSet();
        var releasedPlayers = bufferedPlayers.Where(restoredIds.Contains).ToHashSet();
        bufferedPlayers.ExceptWith(releasedPlayers);
        SetRealPlayerRoles(releasedPlayers, recipients);
        foreach (var player in recipients)
        {
            if (player.IsAlive() || player.HasGhostRole()) player.RpcResetAbilityCooldown();
            player.MarkDirtySettings();
        }

        airshipPendingPlayers.ExceptWith(restoredIds);
        if (releasedPlayers.Count > 0) SyncBuffer();
        GameOptionsSender.SendAllGameOptions();
        foreach (var player in recipients) Utils.NotifyRoles(SpecifySeer: player);
    }

    public static void SetRealPlayerRoles(HashSet<byte> releasedPlayers, PlayerControl[] recipients = null)
    {
        if (GameStates.IsEnded) return;
        var players = recipients ?? Main.AllPlayerControls;
        var vanillaPlayers = players.Where(player => !player.IsModded() && player.Data != null && !player.Data.Disconnected).ToArray();
        Dictionary<byte, (RoleTypes SelfRole, bool RestoreSelf, float? Cooldown)> restorations = [];

        foreach (var seer in vanillaPlayers)
        {
            var key = (seer.PlayerId, seer.PlayerId);
            bool restoreSelf = sentMask.ContainsKey(key) || deferredRoles.ContainsKey(key);
            if (!restoreSelf && !releasedPlayers.Contains(seer.PlayerId)) continue;
            RoleTypes selfRole = default;
            if (restoreSelf)
            {
                if (RpcSetRoleReplacer.RoleMap.TryGetValue(key, out var selfView))
                {
                    selfRole = seer.Data.IsDead ? GhostRoleView.ForRecipient(seer, seer)
                        : deferredRoles.GetValueOrDefault(key, selfView.roleType);
                    if (!seer.Data.IsDead && sentMask.TryGetValue(key, out var temporary) && temporary == selfRole)
                        restoreSelf = false;
                }
                else restoreSelf = false;
            }
            if (restoreSelf || releasedPlayers.Contains(seer.PlayerId))
                restorations[seer.PlayerId] = (selfRole, restoreSelf, null);
        }

        ResetAllCooldown(players, releasedPlayers, restorations);
        publishing = true;
        try
        {
            using var roleSender = OfficialAnticheatPolicy.Enabled ? CustomRpcSender.Create("AntiBlackout role restoration") : null;
            foreach (var seer in vanillaPlayers)
            {
                foreach (var target in Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected && player != seer))
                {
                    var key = (seer.PlayerId, target.PlayerId);
                    if (!sentMask.ContainsKey(key) && !deferredRoles.ContainsKey(key)) continue;
                    if (!RpcSetRoleReplacer.RoleMap.TryGetValue(key, out var view)) continue;
                    var role = target.Data.IsDead ? GhostRoleView.ForRecipient(target, seer)
                        : deferredRoles.GetValueOrDefault(key, view.roleType);
                    if (!target.Data.IsDead && sentMask.TryGetValue(key, out var temporary) && temporary == role) continue;
                    if (roleSender == null) target.RpcSetRoleDesync(role, seer.GetClientId());
                    else roleSender.RpcSetRole(target, role, seer.GetClientId());
                }
            }
            if (roleSender is { RecordCount: > 0 }) roleSender.SendTargetedBatchWithSummary("SetRole");

            // Publish other role views before each recipient's indivisible owner recovery packet.
            foreach (var seer in vanillaPlayers)
            {
                if (!restorations.TryGetValue(seer.PlayerId, out var restoration)) continue;
                bool sendOptions = restoration.Cooldown.HasValue || releasedPlayers.Contains(seer.PlayerId);
                if (!sendOptions)
                {
                    seer.RpcSetRoleDesync(restoration.SelfRole, seer.GetClientId());
                    continue;
                }

                var factory = GameOptionsManager.Instance.gameOptionsFactory;
                var options = new PlayerGameOptionsSender(seer).BuildGameOptions();
                var normalOptions = factory.ToBytes(options, AprilFoolsMode.IsAprilFoolsModeToggledOn);
                // Reliable datagrams can arrive out of order. Keep the temporary
                // cooldown, role, timer and normal settings in one recipient packet;
                // a late release-time settings packet must not reapply the slow view.
                using var owner = new OfficialPacketBuilder.BoundedWriter(SendOption.Reliable);
                var writer = owner.Writer;
                writer.StartMessage(6);
                writer.Write(AmongUsClient.Instance.GameId);
                writer.WritePacked(seer.GetClientId());
                if (restoration.Cooldown is float cooldown)
                {
                    var temporaryOptions = factory.FromBytes(normalOptions);
                    temporaryOptions.SetFloat(FloatOptionNames.KillCooldown, cooldown * 2f);
                    WriteOptions(factory.ToBytes(temporaryOptions, AprilFoolsMode.IsAprilFoolsModeToggledOn));
                }
                if (restoration.RestoreSelf)
                {
                    writer.StartMessage(2);
                    writer.WritePacked(seer.NetId);
                    writer.Write((byte)RpcCalls.SetRole);
                    writer.Write((ushort)restoration.SelfRole);
                    writer.Write(true);
                    writer.EndMessage();
                }
                if (restoration.Cooldown.HasValue)
                {
                    writer.StartMessage(2);
                    writer.WritePacked(seer.NetId);
                    writer.Write((byte)RpcCalls.MurderPlayer);
                    writer.WriteNetObject(seer);
                    writer.Write((int)MurderResultFlags.FailedProtected);
                    writer.EndMessage();
                }
                WriteOptions(normalOptions);
                writer.EndMessage();
                OfficialNetworkSend.Send(AmongUsClient.Instance, writer);

                void WriteOptions(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes)
                {
                    byte index = 0;
                    foreach (var component in GameManager.Instance.LogicComponents)
                    {
                        if (component.TryCast<LogicOptions>() != null)
                        {
                            writer.StartMessage(1);
                            writer.WritePacked(GameManager.Instance.NetId);
                            writer.StartMessage(index);
                            writer.WriteBytesAndSize(bytes);
                            writer.EndMessage();
                            writer.EndMessage();
                        }
                        index++;
                    }
                }
            }
        }
        finally { publishing = false; }
        if (recipients == null)
        {
            sentMask.Clear();
            deferredRoles.Clear();
        }
        else
        {
            var recipientIds = vanillaPlayers.Select(player => player.PlayerId).ToHashSet();
            foreach (var key in sentMask.Keys.Where(key => recipientIds.Contains(key.seer)).ToArray())
                sentMask.Remove(key);
            foreach (var key in deferredRoles.Keys.Where(key => recipientIds.Contains(key.seer)).ToArray())
                deferredRoles.Remove(key);
        }
    }
    private static void ResetAllCooldown(PlayerControl[] players, HashSet<byte> releasedPlayers,
        Dictionary<byte, (RoleTypes SelfRole, bool RestoreSelf, float? Cooldown)> restorations)
    {
        foreach (var seer in players)
        {
            if (seer.IsAlive())
            {
                seer.ResetKillCooldown();
                // Reuse this reset for recovery; role resets can also change speed.
                float kcd = Main.AllPlayerKillCooldown[seer.PlayerId];
                bool batch = restorations.TryGetValue(seer.PlayerId, out var restoration);
                if (batch && restoration.RestoreSelf
                    && DestroyableSingleton<RoleManager>.Instance.GetRole(restoration.SelfRole).CanUseKillButton)
                    restoration.Cooldown = kcd;

                if (kcd >= 2f)
                {
                    float cooldown = releasedPlayers.Contains(seer.PlayerId) ? kcd : UnityEngine.Mathf.Max(0.01f, kcd - 2f);
                    if (batch && seer.GetRoleClass() is not Glitch
                        && seer.HasImpKillButton(considerVanillaShift: true)
                        && (!seer.HasImpKillButton(false) || seer.CanUseKillButton()))
                    {
                        restoration.Cooldown = cooldown;
                    }
                    seer.SetKillCooldown(cooldown, synchronize: !batch);
                }
                if (batch) restorations[seer.PlayerId] = restoration;
            }
        }
    }
    public static void ResetAfterMeeting()
    {
        SkipTasks = false;
        ExilePlayerId = -1;
        BlackOutIsActive = false;
        votingResultPending = false;
        airshipPendingPlayers = null;
        airshipSpawnReadyAt.Clear();
        warnedUnavailable = false;
    }
    public static void Reset()
    {
        logger.Info("==Reset==");
        meetingGeneration++;
        bufferedPlayers.Clear();
        hiddenRecipients.Clear();
        deferredRoles.Clear();
        sentMask.Clear();
        airshipPendingPlayers = null;
        airshipSpawnReadyAt.Clear();
        publishing = false;
        GhostRoleView.ResetPending();
        IsCached = false;
        ShowExiledInfo = false;
        StoreExiledMessage = "";
        ExilePlayerId = -1;
        SkipTasks = false;
        BlackOutIsActive = false;
        votingResultPending = false;
        warnedUnavailable = false;
    }

    public static bool ShowExiledInfo = false;
    public static string StoreExiledMessage = "";
    [HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.Serialize))]
    private static class PlayerInfoPatch
    {
        private static bool Prefix(NetworkedPlayerInfo __instance, [HarmonyArgument(1)] bool initialState, ref bool __result)
        {
            if (initialState || !votingResultPending || !__instance.IsDead || __instance.Disconnected
                || !AmongUsClient.Instance.AmHost || GameStates.IsEnded) return true;

            // An early death flag would make SetRole skip the native Die transition.
            if (!GhostRoleView.IsPublicationPending(__instance.PlayerId))
            {
                if (!sentMask.Keys.Any(key => key.target == __instance.PlayerId)) return true;
                SendPlayerData([__instance]);
                __instance.ClearOrDecrementDirt();
            }
            __result = false;
            return false;
        }
    }
}
