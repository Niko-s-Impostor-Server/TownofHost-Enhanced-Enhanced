# Missing translation keys

The main cause was packaging: MSBuild treated `Features.en_US.json` and
`Features.zh_CN.json` as culture-specific satellite resources. Translator only
reads `Assembly.GetExecutingAssembly().GetManifestResourceNames()`, so the 57
existing feature keys never reached its map. The main resource item now sets
`WithCulture=false`, keeping every language in TOHE.dll. The actual Release DLL
manifest confirms both `TOHE.Resources.Lang.Features.*.json` entries are present.

English and Simplified Chinese also gained 14 missing keys: NewHideMsg, five
active-settings help descriptions, the Mad- and CoinflipCommandInfo spelling
aliases, three enum display names, and the native Detective/Viper dynamic
description variants. Deprecated/sentinel enum values gain display names only;
no new role behavior or invented ability descriptions are introduced.

The existing missing-language fallback to English remains unchanged. A focused
test with real resource embedding, extracted production lookup methods and
native string/language enums passes 1,371 assertions. Its engine translation
provider is stubbed, so native localization quality and all-language UI layout
remain outside that coverage. Missing-key searches in the current runtime log
found no entries; that absence did not establish completeness and did not reveal
the packaging problem. The manifest check did.
