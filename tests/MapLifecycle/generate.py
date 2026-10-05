from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
source = (root / "Patches/DleksPatch.cs").read_text(encoding="utf-8-sig")


def member(scope, signature):
    begin = scope.index(signature)
    brace = scope.index("{", begin)
    depth, end = 1, brace + 1
    while depth:
        depth += (scope[end] == "{") - (scope[end] == "}")
        end += 1
    return scope[begin:end]


dleks = source[source.index("public static class DleksPatch"):]
icons = source[source.index("class AllMapIconsPatch"):]
postfix = member(dleks, "private static void Postfix(")
loader = member(dleks, "private static System.Collections.IEnumerator LoadSelectedShip(")
clone = member(icons, "public static void Postfix_AllMapIcons(")
# Exclude comments before checking executable clone logic. A native manager clone
# would duplicate Start/network handling even if its first icon looks identical.
executable_clone = re.sub(r"//[^\n]*|/\*[\s\S]*?\*/", "", clone)
if re.search(r"\bInstantiate\s*\(", executable_clone):
    raise RuntimeError("AllMapIcons must copy data without instantiating a GameStartManager")
output = "using UnityEngine; using BepInEx.Unity.IL2CPP.Utils.Collections;\nnamespace TOHE.Patches;\n"
output += "public static class DleksPatch {\n" + postfix.replace("private static", "public static", 1) + "\n" + loader + "\n}\n"
output += "public static class AllMapIconsPatch {\n" + clone + "\n}\n"
path = Path(sys.argv[1]).resolve()
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(output, encoding="utf-8")
