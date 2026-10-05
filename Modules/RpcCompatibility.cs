using System;

namespace TOHE;

internal static class RpcCompatibility
{
    // A known build is required for native buttons repurposed by this protocol.
    // VersionCheat and other forks are deliberately not capabilities.
    internal static bool IsCurrentClient(PlayerControl player)
    {
        if (player == null) return false;
        if (player.AmOwner) return true;
        return Main.playerVersion.TryGetValue(player.OwnerId, out var version) &&
            version.version == Main.version && version.forkId == Main.ForkId &&
            version.tag == $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})";
    }
}
