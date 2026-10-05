using TOHE;

int checks = 0;
void Expect(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void Name(string expected, bool? meeting = null, bool prefix = true)
    => Expect(Utils.GetAddOnDisplayName(CustomRoles.Overclocked, meeting, prefix) == expected, $"Expected add-on name {expected}");

var player = new PlayerControl(1);
Main.Players[1] = player;
Main.PlayerStates[1] = new() { SubRoles = [CustomRoles.Overclocked] };
Translator.Translations["Crewmate"] = "Crewmate";
Translator.Translations["Overclocked"] = "Overclocked";
Translator.Translations["Prefix.Overclocked"] = "Overclocked ";

IntroHarness.Schedule(player);
Expect(player.Resets == 0 && player.StartingCooldown == null && LateTask.Pending.Count == 1, "First cooldown reset must remain delayed after intro");
LateTask.Run();
Expect(player.Resets == 1 && player.StartingCooldown == 13f, "Default override must preserve 15 seconds minus elapsed delay");
Options.ChangeFirstKillCooldown.Value = 0;
IntroHarness.Schedule(player); LateTask.Run();
Expect(player.StartingCooldown == 28f, "Override off must apply the actual per-role cooldown minus delay");
Options.FixFirstKillCooldown.Value = 0;
IntroHarness.Schedule(player);
Expect(LateTask.Pending.Count == 0, "Reset off must leave the native first cooldown alone");
Options.FixFirstKillCooldown.Value = 1;
Options.ChangeFirstKillCooldown.Value = 1;
Options.FixKillCooldownValue.FloatValue = 0;
IntroHarness.Schedule(player); LateTask.Run();
Expect(player.StartingCooldown == 0f, "A configured zero cooldown must not pass a negative sentinel to SetKillCooldown");
Options.CurrentGameMode = CustomGameMode.FFA;
IntroHarness.Schedule(player);
Expect(LateTask.Pending.Count == 0, "FFA must keep its own cooldown policy");
Options.CurrentGameMode = CustomGameMode.Standard;

List<CustomRoles> roles = [CustomRoles.Jester, CustomRoles.Arrogance, CustomRoles.Crewmate];
Options.DisableHiddenRoles.Value = 1;
AssignHarness.Apply(roles);
Expect(roles.SequenceEqual([CustomRoles.Jester, CustomRoles.Arrogance, CustomRoles.Crewmate]), "Disable hidden roles must retain selected source roles");
Expect(Sunnyboy.Rolls == 0 && Bard.Rolls == 0, "Disabled hidden replacement must not consume RNG");
Options.DisableHiddenRoles.Value = 0;
AssignHarness.Apply(roles);
Expect(roles.Count == 3 && roles.Contains(CustomRoles.Sunnyboy) && roles.Contains(CustomRoles.Bard), "Enabled hidden replacements must preserve role count");
roles = [CustomRoles.Crewmate];
AssignHarness.Apply(roles);
Expect(roles.SequenceEqual([CustomRoles.Crewmate]), "Hidden rolls must not inject roles without their source role");

Name("Overclocked ");
Options.ShowShortNamesForAddOns.Value = 1;
Name("O", false); Name("O", true);
Translator.Translations["Prefix.Overclocked"] = "<color=#123456>超频</color>";
Name("超");
Translator.Translations["Prefix.Overclocked"] = "<b>🧑‍🚀 Pilot</b>";
Name("🧑‍🚀");
Translator.Translations["Prefix.Overclocked"] = "e\u0301lan";
Name("e\u0301");
Translator.Translations["Prefix.Overclocked"] = "*Prefix.Overclocked";
Name("O");
Translator.Translations.Remove("Prefix.Overclocked");
Name("O");
Translator.Translations["Prefix.Overclocked"] = "<b> </b>";
Name("O");
Translator.Translations["Prefix.Overclocked"] = "超频 ";
Options.ShowShortNamesForAddOns.Value = 2;
Name("超频 ", false); Name("超", true);
GameStates.IsMeeting = true;
Name("超");
Options.ShowShortNamesForAddOns.Value = 3;
Name("超频 ", true); Name("超", false);
GameStates.IsMeeting = false;

Options.ShowShortNamesForAddOns.Value = 2;
var meetingText = player.GetDisplayRoleAndSubName(player, isMeeting: true);
Expect(meetingText.Contains("(超)") && meetingText.EndsWith("</color>"), "Meeting role text must use localized short name and retain rich text");
var gameText = player.GetDisplayRoleAndSubName(player, isMeeting: false);
Expect(gameText.Contains("(超频 )"), "Only-in-meeting must retain full name in game");
player.Client.PlatformData.Platform = Platforms.Switch;
Expect(!Utils.GetRoleAndSubText(1, 1, isMeeting: true).Item1.Contains("</color>"), "Console role text must retain its no-closing-color behavior");
player.Client.PlatformData.Platform = Platforms.Standalone;
Options.AddBracketsToAddons.Value = 0;
Expect(!Utils.GetRoleAndSubText(1, 1, isMeeting: true).Item1.Contains("("), "Short names must honor disabled brackets");
Options.AddBracketsToAddons.Value = 1;
player.HiddenSubRoles.Add(CustomRoles.Overclocked);
Expect(!Utils.GetRoleAndSubText(1, 1, isMeeting: true).Item1.Contains("超"), "Short names must not reveal hidden add-ons");
player.HiddenSubRoles.Clear();
Options.NameDisplayAddons.Value = 0;
Expect(!Utils.GetRoleAndSubText(1, 1, isMeeting: true).Item1.Contains("超"), "Display-addons off must still hide the name prefix");
Options.NameDisplayAddons.Value = 1;
Expect(!Utils.GetRoleAndSubText(1, 1, notShowAddOns: true, isMeeting: true).Item1.Contains("超"), "Existing notShowAddOns argument must retain its meaning");

Options.ShowShortNamesForAddOns.Value = 1;
Expect(Utils.GetSubRolesText(1, summary: true).RemoveHtmlTags() == " (O)", "Result summary must share abbreviation policy using role-name localization");
Expect(player.GetSubRoleName().RemoveHtmlTags() == " + O", "All-role-name path must share abbreviation policy");
Options.ShowShortNamesForAddOns.Value = 2;
GameStates.IsMeeting = true;
Expect(Utils.GetSubRolesText(1, summary: true).RemoveHtmlTags() == " (Overclocked)", "Result summary must use explicit non-meeting context even when a final meeting is present");
Options.ShowShortNamesForAddOns.Value = 3;
Expect(Utils.GetSubRolesText(1, summary: true).RemoveHtmlTags() == " (O)", "Only-in-game must apply to result summaries");
Options.ShowShortNamesForAddOns.Value = 0;
Expect(player.GetSubRoleName().RemoveHtmlTags() == " + Overclocked", "Short names disabled must retain full role names");
Options.ShowShortNamesForAddOns = null;
Name("超频 ");

Console.WriteLine($"PASS: {checks} production gameplay option checks.");
