using TOHE;

static class Program
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        checks++;
    }
    static HudStatusRect Place(HudStatusRect safe, float width, float height, HudStatusRect[] obstacles, float gap = 6)
    {
        Check(HudStatusLayout.TryPlace(safe, width, height, obstacles, gap, out var placed), "status block has a free position");
        Check(placed.Left >= safe.Left + gap && placed.Bottom >= safe.Bottom + gap &&
            placed.Right <= safe.Right - gap && placed.Top <= safe.Top - gap,
            "entire status block remains inside camera/screen safe area");
        Check(Math.Abs(placed.Width - width) < 0.01 && Math.Abs(placed.Height - height) < 0.01,
            "layout preserves text size and readability");
        foreach (var obstacle in obstacles)
        {
            bool separated = placed.Right <= obstacle.Left - gap || placed.Left >= obstacle.Right + gap ||
                placed.Top <= obstacle.Bottom - gap || placed.Bottom >= obstacle.Top + gap;
            Check(separated, "status stays clear of actual obstacle bounds with text-scaled gap");
        }
        return placed;
    }
    static void Main()
    {
        // Landscape resolutions with the same normalized HUD occupancy.
        foreach (var size in new[] { (1920f, 1080f), (1280f, 720f), (1024f, 768f), (2560f, 1080f) })
        {
            var (width, height) = size;
            HudStatusRect safe = new(0, 0, width, height);
            HudStatusRect[] blockers =
            [
                new(width * .76f, height * .85f, width * .995f, height * .99f), // notes/settings/map/chat
                new(width * .01f, height * .93f, width * .62f, height * .99f), // progress bar
                new(width * .01f, height * .55f, width * .40f, height * .91f) // open task panel
            ];
            Place(safe, width * .24f, height * .16f, blockers);
            Place(safe, width * .24f, height * .30f, blockers); // FPS/overlay/long status
        }
        HudStatusRect full = new(0, 0, 1920, 1080);
        var empty = Place(full, 300, 120, []);
        Check(empty.Right == 1914 && empty.Top == 1074, "unobstructed lobby uses safe top-right corner");
        var chatAndButtons = Place(full, 300, 120, [new(1540, 940, 1910, 1070)]);
        Check(chatAndButtons.Top == empty.Top && chatAndButtons.Right < empty.Right,
            "available space beside buttons is preferred before moving downward");
        var allTop = Place(full, 300, 120, [new(0, 920, 1920, 1080)]);
        Check(allTop.Top == 914, "full-width toolbar/task bar moves status underneath with gap");
        var safeInset = Place(new(160, 40, 1760, 1020), 380, 200,
            [new(1420, 900, 1840, 1070), new(20, 930, 900, 1070)]);
        Check(safeInset.Left >= 166 && safeInset.Right <= 1754, "asymmetric safe-area insets and partial camera viewport are respected");
        HudStatusRect meetingSafe = new(0, 0, 1024, 768);
        HudStatusRect[] meetingObstacles =
        [
            new(700, 690, 1024, 768), new(0, 730, 650, 768), // toolbar and task bar
            new(70, 120, 900, 680) // dense meeting vote grid
        ];
        Check(!HudStatusLayout.TryPlace(meetingSafe, 320, 220, meetingObstacles, 6, out _),
            "wide status cannot be placed over a dense meeting grid");
        var wrapped = Place(meetingSafe, 105, 400, meetingObstacles);
        Check(wrapped.Left >= 906, "wrapped same-font status fits the free meeting-side column");
        Check(!HudStatusLayout.TryPlace(full, float.NaN, 120, [], 6, out _) &&
            !HudStatusLayout.TryPlace(full, 300, 0, [], 6, out _) &&
            !HudStatusLayout.TryPlace(full, 3000, 120, [], 6, out _), "invalid/oversize text dimensions fail without overlap");

        Random random = new(20260818);
        for (int i = 0; i < 100; i++)
        {
            float width = random.Next(800, 3000), height = random.Next(650, 1400);
            HudStatusRect safe = new(0, 0, width, height);
            HudStatusRect[] obstacles =
            [
                new(width * .75f, height * .8f, width, height),
                new(0, height * .94f, width * .7f, height)
            ];
            Place(safe, width * .3f, height * .15f, obstacles);
        }
        Console.WriteLine($"HUD status layout: {checks} assertions passed (production pure layout; no Unity/game launch).");
    }
}
