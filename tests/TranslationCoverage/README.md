# Translation resource regression

Run `dotnet run --project tests/TranslationCoverage/TranslationCoverage.csproj -c Release`.
The native source defaults to the supplied 2026.8.18 decompile; override with
`TOHE_NATIVE_SOURCE` if needed. The test embeds all real language JSON files,
extracts the production translation lookup/merge methods, and links the native
StringNames and SupportedLangs enums. Native translation calls are stubbed.

The generator requires the production resource item to use `WithCulture=false`.
Without it, MSBuild classifies dotted Features locale filenames as satellite
resources, so Translator cannot see them in the main assembly. Manifest checks
require both Features JSON files before testing their keys.

1,371 assertions cover 60 feature keys, 14 repaired keys (three overlap), all
16 language lookups with the existing English fallback, replacement parameters,
native string fallback, aliases and invalid-key behavior. This does not assess
translation quality in every language or visual text layout. A separate actual
TOHE.dll manifest check confirmed both Features JSON resources after the fix.
