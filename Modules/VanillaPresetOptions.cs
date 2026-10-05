using System;
using AmongUs.GameOptions;

namespace TOHE.Modules;

public static class VanillaPresetOptions
{
    public sealed class SavedData
    {
        public Dictionary<int, byte[]> Normal { get; init; } = [];
        public Dictionary<int, byte[]> HideNSeek { get; init; } = [];
    }

    public static SavedData Data { get; private set; } = new();
    private static bool applying;
    private static bool pendingLobbyRestore;
    private static bool lobbyRestoreFailed;
    private static bool CanCapture => Options.IsLoaded && AmongUsClient.Instance != null &&
        AmongUsClient.Instance.AmHost && GameStates.IsLobby && !GameStates.IsCoStartGame &&
        GameOptionsManager.Instance != null && GameManager.Instance && GameManager.Instance.LogicOptions != null;

    public static void Load(SavedData data)
    {
        Data = new SavedData
        {
            Normal = data?.Normal ?? [],
            HideNSeek = data?.HideNSeek ?? []
        };
    }

    public static void BeginLobby()
    {
        pendingLobbyRestore = AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;
        lobbyRestoreFailed = false;
    }

    public static bool InitializeLobby()
    {
        if (lobbyRestoreFailed) return false;
        if (!pendingLobbyRestore) return true;
        if (applying || !CanCapture) return false;
        // The last native settings for this mode may belong to another slot
        // (e.g. after hosting HideNSeek). Restore before any outgoing capture.
        if (ForMode(GameOptionsManager.Instance.CurrentGameOptions.GameMode).ContainsKey(OptionItem.CurrentPreset))
            lobbyRestoreFailed = !Apply(null, OptionItem.CurrentPreset);
        pendingLobbyRestore = false;
        return !lobbyRestoreFailed;
    }

    private static Dictionary<int, byte[]> ForMode(GameModes mode) => mode switch
    {
        GameModes.Normal or GameModes.NormalFools => Data.Normal,
        GameModes.HideNSeek or GameModes.SeekFools => Data.HideNSeek,
        _ => throw new InvalidOperationException($"Unsupported preset game mode: {mode}")
    };

    public static void CaptureCurrent()
    {
        if (applying || !CanCapture || !InitializeLobby()) return;
        var manager = GameOptionsManager.Instance;
        // On host migration, CurrentGameOptions contains the inherited lobby
        // settings; GameHostOptions can still contain unrelated local settings.
        var options = manager.CurrentGameOptions;
        ForMode(options.GameMode)[OptionItem.CurrentPreset] = manager.gameOptionsFactory.ToBytes(options, false).ToArray();
    }

    public static bool TrySwitch(int previous, int next)
    {
        if (!CanCapture) return true;
        if (!InitializeLobby()) return false;
        return previous == next || Apply(previous, next);
    }

    private static bool Apply(int? previous, int next)
    {
        var manager = GameOptionsManager.Instance;
        var oldCurrent = manager.CurrentGameOptions;
        var oldHost = manager.GameHostOptions;
        var logic = GameManager.Instance.LogicOptions;
        bool changed = false;
        applying = true;
        try
        {
            var slots = ForMode(oldCurrent.GameMode);
            var outgoing = manager.gameOptionsFactory.ToBytes(oldCurrent, false).ToArray();
            // Old config files have no vanilla snapshots. An unused slot starts
            // from the present host settings; subsequent edits are independent.
            var incoming = slots.TryGetValue(next, out var saved) ? saved : outgoing;
            var restored = manager.gameOptionsFactory.FromBytes(incoming);
            if (restored == null || restored.Version != oldCurrent.Version ||
                ForMode(restored.GameMode) != slots)
                throw new InvalidOperationException("Saved vanilla preset has an incompatible mode or version");

            restored.SetBool(BoolOptionNames.IsDefaults, false);
            if (previous.HasValue) slots[previous.Value] = outgoing;
            changed = true;
            manager.CurrentGameOptions = restored;
            // LogicOptions owns a separate reference used by the lobby/game.
            // Its setter also marks the native options dirty for transmission.
            logic.SetGameOptions(restored);
            AURoleOptions.SetOpt(restored);
            manager.GameHostOptions = restored;
            return true;
        }
        catch (Exception error)
        {
            Logger.Error($"Unable to restore vanilla preset {next + 1}: {error}", "VanillaPresetOptions");
            if (changed)
            {
                manager.CurrentGameOptions = oldCurrent;
                logic.SetGameOptions(oldCurrent);
                AURoleOptions.SetOpt(oldCurrent);
                try { manager.GameHostOptions = oldHost; }
                catch (Exception rollbackError) { Logger.Error($"Host settings rollback failed: {rollbackError}", "VanillaPresetOptions"); }
            }
            return false;
        }
        finally { applying = false; }
    }

    // Native game/map/role editors all commit through this setter. Avoid
    // capturing client syncs or the temporary per-player options used in-game.
    [HarmonyPatch(typeof(GameOptionsManager), nameof(GameOptionsManager.GameHostOptions), MethodType.Setter)]
    private static class SaveHostOptionsPatch
    {
        private static void Postfix()
        {
            if (!applying && CanCapture) OptionSaver.Save();
        }
    }
}
