#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""instrument-module/scan.py —— 模块埋点候选点定位器（`/instrument-module` 的检索部分）。

## 分工：脚本只定位，不改代码

**这个脚本永远不写 `.cs`。** 它只回答「这个模块里哪些位置按契约该埋点、埋了没有」，
输出一份带文件行号的候选清单；**要不要埋、属性里放哪几个值，由模型读懂上下文之后用 Edit 改**。

为什么不做成自动改写器：正则改 C# 源码必踩嵌套、泛型、字符串字面量、`#if` 这四个坑，
而且埋点的价值全在「属性里放的是不是根因分析用得上的那几个数」——那是语义判断，正则给不出。
一个会把埋点插错地方的脚本，比没有脚本更坏：它让人以为这件事已经自动化了。

判据是 `docs/telemetry.md` 第 2.2 节那四类尺子，**本文件不另立标准**：

    意图入口   每个 XxxIntent 被处理的地方
    状态迁移   状态类里改变对外可见状态的公开方法
    失败分支   return false / throw / catch 之前（失败比成功值钱）
    长耗时     可能超过一帧的异步流程，用 BeginSpan 自动记 ms

以及那一节的「不该埋」：**每帧触发的东西一律不埋**——命中每帧回调的候选点会被丢进「跳过」区。

## 能力边界（清单是线索，不是判决）

靠的是**命名约定与结构特征**，不做语义分析、不解析 C# 语法树：

- 会**漏报**：跨行的方法签名、`partial` 拆开的类型、名字不叫 `XxxIntent` 的意图、
  通过接口/委托间接进来的意图、跨行逐字字符串之后的结构归属。
- 会**误报**：叫 `XxxState` 但其实不是状态类、抛异常只是参数校验（那种埋了是噪音）、
  `return false` 只是普通的布尔返回值。
- 「已埋」只看**同一个方法体内**有没有 Track/BeginSpan 调用，不判断埋的是不是这件事。

所以输出里每一条都带「依据」，让人能三秒钟自己判一次。

## 载体锚定（`.claude/rules/harness-authoring.md` 要求）

- **执行载体**：`/instrument-module <模块>` —— 人主动敲的命令，且由 `/new-feature` 的埋点步
  （验证之前）显式调用。**不挂钩子、不定时跑、不生成任何需要人维护的清单文件**；
  每次现扫，扫完即弃。另有 project-lint 的 `module-missing-telemetry`（WARN 级）在保存
  `*State.cs` / `*Intent.cs` 时提醒「这个模块一条埋点都没有」。
- **状态锚点**：`python .claude/skills/instrument-module/scan.py --selftest`，
  内置样例源码 + 十几条断言，没有 Unity、没有工程也能几秒内判断这一层还活着没有。
- **退场条件**：selftest 跑不过而没人修（说明识别手法跟不上代码形状了），
  或者连着几个模块扫出来的清单全是误报、人人都跳过它——那就**删掉这一层**，
  把四类尺子留在 `docs/telemetry.md` 里靠人工照做，别加提醒。

## 用法

    python .claude/skills/instrument-module/scan.py Sample      # 扫一个模块（大小写随意）
    python .claude/skills/instrument-module/scan.py --path Assets/_Project/Scripts/Runtime/Sample
    python .claude/skills/instrument-module/scan.py --selftest  # 自检，不读工程

纯标准库，无第三方依赖。找不到模块时列出可选模块名并 exit 1；扫描本身不改任何文件。
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

# 工程根：本文件在 <root>/.claude/skills/instrument-module/ 下，往上三层。不写死绝对路径。
ROOT = Path(__file__).resolve().parents[3]
RUNTIME_DIR = ROOT / "Assets" / "_Project" / "Scripts" / "Runtime"

# ══════════════════════════════════════════════════════════════════════════
# 四类尺子（docs/telemetry.md 第 2.2 节）
# ══════════════════════════════════════════════════════════════════════════

CAT_INTENT = "意图入口"
CAT_STATE = "状态迁移"
CAT_FAIL = "失败分支"
CAT_SPAN = "长耗时"

#: 每帧回调：落在这里面的候选点一律丢进「跳过」区（契约 2.2「不该埋」）。
#: Tick / FixedTick / LateTick 是 VContainer 的 ITickable 系列，等价于 Update。
PER_FRAME_METHODS = frozenset({
    "Update", "FixedUpdate", "LateUpdate", "OnGUI",
    "OnDrawGizmos", "OnDrawGizmosSelected", "OnAnimatorMove", "OnAnimatorIK",
    "OnValidate", "Tick", "FixedTick", "LateTick",
})

#: 不值得当「状态迁移」报的方法名（对象协议，不是玩法状态）。
BORING_METHODS = frozenset({
    "ToString", "Equals", "GetHashCode", "Dispose", "Awake", "Start", "OnEnable",
    "OnDisable", "OnDestroy", "Install", "Configure",
})

# ══════════════════════════════════════════════════════════════════════════
# 结构识别（正则 + 大括号计数，不解析语法树）
# ══════════════════════════════════════════════════════════════════════════

TYPE_HEAD_RE = re.compile(
    r"^\s*(?:(?:public|private|protected|internal|abstract|sealed|static|partial|readonly|ref)\s+)*"
    r"(?P<kind>class|struct|interface|enum|record)\s+(?P<name>\w+)"
)

#: `修饰符* 返回类型 方法名(参数)` 后面跟 `{` 或换行；构造函数没有返回类型，靠「方法名 == 类型名」认。
#: 参数用 `[^;]*` 排掉分号，顺带挡住 `for (int i = 0; ...)`；接口里的 `void Foo();` 因为末尾是 `;` 不匹配。
METHOD_HEAD_RE = re.compile(
    r"^\s*(?P<mods>(?:(?:public|private|protected|internal|static|override|virtual|abstract|sealed"
    r"|async|partial|unsafe|extern|new)\s+)*)"
    r"(?:(?P<ret>[\w<>\[\],.?:]+)\s+)?(?P<name>\w+)\s*\((?P<args>[^;]*)\)\s*(?:\{|$)"
)
#: 返回类型里的 `:` 是为了 `global::cfg.Item GetItem(int id)` —— Luban 生成类型全带 `global::` 前缀，
#: 漏掉它整个方法认不出来，那一段的失败分支会被算到类型头上（踩过一次，selftest 钉住）。

#: 上面的正则会把 `else if (x) {` 认成「方法 if」。认错不报警，但它会压栈盖住真正的外层方法。
NOT_METHOD_NAMES = frozenset({
    "if", "while", "for", "foreach", "switch", "catch", "using", "lock", "fixed",
    "return", "yield", "else", "do", "try", "get", "set", "add", "remove",
})

NEW_INTENT_RE = re.compile(r"\bnew\s+(?P<name>\w*Intent)\s*\(")
ARG_INTENT_RE = re.compile(r"(?:^|[(,]\s*)(?:in\s+|ref\s+|out\s+)?(?P<name>\w*Intent)\s+\w+")
THROW_RE = re.compile(r"\bthrow\s+new\s+(?P<name>\w+)")
CATCH_RE = re.compile(r"^\s*(?:\}\s*)?catch\s*\((?P<inner>[^)]*)\)")
RETURN_FALSE_RE = re.compile(r"^\s*return\s+false\s*;")
LOG_ERROR_RE = re.compile(r"\b(?:Log\.Error|Debug\.LogError|Debug\.LogException)\s*\(")
AWAIT_RE = re.compile(r"\bawait\s")

#: 已有埋点：任何 `.Track* (` / `.BeginSpan(` 调用。前面那个点是为了不把自定义的 Track 变量当调用。
TELE_CALL_RE = re.compile(r"\.\s*(?P<api>Track|TrackWarn|TrackError|TrackLevel|BeginSpan)\s*\(")
TELE_EVENT_RE = re.compile(r"\.\s*(?:Track|TrackWarn|TrackError|TrackLevel|BeginSpan)\s*\(\s*\"(?P<evt>[^\"]*)\"")
SCOPE_DECL_RE = re.compile(r"\bITelemetryScope\b|\.\s*Scope\s*\(\s*\"")

#: 意图属性名 → 契约里约定俗成的键（docs/telemetry.md 第 1 节「p 里约定俗成的键」）。
PROP_ALIASES = {
    "id": "id", "itemid": "id", "targetid": "id", "playerid": "id", "unitid": "id",
    "count": "n", "num": "n", "amount": "n", "quantity": "n",
    "key": "key", "name": "name", "slot": "slot", "panel": "panel",
    "depth": "depth", "bytes": "bytes", "reason": "reason",
    "from": "from", "to": "to", "ok": "ok",
}

#: 从 if 条件里挑判定用到的值时，这些词不算数据。
COND_NOISE = frozenset({
    "if", "is", "null", "true", "false", "this", "var", "new", "not", "and", "or",
    "string", "IsNullOrEmpty", "IsNaN", "return", "throw",
})

#: 这几种异常是**程序员错误的守卫**（参数为 null、没实现、已释放），不是玩法判定失败。
#: 埋了只会让日志里全是永远不会发生的分支——契约 2.2 的「失败分支」指的是规则判定不通过。
GUARD_EXCEPTIONS = frozenset({
    "ArgumentNullException", "NotImplementedException", "NotSupportedException",
    "ObjectDisposedException",
})

METHOD_PREFIXES = ("On", "Handle", "Do", "Try", "Apply", "Execute")


def _reconfigure_utf8() -> None:
    """Windows 下默认按系统 ANSI 输出，中文会变乱码，强制切 UTF-8。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def snake(name: str) -> str:
    """PascalCase → snake_case。`BuyItem` → `buy_item`，`UIPanel` → `ui_panel`。"""
    s = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", name)
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", s)
    return s.lower().strip("_")


def event_base(method: str) -> str:
    """方法名 → 事件名词干：去掉 On/Handle 这类前缀与 Async 后缀，再转 snake_case。

    `OnSceneReadyAsync` → `scene_ready`，`HandleBackClicked` → `back_clicked`。
    **只是建议**：事件名要描述「已经发生的事实」，最终叫什么由模型定。
    """
    base = method
    for p in METHOD_PREFIXES:
        if base.startswith(p) and len(base) > len(p) and base[len(p)].isupper():
            base = base[len(p):]
            break
    if base.endswith("Async") and len(base) > 5:
        base = base[:-5]
    return snake(base) or snake(method)


def strip_noncode(line: str, in_block: bool):
    """剥掉注释与字符串字面量，只留下用来数大括号、做匹配的代码骨架。

    要剥字符串是因为 `Log.Info($"{a}")` 里的 `{}` 会把大括号计数带偏，
    结构一错，「这一行属于哪个方法」整个就废了；顺带也避免把注释里提到的
    `new FooIntent(` 当成真调用。

    ⚠ 已知取舍：跨行的逐字字符串（`@"` 开头、下一行才闭合）不跟踪——
      撞上了是那一段归属算错，表现为漏报。做法与 project-lint 的同名函数一致。
    """
    out = []
    i, n = 0, len(line)
    while i < n:
        ch = line[i]
        if in_block:
            if ch == "*" and i + 1 < n and line[i + 1] == "/":
                in_block = False
                i += 2
                continue
            i += 1
            continue
        if ch == "/" and i + 1 < n:
            nxt = line[i + 1]
            if nxt == "/":
                break
            if nxt == "*":
                in_block = True
                i += 2
                continue
        if ch == "@" and i + 1 < n and line[i + 1] == '"':
            i += 2
            while i < n:
                if line[i] == '"':
                    if i + 1 < n and line[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            continue
        if ch in ('"', "'"):
            quote = ch
            i += 1
            while i < n:
                if line[i] == "\\":
                    i += 2
                    continue
                if line[i] == quote:
                    i += 1
                    break
                i += 1
            continue
        out.append(ch)
        i += 1
    return "".join(out), in_block


class Method:
    """一个方法实例：名字、所属类型、签名行、方法体覆盖的行号。"""

    __slots__ = ("mid", "name", "type_name", "sig_line", "sig_text", "mods", "ret", "args", "lines")

    def __init__(self, mid, name, type_name, sig_line, sig_text, mods, ret, args):
        self.mid = mid
        self.name = name
        self.type_name = type_name
        self.sig_line = sig_line
        self.sig_text = sig_text
        self.mods = mods or ""
        self.ret = ret or ""
        self.args = args or ""
        self.lines = []

    @property
    def is_async(self) -> bool:
        return "async" in self.mods

    @property
    def is_public(self) -> bool:
        return "public" in self.mods or "protected" in self.mods or "override" in self.mods


def build_structure(lines):
    """逐行算出「这一行属于哪个类型、哪个方法」，并收集每个方法的方法体行号。

    做法与 project-lint 的 `build_method_owner` 同源：识别声明头 → 等它后面第一个 `{`
    → 连同当时的大括号深度压栈；深度退回就出栈。**刻意复制而不是 import**：
    两个 skill 各自独立，互相 import 会让改一个的正则把另一个打坏。
    """
    n = len(lines)
    code_lines = []
    type_of = [None] * n
    method_of = [None] * n  # mid
    methods = {}

    stack = []  # (kind, name, body_depth, mid)
    depth = 0
    pending = None  # (kind, name, sig_line, mods, ret, args)
    in_block = False
    mid_counter = 0

    for i, raw in enumerate(lines):
        was_block = in_block
        code, in_block = strip_noncode(raw, in_block)
        code_lines.append(code)

        cur_type = next((s[1] for s in reversed(stack) if s[0] == "type"), None)

        if pending is None and not was_block:
            mt = TYPE_HEAD_RE.match(code)
            if mt:
                pending = ("type", mt.group("name"), i, "", "", "")
            else:
                mm = METHOD_HEAD_RE.match(code)
                if mm and mm.group("name") not in NOT_METHOD_NAMES:
                    name = mm.group("name")
                    ret = mm.group("ret")
                    # 有返回类型 = 普通方法；没有但方法名等于当前类型名 = 构造函数。
                    if ret or name == cur_type:
                        pending = ("method", name, i, mm.group("mods"), ret or "", mm.group("args"))

        seen_type = cur_type
        seen_mid = next((s[3] for s in reversed(stack) if s[0] == "method"), None)

        for ch in code:
            if ch == "{":
                depth += 1
                if pending is not None:
                    kind, name, sig_line, mods, ret, args = pending
                    mid = None
                    if kind == "method":
                        mid_counter += 1
                        mid = mid_counter
                        methods[mid] = Method(
                            mid, name, seen_type, sig_line, lines[sig_line].strip(), mods, ret, args)
                    stack.append((kind, name, depth, mid))
                    pending = None
                    if kind == "type":
                        seen_type = name
                    else:
                        seen_mid = mid
            elif ch == "}":
                if stack and depth <= stack[-1][2]:
                    popped = stack.pop()
                    if popped[0] == "method":
                        seen_mid = next((s[3] for s in reversed(stack) if s[0] == "method"), None)
                    else:
                        seen_type = next((s[1] for s in reversed(stack) if s[0] == "type"), None)
                depth -= 1
                if depth < 0:
                    depth = 0

        type_of[i] = seen_type
        method_of[i] = seen_mid
        if seen_mid is not None:
            methods[seen_mid].lines.append(i)

    return code_lines, type_of, method_of, methods


# ══════════════════════════════════════════════════════════════════════════
# 候选点
# ══════════════════════════════════════════════════════════════════════════

class Candidate:
    __slots__ = ("rel", "line", "cat", "event", "props", "why", "status", "where", "note")

    def __init__(self, rel, line, cat, event, props, why, status, where, note=""):
        self.rel = rel
        self.line = line          # 1-based
        self.cat = cat
        self.event = event
        self.props = props        # list[str]
        self.why = why
        self.status = status
        self.where = where        # 类型.方法
        self.note = note


def collect_intent_props(parsed):
    """扫全模块，拿每个 `XxxIntent` 类型的公开属性 → 建议的属性键。"""
    prop_re = re.compile(r"^\s*public\s+[\w<>\[\],.?]+\s+(?P<name>\w+)\s*(?:\{\s*get|=>)")
    result = {}
    for rel, data in parsed.items():
        code_lines, type_of, _method_of, _methods = data
        for i, code in enumerate(code_lines):
            tname = type_of[i]
            if not tname or not tname.endswith("Intent"):
                continue
            m = prop_re.match(code)
            if not m:
                continue
            key = PROP_ALIASES.get(m.group("name").lower(), snake(m.group("name")))
            result.setdefault(tname, [])
            if key not in result[tname]:
                result[tname].append(key)
    return result


def condition_values(code_lines, idx):
    """往回找最近的 `if (...)`，把条件里用到的标识符挑出来当「建议属性」。

    失败分支的价值全在「把判定用到的数值写进 p」（契约 2.2）。
    这只是提词器，最终放什么由模型定。
    """
    for j in range(idx, max(-1, idx - 7), -1):
        code = code_lines[j]
        m = re.search(r"\bif\s*\((?P<cond>.+)\)", code)
        if not m:
            continue
        toks = re.findall(r"[A-Za-z_]\w*(?:\.\w+)?", m.group("cond"))
        out = []
        for t in toks:
            short = t.split(".")[-1]
            # 长度 1 的多半是数字字面量的后缀（`0f` 里的 f、`1m` 里的 m），不是数据
            if len(short) < 2 or short in COND_NOISE or t in COND_NOISE or short.isupper():
                continue
            key = PROP_ALIASES.get(short.lower(), snake(short))
            if key not in out:
                out.append(key)
            if len(out) >= 3:
                break
        return out
    return []


def method_has_telemetry(code_lines, method) -> bool:
    return any(TELE_CALL_RE.search(code_lines[i]) for i in method.lines)


def window_has_telemetry(code_lines, idx, back=3, fwd=8) -> bool:
    lo = max(0, idx - back)
    hi = min(len(code_lines), idx + fwd + 1)
    return any(TELE_CALL_RE.search(code_lines[i]) for i in range(lo, hi))


def scan_file(rel, text, intent_props, parsed_entry):
    """扫一个文件，返回 (候选点, 跳过项, 已有埋点)。"""
    lines = text.splitlines()
    code_lines, type_of, method_of, methods = parsed_entry

    candidates = []
    skipped = []
    existing = []

    def where(i, mid=None):
        """「类型.方法」。mid 显式传进来是为了方法签名那一行——那时还没进方法体。"""
        mid = mid or method_of[i]
        t = (methods[mid].type_name if mid else None) or type_of[i] or "?"
        return f"{t}.{methods[mid].name}" if mid else t

    def per_frame(i, mid=None) -> bool:
        mid = mid or method_of[i]
        return bool(mid) and methods[mid].name in PER_FRAME_METHODS

    def add(i, cat, event, props, why, status, note="", mid=None):
        if per_frame(i, mid):
            skipped.append((rel, i + 1, cat, where(i, mid), "落在每帧回调里，契约 2.2「不该埋」"))
            return
        candidates.append(Candidate(rel, i + 1, cat, event, props, why, status, where(i, mid), note))

    # —— 已有埋点 ——
    # 事件名要从**原始行**取：code_lines 已经把字符串字面量剥掉了（剥它是为了数大括号准）。
    for i, code in enumerate(code_lines):
        if not TELE_CALL_RE.search(code):
            continue
        m = TELE_EVENT_RE.search(lines[i])
        api = TELE_CALL_RE.search(code).group("api")
        existing.append((rel, i + 1, api, m.group("evt") if m else "(非字面量)", where(i)))

    # —— 1. 意图入口 ——
    seen_intent = set()

    # 1a. 方法签名上的意图参数。**在方法表上找，不在行上找**：本工程的大括号另起一行，
    #     签名那一行还没进方法体，按 method_of 取归属会取到 None（踩过一次，selftest 钉住）。
    arg_intents = []
    for mid, meth in methods.items():
        for m in ARG_INTENT_RE.finditer(meth.args):
            arg_intents.append((meth.sig_line, mid, m.group("name")))

    for i, code in enumerate(code_lines):
        names = [m.group("name") for m in NEW_INTENT_RE.finditer(code)]
        mid = method_of[i]
        for sig_line, amid, aname in arg_intents:
            if sig_line == i:
                names.append(aname)
                mid = mid or amid
        for name in names:
            if not name or name == "Intent":
                continue
            key = (mid, name)
            if key in seen_intent:
                continue
            seen_intent.add(key)
            props = intent_props.get(name) or ["id"]
            status = "已埋(同方法)" if mid and method_has_telemetry(code_lines, methods[mid]) else "未埋"
            add(i, CAT_INTENT, snake(name[: -len("Intent")]) or snake(name), props[:4],
                f"{name} 在此被构造 / 被消费", status,
                "属性超过 4 个就拆成两条事件（TelemetryProps.Capacity）" if len(props) > 4 else "",
                mid=mid)

    # —— 2. 状态迁移 & 4. 长耗时 ——
    for mid, meth in methods.items():
        if meth.name in PER_FRAME_METHODS:
            continue
        if meth.name in BORING_METHODS and meth.name != "Dispose":
            continue
        i = meth.sig_line
        body_code = [code_lines[k] for k in meth.lines]
        has_await = any(AWAIT_RE.search(c) for c in body_code)
        is_span = meth.is_async and has_await
        is_state = (
            bool(meth.type_name)
            and re.search(r"State$|StateMachine$", meth.type_name) is not None
            and meth.is_public
            and meth.name != meth.type_name  # 构造函数不算状态迁移
        )
        if not (is_span or is_state):
            continue
        status = "已埋(同方法)" if method_has_telemetry(code_lines, meth) else "未埋"
        if is_span:
            note = "同时是状态迁移入口，一条 BeginSpan 就够（Dispose 时自动带 ms）" if is_state else ""
            add(i, CAT_SPAN, event_base(meth.name), ["ms(自动)"],
                f"async + 方法体内有 await，可能跨帧：{meth.name}", status, note, mid=mid)
        else:
            add(i, CAT_STATE, event_base(meth.name), ["from/to 或改变的那个值"],
                f"状态类 {meth.type_name} 的对外方法：{meth.name}", status, mid=mid)

    # —— 3. 失败分支 ——
    for i, code in enumerate(code_lines):
        mid = method_of[i]
        base = event_base(methods[mid].name) if mid else "op"
        hit = None
        if CATCH_RE.match(code):
            inner = (CATCH_RE.match(code).group("inner") or "").strip()
            hit = (f"{base}_failed", ["异常变量走 TrackError(evt, e)"],
                   f"catch 块：{inner or '无类型'}")
        elif RETURN_FALSE_RE.match(code):
            hit = (f"{base}_rejected", condition_values(code_lines, i) or ["判定用到的数值"],
                   "return false —— 规则判定不通过")
        elif THROW_RE.search(code):
            ex = THROW_RE.search(code).group("name")
            if ex in GUARD_EXCEPTIONS:
                continue  # 程序员错误的守卫，不是玩法失败分支
            hit = (f"{base}_failed", condition_values(code_lines, i) or ["判定用到的数值"],
                   f"throw new {ex}")
        elif LOG_ERROR_RE.search(code):
            hit = (f"{base}_failed", ["判定用到的数值"],
                   "Log.Error —— core.log/unity_error 会转一条，但没有判定用的数值")
        if not hit:
            continue
        evt, props, why = hit
        status = "已埋(同分支)" if window_has_telemetry(code_lines, i) else "未埋"
        add(i, CAT_FAIL, evt, props, why, status)

    candidates.sort(key=lambda c: c.line)

    # 同一个方法里同一类、同一个建议事件名的只留第一条：一个 catch 块里
    # 既有 catch 又有 Log.Error，报两遍等于让人去判两次同一件事。
    merged = {}
    for c in candidates:
        key = (c.rel, c.where, c.cat, c.event)
        if key in merged:
            first = merged[key]
            first.note = (first.note + "；" if first.note else "") + f"同方法第 {c.line} 行还有一处同类分支"
            continue
        merged[key] = c
    return list(merged.values()), skipped, existing


# ══════════════════════════════════════════════════════════════════════════
# 模块扫描与报告
# ══════════════════════════════════════════════════════════════════════════

def resolve_module(name: str):
    """模块名（大小写随意）→ 源码目录。找不到就返回 None。"""
    if not RUNTIME_DIR.is_dir():
        return None
    target = name.strip().strip("/\\").lower()
    for d in sorted(RUNTIME_DIR.iterdir()):
        if d.is_dir() and d.name.lower() == target:
            return d
    return None


def available_modules():
    if not RUNTIME_DIR.is_dir():
        return []
    return [d.name for d in sorted(RUNTIME_DIR.iterdir()) if d.is_dir()]


def scan_dir(src: Path):
    files = sorted(p for p in src.rglob("*.cs"))
    parsed = {}
    texts = {}
    for p in files:
        try:
            text = p.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        rel = str(p.relative_to(ROOT)).replace("\\", "/")
        texts[rel] = text
        parsed[rel] = build_structure(text.splitlines())

    intent_props = collect_intent_props(parsed)

    candidates, skipped, existing = [], [], []
    for rel, text in texts.items():
        c, s, e = scan_file(rel, text, intent_props, parsed[rel])
        candidates += c
        skipped += s
        existing += e

    has_scope = any(SCOPE_DECL_RE.search(t) for t in texts.values())
    return {
        "files": list(texts.keys()),
        "candidates": candidates,
        "skipped": skipped,
        "existing": existing,
        "has_scope": has_scope,
    }


def render(module: str, src: Path, result) -> str:
    rel_src = str(src.relative_to(ROOT)).replace("\\", "/") if src.is_absolute() else str(src)
    cands = result["candidates"]
    todo = [c for c in cands if c.status == "未埋"]
    done = [c for c in cands if c.status != "未埋"]

    out = []
    out.append(f"模块 {module} —— 埋点候选点扫描（判据：docs/telemetry.md 2.2 四类尺子）")
    out.append(f"源码 {rel_src}／{len(result['files'])} 个 .cs　建议埋点模块名：{module.lower()}")
    out.append(
        f"已有埋点 {len(result['existing'])} 处　"
        f"ITelemetryScope 已接入：{'是' if result['has_scope'] else '否'}　"
        f"候选 {len(cands)}（未埋 {len(todo)}／已埋 {len(done)}）　跳过 {len(result['skipped'])}")
    out.append("")

    if todo:
        out.append("【应埋未埋】逐条自己判一次，宁可少埋也不要埋成流水账")
        for n, c in enumerate(todo, 1):
            out.append(f"{n:2}. {c.rel}:{c.line}  [{c.cat}]  {c.where}")
            out.append(f"    建议 {c.event}　属性 {', '.join(c.props)}　依据：{c.why}"
                       + (f"　注意：{c.note}" if c.note else ""))
    else:
        out.append("【应埋未埋】无。四类尺子上能机械认出来的点都已经有埋点了。")
    out.append("")

    if done:
        out.append("【已有埋点覆盖到的候选点】不要重复埋")
        for c in done:
            out.append(f"  - {c.rel}:{c.line} [{c.cat}] {c.where} → {c.status}")
        out.append("")

    if result["existing"]:
        out.append("【现有埋点调用】")
        for rel, line, api, evt, where in result["existing"]:
            out.append(f"  - {rel}:{line} {api}(\"{evt}\") @ {where}")
        out.append("")

    if result["skipped"]:
        out.append("【跳过】每帧触发的一律不埋（契约 2.2「不该埋」），需要每帧数据用 core.perf 采样")
        for rel, line, cat, where, why in result["skipped"]:
            out.append(f"  - {rel}:{line} [{cat}] {where} —— {why}")
        out.append("")

    out.append("【这份清单是线索，不是判决】靠命名约定与结构特征识别，不做语义分析：")
    out.append("  会漏：跨行签名、partial 拆开的类型、不叫 XxxIntent 的意图、经接口/委托间接进来的调用。")
    out.append("  会错：叫 XxxState 但不是状态类、只是参数校验的 throw、普通布尔返回的 return false。")
    out.append("  「已埋」只看同方法内有没有 Track/BeginSpan，不判断埋的是不是这件事。")
    out.append("  怎么用这份清单见 .claude/skills/instrument-module/SKILL.md。")
    return "\n".join(out)


# ══════════════════════════════════════════════════════════════════════════
# 自检（没有 Unity、没有工程也能证明脚本是对的）
# ══════════════════════════════════════════════════════════════════════════

SELFTEST_FILES = {
    "FakeIntent.cs": """
namespace Game.Fake
{
    public readonly struct BuyItemIntent
    {
        public BuyItemIntent(int itemId, int count) { ItemId = itemId; Count = count; }
        public int ItemId { get; }
        public int Count { get; }
    }
}
""",
    "FakeState.cs": """
using System;
namespace Game.Fake
{
    public sealed class FakeState
    {
        private readonly ITelemetryScope telemetry;

        public FakeState(ITelemetryService service) { telemetry = service.Scope("fake"); }

        protected override async UniTask OnSceneReadyAsync(CancellationToken ct)
        {
            var intent = new BuyItemIntent(1002, 3);
            try
            {
                await LoadAsync(ct);
            }
            catch (ArgumentOutOfRangeException e)
            {
                Log.Error("算不出价格");
            }
        }

        public bool TryBuy(BuyItemIntent intent, int gold)
        {
            if (gold < intent.Count)
            {
                return false;
            }
            if (intent.Count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intent));
            }
            return true;
        }

        private void Update()
        {
            var perFrame = new BuyItemIntent(1, 1);
        }
    }
}
""",
    "FakeDone.cs": """
namespace Game.Fake
{
    public sealed class FakeDone
    {
        public void Sell(BuyItemIntent intent)
        {
            telemetry.Track("sell_item", ("id", intent.ItemId), ("n", intent.Count));
        }
    }
}
""",
}


def _selftest_result():
    parsed = {}
    for rel, text in SELFTEST_FILES.items():
        parsed[rel] = build_structure(text.splitlines())
    intent_props = collect_intent_props(parsed)
    candidates, skipped, existing = [], [], []
    for rel, text in SELFTEST_FILES.items():
        c, s, e = scan_file(rel, text, intent_props, parsed[rel])
        candidates += c
        skipped += s
        existing += e
    return candidates, skipped, existing, intent_props


def cmd_selftest() -> int:
    candidates, skipped, existing, intent_props = _selftest_result()

    def has(cat, rel=None, event=None, status=None, line_has=None):
        for c in candidates:
            if c.cat != cat:
                continue
            if rel and c.rel != rel:
                continue
            if event and c.event != event:
                continue
            if status and c.status != status:
                continue
            if line_has and line_has not in c.where:
                continue
            return True
        return False

    checks = [
        ("snake(BuyItem) == buy_item", snake("BuyItem") == "buy_item"),
        ("snake(UIPanel) == ui_panel", snake("UIPanel") == "ui_panel"),
        ("event_base(OnSceneReadyAsync) == scene_ready", event_base("OnSceneReadyAsync") == "scene_ready"),
        ("event_base(HandleBackClicked) == back_clicked", event_base("HandleBackClicked") == "back_clicked"),
        ("意图属性从 BuyItemIntent 推出 id / n",
         intent_props.get("BuyItemIntent") == ["id", "n"]),
        ("认出意图入口 buy_item", has(CAT_INTENT, event="buy_item")),
        ("意图入口带上建议属性 id",
         any("id" in c.props for c in candidates if c.cat == CAT_INTENT)),
        ("认出 catch 失败分支", has(CAT_FAIL, event="scene_ready_failed")),
        ("认出 return false 分支", has(CAT_FAIL, event="buy_rejected")),
        ("return false 带上判定用到的数值",
         any(c.cat == CAT_FAIL and c.event == "buy_rejected" and "n" in c.props for c in candidates)),
        ("认出 throw 分支", has(CAT_FAIL, event="buy_failed")),
        ("认出长耗时 async+await", has(CAT_SPAN, event="scene_ready")),
        ("Update 里的意图被丢进跳过区，不出现在候选里",
         any(s[2] == CAT_INTENT for s in skipped)
         and not any(c.cat == CAT_INTENT and "Update" in c.where for c in candidates)),
        ("已有埋点被认出来", any(e[3] == "sell_item" for e in existing)),
        ("同方法已有埋点标成「已埋(同方法)」",
         has(CAT_INTENT, rel="FakeDone.cs", status="已埋(同方法)")),
        ("构造函数不被当成状态迁移",
         not any(c.cat == CAT_STATE and c.where.endswith(".FakeState") for c in candidates)),
        ("结构跟踪认得出方法归属",
         any(c.where == "FakeState.TryBuy" for c in candidates)),
        ("ArgumentNullException 这类守卫不当失败分支报",
         not any(c.cat == CAT_FAIL and "ArgumentNullException" in c.why for c in candidates)),
        ("同方法同类分支只报一条（catch + 里面的 Log.Error 不报两遍）",
         len([c for c in candidates
              if c.cat == CAT_FAIL and c.where == "FakeState.OnSceneReadyAsync"]) == 1),
        ("global:: 开头的返回类型也认得出方法",
         "GetItem" in str(build_structure(
             ["class A", "{", "    public global::cfg.Item GetItem(int id)", "    {", "        return null;",
              "    }", "}"])[3].get(1).name)),
    ]

    bad = 0
    for name, ok in checks:
        print(("  ✓ " if ok else "  ✗ ") + name)
        if not ok:
            bad += 1
    print()
    print(f"自检：{len(checks) - bad}/{len(checks)} 通过"
          + ("" if bad == 0 else f"，{bad} 条失败——识别手法与样例对不上了，先修脚本再去扫模块"))
    return 0 if bad == 0 else 1


# ══════════════════════════════════════════════════════════════════════════

def main() -> int:
    _reconfigure_utf8()
    ap = argparse.ArgumentParser(
        description="模块埋点候选点扫描（只定位，不改代码）", add_help=True)
    ap.add_argument("module", nargs="?", help="模块名，如 Sample（大小写随意）")
    ap.add_argument("--path", help="直接给源码目录，绕过模块名解析")
    ap.add_argument("--selftest", action="store_true", help="内置样例自检，不读工程")
    args = ap.parse_args()

    if args.selftest:
        return cmd_selftest()

    if args.path:
        src = Path(args.path)
        if not src.is_absolute():
            src = ROOT / src
        module = src.name
    elif args.module:
        module = args.module
        src = resolve_module(module)
        if src is None:
            mods = available_modules()
            print(f"找不到模块 {module}：{RUNTIME_DIR} 下没有这个目录。", file=sys.stderr)
            print("现有模块：" + (", ".join(mods) if mods else "（一个都没有）"), file=sys.stderr)
            return 1
        module = src.name
    else:
        ap.print_help()
        return 1

    if not src.is_dir():
        print(f"不是目录：{src}", file=sys.stderr)
        return 1

    result = scan_dir(src)
    if not result["files"]:
        print(f"模块 {module} 目录下一个 .cs 都没有：{src}", file=sys.stderr)
        return 1
    print(render(module, src, result))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(130)
