using InnerNet;
using Hazel;
using TOHE.Roles.Core;
using TOHE.Roles.Vanilla;

namespace TOHE;

internal static class VanillaJudgeMeeting
{
    internal static void Queue(MeetingHud meeting, PlayerId judgeId, PlayerId targetId, ushort nonce)
    {
        if (MeetingAbilities.HandleNativeJudge(meeting, judgeId, targetId, nonce)) return;
        if (!CanQueue(meeting, judgeId, targetId, nonce)) return;
        meeting.AddOrUpdateJudgeOverrule(judgeId, targetId, nonce);
        meeting.playerStates.First(area => area.PlayerId.Value == judgeId.Value).SetHasVoted();
        PlayerControl.LocalPlayer.RpcSendChatNote(judgeId, ChatNoteTypes.DidVote);
        meeting.SetDirtyBit(1u);
        meeting.CheckForEndVoting();
    }

    internal static bool CanQueue(MeetingHud meeting, PlayerId judgeId, PlayerId targetId, ushort nonce)
    {
        var actor = Utils.GetPlayerById(judgeId.Value);
        var target = Utils.GetPlayerById(targetId.Value);
        var vote = meeting.playerStates.FirstOrDefault(area => area.PlayerId.Value == judgeId.Value);
        bool allowed = GameStates.IsVoting && nonce != 0 && actor && actor.IsAlive() &&
            !actor.Data.Disconnected && target && target != actor && target.IsAlive() && !target.Data.Disconnected &&
            vote && !vote.DidVote && actor.GetRoleClass() is JudgeTOHE { AbilityLimit: >= 1 };
        if (allowed)
        {
            var tasks = Main.PlayerStates[actor.PlayerId].TaskState;
            allowed = tasks.CompletedTasksCount * 100 >= System.Math.Max(0, tasks.AllTasksCount) * JudgeTOHE.TaskRequirementPercentage;
        }
        if (!allowed && actor && !actor.Data.Disconnected && GameStates.IsVoting)
            meeting.RpcClearVoteDelay(actor.GetClientId());
        return allowed;
    }

    internal static void Prune(MeetingHud meeting)
    {
        foreach (var entry in meeting.judgeOverrulesQueue.ToArray())
        {
            var actor = Utils.GetPlayerById(entry.JudgePlayerId.Value);
            var target = Utils.GetPlayerById(entry.OverruledPlayerId.Value);
            if (actor && actor.IsAlive() && !actor.Data.Disconnected && target && target.IsAlive() &&
                !target.Data.Disconnected && actor.GetRoleClass() is JudgeTOHE { AbilityLimit: >= 1 }) continue;
            meeting.judgeOverrulesQueue.Remove(entry);
            if (!actor || !actor.IsAlive() || actor.Data.Disconnected) continue;
            var vote = meeting.playerStates.FirstOrDefault(area => area.PlayerId.Value == actor.PlayerId);
            if (vote) vote.UnsetHasVoted();
            meeting.RpcClearVoteDelay(actor.GetClientId());
        }
    }

    internal static bool TryResolve(MeetingHud meeting, out PlayerControl judge, out byte exileId, out ushort nonce)
    {
        judge = null;
        exileId = byte.MaxValue;
        nonce = 0;
        foreach (var entry in meeting.judgeOverrulesQueue)
        {
            var actor = Utils.GetPlayerById(entry.JudgePlayerId.Value);
            var target = Utils.GetPlayerById(entry.OverruledPlayerId.Value);
            if (!actor || !actor.IsAlive() || actor.Data.Disconnected || !target || !target.IsAlive() ||
                target.Data.Disconnected || actor.GetRoleClass() is not JudgeTOHE { AbilityLimit: >= 1 }) continue;
            judge = actor;
            exileId = target.Is(Custom_Team.Impostor) ? target.PlayerId : actor.PlayerId;
            nonce = entry.OverruleNonce;
            return true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.HandleRpc))]
internal static class VanillaJudgeQueuePatch
{
    private static bool Prefix(MeetingHud __instance, [HarmonyArgument(0)] byte callId, [HarmonyArgument(1)] MessageReader reader)
    {
        if (callId != (byte)RpcCalls.QueueOverruleVotes || !AmongUsClient.Instance.AmHost ||
            !GameStates.IsModHost || !GameStates.IsNormalGame) return true;
        if (reader.BytesRemaining == 4)
            VanillaJudgeMeeting.Queue(__instance, new PlayerId(reader.ReadByte()), new PlayerId(reader.ReadByte()), reader.ReadUInt16());
        return false;
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CmdQueueOverruleVotes))]
internal static class VanillaJudgeLocalQueuePatch
{
    private static bool Prefix(MeetingHud __instance, [HarmonyArgument(0)] PlayerId judgeId,
        [HarmonyArgument(1)] PlayerId targetId, [HarmonyArgument(2)] ushort nonce)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsModHost || !GameStates.IsNormalGame) return true;
        VanillaJudgeMeeting.Queue(__instance, judgeId, targetId, nonce);
        return false;
    }
}
