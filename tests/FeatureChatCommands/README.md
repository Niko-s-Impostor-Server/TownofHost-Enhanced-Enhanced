# Feature chat command regression checks

Run `dotnet run --project tests/FeatureChatCommands/FeatureChatCommands.csproj` from the repository root. The harness directly links the production `FeatureChatCommands`, `LocalPlayerTags`, and strict configuration parser. It extracts the existing legacy ban/kick target-protection predicates from `ChatCommandPatch` and evaluates them against the same configuration service.

The game-facing APIs are minimal replacements that record mutations. The preset replacement writes filesystem sentinels when invoked, so unauthorized remote `/save` and `/load` tests verify the production dispatcher never reaches that boundary. A temporary working directory keeps the real local tag configuration and sentinel writes isolated.

Coverage includes local ownership plus host identity for file operations, independent command grants, exact `Data.FriendCode` identity, protected moderator targets across both routes, game/target guards, complete execute state changes, denial without mutations, host loss/disconnect/generation changes, invalid reload revocation, and restoring grants after a valid load.

These tests do not execute the full legacy dispatcher, real preset serialization, native sender authentication, IL2CPP, RPC delivery, role selection, or actual game UI. Those require their own focused checks and game validation.
