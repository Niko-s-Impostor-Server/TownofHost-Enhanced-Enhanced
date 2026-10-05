using System.Text;
using TMPro;
using TOHE.Modules;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE;

[HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
class PingTrackerUpdatePatch
{
    public static PingTracker Instance;
    private static int DelayUpdate = 0;
    private static readonly StringBuilder sb = new();
    private static readonly List<HudStatusRect> layoutObstacles = [];
    private static readonly List<HudStatusRect> previousLayoutObstacles = [];
    private static TextMeshPro measuredText;
    private static string measuredValue;
    private static Vector2 preferredSize;
    private static float measuredFontSize;
    private static string renderedStatus;
    private static TextMeshPro styledText;
    private const float LayoutProbeInterval = 0.1f;
    private static bool layoutCached;
    private static float nextLayoutProbe;
    private static PingTracker layoutTracker;
    private static HudManager layoutHud;
    private static Camera layoutCamera;
    private static PlayerControl layoutOwner;
    private static int layoutOwnerId, layoutScreenWidth, layoutScreenHeight;
    private static MeetingHud layoutMeeting;
    private static Rect layoutScreen, layoutViewport;
    private static string layoutValue;
    private static float layoutFontSize;

    private static bool Prefix(PingTracker __instance)
    {
        try
        {
            Instance ??= __instance;

            DelayUpdate--;

            if (DelayUpdate > 0 && sb.Length > 0)
            {
                ApplyStatusText(__instance);
                return false;
            }

            DelayUpdate = 500;

            sb.Clear();

            sb.Append(Main.credentialsText);

            var ping = AmongUsClient.Instance.Ping;
            string pingcolor = "#ff4500";
            if (ping < 30) pingcolor = "#44dfcc";
            else if (ping < 100) pingcolor = "#7bc690";
            else if (ping < 200) pingcolor = "#f3920e";
            else if (ping < 400) pingcolor = "#ff146e";
            sb.Append($"\r\n<color={pingcolor}>Ping: {ping} ms</color>\r\n<color=#a54aff>Server: <color=#f34c50>{Utils.GetRegionName()}</color>");

            if (!GameStates.IsModHost)
            {
                //CheckIsModHost = true;
                sb.Append($"\r\n{Utils.ColorString(Color.red, GetString("Warning.NoModHost"))}");
            }

            if (Main.ShowFPS.Value)
            {
                var FPSGame = 1.0f / Time.deltaTime;
                Color fpscolor = Color.green;

                if (FPSGame < 20f) fpscolor = Color.red;
                else if (FPSGame < 40f) fpscolor = Color.yellow;

                sb.Append($"\r\n{Utils.ColorString(fpscolor, Utils.ColorString(Color.cyan, GetString("FPSGame")) + ((int)FPSGame).ToString())}");
            }

            if (Main.ShowTextOverlay.Value)
            {
                var sbOverlay = new StringBuilder();
                if (Options.LowLoadMode.GetBool()) sbOverlay.Append($"\r\n{Utils.ColorString(Color.green, GetString("Overlay.LowLoadMode"))}");
                if (Options.NoGameEnd.GetBool()) sbOverlay.Append($"\r\n{Utils.ColorString(Color.red, GetString("Overlay.NoGameEnd"))}");
                if (Options.GuesserMode.GetBool()) sbOverlay.Append($"\r\n{Utils.ColorString(Color.yellow, GetString("Overlay.GuesserMode"))}");
                if (Options.AllowConsole.GetBool() && PlayerControl.LocalPlayer.FriendCode.GetDevUser().DeBug) sbOverlay.Append($"\r\n{Utils.ColorString(Color.red, GetString("Overlay.AllowConsole"))}");
                if (DebugModeManager.IsDebugMode) sbOverlay.Append($"\r\n{Utils.ColorString(Color.green, GetString("Overlay.DebugMode"))}");

                if (sbOverlay.Length > 0)
                    sb.Append(sbOverlay);
            }

            renderedStatus = sb.ToString();
            ApplyStatusText(__instance, refreshStyle: true);

            return false;
        }
        catch
        {
            DelayUpdate = 0;
            sb.Clear();
            layoutCached = false;

            return false;
        }
    }
    private static void ApplyStatusText(PingTracker tracker, bool refreshStyle = false)
    {
        if (!tracker || !tracker.text) return;
        if (refreshStyle || styledText != tracker.text)
        {
            ChangeText(tracker);
            styledText = tracker.text;
        }
        // The cached branch neither allocates a new StringBuilder string nor
        // reapplies TMP properties on every frame. External text changes still repair immediately.
        if (tracker.text.text != renderedStatus) tracker.text.text = renderedStatus;
        PositionStatusText(tracker);
    }
    private static void PositionStatusText(PingTracker tracker)
    {
        if (!tracker || !tracker.text || !HudManager.InstanceExists) return;
        var hud = HudManager.Instance;
        var camera = hud.UICamera;
        if (!camera) camera = tracker.aspectPosition ? tracker.aspectPosition.parentCam : Camera.main;
        if (!camera) return;
        var screen = Screen.safeArea;
        var viewport = camera.pixelRect;
        var text = tracker.text;
        var value = text.text;
        float fontSize = text.fontSize;
        var owner = PlayerControl.LocalPlayer;
        int ownerId = owner ? owner.OwnerId : -1;
        var meeting = MeetingHud.Instance;
        int screenWidth = Screen.width, screenHeight = Screen.height;
        bool dirty = !layoutCached || layoutTracker != tracker || layoutHud != hud || layoutCamera != camera ||
            layoutOwner != owner || layoutOwnerId != ownerId || layoutMeeting != meeting ||
            layoutScreenWidth != screenWidth || layoutScreenHeight != screenHeight ||
            layoutScreen != screen || layoutViewport != viewport || layoutValue != value || layoutFontSize != fontSize;
        float now = Time.realtimeSinceStartup;
        if (!dirty && now < nextLayoutProbe) return;

        // Resolution/safe-area/text/owner changes bypass the timer. Stable UI
        // probes dynamic toolbar/task/meeting bounds at most ten times per second.
        layoutCached = true;
        nextLayoutProbe = now + LayoutProbeInterval;
        layoutTracker = tracker;
        layoutHud = hud;
        layoutCamera = camera;
        layoutOwner = owner;
        layoutOwnerId = ownerId;
        layoutMeeting = meeting;
        layoutScreenWidth = screenWidth;
        layoutScreenHeight = screenHeight;
        layoutScreen = screen;
        layoutViewport = viewport;
        layoutValue = value;
        layoutFontSize = fontSize;
        var safe = new HudStatusRect(Mathf.Max(screen.xMin, viewport.xMin), Mathf.Max(screen.yMin, viewport.yMin),
            Mathf.Min(screen.xMax, viewport.xMax), Mathf.Min(screen.yMax, viewport.yMax));
        if (safe.Width <= 0 || safe.Height <= 0) return;

        layoutObstacles.Clear();
        AddStatusObstacles(hud.SettingsButton, camera);
        if (hud.MatchInfoButton) AddStatusObstacles(hud.MatchInfoButton.gameObject, camera);
        if (hud.MapButton) AddStatusObstacles(hud.MapButton.gameObject, camera);
        if (hud.Chat && hud.Chat.chatButton) AddStatusObstacles(hud.Chat.chatButton.gameObject, camera);
        AddStatusObstacles(hud.TaskStuff, camera);
        if (hud.TaskPanel) AddStatusObstacles(hud.TaskPanel.gameObject, camera);
        if (LobbySettingsPreview.Text) AddStatusObstacles(LobbySettingsPreview.Text.gameObject, camera);
        if (meeting)
        {
            if (meeting.TitleText) AddStatusObstacles(meeting.TitleText.gameObject, camera);
            if (meeting.TimerText) AddStatusObstacles(meeting.TimerText.gameObject, camera);
            foreach (var area in meeting.playerStates)
                if (area) AddStatusObstacles(area.gameObject, camera);
        }

        bool moved = layoutObstacles.Count != previousLayoutObstacles.Count;
        if (!moved)
        {
            for (int index = 0; index < layoutObstacles.Count; index++)
                if (layoutObstacles[index] != previousLayoutObstacles[index]) { moved = true; break; }
        }
        if (!dirty && !moved) return;
        previousLayoutObstacles.Clear();
        previousLayoutObstacles.AddRange(layoutObstacles);

        bool newText = measuredText != text || measuredValue != text.text || measuredFontSize != text.fontSize;
        if (newText)
        {
            measuredText = text;
            measuredValue = text.text;
            measuredFontSize = text.fontSize;
            preferredSize = text.GetPreferredValues(text.text, float.PositiveInfinity, float.PositiveInfinity);
        }
        var preferred = preferredSize;
        bool changedLayout = newText || text.enableWordWrapping || text.rectTransform.sizeDelta != preferred;
        text.enableWordWrapping = false;
        text.rectTransform.sizeDelta = preferred;
        if (changedLayout) text.ForceMeshUpdate();
        var renderer = text.GetComponent<Renderer>();
        if (!renderer) return;
        var bounds = StatusScreenBounds(renderer.bounds, camera);
        float lineHeight = bounds.Height / Mathf.Max(1, text.textInfo.lineCount);
        float gap = Mathf.Max(2f, lineHeight * 0.35f);
        if (!HudStatusLayout.TryPlace(safe, bounds.Width, bounds.Height, layoutObstacles, gap, out var position))
        {
            // A narrow viewport can need wrapping rather than moving over task/vote UI.
            // Keep the font size; progressively reduce the block width to find a free column.
            for (int step = 1; step <= 8; step++)
            {
                text.enableWordWrapping = true;
                float width = preferred.x * (1f - step / 10f);
                var wrapped = text.GetPreferredValues(text.text, width, float.PositiveInfinity);
                text.rectTransform.sizeDelta = new Vector2(width, wrapped.y);
                text.ForceMeshUpdate();
                bounds = StatusScreenBounds(renderer.bounds, camera);
                if (HudStatusLayout.TryPlace(safe, bounds.Width, bounds.Height, layoutObstacles, gap, out position)) break;
                if (step == 8) return;
            }
        }
        if (tracker.aspectPosition) tracker.aspectPosition.enabled = false;
        // Use the rendered glyph bounds, rather than assuming TMP pivot/parent scaling.
        var origin = camera.WorldToScreenPoint(text.transform.position);
        text.transform.position = camera.ScreenToWorldPoint(new Vector3(
            origin.x + position.Right - bounds.Right, origin.y + position.Top - bounds.Top, origin.z));
    }

    private static void AddStatusObstacles(GameObject gameObject, Camera camera)
    {
        if (!gameObject || !gameObject.activeInHierarchy) return;
        bool found = false;
        HudStatusRect combined = default;
        foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>())
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            var bounds = StatusScreenBounds(renderer.bounds, camera);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;
            combined = found ? new HudStatusRect(Mathf.Min(combined.Left, bounds.Left), Mathf.Min(combined.Bottom, bounds.Bottom),
                Mathf.Max(combined.Right, bounds.Right), Mathf.Max(combined.Top, bounds.Top)) : bounds;
            found = true;
        }
        // Treat each control/panel as one obstacle, rather than packing around
        // hundreds of individual label/icon meshes during a full meeting.
        if (found) layoutObstacles.Add(combined);
    }

    private static HudStatusRect StatusScreenBounds(Bounds bounds, Camera camera)
    {
        var lower = camera.WorldToScreenPoint(bounds.min);
        var upper = camera.WorldToScreenPoint(bounds.max);
        return new HudStatusRect(Mathf.Min(lower.x, upper.x), Mathf.Min(lower.y, upper.y),
            Mathf.Max(lower.x, upper.x), Mathf.Max(lower.y, upper.y));
    }
    private static void ChangeText(PingTracker __instance)
    {
        __instance.text.alignment = TextAlignmentOptions.Right;
        __instance.text.outlineColor = Color.black;

        if (Main.ShowTextOverlay.Value || Main.ShowFPS.Value)
        {
            var language = DestroyableSingleton<TranslationController>.Instance.currentLanguage.languageID;
            __instance.text.outlineWidth = language switch
            {
                SupportedLangs.Russian or SupportedLangs.Japanese or SupportedLangs.SChinese or SupportedLangs.TChinese => 0.25f,
                _ => 0.40f,
            };
        }
        else
        {
            __instance.text.outlineWidth = 0.40f;
        }
    }
}
[HarmonyPatch(typeof(VersionShower), nameof(VersionShower.Start))]
class VersionShowerStartPatch
{
    static TextMeshPro SpecialEventText;
    private static TextMeshPro credentials;
    private static void Postfix(VersionShower __instance)
    {
        Main.credentialsText = $"<size=70%><size=85%><color={Main.ModColor}>{Main.ModName}</color> v{Main.PluginDisplayVersion}</size>";
        var buildtype = "";

#if RELEASE
            Main.credentialsText += $"\r\n<color=#a54aff>By <color=#f34c50>The Enhanced Network</color></color>";
            buildtype = "Release";
#endif

#if CANARY
        Main.credentialsText += $"\r\n<color=#ffc0cb>Canary:</color><color=#f34c50>{ThisAssembly.Git.Branch}</color>(<color=#ffc0cb>{ThisAssembly.Git.Commit}</color>)";
        Main.credentialsText += $"\r\n<color=#a54aff>By <color=#f34c50>The Enhanced Network</color></color>";
        buildtype = "Canary";
#endif

#if DEBUG
        Main.credentialsText += $"\r\n<color=#ffc0cb>Debug:</color><color=#f34c50>{ThisAssembly.Git.Branch}</color>(<color=#ffc0cb>{ThisAssembly.Git.Commit}</color>)";
        Main.credentialsText += $"\r\n<color=#a54aff>By <color=#f34c50>The Enhanced Network</color></color>";
        buildtype = "Debug";
#endif
        Main.credentialsText += "</size>";
        Logger.Info($"v{Main.PluginVersion}, {buildtype}:{ThisAssembly.Git.Branch}:({ThisAssembly.Git.Commit}), link [{ThisAssembly.Git.RepositoryUrl}], dirty: [{ThisAssembly.Git.IsDirty}]", "TOHE version");

        if (Main.IsAprilFools)
            Main.credentialsText = $"<color=#00bfff>Town Of Host</color> v11.45.14";

        if (credentials)
        {
            credentials.gameObject.SetActive(false);
            Object.Destroy(credentials.gameObject);
        }
        // Preserve the existing footer anchor. Its child panel follows the
        // label and is destroyed with it when the menu scene is unloaded.
        credentials = Object.Instantiate(__instance.text);
        credentials.name = "TOHEECredentials";
        credentials.text = Main.credentialsText;
        credentials.alignment = TextAlignmentOptions.Bottom;
        credentials.rectTransform.pivot = new Vector2(0.5f, 0f);
        credentials.rectTransform.sizeDelta = new Vector2(4.8f, 0.9f);
        credentials.fontSize = credentials.fontSizeMax = credentials.fontSizeMin = 2f;
        // Keep the title artwork and account bar clear; the center footer is unused.
        var position = credentials.GetComponent<AspectPosition>() ?? credentials.gameObject.AddComponent<AspectPosition>();
        position.Alignment = AspectPosition.EdgeAlignments.Bottom;
        position.DistanceFromEdge = new Vector3(0f, 0.1f, __instance.text.transform.position.z);
        position.AdjustPosition();
        if (__instance.gameObject.scene.name == "MainMenu") AddCredentialsBackground(credentials);

        ErrorText.Create(__instance.text);
        if (Main.hasArgumentException && ErrorText.Instance != null)
        {
            ErrorText.Instance.AddError(ErrorCode.Main_DictionaryError);
        }

        VersionChecker.Check();

        if (SpecialEventText == null && MainMenuManagerStartPatch.ToheLogo != null)
        {
            SpecialEventText = Object.Instantiate(__instance.text, MainMenuManagerStartPatch.ToheLogo.transform);
            SpecialEventText.name = "SpecialEventText";
            SpecialEventText.text = "";
            SpecialEventText.color = Color.white;
            SpecialEventText.fontSizeMin = 3f;
            SpecialEventText.alignment = TextAlignmentOptions.Center;
            SpecialEventText.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        }
        if (SpecialEventText != null)
        {
            SpecialEventText.enabled = MainMenuManagerStartPatch.amongUsLogo != null;
        }
        if (Main.IsInitialRelease && SpecialEventText != null)
        {
            SpecialEventText.text = $"Happy Birthday to {Main.ModName}!";
            if (ColorUtility.TryParseHtmlString(Main.ModColor, out var col))
            {
                SpecialEventText.color = col;
            }
        }
    }

    private static void AddCredentialsBackground(TextMeshPro text)
    {
        var sprite = Utils.LoadSprite("TOHE.Resources.Images.PresetBox.png", 100f);
        var textRenderer = text.GetComponent<Renderer>();
        if (!sprite || !textRenderer) return;
        text.ForceMeshUpdate();
        var bounds = text.textBounds;
        var spriteSize = sprite.bounds.size;
        if (bounds.size.x <= 0f || bounds.size.y <= 0f || spriteSize.x <= 0f || spriteSize.y <= 0f) return;

        var background = new GameObject("TOHEECredentialsBackground");
        background.layer = text.gameObject.layer;
        background.transform.SetParent(text.transform, false);
        // Fit the actual glyph block, including Debug/Canary and author lines,
        // rather than the much wider native version label's RectTransform.
        background.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, 0.02f);
        background.transform.localScale = new Vector3(
            (bounds.size.x + 0.32f) / spriteSize.x,
            (bounds.size.y + 0.16f) / spriteSize.y, 1f);
        var renderer = background.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(0f, 0f, 0f, 0.78f);
        renderer.sortingLayerID = textRenderer.sortingLayerID;
        // Keep the text's UI order; a lower order can put the panel behind the
        // full-screen artwork. The small positive Z offset places it behind text.
        renderer.sortingOrder = textRenderer.sortingOrder;
    }
}
[HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
class ModManagerLateUpdatePatch
{
    public static void Prefix(ModManager __instance)
    {
        __instance.ShowModStamp();

        LateTask.Update(Time.deltaTime);
        CheckMurderPatch.Update();
    }
    public static void Postfix(ModManager __instance)
    {
        var offset_y = HudManager.InstanceExists ? 1.8f : 0.9f;
        __instance.ModStamp.transform.position = AspectPosition.ComputeWorldPosition(
            __instance.localCamera, AspectPosition.EdgeAlignments.RightTop,
            new Vector3(0.4f, offset_y, __instance.localCamera.nearClipPlane + 0.1f));
    }
}
