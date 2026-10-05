using TOHE;

int assertions = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    assertions++;
}

var interrupted = new GameOptionsMenu();
var complete = new GameOptionsMenu();
var partialRow = new OptionBehaviour();
var completeRow = new OptionBehaviour();
var partialHeader = new CategoryHeaderMasked { transform = new() { Parent = interrupted.settingsContainer } };
var completeHeader = new CategoryHeaderMasked { transform = new() { Parent = complete.settingsContainer } };
var coroutine = new Coroutine();
GameOptionsMenuPatch.MenuTabs[interrupted.GetInstanceID()] = TabGroup.SystemSettings;
GameOptionsMenuPatch.MenuTabs[complete.GetInstanceID()] = TabGroup.ModSettings;
GameOptionsMenuPatch.Builds[interrupted.GetInstanceID()] = coroutine;
GameOptionsMenuPatch.BuildVersions[interrupted.GetInstanceID()] = 42;
interrupted.Children.Add(partialRow);
interrupted.ControllerSelectable.Add(new object());
complete.Children.Add(completeRow);
complete.ControllerSelectable.Add(new object());
ModGameOptionsMenu.OptionList[partialRow] = 1;
ModGameOptionsMenu.BehaviourList[1] = partialRow;
ModGameOptionsMenu.OptionList[completeRow] = 2;
ModGameOptionsMenu.BehaviourList[2] = completeRow;
ModGameOptionsMenu.CategoryHeaderList[1] = partialHeader;
ModGameOptionsMenu.CategoryHeaderList[2] = completeHeader;
GameOptionsMenuPatch.HideModMenu(interrupted);
Check(coroutine.Stopped && interrupted.gameObject.Events.SequenceEqual(new[] { "stop", "hide" }), "stop owned coroutine before disabling its menu");
Check(!GameOptionsMenuPatch.Builds.ContainsKey(interrupted.GetInstanceID()) &&
      !GameOptionsMenuPatch.BuildVersions.ContainsKey(interrupted.GetInstanceID()), "invalidate the interrupted build generation");
Check(interrupted.Children.Count == 0 && interrupted.ControllerSelectable.Count == 0 &&
      partialRow.gameObject.Destroyed && partialHeader.gameObject.Destroyed, "clear interrupted rows headers and controller navigation");
Check(!ModGameOptionsMenu.OptionList.ContainsKey(partialRow) && !ModGameOptionsMenu.BehaviourList.ContainsKey(1) &&
      !ModGameOptionsMenu.CategoryHeaderList.ContainsKey(1), "remove interrupted control registry mappings");
Check(ModGameOptionsMenu.OptionList.ContainsKey(completeRow) && complete.Children.Count == 1 &&
      ModGameOptionsMenu.CategoryHeaderList.ContainsKey(2) && !completeHeader.gameObject.Destroyed,
      "interruption preserves another completed menu and its mappings");
GameOptionsMenuPatch.HideModMenu(complete);
Check(complete.Children.Count == 1 && complete.ControllerSelectable.Count == 1 && !completeRow.gameObject.Destroyed,
      "hiding a completed menu keeps its reusable rows and navigation");
GameOptionsMenuPatch.HideModMenu(interrupted);
Check(interrupted.gameObject.Events.Count(eventName => eventName == "stop") == 1, "repeated hide does not cancel the same build twice");
GameOptionsMenuPatch.Instance = complete;
GameOptionsMenuPatch.ReleaseMenus();
Check(GameOptionsMenuPatch.Instance == null && GameOptionsMenuPatch.MenuTabs.Count == 0 &&
      GameOptionsMenuPatch.Builds.Count == 0 && GameOptionsMenuPatch.BuildVersions.Count == 0 &&
      ModGameOptionsMenu.OptionList.Count == 0 && ModGameOptionsMenu.BehaviourList.Count == 0 &&
      ModGameOptionsMenu.CategoryHeaderList.Count == 0, "owner close releases every registry");

GameOptionsManager.Instance.CurrentGameOptions.MapId = 3;
var map = new StringOption
{
    Title = StringNames.GameMapName,
    Values = [StringNames.Skeld, StringNames.Mira, StringNames.Polus, StringNames.Dleks, StringNames.Airship]
};
AutoSelectDleksPatch.Postfix(map);
Check(map.Value == 3 && map.oldValue == 3 && map.ValueText.text == "Dleks" && map.Plus && map.Minus && map.AdjustCalls == 1,
      "native initialization restores Dleks selection label and button state together");
Check(GameOptionsManager.Instance.CurrentGameOptions.MapId == 3, "passive map display does not rewrite game settings");
var unrelated = new StringOption { Title = StringNames.Other, Values = map.Values, Value = 1 };
AutoSelectDleksPatch.Postfix(unrelated);
Check(unrelated.Value == 1 && unrelated.AdjustCalls == 0, "other native string controls are unchanged");
ModGameOptionsMenu.OptionList[map] = 7;
map.Value = 2;
AutoSelectDleksPatch.Postfix(map);
Check(map.Value == 2 && map.AdjustCalls == 1, "registered mod option keeps its existing option callback contract");
ModGameOptionsMenu.OptionList.Clear();
map.Values = [StringNames.Skeld];
AutoSelectDleksPatch.Postfix(map);
Check(map.Value == 2 && map.AdjustCalls == 1, "out of range selected map cannot index the native label array");

var lobby = new LobbyBehaviour();
LobbyBehaviour.Instance = lobby;
SoundManager.Instance.soundPlayers.Add(new SoundPlayer("DropShipAmb"));
SoundManager.Instance.soundPlayers.Add(new SoundPlayer("MapTheme"));
Main.DisableLobbyMusic.Value = true;
for (int frame = 0; frame < 64; frame++) LobbyBehaviourPatch.UpdateMusic(lobby);
Check(SoundManager.Instance.Stops == 1 && SoundManager.Instance.Starts == 0 &&
      SoundManager.Instance.soundPlayers.Single().Name == "DropShipAmb", "disable stops only map music once across repeated updates");
Main.DisableLobbyMusic.Value = false;
for (int frame = 0; frame < 64; frame++) LobbyBehaviourPatch.UpdateMusic(lobby);
Check(SoundManager.Instance.Starts == 1 && SoundManager.Instance.Stops == 1, "reenable starts map music once without restarting each frame");
Main.DisableLobbyMusic.Value = true;
LobbyBehaviourPatch.UpdateMusic(lobby);
SoundManager.Instance.soundPlayers.Add(new SoundPlayer("MapTheme")); // Native delayed ambience started after disabling.
LobbyBehaviourPatch.UpdateMusic(lobby);
Check(SoundManager.Instance.Stops == 3 && SoundManager.Instance.Starts == 1, "disable also stops native delayed ambience when it appears later");
int previousStops = SoundManager.Instance.Stops, previousStarts = SoundManager.Instance.Starts;
Main.DisableLobbyMusic.Value = false;
GameStates.IsLobby = false;
LobbyBehaviourPatch.UpdateMusic(lobby);
GameStates.IsLobby = true;
LobbyBehaviourPatch.UpdateMusic(new LobbyBehaviour());
lobby.gameObject.activeInHierarchy = false;
LobbyBehaviourPatch.UpdateMusic(lobby);
Check(SoundManager.Instance.Stops == previousStops && SoundManager.Instance.Starts == previousStarts,
      "inactive replaced and non-lobby scenes cannot change the music");
Console.WriteLine($"Gui entrypoint contracts: {assertions} assertions passed (offline stub coverage).");
