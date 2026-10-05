# Meeting target abilities

The 2026.8.18 native Judge button is an owner-local selection UI for Keeper,
Cleanser, Eraser, Oracle and FortuneTeller. Its click sends TOHE CustomRPC
`MeetingAbilityRequest` (188) to the host. It never invokes native
`JudgeRole.TryOverrule` or sends `QueueOverruleVotes`.

This separation matters on Nmpostor: a native Judge request queues a real vote
overrule and changes exile processing; another native request during the same
meeting may also be flagged. Reusing that RPC for a generic role skill would
change the meaning of an ordinary vote.

## Player behavior

- Matching fork/version modded clients get the Judge target button after the
  host acknowledges that meeting's Ready request. Their regular target/skip
  vote remains a regular vote. No first-vote ability or skip-to-opt-out action
  runs for those clients.
- Unmodded and incompatible clients keep their existing first-vote skill and
  skip-to-opt-out behavior. They do not receive a temporary Judge role.
- Only living, connected, unvoted actors can request an ability. Targets must
  be another living, connected player. Discussion, animation and results do
  not permit ability execution. The host may already have voted while another
  player remains eligible; eligibility uses that actor's real vote area.
- The host retains the role's existing limits, effects and target rules. A
  rejected Judge selection does not spend a use or claim that actor's first
  vote. Skills do not clear or complete any player's vote.
- A pending skill blocks another click. If the result is unobserved for eight
  seconds, the local ability stops for that meeting; the mutation request is
  not retried. Only the harmless Ready handshake retries, at one-second
  intervals for less than eight seconds.

| Role | Host action | Existing restrictions retained |
| --- | --- | --- |
| Keeper | Protect target from exile | One use per meeting, game protection limit, target not already protected |
| Cleanser | Queue addon removal after meeting | One use per meeting, ability limit, Stubborn excluded |
| Eraser | Queue role erasure after meeting | One use per meeting, ability limit, existing taskless/neutral/CopyCat/Stubborn/Eraser exclusions |
| Oracle | Report target's team using configured accuracy | One use per meeting, ability limit, task-based recharge |
| FortuneTeller | Report configured role information | One use per meeting, ability limit, task-based recharge and previously checked target rule |

Tracker is not an adapter and remains disabled by the migration's existing
Tracker handling. The native Judge gameplay ability itself is not replaced.

## Implementation contract

`IMeetingTargetAbility` separates availability, target validation and execution:

```csharp
bool CanUseMeetingAbility(PlayerControl actor);
bool CanTargetMeetingAbility(PlayerControl actor, PlayerControl target);
bool UseMeetingAbility(PlayerControl actor, PlayerControl target);
```

The five adapters validate availability/target before calling their existing
`CheckVote` action. `false` from that existing action indicates a successful
skill; the adapter exposes it as `true`. Existing vanilla vote paths keep their
original behavior. Oracle and FortuneTeller omit the legacy “vote returned”
message for this independent button action.

The host snapshots eligible matching-version players when the meeting begins,
so an ordinary vote arriving before Ready acknowledgement also skips the
legacy ability branch. A skill action still requires that owner's Ready request.
Actor player object, canonical transport owner and custom role instance must
continue to match the captured registration. Results additionally require the
canonical current host sender. A payload cannot choose a different actor.

The temporary Judge object is created only on its owner through
`RoleManager.SetRole`. No native role RPC is sent for this presentation. Native
`CoSetRole` rejects an already-assigned living role before considering the
override flag, and also performs initial-role side effects; it is not used here.

The transition retains exact task objects/completion state, emergency count,
kill cooldown, role metadata and alive-role metadata. A narrow prefix on the
nonempty `ImpostorRole.Deinitialize` prevents Eraser's existing header task from
being destroyed during the local transition. `RoleBehaviour.AdjustTasks` is
empty in this native version and is not patched.

The Judge presentation retains the original `TeamType` and `DefaultGhostRole`.
This is necessary for Eraser: native `RoleManager.AssignRoleOnDeath` reads
`Data.Role.IsImpostor` and `Data.Role.DefaultGhostRole`, independently of saved
role-type metadata. Meeting cleanup never calls `SetRole` on a natively dead
actor, even if the mod's death state has not caught up, so it cannot revive the
player. Replacement player, role and game instances do not receive stale
restoration or ability actions.

An accepted action with remaining uses reconstructs the local Judge object,
preserving that same state again. The provided five roles retain their existing
one-use-per-meeting rule; the common path also supports adapters that permit
more than one action. `IsBlockedByTasks` uses host-acknowledged availability for
the temporary object. Prefixes on its native click, `TryOverrule` and queue
paths stop native Judge mutation before vote state changes.

## Custom packet

All integer fields use Hazel's ordinary fixed-width writer/readers. The
payload length must be exactly 16 bytes.

| Field | Width | Meaning |
| --- | --- | --- |
| Protocol | 1 byte | 1 |
| Kind | 1 byte | Ready=1, Use=2, Result=3 |
| Game ID | 4 bytes | Current room/game identity |
| Meeting NetId | 4 bytes | Current native meeting identity |
| Target | 1 byte | Player ID; 255 for Ready |
| Nonce | 4 bytes | Positive monotonic owner nonce; 0 for Ready |
| Result flags | 1 byte | Request=0; Ready=1/Accepted=2/Rejected=3, bit 128 means available |

Ready/Use requests are addressed to the current host. Results are addressed
only to the requesting owner. A result must match the pending nonce and target;
duplicate Ready and obsolete results cannot reenable the UI. The dispatcher
passes a canonical PlayerControl sender from the transport ownership boundary.

The host replay window is per owner and is cleared at meeting end/replacement.
Rejected action nonces are consumed too, preventing reuse with a different
target. It rejects zero, decreasing and repeated nonces, another game/meeting,
and inactive meeting windows.

## Validation

```powershell
dotnet run --project tests/MeetingAbilities/MeetingAbilities.csproj -c Release
```

The focused harness links the production meeting module, replay policy and
Harmony patch bodies directly. Its generated vote fixture extracts the complete
production `CastVotePatch.Prefix` instead of copying its decision logic. The
92 assertions cover Ready ordering, accepted/rejected requests, malformed
length/protocol/context, canonical host/owner checks, role replacement,
native/mod death ordering, replay, pending timeout, multi-use reconstruction,
task/header/cooldown/emergency preservation, native queue/TryOverrule blocking,
and compatible versus unmodded ordinary target/skip voting.

Game objects, native role operations and RPC delivery are simulated. This
harness does not prove Unity controller navigation, actual role-specific
messages/effects, Nmpostor network behavior, or a full two-client gameplay
meeting. Those require runtime validation against the matching game/server;
no native Judge packet should appear in that validation.

Runtime validation on 2026-10-05 used two Itch 2026.8.18 clients on native LAN.
The host Keeper's native vote-area selection and Judge button consumed exactly
one skill use (3 to 2), with ordinary vote state, tasks and emergency count
unchanged and native overrule queue empty. Both clients completed normal Skip
voting and returned to gameplay. This covers the host-owned skill path; a
non-host Keeper request/result roundtrip and each of the other four abilities
still need separate runtime coverage. The pure fixture additionally rejects
same-version peers with a different commit/branch tag; that final compatibility
change is not evidence of a new runtime run.
