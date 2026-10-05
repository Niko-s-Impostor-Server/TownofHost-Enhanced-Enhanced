from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
source = (root / "Modules/Translator.cs").read_text(encoding="utf-8-sig")
resource_items = ET.parse(root / "TOHE.csproj").findall(".//EmbeddedResource")
items = [item for item in resource_items if item.get("Include") == "Resources/**"]
if len(items) != 1 or items[0].get("WithCulture", "").lower() != "false":
    raise RuntimeError("Production resource policy must keep all language JSON in the main assembly (WithCulture=false)")


def block(marker):
    start = source.index(marker)
    brace = source.index("{", start)
    # GetString(StringNames) is expression-bodied; retain it verbatim.
    arrow = source.find("=>", start)
    if arrow != -1 and arrow < brace:
        return source[start:source.index(";", arrow) + 1]
    depth, end = 1, brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


markers = [
    "static void MergeJsonIntoTranslationMap(",
    "public static string GetString(string s, Dictionary<string, string>",
    "public static string GetString(string str, SupportedLangs",
    "public static string GetString(StringNames stringName)",
    "public static SupportedLangs GetUserTrueLang()",
]
output = "using System; using System.Collections.Generic; using System.Linq; using System.Globalization; using System.Text.RegularExpressions; using Il2CppInterop.Runtime.InteropTypes.Arrays;\n"
output += "public static class Translator { public static Dictionary<string, Dictionary<int, string>> translateMaps = new();\n"
output += "\n".join(block(marker) for marker in markers)
output += "\npublic static void MergeForTest(int language, Dictionary<string,string> values) => MergeJsonIntoTranslationMap(translateMaps, language, values);\n}\n"
destination = Path(sys.argv[1]).resolve()
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text(output, encoding="utf-8")
