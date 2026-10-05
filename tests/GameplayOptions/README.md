# Gameplay option regression harness

Run `dotnet run --project tests/GameplayOptions/GameplayOptions.csproj`.

The harness extracts the actual production first-kill callback, hidden-role replacement block, add-on name helper, role/sub-role renderers, and extension wrappers at build time. It checks default behavior, reset/override separation, zero cooldowns, disabled hidden rolls, localized and Unicode-safe short names, meeting/game contexts, visibility, brackets, console color tags, and result summaries.

Game objects, translation lookups, colors, cooldown transport, and delayed scheduling are stubs. This verifies the option branches and rendered strings offline; it does not prove native client timer/RPC behavior, actual console rendering, live translation loading, or multiplayer gameplay.
