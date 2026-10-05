using System;
using System.Linq;
using AmongUs.GameOptions;
using InnerNet;

static class Program
{
    static int assertions;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    static NormalGameOptionsV11 Setup(float cooldown = 25)
    {
        var original = new NormalGameOptionsV11(new UnityLogger());
        original.Floats[FloatOptionNames.KillCooldown] = cooldown;
        original.Floats[FloatOptionNames.ShapeshifterCooldown] = 18;
        original.Floats[FloatOptionNames.GuardianAngelCooldown] = 30;
        original.RoleOptions.SetRoleRate(RoleTypes.Scientist, 2, 70);
        original.RoleOptions.SetRoleRate(RoleTypes.GuardianAngel, 1, 100);
        global::Main.NormalOptions = original;
        global::Main.RealOptionsData = new(original);
        GameOptionsManager.Instance.CurrentGameOptions = original;
        PlayerControl.LocalPlayer = new() { AmOwner = true };
        PlayerControl.AllPlayerControls = [PlayerControl.LocalPlayer, new(), new() { Data = new() { Disconnected = true } }];
        AmongUsClient.Instance = new() { AmHost = true, GameState = InnerNetClient.GameStates.Joined };
        GameStates.IsModHost = GameStates.IsNormalGame = true;
        GameStates.IsFreePlay = false;
        GameStates.IsCountDown = false;
        Options.DisableVanillaRoles.Value = false;
        Options.DefaultKillCooldown = 99;
        Trace.Events.Clear();
        return original;
    }
    static void Main()
    {
        foreach (float cooldown in new[] { 25f, 0f })
        {
            var original = Setup(cooldown);
            GameStartManagerBeginGamePatch.DoTasksForBeginGame();
            Check(original.KillCooldown == cooldown && PlayerControl.LocalPlayer.LobbySyncKillCooldown == cooldown,
                "pre-start preparation and lobby sync preserve the original cooldown, including zero");
            Check(Options.DefaultKillCooldown == 99 && Trace.Events.Count == 0,
                "countdown does not apply round defaults or send targeted initial options");
            GameStates.IsCountDown = true;
            ResetStartStatePatch.Prefix(new GameStartManager());
            Check(original.KillCooldown == cooldown && PlayerControl.LocalPlayer.LobbySyncKillCooldown == cooldown,
                "actual cancellation prefix does not overwrite lobby settings with previous-round defaults");
            Check(!GameStartManagerPatch.GameStartManagerUpdatePatch.AlredyBegin,
                "cancellation keeps the native countdown-reset effect");
            GameStates.IsCountDown = false;
            Check(!StartGameHostPatch.SyncInitialGameOptions(), "Joined cannot apply initial role options");
            AmongUsClient.Instance.GameState = InnerNetClient.GameStates.Started;
            var boundary = StartGameHostPatch.StartGameHost();
            Check(boundary.MoveNext() && boundary.Current is WaitForSeconds, "actual start boundary waits after client readiness");
            Check(Trace.Events.SequenceEqual(new[] { "ready" }), "no options or roles before boundary wait");
            Check(boundary.MoveNext(), "actual boundary synchronizes options then yields role assignment");
            var assign = (System.Collections.IEnumerator)boundary.Current;
            while (assign.MoveNext()) { }
            Check(Trace.Events.SequenceEqual(new[] { "ready", "local-options", "remote-options", "roles" }),
                "each connected player's initial options precede role assignment");
            Check(original.KillCooldown == cooldown && original.Floats[FloatOptionNames.ShapeshifterCooldown] == 18 &&
                  global::Main.RealOptionsData.Saved[FloatOptionNames.KillCooldown] == cooldown,
                "temporary initial options do not mutate lobby object or backup");
            Check(!ReferenceEquals(original, GameOptionsManager.Instance.CurrentGameOptions),
                "host receives an independent runtime clone");
            Check(original.RoleOptions.Rates[RoleTypes.GuardianAngel].Count == 1 &&
                  ((NormalGameOptionsV11)GameOptionsManager.Instance.CurrentGameOptions).RoleOptions.Rates[RoleTypes.GuardianAngel].Count == 0,
                "round-only role rate overrides stay on the clone");
        }
        foreach (string mode in new[] { "nonhost", "unmodded", "hide-n-seek", "freeplay" })
        {
            Setup();
            AmongUsClient.Instance.GameState = InnerNetClient.GameStates.Started;
            if (mode == "nonhost") AmongUsClient.Instance.AmHost = false;
            if (mode == "unmodded") GameStates.IsModHost = false;
            if (mode == "hide-n-seek") GameStates.IsNormalGame = false;
            if (mode == "freeplay") GameStates.IsFreePlay = true;
            Il2CppSystem.Collections.IEnumerator native = null;
            Check(StartGameHostPatch.CoStartGameHost_Prefix(AmongUsClient.Instance, ref native) && native == null,
                mode + " retains native host coroutine");
            Check(!StartGameHostPatch.SyncInitialGameOptions() && Trace.Events.Count == 0,
                mode + " cannot send temporary initial options");
        }
        Setup();
        AmongUsClient.Instance.GameState = InnerNetClient.GameStates.Started;
        var stale = StartGameHostPatch.StartGameHost();
        Check(stale.MoveNext(), "stale session fixture reaches boundary wait");
        OnGameJoinedPatch.Generation++;
        Check(!stale.MoveNext() && Trace.Events.SequenceEqual(new[] { "ready" }),
            "session change during boundary wait cancels options and roles");
        var preserved = Setup();
        AmongUsClient.Instance.GameState = InnerNetClient.GameStates.Started;
        Options.DisableVanillaRoles.Value = true;
        Check(StartGameHostPatch.SyncInitialGameOptions() && preserved.RoleOptions.Rates[RoleTypes.Scientist] == (2, 70),
            "disabling vanilla roles preserves original lobby role rates");
        Check(((NormalGameOptionsV11)GameOptionsManager.Instance.CurrentGameOptions).RoleOptions.Rates[RoleTypes.Scientist] == (0, 0),
            "disabling vanilla roles still takes effect on the initial runtime clone");
        Console.WriteLine($"START_OPTIONS_PASS ({assertions} assertions; extracted production preparation, synchronization and final start boundary)");
    }
}
