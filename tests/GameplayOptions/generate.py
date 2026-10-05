from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
utils = (root / "Modules/Utils.cs").read_text(encoding="utf-8-sig")
extended = (root / "Modules/ExtendedPlayerControl.cs").read_text(encoding="utf-8-sig")
intro = (root / "Patches/IntroPatch.cs").read_text(encoding="utf-8-sig")
assign = (root / "Roles/Core/AssignManager/RoleAssign.cs").read_text(encoding="utf-8-sig")
options = (root / "Modules/OptionHolder.cs").read_text(encoding="utf-8-sig")


def member(source, signature):
    start = source.index(signature)
    brace = source.index("{", start)
    depth, end = 1, brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


generated = "using System; using System.Globalization; using System.Text; using System.Text.RegularExpressions; using static TOHE.Translator;\nnamespace TOHE;\n"
generated += "static partial class Options {\n" + member(options, "public enum ShortAddOnNamesMode") + "\n}\n"
generated += "static partial class Utils {\n"
for signature in [
    "public static string GetDisplayRoleAndSubName(byte",
    "public static string GetAddOnDisplayName(",
    "public static (string, Color) GetRoleAndSubText(",
    "public static string GetSubRolesText(",
]:
    generated += member(utils, signature) + "\n"
remove_tags = re.search(r"    public static string RemoveHtmlTags\(this string str\) => .*?;", utils)
generated += remove_tags[0] + "\n}\n"
generated += "static partial class ExtendedPlayerControl {\n"
generated += member(extended, "public static string GetDisplayRoleAndSubName(this PlayerControl") + "\n"
generated += member(extended, "public static string GetSubRoleName(") + "\n}\n"
generated += "static class IntroHarness { public static void Schedule(PlayerControl pc) {\n"
generated += member(intro, "if (Options.FixFirstKillCooldown.GetBool()") + "\n} }\n"
generated += "static class AssignHarness { public static void Apply(List<CustomRoles> FinalRolesList) {\n"
generated += member(assign, "if (!Options.DisableHiddenRoles.GetBool())") + "\n} }\n"

# Verify default values and nesting at the production setup boundary.
assert 'Create(60772, "ChangeFirstKillCooldown", true,' in options
assert 'Create(60568, "DisableHiddenRoles", false,' in options
assert '"ShowShortNamesForAddOns", EnumHelper.GetAllNames<ShortAddOnNamesMode>(), 0,' in options
assert '.SetParent(ChangeFirstKillCooldown)' in options

output = Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(generated, encoding="utf-8")
