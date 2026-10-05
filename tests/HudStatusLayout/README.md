# HUD status placement

```powershell
dotnet run --project tests/HudStatusLayout/HudStatusLayout.csproj -c Release
```

Links the production pixel-space layout policy. Cases cover 16:9, 4:3 and
ultrawide screens, safe-area/camera insets, top-right toolbar, task/progress
panels, expanded status text, dense meeting grids and a wrapped side column.
Every accepted placement retains the requested text size, fits the safe area
and clears all obstacle bounds.

Runtime layout caches the current tracker, camera viewport, screen resolution,
safe area, owner, meeting instance and text/font size. Changes reflow immediately;
stable HUD bounds are probed at most 10 Hz. Unchanged bounds skip packing and TMP
measurement entirely; cached text/style is not reapplied to TMP each frame. The
rectangle packing and text dimensions are unchanged.

Unity renderer bounds, TMP measurement/wrapping and real HUD visibility are
runtime dependencies. This offline test does not validate their visual result
or controller navigation, and launches no game or network connection.
