Run `dotnet run --project tests/GuiEntrypoints/GuiEntrypoints.csproj -c Release`.

The generator extracts the actual partial-build cancellation/cleanup, passive map display, and lobby music methods. The offline fixture checks interrupted versus completed menu ownership, cancellation before deactivation, registry/navigation cleanup, map label/button consistency without changing settings, and music disable/reenable/idempotence including the game's delayed ambience start. It also checks that existing owner and lobby update callsites reach these helpers in the required order.

Unity objects, native audio, Harmony dispatch, IL2CPP code addresses, controller overlays, and actual coroutine timing are stubbed. Passing this harness does not prove native hook safety or GUI/game audio behavior; those need native metadata/source audit and the separate runtime smoke helper.
