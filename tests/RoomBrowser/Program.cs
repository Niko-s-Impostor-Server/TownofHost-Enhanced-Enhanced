using TOHE;

int checks = 0;
void Check(bool result, string name)
{
    if (!result) throw new Exception(name);
    checks++;
}

Check(RoomBrowserLayout.MinimumRoomRows >= 10, "public browser can hold at least ten returned rooms");
for (int count = 0; count <= 200; count++)
{
    int rows = RoomBrowserLayout.RowsPerColumn(count);
    int columns = count == 0 ? 0 : (count + rows - 1) / rows;
    Check(rows >= 6 && columns <= 4, "server layout respects minimum rows and maximum columns");
    var cells = Enumerable.Range(0, count).Select(i => (Column: i / rows, Row: i % rows)).ToArray();
    Check(cells.Distinct().Count() == count, "no server buttons overlap");
}
Check(RoomBrowserLayout.RowsPerColumn(7) == 6, "seventh server starts another column");
Check(RoomBrowserLayout.RowsPerColumn(25) == 7, "large region list grows rows instead of fifth column");
Check(RoomBrowserLayout.ScrollLimit(10, 5, 0.75f) == 3.75f, "all ten rows reachable with five native viewport rows");
Check(RoomBrowserLayout.ScrollLimit(3, 5, 0.75f) == 0f, "smaller refresh cannot retain old scroll extent");
Check(RoomBrowserLayout.ScrollLimit(0, 5, 0.75f) == 0f, "empty response has zero extent");
Check(RoomBrowserLayout.RevealOffset(9, 5, 0.75f, 0f, 3.75f) == 3.75f, "controller can reveal last room");
Check(RoomBrowserLayout.RevealOffset(0, 5, 0.75f, 3.75f, 3.75f) == 0f, "controller can return to first room");
Check(RoomBrowserLayout.RevealOffset(6, 5, 0.75f, 2.25f, 3.75f) == 2.25f, "already-visible controller choice retains scroll");
Check(RoomBrowserLayout.HostDisplay("True Host", "Displayed Name") == "True Host", "true host preferred");
Check(RoomBrowserLayout.HostDisplay(null, "UDP Host") == "UDP Host", "UDP listing without true name has host fallback");
Check(RoomBrowserLayout.HostDisplay("  ", "Fallback") == "Fallback", "blank true name falls back");
Check(RoomBrowserLayout.HostDisplay(null, null) == "", "missing names are safe");
Check(RoomBrowserLayout.HostDisplay("a\n\t\u2028b", null) == "ab", "remote name cannot inject extra lines");
Check(RoomBrowserLayout.HostDisplay(new string('x', 300), null).Length == 48, "remote text length bounded");
Check(RoomBrowserLayout.HostDisplay(new string('x', 47) + "\U0001F600", null).Length == 47, "name cap does not leave a split UTF-16 surrogate");
Check(RoomBrowserLayout.HostDisplay("<color=red>host</color>", null) == "<color=red>host</color>", "display keeps literal content for richText=false");
Console.WriteLine($"Room browser layout: {checks} checks passed.");
