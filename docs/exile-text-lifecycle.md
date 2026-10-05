# Confirm-ejection text lifecycle

The old confirmation policy generated a custom message but only scheduled a player-name change four seconds after voting, then restored it seven seconds after voting. This did not update the exile controller's cached text. A LocalGame host can click Proceed before the name change, or wait until after restoration; either path lets the native cutscene capture the original name and display vanilla text despite Role confirmation mode.

The 2026.8.18 source confirms the actual chain:

- `MeetingHud.cs:328–339`: `CoStartCutscene` fades for one second and calls `BeginForGameplay`.
- `MeetingHud.cs:991–998`: `RpcClose` calls `Close` locally before queuing the reliable close message.
- `ExileController.cs:105–123`: `BeginForGameplay` takes the default outfit and confirmation options, then calls `Begin`.
- `ExileController.cs:126–167`: `Begin` calculates `completeString` once from that outfit name.
- `ExileController.cs:73–90` and `AirshipExileController.cs:93–117`: text animation uses `completeString`; restoring or changing a player name afterward does not recalculate it.
- `ExileController.cs:238`: `ReEnableGameplay` is a normal `void` lifecycle method, not an iterator factory.

The source above is the read-only local reference `D:/SharedUserFiles/TestDesktop/opencode-chat/assembly-compare/src-2026.8.18`.

`ExileText` now keeps the existing host-generated anonymous/team/role/winner/remaining-count policy. It synchronizes a bounded message with room, meeting and player identity plus an explicit AntiBlackout presentation flag. The actual `MeetingHud.Close` transition sends the compatibility name before the queued vanilla close. `BeginForGameplay` postfix sets the modded client's cached text directly, applies late delivery to the active cutscene, and suppresses the separate native remaining-count label. Ordinary ties/skips cannot inherit an exile message; explicit AntiBlackout null/tie presentation can.

Real names remain visible throughout the results screen. The actual exile-end lifecycle restores the compatibility name once. New meetings restore/clear previous owned context, and room join/disconnection clear context without sending old-room packets. This code does not run role/winner callbacks a second time or alter persistent game options.

The cached same-date Steam x86 analysis lists `BeginForGameplay` as matched 1/1, `Begin` as matched 2/2, and `ReEnableGameplay` as matched 2/2. `MeetingHud.Close` is used-by-inline 2/4; the `HandleText` factory is potentially inlined 5/0. The fix does not hook `HandleText`. These are comparison labels for Steam; the repository targets Itch x86, so they do not establish the target binary's actual inline behavior or installed-hook coverage.

`tests/ExileText` directly links the production service and compatibility guard. Its 39 payload/lifecycle checks pass. Engine objects and delivery are stubs: native detour invocation, packet order/censorship for vanilla recipients, and actual base/Airship text rendering still require in-game validation.

2026-10-05 local Itch `.4` runtime validation: two matching mod clients joined and
started a native loopback game. The nonhost requested an ordinary meeting, both
used native selection/Confirm to vote for the host, and the host waited eight
seconds after observing both vote icons before clicking native Proceed. Both
clients observed the actual cached text and fully revealed TMP text remain equal
to the expected message, then observed host ejection and real-name restoration.
The client compared against the one real host RPC123/190 payload; the host compared
against the compatibility name it actually applied, not the internal builder
field. Both recorded zero Error/Fatal logs throughout this run. Native screenshots
were inspected: colored role and remaining-impostor text are readable. Reports
and captures are in ignored `artifacts/gui-fixes-20261005/`.

This validates the base Skeld cutscene on two modded clients. Airship, a true
vanilla recipient and NikoCN delivery are not covered by this scenario.
