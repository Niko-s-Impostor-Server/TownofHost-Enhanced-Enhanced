namespace TOHE;

internal readonly record struct HudStatusRect(float Left, float Bottom, float Right, float Top)
{
    internal float Width => Right - Left;
    internal float Height => Top - Bottom;
    internal bool Overlaps(HudStatusRect other) => Left < other.Right && Right > other.Left &&
        Bottom < other.Top && Top > other.Bottom;
}

// Pixel-space packing shared by runtime and the offline layout harness. Prefer
// the highest free position, then the rightmost; no resolution-specific offsets.
internal static class HudStatusLayout
{
    internal static bool TryPlace(HudStatusRect safe, float width, float height,
        IReadOnlyList<HudStatusRect> obstacles, float padding, out HudStatusRect result)
    {
        result = default;
        if (!float.IsFinite(width) || !float.IsFinite(height) || !float.IsFinite(padding) || padding < 0 ||
            width <= 0 || height <= 0 ||
            width + padding * 2 > safe.Width || height + padding * 2 > safe.Height) return false;
        List<float> rightEdges = [safe.Right - padding];
        List<float> topEdges = [safe.Top - padding];
        foreach (var obstacle in obstacles)
        {
            if (!obstacle.Overlaps(safe)) continue;
            rightEdges.Add(obstacle.Left - padding);
            topEdges.Add(obstacle.Bottom - padding);
        }
        rightEdges.Sort((a, b) => b.CompareTo(a));
        topEdges.Sort((a, b) => b.CompareTo(a));
        foreach (float top in topEdges)
        {
            foreach (float right in rightEdges)
            {
                var candidate = new HudStatusRect(right - width, top - height, right, top);
                if (candidate.Left < safe.Left + padding || candidate.Bottom < safe.Bottom + padding) continue;
                bool blocked = false;
                foreach (var obstacle in obstacles)
                {
                    var expanded = new HudStatusRect(obstacle.Left - padding, obstacle.Bottom - padding,
                        obstacle.Right + padding, obstacle.Top + padding);
                    if (candidate.Overlaps(expanded)) { blocked = true; break; }
                }
                if (blocked) continue;
                result = candidate;
                return true;
            }
        }
        return false;
    }
}
