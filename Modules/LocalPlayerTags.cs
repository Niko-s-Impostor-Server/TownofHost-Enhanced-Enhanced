using System;
using System.IO;

namespace TOHE.Modules;

// Only the host's fixed local file grants permissions. Caller-supplied RPC identities
// are deliberately absent from this API; authorization reads the live sender Data.
public static class LocalPlayerTags
{
#if ANDROID
    private static string ConfigPath => Path.Combine(UnityEngine.Application.persistentDataPath, "TOHE-DATA", "LocalPlayerTags.json");
#else
    private static string ConfigPath => Path.Combine("TOHE-DATA", "LocalPlayerTags.json");
#endif
    private static LocalPlayerTagsConfig config = LocalPlayerTagsConfig.Empty;
    public static int EntryCount => config.Count;

    public static void Reset() => config = LocalPlayerTagsConfig.Empty;

    // Explicit reload only; no file watcher and no automatic game actions.
    // A failed reload immediately revokes all grants from this service.
    public static bool Reload(out string error)
    {
        Reset();
        error = string.Empty;
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost)
        {
            error = "Only the host can load local player tags.";
            return false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            if (!File.Exists(ConfigPath))
            {
                // CreateNew protects existing user edits against an existence-check race.
                using var created = new FileStream(ConfigPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var empty = System.Text.Encoding.UTF8.GetBytes(LocalPlayerTagsConfig.EmptyDocument);
                created.Write(empty, 0, empty.Length);
            }
            using var stream = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > LocalPlayerTagsConfig.MaxFileBytes)
            {
                error = "Configuration exceeds the byte limit.";
                return false;
            }
            // Bound the actual read too, even if the file grows after Length is checked.
            var bytes = new byte[LocalPlayerTagsConfig.MaxFileBytes + 1];
            int length = 0;
            while (length < bytes.Length)
            {
                int read = stream.Read(bytes, length, bytes.Length - length);
                if (read == 0) break;
                length += read;
            }
            Array.Resize(ref bytes, length);
            if (!LocalPlayerTagsConfig.TryParse(bytes, out var loaded, out error)) return false;
            config = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = "Local player configuration could not be read.";
            return false;
        }
    }

    public static bool HasPermission(PlayerControl sender, LocalPlayerPermission permission) =>
        TryGetIdentity(sender, out var friendCode) && config.HasPermission(friendCode, permission);

    public static bool IsDesignatedGameMaster(PlayerControl player) =>
        TryGetIdentity(player, out var friendCode) && config.IsGameMaster(friendCode);

    // Result contains only generated <color=#RRGGBB> markup and sanitized text.
    // Use it solely in name rendering; do not store it as a real name or identity.
    public static bool TryGetRenderedTag(PlayerControl player, bool gradient, out string tag)
    {
        tag = string.Empty;
        return TryGetIdentity(player, out var friendCode) && config.TryGetTag(friendCode, gradient, out tag);
    }

    private static bool TryGetIdentity(PlayerControl player, out string friendCode)
    {
        friendCode = string.Empty;
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || player == null || player.Data == null || player.Data.Disconnected)
            return false;
        friendCode = player.Data.FriendCode;
        return LocalPlayerTagsConfig.IsValidFriendCode(friendCode);
    }
}
