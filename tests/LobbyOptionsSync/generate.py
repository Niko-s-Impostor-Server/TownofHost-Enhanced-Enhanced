from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
native = Path(sys.argv[2])


def block(source, marker):
    start = source.index(marker)
    brace = source.index("{", start)
    end, depth = brace + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def member(source, name):
    match = re.search(r"^[ \t]*(?:public|protected|private) [^\n]*\b" + re.escape(name) + r"\(", source, re.M)
    if not match:
        raise RuntimeError("Actual method not found: " + name)
    return block(source, source[match.start():source.index("\n", match.start())].strip())


menu = (root / "Patches/GameSettingMenuPatch.cs").read_text(encoding="utf-8-sig")
client = (root / "Patches/ClientPatch.cs").read_text(encoding="utf-8-sig")
start = (root / "Patches/GameStartManagerPatch.cs").read_text(encoding="utf-8-sig")
options = (native / "LogicOptions.cs").read_text(encoding="utf-8-sig")
normal = (native / "LogicOptionsNormal.cs").read_text(encoding="utf-8-sig")
manager = (native / "GameManager.cs").read_text(encoding="utf-8-sig")
player = (native / "PlayerControl.cs").read_text(encoding="utf-8-sig")
custom_options = (root / "Modules/OptionItem/OptionItem.cs").read_text(encoding="utf-8-sig")
main = (root / "main.cs").read_text(encoding="utf-8-sig")
native_options_manager = (native / "GameOptionsManager.cs").read_text(encoding="utf-8-sig")
for name, cache in [("NormalOptions", "currentNormalGameOptions"), ("HideNSeekOptions", "currentHideNSeekGameOptions")]:
    if not re.search(r"\b" + name + r"\s*=>\s*GameOptionsManager.Instance\." + cache + r"\s*;", main):
        raise RuntimeError("Repository current-option getter changed: " + name)
    if cache + " = value as " not in native_options_manager:
        raise RuntimeError("Native current-option cache contract changed: " + cache)

# Guard the native fact this workaround relies on; never silently accept a newer
# native source that restores the old SyncSettings handler.
legacy = member(player, "RpcSyncSettings")
handler = member(player, "HandleRpc")
if "StartRpcImmediately(NetId, 2," not in legacy or "case RpcCalls.SyncSettings:" in handler:
    raise RuntimeError("Native RPC2 contract changed; review the suppression patch")
if "HandleRoleRpc(callId, reader)" not in handler:
    raise RuntimeError("Native fallback contract changed")

output = "using System; using AmongUs.GameOptions; using Hazel; using TOHE.Modules; using Object = UnityEngine.Object;\n"
output += block(menu, "public class RpcSyncSettingsPatch") + "\n"
output += block(client, "internal class InnerNetObjectSerializePatch") + "\n"
output += "public static class OptionItem { public static int Syncs { get => RPC.Syncs; set => RPC.Syncs = value; }\n"
output += member(custom_options, "SyncAllOptions") + "\n}\n"
output += "public abstract class LogicOptions : GameLogicComponent {\n"
output += "private readonly GameOptionsFactory gameOptionsFactory = new GameOptionsFactory((ILogger)(object)new UnityLogger());\n"
output += "protected abstract IGameOptions currentGameOptions { get; }\nprotected abstract void SetGameOptions(IGameOptions newOptions);\n"
output += "\n".join(member(options, name) for name in ["LogicOptions", "SyncOptions", "Serialize", "Deserialize"])
output += "\n}\n"
output += "public class LogicOptionsNormal : LogicOptions {\n"
output += re.search(r"private NormalGameOptionsV11 GameOptions;", normal)[0] + "\n"
output += re.search(r"protected override IGameOptions currentGameOptions[^\n]*", normal)[0] + "\n"
output += "\n".join(member(normal, name) for name in ["LogicOptionsNormal", "SetGameOptions", "OnGameStart", "OnGameEnd", "FixedUpdate", "OnDestroy"])
output += "\n}\n"
output += "public partial class GameManager {\n"
output += block(manager, "public override bool IsDirty") + "\n" + member(manager, "Serialize") + "\n}\n"
output += "public class NativeLegacyRpcFixture { public uint NetId = 99;\n" + legacy + "\n}\n"

# Execute the actual final settings call in DoTasksForBeginGame. Random-map
# selection and unrelated warning/version operations are outside this fixture.
begin = member(start, "DoTasksForBeginGame")
call_start = begin.index("IGameOptions opt =")
call_end = begin.index("        RPC.RpcVersionCheck();")
output += "static class BeginSettingsCaller { public static void Run() {\n" + begin[call_start:call_end] + "\n} }\n"
reset = start[start.index("class ResetStartStatePatch"):]
output += "static class ResetStartStatePatch {\n" + member(reset, "Prefix") + "\n}\n"

# Keep the number and locations of repository callers explicit. A new caller
# with independent option bytes would invalidate the no-argument Prefix premise.
callers = []
for path in root.rglob("*.cs"):
    if any(part in {"tests", "obj", "bin", "artifacts", ".git"} for part in path.relative_to(root).parts):
        continue
    if b".RpcSyncSettings(" in path.read_bytes():
        callers.append(path.relative_to(root).as_posix())
if callers != ["Patches/GameStartManagerPatch.cs"]:
    raise RuntimeError("Repository RpcSyncSettings callers changed: " + repr(callers))
if start.count(".RpcSyncSettings(") != 2:
    raise RuntimeError("Repository RpcSyncSettings call count changed")

path = Path(sys.argv[1]).resolve()
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(output, encoding="utf-8")
