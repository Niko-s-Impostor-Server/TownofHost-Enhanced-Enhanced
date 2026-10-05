# Meeting ability focused harness

```powershell
dotnet run --project tests/MeetingAbilities/MeetingAbilities.csproj -c Release
```

The project links `Modules/MeetingAbilities.cs`,
`Modules/MeetingAbilityReplayWindow.cs`, `Modules/RpcCompatibility.cs` and
`Patches/MeetingAbilityPatch.cs`.
`generate.py` extracts the production `CastVotePatch.Prefix` for target/skip
vote routing checks. It requires Python and a .NET 8 SDK, and starts no game or
network connection.

The 92 assertions cover the custom request lifecycle, phase/ownership/role/death
validation, replay, timeout, task/header/KCD/emergency state, multi-use Judge
reconstruction, no native queue/TryOverrule execution, menu guards, and modded
versus unmodded vote routing, plus current commit/branch admission and legacy
vote fallback for old tags sharing the same fork/version. Native dependencies and role actions are doubles;
this is not a game-client, role-effect or Nmpostor end-to-end proof.

See `docs/meeting-abilities.md` for behavior and packet layout.
