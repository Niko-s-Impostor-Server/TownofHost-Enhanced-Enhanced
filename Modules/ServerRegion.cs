using System;
using InnerNet;

namespace TOHE.Modules;

internal static class ServerRegion
{
    private static IRegionInfo connectedRegion;
    private static IRegionInfo foundRegion;
    private static int foundGameId;
    private static string foundAddress;
    private static ushort foundPort;

    // The menu selection is restored after a cross-region game-code lookup.
    // Keep the region that supplied the listing until its endpoint is joined.
    public static IRegionInfo Current => connectedRegion ?? ServerManager.Instance?.CurrentRegion;

    public static void ClearLookup()
    {
        foundRegion = null;
        foundAddress = null;
    }

    public static void RememberFoundGame(HttpMatchmakerManager.FindGameByCodeResponse response)
    {
        if (response == null) return;

        foundRegion = ServerManager.Instance?.CurrentRegion?.Duplicate();
        foundGameId = response.Game.GameId;
        foundAddress = response.Game.IPString;
        foundPort = response.Game.Port;
    }

    public static void BeginConnection(int gameId, string address, ushort port)
    {
        connectedRegion = foundRegion != null && foundGameId == gameId &&
                          foundAddress == address && foundPort == port
            ? foundRegion
            : ServerManager.Instance?.CurrentRegion?.Duplicate();
        ClearLookup();
    }

    public static void ClearConnection()
    {
        connectedRegion = null;
        ClearLookup();
    }

    public static bool IsOfficialHost(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;

        // ServerInfo.Ip may be an HTTP URL; PingServer is a bare hostname.
        if (address.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
            address = uri.Host;
        }

        address = address.TrimEnd('.');
        return address.Equals("among.us", StringComparison.OrdinalIgnoreCase) ||
               address.EndsWith(".among.us", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOfficial(IRegionInfo region)
    {
        return region?.TryCast<StaticHttpRegionInfo>() is { } httpRegion &&
               IsOfficialHost(httpRegion.PingServer) && httpRegion.Servers.Length > 0 &&
               httpRegion.Servers.All(server => IsOfficialHost(server.Ip));
    }
}
