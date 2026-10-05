from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
source = (root / "Patches/GameOptionsMenuPatch.cs").read_text(encoding="utf-8-sig")


def member(text, signature):
    start = text.index(signature)
    brace = text.index("{", start)
    end, depth = brace + 1, 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


generated = "using System; using TOHE;\nnamespace TOHE {\n"
for name, signatures in (
    ("ToggleOptionPatch", ("public static bool TogglePrefix(", "private static bool UpdateValuePrefix(")),
    ("NumberOptionPatch", ("private static int IncrementMultiplier", "private static bool UpdateValuePrefix(", "public static bool IncreasePrefix(", "public static bool DecreasePrefix(")),
    ("StringOptionPatch", ("private static bool UpdateValuePrefix(", "public static bool IncreasePrefix(", "public static bool DecreasePrefix(")),
):
    body = source[source.index("public static class " + name):]
    generated += "public static class " + name + " {\n"
    for signature in signatures:
        generated += member(body, signature) + "\n"
    generated += "}\n"
generated += "}\n"
output = Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(generated, encoding="utf-8")
