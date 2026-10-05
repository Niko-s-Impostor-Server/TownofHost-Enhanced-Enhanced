using System;

namespace TOHE;

// /cmd is a transport envelope, not a new command or a permission grant.
internal static class HostOnlyChatCommand
{
    [ThreadStatic] private static int depth;
    internal static bool IsActive => depth > 0;

    // Match the server's case-sensitive prefix, including malformed /cmdfoo.
    // Such messages must never enter public history even though they cannot execute.
    internal static bool IsEnvelope(string text) => text != null && text.StartsWith("/cmd", StringComparison.Ordinal);

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
