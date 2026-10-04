using System;
using InnerNet;
using TOHE;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace TOHEE.RuntimeSmoke;

// Fixed loopback LAN actions. The native button keeps name, ban and connection checks.
internal sealed class LocalPlaySmoke
{
    private readonly Func<bool> ownsLocalHost;
    private string command = "";
    private int stage;
    private double started;
    private double stageStarted;
    private JoinGameButton? joinButton;
    private bool joinRequested;
    private IntPtr requestingClient;
    private bool ownsJoin;
    private IntPtr joinedClient;
    private IntPtr joinedPlayer;
    private int joinedClientId;
    private int joinedHostId;
    private bool leavingHost;
    private bool optionPending;
    private int originalNoGameEnd;
    private int originalPreset;

    internal LocalPlaySmoke(string directory, Func<bool> ownsLocalHost)
    {
        _ = directory; // The caller owns report files; this helper performs no file I/O.
        this.ownsLocalHost = ownsLocalHost;
    }

    internal bool Busy { get; private set; }
    internal bool OwnsSession => OwnsHost() || OwnsJoin();
    internal string Outcome { get; private set; } = "idle";
    internal string Detail { get; private set; } = "";
    internal static bool IsCommand(string value) => value is "local_join" or "local_start" or "local_leave";

    internal bool Begin(string value, double now, out string rejection)
    {
        rejection = "";
        var client = AmongUsClient.Instance;
        if (Busy) { rejection = "local_operation_busy"; return false; }
        if (!IsCommand(value)) { rejection = "invalid_local_command"; return false; }
        if (!client) { rejection = "client_unavailable"; return false; }
        if (value == "local_join")
        {
            if (client.AmConnected || client.GameState != InnerNetClient.GameStates.NotJoined ||
                SceneManager.GetActiveScene().name is not ("MainMenu" or "MatchMaking") ||
                joinRequested || ownsJoin || ownsLocalHost())
            { rejection = "requires_disconnected_menu_without_owned_session"; return false; }
        }
        else if (value == "local_start")
        {
            if (!OwnsHost() || !ReadyLobby() || GameData.Instance.PlayerCount != 2 ||
                GameSettingMenu.Instance || !TOHE.Options.IsLoaded || TOHE.Options.NoGameEnd == null ||
                !TOHE.GameStates.IsNormalGame)
            { rejection = "requires_owned_loopback_host_normal_lobby_with_two_players"; return false; }
            if (TOHE.Main.AutoStart.Value || TOHE.Main.AutoRehost.Value || optionPending)
            { rejection = "automatic_start_rehost_or_previous_option_restore_pending"; return false; }
        }
        else
        {
            if (!OwnsHost() && !OwnsJoin())
            { rejection = "no_current_owned_local_session"; return false; }
        }
        command = value;
        stage = 0;
        started = stageStarted = now;
        Busy = true;
        Outcome = "running";
        Detail = "fixed_native_loopback_sequence";
        return true;
    }

    internal bool Tick(double now)
    {
        if (!Busy) return true;
        try
        {
            ObserveJoin();
            // The native intro animation itself takes about nine seconds. Its
            // observer has a separate bound; Tick never blocks a tool or frame.
            double stageLimit = command == "local_start" && stage is 2 or 3 ? 15 : 8;
            if (now - started >= 30 || now - stageStarted >= stageLimit)
                return Finish("timed_out", "local_observation_timeout_no_retry", restore: true);
            var client = AmongUsClient.Instance;
            if (!client) return Finish("failed", "client_unavailable", restore: true);
            if (command == "local_join") return Join(now);
            if (command == "local_leave") return Leave(now);
            if (!OwnsHost()) return Finish("failed", "owned_loopback_host_lost", restore: true);
            if (!GameData.Instance || GameData.Instance.PlayerCount != 2)
                return Finish("failed", "two_player_session_changed", restore: true);
            if (stage == 0)
            {
                if (!ReadyLobby() || GameSettingMenu.Instance)
                    return Finish("failed", "native_host_lobby_lost", restore: true);
                originalPreset = OptionItem.CurrentPreset;
                originalNoGameEnd = TOHE.Options.NoGameEnd.CurrentValue;
                optionPending = true;
                TOHE.Options.NoGameEnd.SetValue(1, doSave: false, doSync: true);
                DestroyableSingleton<GameStartManager>.Instance.BeginGame();
                Advance(1, now);
            }
            else if (stage == 1 && client.GameState == InnerNetClient.GameStates.Started)
                Advance(2, now);
            else if (stage == 2 && ShipStatus.Instance && Counts().NativeRoles == 2 &&
                     DestroyableSingleton<HudManager>.InstanceExists && HudManager.Instance.IsIntroDisplayed)
                Advance(3, now);
            else if (stage == 3 && DestroyableSingleton<HudManager>.InstanceExists && !HudManager.Instance.IsIntroDisplayed)
                Advance(4, now);
            else if (stage == 4 && GameplayReady())
                return Finish("succeeded", "two_player_roles_intro_tasks_and_local_movement_ready", restore: false);
            return false;
        }
        catch (Exception ex)
        {
            return Finish("failed", "local_native_sequence_" + ex.GetType().Name, restore: true);
        }
    }

    private bool Join(double now)
    {
        var client = AmongUsClient.Instance;
        if (stage < 2 && (client.AmConnected || client.GameState != InnerNetClient.GameStates.NotJoined))
            return Finish("failed", "menu_session_changed_before_join", restore: true);
        if (stage == 0)
        {
            if (SceneManager.GetActiveScene().name == "MainMenu")
            {
                var menu = Unique<MainMenuManager>("MainMenu");
                if (menu == null) return Finish("failed", "native_main_menu_unavailable", restore: true);
                menu.OpenGameModeMenu();
                var entry = menu.playLocalButton ? menu.playLocalButton.GetComponent<SceneChanger>() : null;
                if (entry == null || entry.TargetScene != "MatchMaking")
                    return Finish("failed", "native_local_menu_entry_unavailable", restore: true);
                entry.Click();
            }
            Advance(1, now);
        }
        else if (stage == 1 && SceneManager.GetActiveScene().name == "MatchMaking")
        {
            var discovery = Unique<GameDiscovery>("MatchMaking");
            if (discovery == null || !discovery.ButtonPrefab || !discovery.ItemLocation) return false;
            var node = discovery.ItemLocation;
            while (node) { node.gameObject.SetActive(true); node = node.parent; }
            joinButton = UObject.Instantiate(discovery.ButtonPrefab, discovery.ItemLocation);
            joinButton.NetworkMode = NetworkModes.LocalGame;
            joinButton.netAddress = "127.0.0.1";
            joinButton.gameObject.SetActive(true);
            if (!joinButton.gameObject.activeInHierarchy)
                return Finish("failed", "native_local_join_button_inactive", restore: true);
            // Only the existing native handler sets the endpoint and requests the connection.
            joinRequested = true;
            requestingClient = client.Pointer;
            joinButton.OnClick();
            Advance(2, now);
        }
        else if (stage == 2)
        {
            if (client.AmConnected && !LoopbackLocal())
                return Finish("failed", "joined_session_is_not_fixed_loopback", restore: true);
            if (ReadyLobby() && OwnsJoin())
            {
                if (GameData.Instance.PlayerCount != 2)
                    return Finish("failed", "joined_room_does_not_have_two_players", restore: true);
                return Finish("succeeded", "native_loopback_two_player_lobby_ready", restore: false);
            }
            if (!client.AmConnected && client.GameState == InnerNetClient.GameStates.NotJoined &&
                !joinButton && SceneManager.GetActiveScene().name != "MatchMaking")
                return Finish("failed", "native_join_ui_or_connection_lost", restore: true);
        }
        return false;
    }

    private void ObserveJoin()
    {
        if (!Busy || command != "local_join" || !joinRequested || !LoopbackLocal()) return;
        var client = AmongUsClient.Instance;
        if (client.Pointer != requestingClient) return;
        if (ownsJoin)
        {
            if (joinedPlayer == IntPtr.Zero && OwnsJoin() && PlayerControl.LocalPlayer)
                joinedPlayer = PlayerControl.LocalPlayer.Pointer;
            return;
        }
        if (!client.AmConnected || client.AmHost || client.ClientId < 0 || client.HostId < 0) return;
        ownsJoin = true;
        joinedClient = client.Pointer;
        joinedClientId = client.ClientId;
        joinedHostId = client.HostId;
        joinedPlayer = PlayerControl.LocalPlayer ? PlayerControl.LocalPlayer.Pointer : IntPtr.Zero;
    }

    private bool Leave(double now)
    {
        var client = AmongUsClient.Instance;
        if (stage == 0)
        {
            if (!OwnsHost() && !OwnsJoin())
                return Finish("failed", "owned_local_session_lost_before_leave", restore: true);
            leavingHost = OwnsHost();
            RestoreOption();
            client.ExitGame(DisconnectReasons.ExitGame);
            Advance(1, now);
        }
        else if (!client.AmConnected && client.GameState == InnerNetClient.GameStates.NotJoined &&
                 SceneManager.GetActiveScene().name is "MainMenu" or "MatchMaking")
        {
            ownsJoin = joinRequested = false;
            joinedPlayer = joinedClient = requestingClient = IntPtr.Zero;
            return Finish(leavingHost ? "stop_owned_server" : "succeeded",
                leavingHost ? "native_host_left_caller_must_stop_owned_server" : "native_joined_client_left", restore: true);
        }
        else if (client.AmConnected && !LoopbackLocal())
            return Finish("failed", "another_session_replaced_local_leave", restore: true);
        return false;
    }

    private static bool LoopbackLocal()
    {
        var client = AmongUsClient.Instance;
        return client && client.NetworkMode == NetworkModes.LocalGame && client.GameId == 32 &&
            client.GetNetworkAddress() == "127.0.0.1" && client.GetNetworkPort() == 22023;
    }

    private bool OwnsHost() => ownsLocalHost() && LoopbackLocal() &&
        AmongUsClient.Instance.AmConnected && AmongUsClient.Instance.AmHost;

    private bool OwnsJoin()
    {
        var client = AmongUsClient.Instance;
        return ownsJoin && LoopbackLocal() && client.AmConnected && !client.AmHost &&
            client.Pointer == joinedClient && client.ClientId == joinedClientId && client.HostId == joinedHostId &&
            (joinedPlayer == IntPtr.Zero || PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.Pointer == joinedPlayer);
    }

    private static bool ReadyLobby() => LoopbackLocal() && AmongUsClient.Instance.AmConnected &&
        AmongUsClient.Instance.GameState == InnerNetClient.GameStates.Joined &&
        SceneManager.GetActiveScene().name == "OnlineGame" && GameData.Instance && PlayerControl.LocalPlayer &&
        PlayerControl.LocalPlayer.Data != null && DestroyableSingleton<GameStartManager>.InstanceExists && !ShipStatus.Instance;

    private static (int Players, int NativeRoles, int ModRoles, int PlayersWithTasks) Counts()
    {
        int players = 0, nativeRoles = 0, modRoles = 0, playersWithTasks = 0;
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (!player || player.Data == null || player.Data.Disconnected) continue;
            players++;
            if (player.Data.Role) nativeRoles++;
            if (TOHE.Main.PlayerStates.TryGetValue(player.PlayerId, out var state) &&
                state.MainRole < CustomRoles.NotAssigned) modRoles++;
            if (player.Data.Tasks != null && player.Data.Tasks.Count > 0) playersWithTasks++;
        }
        return (players, nativeRoles, modRoles, playersWithTasks);
    }

    private static bool GameplayReady()
    {
        if (!LoopbackLocal() || AmongUsClient.Instance.GameState != InnerNetClient.GameStates.Started ||
            !GameData.Instance || GameData.Instance.PlayerCount != 2 || !ShipStatus.Instance ||
            !TOHE.Main.IntroDestroyed || TOHE.Main.RealOptionsData == null ||
            !PlayerControl.LocalPlayer || !PlayerControl.LocalPlayer.CanMove ||
            !DestroyableSingleton<HudManager>.InstanceExists || HudManager.Instance.IsIntroDisplayed) return false;
        var counts = Counts();
        return counts.Players == 2 && counts.NativeRoles == 2 && counts.ModRoles == 2 &&
            counts.PlayersWithTasks > 0 && GameData.Instance.TotalTasks > 0;
    }

    private void RestoreOption()
    {
        if (!optionPending) return;
        int currentPreset = OptionItem.CurrentPreset;
        try
        {
            if (currentPreset != originalPreset) OptionItem.SwitchPreset(originalPreset, doSync: false);
            TOHE.Options.NoGameEnd.SetValue(originalNoGameEnd, doSave: false, doSync: false);
            optionPending = false;
        }
        finally
        {
            if (OptionItem.CurrentPreset != currentPreset) OptionItem.SwitchPreset(currentPreset, doSync: false);
        }
        if (OwnsHost()) OptionItem.SyncAllOptions();
    }

    private static T? Unique<T>(string scene) where T : Component
    {
        T? result = null;
        foreach (var candidate in UObject.FindObjectsOfType<T>(true))
        {
            if (candidate.gameObject.scene.name != scene) continue;
            if (result != null) return null;
            result = candidate;
        }
        return result;
    }

    private void Advance(int next, double now) { stage = next; stageStarted = now; }
    internal void Stop(string outcome, string detail) => Finish(outcome, detail, restore: true);
    private bool Finish(string outcome, string detail, bool restore)
    {
        if (restore)
        {
            try { RestoreOption(); }
            catch (Exception ex)
            {
                // Leave optionPending set, allowing local_leave/Stop to retry restoration.
                outcome = "failed";
                detail = "local_option_restore_" + ex.GetType().Name;
            }
        }
        if (joinButton != null && joinButton) UObject.Destroy(joinButton.gameObject);
        joinButton = null;
        if (command == "local_join" && !ownsJoin &&
            (!AmongUsClient.Instance || !AmongUsClient.Instance.AmConnected && AmongUsClient.Instance.mode == MatchMakerModes.None))
        {
            joinRequested = false;
            requestingClient = IntPtr.Zero;
        }
        Busy = false;
        Outcome = outcome;
        Detail = detail;
        return true;
    }

    internal object Snapshot()
    {
        var client = AmongUsClient.Instance;
        var counts = Counts();
        return new
        {
            state = Outcome, detail = Detail, stage = Busy ? (int?)stage : null,
            network_mode = client ? client.NetworkMode.ToString() : null,
            game_state = client ? client.GameState.ToString() : null,
            loopback_local = LoopbackLocal(), owned_host = OwnsHost(), owned_join = OwnsJoin(),
            option_restore_pending = optionPending, player_count = GameData.Instance ? GameData.Instance.PlayerCount : 0,
            connected_player_count = counts.Players, native_role_ready_count = counts.NativeRoles,
            mod_role_ready_count = counts.ModRoles, players_with_tasks_count = counts.PlayersWithTasks,
            local_task_state_total = PlayerControl.LocalPlayer &&
                TOHE.Main.PlayerStates.TryGetValue(PlayerControl.LocalPlayer.PlayerId, out var taskOwner)
                ? (int?)taskOwner.TaskState.AllTasksCount : null,
            total_task_count = GameData.Instance ? GameData.Instance.TotalTasks : 0,
            completed_task_count = GameData.Instance ? GameData.Instance.CompletedTasks : 0,
            intro_destroyed = TOHE.Main.IntroDestroyed,
            intro_displayed = DestroyableSingleton<HudManager>.InstanceExists && HudManager.Instance.IsIntroDisplayed,
            local_can_move = PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.CanMove,
            gameplay_ready = GameplayReady()
        };
    }
}
