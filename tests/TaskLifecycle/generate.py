from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
source = (root / "Modules/GameState.cs").read_text(encoding="utf-8-sig")
source = source[source.index("public class TaskState"):]
match = re.search(r"^    public void Init\(PlayerControl player\)", source, re.M)
if match is None:
    raise RuntimeError("Production TaskState.Init not found")
start = match.start()
brace = source.index("{", start)
depth, end = 1, brace + 1
while depth:
    depth += (source[end] == "{") - (source[end] == "}")
    end += 1
method = source[start:end]
output = """using static TOHE.Utils;
namespace TOHE;
class TaskState {
    public static int InitialTotalTasks;
    public int AllTasksCount = -1;
    public int CompletedTasksCount;
    public bool hasTasks;
""" + method + "\n}\n"
path = Path(sys.argv[1]).resolve()
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(output, encoding="utf-8")
