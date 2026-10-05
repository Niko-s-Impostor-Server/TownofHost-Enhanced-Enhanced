# AFK monitor offline checks

Run `dotnet run --project tests/AfkMonitor/AfkMonitor.csproj`.

Both production AFK source files are linked directly. The test drives a simulated monotonic clock through intro grace, inactivity, movement, tasks, chat, venting, meetings, exemptions, reused player IDs, session/round changes, warning/shield/kick modes, host stalls, minimum player count, and loss of host authority.

Only engine/network/player bindings are stubs. These checks verify actual policy and host-service decisions; they do not prove native movement flags, notifications, kill cancellation integration, network kicks, or multiplayer behavior.
