"""Extract production routes/guards; replace unrelated native role effects with sinks."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]


def source(path):
    return (root / path).read_text(encoding="utf-8-sig")


def method(text, signature):
    start = text.index(signature)
    brace = text.index("{", start)
    end, depth = brace + 1, 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


chat = source("Patches/ChatCommandPatch.cs")
prefix = method(chat, "    public static bool Prefix(ChatController __instance)")
# Preserve input parsing, scope and original recall history. The large command
# switch is a boundary sink that can override canceled, as existing commands do.
prefix = prefix[:prefix.index("        string[] args")]
final = chat[chat.index("        canceled |= privateCommand;"):]
final = final[:final.index("    public static string FixRoleNameInput")]
prefix += "        bool canceled = Sink.LocalCommand(text);\n        string cancelVal = string.Empty;\n" + final

wrapper = method(chat, "    public static void OnReceiveChat(")
core = method(chat, "    private static void OnReceiveChatCore(")
core = core[:core.index("        Directory.CreateDirectory")]
# Exercise the real default branch, including its private-scope spam guard.
default = re.search(r"default:\s*(if \([^\n]*SpamManager.CheckSpam[^\n]*\) return;)", chat[chat.index("    private static void OnReceiveChatCore("):])
assert default, "Receive default spam statement not found"
core += "        " + default[1] + "\n    }\n"
rpc = method(chat[chat.index("class RpcSendChatPatch"):], "    public static bool Prefix(")

manager = source("Modules/ChatManager.cs")
history_methods = "\n".join(method(manager, sig) for sig in (
    "        public static bool CheckCommond(",
    "        private static string GetTextHash(",
    "        public static void SendMessage(",
))
replay = method(manager, "        public static void SendPreviousMessagesToAll(")
replay = replay[:replay.index("            //This should never function")]
replay += "            Sink.Replays++;\n        }\n"

guards = []
for name, path, signature in (
    ("Guess", "Modules/GuessManager.cs", "    public static void TryHideMsg()"),
    ("Inspector", "Roles/Crewmate/Inspector.cs", "    private static void TryHideMsgForCompare()"),
    ("President", "Roles/Crewmate/President.cs", "    public static void TryHideMsgForPresident()"),
    ("Pirate", "Roles/Neutral/Pirate.cs", "    public static void TryHideMsgForDuel()"),
):
    body = method(source(path), signature)
    leading = body[body.index("{") + 1:body.index("        ChatUpdatePatch.DoBlockChat")]
    guards.append(f"public static void Hide{name}() {{ {leading} Sink.Hides++; }}")

# Extract every public originMsg echo condition; conditions are not rewritten.
echo_paths = ("Modules/GuessManager.cs", "Roles/Crewmate/Inspector.cs", "Roles/Crewmate/President.cs",
              "Roles/Neutral/Pirate.cs", "Roles/Crewmate/Judge.cs", "Roles/Crewmate/Swapper.cs",
              "Roles/Impostor/Councillor.cs")
for path in echo_paths:
    name = Path(path).stem
    echoes = re.findall(r"else (if \([^\n]*\) (?:Utils\.)?SendMessage\(originMsg[^\n]*;)", source(path))
    assert echoes, f"No echo guards in {path}"
    for i, echo in enumerate(echoes):
        guards.append(f"public static void Echo{name}{i}(PlayerControl pc, string originMsg, bool isUI = false) {{ {echo} }}")

output = Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text("""// Generated from current production source, never checked in.
#pragma warning disable CS0219 // Unrelated locals retained from extracted receive core.
using System.Text; using System.Text.RegularExpressions; using System.Security.Cryptography;
using static TOHE.Utils;
namespace TOHE;
static class ChatCommands {
    public static readonly List<string> ChatHistory = [];
""" + prefix + wrapper + core + "\n}\nstatic class RpcSendChatPatch {\n" + rpc + "\n}\n" + """
static class ChatManager {
    public static bool cancel;
    private static readonly List<Dictionary<byte, string>> chatHistory = [];
    public static List<string> ChatSentBySystem = [];
    private const int maxHistorySize = 20;
    public static int HistoryCount => chatHistory.Count;
    public static string LastMessage => chatHistory.Last().Values.Single();
    public static void Reset() { chatHistory.Clear(); ChatSentBySystem.Clear(); cancel = false; }
""" + history_methods + replay + "\n}\nstatic class Effects {\n" + "\n".join(guards) + "\n}\n", encoding="utf-8")
