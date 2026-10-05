# Presentation recovery offline checks

Run `dotnet run --project tests/PresentationRecovery/PresentationRecovery.csproj`.

The test links production `PresentationRecovery` and `RpcCompatibility` directly. It verifies readiness rejection through intro, meeting/exile, minigame, role blackout, vent and ladder transitions; preserves colored/translucent effects and another camera target; checks targeted compatible requests, host authority, recipient throttling and failed local attempts; and verifies stale opaque black-screen repair preserves tasks, role, cooldown, movement permission, position, and death state.

Unity/game objects and transport are boundary stubs. This proves service branches and selected property changes offline, not actual IL2CPP camera rendering, native HUD refresh, network delivery, or multiplayer recovery. Recovery intentionally requires the current compatible mod for remote recipients.
