using System;
using System.Linq;
using TOHE;
using TOHE.Patches;

static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }
    private static AmongUsClient Reset()
    {
        Trace.Events.Clear();
        ShipStatus.Instance = null;
        GameStates.IsModHost = GameStates.IsHideNSeek = true;
        GameStates.IsNormalGame = GameStates.IsFreePlay = false;
        GameOptionsManager.Instance.CurrentGameOptions.MapId = 3;
        OnGameJoinedPatch.Generation++;
        Options.DisableAirshipMovingPlatform.Value = false;
        return new();
    }
    private sealed class NativeOriginal : Il2CppSystem.Collections.IEnumerator
    {
        public int Moves;
        public object Current => "native-readiness-yield";
        public bool MoveNext() { Trace.Events.Add("native:" + Moves); return Moves++ == 0; }
    }
    private static Il2CppSystem.Collections.IEnumerator Wrap(AmongUsClient client, NativeOriginal original)
    {
        Il2CppSystem.Collections.IEnumerator result = original;
        DleksPatch.Postfix(client, ref result);
        return result;
    }
    private static void DleksGuardsAndOrdering()
    {
        foreach (Action<AmongUsClient> disable in new Action<AmongUsClient>[] {
            c => c.AmHost = false, _ => GameStates.IsModHost = false,
            _ => GameStates.IsHideNSeek = false, _ => GameStates.IsFreePlay = true,
            _ => GameOptionsManager.Instance.CurrentGameOptions.MapId = 0,
            _ => GameOptionsManager.Instance.CurrentGameOptions.MapId = 2,
            _ => GameOptionsManager.Instance.CurrentGameOptions.MapId = 4 })
        {
            var client = Reset(); var original = new NativeOriginal(); disable(client);
            Check(ReferenceEquals(Wrap(client, original), original) && Trace.Events.Count == 0,
                "only current mod host HnS map3 is wrapped");
        }
        var host = Reset(); var native = new NativeOriginal(); var routine = Wrap(host, native);
        Check(!ReferenceEquals(routine, native) && Trace.Events.Count == 0, "wrapper construction does not eagerly run native or load");
        Check(routine.MoveNext() && routine.Current is AsyncHandle && Trace.Events.SequenceEqual(new[] { "load:3" })
            && native.Moves == 0 && host.Spawns == 0, "selected ship starts loading and yields before native is driven");
        var operation = host.ShipLoadingAsyncHandle.Operation; operation.Completed = true;
        Check(routine.MoveNext() && (string)routine.Current == "native-readiness-yield"
            && Trace.Events.SequenceEqual(new[] { "load:3", "spawn", "native:0" }), "load completion and ship spawn precede original native readiness yield");
        Check(ReferenceEquals(ShipStatus.Instance, operation.Result.Component) && host.ShipLoadingAsyncHandle.Operation == null
            && host.ShipPrefabs[3].Requests == 1 && host.ShipPrefabs.Where((_, index) => index != 3).All(p => p.Requests == 0),
            "correct prefab is used and consumed async handle cleared");
        Check(!routine.MoveNext() && native.Moves == 2 && host.Spawns == 1, "original iterator runs to its own completion without duplicate spawn");
        host = Reset(); native = new(); ShipStatus.Instance = new(); routine = Wrap(host, native);
        Check(routine.MoveNext() && native.Moves == 1 && host.Spawns == 0 && host.ShipPrefabs.All(p => p.Requests == 0),
            "existing ship skips preload and preserves original iterator");
        OnGameJoinedPatch.Generation++;
        Check(!routine.MoveNext() && native.Moves == 1, "session change while original is yielded prevents further native MoveNext");
        foreach (bool loseHost in new[] { false, true })
        {
            host = Reset(); native = new(); routine = Wrap(host, native);
            Check(routine.MoveNext(), "stale preload fixture reaches async boundary");
            host.ShipLoadingAsyncHandle.Operation.Completed = true;
            if (loseHost) host.AmHost = false; else OnGameJoinedPatch.Generation++;
            Check(!routine.MoveNext() && native.Moves == 0 && host.Spawns == 0 && ShipStatus.Instance == null,
                "session change or host loss during preload does not spawn or continue native routine");
        }
    }
    private static void DataOnlyMapIcons()
    {
        Reset(); GameStates.IsHideNSeek = false; GameStates.IsNormalGame = true;
        global::Main.NormalOptions.MapId = 3; Options.RandomMapsMode.Value = false; CreateOptionsPickerPatch.SetDleks = false;
        var manager = new GameStartManager(); var original = manager.AllMapIcons[0]; var constructions = GameStartManager.Constructions;
        AllMapIconsPatch.Postfix_AllMapIcons(manager);
        var dleks = manager.AllMapIcons.Single(icon => icon.Name == MapNames.Dleks);
        Check(GameStartManager.Constructions == constructions && manager.AllMapIcons.Count == 2 && !ReferenceEquals(dleks, original),
            "new icon is independent data without constructing or cloning GameStartManager");
        Check(ReferenceEquals(dleks.MapIcon, original.MapIcon) && !ReferenceEquals(dleks.MapImage, original.MapImage)
            && !ReferenceEquals(dleks.NameImage, original.NameImage) && original.Name == MapNames.Skeld,
            "map icon shares intended icon reference, replaces banners and preserves original data");
        Check(global::Main.NormalOptions.MapId == 0 && manager.MapUpdates == 1 && CreateOptionsPickerPatch.SetDleks,
            "normal Dleks normalization preserves lobby map image and picker flag");
        AllMapIconsPatch.Postfix_AllMapIcons(manager);
        Check(manager.AllMapIcons.Count == 2 && GameStartManager.Constructions == constructions, "repeated Start handling is idempotent for icon data");
        Reset(); global::Main.HideNSeekOptions.MapId = 3; Options.RandomMapsMode.Value = true; CreateOptionsPickerPatch.SetDleks = false;
        manager = new(); AllMapIconsPatch.Postfix_AllMapIcons(manager);
        Check(global::Main.HideNSeekOptions.MapId == 0 && manager.MapUpdates == 1 && !CreateOptionsPickerPatch.SetDleks,
            "HnS Dleks normalization respects random-map picker behavior");
        AllMapIconsPatch.Postfix_AllMapIcons(null);
        Check(GameStartManager.Constructions == constructions + 1, "missing manager causes no clone or construction");
    }
    private static void CurrentMovingPlatformOption()
    {
        Reset(); var ship = new AirshipStatus(); ShipStatus.Instance = ship;
        var platform = new MovingPlatformBehaviour(); Options.DisableAirshipMovingPlatform.Value = true;
        Check(!MovingPlatformBehaviourPatch.SetTarget_Prefix() && !MovingPlatformBehaviourPatch.Use_Prefix(),
            "disabled SetTarget and Use are blocked before Unity Start reads any option");
        Check(!MovingPlatformBehaviourPatch.SetSide_Prefix(platform) && !platform.Dirty && platform.Cleans == 1,
            "disabled SetSide rejects native mutation and clears dirty state");
        platform.Dirty = true; MovingPlatformBehaviourPatch.Start_Prefix(platform);
        Check(ReferenceEquals(platform.transform.localPosition, platform.DisabledPosition) && ship.outOfOrderPlat.Active
            && !platform.Dirty && platform.Cleans == 2, "disabled Start positions platform, activates outage visual and marks clean");
        Options.DisableAirshipMovingPlatform.Value = false; var enabled = new MovingPlatformBehaviour();
        Check(MovingPlatformBehaviourPatch.SetTarget_Prefix() && MovingPlatformBehaviourPatch.Use_Prefix()
            && MovingPlatformBehaviourPatch.SetSide_Prefix(enabled) && enabled.Cleans == 0 && enabled.Dirty,
            "new lobby option takes effect immediately without Start or a cached prior disabled value");
        MovingPlatformBehaviourPatch.Start_Prefix(enabled);
        Check(enabled.transform.localPosition == null && enabled.Cleans == 0 && enabled.Dirty,
            "enabled Start leaves native placement and dirty state available");
        Options.DisableAirshipMovingPlatform.Value = true;
        Check(!MovingPlatformBehaviourPatch.SetTarget_Prefix() && !MovingPlatformBehaviourPatch.Use_Prefix()
            && !MovingPlatformBehaviourPatch.SetSide_Prefix(enabled) && enabled.Cleans == 1,
            "cross-lobby option change back to disabled is effective before another Start");
    }
    private static void Main()
    {
        DleksGuardsAndOrdering(); DataOnlyMapIcons(); CurrentMovingPlatformOption();
        Console.WriteLine($"MAP_LIFECYCLE_PASS ({assertions} assertions; extracted Dleks/MapIcons and linked moving-platform patch, offline fixtures)");
    }
}
