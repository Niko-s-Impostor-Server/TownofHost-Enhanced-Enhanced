from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
rpc = (root / "Modules/RPC.cs").read_text(encoding="utf-8-sig")
transport = (root / "Modules/Rpc/CustomRpcTransport.cs").read_text(encoding="utf-8-sig")


def block(source, signature):
    begin = source.index(signature)
    brace = source.index("{", begin)
    depth, end = 1, brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[begin:end]


trusted_start = rpc.index("public static bool TrustedRpc(")
trusted = rpc[trusted_start:rpc.index(";", trusted_start) + 1]
handler = rpc[rpc.index("internal class RPCHandlerPatch"):]
prefix = re.sub(r"\[HarmonyArgument\(\d+\)\] ", "", block(handler, "public static bool Prefix(PlayerControl __instance,"))
valid_start = transport.index("internal static bool IsValidRpcId(")
valid = transport[valid_start:transport.index(";", valid_start) + 1]
generated = "using System; using Hazel;\nnamespace TOHE;\n"
generated += block(rpc, "enum CustomRPC : uint") + "\n"
generated += "internal static partial class RPCHandlerPatch {\n" + trusted + "\n" + prefix + "\n}\n"
generated += "internal static class CustomRpcTransport {\n" + valid + "\n}\n"
output = Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(generated, encoding="utf-8")
