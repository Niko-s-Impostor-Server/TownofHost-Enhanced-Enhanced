namespace TOHE.Modules;

internal static class PlayerFriendCode
{
    // Native player-info synchronization can replace Data.FriendCode. Resolve
    // the connected owner and reject stale/reused player objects instead.
    internal static string FromOwner(PlayerControl player)
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmConnected || !AmongUsClient.Instance.AmHost
            || player == null || player.Data == null || player.Data.Disconnected || player.OwnerId < 0)
            return string.Empty;
        var owner = AmongUsClient.Instance.FindClientById(player.OwnerId);
        return owner?.Character != null && owner.Character.Pointer == player.Pointer
            ? owner.FriendCode ?? string.Empty : string.Empty;
    }
}
