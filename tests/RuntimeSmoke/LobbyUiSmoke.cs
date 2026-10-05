using System;
using System.Collections.Generic;
using System.IO;
using InnerNet;
using TMPro;
using TOHE;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace TOHEE.RuntimeSmoke;

// Fixed native lobby paths only. Text is compared in memory and never serialized.
internal sealed class LobbyUiSmoke(Func<bool> ownsSession, string directory)
{
    private const string WelcomePrefix = "【欢迎来到 Town of Host Enhanced】";
    private const string WelcomeTitle = "<b><color=#ffc0cb>" + WelcomePrefix + "</color></b>";
    private const string LegacyTitle = "<b><color=#ffc0cb" + WelcomePrefix + "</color></b>";
    private ChatController? chat;
    private IntPtr clientPointer, playerPointer;
    private int originalPage, previewId, stage, stableFrames, initialWelcomeCount;
    private double started;
    private long errorsAtStart;
    private string command = "", originalPlayerName = "";
    private bool restorePending;
    private readonly List<object> checks = new();
    internal bool Busy { get; private set; }
    internal string Outcome { get; private set; } = "idle";
    internal string Detail { get; private set; } = "";
    internal static bool IsCommand(string value) => value is "gui_lobby" or "lobby_welcome";

    internal bool Begin(string value, double now, out string rejection)
    {
        rejection = "";
        if (Busy || !IsCommand(value) || !Ready())
        { rejection = "requires_owned_single_player_loopback_host_lobby"; return false; }
        chat = HudManager.Instance.Chat;
        if (!chat || chat.State != ChatControllerState.Closed || GameSettingMenu.Instance ||
            Main.MessagesToSend.Count != 0 || !LobbySettingsPreview.Text || !LobbySettingsPreview.Text.gameObject.activeInHierarchy)
        { rejection = "requires_visible_preview_closed_chat_settings_and_empty_message_queue"; return false; }
        if (value == "lobby_welcome" && !KnownWelcomeOnDisk())
        { rejection = "requires_one_known_default_chinese_welcome_title_in_existing_template"; return false; }
        command = value; started = now; stage = stableFrames = 0; checks.Clear();
        errorsAtStart = SmokeErrorCounter.Count; originalPage = OptionShower.currentPage;
        previewId = LobbySettingsPreview.Text.GetInstanceID();
        clientPointer = AmongUsClient.Instance.Pointer; playerPointer = PlayerControl.LocalPlayer.Pointer;
        originalPlayerName = PlayerControl.LocalPlayer.Data.PlayerName;
        Busy = restorePending = true; Outcome = "running"; Detail = "";
        if (value == "lobby_welcome")
        {
            try
            {
                initialWelcomeCount = CountWelcome();
                // The actual public template path covers the legacy on-disk typo repair.
                TemplateManager.SendTemplate("welcome", byte.MaxValue);
                chat.Toggle();
            }
            catch (Exception ex) { Finish("failed", ex.GetType().Name); throw; }
        }
        return true;
    }

    private bool Ready()
    {
        var client = AmongUsClient.Instance;
        return ownsSession() && client && client.AmConnected && client.AmHost &&
            client.NetworkMode == NetworkModes.LocalGame && client.GameId == 32 &&
            client.GetNetworkAddress() == "127.0.0.1" && client.GetNetworkPort() == 22023 &&
            client.GameState == InnerNetClient.GameStates.Joined && GameData.Instance && GameData.Instance.PlayerCount == 1 &&
            PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.Data != null && !ShipStatus.Instance &&
            DestroyableSingleton<HudManager>.InstanceExists && DestroyableSingleton<GameStartManager>.InstanceExists;
    }

    private bool StillOwned() => Ready() && AmongUsClient.Instance.Pointer == clientPointer &&
        PlayerControl.LocalPlayer.Pointer == playerPointer && chat && HudManager.Instance.Chat.Pointer == chat!.Pointer;

    private static bool KnownWelcomeOnDisk()
    {
        // Reject customized titles; never replace a user's template just for a test.
        string path = Path.Combine(BepInEx.Paths.GameRootPath, "TOHE-DATA", "template.txt");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return false;
        int count = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (!line.StartsWith("welcome:", StringComparison.OrdinalIgnoreCase)) continue;
            count++;
            int begin = line.IndexOf("<title>", StringComparison.Ordinal), end = line.IndexOf("</title>", StringComparison.Ordinal);
            if (begin < 0 || end <= begin) return false;
            string title = line[(begin + 7)..end];
            if (title != WelcomeTitle && title != LegacyTitle) return false;
        }
        return count == 1;
    }

    internal bool Tick(double now)
    {
        if (!Busy) return true;
        if (!StillOwned()) return Finish("failed", "owned_lobby_or_native_chat_lost");
        if (now - started >= 8) return Finish("timed_out", "eight_second_native_ui_observation_limit");
        if (SmokeErrorCounter.Count != errorsAtStart) return Finish("failed", "logged_error_during_lobby_ui");
        if (command == "lobby_welcome") return TickWelcome();
        var preview = LobbySettingsPreview.Text;
        if (!preview || preview.GetInstanceID() != previewId || PreviewCount() != 1)
            return Finish("failed", "preview_replaced_or_accumulated");
        if (stage == 0)
        {
            if (++stableFrames < 2) return false;
            if (!preview.gameObject.activeInHierarchy || preview.text != OptionShower.GetTextNoFresh())
                return Finish("failed", "initial_preview_text_or_visibility_mismatch");
            if (OptionShower.pages.Count < 2) return Finish("failed", "pagination_requires_at_least_two_pages");
            checks.Add(new { check = "initial_single_visible_preview", page = OptionShower.currentPage, pages = OptionShower.pages.Count });
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "lobby-preview-first.png"));
            OptionShower.Next(); stableFrames = 0; stage = 1; return false;
        }
        if (stage == 1)
        {
            if (OptionShower.currentPage == originalPage || preview.text != OptionShower.GetTextNoFresh()) return false;
            if (++stableFrames < 2) return false;
            checks.Add(new { check = "next_page_same_preview", page = OptionShower.currentPage });
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "lobby-preview-next.png"));
            OptionShower.currentPage = originalPage; stableFrames = 0; stage = 2; return false;
        }
        if (stage == 2)
        {
            if (preview.text != OptionShower.GetTextNoFresh()) return false;
            checks.Add(new { check = "original_page_restored", page = OptionShower.currentPage });
            chat!.Toggle(); stage = 3; return false;
        }
        if (stage == 3)
        {
            if (chat!.State != ChatControllerState.Open) return false;
            if (preview.gameObject.activeInHierarchy) return Finish("failed", "preview_visible_over_native_open_chat");
            checks.Add(new { check = "native_open_chat_hides_preview" });
            chat.Toggle(); stage = 4; return false;
        }
        if (chat!.State != ChatControllerState.Closed || !preview.gameObject.activeInHierarchy) return false;
        checks.Add(new { check = "native_closed_chat_restores_same_single_preview" });
        return Finish("succeeded", "native_page_switch_return_and_chat_visibility_observed");
    }

    private bool TickWelcome()
    {
        if (stage == 0)
        {
            if (chat!.State != ChatControllerState.Open || Main.MessagesToSend.Count != 0 || CountWelcome() <= initialWelcomeCount) return false;
            ChatBubble? welcome = null;
            foreach (var bubble in chat.GetComponentsInChildren<ChatBubble>(true))
                if (bubble.gameObject.activeInHierarchy && bubble.NameText && bubble.NameText.GetParsedText().StartsWith(WelcomePrefix, StringComparison.Ordinal)) welcome = bubble;
            if (!welcome || !welcome!.TextArea) return false;
            welcome.NameText.ForceMeshUpdate(); welcome.TextArea.ForceMeshUpdate();
            string parsed = welcome.NameText.GetParsedText();
            var nameBounds = WorldTextBounds(welcome.NameText); var bodyBounds = WorldTextBounds(welcome.TextArea);
            bool overlap = Overlap(nameBounds, bodyBounds);
            bool nameRestored = PlayerControl.LocalPlayer.Data.PlayerName == originalPlayerName;
            checks.Add(new { check = "actual_template_native_bubble", visible_prefix_correct = parsed.StartsWith(WelcomePrefix, StringComparison.Ordinal),
                raw_color_tag_visible = parsed.Contains("<color", StringComparison.Ordinal), name_body_overlap = overlap,
                player_display_name_restored = nameRestored, name_bounds = BoundsReport(nameBounds), body_bounds = BoundsReport(bodyBounds) });
            if (parsed.Contains("<color", StringComparison.Ordinal) || overlap || !nameRestored || nameBounds.size.y <= 0 || bodyBounds.size.y <= 0)
                return Finish("failed", "native_welcome_title_parse_layout_or_player_restore_failed");
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "lobby-welcome.png"));
            stage = 1; return false; // Leave one end-of-frame render before closing.
        }
        if (stage == 1) { chat!.Toggle(); stage = 2; return false; }
        if (chat!.State != ChatControllerState.Closed || !LobbySettingsPreview.Text || !LobbySettingsPreview.Text.gameObject.activeInHierarchy) return false;
        return Finish("succeeded", "actual_on_disk_welcome_native_parse_bounds_and_player_restore_observed");
    }

    private int CountWelcome()
    {
        int count = 0;
        foreach (var bubble in chat!.GetComponentsInChildren<ChatBubble>(true))
            if (bubble.gameObject.activeInHierarchy && bubble.NameText && bubble.NameText.GetParsedText().StartsWith(WelcomePrefix, StringComparison.Ordinal)) count++;
        return count;
    }

    internal void Stop(string outcome, string detail) => Finish(outcome, detail);
    private bool Finish(string outcome, string detail)
    {
        if (restorePending && StillOwned())
        {
            OptionShower.currentPage = originalPage;
            if (chat!.IsOpenOrOpening) chat!.Close();
        }
        restorePending = false; originalPlayerName = ""; Busy = false; Outcome = outcome; Detail = detail;
        return true;
    }

    internal object Result() => new { state = Outcome, detail = Detail, checks, error_delta = SmokeErrorCounter.Count - errorsAtStart, layout = Layout() };
    internal static object Layout()
    {
        var text = LobbySettingsPreview.Text;
        var nativeChat = DestroyableSingleton<HudManager>.InstanceExists ? HudManager.Instance.Chat : null;
        return new { live_preview_count = PreviewCount(), page = OptionShower.currentPage, pages = OptionShower.pages.Count,
            chat_state = nativeChat ? nativeChat!.State.ToString() : null,
            preview = text ? new { active = text.gameObject.activeInHierarchy, enabled = text.enabled,
                font = text.font ? text.font.name : null, font_size = text.fontSize, rich_text = text.richText,
                bounds = BoundsReport(WorldTextBounds(text)), rect_width = text.rectTransform.rect.width, rect_height = text.rectTransform.rect.height,
                text_matches_page = text.text == OptionShower.GetTextNoFresh(), raw_color_tag_visible = text.GetParsedText().Contains("<color", StringComparison.Ordinal) } : null };
    }

    private static int PreviewCount()
    {
        int count = 0;
        foreach (var text in UObject.FindObjectsOfType<TextMeshPro>(true))
            if (text.gameObject.name == "TOHEELobbySettingsPreview" && text.gameObject.scene.IsValid()) count++;
        return count;
    }
    private static Bounds WorldTextBounds(TMP_Text text)
    {
        var local = text.textBounds;
        var bounds = new Bounds(text.transform.TransformPoint(local.min), Vector3.zero);
        bounds.Encapsulate(text.transform.TransformPoint(local.max));
        return bounds;
    }
    private static bool Overlap(Bounds a, Bounds b) => a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y;
    private static object BoundsReport(Bounds b) => new { min_x = b.min.x, max_x = b.max.x, min_y = b.min.y, max_y = b.max.y };
}
