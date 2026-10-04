using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AmongUs.Data;
using InnerNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace TOHEE.RuntimeSmoke;

// Fixed native UI actions only. Never edits identity, endpoints, login state, or protection settings.
internal sealed class OnlineSmoke
{
    private const string RegionName = "Niko233(CN2)";
    private const string TicketMarker = "tohee-runtime-smoke-private-room-v1";
    private readonly string directory;
    private readonly Func<string, string, double, Task> write;
    private string command = "";
    private string? originalRegion;
    private int ownedGameId;
    private bool hostRequested;
    private int gameIdBeforeHost;
    private int stage;
    private double started;
    private double stageStarted;
    private Task<RoomTicket?>? ticketRead;
    private Task? ticketWrite;
    private RoomTicket? ticket;
    private DisconnectReasons reasonBeforeRequest;
    internal bool Busy { get; private set; }
    internal string Outcome { get; private set; } = "idle";
    internal string Detail { get; private set; } = "";

    internal OnlineSmoke(string directory, Func<string, string, double, Task> write)
    { this.directory = directory; this.write = write; }

    internal static bool IsCommand(string value)
        => value is "nikocn_host" or "nikocn_join" or "online_leave" or "online_start";

    internal bool Begin(string value, double now, out string rejection)
    {
        rejection = "";
        var client = AmongUsClient.Instance;
        if (!client) { rejection = "client_unavailable"; return false; }
        if (value != "online_leave" && Constants.GetPlatformType() != Platforms.StandaloneItch)
        { rejection = "requires_native_itch_platform"; return false; }
        if (value is "nikocn_host" or "nikocn_join")
        {
            if (client.AmConnected || client.GameState != InnerNetClient.GameStates.NotJoined ||
                SceneManager.GetActiveScene().name != "MainMenu")
            { rejection = "requires_disconnected_main_menu"; return false; }
            if (!Authenticated()) { rejection = "logged_in_account_not_ready"; return false; }
            if (TOHE.Main.AutoStart.Value || TOHE.Main.AutoRehost.Value ||
                (!TOHE.Modules.RehostManager.IsAutoRehostDone && TOHE.Modules.RehostManager.ShouldPublic))
            { rejection = "automatic_start_rehost_or_public_rehost_enabled"; return false; }
            if (FindRegion() == null) { rejection = "unique_existing_region_unavailable"; return false; }
            if (Unique<MainMenuManager>("MainMenu") == null) { rejection = "unique_native_main_menu_unavailable"; return false; }
            if (ownedGameId != 0 || hostRequested || originalRegion != null)
            { rejection = "previous_test_session_requires_online_leave"; return false; }
        }
        else if (value == "online_leave")
        {
            if (client.AmConnected && !OwnsCurrentRoom())
            { rejection = "unowned_online_session"; return false; }
            if (!client.AmConnected && SceneManager.GetActiveScene().name != "MainMenu")
            { rejection = "requires_test_session_or_disconnected_main_menu"; return false; }
            if (ownedGameId == 0 && !hostRequested && originalRegion == null)
            { rejection = "no_owned_online_test_session"; return false; }
        }
        else if (value == "online_start")
        {
            if (!OwnsCurrentRoom() || !ReadyLobby() || !client.AmHost || !Authenticated() ||
                !GameData.Instance || GameData.Instance.PlayerCount is < 2 or > 3 || GameSettingMenu.Instance)
            { rejection = "requires_owned_private_host_lobby_with_two_or_three_players"; return false; }
            if (TOHE.Main.AutoStart.Value || TOHE.Main.AutoRehost.Value)
            { rejection = "automatic_start_or_rehost_enabled"; return false; }
        }
        else { rejection = "invalid_online_command"; return false; }

        command = value;
        started = stageStarted = now;
        stage = 0;
        ticket = null;
        ticketRead = null;
        ticketWrite = null;
        Busy = true;
        Outcome = "running";
        Detail = "fixed_native_ui_sequence";
        reasonBeforeRequest = client.LastDisconnectReason;
        if (value == "nikocn_join") ticketRead = Task.Run(ReadTicket);
        return true;
    }

    internal bool Tick(double now)
    {
        ObserveOwnership(now);
        if (!Busy) return true;
        if (now - started >= 20 || now - stageStarted >= 8)
            return Finish("timed_out", "online_observation_timeout_no_retry");
        var client = AmongUsClient.Instance;
        if (!client) return Finish("failed", "client_unavailable");
        if (command != "online_leave" && !Authenticated())
            return Finish("failed", "logged_in_account_not_ready");
        if (command != "online_leave" && originalRegion != null && !CurrentRegionMatches())
            return Finish("failed", "selected_region_changed");
        if (command is "nikocn_host" or "nikocn_join")
        {
            if (client.LastDisconnectReason != reasonBeforeRequest &&
                client.LastDisconnectReason != DisconnectReasons.Unknown &&
                client.LastDisconnectReason != DisconnectReasons.ExitGame)
                return Finish("failed", "native_disconnect_" + client.LastDisconnectReason);
            if (client.AmConnected && client.IsGamePublic)
                return Finish("failed", "test_room_is_public");
        }

        if (command == "nikocn_host") return Host(now);
        if (command == "nikocn_join") return Join(now);
        if (command == "online_leave") return Leave(now);
        if (stage == 0)
        {
            DestroyableSingleton<GameStartManager>.Instance.BeginGame();
            Advance(1, now);
            return false;
        }
        if (!OwnsCurrentRoom()) return Finish("failed", "owned_room_lost");
        if (client.IsGamePublic) return Finish("failed", "test_room_is_public");
        if (stage == 1 && client.GameState == InnerNetClient.GameStates.Started) Advance(2, now);
        var player = PlayerControl.LocalPlayer;
        return client.GameState == InnerNetClient.GameStates.Started && ShipStatus.Instance && player &&
            player.Data != null && player.Data.Role && player.CanMove && DestroyableSingleton<HudManager>.InstanceExists &&
            !DestroyableSingleton<HudManager>.Instance.IsIntroDisplayed
            ? Finish("succeeded", "started_ship_role_and_movement_ready") : false;
    }

    private bool Host(double now)
    {
        if (stage == 0)
        {
            Unique<MainMenuManager>("MainMenu")!.OpenOnlineMenu();
            Advance(1, now);
        }
        else if (stage == 1 && now - stageStarted >= 0.5)
        {
            Unique<MainMenuManager>("MainMenu")!.OpenCreateGame();
            Advance(2, now);
        }
        else if (stage == 2 && now - stageStarted >= 0.75)
        {
            var options = Unique<CreateGameOptions>("MainMenu");
            if (!CreateUiReady(options)) return false;
            SelectRegion(options!);
            options!.OpenConfirmPopup();
            Advance(3, now);
        }
        else if (stage == 3 && now - stageStarted >= 0.5)
        {
            var options = Unique<CreateGameOptions>("MainMenu");
            if (options == null || !options.gameObject.activeInHierarchy) return Finish("failed", "create_game_ui_lost");
            hostRequested = true;
            gameIdBeforeHost = AmongUsClient.Instance.GameId;
            reasonBeforeRequest = AmongUsClient.Instance.LastDisconnectReason;
            options.Confirm(); // Performs the native CheckOnlinePermissions -> CoCreateOnlineGame chain.
            Advance(4, now);
        }
        else if (stage == 4 && ReadyLobby() && AmongUsClient.Instance.AmHost && ownedGameId != 0)
        {
            if (GameData.Instance.PlayerCount > 3) return Finish("failed", "test_player_limit_exceeded");
            ticket = new RoomTicket
            {
                GameId = ownedGameId, RoomCode = GameCode.IntToGameName(ownedGameId),
                CreatedUtc = DateTimeOffset.UtcNow, TestMarker = TicketMarker, Region = RegionName
            };
            if (!ValidTicket(ticket)) return Finish("failed", "invalid_native_room_code");
            ticketWrite = write(".test-room.json", JsonSerializer.Serialize(ticket), now);
            Advance(5, now);
        }
        else if (stage == 5 && ticketWrite!.IsCompleted)
        {
            ticketWrite.GetAwaiter().GetResult();
            return Finish("succeeded", "private_test_room_ready_and_ticket_written");
        }
        return false;
    }

    private bool Join(double now)
    {
        if (stage == 0)
        {
            if (!ticketRead!.IsCompleted) return false;
            ticket = ticketRead.GetAwaiter().GetResult();
            ticketRead = null;
            if (ticket == null || !ValidTicket(ticket)) return Finish("rejected", "missing_invalid_or_expired_test_room_ticket");
            Unique<MainMenuManager>("MainMenu")!.OpenOnlineMenu();
            Advance(1, now);
        }
        else if (stage == 1 && now - stageStarted >= 0.5)
        {
            Unique<MainMenuManager>("MainMenu")!.OpenCreateGame();
            Advance(2, now);
        }
        else if (stage == 2 && now - stageStarted >= 0.75)
        {
            var options = Unique<CreateGameOptions>("MainMenu");
            if (!CreateUiReady(options)) return false;
            SelectRegion(options!);
            options!.Close(); // CoHide -> GoBackCreateGame -> OpenOnlineMenu -> ResetScreen.
            Advance(3, now);
        }
        else if (stage == 3)
        {
            var options = Unique<CreateGameOptions>("MainMenu");
            if (options == null || options.gameObject.activeInHierarchy) return false;
            Unique<MainMenuManager>("MainMenu")!.OpenEnterCodeMenu(false);
            Advance(4, now);
        }
        else if (stage == 4 && now - stageStarted >= 0.5)
        {
            var manager = Unique<EnterCodeManager>("MainMenu");
            var controller = ControllerManager.Instance;
            var selection = controller != null && controller ? controller.CurrentUiState?.CurrentSelection : null;
            var input = selection != null && selection ? selection.GetComponent<TextBoxTMP>() : null;
            if (manager == null || !manager.gameObject.activeInHierarchy || input == null || !input ||
                !input.gameObject.activeInHierarchy || input.gameObject.scene.name != "MainMenu") return false;
            input.SetText(ticket!.RoomCode, "");
            if (input.text != ticket.RoomCode) return Finish("failed", "native_code_input_rejected_ticket");
            reasonBeforeRequest = AmongUsClient.Instance.LastDisconnectReason;
            manager.LookForGame(); // Native permission check and private-code lookup populate the UI.
            Advance(5, now);
        }
        else if (stage == 5)
        {
            var manager = Unique<EnterCodeManager>("MainMenu");
            if (manager == null || !manager.gameObject.activeInHierarchy) return Finish("failed", "enter_code_ui_lost");
            var join = FindNativeJoinButton(manager);
            if (join == null || !join.enabled || !join.gameObject.activeInHierarchy) return false;
            if (!ValidTicket(ticket!)) return Finish("rejected", "expired_test_room_ticket");
            if (AmongUsClient.Instance.GameId != ticket!.GameId) return Finish("failed", "native_code_lookup_mismatch");
            ownedGameId = ticket!.GameId;
            reasonBeforeRequest = AmongUsClient.Instance.LastDisconnectReason;
            manager.ClickJoin(); // No custom endpoint, token, direct RPC, or public listing.
            Advance(6, now);
        }
        else if (stage == 6 && ReadyLobby())
        {
            if (!OwnsCurrentRoom() || AmongUsClient.Instance.AmHost) return Finish("failed", "joined_room_mismatch");
            if (GameData.Instance.PlayerCount > 3) return Finish("failed", "test_player_limit_exceeded");
            return Finish("succeeded", "joined_ticket_private_test_room");
        }
        return false;
    }

    // This only identifies the fixed native ClickJoin binding. It never invokes event metadata or arbitrary methods.
    private static PassiveButton? FindNativeJoinButton(EnterCodeManager manager)
    {
        PassiveButton? result = null;
        foreach (var button in UObject.FindObjectsOfType<PassiveButton>(true))
        {
            if (button.gameObject.scene.name != "MainMenu") continue;
            var click = button.OnClick;
            for (int index = 0; index < click.GetPersistentEventCount(); index++)
            {
                var target = click.GetPersistentTarget(index);
                if (!target || target.GetInstanceID() != manager.GetInstanceID() || click.GetPersistentMethodName(index) != "ClickJoin") continue;
                if (result != null && result.GetInstanceID() != button.GetInstanceID()) return null;
                result = button;
            }
        }
        return result;
    }

    private void SelectRegion(CreateGameOptions options)
    {
        var server = DestroyableSingleton<ServerManager>.Instance;
        originalRegion = server.CurrentRegion?.Name ?? throw new InvalidOperationException("original_region_missing");
        var region = FindRegion() ?? throw new InvalidOperationException("unique_existing_region_unavailable");
        ChooseNativeRegion(options, region);
        if (!CurrentRegionMatches()) throw new InvalidOperationException("region_selection_failed");
    }

    private bool Leave(double now)
    {
        var client = AmongUsClient.Instance;
        if (stage == 0)
        {
            if (client.AmConnected) client.ExitGame(DisconnectReasons.ExitGame);
            Advance(1, now);
            return false;
        }
        if (client.AmConnected || client.GameState != InnerNetClient.GameStates.NotJoined ||
            SceneManager.GetActiveScene().name != "MainMenu") return false;
        if (stage == 1)
        {
            var options = Unique<CreateGameOptions>("MainMenu");
            if (originalRegion == null || OriginalRegionAlreadySelected())
            {
                if (options != null && options.gameObject.activeInHierarchy)
                {
                    if (!CreateUiReady(options)) return false;
                    options.Close();
                    Advance(4, now);
                    return false;
                }
                return FinishLeave();
            }
            if (!Authenticated()) return Finish("failed", "original_region_restore_requires_native_online_permission");
            var mainMenu = Unique<MainMenuManager>("MainMenu");
            if (mainMenu == null) return Finish("failed", "unique_native_main_menu_unavailable");
            mainMenu.OpenOnlineMenu();
            Advance(2, now);
        }
        else if (stage == 2 && now - stageStarted >= 0.5)
        {
            Unique<MainMenuManager>("MainMenu")!.OpenCreateGame();
            Advance(3, now);
        }
        else if (stage == 3 && now - stageStarted >= 0.75)
        {
            var options = Unique<CreateGameOptions>("MainMenu");
            if (!CreateUiReady(options)) return false;
            ChooseNativeRegion(options!, FindOriginalRegion());
            if (!OriginalRegionAlreadySelected()) return Finish("failed", "original_region_selection_failed");
            options!.Close();
            Advance(4, now);
        }
        else if (stage == 4)
        {
            var options = Unique<CreateGameOptions>("MainMenu");
            if (options == null || options.gameObject.activeInHierarchy) return false;
            return FinishLeave();
        }
        return false;
    }

    private IRegionInfo FindOriginalRegion()
    {
        IRegionInfo? original = null;
        foreach (var region in DestroyableSingleton<ServerManager>.Instance.AvailableRegions)
            if (region.Name == originalRegion)
            { if (original != null) throw new InvalidOperationException("original_region_ambiguous"); original = region; }
        return original ?? throw new InvalidOperationException("original_region_unavailable");
    }

    private bool OriginalRegionAlreadySelected()
        => originalRegion != null && FindOriginalRegion().Name == DestroyableSingleton<ServerManager>.Instance.CurrentRegion?.Name;

    private bool FinishLeave()
    {
        originalRegion = null;
        ownedGameId = 0;
        hostRequested = false;
        return Finish("succeeded", "owned_test_session_left_and_original_region_restored");
    }

    private static bool CreateUiReady(CreateGameOptions? options)
    {
        if (options == null || !options || !options.gameObject.activeInHierarchy) return false;
        var controller = ControllerManager.Instance;
        return controller != null && controller && controller.CurrentUiState?.MenuName == options.name;
    }

    private static void ChooseNativeRegion(CreateGameOptions options, IRegionInfo region)
    {
        var dropdown = options.serverDropdown;
        if (dropdown == null || !dropdown || dropdown.gameObject.scene.name != "MainMenu")
            throw new InvalidOperationException("native_server_dropdown_unavailable");
        options.OpenServerDropdown(); // The native OnEnable fills options after CoShow has initialized both callbacks.
        if (!dropdown.gameObject.activeInHierarchy) throw new InvalidOperationException("native_server_dropdown_inactive");
        dropdown.ChooseOption(region); // Fixed original button handler, exposed by generated IL2CPP interop.
        if (dropdown.gameObject.activeInHierarchy) throw new InvalidOperationException("native_server_dropdown_did_not_close");
    }

    internal void ObserveOwnership(double now)
    {
        var client = AmongUsClient.Instance;
        // Record only this live creation attempt; never claim a later manual host action after timeout.
        if (Busy && command == "nikocn_host" && stage >= 4 && now - started < 20 && now - stageStarted < 8 &&
            hostRequested && ownedGameId == 0 && client && client.AmConnected && client.AmHost &&
            client.GameId != gameIdBeforeHost &&
            client.NetworkMode == NetworkModes.OnlineGame && CurrentRegionMatches() &&
            GameCode.IntToGameName(client.GameId)?.Length == 6) ownedGameId = client.GameId;
    }

    private bool OwnsCurrentRoom()
    {
        var client = AmongUsClient.Instance;
        return ownedGameId != 0 && client && client.AmConnected && client.NetworkMode == NetworkModes.OnlineGame &&
            client.GameId == ownedGameId && CurrentRegionMatches();
    }

    private static bool ReadyLobby()
    {
        var client = AmongUsClient.Instance;
        return client && client.NetworkMode == NetworkModes.OnlineGame && client.GameState == InnerNetClient.GameStates.Joined &&
            SceneManager.GetActiveScene().name == "OnlineGame" && !client.IsGamePublic && GameData.Instance &&
            PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.Data != null &&
            DestroyableSingleton<GameStartManager>.InstanceExists && !ShipStatus.Instance;
    }

    private static bool Authenticated() => DestroyableSingleton<EOSManager>.InstanceExists &&
        DestroyableSingleton<AccountManager>.InstanceExists && EOSManager.Instance.HasFinishedLoginFlow() &&
        DataManager.Player.Account.LoginStatus == EOSManager.AccountLoginStatus.LoggedIn &&
        DestroyableSingleton<AccountManager>.Instance.CanPlayOnline() && !DataManager.Player.Ban.IsBanned;

    private static IRegionInfo? FindRegion()
    {
        if (!DestroyableSingleton<ServerManager>.InstanceExists) return null;
        IRegionInfo? result = null;
        foreach (var region in DestroyableSingleton<ServerManager>.Instance.AvailableRegions)
        {
            if (region.Name != RegionName) continue;
            if (result != null || !EndpointMatches(region)) return null;
            result = region;
        }
        return result;
    }

    private static bool CurrentRegionMatches() => DestroyableSingleton<ServerManager>.InstanceExists &&
        DestroyableSingleton<ServerManager>.Instance.CurrentRegion != null &&
        DestroyableSingleton<ServerManager>.Instance.CurrentRegion.Name == RegionName &&
        EndpointMatches(DestroyableSingleton<ServerManager>.Instance.CurrentRegion) &&
        ServerEndpointMatches(DestroyableSingleton<ServerManager>.Instance.CurrentUdpServer);

    private static bool EndpointMatches(IRegionInfo region)
    {
        return region.TryCast<StaticHttpRegionInfo>() != null && string.IsNullOrEmpty(region.TargetServer) &&
            region.Servers != null && region.Servers.Length == 1 && ServerEndpointMatches(region.Servers[0]);
    }

    private static bool ServerEndpointMatches(ServerInfo? server)
    {
        return server != null && server.Port == 443 && !server.UseDtls &&
            Uri.TryCreate(server.HttpUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            uri.Host == "au-cn.netease.me" && uri.Port == 443 && uri.AbsolutePath == "/" &&
            uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
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

    private RoomTicket? ReadTicket()
    {
        string path = Path.Combine(directory, ".test-room.json");
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 2 or > 1024) return null;
        try { return JsonSerializer.Deserialize<RoomTicket>(stream); }
        catch (JsonException) { return null; }
    }

    private static bool ValidTicket(RoomTicket value)
    {
        var age = DateTimeOffset.UtcNow - value.CreatedUtc;
        if (value.TestMarker != TicketMarker || value.Region != RegionName || age < TimeSpan.Zero || age > TimeSpan.FromMinutes(30) ||
            value.RoomCode == null || value.RoomCode.Length != 6 || GameCode.IntToGameName(value.GameId) != value.RoomCode) return false;
        foreach (char c in value.RoomCode) if (c is < 'A' or > 'Z') return false;
        return GameCode.GameNameToInt(value.RoomCode) == value.GameId;
    }

    private void Advance(int next, double now) { stage = next; stageStarted = now; }
    internal void Stop(string state, string detail) => Finish(state, detail);
    private bool Finish(string state, string detail) { Busy = false; Outcome = state; Detail = detail; return true; }

    internal object Snapshot()
    {
        var client = AmongUsClient.Instance;
        return new
        {
            authenticated_ready = Authenticated(), region_matches = CurrentRegionMatches(),
            native_itch_platform = Constants.GetPlatformType() == Platforms.StandaloneItch,
            owns_current_room = OwnsCurrentRoom(), has_test_session = ownedGameId != 0 || hostRequested,
            original_region_restore_pending = originalRegion != null,
            is_private = client && client.AmConnected ? (bool?)!client.IsGamePublic : null,
            ready_lobby = ReadyLobby(), local_can_move = PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.CanMove,
            ship_present = (bool)ShipStatus.Instance, stage = Busy ? (int?)stage : null,
            state = Outcome, detail = Detail, disconnect_reason = client ? client.LastDisconnectReason.ToString() : null
        };
    }

    private sealed class RoomTicket
    {
        public RoomTicket() { }
        public int GameId { get; set; }
        public string RoomCode { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; }
        public string TestMarker { get; set; } = "";
        public string Region { get; set; } = "";
    }
}
