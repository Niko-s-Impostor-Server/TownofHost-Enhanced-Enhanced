using System;

namespace TOHE;

// Captured by transactions before yielding. Object identity also protects
// against client IDs being reused by a later connection.
internal readonly record struct OfficialSessionContext(uint LobbyGeneration, IntPtr ClientIdentity,
    int ClientId, int GameId, int HostId, uint RoundIdentity)
{
    private static uint lobbyGeneration;
    private static uint roundIdentity;
    private static bool roundActive;
    private static int observedHostId;

    public static OfficialSessionContext Capture()
    {
        var client = AmongUsClient.Instance;
        return client == null ? default : new(lobbyGeneration, client.Pointer, client.ClientId,
            client.GameId, client.HostId, roundIdentity);
    }

    public bool IsCurrent()
    {
        var client = AmongUsClient.Instance;
        return ClientIdentity != IntPtr.Zero && client != null && client.AmConnected &&
            LobbyGeneration == lobbyGeneration && RoundIdentity == roundIdentity &&
            ClientIdentity == client.Pointer && ClientId == client.ClientId &&
            GameId == client.GameId && HostId == client.HostId;
    }

    public static bool IsCurrent(OfficialSessionContext context) => context.IsCurrent();
    public static void BeginLobby() => Reset();
    public static void BeginRound()
    {
        if (roundActive) return;
        roundActive = true;
        unchecked { roundIdentity++; }
    }
    public static void EndRound()
    {
        roundActive = false;
        unchecked { roundIdentity++; }
    }
    public static void Reset()
    {
        unchecked { lobbyGeneration++; roundIdentity++; }
        roundActive = false;
        observedHostId = AmongUsClient.Instance?.HostId ?? -1;
    }

    public static bool ObserveHostMigration()
    {
        if (AmongUsClient.Instance == null || observedHostId == AmongUsClient.Instance.HostId) return false;
        Reset();
        return true;
    }
}
