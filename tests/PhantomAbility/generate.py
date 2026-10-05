from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]

def statement(source, start):
    brace = source.index("{", start)
    end, depth = brace + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]

def method(source, name):
    found = re.search(r"^    (?:public|private|internal) [^\n]*\b" + name + r"\(", source, re.M)
    if not found:
        raise RuntimeError("Missing production method " + name)
    return statement(source, found.start())

role = (root / "Roles/Core/RoleBase.cs").read_text(encoding="utf-8-sig")
disperser = (root / "Roles/Impostor/Disperser.cs").read_text(encoding="utf-8-sig")
patch = (root / "Patches/PhantomRolePatch.cs").read_text(encoding="utf-8-sig")
output = "using System; using System.Linq; using AmongUs.GameOptions; using TOHE.Roles.Core; using static TOHE.Utils; using static TOHE.Translator;\nnamespace TOHE;\n"
output += "partial class RoleBase {\n"
for name in ["PhantomAbilityReadyAt", "PhantomAbilityExecuting", "UsesPhantomAbility", "PhantomAbilityCooldown", "OnPhantomAbility"]:
    output += re.search(r"^    [^\n]*\b" + name + r"\b[^\n]*", role, re.M)[0] + "\n"
output += "}\n"
output += "class Disperser : RoleBase {\nstatic OptionItem DisperserShapeshiftCooldown = new(20); static OptionItem DisperserShapeshiftDuration = new(15);\n"
for name in ["ThisRoleBase", "UsesPhantomAbility", "PhantomAbilityCooldown"]:
    output += re.search(r"^    [^\n]*\b" + name + r"\b[^\n]*", disperser, re.M)[0] + "\n"
for name in ["ApplyGameOptions", "OnCheckShapeshift", "OnPhantomAbility"]:
    output += method(disperser, name) + "\n"
output += "}\nstatic class PhantomPatchFixture {\n"
# Keep the new, complete opt-in statements verbatim; the original vanilla/desync
# branch remains outside this fixture and is not replaced with a copied implementation.
for name, parameters, marker in [
    ("Cmd", "PlayerControl __instance, float maxDuration", "CmdCheckVanish_Prefix"),
    ("Check", "PlayerControl __instance", "private static bool CheckVanish_Prefix("),
    ("Use", "PhantomRole __instance", "class PhantomRoleUseAbilityPatch"),
]:
    source = patch[patch.index(marker):]
    start = source.index("if (PhantomAbility.IsEnabled(")
    output += f"public static bool {name}({parameters}) {{\n" + statement(source, start) + "\nreturn true;\n}\n"
output += method(patch, "HandleServerAppear_Prefix").replace("private static", "public static") + "\n}\n"
path = Path(sys.argv[1]).resolve()
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(output, encoding="utf-8")
