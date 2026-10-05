# Exile text lifecycle checks

Run `dotnet run --project tests/ExileText/ExileText.csproj`.

This harness links actual `ExileText` and `RpcCompatibility`. It checks payload/action boundaries, clean results-phase names, compatibility rename at the actual close boundary, direct custom text after native initialization, late delivery, anonymous/team/role text preservation, separate remaining-count suppression, explicit anti-blackout null/tie presentation, restoration, and rejection of stale/invalid contexts.

Engine objects, native packet delivery, and the native exile animation are stubs. Assertions verify the service and lifecycle callbacks directly; they do not prove installed Itch detours, vanilla name-RPC delivery order, censorship behavior, Airship animation rendering, or multiplayer display.
