using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using InnerNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace TOHEE.RuntimeSmoke;

[BepInPlugin("local.tohee.runtime-smoke", "TOHEE Runtime Smoke (TEST ONLY)", "1.0.0")]
[BepInDependency(TOHE.Main.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class SmokePlugin : BasePlugin
{
    internal static SmokeSession? Session;

    public override void Load()
    {
        Session = new SmokeSession(Paths.GameRootPath, Log);
        BepInEx.Logging.Logger.Listeners.Add(new SmokeErrorCounter());
        AddComponent<SmokeDriver>();
        Log.LogInfo("Test-only fixed commands: snapshot, capture, freeplay, locallobby, gui_local, gui_lobby, lobby_welcome, gui_tasks, gui_roles, leave, local_join, local_start, local_leave, local_chat_watch, local_cmd_id, local_task, local_meeting, local_meeting_request, local_skip, local_prepare_abilities, local_phantom, local_ability_meeting, local_judge, nikocn_host, nikocn_join, online_leave, online_start. No command arguments.");
    }
}

public sealed class SmokeDriver : MonoBehaviour
{
    public SmokeDriver(IntPtr pointer) : base(pointer) { }
    public void Update() => SmokePlugin.Session?.Tick();
}

// Unity APIs only run in Tick, called by the injected component on the main thread.
// Disk work runs in tasks; Tick never waits for an incomplete task.
internal sealed class SmokeSession
{
    private const double TimeoutSeconds = 8;
    private readonly string directory;
    private readonly ManualLogSource log;
    private Task<string?>? readTask;
    private double readStarted;
    private bool readTimedOut;
    private double nextRead;
    private Task writeTail = Task.CompletedTask;
    private readonly List<WriteWork> writes = new();
    private Pending? pending;
    private long sequence;
    private bool ownsLocalLobby;
    private readonly OnlineSmoke online;
    private readonly LocalPlaySmoke local;
    private readonly MeetingSmoke meeting;
    private readonly TaskCompletionSmoke taskCompletion;
    private readonly AbilitySmoke abilities;
    private readonly LocalChatSmoke localChat;
    private readonly LobbyUiSmoke lobbyUi;

    internal SmokeSession(string gameRoot, ManualLogSource log)
    {
        directory = Path.GetFullPath(Path.Combine(gameRoot, ".tohee-smoke"));
        this.log = log;
        online = new OnlineSmoke(directory, QueueWrite);
        local = new LocalPlaySmoke(directory, () => ownsLocalLobby);
        meeting = new MeetingSmoke(() => local.OwnsSession);
        taskCompletion = new TaskCompletionSmoke(() => local.OwnsSession);
        abilities = new AbilitySmoke(() => local.OwnsSession);
        localChat = new LocalChatSmoke(() => local.OwnsSession);
        lobbyUi = new LobbyUiSmoke(() => local.OwnsSession, directory);
    }

    internal void Tick()
    {
        double now = Time.realtimeSinceStartup;
        CheckWrites(now);
        online.ObserveOwnership(now);
        try { abilities.Observe(); }
        catch (Exception ex) { Report("local_ability_restore", "failed", ex.GetType().Name, now); }
        if (pending != null) PollPending(now);

        if (readTask != null)
        {
            if (readTask.IsCompleted)
            {
                try
                {
                    string? command = readTask.GetAwaiter().GetResult();
                    if (!readTimedOut && command != null) RunCommand(command, now);
                }
                catch (Exception ex) { Report("command_io", "failed", ex.GetType().Name, now); }
                readTask = null;
                readTimedOut = false;
            }
            else if (!readTimedOut && now - readStarted >= TimeoutSeconds)
            {
                readTimedOut = true;
                Report("command_io", "timed_out", "8 second command-file timeout; stale result will be discarded", now);
            }
        }

        if (readTask == null && now >= nextRead)
        {
            nextRead = now + 0.25;
            readStarted = now;
            readTask = Task.Run(ReadOnce);
        }
    }

    private string? ReadOnce()
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "command.txt");
        if (!File.Exists(input)) return null;
        string claimed = Path.Combine(directory, ".claimed-command.txt");
        File.Move(input, claimed, true);
        try
        {
            using var stream = new FileStream(claimed, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 64) return "invalid";
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd().Trim();
        }
        finally { File.Delete(claimed); }
    }

    private void RunCommand(string command, double now)
    {
        if (command is not ("snapshot" or "capture" or "freeplay" or "locallobby" or "gui_local" or "gui_tasks" or "gui_roles" or "leave") && !OnlineSmoke.IsCommand(command) && !LocalPlaySmoke.IsCommand(command) && !LocalChatSmoke.IsCommand(command) && !LobbyUiSmoke.IsCommand(command) && !MeetingSmoke.IsCommand(command) && !TaskCompletionSmoke.IsCommand(command) && !AbilitySmoke.IsCommand(command))
        {
            Report("invalid", "rejected", "Only fixed documented commands are accepted", now);
            return;
        }
        if (pending != null && command != "snapshot")
        {
            Report(command, "rejected", "Another command is pending", now);
            return;
        }
        try
        {
            if (command == "snapshot")
            {
                QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                Report(command, "succeeded", "snapshot.json queued", now);
                return;
            }

            var client = AmongUsClient.Instance;
            if (LobbyUiSmoke.IsCommand(command))
            {
                if (!lobbyUi.Begin(command, now, out string rejection))
                {
                    Report(command, "rejected", rejection, now);
                    return;
                }
                pending = new Pending(command, now) { LobbyUi = lobbyUi };
                Report(command, "started", "Fixed owned single-player lobby UI path; 8 second native observer", now);
            }
            else if (LocalChatSmoke.IsCommand(command))
            {
                if (!localChat.Begin(command, now, out string rejection))
                {
                    QueueWrite("private-chat.json", JsonSerializer.Serialize(localChat.Snapshot()), now);
                    Report(command, "rejected", rejection, now);
                    return;
                }
                pending = new Pending(command, now) { LocalChat = localChat };
                Report(command, "started", "Fixed owned loopback private chat path; 8 second counter-only observation", now);
            }
            else if (AbilitySmoke.IsCommand(command))
            {
                if (!abilities.Begin(command, now, out string rejection))
                {
                    Report(command, "rejected", rejection, now);
                    return;
                }
                pending = new Pending(command, now) { Abilities = abilities };
                Report(command, "started", "Fixed owned two-player loopback native ability scenario; 8 second stages and 30 second total limit", now);
            }
            else if (TaskCompletionSmoke.IsCommand(command))
            {
                if (!taskCompletion.Begin(now, out string rejection))
                {
                    Report(command, "rejected", rejection, now);
                    return;
                }
                pending = new Pending(command, now) { TaskCompletion = taskCompletion };
                Report(command, "started", "Single native task completion callback in the owned loopback game; 8 second observer", now);
            }
            else if (MeetingSmoke.IsCommand(command))
            {
                if (!meeting.Begin(command, now, out string rejection))
                {
                    Report(command, "rejected", rejection, now);
                    return;
                }
                pending = new Pending(command, now) { Meeting = meeting };
                Report(command, "started", "Fixed native meeting and vote UI sequence; bounded animation waits", now);
            }
            else if (LocalPlaySmoke.IsCommand(command))
            {
                if (command == "local_leave") abilities.RestorePrepared();
                if (command == "local_start" && !abilities.BeforeStart(out string preparationRejection))
                {
                    Report(command, "rejected", preparationRejection, now);
                    return;
                }
                if (!local.Begin(command, now, out string rejection))
                {
                    Report(command, "rejected", rejection, now);
                    return;
                }
                pending = new Pending(command, now) { Local = local };
                Report(command, "started", "Fixed native loopback sequence; 15 second role/intro observers, 8 second other stages, 30 second total limit", now);
            }
            else if (OnlineSmoke.IsCommand(command))
            {
                if (!online.Begin(command, now, out string rejection))
                {
                    Report(command, "rejected", rejection, now);
                    return;
                }
                pending = new Pending(command, now) { Online = online };
                Report(command, "started", "Fixed native online UI sequence; 8 second stages and 20 second total observation limit", now);
            }
            else if (command == "gui_roles")
            {
                if (!client || client.NetworkMode != NetworkModes.FreePlay || SceneManager.GetActiveScene().name != "Tutorial" ||
                    !PlayerControl.LocalPlayer || !ShipStatus.Instance || Minigame.Instance || !TOHE.Main.GameIsLoaded)
                {
                    Report(command, "rejected", "Requires initialized offline FreePlay with no open minigame", now);
                    return;
                }
                pending = new Pending(command, now) { Roles = new RoleActivationSmoke(directory) };
                pending.Roles.Begin();
                Report(command, "started", "Fixed offline role selections and real HUD/text updates; 25 second total limit", now);
            }
            else if (command == "gui_tasks")
            {
                var player = PlayerControl.LocalPlayer;
                if (!client || client.NetworkMode != NetworkModes.FreePlay || SceneManager.GetActiveScene().name != "Tutorial" ||
                    !player || player.Data == null || !player.Data.Role || !ShipStatus.Instance || Minigame.Instance ||
                    !TOHE.Main.PlayerStates.TryGetValue(player.PlayerId, out var playerState) ||
                    playerState.MainRole >= TOHE.CustomRoles.NotAssigned)
                {
                    Report(command, "rejected", "Requires initialized TOHE FreePlay state, a restorable main role, and no open minigame", now);
                    return;
                }
                pending = new Pending(command, now) { Tasks = new GuiTasksSmoke() };
                pending.Tasks.Begin();
                Report(command, "started", "Fixed native TaskAdder role, overlay, and lifecycle assertions", now);
            }
            else if (command == "gui_local")
            {
                if (!ownsLocalLobby || !client || client.NetworkMode != NetworkModes.LocalGame || !client.AmHost ||
                    client.GameState != InnerNetClient.GameStates.Joined || !PlayerControl.LocalPlayer ||
                    PlayerControl.LocalPlayer.Data == null || ShipStatus.Instance || !DestroyableSingleton<GameStartManager>.InstanceExists ||
                    GameSettingMenu.Instance)
                {
                    Report(command, "rejected", "Requires this helper's ready LocalGame lobby with settings closed", now);
                    return;
                }
                pending = new Pending(command, now) { Gui = new GuiLocalSmoke(directory) };
                pending.Gui.Begin();
                Report(command, "started", "Fixed settings lifecycle and navigation assertions; options are not changed", now);
            }
            else if (command == "freeplay")
            {
                var mainMenu = UObject.FindObjectOfType<MainMenuManager>();
                if (!client || client.GameState != InnerNetClient.GameStates.NotJoined || client.AmConnected ||
                    SceneManager.GetActiveScene().name != "MainMenu" || !mainMenu)
                {
                    Report(command, "rejected", "Requires the main menu and an initialized AmongUsClient", now);
                    return;
                }
                FreeplayPopover? popover = null;
                foreach (var candidate in UObject.FindObjectsOfType<FreeplayPopover>(true))
                {
                    if (candidate.gameObject.scene.name == "MainMenu" && candidate.hostGameButton &&
                        candidate.hostGameButton.NetworkMode == NetworkModes.FreePlay)
                    {
                        popover = candidate;
                        break;
                    }
                }
                if (popover == null || !popover)
                {
                    Report(command, "rejected", "No loaded FreeplayPopover with a FreePlay host button", now);
                    return;
                }
                pending = new Pending(command, now);
                mainMenu.OpenGameModeMenu();
                popover.gameObject.SetActive(true);
                popover.Show();
                if (!popover.hostGameButton.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Native FreePlay host button remains inactive");
                Report(command, "started", "Vanilla game-mode menu and FreeplayPopover.Show, then PlayMap(Skeld)", now);
                popover.PlayMap(MapNames.Skeld);
            }
            else if (command == "locallobby")
            {
                string scene = SceneManager.GetActiveScene().name;
                if (!client || client.GameState != InnerNetClient.GameStates.NotJoined || client.AmConnected ||
                    scene is not ("MainMenu" or "MatchMaking"))
                {
                    Report(command, "rejected", "Requires a disconnected main menu or local MatchMaking menu", now);
                    return;
                }
                pending = new Pending(command, now);
                if (scene == "MainMenu")
                {
                    var mainMenu = UObject.FindObjectOfType<MainMenuManager>();
                    if (mainMenu == null || !mainMenu) throw new InvalidOperationException("MainMenuManager unavailable");
                    mainMenu.OpenGameModeMenu();
                    SceneChanger? localEntry = mainMenu.playLocalButton ? mainMenu.playLocalButton.GetComponent<SceneChanger>() : null;
                    if (localEntry == null || !localEntry || localEntry.TargetScene != "MatchMaking")
                    {
                        localEntry = null;
                        foreach (var candidate in UObject.FindObjectsOfType<SceneChanger>(true))
                            if (candidate.gameObject.scene.name == "MainMenu" && candidate.TargetScene == "MatchMaking")
                            { localEntry = candidate; break; }
                    }
                    if (localEntry == null || !localEntry) throw new InvalidOperationException("Native MatchMaking entry unavailable");
                    localEntry.Click();
                }
                Report(command, "started", "Waiting for native LocalGame host button; client connects to 127.0.0.1, native server listens on LAN", now);
            }
            else if (command == "leave")
            {
                string scene = SceneManager.GetActiveScene().name;
                if (client && !client.AmConnected && client.GameState == InnerNetClient.GameStates.NotJoined &&
                    scene is "MainMenu" or "MatchMaking")
                {
                    StopOwnedServer();
                    Report(command, "succeeded", "Already at a disconnected native menu", now);
                    return;
                }
                if (!client || (client.NetworkMode != NetworkModes.FreePlay &&
                    !(client.NetworkMode == NetworkModes.LocalGame && ownsLocalLobby && client.AmHost)))
                {
                    Report(command, "rejected", "Only FreePlay or this helper's native LocalGame host session may be exited", now);
                    return;
                }
                pending = new Pending(command, now);
                Report(command, "started", "Vanilla AmongUsClient.ExitGame", now);
                abilities.RestorePrepared();
                client.ExitGame(DisconnectReasons.ExitGame);
                StopOwnedServer();
            }
            else
            {
                var operation = new Pending(command, now);
                operation.CaptureWork = Task.Run(() =>
                {
                    Directory.CreateDirectory(directory);
                    File.Delete(Path.Combine(directory, "capture.png"));
                    return true;
                });
                pending = operation;
                Report(command, "started", "Preparing fixed capture.png path", now);
            }
        }
        catch (Exception ex)
        {
            pending?.Online?.Stop("failed", ex.GetType().Name);
            pending?.Local?.Stop("failed", ex.GetType().Name);
            pending?.Meeting?.Stop("failed", ex.GetType().Name);
            pending?.TaskCompletion?.Stop("failed", ex.GetType().Name);
            pending?.Abilities?.Stop("failed", ex.GetType().Name);
            pending?.LocalChat?.Fail(ex);
            if (pending?.LobbyUi != null)
            {
                pending.LobbyUi.Stop("failed", ex.GetType().Name);
                QueueWrite("lobby-ui.json", JsonSerializer.Serialize(pending.LobbyUi.Result()), now);
            }
            if (pending?.Roles != null)
            {
                pending.Roles.Stop("failed");
                QueueWrite("gui-roles.json", JsonSerializer.Serialize(pending.Roles.Result()), now);
            }
            if (pending?.Gui != null)
            {
                pending.Gui.Stop("failed");
                QueueWrite("gui-local.json", JsonSerializer.Serialize(pending.Gui.Result()), now);
            }
            if (pending?.Tasks != null)
            {
                pending.Tasks.Stop("failed");
                QueueWrite("gui-tasks.json", JsonSerializer.Serialize(pending.Tasks.Result()), now);
            }
            pending = null;
            Report(command, "failed", ex.GetType().Name, now);
        }
    }

    private void PollPending(double now)
    {
        var operation = pending!;
        try
        {
            if (operation.LobbyUi != null)
            {
                if (operation.LobbyUi.Tick(now))
                {
                    pending = null;
                    QueueWrite("lobby-ui.json", JsonSerializer.Serialize(operation.LobbyUi.Result()), now);
                    QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                    Report(operation.Command, operation.LobbyUi.Outcome, operation.LobbyUi.Detail, now);
                }
                return;
            }
            if (operation.LocalChat != null)
            {
                if (operation.LocalChat.Tick(now))
                {
                    pending = null;
                    QueueWrite("private-chat.json", JsonSerializer.Serialize(operation.LocalChat.Snapshot()), now);
                    QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                    Report(operation.Command, operation.LocalChat.Outcome, operation.LocalChat.Detail, now);
                }
                return;
            }
            if (operation.Abilities != null)
            {
                if (operation.Abilities.Tick(now))
                {
                    pending = null;
                    QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                    Report(operation.Command, operation.Abilities.Outcome, operation.Abilities.Detail, now);
                }
                return;
            }
            if (operation.TaskCompletion != null)
            {
                if (operation.TaskCompletion.Tick(now))
                {
                    pending = null;
                    QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                    Report(operation.Command, operation.TaskCompletion.Outcome, operation.TaskCompletion.Detail, now);
                }
                return;
            }
            if (operation.Meeting != null)
            {
                if (operation.Meeting.Tick(now))
                {
                    string outcome = operation.Meeting.Outcome;
                    string detail = operation.Meeting.Detail;
                    if (outcome == "succeeded" && operation.Command == "local_skip" && !abilities.VerifyAfterSkip(out string failure))
                    { outcome = "failed"; detail = failure; }
                    pending = null;
                    QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                    Report(operation.Command, outcome, detail, now);
                }
                return;
            }
            if (operation.Local != null)
            {
                if (operation.Local.Tick(now))
                {
                    string outcome = operation.Local.Outcome;
                    string detail = operation.Local.Detail;
                    if (outcome == "succeeded" && operation.Command == "local_start" && !abilities.VerifyStart(out string failure))
                    { outcome = "failed"; detail = failure; }
                    if (outcome == "stop_owned_server") { StopOwnedServer(); outcome = "succeeded"; }
                    else if (outcome == "passed") outcome = "succeeded";
                    pending = null;
                    QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                    Report(operation.Command, outcome, detail, now);
                }
                return;
            }
            if (operation.Online != null)
            {
                if (operation.Online.Tick(now))
                {
                    pending = null;
                    QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                    Report(operation.Command, operation.Online.Outcome, operation.Online.Detail, now);
                }
                return;
            }
            if (operation.Roles != null)
            {
                bool finished = operation.Roles.Tick();
                string? failure = operation.Roles.Failure;
                if (failure != null || finished || now - operation.Started >= RoleActivationSmoke.TimeoutSeconds)
                {
                    string state = failure != null ? "failed" : finished ? "succeeded" : "timed_out";
                    operation.Roles.Stop(state);
                    QueueWrite("gui-roles.json", JsonSerializer.Serialize(operation.Roles.Result()), now);
                    QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                    pending = null;
                    Report(operation.Command, state, failure ?? "Fixed role activation sequence completed or reached its 25 second limit", now);
                }
                return;
            }
            if (now - operation.Started >= TimeoutSeconds)
            {
                if (operation.Gui != null)
                {
                    operation.Gui.Stop("timed_out");
                    QueueWrite("gui-local.json", JsonSerializer.Serialize(operation.Gui.Result()), now);
                }
                if (operation.Tasks != null)
                {
                    operation.Tasks.Stop("timed_out");
                    QueueWrite("gui-tasks.json", JsonSerializer.Serialize(operation.Tasks.Result()), now);
                }
                pending = null;
                Report(operation.Command, "timed_out", "8 second operation timeout; no automatic retry", now);
                return;
            }
            bool complete = false;
            if (operation.Tasks != null)
            {
                complete = operation.Tasks.Tick();
                if (operation.Tasks.Failure != null)
                {
                    operation.Tasks.Stop("failed");
                    QueueWrite("gui-tasks.json", JsonSerializer.Serialize(operation.Tasks.Result()), now);
                    pending = null;
                    Report(operation.Command, "failed", operation.Tasks.Failure, now);
                    return;
                }
                if (complete) QueueWrite("gui-tasks.json", JsonSerializer.Serialize(operation.Tasks.Result()), now);
            }
            else if (operation.Gui != null)
            {
                complete = operation.Gui.Tick();
                if (operation.Gui.Failure != null)
                {
                    operation.Gui.Stop("failed");
                    QueueWrite("gui-local.json", JsonSerializer.Serialize(operation.Gui.Result()), now);
                    pending = null;
                    Report(operation.Command, "failed", operation.Gui.Failure, now);
                    return;
                }
                if (complete) QueueWrite("gui-local.json", JsonSerializer.Serialize(operation.Gui.Result()), now);
            }
            else if (operation.Command == "freeplay")
            {
                var client = AmongUsClient.Instance;
                complete = client && client.NetworkMode == NetworkModes.FreePlay &&
                    SceneManager.GetActiveScene().name == "Tutorial" && PlayerControl.LocalPlayer && ShipStatus.Instance;
            }
            else if (operation.Command == "locallobby")
            {
                var client = AmongUsClient.Instance;
                if (!operation.HostRequested && SceneManager.GetActiveScene().name == "MatchMaking")
                {
                    foreach (var button in UObject.FindObjectsOfType<HostLocalGameButton>(true))
                    {
                        if (button.gameObject.scene.name != "MatchMaking" || button.NetworkMode != NetworkModes.LocalGame) continue;
                        // Activate only the loaded native LocalGame button's scene ancestry; never an online entry.
                        var node = button.transform;
                        while (node) { node.gameObject.SetActive(true); node = node.parent; }
                        if (!button.gameObject.activeInHierarchy) throw new InvalidOperationException("Native LocalGame host button inactive");
                        operation.HostRequested = true;
                        ownsLocalLobby = true;
                        button.OnClick();
                        break;
                    }
                }
                complete = client && client.NetworkMode == NetworkModes.LocalGame && client.AmHost &&
                    client.GameState == InnerNetClient.GameStates.Joined && PlayerControl.LocalPlayer &&
                    PlayerControl.LocalPlayer.Data != null && DestroyableSingleton<GameStartManager>.InstanceExists && !ShipStatus.Instance;
            }
            else if (operation.Command == "leave")
            {
                var client = AmongUsClient.Instance;
                string scene = SceneManager.GetActiveScene().name;
                complete = client && !client.AmConnected && client.GameState == InnerNetClient.GameStates.NotJoined &&
                    scene is "MainMenu" or "MatchMaking";
            }
            else if (operation.CaptureWork?.IsCompleted == true)
            {
                bool ready = operation.CaptureWork.GetAwaiter().GetResult();
                operation.CaptureWork = null;
                if (!operation.CaptureRequested)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(directory, "capture.png"));
                    operation.CaptureRequested = true;
                }
                else if (ready) complete = true;
            }

            if (complete)
            {
                pending = null;
                QueueWrite("snapshot.json", JsonSerializer.Serialize(Snapshot()), now);
                Report(operation.Command, "succeeded", "Completed; snapshot.json queued", now);
            }
            else if (operation.Command == "capture" && operation.CaptureRequested &&
                operation.CaptureWork == null && now >= operation.NextCaptureCheck)
            {
                operation.NextCaptureCheck = now + 0.25;
                operation.CaptureWork = Task.Run(() =>
                {
                    string path = Path.Combine(directory, "capture.png");
                    if (!File.Exists(path)) return false;
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (file.Length < 36) return false;
                    byte[] magic = new byte[8];
                    if (file.Read(magic, 0, magic.Length) != 8 || magic[0] != 137 || magic[1] != 80 ||
                        magic[2] != 78 || magic[3] != 71 || magic[4] != 13 || magic[5] != 10 || magic[6] != 26 || magic[7] != 10)
                        return false;
                    file.Seek(-12, SeekOrigin.End);
                    byte[] end = new byte[12];
                    return file.Read(end, 0, end.Length) == end.Length && end[0] == 0 && end[1] == 0 &&
                        end[2] == 0 && end[3] == 0 && end[4] == 73 && end[5] == 69 && end[6] == 78 && end[7] == 68 &&
                        end[8] == 174 && end[9] == 66 && end[10] == 96 && end[11] == 130;
                });
            }
        }
        catch (Exception ex)
        {
            operation.Online?.Stop("failed", ex.GetType().Name);
            operation.Local?.Stop("failed", ex.GetType().Name);
            operation.Meeting?.Stop("failed", ex.GetType().Name);
            operation.TaskCompletion?.Stop("failed", ex.GetType().Name);
            operation.Abilities?.Stop("failed", ex.GetType().Name);
            operation.LocalChat?.Fail(ex);
            if (operation.LobbyUi != null)
            {
                operation.LobbyUi.Stop("failed", ex.GetType().Name);
                QueueWrite("lobby-ui.json", JsonSerializer.Serialize(operation.LobbyUi.Result()), now);
            }
            if (operation.LocalChat != null)
                QueueWrite("private-chat.json", JsonSerializer.Serialize(operation.LocalChat.Snapshot()), now);
            if (operation.Roles != null)
            {
                operation.Roles.Stop("failed");
                QueueWrite("gui-roles.json", JsonSerializer.Serialize(operation.Roles.Result()), now);
            }
            if (operation.Gui != null)
            {
                operation.Gui.Stop("failed");
                QueueWrite("gui-local.json", JsonSerializer.Serialize(operation.Gui.Result()), now);
            }
            if (operation.Tasks != null)
            {
                operation.Tasks.Stop("failed");
                QueueWrite("gui-tasks.json", JsonSerializer.Serialize(operation.Tasks.Result()), now);
            }
            pending = null;
            Report(operation.Command, "failed", ex.GetType().Name, now);
        }
    }

    private void StopOwnedServer()
    {
        abilities.RestorePrepared();
        if (ownsLocalLobby && DestroyableSingleton<InnerNetServer>.InstanceExists)
            DestroyableSingleton<InnerNetServer>.Instance.StopServer();
        ownsLocalLobby = false;
    }

    private object Snapshot()
    {
        var client = AmongUsClient.Instance;
        var data = GameData.Instance;
        var ui = new List<object>();
        foreach (var item in UObject.FindObjectsOfType<MainMenuManager>(true)) AddUi(ui, item, "MainMenuManager");
        foreach (var item in UObject.FindObjectsOfType<RegionMenu>(true)) AddUi(ui, item, "RegionMenu");
        foreach (var item in UObject.FindObjectsOfType<FreeplayPopover>(true)) AddUi(ui, item, "FreeplayPopover");
        foreach (var item in UObject.FindObjectsOfType<GameSettingMenu>(true)) AddUi(ui, item, "GameSettingMenu");
        foreach (var item in UObject.FindObjectsOfType<GameOptionsMenu>(true)) AddUi(ui, item, "GameOptionsMenu");
        foreach (var item in UObject.FindObjectsOfType<HostLocalGameButton>(true)) AddUi(ui, item, "HostLocalGameButton");
        foreach (var item in UObject.FindObjectsOfType<SceneChanger>(true)) AddUi(ui, item, "SceneChanger");
        return new
        {
            utc = DateTimeOffset.UtcNow.ToString("O"),
            scene = SceneManager.GetActiveScene().name,
            mod_instance_present = TOHE.Main.Instance != null,
            mod_game_loaded = TOHE.Main.GameIsLoaded,
            mod_options_backup_present = TOHE.Main.RealOptionsData != null,
            mod_player_state_count = TOHE.Main.PlayerStates.Count,
            mod_is_host = client ? (bool?)TOHE.GameStates.IsModHost : null,
            mod_is_lobby = client ? (bool?)TOHE.GameStates.IsLobby : null,
            mod_is_ended = client ? (bool?)TOHE.GameStates.IsEnded : null,
            mod_registration_guid = CurrentModRegistration.ModRegistrationGuidString,
            network_mode = client ? client.NetworkMode.ToString() : null,
            game_state = client ? client.GameState.ToString() : null,
            player_count = data ? data.PlayerCount : 0,
            local_is_host = client && client.AmHost,
            local_player_present = (bool)PlayerControl.LocalPlayer,
            pending_command = pending?.Command,
            logged_error_count = SmokeErrorCounter.Count,
            online = online.Snapshot(),
            local = local.Snapshot(),
            meeting = meeting.Snapshot(),
            task_completion = taskCompletion.Snapshot(),
            abilities = abilities.Snapshot(),
            private_chat = localChat.Snapshot(),
            lobby_layout = LobbyUiSmoke.Layout(),
            credentials_layout = CredentialsLayout(),
            gui_objects = ui
        };
    }

    private static object? CredentialsLayout()
    {
        // A single fixed diagnostic object; no arbitrary object lookup and no text reads.
        foreach (var text in UObject.FindObjectsOfType<TMPro.TextMeshPro>(true))
        {
            if (text.gameObject.name != "TOHEECredentials" || !text.gameObject.scene.IsValid()) continue;
            var position = text.transform.position;
            var local = text.transform.localPosition;
            var camera = Camera.main;
            var screen = camera ? camera.WorldToScreenPoint(position) : Vector3.zero;
            var rect = text.GetComponent<RectTransform>();
            return new
            {
                scene = text.gameObject.scene.name,
                world_position = new { x = position.x, y = position.y, z = position.z },
                local_position = new { x = local.x, y = local.y, z = local.z },
                screen_position = camera ? new { x = screen.x, y = screen.y, z = screen.z } : null,
                screen_width = Screen.width, screen_height = Screen.height,
                rect_size = rect ? new { width = rect.rect.width, height = rect.rect.height } : null,
                font = text.font ? text.font.name : null,
                font_size = text.fontSize, enabled = text.enabled,
                active = text.gameObject.activeInHierarchy,
                renderer_enabled = text.renderer && text.renderer.enabled
            };
        }
        return null;
    }

    private static void AddUi(List<object> destination, Component component, string type)
    {
        if (destination.Count >= 64 || !component.gameObject.scene.IsValid()) return;
        // Structural object names only: no UI text, player names, credentials or account IDs.
        destination.Add(new { type, name = component.gameObject.name, scene = component.gameObject.scene.name,
            active = component.gameObject.activeInHierarchy });
    }

    private void Report(string command, string state, string detail, double now)
    {
        string json = JsonSerializer.Serialize(new { sequence = ++sequence, utc = DateTimeOffset.UtcNow.ToString("O"), command, state, detail });
        log.LogInfo($"{command}: {state}: {detail}");
        QueueWrite("status.json", json, now);
    }

    private Task QueueWrite(string name, string content, double now)
    {
        writeTail = writeTail.ContinueWith(_ =>
        {
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, name);
            string temporary = target + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, target, true);
        }, TaskScheduler.Default);
        writes.Add(new WriteWork(writeTail, now, name));
        return writeTail;
    }

    private void CheckWrites(double now)
    {
        for (int index = writes.Count - 1; index >= 0; --index)
        {
            var work = writes[index];
            if (work.Task.IsCompleted)
            {
                if (work.Task.IsFaulted) log.LogError($"output_io failed: {work.Name}: {work.Task.Exception?.GetBaseException().GetType().Name}");
                writes.RemoveAt(index);
            }
            else if (now - work.Started >= TimeoutSeconds)
            {
                log.LogError($"output_io timed_out: {work.Name}: 8 second write timeout");
                writes.RemoveAt(index);
            }
        }
    }

    private sealed class Pending(string command, double started)
    {
        internal string Command { get; } = command;
        internal double Started { get; } = started;
        internal bool CaptureRequested;
        internal bool HostRequested;
        internal double NextCaptureCheck;
        internal Task<bool>? CaptureWork;
        internal GuiLocalSmoke? Gui;
        internal GuiTasksSmoke? Tasks;
        internal RoleActivationSmoke? Roles;
        internal OnlineSmoke? Online;
        internal LocalPlaySmoke? Local;
        internal MeetingSmoke? Meeting;
        internal TaskCompletionSmoke? TaskCompletion;
        internal AbilitySmoke? Abilities;
        internal LocalChatSmoke? LocalChat;
        internal LobbyUiSmoke? LobbyUi;
    }

    private sealed record WriteWork(Task Task, double Started, string Name);
}
