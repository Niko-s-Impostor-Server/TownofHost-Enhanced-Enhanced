using AmongUs.GameOptions;

namespace TOHE.Modules;

internal static class RoundGameOptions
{
    private static readonly Dictionary<byte, IGameOptions> PlayerOptions = [];
    internal static IGameOptions LobbyOptions { get; private set; }

    internal static void Begin()
    {
        var manager = GameOptionsManager.Instance;
        LobbyOptions = manager.CurrentGameOptions;
        var factory = manager.gameOptionsFactory;
        var baseline = factory.ToBytes(LobbyOptions, false);
        PlayerOptions.Clear();
        foreach (var player in Main.AllPlayerControls)
            PlayerOptions[player.PlayerId] = factory.FromBytes(baseline);
        GameOptionsSender.AllSenders.Clear();

        var local = PlayerControl.LocalPlayer;
        var hostOptions = local != null && PlayerOptions.TryGetValue(local.PlayerId, out var options)
            ? options : factory.FromBytes(baseline);
        manager.CurrentGameOptions = hostOptions;
        if (GameManager.Instance != null)
            foreach (var component in GameManager.Instance.LogicComponents.GetFastEnumerator())
                if (component.TryCast<LogicOptions>(out var logicOptions))
                    logicOptions.SetGameOptions(hostOptions);
        if (GameStates.IsNormalGame) AURoleOptions.SetOpt(hostOptions);
    }

    internal static IGameOptions ForPlayer(byte playerId)
    {
        if (LobbyOptions != null && PlayerOptions.TryGetValue(playerId, out var options)) return options;

        var manager = GameOptionsManager.Instance;
        var factory = manager.gameOptionsFactory;
        options = factory.FromBytes(factory.ToBytes(LobbyOptions ?? manager.CurrentGameOptions, false));
        if (LobbyOptions != null) PlayerOptions[playerId] = options;
        return options;
    }

    internal static void SetMaxPlayers(int value)
    {
        foreach (var options in PlayerOptions.Values)
            options.SetInt(Int32OptionNames.MaxPlayers, value);
    }

    internal static void End()
    {
        if (LobbyOptions == null) return;

        var lobbyOptions = LobbyOptions;
        LobbyOptions = null;
        PlayerOptions.Clear();
        GameOptionsManager.Instance.CurrentGameOptions = lobbyOptions;
        if (GameManager.Instance != null)
            foreach (var component in GameManager.Instance.LogicComponents.GetFastEnumerator())
                if (component.TryCast<LogicOptions>(out var logicOptions))
                    logicOptions.SetGameOptions(lobbyOptions);
        if (GameStates.IsNormalGame) AURoleOptions.SetOpt(lobbyOptions);
        GameOptionsSender.AllSenders.Clear();
        GameOptionsSender.AllSenders.Add(new NormalGameOptionsSender());
    }
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
internal static class RoundGameOptionsEndPatch
{
    private static void Finalizer() => RoundGameOptions.End();
}
