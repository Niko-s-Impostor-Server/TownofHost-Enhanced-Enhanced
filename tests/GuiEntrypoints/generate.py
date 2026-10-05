from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]


def member(source, signature):
    begin = source.index(signature)
    brace = source.index("{", begin)
    depth, end = 1, brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[begin:end]


menu = (root / "Patches/GameOptionsMenuPatch.cs").read_text(encoding="utf-8-sig")
dleks = (root / "Patches/DleksPatch.cs").read_text(encoding="utf-8-sig")
lobby = (root / "Patches/LobbyPatch.cs").read_text(encoding="utf-8-sig")
owner = (root / "Patches/GameSettingMenuPatch.cs").read_text(encoding="utf-8-sig")
start = (root / "Patches/GameStartManagerPatch.cs").read_text(encoding="utf-8-sig")

# Extract production bodies rather than reimplementing lifecycle/music decisions.
# Expose fixture state only: native rendering, Harmony and coroutine scheduling
# remain outside the offline assertions.
generated = """using System; using System.Collections.Generic; using System.Linq;
using TOHE; using Object = UnityEngine.Object;
public static class GameOptionsMenuPatch {
    public static GameOptionsMenu Instance;
    public static readonly Dictionary<int, TabGroup> MenuTabs = new();
    public static readonly Dictionary<int, Coroutine> Builds = new();
    public static readonly Dictionary<int, int> BuildVersions = new();
    public static int reopenVersion;
    public static void CancelReopen() => reopenVersion++;
"""
for signature in (
    "private static bool TryGetTab(", "public static void ReleaseMenus()",
    "public static void HideModMenu(", "public static void CancelBuild(",
    "private static void ClearPartialBuild(",
):
    generated += member(menu, signature) + "\n"
generated += "}\n"
auto = member(dleks, "class AutoSelectDleksPatch")
generated += auto.replace("private static void Postfix", "public static void Postfix") + "\n"
generated += member(lobby, "public class LobbyBehaviourPatch") + "\n"

# Callsite ordering matters: execute the actual hide path above, and ensure the
# owning lifecycle reaches it before toggling active state/destroying the tab.
change = member(owner, "public static bool ChangeTabPrefix(")
close = member(owner, "private static void ClosePostfix(")
update = start[start.index("public static class GameStartManagerUpdatePatch"):]
update = member(update, "public static void Prefix(GameStartManager __instance)")
if "GameOptionsMenuPatch.HideModMenu(settingsTab);" not in change:
    raise RuntimeError("Tab transitions no longer reach explicit partial-build cleanup")
if close.index("GameOptionsMenuPatch.CancelBuild(tab);") > close.index("Object.Destroy(tab);"):
    raise RuntimeError("Closing destroys a tab before cancelling its owned build")
if close.index("Object.Destroy(tab);") > close.index("GameOptionsMenuPatch.ReleaseMenus();"):
    raise RuntimeError("Owner registries are released before the close lifecycle")
if update.index("LobbyBehaviourPatch.UpdateMusic(LobbyBehaviour.Instance);") > update.index("Options.MinWaitAutoStart"):
    raise RuntimeError("Lobby music update became conditional on host automatic-start logic")
if "nameof(GameOptionsMenu.OnDisable)" in menu or "nameof(LobbyBehaviour.Update)" in lobby:
    raise RuntimeError("Unsafe stripped forwarding hooks were reintroduced")

output = Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(generated, encoding="utf-8")
