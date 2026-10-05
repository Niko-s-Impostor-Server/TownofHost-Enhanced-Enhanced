# Offline map lifecycle regression

Run `dotnet run --project tests/MapLifecycle/MapLifecycle.csproj` with .NET 8 and Python 3.

The build extracts the actual Dleks `Postfix`/`LoadSelectedShip` and `AllMapIconsPatch.Postfix_AllMapIcons` methods. Only the private Postfix visibility changes so the fixture can invoke it. The moving-platform production patch is linked directly. The extractor rejects executable `Instantiate(...)` calls inside the icon method, and the fixture records GameStartManager constructions and validates distinct icon data, shared icon reference, replacement banners and repeated handling.

Focused scenarios cover HnS/map3/host guards, selected async load and spawn before original iterator execution, existing ship reuse, session change during both async wait and original iterator yield, host loss during preload, map normalization, random-map picker behavior, and moving-platform guards before Start and after option changes. Disabled Start/SetSide clear dirty state; SetTarget/Use/SetSide prevent native mutations; enabled guards pass through.

All engine, prefab, handle, native iterator and client objects are deterministic offline fixtures. The async fixture exposes a controlled completion boundary; it does not emulate Unity Addressables timing or native struct marshalling. The original iterator only records MoveNext/yield ordering, without implementing native role assignment/readiness/Begin. These checks prove managed branch ordering and option lifetime behavior in the fixture. They do not prove actual HnS Dleks ship loading, Airship movement, IL2CPP/Harmony installation, coroutine wrapping, networking or gameplay. No game process, server or packet send is used.
