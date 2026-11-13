using System;
using System.Globalization;
using System.Text;
using TMPro;
using TOHE.Modules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TOHE;

// Owned by the current HUD and updated from its native Update callback.
internal static class DebugShower
{
    private static HudManager owner;
    private static GameObject root;
    private static TextMeshPro rows, footer, positionText;
    private static PassiveButton previous, next;
    private static Camera layoutCamera;
    private static Rect layoutSafeArea, layoutViewport;
    private static int page, pageCount = 1, rowsPerPage = 1;
    private static float rowHeight, width, hitWidth, hitHeight, layoutOrthoSize, refreshAt;
    private static float previousHitWidth, previousHitHeight;
    private static int hitUpdatedFrame = -1;
    private static bool lastDeveloper, layoutLobby;

    internal static void Update(HudManager hud)
    {
        if (Main.ShowHudDiagnostics?.Value != true || hud == null || AmongUsClient.Instance == null ||
            PlayerControl.LocalPlayer == null || (!GameStates.IsInGame && !GameStates.IsLobby))
        {
            if (root != null) root.SetActive(false);
            page = 0;
            refreshAt = 0f;
            return;
        }

        if (owner != hud || root == null)
        {
            Dispose();
            var template = hud.GameMenu ? hud.GameMenu.DisableMouseMovement?.Text : null;
            if (!template || !hud.UICamera) return;
            owner = hud;
            root = new GameObject("TOHEDebugShower");
            root.transform.SetParent(hud.transform, false);
            root.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 99;

            TextMeshPro CreateText(string name, float size)
            {
                var label = Object.Instantiate(template, root.transform);
                label.name = name;
                label.DestroyTranslator();
                var anchor = label.GetComponent<AspectPosition>();
                if (anchor != null) { anchor.enabled = false; Object.Destroy(anchor); }
                label.transform.localPosition = Vector3.zero;
                label.transform.localScale = Vector3.one;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 1f);
                label.rectTransform.pivot = new Vector2(0f, 1f);
                label.margin = Vector4.zero;
                label.alignment = TextAlignmentOptions.TopLeft;
                label.enableWordWrapping = false;
                label.enableAutoSizing = false;
                label.autoSizeTextContainer = false;
                label.fontSize = size;
                label.fontStyle = FontStyles.Bold;
                label.overflowMode = TextOverflowModes.Overflow;
                label.color = Color.white;
                label.outlineColor = Color.black;
                label.outlineWidth = 0.15f;
                label.sortingOrder = 2;
                label.enabled = true;
                label.gameObject.SetActive(true);
                return label;
            }

            rows = CreateText("Rows", 1.25f);
            footer = CreateText("Page", 1.25f);
            positionText = CreateText("LocalPosition", 1.65f);
            if (Application.platform == RuntimePlatform.Android)
            {
                var visuals = new MenuVisuals(template);
                previous = visuals.Button(root.transform, "PreviousPage", Vector3.zero, new Vector2(1.4f, 0.5f),
                    Translator.GetString("PreviousPage"), () => { page = (page + pageCount - 1) % pageCount; refreshAt = 0f; });
                next = visuals.Button(root.transform, "NextPage", Vector3.zero, new Vector2(1.4f, 0.5f),
                    Translator.GetString("NextPage"), () => { page = (page + 1) % pageCount; refreshAt = 0f; });
                foreach (var button in new[] { previous, next })
                {
                    button.GetComponent<SpriteRenderer>().color = Color.clear;
                    button.GetComponentInChildren<TextMeshPro>().sortingOrder = 2;
                }
            }
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = template.gameObject.layer;
            layoutCamera = null;
            refreshAt = 0f;
        }

        if (!CanShow(hud))
        {
            if (root.activeSelf) root.SetActive(false);
            refreshAt = 0f;
            return;
        }
        if (!root.activeSelf) root.SetActive(true);

        var camera = hud.UICamera;
        if (!camera)
        {
            root.SetActive(false);
            return;
        }
        var safe = Screen.safeArea;
        var viewport = camera.pixelRect;
        bool lobby = !GameStates.IsInGame && GameStates.IsLobby;
        if (camera != layoutCamera || safe != layoutSafeArea || viewport != layoutViewport ||
            camera.orthographicSize != layoutOrthoSize || lobby != layoutLobby)
        {
            if (lobby != layoutLobby) page = 0;
            layoutLobby = lobby;
            layoutCamera = camera;
            layoutSafeArea = safe;
            layoutViewport = viewport;
            layoutOrthoSize = camera.orthographicSize;
            float left = Mathf.Max(safe.xMin, viewport.xMin), right = Mathf.Min(safe.xMax, viewport.xMax);
            float bottom = Mathf.Max(safe.yMin, viewport.yMin), top = Mathf.Min(safe.yMax, viewport.yMax);
            if (right <= left || top <= bottom) return;
            float depth = camera.WorldToScreenPoint(hud.transform.position).z;
            var lower = camera.ScreenToWorldPoint(new Vector3(left, bottom, depth));
            var upper = camera.ScreenToWorldPoint(new Vector3(right, top, depth));
            var anchor = camera.ScreenToWorldPoint(new Vector3(lobby ? left + (right - left) * 0.25f : left + 4f, top - 4f, depth));
            // The native task panel leaves its tab visible even when collapsed.
            float taskTabGutter = lobby ? 0f : 0.32f;
            root.transform.position = new Vector3(anchor.x + taskTabGutter, anchor.y - 1.05f, hud.transform.position.z - 10f);
            width = lobby ? (upper.x - lower.x) * 0.5f : Mathf.Min(8.8f, (upper.x - lower.x - taskTabGutter) * 0.92f);
            rows.rectTransform.sizeDelta = new Vector2(width, upper.y - lower.y);
            rowHeight = rows.GetPreferredValues("<line-height=125%>X\nX").y -
                rows.GetPreferredValues("<line-height=125%>X").y;
            rowsPerPage = Mathf.Clamp(Mathf.FloorToInt((upper.y - lower.y - (lobby ? 3.55f : 2.60f)) / Mathf.Max(rowHeight, 0.1f)), 1, 24);
            footer.rectTransform.sizeDelta = new Vector2(width - 1.55f, 0.45f);
            positionText.rectTransform.sizeDelta = new Vector2(width, 0.5f);
            refreshAt = 0f;
        }

        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f && WantsScroll())
        {
            page = (page + (scroll > 0f ? pageCount - 1 : 1)) % pageCount;
            refreshAt = 0f;
        }
        var localPosition = PlayerControl.LocalPlayer.GetTruePosition();
        positionText.text = string.Format(CultureInfo.InvariantCulture, Translator.GetString("DebugShowerPosition"), localPosition.x, localPosition.y);
        bool developer = !lobby && PlayerControl.LocalPlayer.FriendCode.GetDevUser().DeBug;
        if (developer != lastDeveloper || Time.unscaledTime >= refreshAt)
        {
            lastDeveloper = developer;
            refreshAt = Time.unscaledTime + 0.25f;
            var players = Main.AllPlayerControls.Where(player => player != null && player.Data != null && !player.Data.Disconnected)
                .OrderBy(player => player.PlayerId).ToArray();
            pageCount = Math.Max(1, (players.Length + rowsPerPage - 1) / rowsPerPage);
            page = Math.Clamp(page, 0, pageCount - 1);
            int visibleRows = Math.Min(rowsPerPage, players.Length - page * rowsPerPage);
            float contentHeight = visibleRows * rowHeight;
            footer.transform.localPosition = new Vector3(Application.platform == RuntimePlatform.Android ? 1.55f : 0f, -contentHeight - 0.05f, -0.1f);
            positionText.transform.localPosition = new Vector3(0f,
                -contentHeight - (Application.platform == RuntimePlatform.Android ? 0.65f : 0.35f), -0.1f);
            if (previous && next)
            {
                previous.transform.localPosition = new Vector3(0.7f, -contentHeight - 0.28f, -0.1f);
                next.transform.localPosition = new Vector3(3.25f, -contentHeight - 0.28f, -0.1f);
            }
            var text = new StringBuilder("<line-height=125%>");
            float longestRow = 0f;
            foreach (var player in players.Skip(page * rowsPerPage).Take(rowsPerPage))
            {
                var data = player.Data;
                int color = data.DefaultOutfit.ColorId;
                var currentPosition = player.GetTruePosition();
                string name = lobby ? data.PlayerName :
                    Main.AllPlayerNames.TryGetValue(player.PlayerId, out var realName) ? realName : player.GetRealName(clientData: true);
                string colorName = color >= 0 && color < Palette.PlayerColors.Length ? Palette.GetColorName(color) : "?";
                int clientId = player.GetClientId();
                string modded = Translator.GetString(player.AmOwner || Main.playerVersion.ContainsKey(clientId)
                    ? "DebugShowerModded" : "DebugShowerVanilla");
                string coordinates = "(" + currentPosition.x.ToString("F2", CultureInfo.InvariantCulture) + ", " +
                    currentPosition.y.ToString("F2", CultureInfo.InvariantCulture) + ")";
                string[] values;
                if (developer)
                {
                    bool hasState = Main.PlayerStates.TryGetValue(player.PlayerId, out var state);
                    string customRole = hasState && state.MainRole != CustomRoles.NotAssigned ? Utils.GetRoleName(state.MainRole) : "?";
                    string nativeRole = "?";
                    if (player.AmOwner && data.Role != null) nativeRole = data.Role.Role.ToString();
                    else if (AmongUsClient.Instance.AmHost && AntiBlackout.TryGetSelfRoleMask(player.PlayerId, out var mask)) nativeRole = mask.ToString();
                    else if (AmongUsClient.Instance.AmHost && RpcSetRoleReplacer.RoleMap.TryGetValue((player.PlayerId, player.PlayerId), out var view))
                    {
                        if (hasState && state.IsDead)
                        {
                            if (data.IsDead && !GameStates.IsHideNSeek && !GhostRoleView.IsPublicationPending(player.PlayerId))
                                nativeRole = GhostRoleView.ForRecipient(player, player).ToString();
                        }
                        else if (hasState) nativeRole = view.roleType.ToString();
                    }
                    string life = hasState ? Translator.GetString(state.IsDead ? "DebugShowerDead" : "DebugShowerAlive") : "?";
                    values = [player.PlayerId.ToString(), name, colorName, customRole, nativeRole, life, modded, coordinates];
                }
                else values = [player.PlayerId.ToString(), name, colorName, modded, coordinates];

                for (int i = 0; i < values.Length; i++)
                {
                    string raw = (values[i] ?? "?").RemoveHtmlTags();
                    var clean = new StringBuilder(raw.Length);
                    foreach (char character in raw)
                        if (!char.IsControl(character)) clean.Append(character == '<' ? '‹' : character == '>' ? '›' : character);
                    values[i] = clean.ToString().Trim();
                    if (values[i].Length == 0) values[i] = "?";
                }
                string line = string.Join(" ", values);
                if (rows.GetPreferredValues(line).x > width)
                {
                    string original = values[1];
                    var elements = StringInfo.ParseCombiningCharacters(original);
                    int min = 0, max = elements.Length - 1;
                    while (min < max)
                    {
                        int count = (min + max + 1) / 2;
                        values[1] = original[..elements[count]] + "…";
                        if (rows.GetPreferredValues(string.Join(" ", values)).x <= width) min = count;
                        else max = count - 1;
                    }
                    values[1] = original[..elements[min]] + "…";
                    line = string.Join(" ", values);
                }
                float measuredWidth = rows.GetPreferredValues(line).x;
                if (measuredWidth > width)
                {
                    int size = Mathf.Clamp(Mathf.FloorToInt(width * 100f / measuredWidth), 1, 99);
                    line = $"<size={size}%>{line}</size>";
                    measuredWidth = rows.GetPreferredValues(line).x;
                }
                longestRow = Mathf.Max(longestRow, measuredWidth);
                text.Append(line).Append('\n');
            }
            rows.text = text.ToString();
            rows.rectTransform.sizeDelta = new Vector2(Mathf.Min(width, Mathf.Max(0.1f, longestRow + 0.05f)), rows.rectTransform.sizeDelta.y);
            footer.text = Application.platform == RuntimePlatform.Android
                ? $"({page + 1}/{pageCount})" : $"{Translator.GetString("DebugShowerPageHint")} ({page + 1}/{pageCount})";
            if (hitUpdatedFrame != Time.frameCount)
            {
                previousHitWidth = hitWidth;
                previousHitHeight = hitHeight;
                hitUpdatedFrame = Time.frameCount;
            }
            hitWidth = Mathf.Min(width, Mathf.Max(longestRow, footer.GetPreferredValues(footer.text).x,
                positionText.GetPreferredValues(positionText.text).x) + 0.1f);
            hitHeight = contentHeight + 0.95f;
            if (previous && next)
            {
                previous.gameObject.SetActive(pageCount > 1);
                next.gameObject.SetActive(pageCount > 1);
                previous.GetComponentInChildren<TextMeshPro>().text = Translator.GetString("PreviousPage");
                next.GetComponentInChildren<TextMeshPro>().text = Translator.GetString("NextPage");
            }
        }
    }

    private static bool CanShow(HudManager hud)
    {
        if (!hud || !hud.gameObject.activeInHierarchy || hud.IsIntroDisplayed ||
            hud.Chat && (!hud.Chat.IsClosedOrClosing || hud.Chat.IsAnimating) ||
            hud.GameMenu && hud.GameMenu.gameObject.activeInHierarchy ||
            GameStates.IsInGame && hud.TaskPanel && hud.TaskPanel.gameObject.activeInHierarchy && hud.TaskPanel.open ||
            GameSettingMenu.Instance && GameSettingMenu.Instance.gameObject.activeInHierarchy ||
            MapBehaviour.Instance && MapBehaviour.Instance.IsOpen ||
            Minigame.Instance && Minigame.Instance.isActiveAndEnabled || ExileController.Instance) return false;
        var ui = ControllerManager.Instance ? ControllerManager.Instance.CurrentUiState : null;
        return ui == null || ui.IsScene || string.IsNullOrEmpty(ui.MenuName) ||
            MeetingHud.Instance && ui.MenuName == MeetingHud.Instance.name;
    }

    internal static bool WantsScroll()
    {
        if (Application.platform == RuntimePlatform.Android || !Application.isFocused || pageCount <= 1 ||
            Main.ShowHudDiagnostics?.Value != true || !root || !root.activeInHierarchy || !owner || !owner.UICamera ||
            AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null ||
            (!GameStates.IsInGame && !GameStates.IsLobby) || layoutLobby != (!GameStates.IsInGame && GameStates.IsLobby) || !CanShow(owner)) return false;
        var camera = owner.UICamera;
        float depth = camera.WorldToScreenPoint(root.transform.position).z;
        var pointer = Input.mousePosition;
        var local = root.transform.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, depth)));
        float activeWidth = hitUpdatedFrame == Time.frameCount ? previousHitWidth : hitWidth;
        float activeHeight = hitUpdatedFrame == Time.frameCount ? previousHitHeight : hitHeight;
        return local.x >= 0f && local.x <= activeWidth && local.y <= 0f && local.y >= -activeHeight;
    }

    internal static void Dispose(HudManager hud = null)
    {
        if (hud != null && owner != hud) return;
        if (root != null) Object.Destroy(root);
        owner = null;
        root = null;
        rows = footer = positionText = null;
        previous = next = null;
        layoutCamera = null;
        page = 0;
        pageCount = rowsPerPage = 1;
        hitWidth = hitHeight = 0f;
        previousHitWidth = previousHitHeight = 0f;
        hitUpdatedFrame = -1;
        refreshAt = 0f;
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.OnDestroy))]
internal static class DebugShowerHudDestroyPatch
{
    public static void Prefix(HudManager __instance) => DebugShower.Dispose(__instance);
}
