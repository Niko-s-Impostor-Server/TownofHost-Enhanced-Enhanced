from pathlib import Path
import os
import re
import sys

# Optional override supports another read-only checkout of the same native version.
root = Path(os.environ.get("TOHE_NATIVE_SOURCE", r"D:\SharedUserFiles\TestDesktop\opencode-chat\assembly-compare\src-2026.8.18")) / "AmongUs.GameOptions"


def method(source, signature):
    start = source.index(signature)
    brace = source.index("{", start)
    depth, end = 1, brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end].replace("public ", "public override ", 1)


parts = []
for name in ["BoolOptionNames", "ByteOptionNames", "FloatOptionNames", "Int32OptionNames", "UInt32OptionNames", "RoleTypes"]:
    source = (root / (name + ".cs")).read_text(encoding="utf-8-sig")
    parts.append(source.replace("namespace AmongUs.GameOptions;", "namespace AmongUs.GameOptions {", 1) + "\n}")

normal = (root / "NormalGameOptionsV11.cs").read_text(encoding="utf-8-sig")
roles = re.search(r"AvailableRoles = new RoleTypes\[\d+\]\s*\{([^}]+)\}", normal).group(1)
parts.append("namespace AmongUs.GameOptions { static class NativeRoleContract { public static readonly RoleTypes[] Available = [" + roles + "]; } }")
for source_name, fixture in [("NormalGameOptionsV11", "NativeNormalFixture"), ("HideNSeekGameOptionsV11", "NativeHideNSeekFixture")]:
    source = (root / (source_name + ".cs")).read_text(encoding="utf-8-sig")
    parts.append("namespace AmongUs.GameOptions { sealed class " + fixture + " : NativeOptionsFixture {\n" +
                 method(source, "public void SetBool(BoolOptionNames optionName, bool value)") + "\n" +
                 method(source, "public bool TryGetBool(BoolOptionNames optionName, out bool value)") + "\n} }")

output = Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text("\n".join(parts), encoding="utf-8")
