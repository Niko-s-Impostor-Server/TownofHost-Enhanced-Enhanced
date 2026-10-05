using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Hazel;
using InnerNet;
using TOHE;
using TOHE.Modules.ChatManager;
using UnityEngine;

namespace TOHEE.RuntimeSmoke;

// Fixed local chat test. Observers never change arguments, results, or original execution.
internal sealed class LocalChatSmoke(Func<bool> ownsSession)
{
    private static LocalChatSmoke? active;
    private Harmony? hooks;
    private readonly List<HookTarget> targets = new();
    private readonly List<CleanupFailure> cleanupFailures = new();
    private List<ExceptionDiagnostic> failureExceptions = new();
    private string phase = "idle", targetLabel = "none", failurePhase = "", failureTarget = "";
    private string observationOutcome = "idle", observationDetail = "";
    private string? startedUtc, endedUtc;
    private double ended;
    private int patchedTargetCount;
    private double started;
    private IntPtr clientPointer, playerPointer;
    private int hostId, clientId, playerCount;
    private bool isHost, sending;
    [ThreadStatic] private static int receiptDepth;
    [ThreadStatic] private static byte receiptSender;
    private string command = "";
    private long uiSends, rpcSends, reliableHostRpcs, otherChatRpcs;
    private long receipts, canceledReceipts, coreDispatches, privateReplies, publicReplies;
    private long unexpectedNonhostReceipts;
    private long displayAttempts, nativeDisplays, replayCalls, scopedReplayCalls;
    private long nativeChatAdds, nativeRemoteChatAdds, receivedFixedReplies;
    internal bool Busy { get; private set; }
    internal string Outcome { get; private set; } = "idle";
    internal string Detail { get; private set; } = "";
    internal static bool IsCommand(string value) => value is "local_chat_watch" or "local_cmd_id";

    internal bool Begin(string value, double now, out string rejection)
    {
        rejection = "";
        var client = AmongUsClient.Instance;
        var player = PlayerControl.LocalPlayer;
        if (Busy || active != null) { rejection = "local_chat_observer_busy"; return false; }
        if (targets.Count > 0) { rejection = "requires_process_restart_after_observer_cleanup_failure"; return false; }
        if (!IsCommand(value) || !ownsSession() || !LocalReady() || !player || !player.AmOwner ||
            player.Data == null || !DestroyableSingleton<HudManager>.InstanceExists || !HudManager.Instance.Chat)
        { rejection = "requires_owned_loopback_two_or_three_player_lobby_or_game"; return false; }
        var chat = HudManager.Instance.Chat;
        if (value == "local_cmd_id" && (client.AmHost || !TOHE.GameStates.IsModHost ||
            !Options.IsLoaded || !Options.EnableVoteCommand.GetBool() ||
            !chat.freeChatField || !chat.freeChatField.textArea || chat.quickChatField.visible ||
            chat.freeChatField.textArea.text != "" || chat.timeSinceLastMessage < 3f))
        { rejection = "requires_owned_mod_nonhost_empty_free_chat_natural_cooldown_and_enabled_vote_command"; return false; }
        command = value;
        clientPointer = client.Pointer;
        playerPointer = player.Pointer;
        hostId = client.HostId; clientId = client.ClientId;
        isHost = client.AmHost; playerCount = GameData.Instance.PlayerCount;
        started = now;
        startedUtc = DateTimeOffset.UtcNow.ToString("O"); endedUtc = null; ended = 0;
        failureExceptions.Clear(); cleanupFailures.Clear(); failurePhase = failureTarget = "";
        observationOutcome = "running"; observationDetail = ""; patchedTargetCount = 0;
        uiSends = rpcSends = reliableHostRpcs = otherChatRpcs = receipts = canceledReceipts = coreDispatches =
            privateReplies = publicReplies = displayAttempts = nativeDisplays = replayCalls = scopedReplayCalls =
            nativeChatAdds = nativeRemoteChatAdds = receivedFixedReplies = unexpectedNonhostReceipts = 0;
        Outcome = "running"; Detail = "bounded_counter_only_local_chat_observation";
        try
        {
            active = this;
            hooks = new Harmony("local.tohee.runtime-smoke.private-chat");
            // Passive watchers never send: no sender detours are installed there.
            if (value == "local_cmd_id")
            {
                Patch(typeof(ChatController), nameof(ChatController.SendChat), null, nameof(UiSend));
                Patch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChat), nameof(RpcSend), null);
                Patch(typeof(InnerNetClient), nameof(InnerNetClient.StartRpcImmediately), nameof(StartRpc), null,
                    [typeof(uint), typeof(byte), typeof(SendOption), typeof(int)]);
            }
            Patch(typeof(ChatController), nameof(ChatController.AddChat), nameof(AddChatBefore), nameof(AddChatAfter),
                [typeof(PlayerControl), typeof(string), typeof(bool)]);
            phase = "resolve_fixed_type"; targetLabel = "TOHE.ChatCommands";
            var commands = typeof(Main).Assembly.GetType("TOHE.ChatCommands", throwOnError: true)!;
            Patch(commands, "OnReceiveChat", nameof(ReceiveBefore), nameof(ReceiveAfter));
            Patch(commands, "OnReceiveChatCore", nameof(CoreBefore), null);
            Patch(typeof(Utils), nameof(Utils.SendMessage), nameof(ReplyBefore), null);
            Patch(typeof(ChatManager), nameof(ChatManager.SendPreviousMessagesToAll), nameof(ReplayBefore), null);
            Busy = true;
            phase = "observe"; targetLabel = "none";
            if (value == "local_cmd_id")
            {
                // Set only the fixed input, then use the native UI handler once.
                // Do not reset cooldown, fabricate packets, or invoke a production handler directly.
                phase = "native_input"; targetLabel = "TextBoxTMP.SetText";
                chat.freeChatField.textArea.SetText("/cmd id", "");
                sending = true;
                phase = "native_send"; targetLabel = "ChatController.SendChat";
                try { chat.SendChat(); }
                finally { sending = false; }
                phase = "observe"; targetLabel = "none";
            }
            return true;
        }
        catch (Exception ex)
        {
            Fail(ex);
            rejection = Detail;
            return false;
        }
    }

    private void Patch(Type type, string target, string? before, string? after, Type[]? arguments = null)
    {
        phase = "resolve_fixed_method"; targetLabel = type.Name + "." + target;
        var method = arguments == null ? AccessTools.Method(type, target) : AccessTools.Method(type, target, arguments);
        if (method == null) throw new MissingMethodException(type.FullName, target);
        HarmonyMethod? Hook(string? name, int priority) => name == null ? null : new HarmonyMethod(
            AccessTools.Method(typeof(LocalChatSmoke), name)) { priority = priority };
        // Record before Patch because a failed wrapper construction can leave partial state.
        targets.Add(new HookTarget(method, targetLabel));
        phase = "patch";
        hooks!.Patch(method, prefix: Hook(before, Priority.First), postfix: Hook(after, Priority.Last));
        patchedTargetCount++;
    }

    private static bool LocalReady()
    {
        var client = AmongUsClient.Instance;
        return client && client.AmConnected && client.NetworkMode == NetworkModes.LocalGame &&
            client.GameId == 32 && client.GetNetworkAddress() == "127.0.0.1" && client.GetNetworkPort() == 22023 &&
            client.GameState is InnerNetClient.GameStates.Joined or InnerNetClient.GameStates.Started &&
            GameData.Instance && GameData.Instance.PlayerCount is >= 2 and <= 3;
    }

    private bool StillOwned()
    {
        var client = AmongUsClient.Instance;
        return ownsSession() && LocalReady() && client.Pointer == clientPointer && client.HostId == hostId &&
            client.ClientId == clientId && client.AmHost == isHost && GameData.Instance.PlayerCount == playerCount &&
            PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.Pointer == playerPointer;
    }

    internal bool Tick(double now)
    {
        if (!Busy) return true;
        if (!StillOwned()) { Stop("failed", "owned_local_chat_session_changed"); return true; }
        if (now - started < 8) return false;
        if (displayAttempts != 0 || nativeDisplays != 0 || replayCalls != 0 || publicReplies != 0 ||
            otherChatRpcs != 0 || unexpectedNonhostReceipts != 0 ||
            command == "local_chat_watch" && !isHost && receivedFixedReplies != 0)
            Stop("failed", "private_chat_display_replay_or_public_route_observed");
        else if (command == "local_cmd_id" &&
                 (uiSends != 1 || rpcSends != 1 || reliableHostRpcs != 1 || receivedFixedReplies != 1))
            Stop("failed", "fixed_ui_command_send_or_native_private_reply_not_observed_exactly_once");
        else if (command == "local_chat_watch" && isHost &&
                 (receipts != 1 || canceledReceipts != 1 || coreDispatches != 1 || privateReplies != 1))
            Stop("failed", "host_did_not_observe_one_consumed_command_and_private_reply");
        else Stop("succeeded", command == "local_cmd_id" ? "native_ui_fixed_command_host_route_observed" :
            isHost ? "host_fixed_command_execution_and_private_reply_observed" : "bounded_local_chat_observation_complete");
        return true;
    }

    internal void Stop(string outcome, string detail)
    {
        // Disable observation before touching detours; cleanup failure must never
        // reactivate observers or replace a prior setup/observation failure.
        Busy = false; sending = false;
        if (active == this) active = null;
        receiptDepth = 0;
        observationOutcome = outcome; observationDetail = detail;
        Outcome = outcome; Detail = detail;
        ended = Time.realtimeSinceStartup;
        endedUtc = DateTimeOffset.UtcNow.ToString("O");
        if (hooks != null)
        {
            for (int index = targets.Count - 1; index >= 0; index--)
            {
                var target = targets[index];
                try
                {
                    // Fixed original and this helper's ID only; product patches remain owned by TOHE.
                    hooks.Unpatch(target.Method, HarmonyPatchType.All, hooks.Id);
                    targets.RemoveAt(index);
                }
                catch (Exception ex)
                {
                    cleanupFailures.Add(new CleanupFailure(target.Label, Diagnose(ex)));
                }
            }
        }
        if (targets.Count == 0) hooks = null;
        else if (outcome != "failed")
        {
            Outcome = "failed";
            Detail = "local_chat_cleanup_failed_requires_process_restart";
        }
        phase = targets.Count == 0 ? "finished" : "cleanup_failed";
        targetLabel = "none";
    }

    internal void Fail(Exception exception)
    {
        if (failureExceptions.Count == 0)
        {
            failurePhase = phase; failureTarget = targetLabel;
            failureExceptions = Diagnose(exception);
        }
        Stop("failed", "local_chat_" + failurePhase + "_" + failureTarget + "_" + failureExceptions[0].Type);
    }

    private static List<ExceptionDiagnostic> Diagnose(Exception exception)
    {
        var result = new List<ExceptionDiagnostic>();
        for (Exception? current = exception; current != null && result.Count < 8; current = current.InnerException)
            result.Add(new ExceptionDiagnostic(current.GetType().FullName ?? current.GetType().Name,
                SafeMessage(current.Message)));
        return result;
    }

    private static string SafeMessage(string? message)
    {
        // Exception messages are not copied. These fixed categories expose the
        // deciding error without paths, actual arguments, chat bodies or secrets.
        if (string.IsNullOrEmpty(message)) return "empty_exception_message";
        if (message.Contains("Parameter", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("not found", StringComparison.OrdinalIgnoreCase)) return "patch_parameter_not_found";
        if (message.Contains("invalid IL", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("invalid program", StringComparison.OrdinalIgnoreCase)) return "invalid_generated_il_or_program";
        if (message.Contains("signature", StringComparison.OrdinalIgnoreCase)) return "patch_signature_error";
        if (message.Contains("label", StringComparison.OrdinalIgnoreCase)) return "generated_il_label_error";
        if (message.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unsupported", StringComparison.OrdinalIgnoreCase)) return "unsupported_runtime_operation";
        if (message.Contains("not set to an instance", StringComparison.OrdinalIgnoreCase)) return "null_object_reference";
        if (message.Contains("out of range", StringComparison.OrdinalIgnoreCase)) return "argument_or_index_out_of_range";
        if (message.Contains("Patching exception", StringComparison.OrdinalIgnoreCase)) return "harmony_patching_exception";
        if (message.Contains("method", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("not found", StringComparison.OrdinalIgnoreCase)) return "fixed_method_not_found";
        return "exception_message_omitted";
    }

    internal object Snapshot() => new
    {
        state = Outcome, detail = Detail, command, observer_active = Busy, observed_as_host = isHost,
        observation_seconds = 8, player_count = playerCount,
        started_utc = startedUtc, ended_utc = endedUtc, started_realtime = started,
        ended_realtime = endedUtc == null ? (double?)null : ended,
        observation_outcome = observationOutcome, observation_detail = observationDetail,
        current_phase = phase, current_fixed_target = targetLabel,
        patched_target_count = patchedTargetCount, cleanup_pending_target_count = targets.Count,
        failure_phase = failurePhase, failure_fixed_target = failureTarget,
        failure_exception_chain = failureExceptions, cleanup_failures = cleanupFailures,
        native_ui_send_original_count = uiSends, native_rpc_send_call_count = rpcSends,
        reliable_send_chat_to_host_count = reliableHostRpcs, other_send_chat_route_count = otherChatRpcs,
        host_envelope_receive_count = receipts, consumed_envelope_count = canceledReceipts,
        unexpected_nonhost_envelope_receive_count = unexpectedNonhostReceipts,
        host_fixed_id_dispatch_count = coreDispatches, private_reply_to_command_sender_count = privateReplies,
        public_reply_during_dispatch_count = publicReplies, command_add_chat_attempt_count = displayAttempts,
        command_native_display_count = nativeDisplays, native_chat_add_original_count = nativeChatAdds,
        native_remote_chat_add_original_count = nativeRemoteChatAdds, public_replay_call_count = replayCalls,
        native_remote_fixed_id_reply_display_count = receivedFixedReplies,
        replay_call_during_command_count = scopedReplayCalls,
        host_execution_requires_host_report = command == "local_cmd_id",
        third_client_ui_observation_requires_separate_capture = true
    };

    private static LocalChatSmoke? Current => active is { Busy: true } test && test.StillOwned() ? test : null;
    private static void UiSend(bool __runOriginal)
    { var test = Current; if (test?.sending == true && __runOriginal) test.uiSends++; }
    private static void RpcSend(PlayerControl __instance, string chatText)
    { var test = Current; if (test?.sending == true && __instance == PlayerControl.LocalPlayer && chatText == "/cmd id") test.rpcSends++; }
    private static void StartRpc(byte __1, SendOption __2, int __3)
    {
        var test = Current;
        if (test?.sending != true || __1 != (byte)RpcCalls.SendChat) return;
        if (__2 == SendOption.Reliable && __3 == AmongUsClient.Instance.HostId) test.reliableHostRpcs++;
        else test.otherChatRpcs++;
    }
    private static void AddChatBefore(string __1, out bool __state)
    {
        var test = Current; __state = test != null && __1 != null &&
            (__1 == "/id" || __1.StartsWith("/cmd", StringComparison.Ordinal));
        if (__state) test!.displayAttempts++;
    }
    private static void AddChatAfter(PlayerControl __0, string __1, bool __state, bool __runOriginal)
    {
        if (!__runOriginal || Current is not { } test) return;
        test.nativeChatAdds++;
        if (__0 && !__0.AmOwner)
        {
            test.nativeRemoteChatAdds++;
            // Hosts may use a different locale. Compare only this one fixed
            // translation key in native supported languages; retain no body text.
            foreach (SupportedLangs language in Enum.GetValues(typeof(SupportedLangs)))
            {
                var heading = Translator.GetString("PlayerIdList", language, showInvalid: false).RemoveHtmlTagsTemplate();
                if (string.IsNullOrEmpty(heading) || __1 == null || !__1.StartsWith(heading, StringComparison.Ordinal)) continue;
                test.receivedFixedReplies++;
                break;
            }
        }
        if (__state) test.nativeDisplays++;
    }
    private static void ReceiveBefore(PlayerControl player, string text, out bool __state)
    {
        var test = Current;
        if (test?.isHost == false && text == "/cmd id") test.unexpectedNonhostReceipts++;
        __state = test?.isHost == true && text == "/cmd id" && player && !player.AmOwner;
        if (!__state) return;
        test!.receipts++; receiptSender = player.PlayerId; receiptDepth++;
    }
    private static void ReceiveAfter(bool canceled, bool __state)
    {
        if (!__state) return;
        if (canceled && Current is { } test) test.canceledReceipts++;
        receiptDepth--;
    }
    private static void CoreBefore(string text)
    { if (receiptDepth > 0 && text == "/id" && Current is { } test) test.coreDispatches++; }
    private static void ReplyBefore(byte sendTo)
    {
        if (receiptDepth <= 0 || Current is not { } test) return;
        if (sendTo == receiptSender) test.privateReplies++;
        else if (sendTo == byte.MaxValue) test.publicReplies++;
    }
    private static void ReplayBefore()
    {
        if (Current is not { } test) return;
        test.replayCalls++; if (receiptDepth > 0) test.scopedReplayCalls++;
    }

    private sealed record HookTarget(MethodBase Method, string Label);
    private sealed record ExceptionDiagnostic(string Type, string SafeMessage);
    private sealed record CleanupFailure(string FixedTarget, List<ExceptionDiagnostic> ExceptionChain);
}
