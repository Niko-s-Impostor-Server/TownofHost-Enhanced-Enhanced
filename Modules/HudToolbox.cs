using System;
using System.IO;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TOHE;

internal static class HudToolbox
{
    private const string OverlayName = "TOHEHudToolbox";
    private static readonly Color CaptionColor = new Color32(180, 216, 215, 255);
    private static readonly Color DividerColor = new(0.36f, 0.48f, 0.5f, 0.75f);
    private static PassiveButton entry;
    private static GameObject panel, content;
    private static BoxCollider2D blocker;
    private static TextMeshPro positionText;
    private static HudManager ownerHud;
    private static AmongUsClient panelClient;
    private static PlayerControl panelOwner;
    private static MeetingHud panelMeeting;
    private static GameStartManager pendingStartManager;
    private static MeetingHud.MeetingStates pendingMeetingState;
    private static GameStartManager.StartingStates pendingStartState;
    private static bool panelLobby;
    private static int availability;
    private static Section section;
    private static ActionKind pending;
    private static MenuVisuals visuals;
    private static PassiveButton close;
    private static string logResult;
    private static float nextDepthProbe, panelDepth;
    private static string depthMenu;

    private enum Section { Meeting, Lobby, Diagnostics, Debug }
    private enum ActionKind { None, StartMeeting, Settle, ForceSkip, StartGameNow, CancelCountdown, ExportLog, ShowChat, CrewWin, ImpostorWin }

    internal static void Update(HudManager hud)
    {
        if (!CanShow(hud))
        {
            Close();
            if (entry != null && entry.gameObject.activeSelf) entry.gameObject.SetActive(false);
            return;
        }

        if (ownerHud != hud || entry == null)
        {
            if (entry != null) Object.Destroy(entry.gameObject);
            ownerHud = hud;
            nextDepthProbe = 0f;
            depthMenu = null;
            var template = hud.GameMenu.DisableMouseMovement.Text;
            var entryVisuals = new MenuVisuals(template);
            entry = entryVisuals.Button(hud.transform, "TOHEHudToolboxButton", Vector3.zero,
                new Vector2(0.95f, 0.5f), Translator.GetString("ToolboxEntry"), () => { if (panel != null) Close(); else Open(hud); });
            var outline = entry.GetComponent<SpriteRenderer>();
            outline.color = new Color32(232, 178, 192, 255);
            outline.sortingOrder = 0;
            var inner = entryVisuals.Surface(entry.transform, "Inner", new Vector3(0f, 0f, -0.05f),
                new Vector2(0.90f, 0.45f), new Color32(43, 65, 71, 255));
            inner.sortingOrder = 1;
            entry.GetComponentInChildren<TextMeshPro>(true).sortingOrder = 2;
            entry.gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 32001;
            foreach (var item in entry.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = template.gameObject.layer;
        }

        if (panel is not null && !CurrentContext()) Close();

        Position(hud);
        if (!entry.gameObject.activeSelf) entry.gameObject.SetActive(true);
        if (panel == null) return;
        var controller = ControllerManager.Instance;
        if (controller != null && controller.IsMenuActiveAtAll(OverlayName) && controller.CurrentUiState?.MenuName != OverlayName)
            controller.OpenSpecificMenu(OverlayName);
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (pending != ActionKind.None) { pending = ActionKind.None; Render(); }
            else Close();
            return;
        }
        if (pending != ActionKind.None && !PendingValid())
        {
            pending = ActionKind.None;
            Render();
        }
        int current = Availability();
        if (pending == ActionKind.None && current != availability) Render();
        if (positionText != null && PlayerControl.LocalPlayer != null)
        {
            var position = PlayerControl.LocalPlayer.GetTruePosition();
            positionText.text = string.Format(Translator.GetString("ToolboxPositionValue"), position.x, position.y);
        }
    }

    private static bool CanShow(HudManager hud)
    {
        if (!Main.EnableHudToolbox.Value || !Options.IsLoaded || hud == null || !hud.gameObject.activeInHierarchy ||
            hud.GameMenu == null || hud.GameMenu.DisableMouseMovement == null || hud.GameMenu.DisableMouseMovement.Text == null ||
            hud.UICamera == null || AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null ||
            (!GameStates.IsLobby && !GameStates.IsInGame)) return false;
        return true;
    }

    private static void Position(HudManager hud)
    {
        var camera = hud.UICamera;
        var safe = Screen.safeArea;
        var viewport = camera.pixelRect;
        float left = Mathf.Max(safe.xMin, viewport.xMin), right = Mathf.Min(safe.xMax, viewport.xMax);
        float bottom = Mathf.Max(safe.yMin, viewport.yMin), top = Mathf.Min(safe.yMax, viewport.yMax);
        if (right <= left || top <= bottom) return;
        float depth = camera.WorldToScreenPoint(hud.transform.position).z;
        var lower = camera.ScreenToWorldPoint(new Vector3(left, bottom, depth));
        var upper = camera.ScreenToWorldPoint(new Vector3(right, top, depth));
        float width = upper.x - lower.x, height = upper.y - lower.y;
        string currentMenu = ControllerManager.Instance?.CurrentUiState?.MenuName;
        if (Time.unscaledTime >= nextDepthProbe || currentMenu != depthMenu)
        {
            nextDepthProbe = Time.unscaledTime + 0.2f;
            depthMenu = currentMenu;
            panelDepth = hud.transform.position.z - 20f;
            var buttonManager = DestroyableSingleton<PassiveButtonManager>.Instance;
            if (buttonManager != null)
            {
                foreach (var button in buttonManager.Buttons)
                {
                    if (button == null || !button.isActiveAndEnabled || button.transform == entry.transform ||
                        (panel != null && button.transform.IsChildOf(panel.transform))) continue;
                    panelDepth = Mathf.Min(panelDepth, button.transform.position.z - 4f);
                }
            }
        }
        float nearDepth = camera.transform.TransformPoint(new Vector3(0f, 0f, camera.nearClipPlane)).z;
        float depthInView = Mathf.Max(panelDepth, nearDepth + 4f);
        entry.transform.position = new Vector3((lower.x + upper.x) * 0.5f, upper.y - 0.30f, depthInView - 3.5f);
        if (panel == null) return;
        float scale = Mathf.Min(1f, (width - 0.16f) / 6.4f, (height - 0.16f) / 4.6f);
        panel.transform.position = new Vector3((lower.x + upper.x) * 0.5f, (lower.y + upper.y) * 0.5f, depthInView);
        panel.transform.localScale = new Vector3(scale, scale, 1f);
        var blockerSize = new Vector2(Mathf.Max(30f, width / scale + 2f), Mathf.Max(20f, height / scale + 2f));
        blocker.size = blockerSize;
    }

    private static void Open(HudManager hud)
    {
        if (panel != null || !CanShow(hud)) return;
        panelClient = AmongUsClient.Instance;
        panelOwner = PlayerControl.LocalPlayer;
        panelMeeting = MeetingHud.Instance;
        panelLobby = GameStates.IsLobby;
        section = panelLobby ? Section.Lobby : panelMeeting != null ? Section.Meeting : Section.Diagnostics;
        pending = ActionKind.None;
        logResult = null;
        var template = hud.GameMenu.DisableMouseMovement.Text;
        visuals = new MenuVisuals(template);
        panel = new GameObject(OverlayName);
        panel.transform.SetParent(hud.transform, false);
        panel.transform.localPosition = new Vector3(0f, 0f, -20f);
        panel.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 32000;
        var blockerObject = new GameObject("ClickBlocker");
        blockerObject.SetActive(false);
        blockerObject.transform.SetParent(panel.transform, false);
        blockerObject.transform.localPosition = new Vector3(0f, 0f, 1f);
        blocker = blockerObject.AddComponent<BoxCollider2D>();
        blocker.size = new Vector2(30f, 20f);
        var blockerButton = blockerObject.AddComponent<PassiveButton>();
        blockerButton.OnMouseOver = new();
        blockerButton.OnMouseOut = new();
        blockerButton.OnClick = new();
        blockerObject.SetActive(true);
        var background = new GameObject("Background");
        background.transform.SetParent(panel.transform, false);
        var surface = background.AddComponent<SpriteRenderer>();
        surface.sprite = PresetSelectorVisuals.Panel;
        surface.drawMode = SpriteDrawMode.Sliced;
        surface.size = new Vector2(6.4f, 4.6f);
        visuals.Label(panel.transform, "Title", Translator.GetString("ToolboxTitle"), new Vector3(-1.2f, 1.9f, -2f), new Vector2(3.5f, 0.45f), 2.7f, TextAlignmentOptions.Left);
        close = visuals.Button(panel.transform, "Close", new Vector3(2.68f, 1.88f, -3f), new Vector2(0.55f, 0.45f), "X", Close);
        visuals.Surface(panel.transform, "HeaderDivider", new Vector3(0f, 1.57f, -1f), new Vector2(5.85f, 0.018f), DividerColor);
        Position(hud);
        Render();
    }

    internal static void Close()
    {
        if (panel is null) return;
        if (panel != null) panel.SetActive(false);
        if (ControllerManager.Instance != null && ControllerManager.Instance.IsMenuActiveAtAll(OverlayName))
            ControllerManager.Instance.CloseOverlayMenu(OverlayName);
        if (panel != null)
        {
            Object.Destroy(panel);
        }
        panel = content = null;
        blocker = null;
        positionText = null;
        panelClient = null;
        panelOwner = null;
        panelMeeting = null;
        pendingStartManager = null;
        pending = ActionKind.None;
        logResult = null;
    }

    private static bool CurrentContext() => panel != null && panelClient == AmongUsClient.Instance &&
        panelOwner == PlayerControl.LocalPlayer && panelMeeting == MeetingHud.Instance && panelLobby == GameStates.IsLobby && CanShow(ownerHud);

    private static int Availability()
    {
        int value = 0;
        if (CanExecute(ActionKind.Settle)) value |= 1;
        if (CanExecute(ActionKind.ForceSkip)) value |= 2;
        if (CanExecute(ActionKind.StartGameNow)) value |= 4;
        if (CanExecute(ActionKind.CancelCountdown)) value |= 8;
        if (CanExecute(ActionKind.CrewWin)) value |= 16;
        if (Options.NoGameEnd.GetBool()) value |= 32;
        if (CanExecute(ActionKind.StartMeeting)) value |= 64;
        return value;
    }

    private static bool CanExecute(ActionKind action)
    {
        if (!CurrentContext()) return false;
        if (action == ActionKind.ExportLog) return true;
        if (action == ActionKind.ShowChat) return ownerHud.Chat != null;
        if (!AmongUsClient.Instance.AmHost) return false;
        if (action is ActionKind.CrewWin or ActionKind.ImpostorWin) return GameEndCheckerForNormal.CanEndWithTeam();
        if (action == ActionKind.StartMeeting)
            return GameStates.IsInGame && !GameStates.IsEnded && !GameStates.IsHideNSeek && !MeetingHud.Instance && !Main.MeetingIsStarted &&
                !ownerHud.IsIntroDisplayed && ShipStatus.Instance != null && !RoleDistribution.BlocksHostGameEnd;
        if (action is ActionKind.Settle or ActionKind.ForceSkip)
            return GameStates.IsMeeting && (MeetingHud.Instance.state is MeetingHud.MeetingStates.Discussion or MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted) &&
                (action != ActionKind.ForceSkip || !GameStates.IsHideNSeek);
        if (!GameStates.IsLobby || GameStartManager.Instance == null) return false;
        if (action == ActionKind.CancelCountdown) return GameStates.IsCountDown;
        var start = GameStartManager.Instance;
        return action == ActionKind.StartGameNow && (GameStates.IsCountDown ||
            (start.startState == GameStartManager.StartingStates.NotStarting && start.StartButton != null &&
             start.StartButton.isActiveAndEnabled && GameData.Instance != null && GameData.Instance.PlayerCount >= start.MinPlayers));
    }

    private static bool PendingValid() => pending != ActionKind.None && CanExecute(pending) &&
        (panelMeeting == null || panelMeeting.state == pendingMeetingState) &&
        (pending is not (ActionKind.StartGameNow or ActionKind.CancelCountdown) ||
         (pendingStartManager != null && pendingStartManager == GameStartManager.Instance && pendingStartManager.startState == pendingStartState));

    private static void Choose(ActionKind action)
    {
        if (!CanExecute(action)) return;
        if (Main.HudToolboxInstantActions.Value) { Execute(action); return; }
        pending = action;
        if (panelMeeting != null) pendingMeetingState = panelMeeting.state;
        pendingStartManager = GameStartManager.Instance;
        if (pendingStartManager != null) pendingStartState = pendingStartManager.startState;
        Render();
    }

    private static void Confirm()
    {
        if (!PendingValid()) { pending = ActionKind.None; Render(); return; }
        var action = pending;
        pending = ActionKind.None;
        Execute(action);
    }

    private static void Execute(ActionKind action)
    {
        if (!CanExecute(action)) { Render(); return; }
        if (action == ActionKind.ExportLog)
        {
            try { logResult = string.Format(Translator.GetString("ToolboxLogSaved"), Utils.DumpLog(false)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Logger.Exception(ex, "ToolboxExportLog");
                logResult = string.Format(Translator.GetString("ToolboxLogFailed"), ex.Message);
            }
            Render();
            return;
        }
        Close();
        switch (action)
        {
            case ActionKind.StartMeeting: PlayerControl.LocalPlayer.NoCheckStartMeeting(null, force: true); break;
            case ActionKind.Settle: CheckForEndVotingPatch.CountVotes(MeetingHud.Instance, true); break;
            case ActionKind.ForceSkip: ControllerManagerUpdatePatch.ForceSkipMeeting(MeetingHud.Instance); break;
            case ActionKind.StartGameNow:
                if (!GameStates.IsCountDown) GameStartManager.Instance.StartButton.ReceiveClickDown();
                if (GameStates.IsCountDown) ControllerManagerUpdatePatch.FinishCountdown();
                break;
            case ActionKind.CancelCountdown: ControllerManagerUpdatePatch.CancelCountdown(); break;
            case ActionKind.ShowChat: ownerHud.Chat.SetVisible(true); break;
            case ActionKind.CrewWin: GameEndCheckerForNormal.EndWithTeam(CustomWinner.Crewmate); break;
            case ActionKind.ImpostorWin: GameEndCheckerForNormal.EndWithTeam(CustomWinner.Impostor); break;
        }
    }

    private static void Render()
    {
        if (panel == null) return;
        if (content != null) { content.SetActive(false); Object.Destroy(content); }
        positionText = null;
        content = new GameObject("Content");
        content.transform.SetParent(panel.transform, false);
        availability = Availability();
        PassiveButton back = close, selected = close;
        if (logResult != null)
        {
            var heading = visuals.Label(content.transform, "LogTitle", Translator.GetString("ToolboxExportLog"), new Vector3(0f, 0.85f, -2f), new Vector2(5.3f, 0.5f), 2.4f, TextAlignmentOptions.Center);
            heading.color = CaptionColor;
            var result = visuals.Label(content.transform, "LogResult", logResult, new Vector3(0f, 0.05f, -2f), new Vector2(5.15f, 1.2f), 1.7f, TextAlignmentOptions.Center);
            result.enableWordWrapping = true;
            result.fontSizeMin = 0.7f;
            back = visuals.Button(content.transform, "Back", new Vector3(0f, -1.68f, -3f), new Vector2(1.7f, 0.48f), Translator.GetString("ToolboxBack"), () => { logResult = null; Render(); });
            selected = back;
        }
        else if (pending != ActionKind.None)
        {
            string key = pending switch
            {
                ActionKind.StartMeeting => "ToolboxStartMeeting",
                ActionKind.Settle => "ToolboxSettle",
                ActionKind.ForceSkip => "ToolboxForceSkip",
                ActionKind.StartGameNow => "ToolboxStartGameNow",
                ActionKind.CancelCountdown => "ToolboxCancelCountdown",
                ActionKind.ExportLog => "ToolboxExportLog",
                ActionKind.ShowChat => "ToolboxShowChat",
                ActionKind.CrewWin => "ToolboxCrewWin",
                _ => "ToolboxImpostorWin"
            };
            var heading = visuals.Label(content.transform, "ConfirmationTitle", Translator.GetString(key), new Vector3(0f, 0.85f, -2f), new Vector2(5.3f, 0.5f), 2.4f, TextAlignmentOptions.Center);
            heading.color = CaptionColor;
            visuals.Label(content.transform, "Consequence", Translator.GetString(key + "Effect"), new Vector3(0f, 0.05f, -2f), new Vector2(5.15f, 0.9f), 1.9f, TextAlignmentOptions.Center);
            back = visuals.Button(content.transform, "Cancel", new Vector3(-1.15f, -1.68f, -3f), new Vector2(1.7f, 0.48f), Translator.GetString("ToolboxCancel"), () => { pending = ActionKind.None; Render(); });
            selected = back;
            var confirm = visuals.Button(content.transform, "Confirm", new Vector3(1.15f, -1.68f, -3f), new Vector2(1.7f, 0.48f), Translator.GetString("ToolboxConfirm"), Confirm);
            if (pending == ActionKind.ForceSkip) confirm.GetComponent<SpriteRenderer>().color = new Color32(143, 73, 75, 255);
        }
        else
        {
            var sections = panelLobby
                ? new[] { Section.Lobby, Section.Diagnostics, Section.Debug }
                : new[] { Section.Meeting, Section.Diagnostics, Section.Debug };
            float sectionWidth = 5.8f / sections.Length;
            for (int i = 0; i < sections.Length; i++)
            {
                var item = sections[i];
                var tab = visuals.Button(content.transform, "Tab" + item, new Vector3(-2.9f + sectionWidth * (i + 0.5f), 1.19f, -3f), new Vector2(sectionWidth - 0.16f, 0.42f),
                    Translator.GetString("Toolbox" + item), () => { section = item; Render(); });
                if (item == section) tab.GetComponent<SpriteRenderer>().color = new Color32(88, 130, 133, 255);
            }
            if (section == Section.Meeting)
            {
                ActionRow(ActionKind.StartMeeting, "ToolboxStartMeeting", 0.67f, compact: true);
                ActionRow(ActionKind.Settle, "ToolboxSettle", -0.13f, compact: true);
                ActionRow(ActionKind.ForceSkip, "ToolboxForceSkip", -0.93f, compact: true);
            }
            else if (section == Section.Lobby)
            {
                ActionRow(ActionKind.StartGameNow, "ToolboxStartGameNow", 0.52f);
                ActionRow(ActionKind.CancelCountdown, "ToolboxCancelCountdown", -0.53f);
            }
            else if (section == Section.Diagnostics)
            {
                var caption = visuals.Label(content.transform, "PositionCaption", Translator.GetString("ToolboxPosition"), new Vector3(-1.95f, 0.76f, -2f), new Vector2(1.8f, 0.35f), 1.6f, TextAlignmentOptions.Left);
                caption.color = CaptionColor;
                positionText = visuals.Label(content.transform, "PositionValue", "", new Vector3(0.9f, 0.76f, -2f), new Vector2(3.7f, 0.4f), 1.7f, TextAlignmentOptions.Left);
                var buildCaption = visuals.Label(content.transform, "BuildCaption", Translator.GetString("ToolboxBuild"), new Vector3(-1.95f, 0.22f, -2f), new Vector2(1.8f, 0.35f), 1.6f, TextAlignmentOptions.Left);
                buildCaption.color = CaptionColor;
                visuals.Label(content.transform, "BuildValue", $"TOHEE {Main.PluginDisplayVersion}\nAmong Us {Application.version}\n{Translator.GetString("ToolboxNetworkVersion")} {Constants.Year}.{Constants.Month}.{Constants.Day}", new Vector3(0.9f, 0.22f, -2f), new Vector2(3.7f, 0.72f), 1.5f, TextAlignmentOptions.Left);
                ActionRow(ActionKind.ExportLog, "ToolboxExportLog", -0.52f, compact: true);
                var diagnosticLabel = visuals.Label(content.transform, "HudDiagnosticsLabel", Translator.GetString("ToolboxShowHudDiagnostics"), new Vector3(-0.85f, -1.32f, -2f), new Vector2(3.9f, 0.4f), 1.6f, TextAlignmentOptions.Left);
                diagnosticLabel.color = CaptionColor;
                visuals.Checkbox(content.transform, "HudDiagnostics", new Vector3(2.25f, -1.32f, -3f), Main.ShowHudDiagnostics.Value, true,
                    () => { Main.ShowHudDiagnostics.Value = !Main.ShowHudDiagnostics.Value; Render(); });
            }
            else
            {
                ActionRow(ActionKind.ShowChat, "ToolboxShowChat", 0.60f, compact: true);
                if (GameStates.IsInGame)
                {
                    visuals.Surface(content.transform, "EndGameRow", new Vector3(0f, -0.19f, -0.5f), new Vector2(5.8f, 0.74f), new Color(0.12f, 0.19f, 0.21f, 0.6f));
                    var endTitle = visuals.Label(content.transform, "EndGameTitle", Translator.GetString("ToolboxEndGame"), new Vector3(-1.25f, 0.01f, -2f), new Vector2(3.3f, 0.3f), 1.65f, TextAlignmentOptions.Left);
                    endTitle.color = CaptionColor;
                    EndGameButton(ActionKind.CrewWin, "ToolboxCrewWin", -1.42f);
                    EndGameButton(ActionKind.ImpostorWin, "ToolboxImpostorWin", 1.42f);
                    visuals.Surface(content.transform, "NoGameEndRow", new Vector3(0f, -0.96f, -0.5f), new Vector2(5.8f, 0.56f), new Color(0.12f, 0.19f, 0.21f, 0.6f));
                    var noEndLabel = visuals.Label(content.transform, "NoGameEndLabel", Translator.GetString("ToolboxNoGameEnd"), new Vector3(-0.85f, -0.96f, -2f), new Vector2(3.9f, 0.4f), 1.6f, TextAlignmentOptions.Left);
                    noEndLabel.color = CaptionColor;
                    visuals.Checkbox(content.transform, "NoGameEndToggle", new Vector3(2.25f, -0.96f, -3f), Options.NoGameEnd.GetBool(), AmongUsClient.Instance.AmHost, () =>
                    {
                        if (!CurrentContext() || !GameStates.IsInGame || !AmongUsClient.Instance.AmHost) return;
                        Options.NoGameEnd.SetValue(Options.NoGameEnd.GetBool() ? 0 : 1, doSave: false, doSync: true);
                        Render();
                    });
                }
                var toggleLabel = visuals.Label(content.transform, "InstantCaption", Translator.GetString("ToolboxInstantActions"), new Vector3(-0.9f, -1.79f, -2f), new Vector2(4.0f, 0.42f), 1.55f, TextAlignmentOptions.Left);
                toggleLabel.color = CaptionColor;
                visuals.Checkbox(content.transform, "InstantActions", new Vector3(2.45f, -1.79f, -3f), Main.HudToolboxInstantActions.Value, true,
                    () => { Main.HudToolboxInstantActions.Value = !Main.HudToolboxInstantActions.Value; Render(); });
            }
            if (section is Section.Meeting or Section.Lobby or Section.Debug)
            {
                string status = !AmongUsClient.Instance.AmHost && section != Section.Debug ? "ToolboxHostOnly"
                    : section == Section.Meeting && (availability & 1) == 0 ? "ToolboxNoMeeting"
                    : section == Section.Meeting && (availability & 2) == 0 ? "ToolboxForceUnavailable"
                    : section == Section.Lobby && (availability & 4) == 0 ? "ToolboxStartUnavailable"
                    : section == Section.Lobby && (availability & 8) == 0 ? "ToolboxNoCountdown"
                    : section == Section.Debug && GameStates.IsInGame && !AmongUsClient.Instance.AmHost ? "ToolboxHostOnly"
                    : section == Section.Debug && GameStates.IsInGame && (availability & 16) == 0 ? "ToolboxEndUnavailable" : null;
                if (status != null)
                {
                    var hint = visuals.Label(content.transform, "Availability", Translator.GetString(status), new Vector3(0f, section is Section.Debug or Section.Meeting ? -1.46f : -1.22f, -2f), new Vector2(5.6f, 0.24f), 1.3f, TextAlignmentOptions.Center);
                    hint.color = CaptionColor;
                }
            }
        }
        foreach (var item in panel.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = ownerHud.GameMenu.DisableMouseMovement.Text.gameObject.layer;
        foreach (var renderer in panel.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.sortingOrder = renderer.gameObject.name == "Background" ? 1 :
                renderer.gameObject.name == "Checkmark" ? 5 : renderer.gameObject.name == "Box" ? 4 :
                renderer.GetComponent<PassiveButton>() != null ? 3 : 2;
        foreach (var label in panel.GetComponentsInChildren<TextMeshPro>(true)) label.sortingOrder = 6;
        var selectable = new Il2CppSystem.Collections.Generic.List<UiElement>();
        foreach (var button in panel.GetComponentsInChildren<PassiveButton>(false))
            if (button != null && button.isActiveAndEnabled && button.name != "ClickBlocker" && button.GetComponent<Collider2D>().enabled) selectable.Add(button);
        if (ControllerManager.Instance != null)
        {
            if (ControllerManager.Instance.IsMenuActiveAtAll(OverlayName)) ControllerManager.Instance.CloseOverlayMenu(OverlayName);
            ControllerManager.Instance.OpenOverlayMenu(OverlayName, back, selected, selectable);
        }
    }

    private static void EndGameButton(ActionKind action, string key, float x)
    {
        var button = visuals.Button(content.transform, key + "Button", new Vector3(x, -0.35f, -3f), new Vector2(2.6f, 0.34f), Translator.GetString(key), () => Choose(action));
        if (!CanExecute(action))
        {
            button.GetComponent<BoxCollider2D>().enabled = false;
            button.GetComponent<SpriteRenderer>().color = new Color32(63, 69, 71, 255);
            button.GetComponentInChildren<TextMeshPro>().color = new Color32(155, 163, 163, 255);
        }
    }

    private static void ActionRow(ActionKind action, string key, float y, bool compact = false)
    {
        visuals.Surface(content.transform, key + "Row", new Vector3(0f, y, -0.5f), new Vector2(5.8f, compact ? 0.65f : 0.88f), new Color(0.12f, 0.19f, 0.21f, 0.6f));
        var name = visuals.Label(content.transform, key + "Name", Translator.GetString(key), new Vector3(-1.22f, y + (compact ? 0.13f : 0.19f), -2f), new Vector2(3.25f, 0.3f), 1.75f, TextAlignmentOptions.Left);
        name.color = CaptionColor;
        visuals.Label(content.transform, key + "Description", Translator.GetString(key + "Effect"), new Vector3(-1.22f, y - (compact ? 0.13f : 0.18f), -2f), new Vector2(3.25f, compact ? 0.27f : 0.4f), 1.35f, TextAlignmentOptions.Left);
        var button = visuals.Button(content.transform, key + "Button", new Vector3(1.85f, y, -3f), new Vector2(1.65f, 0.48f),
            Translator.GetString(Main.HudToolboxInstantActions.Value ? "ToolboxRun" : "ToolboxSelect"), () => Choose(action));
        if (!CanExecute(action))
        {
            button.GetComponent<BoxCollider2D>().enabled = false;
            button.GetComponent<SpriteRenderer>().color = new Color32(63, 69, 71, 255);
            button.GetComponentInChildren<TextMeshPro>().color = new Color32(155, 163, 163, 255);
        }
        else if (action == ActionKind.ForceSkip) button.GetComponent<SpriteRenderer>().color = new Color32(143, 73, 75, 255);
    }
}
