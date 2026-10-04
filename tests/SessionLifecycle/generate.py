from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]


def member(source, name):
    match = re.search(r"^    (?:public|private|internal) [^\n(=]*\b" + re.escape(name) + r"\(", source, re.M)
    if not match:
        raise RuntimeError("Production method not found: " + name)
    start = match.start()
    brace = source.find("{", start)
    arrow = source.find("=>", start)
    if arrow >= 0 and arrow < brace:
        return source[start:source.index(";", arrow) + 1]
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


rpc = (root / "Modules/RPC.cs").read_text(encoding="utf-8-sig")
joined = (root / "Patches/PlayerJoinAndLeftPatch.cs").read_text(encoding="utf-8-sig")
joined = joined[joined.index("class OnGameJoinedPatch"):]
generation = re.search(r"^    internal static uint Generation[^\n]*", joined, re.M)[0]
pending = re.search(r"^    private static readonly HashSet<[^\n]*PendingVersionRequests[^\n]*", rpc, re.M)[0]
enum = re.search(r"enum CustomRPC : byte[^\n]*\n\{.*?\n\}", rpc, re.S)[0]
output = "using System; using System.Collections.Generic;\n" + enum + "\n"
output += "static class RPC {\n" + pending + "\n"
output += "\n".join(member(rpc, name) for name in ["RpcVersionCheck", "RpcRequestRetryVersionCheck", "WaitAndSendVersion"])
output += "\ninternal static int PendingCountForTest => PendingVersionRequests.Count;\n}\n"
output += "static class OnGameJoinedPatch {\n" + generation + "\n"
output += "\n".join(member(joined, name) for name in ["IsCurrentSession", "IsCurrentClient", "Postfix", "WaitForOptions"])
# The full multiplayer initializer is deliberately outside this test's scope.
output += "\nprivate static void InitializeSession(AmongUsClient client) => client.Initializations++;\n}\n"
path = Path(sys.argv[1]).resolve()
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(output, encoding="utf-8")
