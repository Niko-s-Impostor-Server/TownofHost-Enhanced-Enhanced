# AFK monitoring

AFK monitoring is disabled by default. When enabled, the host checks live connected players every 0.5 seconds during normal Standard task gameplay. The default idle threshold is 180 seconds, configurable from 30 to 600 seconds. Monitoring pauses with fewer than the configured minimum number of living players (default 3).

Moving at least 0.05 game units from the last activity position, completing tasks, or host-observed chat resets inactivity. Meetings, a paused game clock, intro/exile transitions, AntiBlackout task pauses, immobility, venting, ladder animations, and moving platforms suspend tracking. New players and resumed task stages receive 15 seconds of grace before inactivity accumulates. Host stalls contribute at most one observed second per poll.

The default consequence is one private warning per uninterrupted idle episode. Shield mode exposes `IsShielded` to the host's murder validation until activity resumes. Kick mode warns first, then permits 15 seconds to resume activity before a non-ban kick. It never kicks the host.

The host can exempt a player for the current game. Exemptions survive meetings and clear on new room/game/session identity. Reused player IDs do not inherit another player's exemption or timer.

Integration APIs: `SetupCustomOptions()`, `Tick()`, `Reset()`, `RecordActivity(PlayerControl)`, `SetExempt(PlayerControl, bool)` returning success, `IsShielded(PlayerControl)`, and `GetStatus(PlayerControl)` / `GetStatus()` returning localized text. Option IDs are 61050–61053. The integration must call `RecordActivity` from authenticated host-observed chat/task paths and consult `IsShielded` before applying a player kill.

No AFK action changes roles, fabricates kills, bypasses anti-cheat, or automatically repairs native presentation. The offline harness verifies policy/service state transitions; live native flags, client notification display, kill validation, and multiplayer kick delivery require in-game validation.
