using AmongUs.GameOptions;
using Hazel;
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

    public static bool IsCached { get; private set; } = false;
    private static Dictionary<byte, (bool isDead, bool Disconnected)> isDeadCache = [];
    private readonly static LogHandler logger = Logger.Handler("AntiBlackout");
    private static bool votingResultPending;
    private static bool warnedUnavailable;

    public static void PrepareVotingResult(NetworkedPlayerInfo exiled)
    {
        var players = Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected && player.GetClientId() >= 0).ToArray();
        votingResultPending = true;
        warnedUnavailable = false;
        BlackOutIsActive = exiled != null && players.Length == 3 && players.Any(player => !player.IsModded());
    }

    public static void SetIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        logger.Info($"SetIsDead is called from {callerMethodName}");
        if (IsCached) return;

        SkipTasks = true;
        var players = Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected && player.GetClientId() >= 0).ToArray();
        var vanillaPlayers = players.Where(player => !player.IsModded()).ToArray();
        var realExile = ExilePlayerId >= 0 && !BlackOutIsActive;
        ProtectingVanilla = CustomWinnerHolder.WinnerTeam == CustomWinner.Default && vanillaPlayers.Length > 0
            && players.Length >= 3 && (!realExile || players.Length >= 4);
        if (vanillaPlayers.Length > 0 && !ProtectingVanilla && CustomWinnerHolder.WinnerTeam == CustomWinner.Default)
            WarnUnavailable(players);

        isDeadCache.Clear();
        foreach (var info in GameData.Instance.AllPlayers)
        {
            if (info == null) continue;
            isDeadCache[info.PlayerId] = (info.IsDead, info.Disconnected);
            if (info.Disconnected) continue;
            info.IsDead = false;
        }
        IsCached = true;
        if (ProtectingVanilla) RevivePlayersAndSetDummyImp();
        if (doSend) SendGameData();
    }
    private static void RevivePlayersAndSetDummyImp()
    {
        var players = Main.AllPlayerControls.Where(player => player.Data != null && !player.Data.Disconnected && player.GetClientId() >= 0).ToArray();
        var dummyImp = players.First(player => player.IsHost());
        if (!BlackOutIsActive && dummyImp.PlayerId == ExilePlayerId)
            dummyImp = players.First(player => player.PlayerId != ExilePlayerId);
        foreach (var seer in players)
        {
            if (seer.IsModded()) continue;
            foreach (var target in players)
            {
                RoleTypes targetRoleType = target.PlayerId == dummyImp.PlayerId ? RoleTypes.Impostor : RoleTypes.Crewmate;

                target.RpcSetRoleDesync(targetRoleType, seer.GetClientId());
            }
        }
    }
    private static void WarnUnavailable(PlayerControl[] players)
    {
        if (warnedUnavailable) return;
        warnedUnavailable = true;
        ErrorText.Instance?.ShowAntiBlackoutWarning();
        foreach (var player in players.Where(player => player.IsNonHostModdedClient()))
            CustomRpcTransport.SendPayload(CustomRPC.AntiBlackoutUnavailable, _ => { }, player.GetClientId());
        logger.Warn("This meeting cannot protect vanilla clients from a post-meeting black screen");
    }
    public static void RestoreIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        logger.Info($"RestoreIsDead is called from {callerMethodName}");
        foreach (var info in GameData.Instance.AllPlayers)
        {
            if (info == null) continue;
            if (isDeadCache.TryGetValue(info.PlayerId, out var val))
            {
                info.IsDead = val.isDead;
                info.Disconnected = val.Disconnected;
            }
        }
        isDeadCache.Clear();
        IsCached = false;
        if (doSend) SendGameData();
    }

    public static void SendGameData([CallerMemberName] string callerMethodName = "")
    {
        logger.Info($"SendGameData is called from {callerMethodName}");
        Utils.SendGameData(SendOption.Reliable);
    }
    public static void OnDisconnect(NetworkedPlayerInfo player)
    {
        if (!AmongUsClient.Instance.AmHost || (!IsCached && !votingResultPending) || !player.Disconnected) return;
        if (IsCached)
        {
            isDeadCache[player.PlayerId] = (true, true);
            player.IsDead = player.Disconnected = true;
        }
        var players = Main.AllPlayerControls.Where(pc => pc.Data != null && !pc.Data.Disconnected && pc.GetClientId() >= 0).ToArray();
        if (votingResultPending && CustomWinnerHolder.WinnerTeam == CustomWinner.Default && players.Any(pc => !pc.IsModded()) &&
            (players.Length < 3 || players.Length == 3 && ExilePlayerId >= 0 && !BlackOutIsActive))
        {
            ProtectingVanilla = false;
            WarnUnavailable(players);
        }
        if (!IsCached) return;
        if (ProtectingVanilla) RevivePlayersAndSetDummyImp();
        SendGameData();
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
            if (pc.IsNonHostModdedClient()) //For mod client show real result
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
            foreach (var pc in Main.AllPlayerControls.Where(p => !p.IsModded()).ToArray())
            {
                pc.Notify(CheckForEndVotingPatch.TempExileMsg, time: timeNotify);
            }
        }

        try
        {
            _ = new LateTask(() =>
            {
                foreach (var pc in Main.AllAlivePlayerControls)
                {
                    pc.GetRoleClass()?.NotifyAfterMeeting();
                }
            }, timeNotify + 0.2f, "Notify AfterMeetingTasks");
        }
        catch (Exception error)
        {
            Logger.Error($"{error}", "AntiBlackout.AfterMeetingTasks");
        }
    }
    public static void SetRealPlayerRoles(bool sendGameDataAfter = false)
    {
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;
        if (OfficialAnticheatPolicy.Enabled)
        {
            var barrier = CustomRpcTransport.CaptureBarrier();
            if (!barrier.IsComplete)
            {
                Main.Instance.StartCoroutine(WaitForRoleMetadata(OfficialSessionContext.Capture(), barrier, sendGameDataAfter));
                return;
            }
        }

        foreach (var ((seerId, targetId), (roletype, _)) in RpcSetRoleReplacer.RoleMap)
        {
            // skip host
            var seer = seerId.GetPlayer();
            var target = targetId.GetPlayer();

            if (seer == null || target == null) continue;
            if (seer.IsHost()) continue;
            if (seer.IsModded()) continue;

            var changedRoleType = roletype;
            if (target.Data.IsDead)
            {
                changedRoleType = GhostRoleView.ForRecipient(target, seer);
            }

            target.RpcSetRoleDesync(changedRoleType, seer.GetClientId());
        }
        ProtectingVanilla = false;
        if (sendGameDataAfter) SendGameData();
        votingResultPending = false;
    }
    private static System.Collections.IEnumerator WaitForRoleMetadata(OfficialSessionContext context, RpcSendBarrier barrier, bool sendGameDataAfter)
    {
        while (!barrier.IsComplete)
        {
            if (barrier.IsCancelled || !context.IsCurrent() || !AmongUsClient.Instance.AmHost
                || CustomWinnerHolder.WinnerTeam != CustomWinner.Default) yield break;
            yield return null;
        }
        if (context.IsCurrent() && AmongUsClient.Instance.AmHost) SetRealPlayerRoles(sendGameDataAfter);
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
                    seer.SetKillCooldown(kcd - 2f);
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
        ResetAllCooldown();
    }
    public static void Reset()
    {
        logger.Info("==Reset==");
        isDeadCache ??= [];
        isDeadCache.Clear();
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
