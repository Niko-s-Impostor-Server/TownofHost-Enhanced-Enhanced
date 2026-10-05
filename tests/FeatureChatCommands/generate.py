"""Extract the real legacy ban/kick protection predicates for executable checks.

This does not replace or recreate the full legacy chat dispatcher.
"""
import pathlib
import re
import sys

project = pathlib.Path(__file__).resolve().parent
source = (project / "../../Patches/ChatCommandPatch.cs").resolve().read_text(encoding="utf-8-sig")
methods = []
for action, variable in (("Ban", "bannedPlayer"), ("Kick", "kickedPlayer")):
    predicates = re.findall(r"if \((Utils\.IsPlayerModerator\(" + variable + r"\.FriendCode\).*)\)\s*\n", source)
    if len(predicates) != 1:
        raise RuntimeError(f"Expected exactly one legacy {action} target predicate, found {len(predicates)}")
    methods.append(f"    internal static bool {action}(PlayerControl {variable}) => {predicates[0]};")
output = pathlib.Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text("using TOHE.Modules;\nnamespace TOHE;\ninternal static class ExtractedLegacyModeration\n{\n" + "\n".join(methods) + "\n}\n", encoding="utf-8")
