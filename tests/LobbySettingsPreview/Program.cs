using TOHE;
using UnityEngine;

int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    checks++;
}
var roles = Enumerable.Range(0, 95).Select(i => "Role" + i).ToArray();
var detail = Enumerable.Range(0, 80).Select(i => "Child" + i).ToArray();
var pages = SettingsPreviewPagination.Build([string.Join('\n', roles), "Short\nFamily", string.Join('\n', detail)], 32);
Check(pages.All(page => page.Split('\n').Length <= 32), "oversized role list and option family obey page capacity");
var flattened = pages.SelectMany(page => page.Split('\n')).Where(line => line.Length > 0).ToArray();
Check(flattened.SequenceEqual(roles.Concat(["Short", "Family"]).Concat(detail)), "every row survives pagination in order");
Check(pages.Any(page => page.Contains("Short\nFamily")), "short family remains together");
Check(SettingsPreviewPagination.Build(["", "\n"], 32).SequenceEqual([""]), "empty settings produce one navigable page");
// A fake rendered-height budget models wrapped rows in a narrow window.
var wrapped = SettingsPreviewPagination.Build(["AAAA\nB\nCCCC\nD\nEEEE\nF"], 32,
    value => value.Split('\n').Sum(line => (line.Length + 1) / 2) <= 5);
Check(wrapped.All(page => page.Split('\n').Sum(line => (line.Length + 1) / 2) <= 5), "rendered-height predicate drives splitting before footer space is exhausted");
Check(wrapped.SelectMany(page => page.Split('\n')).Where(line => line.Length > 0).SequenceEqual(["AAAA", "B", "CCCC", "D", "EEEE", "F"]), "rendered-height splitting retains wrapped rows");

var setting = new OptionItem { Name = "VisibleSetting", CurrentValue = 1 };
_ = Options.HideGameSettings;
OptionShower.Reset();
var initial = OptionShower.GetTextNoFresh();
Check(initial.Contains("Vanilla10") && OptionShower.pages.Any(page => page.Contains("VisibleSetting: 1")), "visible lobby includes vanilla and custom settings");
Check(ReferenceEquals(initial, OptionShower.GetTextNoFresh()), "stable frame returns the cached string");
OptionShower.SelectPage(OptionShower.pages.FindIndex(page => page.Contains("VisibleSetting")));
Time.unscaledTime = 1;
setting.CurrentValue = 7;
Check(OptionShower.GetTextNoFresh().Contains("VisibleSetting: 7"), "changes refresh on non-first page");
int count = OptionShower.pages.Count;
for (int i = 0; i < count; i++) OptionShower.Next();
Check(OptionShower.GetTextNoFresh().Contains("VisibleSetting: 7"), "next-page wraps back to selected page");
AmongUsClient.Instance.AmHost = false;
Options.HideGameSettings.CurrentValue = 1;
Time.unscaledTime += 0.01f;
var hidden = OptionShower.GetTextNoFresh();
Check(hidden.Contains("Message.HideGameSettings") && !hidden.Contains("Vanilla") &&
    !hidden.Contains("VisibleSetting") && OptionShower.pages.Count == 1, "privacy bypasses timer and removes every cached settings page");
AmongUsClient.Instance.AmHost = true;
Check(OptionShower.GetTextNoFresh().Contains("Vanilla"), "host retains settings when hidden for clients");
Time.unscaledTime += 1;
GameOptionsManager.Instance.CurrentGameOptions.Value = "NewVanilla";
OptionShower.SelectPage(0);
Check(OptionShower.GetTextNoFresh().Contains("NewVanilla"), "vanilla updates refresh");
Time.unscaledTime += 1;
TranslationController.Instance.currentLanguage.languageID++;
var previousPages = OptionShower.pages;
OptionShower.GetTextNoFresh();
Check(!ReferenceEquals(previousPages, OptionShower.pages), "language change rebuilds all settings pages");
OptionShower.Reset();
Check(OptionShower.pages.Count == 0 && OptionShower.currentPage == 0, "new lobby discards cached values and selection");
Console.WriteLine($"Lobby settings preview: {checks} assertions passed (production formatter/pagination with offline game stubs).");
