using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using System;
using System.Runtime.CompilerServices;
using TOHE.Modules;
using TOHE.Roles.Core;
using TOHE.Roles.Neutral;

namespace TOHE;

public static class AntiBlackout
{
    // A three-player meeting must hide a real exile from vanilla clients so
    // their end-of-game check still sees one impostor and two crewmates.
    public static bool BlackOutIsActive { get; private set; }
    public static bool ProtectingVanilla { get; private set; }

    //this is simply just called in less places, because antiblackout with role-basis changing is OP
    public static int ExilePlayerId = -1;
    public static bool SkipTasks = false;

    public static bool IsCached { get; private set; }
    private readonly static LogHandler logger = Logger.Handler("AntiBlackout");
    private static readonly HashSet<byte> protectedRecipients = [];
    private static readonly HashSet<byte> bufferedPlayers = [];
    private static readonly HashSet<byte> hiddenRecipients = [];
    private static readonly Dictionary<(byte seer, byte target), RoleTypes> sentMask = [];
    private static readonly Dictionary<(byte seer, byte target), RoleTypes> deferredRoles = [];
    private static OfficialSessionContext meetingContext;
    private static uint meetingGeneration;
    private static bool votingResultPending;
    private static bool publishing;
    private static bool serializing;
    private static bool warnedUnavailable;

    public static bool IsBuffered(byte playerId) => bufferedPlayers.Contains(playerId);
    public static bool IsProtectedRecipient(byte playerId) => protectedRecipients.Contains(playerId);
    internal static bool WaitingForGhostViews => votingResultPending && GhostRoleView.HasPendingPublications;
    internal static bool RedirectingPlayerData => (ProtectingVanilla || WaitingForGhostViews) && !serializing;

    // Evaluate the real post-meeting matrix, never the temporary mask. The role
    // prototype supplies native IsImpostor; custom kill-button access is unrelated.
    private static HashSet<byte> FindRiskyRecipients(PlayerControl[] players)
    {
        HashSet<byte> risky = [];
        foreach (var seer in players.Where(player => !player.IsModded()))
        {
            int impostors = 0, crew = 0;
            bool missing = false;
            foreach (var target in players)
            {
                bool dead = target.Data.IsDead || target.PlayerId == ExilePlayerId
                    || Main.AfterMeetingDeathPlayers.ContainsKey(target.PlayerId);
                if (!RpcSetRoleReplacer.RoleMap.TryGetValue((seer.PlayerId, target.PlayerId), out var view))
                {
                    missing = true;
                    break;
                }
                var roleType = dead ? GhostRoleView.ForRecipient(target, seer) : view.Item1;
                if (deferredRoles.TryGetValue((seer.PlayerId, target.PlayerId), out var deferred))
                    roleType = dead ? GhostRoleView.ForRecipient(target, seer) : deferred;
                var role = DestroyableSingleton<RoleManager>.Instance.GetRole(roleType);
                if (role == null) { missing = true; break; }
                if (!dead)
                {
                    if (role.IsImpostor) impostors++;
                    else crew++;
                }
                else if (roleType == RoleTypes.ImpostorGhost && target.Data.Role != null
                    && target.Data.Role.TryCast<ImpostorGhostRole>() is { WasManuallyPicked: true }
                    && role.IsImpostor)
                    impostors++;
            }
            logger.Info($"Recipient {seer.PlayerId}: real post-meeting I={impostors}, C={crew}, incomplete={missing}");
            if (missing || impostors <= 0 || crew <= impostors) risky.Add(seer.PlayerId);
        }
        return risky;
    }

    public static void PrepareVotingResult(NetworkedPlayerInfo exiled)
    {
        meetingGeneration++;
        meetingContext = OfficialSessionContext.Capture();
        votingResultPending = true;
        warnedUnavailable = false;
        ExilePlayerId = exiled != null ? exiled.PlayerId : -1;
        var players = Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected && player.GetClientId() >= 0).ToArray();
        hiddenRecipients.Clear();
        if (exiled != null && players.Length == 3) hiddenRecipients.UnionWith(FindRiskyRecipients(players));
        BlackOutIsActive = hiddenRecipients.Count > 0;
    }

    public static void SetIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        if (!votingResultPending || IsCached || GameStates.IsEnded) return;
        logger.Info($"Prepare meeting protection from {callerMethodName}");
        IsCached = true;
        SkipTasks = true;
        RefreshProtection();
        if (doSend) SendGameData();
    }

    private static void RefreshProtection()
    {
        var players = Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected && player.GetClientId() >= 0).ToArray();
        var risky = FindRiskyRecipients(players);
        protectedRecipients.IntersectWith(players.Select(player => player.PlayerId));
        // Once a recipient has a mask it must keep it until the restoration transaction.
        protectedRecipients.UnionWith(risky);
        bool canProtect = players.Length >= 3 && (BlackOutIsActive || ExilePlayerId < 0 || players.Length >= 4);
        if (!canProtect && protectedRecipients.Count > 0)
        {
            protectedRecipients.Clear();
            if (!warnedUnavailable)
            {
                warnedUnavailable = true;
                ErrorText.Instance?.ShowAntiBlackoutWarning();
                foreach (var player in players.Where(player => player.IsNonHostModdedClient()))
                    CustomRpcTransport.SendPayload(CustomRPC.AntiBlackoutUnavailable, _ => { }, player.GetClientId());
                logger.Warn("This meeting cannot construct a safe native roster");
            }
        }
        ProtectingVanilla = protectedRecipients.Count > 0;
        int mode = Options.AntiBlackoutBufferMode.GetInt();
        foreach (var player in players)
        {
            if (mode == 0 || mode == 1 && !player.IsModded() || mode == 2 && protectedRecipients.Contains(player.PlayerId))
                bufferedPlayers.Add(player.PlayerId);
        }
        bufferedPlayers.IntersectWith(players.Select(player => player.PlayerId));
        SyncBuffer();
        foreach (var player in players.Where(player => IsBuffered(player.PlayerId))) player.MarkDirtySettings();
        GameOptionsSender.SendAllGameOptions();
        if (ProtectingVanilla)
        {
            var dummyImp = players.First(player => player.PlayerId != ExilePlayerId);
            publishing = true;
            try
            {
                foreach (var seer in players.Where(player => IsProtectedRecipient(player.PlayerId)))
                    foreach (var target in players)
                    {
                        var role = target == dummyImp ? RoleTypes.Impostor : RoleTypes.Crewmate;
                        var key = (seer.PlayerId, target.PlayerId);
                        if (sentMask.TryGetValue(key, out var previous) && previous == role) continue;
                        target.RpcSetRoleDesync(role, seer.GetClientId());
                        sentMask[key] = role;
                    }
            }
            finally { publishing = false; }
        }
        Utils.NotifyRoles(NoCache: true);
    }

    // No authoritative death flag is changed. Only the affected recipient's
    // serialization sees the living mask, including native dirty-player updates.
    public static void SendGameData([CallerMemberName] string callerMethodName = "")
    {
        var batch = new OfficialPacketBuilder.Batch();
        foreach (var info in GameData.Instance.AllPlayers)
            if (info != null) SendPlayerData(info, batch);
        batch.Send();
    }

    internal static void SendPlayerData(NetworkedPlayerInfo info, OfficialPacketBuilder.Batch batch = null)
    {
        if (WaitingForGhostViews) return;
        bool ownsBatch = batch == null;
        batch ??= new OfficialPacketBuilder.Batch();
        foreach (var seer in Main.AllPlayerControls)
        {
            if (seer.IsHost() || seer.Data == null || seer.Data.Disconnected || seer.GetClientId() < 0) continue;
            batch.AddChild(seer.GetClientId(), writer =>
            {
                writer.StartMessage(1);
                writer.WritePacked(info.NetId);
                bool dead = info.IsDead;
                serializing = true;
                try
                {
                    if (IsProtectedRecipient(seer.PlayerId) && !info.Disconnected) info.IsDead = false;
                    info.Serialize(writer, true);
                }
                finally { info.IsDead = dead; serializing = false; }
                writer.EndMessage();
            });
        }
        if (ownsBatch) batch.Send();
    }

    public static void RestoreIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        IsCached = false;
        if (doSend) SendGameData();
    }

    // Hold actual role publications during business callbacks, and preserve an
    // existing mask until its single recovery transaction. RoleMap remains real.
    internal static bool DeferRole(PlayerControl target, RoleTypes role, int clientId)
    {
        if (publishing || !votingResultPending) return false;
        var seer = Main.AllPlayerControls.FirstOrDefault(player => player.GetClientId() == clientId);
        if (seer == null || seer.IsModded() || !IsProtectedRecipient(seer.PlayerId)) return false;
        deferredRoles[(seer.PlayerId, target.PlayerId)] = role;
        return true;
    }

    public static void OnDisconnect(NetworkedPlayerInfo player)
    {
        if (!AmongUsClient.Instance.AmHost || !votingResultPending || !player.Disconnected) return;
        bufferedPlayers.Remove(player.PlayerId);
        protectedRecipients.Remove(player.PlayerId);
        RefreshProtection();
        SendGameData();
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

    public static void AntiBlackRpcVotingComplete(this MeetingHud __instance, MeetingHud.VoterState[] states, NetworkedPlayerInfo exiled, bool tie)
    {
        if (AmongUsClient.Instance.AmClient)
        {
            __instance.VotingComplete(states, exiled, tie, false, 0);
        }

        using var sender = CustomRpcSender.Create("AntiBlack RpcVotingComplete", SendOption.Reliable);
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
                    sender.Write(false); // wasOverruled
                    sender.Write((ushort)0); // overruleNonce
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
        SendGameData();
        var generation = meetingGeneration;
        var context = meetingContext;
        Main.Instance.StartCoroutine(Complete());

        System.Collections.IEnumerator Complete()
        {
            // Both anchors are after native recovery; Airship's anchor is after
            // PrespawnStep, never the WrapUpAndSpawn iterator factory.
            yield return new UnityEngine.WaitForSeconds(1.2f);
            if (!Current()) yield break;
            settle();
            if (!Current()) yield break;
            RefreshProtection();
            SendGameData();
            // Fixed empirical allowance, not a receiver WrapUp acknowledgement.
            // The first 1.2 seconds above already belongs to this two-second hold.
            if (bufferedPlayers.Count > 0) yield return new UnityEngine.WaitForSeconds(0.8f);
            var barrier = CustomRpcTransport.CaptureBarrier();
            while (!barrier.IsComplete || GhostRoleView.HasPendingPublications)
            {
                if (!Current() || barrier.IsCancelled) yield break;
                yield return null;
            }
            if (!Current()) yield break;
            var releasedPlayers = bufferedPlayers.ToHashSet();
            bufferedPlayers.Clear();
            SetRealPlayerRoles(releasedPlayers);
            foreach (var seer in Main.AllPlayerControls)
                if (seer.IsAlive() || seer.HasGhostRole()) seer.RpcResetAbilityCooldown();
            protectedRecipients.Clear();
            sentMask.Clear();
            ProtectingVanilla = false;
            SendGameData();
            SyncBuffer();
            ResetAfterMeeting();
            Utils.SyncAllSettings();
            Utils.NotifyRoles(NoCache: true);
        }

        bool Current() => generation == meetingGeneration && context.IsCurrent()
            && AmongUsClient.Instance.AmHost && !GameStates.IsEnded && votingResultPending;
    }

    public static void SetRealPlayerRoles(HashSet<byte> releasedPlayers, bool sendGameDataAfter = false)
    {
        if (GameStates.IsEnded) return;
        var vanillaPlayers = Main.AllPlayerControls.Where(player => !player.IsModded() && player.Data != null && !player.Data.Disconnected).ToArray();
        Dictionary<byte, (RoleTypes SelfRole, bool RestoreSelf, float? Cooldown)> restorations = [];

        foreach (var seer in vanillaPlayers)
        {
            bool restoreSelf = IsProtectedRecipient(seer.PlayerId) || deferredRoles.ContainsKey((seer.PlayerId, seer.PlayerId));
            if (!restoreSelf && !releasedPlayers.Contains(seer.PlayerId)) continue;
            RoleTypes selfRole = default;
            if (restoreSelf)
            {
                if (RpcSetRoleReplacer.RoleMap.TryGetValue((seer.PlayerId, seer.PlayerId), out var selfView))
                {
                    selfRole = seer.Data.IsDead ? GhostRoleView.ForRecipient(seer, seer)
                        : deferredRoles.GetValueOrDefault((seer.PlayerId, seer.PlayerId), selfView.Item1);
                }
                else restoreSelf = false;
            }
            if (restoreSelf || releasedPlayers.Contains(seer.PlayerId))
                restorations[seer.PlayerId] = (selfRole, restoreSelf, null);
        }

        ResetAllCooldown(releasedPlayers, restorations);
        publishing = true;
        try
        {
            foreach (var seer in vanillaPlayers)
            {
                bool masked = IsProtectedRecipient(seer.PlayerId);
                foreach (var target in Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected && player != seer))
                {
                    if (!RpcSetRoleReplacer.RoleMap.TryGetValue((seer.PlayerId, target.PlayerId), out var view)) continue;
                    if (!masked && !deferredRoles.ContainsKey((seer.PlayerId, target.PlayerId))) continue;
                    var role = target.Data.IsDead ? GhostRoleView.ForRecipient(target, seer)
                        : deferredRoles.GetValueOrDefault((seer.PlayerId, target.PlayerId), view.Item1);
                    target.RpcSetRoleDesync(role, seer.GetClientId());
                }
                if (!restorations.TryGetValue(seer.PlayerId, out var restoration)) continue;
                var options = new PlayerGameOptionsSender(seer).BuildGameOptions();
                var factory = GameOptionsManager.Instance.gameOptionsFactory;
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
                    options.SetFloat(FloatOptionNames.KillCooldown, cooldown * 2f);
                    WriteOptions(factory.ToBytes(options, AprilFoolsMode.IsAprilFoolsModeToggledOn));
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
        deferredRoles.Clear();
        if (sendGameDataAfter) SendGameData();
    }
    private static void ResetAllCooldown(HashSet<byte> releasedPlayers,
        Dictionary<byte, (RoleTypes SelfRole, bool RestoreSelf, float? Cooldown)> restorations)
    {
        foreach (var seer in Main.AllPlayerControls)
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
        warnedUnavailable = false;
    }
    public static void Reset()
    {
        logger.Info("==Reset==");
        meetingGeneration++;
        protectedRecipients.Clear();
        bufferedPlayers.Clear();
        hiddenRecipients.Clear();
        deferredRoles.Clear();
        sentMask.Clear();
        publishing = serializing = false;
        GhostRoleView.ResetPending();
        IsCached = false;
        ShowExiledInfo = false;
        StoreExiledMessage = "";
        ExilePlayerId = -1;
        SkipTasks = false;
        BlackOutIsActive = false;
        ProtectingVanilla = false;
        votingResultPending = false;
        warnedUnavailable = false;
    }

    public static bool ShowExiledInfo = false;
    public static string StoreExiledMessage = "";
}

// A broadcast native player-info update would otherwise dismantle the private
// living roster during exile or the buffer. Serialize the same update per seer.
[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.Serialize))]
internal static class AntiBlackoutPlayerInfoPatch
{
    private static bool Prefix(NetworkedPlayerInfo __instance, [HarmonyArgument(1)] bool initialState, ref bool __result)
    {
        if (initialState || !AmongUsClient.Instance.AmHost || !AntiBlackout.RedirectingPlayerData) return true;
        // Retain dirt until the queued native ghost publication has run. Sending
        // IsDead first makes RoleManager skip the native Die transition.
        if (!AntiBlackout.WaitingForGhostViews)
        {
            AntiBlackout.SendPlayerData(__instance);
            __instance.ClearOrDecrementDirt();
        }
        __result = false;
        return false;
    }
}
