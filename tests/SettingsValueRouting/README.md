Run `dotnet run --project tests/SettingsValueRouting/SettingsValueRouting.csproj -c Release`.

The generator extracts the production Toggle/Increase/Decrease entry prefixes,
their shared managed update bodies and numeric modifier policy. Cases cover
boolean transitions, integer/float steps and endpoints, every three-value string
transition, nonhost rejection, existing vanilla fallthrough/wrapping and preset
reopen dispatch. Registered mod rows must never call native `UpdateValue`, and
must invoke one setter, notification and owner callback.

Option storage, native UI methods/renderers and network notifications are stubs.
This verifies managed value routing; it does not prove Harmony installation,
IL2CPP inlining, actual settings persistence or multiplayer synchronization.
