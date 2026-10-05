using AmongUs.GameOptions;
using Hazel;
using TOHE.Modules;

static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    private static NormalGameOptionsV11 Options(float cooldown = 0, byte map = 5) => new()
    {
        KillCooldown = cooldown, MapId = map, DetectiveRate = 30, ViperRate = 70, JudgeRate = 100
    };

    private static void Reset(bool hns = false)
    {
        AmongUsClient.Instance = new();
        GameOptionsManager.Instance = new();
        GameOptionsManager.Instance.CurrentGameOptions = hns ? new HideNSeekGameOptionsV11 { MapId = 2 } : Options();
        GameManager.Instance = new();
        GameManager.Instance.LogicOptions = hns ? new HnSLogic(GameManager.Instance) : new LogicOptionsNormal(GameManager.Instance);
        GameManager.Instance.LogicComponents = [new OtherLogic(GameManager.Instance), GameManager.Instance.LogicOptions];
        foreach (var component in GameManager.Instance.LogicComponents) component.ClearDirtyFlag();
        GameOptionsSender.AllSenders.Clear();
        GameOptionsSender.AllSenders.Add(new NormalGameOptionsSender());
        GameOptionsFactory.Encoded.Clear();
        PlayerControl.LocalPlayer = new();
        global::Main.AllPlayerControls = [PlayerControl.LocalPlayer, new()];
        GameStates.IsFreePlay = GameStates.IsCountDown = false;
        GameStates.IsHideNSeek = hns;
        OptionItem.Syncs = 0;
    }

    // Same ordering as Harmony's SendAllStreamedObjects prefix and the native
    // GameManager stream serialization; no networking is performed by the stub.
    private static MessageWriter StreamTick()
    {
        InnerNetObjectSerializePatch.Prefix();
        var native = MessageWriter.Get(SendOption.Reliable);
        GameManager.Instance.Serialize(native, initialState: false);
        return native;
    }

    private static void HostDirtyAndSend()
    {
        Reset();
        var options = GameOptionsManager.Instance.CurrentGameOptions;
        Check(!RpcSyncSettingsPatch.Prefix(), "host must suppress legacy RPC2");
        Check(GameManager.Instance.LogicOptions.IsDirty && GameManager.Instance.IsDirty, "options mark native manager dirty");
        Check(OptionItem.Syncs == 1, "host custom option sync requested once");
        Check(AmongUsClient.Instance.Sent.Count == 0, "Prefix uses deferred dirty path");
        Check(ReferenceEquals(GameOptionsManager.Instance.CurrentGameOptions, options), "Prefix does not replace lobby options");
        var native = StreamTick();
        Check(AmongUsClient.Instance.Sent.Count == 1, "dirty lobby options broadcast once");
        var sent = AmongUsClient.Instance.Sent.Single();
        Check(sent.Tags.SequenceEqual(new byte[] { InnerNet.Tags.GameData, 1, 1 }), "sender uses GameManager data/component index, not PlayerControl RPC");
        Check(sent.OptionBytes.Single().SequenceEqual(options.Bytes), "map, legal zero cooldown and three added rates preserved");
        Check(sent.Recycled, "sender recycles writer");
        Check(!GameManager.Instance.LogicOptions.IsDirty, "normal sender consumes options dirt");
        Check(native.OptionBytes.Count == 0, "native delta does not overwrite already sent options");
        StreamTick();
        Check(AmongUsClient.Instance.Sent.Count == 1, "second clean stream tick does not duplicate options");
        Check(AmongUsClient.Instance.LegacyCalls == 0, "no RPC2 sent");
    }

    private static void Boundaries()
    {
        var cases = new (string Name, Action Arrange)[]
        {
            ("nonhost", () => AmongUsClient.Instance.AmHost = false),
            ("FreePlay", () => GameStates.IsFreePlay = true),
            ("disconnected", () => AmongUsClient.Instance.AmConnected = false),
            ("missing client", () => AmongUsClient.Instance = null),
            ("destroyed client", () => AmongUsClient.Instance.Destroyed = true),
            ("missing manager", () => GameManager.Instance = null),
            ("destroyed manager", () => GameManager.Instance.Destroyed = true),
            ("missing logic", () => GameManager.Instance.LogicOptions = null)
        };
        foreach (var test in cases)
        {
            Reset();
            var logic = GameManager.Instance.LogicOptions;
            var options = GameOptionsManager.Instance.CurrentGameOptions;
            test.Arrange();
            Check(!RpcSyncSettingsPatch.Prefix(), test.Name + ": obsolete RPC always suppressed");
            Check(!logic.IsDirty && OptionItem.Syncs == 0, test.Name + ": no dirty/custom sync side effects");
            Check(ReferenceEquals(options, GameOptionsManager.Instance.CurrentGameOptions), test.Name + ": options unchanged");
        }
        Reset();
        GameManager.Instance.LogicOptions.SetDirty();
        GameStates.IsFreePlay = true;
        RpcSyncSettingsPatch.Prefix();
        Check(GameManager.Instance.LogicOptions.IsDirty, "suppression preserves existing native FreePlay dirt");
        foreach (bool missingPlayer in new[] { false, true })
        {
            Reset();
            if (missingPlayer) PlayerControl.LocalPlayer = null;
            else global::Main.AllPlayerControls = [PlayerControl.LocalPlayer];
            Check(!RpcSyncSettingsPatch.Prefix(), "host without custom sync recipient still suppresses RPC2");
            Check(GameManager.Instance.LogicOptions.IsDirty && OptionItem.Syncs == 0, "real SyncAllOptions guard leaves native dirty sync intact");
            StreamTick();
            Check(AmongUsClient.Instance.Sent.Count == 1, "no local/custom recipient does not drop vanilla settings");
        }
    }

    private static void ReplacementAndOtherComponents()
    {
        Reset();
        var stale = GameOptionsManager.Instance.CurrentGameOptions;
        var replacement = Options(27.5f, 4);
        GameOptionsManager.Instance.CurrentGameOptions = replacement;
        RpcSyncSettingsPatch.Prefix();
        ((OtherLogic)GameManager.Instance.LogicComponents[0]).SetDirty();
        var native = StreamTick();
        Check(ReferenceEquals(GameOptionsFactory.Encoded.Single(), replacement), "sender reads latest current object, not LogicOptions cached object");
        Check(!ReferenceEquals(stale, replacement), "replacement regression uses distinct option objects");
        Check(AmongUsClient.Instance.Sent.Single().OptionBytes.Single().SequenceEqual(replacement.Bytes), "replacement rates/cooldown remain intact");
        Check(native.Tags.SequenceEqual(new byte[] { 0 }), "other native dirty component remains serialized");
        Check(((OtherLogic)GameManager.Instance.LogicComponents[0]).Serializations == 1, "only option dirt consumed by sender");

        // Cached sender resolves the current GameManager components across a room
        // transition instead of continuing to consume the old manager's flag.
        var oldLogic = GameManager.Instance.LogicOptions;
        oldLogic.SetDirty();
        GameManager.Instance = new();
        GameManager.Instance.LogicOptions = new LogicOptionsNormal(GameManager.Instance);
        GameManager.Instance.LogicComponents = [GameManager.Instance.LogicOptions];
        StreamTick();
        Check(AmongUsClient.Instance.Sent.Count == 2 && oldLogic.IsDirty, "sender rebinds after manager replacement");
    }

    private static void NativeCallerAndReceive()
    {
        Reset();
        GameManager.Instance.LogicOptions.SyncOptions();
        Check(GameManager.Instance.LogicOptions.IsDirty && OptionItem.Syncs == 1, "native SyncOptions reaches real suppression patch");
        Check(PlayerControl.LocalPlayer.SettingsCalls == 1 && AmongUsClient.Instance.LegacyCalls == 0, "native call retains dirty sync without RPC2");
        StreamTick();
        var receiveOptions = Options(12, 3);
        AmongUsClient.Instance.AmHost = false;
        var receiver = new LogicOptionsNormal(new GameManager());
        receiver.Deserialize(new MessageReader { Options = receiveOptions });
        Check(ReferenceEquals(GameOptionsManager.Instance.CurrentGameOptions, receiveOptions), "native Deserialize installs received current options");
        var writer = MessageWriter.Get(SendOption.Reliable);
        receiver.Serialize(writer);
        Check(writer.OptionBytes.Single().SequenceEqual(receiveOptions.Bytes), "native receiver LogicOptions cache updated as well");
    }

    private static void ExistingRepositoryCallers()
    {
        foreach (bool hns in new[] { false, true })
        {
            Reset(hns);
            var options = GameOptionsManager.Instance.CurrentGameOptions;
            BeginSettingsCaller.Run();
            Check(ReferenceEquals(GameOptionsFactory.Encoded.Single(), options), "begin caller chooses current " + (hns ? "HnS" : "normal") + " options");
            StreamTick();
            Check(AmongUsClient.Instance.Sent.Single().OptionBytes.Single().SequenceEqual(options.Bytes), "begin settings reach data stream unchanged");
            Check(AmongUsClient.Instance.LegacyCalls == 0 && OptionItem.Syncs == 1, "begin never sends RPC2, custom sync still requested");
            Check(ReferenceEquals(GameOptionsManager.Instance.CurrentGameOptions, options), "begin sync does not replace options");
        }
        Reset();
        GameStates.IsCountDown = true;
        ResetStartStatePatch.Prefix(new());
        Check(GameOptionsFactory.Encoded.Count == 1 && ReferenceEquals(GameOptionsFactory.Encoded[0], global::Main.NormalOptions), "countdown cancellation supplies actual current options");
        StreamTick();
        Check(AmongUsClient.Instance.Sent.Count == 1 && AmongUsClient.Instance.LegacyCalls == 0, "cancel settings use data stream");
        Reset();
        ResetStartStatePatch.Prefix(new());
        Check(GameOptionsFactory.Encoded.Count == 0 && OptionItem.Syncs == 0, "reset outside countdown does not sync");
    }

    public static void Main()
    {
        HostDirtyAndSend();
        Boundaries();
        ReplacementAndOtherComponents();
        NativeCallerAndReceive();
        ExistingRepositoryCallers();
        Console.WriteLine($"LobbyOptionsSync: {assertions} assertions passed.");
    }
}
