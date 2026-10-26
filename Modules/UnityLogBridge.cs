using System;
using BepInEx.Logging;

namespace TOHE.Modules;

// BepInEx already captures IL2CPP Unity logs, but excludes its Unity source from
// LogOutput.log by default. Forward only that source through the TOHE source.
internal sealed class UnityLogBridge : ILogListener
{
    public LogLevel LogLevelFilter => LogLevel.All;

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (eventArgs.Source?.SourceName != "Unity" || Main.Logger == null) return;

        string message = (eventArgs.Data?.ToString() ?? string.Empty)
            .Replace("\r", "\\r").Replace("\n", "\\n");
        Main.Logger.Log(eventArgs.Level, $"[{DateTime.Now:HH:mm:ss}][Unity] {message}");
    }

    public void Dispose() { }
}
