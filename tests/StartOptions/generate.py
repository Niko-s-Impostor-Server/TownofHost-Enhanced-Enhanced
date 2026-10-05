from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
start = (root / "Patches/GameStartManagerPatch.cs").read_text(encoding="utf-8-sig")
roles = (root / "Patches/onGameStartedPatch.cs").read_text(encoding="utf-8-sig")


def member(source, signature):
    begin = source.index(signature)
    brace = source.index("{", begin)
    depth, end = 1, brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[begin:end]


# Extract the actual final boundary after ship/client readiness, not a rewritten
# model of its ordering. Ship loading/network timeout logic is outside this test.
coroutine = member(roles, "public static System.Collections.IEnumerator StartGameHost()")
tail = coroutine[coroutine.index("        thiz.SendClientReady();"):]
generated = """using System; using System.Collections.Generic; using System.Linq;
using AmongUs.GameOptions; using InnerNet; using static Translator;
class GameStartManagerBeginGamePatch {
    static byte SelectRandomMap() => 0;
""" + member(start, "public static void DoTasksForBeginGame()") + "\n}\n"
reset = start[start.index("class ResetStartStatePatch"):]
generated += "class ResetStartStatePatch {\n" + member(reset, "public static void Prefix(GameStartManager __instance)") + "\n}\n"
generated += """class StartGameHostPatch {
    static AmongUsClient thiz { get => AmongUsClient.Instance; set => AmongUsClient.Instance = value; }
    public static System.Collections.IEnumerator StartGameHost() {
        var generation = OnGameJoinedPatch.Generation;
""" + tail + "\n"
generated += member(roles, "public static bool CoStartGameHost_Prefix(") + "\n"
generated += member(roles, "internal static bool SyncInitialGameOptions()") + "\n"
generated += member(roles, "private sealed class InitialGameOptionsSender(") + "\n"
generated += """    static System.Collections.IEnumerator AssignRoles() {
        Trace.Events.Add("roles"); yield break;
    }
}
"""
# Capture ordering is an independent source invariant: initializer must snapshot
# before its first normal-mode role/option mutation.
initializer = member(roles, "public static void Postfix(AmongUsClient __instance)")
if initializer.index("Main.RealOptionsData = new OptionBackupData") > initializer.index("currentNormalGameOptions.ConfirmImpostor"):
    raise RuntimeError("Round settings mutated before original option backup")
output = Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(generated, encoding="utf-8")
