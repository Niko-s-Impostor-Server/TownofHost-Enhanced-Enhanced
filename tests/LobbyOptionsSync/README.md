# Lobby option synchronization regression

Run from the repository root:

```powershell
$env:TOHE_NATIVE_SOURCE = 'D:\SharedUserFiles\TestDesktop\opencode-chat\assembly-compare\src-2026.8.18'
dotnet run --project tests/LobbyOptionsSync/LobbyOptionsSync.csproj
```

Requires .NET 8 and Python 3. `TOHE_NATIVE_SOURCE` can point to a different local
copy of the **2026.8.18** decompiled source. Its default is the path above. This
test never opens a game, creates a socket, sends a packet, or loads credentials.

The project links the actual `GameOptionsSender`, `NormalGameOptionsSender` and
native `GameLogicComponent`. Build-time extraction executes the actual
`RpcSyncSettingsPatch`, `InnerNetObjectSerializePatch`, `OptionItem.SyncAllOptions`,
native `LogicOptions.SyncOptions/Serialize/Deserialize`, native normal-options
constructor/cache installation, and native `GameManager.IsDirty/Serialize`.
It also extracts the current final settings call from `DoTasksForBeginGame` and
the whole countdown-cancel prefix. Changes to the repository's two call sites,
its mode-specific current-option getters, or native RPC2 handling fail extraction
and require review instead of silently retaining a copied implementation.

Behavior coverage:

- Connected host marks options dirty, synchronizes custom options, and suppresses
  legacy PlayerControl RPC2. The next actual sender tick broadcasts the current
  options as GameManager component data and consumes only that component's dirt.
- Nonhost, FreePlay, disconnected, null/destroyed client or manager, and missing
  LogicOptions suppress RPC2 without introducing synchronization side effects.
  Existing FreePlay dirt is preserved. Real custom-option guards for one player
  or missing LocalPlayer do not prevent the vanilla dirty stream.
- Distinct replacement option objects send current values rather than the native
  LogicOptions object's cached values. Zero kill cooldown, map and representative
  Detective/Viper/Judge rates survive. A manager replacement rebinds the sender.
- Native SyncOptions reaches the patch; native Deserialize installs both the
  current options and the receiver's LogicOptions cache. Both existing repository
  callers preserve their selected Normal/HnS or current countdown options.

Boundaries: Unity object truthiness, IL2CPP collections, network delivery and the
GameOptionsFactory are stubs. The factory encodes representative values so the
test proves input preservation and component framing, **not** the complete V11
binary encoding or peer delivery. HnS lobby selection uses a small LogicOptions
fixture with the extracted native base serializer; seeker gameplay is excluded.
The extracted begin-game fragment excludes random-map selection and unrelated
version checks. This is an offline regression, not a replacement for two-client
game testing.

Review conclusion: native `RpcSyncSettings` only writes RPC2; it never installs
the supplied bytes locally. Suppression therefore drops no prior local update.
Normal and HnS LogicOptions cache their options, while this repository's lobby
sender reads `CurrentGameOptions` and clears option dirt before the native delta
stream. The current call sites serialize those same current options. An arbitrary
future caller supplying an independent byte array would require an explicit
decode/install design and must not be assumed compatible with this prefix.
