using System;

namespace TOHE;

// /cmd is a transport envelope, not a new command or a permission grant.
internal static class HostOnlyChatCommand
{
    private static readonly string[] ExactRoleCommands =
    [
        "/id", "/guesslist", "/gl", "/duel", "/finish", "/reveal",
        "/编号", "/玩家编号", "/玩家id", "/id列表", "/玩家列表", "/列表", "/所有id", "/全部id", "/編號", "/玩家編號",
        "/结束", "/结束会议", "/結束", "/結束會議", "/展示"
    ];

    private static readonly string[] RoleCommandPrefixes =
    [
        "/shoot", "/guess", "/bet", "/st", "/gs", "/bt", "/猜", "/赌", "/賭",
        "/compare", "/cmp", "/比较", "/比較",
        "/sp", "/jj", "/tl", "/trial", "/murder", "/审判", "/判", "/审", "/審判", "/審",
        "/sw", "/swap", "/换票", "/换", "/換票", "/換"
    ];

    [ThreadStatic] private static int depth;
    internal static bool IsActive => depth > 0;

    // Match the server's case-sensitive prefix, including malformed /cmdfoo.
    // Such messages must never enter public history even though they cannot execute.
    internal static bool IsEnvelope(string text) => text != null && text.StartsWith("/cmd", StringComparison.Ordinal);

    internal static bool IsRoleCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(new[] { '\r', '\n' }) >= 0) return false;
        string command = text.Trim();
        foreach (string exact in ExactRoleCommands)
            if (string.Equals(command, exact, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (string prefix in RoleCommandPrefixes)
            if (command.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || (command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && command.Length > prefix.Length
                    && char.IsWhiteSpace(command[prefix.Length]))) return true;
        return false;
    }

    internal static string CreateEnvelope(string command) => "/cmd " + command.Trim();

    internal static bool TryGetCommand(string text, out string command)
    {
        command = string.Empty;
        if (!IsEnvelope(text) || text.Length <= 4 || !char.IsWhiteSpace(text[4]) ||
            text.IndexOfAny(new[] { '\r', '\n' }) >= 0) return false;
        var body = text[4..].Trim();
        if (body.Length == 0 || body == "/") return false;
        if (body[0] != '/') body = "/" + body;
        int separator = 0;
        while (separator < body.Length && !char.IsWhiteSpace(body[separator])) separator++;
        command = separator == body.Length ? body : body[..separator] + " " + body[separator..].TrimStart();
        return true;
    }

    // Dispatch is synchronous on the Unity thread. Do not carry this scope into
    // delayed role effects: their intended public outcome messages remain public.
    internal static IDisposable Enter() => new Scope();

    private sealed class Scope : IDisposable
    {
        private bool disposed;
        internal Scope() => depth++;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            depth--;
        }
    }
}
