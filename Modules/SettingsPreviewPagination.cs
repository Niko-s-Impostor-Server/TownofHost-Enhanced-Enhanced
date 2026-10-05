using System;
using System.Text;

namespace TOHE;

internal static class SettingsPreviewPagination
{
    // Keep short option families together, but split any oversized family or
    // active-role list. The footer is added separately by OptionShower.
    internal static List<string> Build(IEnumerable<string> sections, int maxLines, Func<string, bool> fits = null)
    {
        if (maxLines < 1) throw new ArgumentOutOfRangeException(nameof(maxLines));
        List<string> result = [];
        StringBuilder page = new();
        int linesOnPage = 0;
        void Flush()
        {
            if (linesOnPage == 0) return;
            result.Add(page.ToString().TrimEnd('\n'));
            page.Clear();
            linesOnPage = 0;
        }
        foreach (string section in sections)
        {
            if (string.IsNullOrWhiteSpace(section)) continue;
            string[] lines = section.Replace("\r\n", "\n").Trim('\n').Split('\n');
            string block = string.Join("\n", lines);
            if (lines.Length <= maxLines && (fits == null || fits(block)) && linesOnPage > 0 &&
                (linesOnPage + 1 + lines.Length > maxLines || (fits != null && !fits(page.ToString() + "\n" + block)))) Flush();
            if (linesOnPage > 0 && linesOnPage < maxLines) { page.Append('\n'); linesOnPage++; }
            foreach (string line in lines)
            {
                if (linesOnPage == maxLines || (linesOnPage > 0 && fits != null && !fits(page.ToString() + line))) Flush();
                page.Append(line).Append('\n');
                linesOnPage++;
            }
        }
        Flush();
        if (result.Count == 0) result.Add(string.Empty);
        return result;
    }
}
