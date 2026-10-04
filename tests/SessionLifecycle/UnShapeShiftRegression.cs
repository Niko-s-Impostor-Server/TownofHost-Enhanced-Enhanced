using System;
using System.Collections.Generic;
using System.Linq;

static class UnShapeShiftRegression
{
    static void Check(bool value, string message) => Program.Check(value, message);

    static PlayerControl Reset(bool peer = true)
    {
        LateTask.Pending.Clear();
        PlayerControl.AllPlayerControls.Clear();
        Main.UnShapeShifter.Clear();
        Main.CheckShapeshift.Clear();
        Main.PlayerStates = [];
        Main.GameIsLoaded = false;
        GameManager.Instance = new();
        GameStates.IsInGame = GameStates.IsInTask = true;
        GameStates.IsLobby = AntiBlackout.SkipTasks = false;
        AmongUsClient.Instance = new() { AmConnected = true, AmHost = true, HostId = 1 };
        var owner = new PlayerControl { PlayerId = 1 };
        PlayerControl.LocalPlayer = owner;
        PlayerControl.AllPlayerControls.Add(owner);
        if (peer) PlayerControl.AllPlayerControls.Add(new() { PlayerId = 2 });
        Main.UnShapeShifter.Add(1);
        return owner;
    }

    static void IntroCancelsStaleContext()
    {
        var changes = new Dictionary<string, Action>
        {
            ["disconnect"] = () => AmongUsClient.Instance.AmConnected = false,
            ["generation"] = () => OnGameJoinedPatch.Postfix(AmongUsClient.Instance),
            ["client replacement"] = () => AmongUsClient.Instance = new() { AmConnected = true },
            ["missing client"] = () => AmongUsClient.Instance = null,
            ["host loss"] = () => AmongUsClient.Instance.AmHost = false,
            ["host transfer"] = () => AmongUsClient.Instance.HostId++,
            ["room change"] = () => AmongUsClient.Instance.GameId++,
            ["game over"] = () => AmongUsClient.Instance.IsGameOver = true,
            ["lobby"] = () => GameStates.IsLobby = true,
            ["round stopped"] = () => GameStates.IsInGame = false,
            ["game replacement"] = () => GameManager.Instance = new(),
            ["missing game"] = () => GameManager.Instance = null,
            ["new round states"] = () => Main.PlayerStates = [],
        };
        foreach (var (name, change) in changes)
        {
            var owner = Reset();
            IntroCutsceneDestroyPatch.Prefix();
            change();
            LateTask.RunAll();
            Check(owner.ShiftTargets.Count == 0 && owner.Rejects == 0 && !Main.GameIsLoaded,
                "intro callback cancels on " + name);
        }
        var nonHost = Reset();
        AmongUsClient.Instance.AmHost = false;
        IntroCutsceneDestroyPatch.Prefix();
        Check(LateTask.Pending.Count == 0 && nonHost.ShiftTargets.Count == 0, "non-host does not schedule intro RPCs");
    }

    static void IntroTargets()
    {
        var owner = Reset();
        IntroCutsceneDestroyPatch.Prefix();
        Check(LateTask.Pending.Count == 1, "intro schedules one delayed callback");
        LateTask.RunAll();
        Check(owner.ShiftTargets.SequenceEqual(new byte[] { 2 }) && owner.Rejects == 1 && owner.OutfitResets == 1,
            "valid intro preserves shift, reject and force-outfit sequence");
        Check(Main.GameIsLoaded && Main.CheckShapeshift[1] == false, "valid intro marks round ready and clears shift flag");
        LateTask.RunAll();
        Check(owner.ShiftTargets.Count == 1, "delayed task fires once");

        foreach (var kind in new[] { "missing", "null data", "disconnected", "null role", "role changed", "same-type role replaced", "player id reused" })
        {
            owner = Reset();
            IntroCutsceneDestroyPatch.Prefix();
            switch (kind)
            {
                case "missing": PlayerControl.AllPlayerControls.Remove(owner); break;
                case "null data": owner.Data = null; break;
                case "disconnected": owner.Data.Disconnected = true; break;
                case "null role": owner.Role = null; break;
                case "role changed": owner.Role = new RoleBase(); break;
                case "same-type role replaced": owner.Role = new UnShapeShiftRole(); break;
                case "player id reused":
                    PlayerControl.AllPlayerControls.Remove(owner);
                    PlayerControl.AllPlayerControls.Add(new() { PlayerId = 1 });
                    break;
            }
            LateTask.RunAll();
            bool registrationReplaced = kind is "null role" or "role changed" or "same-type role replaced" or "player id reused";
            Check(owner.ShiftTargets.Count == 0 && Main.UnShapeShifter.Contains(1) == registrationReplaced,
                "intro clears stale ownership but preserves replacement registration: " + kind);
            Check(PlayerControl.AllPlayerControls.All(p => p.ShiftTargets.Count == 0), "replacement receives no stale intro RPC: " + kind);
        }
        owner = Reset(peer: false);
        IntroCutsceneDestroyPatch.Prefix();
        LateTask.RunAll();
        Check(owner.ShiftTargets.Count == 0 && Main.UnShapeShifter.Contains(1) && Main.GameIsLoaded,
            "intro safely skips absent peer and retains valid owner for later update");

        owner = Reset();
        IntroCutsceneDestroyPatch.Prefix();
        Main.UnShapeShifter.Remove(1);
        Main.UnShapeShifter.Add(2);
        LateTask.RunAll();
        Check(PlayerControl.AllPlayerControls.All(p => p.ShiftTargets.Count == 0), "intro snapshot ignores removed owner and newly registered IDs");
    }

    static void FixedUpdateTargetsAndGuards()
    {
        var owner = Reset();
        Main.GameIsLoaded = true;
        Main.UnShapeShifter.UnionWith(new byte[] { 3, 4, 5, 6, 7 });
        PlayerControl.AllPlayerControls.Add(new() { PlayerId = 4, Data = null });
        PlayerControl.AllPlayerControls.Add(new() { PlayerId = 5, Data = new() { Disconnected = true } });
        PlayerControl.AllPlayerControls.Add(new() { PlayerId = 6, Role = new RoleBase() });
        PlayerControl.AllPlayerControls.Add(new() { PlayerId = 7, Role = null });
        UnShapeShiftFixedUpdateFixture.Run(owner);
        Check(Main.UnShapeShifter.SetEquals(new byte[] { 1 }), "snapshot update removes all stale entries without collection-modified exception");
        Check(owner.ShiftTargets.SequenceEqual(new byte[] { 2 }) && owner.Rejects == 1 && owner.OutfitResets == 1,
            "fixed update preserves valid owner and sync sequence");
        UnShapeShiftFixedUpdateFixture.Run(owner);
        Check(owner.ShiftTargets.Count == 1, "already shifted owner is not retriggered");

        owner = Reset();
        Main.GameIsLoaded = true;
        Main.UnShapeShifter.Clear();
        Main.UnShapeShifter.Add(99);
        UnShapeShiftFixedUpdateFixture.Run(owner);
        Check(Main.UnShapeShifter.Count == 0, "only missing entries still enter cleanup");
        owner = Reset(peer: false);
        Main.GameIsLoaded = true;
        UnShapeShiftFixedUpdateFixture.Run(owner);
        Check(owner.ShiftTargets.Count == 0 && Main.UnShapeShifter.Contains(1), "fixed update safely skips missing peer");
        PlayerControl.AllPlayerControls.Add(new() { PlayerId = 2, Data = new() { Disconnected = true } });
        UnShapeShiftFixedUpdateFixture.Run(owner);
        Check(owner.ShiftTargets.Count == 0, "disconnected peer is not a shapeshift target");

        var changes = new Dictionary<string, Action<PlayerControl>>
        {
            ["non-host"] = _ => AmongUsClient.Instance.AmHost = false,
            ["outside task phase"] = _ => GameStates.IsInTask = false,
            ["anti-blackout skip"] = _ => AntiBlackout.SkipTasks = true,
            ["non-owner update"] = p => p.AmOwner = false,
            ["mushroom mixup"] = p => p.MushroomMixup = true,
            ["round not ready"] = _ => Main.GameIsLoaded = false,
        };
        foreach (var (name, change) in changes)
        {
            owner = Reset();
            Main.GameIsLoaded = true;
            change(owner);
            UnShapeShiftFixedUpdateFixture.Run(owner);
            Check(owner.ShiftTargets.Count == 0, "fixed update guards RPC on " + name);
        }
        owner = Reset();
        Main.GameIsLoaded = true;
        UnShapeShiftFixedUpdateFixture.Run(owner, lowLoad: true);
        Check(owner.ShiftTargets.Count == 0, "fixed update retains low-load guard");
    }

    public static void Run()
    {
        IntroCancelsStaleContext();
        IntroTargets();
        FixedUpdateTargetsAndGuards();
    }
}
