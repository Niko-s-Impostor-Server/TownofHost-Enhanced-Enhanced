using AmongUs.GameOptions;
using TOHE.Modules;

namespace TOHE;

// Policy is frozen for a room; changing the saved preference cannot strand
// queued messages in a different wire format halfway through that room.
internal static class OfficialAnticheatPolicy
{
    private static bool enabled;
    private static bool forced;
    private static bool roundFrozen;
    private static int canonicalMaxPlayers;

    public static bool SessionFrozen { get; private set; }
    public static bool Forced => SessionFrozen ? forced : ServerRegion.IsOfficialConnection;
    public static bool Enabled => SessionFrozen ? enabled : Forced || Main.OfficialAnticheatSupport?.Value == true;
    public static int CanonicalMaxPlayers => canonicalMaxPlayers;
    public static int PackingLimit => AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost
        ? 10 + 2 * canonicalMaxPlayers : 10;

    public static void BeginLobby()
    {
        forced = AmongUsClient.Instance != null && GameStates.IsOnlineGame && ServerRegion.IsOfficialConnection;
        enabled = forced || Main.OfficialAnticheatSupport?.Value == true;
        SessionFrozen = true;
        roundFrozen = false;
        canonicalMaxPlayers = 0;
        CaptureCanonicalOptions(GameOptionsManager.Instance?.CurrentGameOptions);
    }

    public static void CaptureCanonicalOptions(IGameOptions options)
    {
        if (!SessionFrozen || roundFrozen || options == null) return;
        // Capture committed lobby settings only, never an online count or a
        // player-specific GameOptions built after CoStartGame begins.
        if (AmongUsClient.Instance == null || AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined) return;
        if (options.MaxPlayers > 0 && options.MaxPlayers <= byte.MaxValue)
            canonicalMaxPlayers = options.MaxPlayers;
    }

    public static void FreezeRound()
    {
        if (roundFrozen) return;
        CaptureCanonicalOptions(GameOptionsManager.Instance?.CurrentGameOptions);
        roundFrozen = true;
    }

    public static void EndRound() => roundFrozen = false;

    public static void Reset()
    {
        SessionFrozen = false;
        enabled = forced = roundFrozen = false;
        canonicalMaxPlayers = 0;
    }
}
