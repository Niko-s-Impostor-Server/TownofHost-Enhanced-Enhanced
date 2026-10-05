from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
source = (root / "Patches/MeetingHudPatch.cs").read_text(encoding="utf-8-sig")
start = source.index("    public static bool Prefix(", source.index("class CastVotePatch"))
brace = source.index("{", start)
end, depth = brace + 1, 1
while depth:
    depth += (source[end] == "{") - (source[end] == "}")
    end += 1
output = Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(
    "using TOHE.Roles.Core; using UnityEngine; using static TOHE.Utils; using static TOHE.Translator;\n"
    "namespace TOHE;\nstatic class VoteFixture {\n" + source[start:end] + "\n}\n",
    encoding="utf-8",
)
