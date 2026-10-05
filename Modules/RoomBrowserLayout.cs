using System;
using System.Text;

namespace TOHE;

// Pure layout/text rules also used by the offline room-browser checks.
internal static class RoomBrowserLayout
{
    internal const int MinimumRoomRows = 10;

    internal static int RowsPerColumn(int count, int preferredRows = 6, int maxColumns = 4)
        => Math.Max(preferredRows, (Math.Max(0, count) + maxColumns - 1) / maxColumns);

    internal static float ScrollLimit(int activeRows, int visibleRows, float spacing)
        => Math.Max(0, activeRows - visibleRows) * spacing;

    internal static float RevealOffset(int row, int visibleRows, float spacing, float current, float limit)
    {
        float top = row * spacing;
        float bottom = (row - visibleRows + 1) * spacing;
        return Math.Clamp(current > top ? top : Math.Max(current, bottom), 0, limit);
    }

    internal static string HostDisplay(string trueHostName, string hostName)
    {
        string value = string.IsNullOrWhiteSpace(trueHostName) ? hostName : trueHostName;
        if (string.IsNullOrEmpty(value)) return string.Empty;
        StringBuilder text = new();
        foreach (char c in value)
        {
            // Listing names are remote input. Keep one bounded plain-text line.
            if (char.IsControl(c) || c is '\u2028' or '\u2029') continue;
            text.Append(c);
            if (text.Length >= 48) break;
        }
        if (text.Length > 0 && char.IsHighSurrogate(text[text.Length - 1])) text.Length--;
        return text.ToString();
    }
}
