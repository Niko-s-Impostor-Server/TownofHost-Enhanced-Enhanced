using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using System;
using System.Runtime.CompilerServices;
using TOHE.Modules;
using TOHE.Roles.Core;

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
    private static bool settling;
    private static bool publishing;
    private static bool serializing;
    private static bool warnedUnavailable;

    public static bool IsBuffered(byte playerId) => bufferedPlayers.Contains(playerId);
    public static bool IsProtectedRecipient(byte playerId) => protectedRecipients.Contains(playerId);
    internal static bool RedirectingPlayerData => ProtectingVanilla && !serializing;

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
        if (seer == null || seer.IsModded() || !(settling || IsProtectedRecipient(seer.PlayerId))) return false;
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
        if (!votingResultPending) return;
        var generation = meetingGeneration;
        var context = meetingContext;
        Main.Instance.StartCoroutine(Complete());

        System.Collections.IEnumerator Complete()
        {
            // Both anchors are after native recovery; Airship's anchor is after
            // PrespawnStep, never the WrapUpAndSpawn iterator factory.
            yield return new UnityEngine.WaitForSeconds(1.2f);
            if (!Current()) yield break;
            settling = true;
            try { settle(); }
            finally { settling = false; }
            if (!Current()) yield break;
            RefreshProtection();
            SendGameData();
            // Fixed empirical allowance, not a receiver WrapUp acknowledgement.
            // The first 1.2 seconds above already belongs to this two-second hold.
            if (bufferedPlayers.Count > 0) yield return new UnityEngine.WaitForSeconds(0.8f);
            var barrier = CustomRpcTransport.CaptureBarrier();
            while (!barrier.IsComplete)
            {
                if (!Current() || barrier.IsCancelled) yield break;
                yield return null;
            }
            if (!Current()) yield break;
            SetRealPlayerRoles();
            ResetAllCooldown();
            protectedRecipients.Clear();
            sentMask.Clear();
            ProtectingVanilla = false;
            SendGameData();
            bufferedPlayers.Clear();
            SyncBuffer();
            ResetAfterMeeting();
            Utils.SyncAllSettings();
            Utils.NotifyRoles(NoCache: true);
        }

        bool Current() => generation == meetingGeneration && context.IsCurrent()
            && AmongUsClient.Instance.AmHost && !GameStates.IsEnded && votingResultPending;
    }

    public static void SetRealPlayerRoles(bool sendGameDataAfter = false)
    {
        if (GameStates.IsEnded) return;
        publishing = true;
        try
        {
            foreach (var seer in Main.AllPlayerControls.Where(player => !player.IsModded() && player.Data != null && !player.Data.Disconnected))
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
                if (!masked && !deferredRoles.ContainsKey((seer.PlayerId, seer.PlayerId))) continue;
                if (!RpcSetRoleReplacer.RoleMap.TryGetValue((seer.PlayerId, seer.PlayerId), out var selfView)) continue;
                var selfRole = seer.Data.IsDead ? GhostRoleView.ForRecipient(seer, seer)
                    : deferredRoles.GetValueOrDefault((seer.PlayerId, seer.PlayerId), selfView.Item1);
                bool killButton = !seer.Data.IsDead && DestroyableSingleton<RoleManager>.Instance.GetRole(selfRole).CanUseKillButton;
                if (killButton)
                {
                    seer.ResetKillCooldown();
                    float cooldown = Main.AllPlayerKillCooldown.GetValueOrDefault(seer.PlayerId, Options.DefaultKillCooldown);
                    // FailedProtected sets half the configured cooldown. Publish
                    // doubled options while the mask/action restriction still holds.
                    Main.AllPlayerKillCooldown[seer.PlayerId] = cooldown * 2f;
                    seer.SyncSettings();
                    Main.AllPlayerKillCooldown[seer.PlayerId] = cooldown;
                }
                // These two tiny RPCs occupy one reliable packet. Native CoSetRole
                // installs the living role on its first advance in a loaded round;
                // the subsequent RPC initializes its timer before another frame.
                using var owner = new OfficialPacketBuilder.BoundedWriter(SendOption.Reliable);
                var writer = owner.Writer;
                writer.StartMessage(6);
                writer.Write(AmongUsClient.Instance.GameId);
                writer.WritePacked(seer.GetClientId());
                writer.StartMessage(2);
                writer.WritePacked(seer.NetId);
                writer.Write((byte)RpcCalls.SetRole);
                writer.Write((ushort)selfRole);
                writer.Write(true);
                writer.EndMessage();
                if (killButton)
                {
                    writer.StartMessage(2);
                    writer.WritePacked(seer.NetId);
                    writer.Write((byte)RpcCalls.MurderPlayer);
                    writer.WriteNetObject(seer);
                    writer.Write((int)MurderResultFlags.FailedProtected);
                    writer.EndMessage();
                }
                writer.EndMessage();
                OfficialNetworkSend.Send(AmongUsClient.Instance, writer);
            }
        }
        finally { publishing = false; }
        deferredRoles.Clear();
        if (sendGameDataAfter) SendGameData();
    }
    private static void ResetAllCooldown()
    {
        foreach (var seer in Main.AllPlayerControls)
        {
            if (seer.IsAlive())
            {
                seer.RpcResetAbilityCooldown();
                seer.ResetKillCooldown();

                if (Main.AllPlayerKillCooldown.TryGetValue(seer.PlayerId, out var kcd) && kcd >= 2f)
                    seer.SetKillCooldown(IsBuffered(seer.PlayerId) ? kcd : UnityEngine.Mathf.Max(0.01f, kcd - 2f));
            }
            else if (seer.HasGhostRole())
            {
                seer.RpcResetAbilityCooldown();
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
        settling = publishing = serializing = false;
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
        AntiBlackout.SendPlayerData(__instance);
        __instance.ClearOrDecrementDirt();
        __result = false;
        return false;
    }
}
