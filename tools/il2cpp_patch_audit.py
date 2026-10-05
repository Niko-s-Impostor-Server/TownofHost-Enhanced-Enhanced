"""Read-only Harmony target inventory against public allofus.dev analyzer data.

No game process is started. Every HTTP request has a hard 10 second timeout.
The source scanner reports ambiguities instead of pretending to be Harmony.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
BASE = "https://allofus.dev/il2cpp/"
TOKEN = re.compile(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*[\s\S]*?\*/')


def clean(text, strings=False):
    def substitute(match):
        token = match[0]
        return re.sub(r"[^\n]", " ", token) if strings or token.startswith(("//", "/*")) else token
    return TOKEN.sub(substitute, text)


def read_source(path):
    try:
        return path.read_text(encoding="utf-8-sig")
    except UnicodeDecodeError:
        # Existing TOHEOnlySearch.cs uses the legacy Chinese code page.
        return path.read_text(encoding="gb18030")


def closing(text, start, opening="{", close="}"):
    depth = 1
    for index in range(start + 1, len(text)):
        depth += (text[index] == opening) - (text[index] == close)
        if depth == 0:
            return index + 1
    raise ValueError("Unclosed source token")


def split_args(text):
    args, begin, depth = [], 0, 0
    for index, char in enumerate(text):
        depth += char in "(<[{"
        depth -= char in ")>]}"
        if char == "," and depth == 0:
            args.append(text[begin:index].strip())
            begin = index + 1
    if text[begin:].strip():
        args.append(text[begin:].strip())
    return args


def typename(value):
    aliases = {"bool": "Boolean", "byte": "Byte", "sbyte": "SByte", "char": "Char", "short": "Int16", "ushort": "UInt16", "int": "Int32", "uint": "UInt32", "long": "Int64", "ulong": "UInt64", "float": "Single", "double": "Double", "string": "String", "object": "Object", "void": "void", "unsignedchar": "Byte", "unsignedshort": "UInt16", "unsignedint": "UInt32", "unsignedlong": "UInt64"}
    byref = bool(re.match(r"^(?:ref|out|in)\s+", value))
    value = re.sub(r"^(?:ref|out|in|this)\s+", "", value)
    value = value.replace("global::", "").replace("::", ".").replace(" ", "")
    value = re.sub(r"(?:[A-Za-z_]\w*\.)+([A-Za-z_]\w*)", r"\1", value)
    value = re.sub(r"\b(?:" + "|".join(aliases) + r")\b", lambda m: aliases[m[0]], value)
    return value + ("&" if byref else "")


def fetch(url, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    # Per-request subprocess gives a total deadline, including streamed body;
    # socket timeout alone would only limit an individual blocking operation.
    code = """import sys, urllib.request; from pathlib import Path
request = urllib.request.Request(sys.argv[1], headers={"User-Agent": "Mozilla/5.0 (compatible; TOHEE-ReadOnlyPatchAudit/1.0)"})
with urllib.request.urlopen(request, timeout=9) as response:
    Path(sys.argv[2]).write_bytes(response.read())
"""
    subprocess.run([sys.executable, "-c", code, url, str(path)], timeout=10, check=True,
                   creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    return path.read_bytes()


def pe_metadata(path):
    if not path.is_file():
        return {"path": str(path), "available": False}
    with path.open("rb") as binary:
        binary.seek(0x3c)
        offset = struct.unpack("<I", binary.read(4))[0]
        binary.seek(offset)
        signature, machine = binary.read(4), struct.unpack("<H", binary.read(2))[0]
    return {"path": str(path), "available": True, "pe_signature": signature.hex(), "machine": hex(machine), "architecture": "x86" if machine == 0x14c else "x86_64" if machine == 0x8664 else "other"}


def inventory():
    records, unresolved, attributes = [], [], 0
    for path in sorted(ROOT.rglob("*.cs")):
        if any(part in {"tests", "obj", "bin", "artifacts", ".git"} for part in path.relative_to(ROOT).parts):
            continue
        original = read_source(path)
        source, masked = clean(original), clean(original, strings=True)
        classes = []
        for match in re.finditer(r"\b(?:class|struct)\s+(\w+)[^\n;{]*\s*\{", masked):
            brace = match.end() - 1
            classes.append({"name": match[1], "start": match.start(), "brace": brace, "end": closing(masked, brace), "attrs": []})
        attrs = []
        for match in re.finditer(r"\[\s*HarmonyPatch\b", masked):
            end = closing(masked, match.start(), "[", "]")
            body = source[match.start():end]
            position = end
            while True:
                rest = re.match(r"\s*", masked[position:])
                position += rest.end()
                if position < len(masked) and masked[position] == "[":
                    position = closing(masked, position, "[", "]")
                else:
                    break
            upcoming = next((c for c in classes if position <= c["start"] and c["start"] - position < 70 and not re.search(r"[;{}()]", masked[position:c["start"]])), None)
            owner = upcoming or next(iter(sorted((c for c in classes if c["brace"] < match.start() < c["end"]), key=lambda c: c["brace"], reverse=True)), None)
            attr = {"start": match.start(), "end": end, "text": body, "owner": owner, "class_level": upcoming is not None}
            attrs.append(attr)
            if upcoming:
                upcoming["attrs"].append(attr)
        attributes += len(attrs)
        for attr in attrs:
            text, owner = attr["text"], attr["owner"]
            types = re.findall(r"typeof\s*\(\s*([^()]+)\s*\)", text)
            names = re.findall(r"nameof\s*\(\s*([^()]+)\s*\)", text)
            quoted = re.findall(r'"([^"\n]+)"', text)
            array_only = bool(re.search(r"new\s+(?:Type\s*\[\s*\]|\[\s*\])", text))
            opening = text.find("(")
            call_args = split_args(text[opening + 1:closing(text, opening, "(", ")") - 1]) if opening >= 0 else []
            first_is_type = bool(call_args and call_args[0].startswith("typeof"))
            inherited = next((a for a in (owner or {}).get("attrs", []) if re.search(r"typeof", a["text"])), None)
            target_type = types[0].strip() if first_is_type else None
            target_method = names[0].split(".")[-1] if names else quoted[0] if quoted else None
            if not target_type and target_method and inherited:
                target_type = re.search(r"typeof\s*\(\s*([^()]+)\s*\)", inherited["text"])[1].strip()
            if not target_method:
                # Bare/container and overload-selector attributes are not targets.
                continue
            if not target_type:
                unresolved.append({"file": str(path.relative_to(ROOT)).replace("\\", "/"), "line": original[:attr["start"]].count("\n") + 1, "attribute": text, "reason": "target type unresolved"})
                continue
            parameter_types = types[1:] if first_is_type else types
            arguments = parameter_types if parameter_types or array_only else None
            if attr["class_level"] and owner:
                overload = next((a for a in owner["attrs"] if re.search(r"new\s+(?:Type\s*\[\s*\]|\[\s*\])", a["text"])), None)
                if overload:
                    arguments = re.findall(r"typeof\s*\(\s*([^()]+)\s*\)", overload["text"])
            if "MethodType.Getter" in text:
                target_method = "get_" + target_method
            if "MethodType.Setter" in text:
                target_method = "set_" + target_method
            records.append({"file": str(path.relative_to(ROOT)).replace("\\", "/"), "line": original[:attr["start"]].count("\n") + 1, "patch_class": owner["name"] if owner else None, "target_type": target_type, "target_method": target_method, "argument_types": arguments, "attribute": text})
        dynamic = list(re.finditer(r"\b(?:TargetMethod|TargetMethods)\s*\(", masked))
        for match in dynamic:
            unresolved.append({"file": str(path.relative_to(ROOT)).replace("\\", "/"), "line": original[:match.start()].count("\n") + 1, "reason": "dynamic TargetMethod(s) requires review"})
    return records, unresolved, attributes


def native_index(root):
    index, bases = {}, {}
    if not root:
        return index, bases
    for path in root.rglob("*.cs"):
        source = read_source(path)
        masked = clean(source, strings=True)
        type_name = path.stem
        class_match = re.search(r"\bclass\s+" + re.escape(type_name) + r"\s*:\s*([\w.]+)", masked)
        if class_match:
            bases[type_name] = class_match[1].split(".")[-1]
        pattern = r"(?m)^[ \t]*(?:public|private|protected|internal)\s+(?:(?:static|virtual|override|sealed|new|async|unsafe|extern)\s+)*([\w.<>,\[\]?]+)\s+(\w+)\s*\(([^\n]*)\)\s*(\{|=>|;)"
        for match in re.finditer(pattern, masked):
            parameters = []
            for parameter in split_args(source[match.start(3):match.end(3)]):
                parameter = parameter.split("=")[0].strip()
                tokens = parameter.split()
                parameters.append(typename(" ".join(tokens[:-1])))
            body = None
            if match[4] == "{":
                body = masked[match.end() - 1:closing(masked, match.end() - 1)]
            row = {"signature": f"{type_name}::{match[2]}({', '.join(parameters) or 'void'})", "parameter_types": parameters, "file": str(path), "line": source[:match.start()].count("\n") + 1, "return_type": typename(match[1]), "empty_body": body is not None and not body[1:-1].strip(), "coroutine_factory": typename(match[1]).endswith("IEnumerator")}
            index.setdefault((type_name, match[2]), []).append(row)
        # Auto-property accessors are actual patch targets, not the property name.
        for match in re.finditer(r"(?m)^[ \t]*(?:public|private|protected|internal)\s+([\w.]+)\s+(\w+)\s*\{\s*get\s*;[^}]*\}", masked):
            row = {"signature": f"{type_name}::get_{match[2]}(void)", "parameter_types": [], "file": str(path), "line": source[:match.start()].count("\n") + 1, "return_type": typename(match[1]), "empty_body": False, "auto_property_getter": True, "coroutine_factory": False}
            index.setdefault((type_name, "get_" + match[2]), []).append(row)
    return index, bases


def compare(records, data, native, bases):
    website = {}
    for signature, row in data.items():
        match = re.match(r"^(.*)::([^:()]+)\((.*)\)$", signature)
        if not match:
            continue
        key = (match[1].split("::")[-1], match[2])
        parameters = [] if match[3] in {"void", ""} else [typename(p) for p in split_args(match[3])]
        website.setdefault(key, []).append({"signature": signature, "parameter_types": parameters, **{field: row.get(field) for field in ["ReturnType", "MonoCount", "XrefCount", "ThisCount", "Type", "Tags", "MonoUsages", "XrefUsages"]}})
    for record in records:
        type_name, method = record["target_type"].split(".")[-1], record["target_method"]
        candidates, source_rows = website.get((type_name, method), []), native.get((type_name, method), [])
        lookup_type, visited = type_name, set()
        while not source_rows and lookup_type in bases and lookup_type not in visited:
            visited.add(lookup_type)
            lookup_type = bases[lookup_type]
            source_rows = native.get((lookup_type, method), [])
            if not candidates:
                candidates = website.get((lookup_type, method), [])
        expected = [typename(t) for t in record["argument_types"]] if record["argument_types"] is not None else None
        if expected is not None:
            candidates = [row for row in candidates if row["parameter_types"] == expected]
            source_rows = [row for row in source_rows if row["parameter_types"] == expected]
        # An unspecified overload with one actual source signature can be resolved;
        # multiple source overloads remain ambiguous even when website has one row.
        if expected is None and len(source_rows) == 1:
            candidates = [row for row in candidates if row["parameter_types"] == source_rows[0]["parameter_types"]]
        record["source_candidates"] = source_rows
        record["website_candidates"] = candidates
        record["matching"] = "exact-signature" if len(candidates) == 1 and (expected is not None or len(source_rows) == 1) else "unique-name-only" if len(candidates) == 1 else "overload-ambiguous" if len(candidates) > 1 or len(source_rows) > 1 else "not-in-dataset"
        record["tags"] = sorted({tag for row in candidates for tag in (row["Tags"] or [])})
        record["empty_body"] = any(row["empty_body"] for row in source_rows)
        record["coroutine_factory"] = any(row["coroutine_factory"] for row in source_rows)
        record["risk"] = "empty-body-review" if record["empty_body"] else "stripped-comparison-only" if "stripped" in record["tags"] else "potential-inline-comparison-only" if "inlined" in record["tags"] else "ambiguous-target-review" if record["matching"] == "overload-ambiguous" else "uncovered-review" if not candidates else "no-site-warning"
    return records


def markdown(report):
    data = report["dataset"]
    rows = report["targets"]
    count = report["matching_counts"]
    migrated_count = sum(item["status"] == "已移除/迁移" for item in report["migrations"])
    lines = ["# IL2CPP patch inline 风险审计", "", f"生成时间：{report['generated_utc']}。本报告根据当前源码自动生成；结构化记录见 [il2cpp-patch-audit.json](il2cpp-patch-audit.json)。", "",
             f"目标是 Among Us **{report['target']['version']} / {report['target']['platform']}**。本机只读 GameAssembly.dll PE header：`{report['target']['binary'].get('machine', '未读取')}` / `{report['target']['binary'].get('architecture', '未知')}`，未启动游戏。网站实际提供的同日期数据是 **{data['version']} / {data['platform']} / 游戏版本 {data['game_version']}**；该版本仅列出 `{'`、`'.join(data['available_platforms'])}`，没有 Itch。版本匹配：`{str(data['version_matches_target']).lower()}`；平台匹配：`{str(data['platform_matches_target']).lower()}`。**Steam 标签只能用于筛选风险，不能据此断言 Itch 的方法被内联、被剥离或不可 patch。**", "",
             "## 数据来源与判定边界", "",
             "网站 [分析器页面](https://allofus.dev/il2cpp/) 的 Select Version 从 [available_versions.json](https://allofus.dev/il2cpp/available_versions.json) 加载；路径由 [实际 JS 资产](https://allofus.dev/static/js/il2cpp.js?v=9) 的 `setupVersionSelector/loadFileFromUrl` 确认。", "",
             f"本次完整 JSON：[同日期 Steam x86 数据]({data['url']})，共 {data['method_count']} 个方法，SHA-256 `{data['sha256']}`。缓存获取时间：{data['retrieved_utc']}；本次模式：`{data['fetch_mode']}`。公开数据缓存位于 ignored `artifacts/il2cpp-audit/`。本审计没有启动游戏、安装 detour、连接游戏服务器或发游戏包。", "",
             "页面 Help 明确：动态调用（抽象/接口/虚方法、Unity 消息等）不会贡献普通 Xref；Potentially Inlined 可能由于动态调用产生误判。因此 `XrefCount=0`、`MonoCount>XrefCount` 或单独的 `inlined` 标签都不等价于“不可 patch”。`used-by-inline` 与 `inlined` 分开：前者通常仍可 patch。`matched` 也不保证所有调用点都会经过已安装 hook。", "",
             "- `stripped`：网站未在它的 IDA dump 找到方法；当前目标平台须另外确认 native pointer、入口与安装结果。不可仅按 Steam 标签删除 Itch patch。",
             "- `empty-body`：当前反编译源码只有空方法体；独立于网站标签。可能没有有效 detour 入口，或被合并到共享空函数，必须避免盲目 hook。",
             "- `shared native address`：需要目标平台真实方法指针/二进制地址证据。本次静态审计没有测量地址，也没有将空 C# 方法等同于已证共享 native 地址。",
             "- `Potentially Inlined`：某些调用点可能绕过方法入口；需要沿具体 native 调用链验证或把逻辑移至已确认的 caller。",
             "", "## 覆盖范围", "",
             f"扫描得到 {report['source_attribute_count']} 个未注释的 `HarmonyPatch` 属性，折叠 type-only 容器和 overload 选择器后为 {report['target_count']} 个目标声明、{report['unique_target_count']} 个唯一目标。{count.get('exact-signature', 0)} 个声明按类型、方法名及参数签名匹配网站；{count.get('not-in-dataset', 0)} 个没有网站记录；未解析动态目标 {len(report['unresolved'])} 个。当前没有 `TargetMethod/TargetMethods` 动态选择器。", "",
             "scanner 处理类/方法级属性、字符串方法名、显式 `Type[]`/单独 `typeof` 参数、继承声明、`MethodType.Getter/Setter`、`ref/out`、C# 与 IDA 基础类型别名，并排除行/块注释。未指定 overload 时采用实际 source signature；发现多个 overload 会标记 ambiguity 而不猜测。条件编译未求值；这份源码清单不能替代 Harmony 在目标运行时的最终 `MethodBase` 解析。所有源文件 SHA-256、完整目标签名、源码位置和网站调用列表保留在 JSON。", "",
             "协程目标均记录 `coroutine_factory=true`：Harmony 指向的是返回 IEnumerator 的 factory，不是生成状态机 `MoveNext`。例如 `HandleGameDataInner`、`WrapUpAndSpawn`、`ShowRole`，需要区分 factory 入口与协程每次恢复的业务位置；不会把网站某个 `_..._d__::MoveNext` 自动替换成项目 target。", "",
             "## 本轮空方法事故与优先判断", "",
             "`RoleBehaviour.AdjustTasks(PlayerControl)` 当前不再出现在 patch 清单。对应只读目标源码 `RoleBehaviour.cs:420` 的 `public virtual void AdjustTasks` 方法完全为空；全目录只找到这一处定义，没有 override。网站同版本 Steam 数据为 `stripped`、Mono/Xref `1/0`。集成工作流本轮已报告该新 hook 引起 DMD 启动 NRE，并删除后进入菜单；本审计未重启游戏复现。这个案例支持避免对空基类方法盲目 hook，不能推广为“所有 Xref 0 或所有 virtual 方法都要删除”。现有目标源码未检出完全空的方法体；auto-property getter 是返回字段的 accessor，不算空方法。", "",
             f"原 5 个 Steam `stripped` target 中 {migrated_count} 个已从当前 patch 清单移除/迁移；下表记录当前状态与替代入口，已迁移项不再列为仍未修复的问题。网站标签仍是不同平台的比较证据。", "",
             "| 原入口 | 当前替代/控制点 | 替代目标的比较数据 | 覆核与验证边界 |", "|---|---|---|---|"]
    for migration in report["migrations"]:
        replacement = "; ".join(f"`{target['signature']}` {','.join(target['Tags'] or [])} {target['MonoCount']}/{target['XrefCount']}" for target in migration["replacement_website"])
        lines.append(f"| `{migration['old_target']}`；{migration['status']} | {migration['replacement_control']} | {replacement} | {migration['review']} |")
    lines += ["", "Dleks 的 CoStartGameHost postfix 仅作用于房主、mod host、HnS、非 FreePlay、map 3：先加载 ShipPrefabs[3]，然后逐次 MoveNext 原 native IEnumerator；normal-mode 的现有 prefix 对 HnS 放行，因此没有用自写 readiness/SelectRoles/Begin 替代 vanilla HnS 协程。AllMapIcons 改用 new MapIconByName，并检查重复图标；没有再 Instantiate 整个 GameStartManager，避免复制 Start、控件和网络处理器。HnS/Dleks 载入和 Airship 平台禁用仍未完成实机覆盖。", "",
             "GameOptionsMenu 的 HideModMenu 在 owning ChangeTab 路径先 CancelBuild，再隐藏 tab；GameSettingMenu.Close 逐个 CancelBuild 并 ReleaseMenus，OnDisable 的 matched 入口复用同一清理。StringOption.Initialize 补回显示值、oldValue、翻译标签与按钮状态，且排除自定义选项，没有触发 UpdateValue 的配置写入。大厅音乐从现有 GameStartManager.Update 调用 UpdateMusic，检查活跃大厅并避免重复开始/停止音效。", "",
             "| 优先级 | 目标/入口 | 证据与触发 | 实际影响与修复方向 |", "|---|---|---|---|",
             "| 中，已完成迁移、待实机 | HnS Dleks preload / Airship 平台 state guard | 新 CoStartGameHost、SetTarget/SetSide/Use 都是 matched；分别预加载所选船体、阻止被禁用平台启动移动并 MarkClean | HnS map 3 readiness/选角/Begin，以及 Airship 初始化与中途移动路径仍须实测，不以静态迁移称为游戏功能通过 |",
             "| 中，需调用链实证 | `GameOptionsMapPicker.Initialize(int)`、`ToggleOption.UpdateValue()` | Steam Potentially Inlined 3/0、1/0 | GUI map 初始化和值更新可能有调用绕过。验证真实 UI 开关与新建/切页路径；必要时放入确认可达的 picker/menu caller，不以测试桩直接调用证明 |",
             "| 已获本轮实机证据 | Phantom 远程 UseAbility → native CheckVanish → host skill → StartAppear | 本轮双 Itch LocalGame 实机已报告两人传送、保持可见并按冷却生效；Steam CheckVanish 缺失的 CmdCheckVanish Xref 已由 prefix 覆盖 | 保留现有 Cmd/native role-RPC 接收边界；没有基于网站 inline 标签修改 native 接收。此证据不扩大为 HnS、Airship 或所有 Phantom 分支通过 |",
             "| 中，协议路径验证 | `InnerNetClient.HandleGameDataInner`、`Constants.GetBroadcastVersion`、`PlayerControl.RpcMurderPlayer` | Steam Potentially Inlined；分别为 coroutine factory、版本 getter、native sender | 若业务依赖某一入口必须检查真实 caller；自定义接收以 PlayerControl.HandleRpc 为边界，不能用 factory hook 证明每次 coroutine resume 均受拦截 |",
             "", "只读残留检查：" + report["follow_up_review"]["moving_platform_guard"],
             "", "## 新 Judge、Phantom、自定义 RPC 与注册", "",
             "当前 Judge 的 MeetingHud.Start/OnDestroy、HudManager.Update、PlayerVoteArea.JudgeOverruleVote、MeetingHud.CmdQueueOverruleVotes、JudgeRole.TryOverrule、JudgeRole.IsBlockedByTasks、ImpostorRole.Deinitialize 均按完整签名匹配 `matched`。其中 TryOverrule 的实际参数是 `InnerNet.PlayerId`，CmdQueueOverruleVotes 为两个 PlayerId 加 UInt16。新增 AdjustTasks hook 已移除，其他 Judge hooks 没有网站 inline/stripped 警告；仍须验证实际 UI 和 RPC 路径。", "",
             "`PlayerControl.HandleRpc(byte, Hazel.MessageReader)` 与 `PlayerPhysics.HandleRpc(byte, Hazel.MessageReader)` 都是 `matched 0/0`，与动态分派吻合。`ShouldProcessRpc(RpcCalls, byte)` 为 matched 1/1；`InnerNetClient.StartRpcImmediately(uint, byte, SendOption, int)` 为 used-by-inline 27/41，而不是 Potentially Inlined。outer 123 接收仍应由 HandleRpc 入口解包；0 Xref 不构成删除理由。", "",
             "`PhantomRole.UseAbility`、`PlayerControl.CmdCheckVanish`、`CmdCheckAppear`、`HandleServerAppear` 为 matched；`CheckVanish/CheckAppear` 的 caller 差异已有 Cmd prefix 覆盖。本轮集成工作流已报告双 Itch 本地联网的远程 UseAbility → native CheckVanish → host skill → StartAppear 路径通过，两人传送、保持可见和冷却均符合预期；本审计没有重启游戏复测。`SetRoleInvisibility` 为 used-by-inline 5/7。", "",
             "`CustomRpcTransport.Register()` 是项目自有方法，由 main.cs 在 Harmony.PatchAll 前调用 `ClassInjector.RegisterTypeInIl2Cpp<TOHERpcMessage>()`；它不是预编译游戏中的 Harmony target。注入类的 SerializeValues 回调与基类虚接口属于 ClassInjector/IL2CPP vtable 集成验证，网站无法给这个新类型提供 inline 标签。没有为获得网站标签而给空基类/abstract placeholder 增加 hook。", "",
             "## Stripped 比较项（与空方法分开）", "",
             "| 项目位置 | 实际签名 | Mono/Xref | 目标源码 |", "|---|---|---|---|"]
    for row in rows:
        if "stripped" in row["tags"]:
            candidate = row["website_candidates"][0]
            local = row["source_candidates"][0] if row["source_candidates"] else {}
            lines.append(f"| {row['file']}:{row['line']} | `{candidate['signature']}` | {candidate['MonoCount']}/{candidate['XrefCount']} | {Path(local.get('file', '')).name}:{local.get('line', '?')}；empty={row['empty_body']} |")
    if not any("stripped" in row["tags"] for row in rows):
        lines.append("| 当前无 active target | 原 5 项均已移除/迁移，见替代入口表 | — | 与 empty/shared 判定保持分开 |")
    lines += ["", "## Potentially Inlined 比较项", "", "以下保留重复 patch 声明，避免漏掉同一 native 入口上的多个模块。", "", "| 项目位置 | 实际签名 | Mono/Xref | 协程 factory |", "|---|---|---|---|"]
    for row in rows:
        if "inlined" in row["tags"]:
            candidate = row["website_candidates"][0]
            lines.append(f"| {row['file']}:{row['line']} | `{candidate['signature']}` | {candidate['MonoCount']}/{candidate['XrefCount']} | {row['coroutine_factory']} |")
    lines += ["", "## 完整现有 patch 清单", "", "`no-site-warning` 表示比较数据没有 inline/stripped 标签；不等于已通过 Itch 实机验证。`ActivityManager.UpdateActivity` 来自项目 `using Discord`，未在此游戏方法 JSON 中找到；未据此判 stripped，也未确认它在当前安装中属于托管还是 IL2CPP 包装器。", "", "| 项目位置 | 声明类 | 解析目标签名 | 网站标签 | 匹配 |", "|---|---|---|---|---|"]
    for row in rows:
        signature = row["website_candidates"][0]["signature"] if len(row["website_candidates"]) == 1 else row["target_type"] + "::" + row["target_method"]
        lines.append(f"| {row['file']}:{row['line']} | {row['patch_class']} | `{signature}` | {', '.join(row['tags']) or '未覆盖'} | {row['matching']} |")
    lines += ["", "## 重跑", "", "```powershell", "python tools/il2cpp_patch_audit.py", "python tools/il2cpp_patch_audit.py --offline", "```", "", "默认读取本机只读反编译目录；其他机器用 `--native-source <目录>` 指定。`--version/--platform` 控制比较数据，`--target-version/--target-platform` 单独标记实际目标，版本/平台不一致会写入报告。每次 HTTP 下载单独进程执行，总时限 10 秒；offline 模式仅使用 ignored 缓存。默认输出 JSON 与本 Markdown，不修改生产代码或游戏。"]
    return "\n".join(lines) + "\n"


def migration_reviews(records, data):
    definitions = [
        ("AprilFoolsMode.ShouldFlipSkeld", ["AmongUsClient.CoStartGameHost"], "HnS map 3 的 CoStartGameHost postfix 预加载，然后继续原 native IEnumerator", "保留 HnS readiness、SelectRoles、Begin；尚未 HnS 实机覆盖"),
        ("StringOption.Start", ["StringOption.Initialize"], "StringOption.Initialize postfix 恢复显示值、oldValue、翻译文本与按钮", "避开 forwarding Start；排除自定义 OptionList，不触发配置 write/callback"),
        ("GameOptionsMenu.OnDisable", ["GameSettingMenu.ChangeTab", "GameSettingMenu.Close", "GameSettingMenu.OnDisable"], "owning ChangeTab 调用 HideModMenu/CancelBuild，Close 与 OnDisable 清理所有 owned tab", "不再 hook forwarding OnDisable；Close 仍有 inline 比较标签，matched owning OnDisable 复用同一 cleanup"),
        ("LobbyBehaviour.Update", ["GameStartManager.Update"], "现有 GameStartManager.Update 调用 guarded UpdateMusic", "检查活跃大厅、声音列表；避免逐帧重复开始/停止"),
        ("MovingPlatformBehaviour.get_IsDirty", ["MovingPlatformBehaviour.Start", "MovingPlatformBehaviour.SetTarget", "MovingPlatformBehaviour.SetSide", "MovingPlatformBehaviour.Use"], "删除 accessor hook；Start/SetSide MarkClean，Use/SetTarget 阻止 disabled 状态移动", "SetTarget 覆盖 native Deserialize(initialState) 调用链；MarkClean 是 matched 0/0；尚未 Airship 实机覆盖"),
    ]
    reviews = []
    for old, replacements, control, review in definitions:
        old_type, old_method = old.split(".")
        still_present = any(row["target_type"] == old_type and row["target_method"] == old_method for row in records)
        website = {candidate["signature"]: candidate for row in records if row["target_type"] + "." + row["target_method"] in replacements for candidate in row["website_candidates"]}
        old_entries = {signature: {field: value.get(field) for field in ["Tags", "MonoCount", "XrefCount"]} for signature, value in data.items() if signature.startswith(f"{old_type}::{old_method}(")}
        reviews.append({"old_target": old, "status": "仍在当前清单，需复核" if still_present else "已移除/迁移", "historical_website": old_entries, "replacement_targets": replacements, "replacement_control": control, "replacement_website": list(website.values()), "review": review})
    return reviews


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", default="2026.8.18")
    parser.add_argument("--platform", default="steam-x86")
    parser.add_argument("--target-platform", default="Itch Windows x86")
    parser.add_argument("--target-version", default="2026.8.18")
    parser.add_argument("--native-source", type=Path, default=Path(r"D:\SharedUserFiles\TestDesktop\opencode-chat\assembly-compare\src-2026.8.18"))
    parser.add_argument("--native-binary", type=Path, default=Path(r"D:\Game\20260818\Itch\GameAssembly.dll"))
    parser.add_argument("--offline", action="store_true")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/il2cpp-patch-audit.json")
    args = parser.parse_args()
    cache = ROOT / "artifacts/il2cpp-audit"
    versions_path = cache / "available_versions.json"
    if not args.offline:
        fetch(BASE, cache / "index.html")
        fetch("https://allofus.dev/static/js/il2cpp.js?v=9", cache / "il2cpp.js")
        fetch(BASE + "available_versions.json", versions_path)
    versions = json.loads(versions_path.read_bytes())
    selected = versions[args.version]
    url = BASE + selected["analyses"][args.platform]
    dataset_path = cache / f"{args.version}-{args.platform}.json"
    if not args.offline:
        fetch(url, dataset_path)
    dataset_bytes = dataset_path.read_bytes()
    data = json.loads(dataset_bytes)
    records, unresolved, attribute_count = inventory()
    native, bases = native_index(args.native_source)
    compare(records, data, native, bases)
    reference = data.get("RoleBehaviour::AdjustTasks(PlayerControl)")
    report = {"generated_utc": datetime.now(timezone.utc).isoformat(), "target": {"version": args.target_version, "platform": args.target_platform, "native_source": str(args.native_source)}, "dataset": {"version": args.version, "game_version": selected["game_version"], "version_matches_target": args.version == args.target_version, "platform": args.platform, "platform_matches_target": args.platform == args.target_platform, "url": url, "sha256": hashlib.sha256(dataset_bytes).hexdigest(), "method_count": len(data), "available_platforms": list(selected["analyses"])}, "source_attribute_count": attribute_count, "target_count": len(records), "unique_target_count": len({(r["target_type"], r["target_method"], str(r["argument_types"])) for r in records}), "matching_counts": dict(Counter(r["matching"] for r in records)), "risk_counts": dict(Counter(r["risk"] for r in records)), "source_file_sha256": {file: hashlib.sha256((ROOT / file).read_bytes()).hexdigest() for file in sorted({r["file"] for r in records})}, "reference_cases": [{"target": "RoleBehaviour.AdjustTasks(PlayerControl)", "currently_patched": any(r["target_type"] == "RoleBehaviour" and r["target_method"] == "AdjustTasks" for r in records), "source_candidates": native.get(("RoleBehaviour", "AdjustTasks"), []), "website": {k: reference.get(k) for k in ["Tags", "MonoCount", "XrefCount"]} if reference else None, "actual_native_shared_address_verified": False}], "limitations": ["Static source scanner, not Harmony runtime target resolution; comments excluded, conditional compilation not evaluated", "Name-only and ambiguous overload matches require review", "Source empty body is not proof of native shared address", "Xref zero is not proof of unpatchability; dynamic calls do not count", "Website Potentially Inlined may be a false positive", "Steam dataset cannot prove Itch native patchability", "No game start, native hooks, server access or network packets"], "unresolved": unresolved, "targets": records}
    report["dataset"]["retrieved_utc"] = datetime.fromtimestamp(dataset_path.stat().st_mtime, timezone.utc).isoformat()
    report["dataset"]["fetch_mode"] = "offline-cache" if args.offline else "online"
    report["target"]["binary"] = pe_metadata(args.native_binary)
    report["migrations"] = migration_reviews(records, data)
    moving_source = read_source(ROOT / "Patches/MovingPlatformBehaviourPatch.cs")
    live_guard = bool(re.search(r"private static bool isDisabled\s*=>\s*Options\.DisableAirshipMovingPlatform\.GetBool\(\)", moving_source))
    report["follow_up_review"] = {"moving_platform_reads_current_option": live_guard, "moving_platform_guard": "原 static 缓存仅由 Start 更新的先后序风险已修复：guard 现在每次读取当前 option，初始 Deserialize 先于 Start 或跨局 option 变化也不会沿用旧缓存。仍需 Airship 实机验证实际移动/禁用行为。" if live_guard else "guard 尚未确认每次读取当前 option；若仅由 Start 更新 static 缓存，初始 Deserialize/SetTarget 先于 Start 或跨局配置变化可能漏放或误拒，需复核。"}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.output.with_suffix(".md").write_text(markdown(report), encoding="utf-8")
    print(json.dumps({key: report[key] for key in ["source_attribute_count", "target_count", "unique_target_count", "matching_counts", "risk_counts", "unresolved"]}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
