#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""project-lint —— 数据驱动的 Unity / C# 项目语义 linter。

规则是数据（同目录 `rules.json`），本文件是引擎（逻辑，不随规则变化）。
加一条规则只改 JSON，不动这里。

## 为什么要这一层

Roslyn / IDE 管的是通用 C# 语法与风格；**项目语义违规它们不管** ——
`public` 字段暴露到 Inspector、`Update` 里 `GameObject.Find`、
`gameObject.tag == "Player"` 这类是 Unity 的坑，不是 C# 的错。
写进 `.claude/rules/csharp-code.md` 也拦不住「看了没往心里去」，
**能翻译成正则的坑就别指望人记得** —— 这个文件就是那一层。

## 两种调用方式

    无参数（hook 模式）  从 stdin 读 Claude Code 工具负载 JSON，取 tool_input.file_path
    带文件参数（CLI）    lint 指定的一批文件，便于手动跑与回归测试

## 过滤流水线（逐层收窄，为的是把误报压到几乎没有）

    1. files / path_contains / file_context / file_context_absent  文件级前置，不满足整条规则跳过
    2. pattern                                                     行级主匹配
    3. exclude_patterns                                            命中任一就跳过（排合法写法）
    4. confirm_patterns                                            须再命中任一才算数（二次确认）
    5. in_methods                                                  命中行须落在指定方法体内（如只管 Update）

## 豁免

命中行写 `// lint-ok: <理由>` 放行。要求写理由 —— 绕了也留痕（CLAUDE.md 硬规则 5）。

## 输出约定：成功静默、失败冗余

    无违规 -> 零输出，exit 0（不打扰 Agent）
    有违规 -> 文件 / 行号 / 规则 / 违规行 / 改法 / 依据 打到 stderr，exit 2（喂回给 Agent 自我纠正）

引擎自身出任何岔子一律 exit 0（fail-open）：护栏不该把会话卡死。
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

RULES_PATH = Path(__file__).with_name("rules.json")

#: 豁免标记：命中行写上就放行（理由必须写在冒号后面）。
ALLOW_RE = re.compile(r"//\s*lint-ok:")

#: 方法头识别：`修饰符* 返回类型 方法名(参数)` 后面跟 `{` 或直接换行。
#: 参数部分用 `[^;]*` 排掉分号，顺带挡住 `for (int i = 0; ...)` 这类。
METHOD_HEAD_RE = re.compile(
    r"^\s*(?:(?:public|private|protected|internal|static|override|virtual|async|partial|unsafe)\s+)*"
    r"[\w<>\[\],.]+\s+(\w+)\s*\([^;]*\)\s*(?:\{|$)"
)

#: ⚠ 上面的正则会把 `else if (x) {` 认成「方法 if」——`else` 当返回类型、`if` 当方法名。
#:   认错本身不报警，但它会压进方法栈，把真正的外层方法盖住，导致 in_methods 漏报。
#:   这里按方法名兜一道。
NOT_METHOD_NAMES = frozenset({
    "if", "while", "for", "foreach", "switch", "catch", "using", "lock", "fixed", "return",
})

HINT = (
    "确属误报就在那一行末尾写 `// lint-ok: <理由>` 放行，理由必须写"
    "（CLAUDE.md 硬规则 5：护栏挡住时不拆护栏）。"
)


def _reconfigure_utf8() -> None:
    """Windows 下默认按系统 ANSI 输出，中文提示会变乱码，强制切 UTF-8。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def norm(path: str) -> str:
    return str(path).replace("\\", "/")


def _compile(pattern: str):
    try:
        return re.compile(pattern)
    except re.error:
        return None  # 规则里的正则写坏了就当这条不存在，别让引擎挂掉


def _search(pattern: str, text: str) -> bool:
    pat = _compile(pattern)
    return bool(pat and pat.search(text))


def load_rules() -> list:
    try:
        data = json.loads(RULES_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return []
    rules = data.get("rules")
    return rules if isinstance(rules, list) else []


def strip_noncode(line: str, in_block: bool) -> tuple:
    """剥掉注释与字符串字面量，只留下用来数大括号的代码骨架。

    要剥字符串是因为 `Debug.Log($"{a}")` 里的 `{}` 会把大括号计数带偏，
    方法体范围一错，in_methods 整条规则就废了。

    ⚠ 已知取舍：**跨行的逐字字符串**（`@"` 开头、下一行才闭合）不跟踪。
      这种写法在 Unity 业务代码里罕见，为它维护跨行状态不划算；
      真撞上了只是那一段的方法归属算错 —— 表现为漏报，不会误报。
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
                break  # 行注释，本行后面整段不要
            if nxt == "*":
                in_block = True
                i += 2
                continue
        if ch == "@" and i + 1 < n and line[i + 1] == '"':
            i += 2  # 逐字字符串 @"..."，内部 "" 是转义
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


def build_method_owner(lines: list) -> list:
    """逐行算出「这一行属于哪个方法」，没落在任何方法体里就是 None。

    做法：识别方法头 -> 等它后面第一个 `{` -> 连同当时的大括号深度压栈；
    深度退回到方法体起点以下就出栈。注释与字符串已经剥干净，计数才准。

    ⚠ 已知取舍：构造函数（`public PlayerMovement()`，没有返回类型那一段）
      不被识别成方法 —— 正则要求方法名前面有返回类型 token。
      现有规则（Update/FixedUpdate/LateUpdate/OnGUI）不关心构造函数，够用。
    """
    owner = [None] * len(lines)
    stack = []  # [(方法名, 方法体起始深度)]
    depth = 0
    pending = None  # 已认出方法头、还没等到 `{`
    in_block = False

    for idx, raw in enumerate(lines):
        was_in_block = in_block
        code, in_block = strip_noncode(raw, in_block)

        if pending is None and not was_in_block:
            m = METHOD_HEAD_RE.match(code)
            if m and m.group(1) not in NOT_METHOD_NAMES:
                pending = m.group(1)

        # 本行归属：进入本行时栈顶那个方法；若本行当场压入新方法，按新的算
        # （`void Update() { Debug.Log("x"); }` 这种一行写完的要能抓到）。
        seen = stack[-1][0] if stack else None

        for ch in code:
            if ch == "{":
                depth += 1
                if pending is not None:
                    stack.append((pending, depth))
                    pending = None
                    seen = stack[-1][0]
            elif ch == "}":
                if stack and depth <= stack[-1][1]:
                    stack.pop()
                depth -= 1
                if depth < 0:
                    depth = 0

        owner[idx] = seen
    return owner


def lint_file(path: str, rules: list) -> list:
    """lint 一个文件，返回已格式化的违规条目。读不到 / 后缀没规则管就返回空。"""
    p = Path(path)
    suffix = p.suffix.lower()
    try:
        if not p.is_file():
            return []
        text = p.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []

    npath = norm(str(p))
    lines = text.splitlines()
    owner = None  # 惰性计算：没有 in_methods 规则就不做这趟大括号扫描
    out = []

    for rule in rules:
        # —— 第 1 层：文件级前置 ——
        files = [str(f).lower() for f in (rule.get("files") or [".cs"])]
        if suffix not in files:
            continue
        contains = rule.get("path_contains")
        if contains and not any(norm(c) in npath for c in contains):
            continue
        fctx = rule.get("file_context")
        if fctx and not _search(fctx, text):
            continue
        fabsent = rule.get("file_context_absent")
        if fabsent and _search(fabsent, text):
            continue

        pat = _compile(rule.get("pattern") or "")
        if pat is None or not rule.get("pattern"):
            continue
        excludes = [c for c in (_compile(e) for e in rule.get("exclude_patterns") or []) if c]
        confirms = [c for c in (_compile(e) for e in rule.get("confirm_patterns") or []) if c]
        in_methods = rule.get("in_methods") or []
        if in_methods and owner is None:
            owner = build_method_owner(lines)

        for i, line in enumerate(lines):
            # —— 第 2 层：行级主匹配 ——
            if not pat.search(line):
                continue
            if ALLOW_RE.search(line):
                continue  # 写了理由的豁免行
            # —— 第 3 层：排除合法写法 ——
            if any(e.search(line) for e in excludes):
                continue
            # —— 第 4 层：二次确认 ——
            if confirms and not any(c.search(line) for c in confirms):
                continue
            # —— 第 5 层：必须落在指定方法体内 ——
            if in_methods and (owner is None or owner[i] not in in_methods):
                continue

            body = line.strip()
            tpl = rule.get("violation_tpl") or "{line}"
            try:
                msg = tpl.format(line=body)
            except (KeyError, IndexError, ValueError):
                msg = f"{tpl} {body}"
            out.append(
                f"  第 {i + 1} 行  [{rule.get('rule') or rule.get('id') or '?'}] {msg}\n"
                f"      改法：{rule.get('fix', '')}\n"
                f"      依据：{rule.get('ref', '')}"
            )
    return out


def collect_paths() -> list:
    """带参数 = CLI 模式；不带参数 = hook 模式，从 stdin 负载里取路径。"""
    if len(sys.argv) > 1:
        return sys.argv[1:]
    try:
        # ⚠ 必须从 buffer 读再按 UTF-8 解码：Windows 下 sys.stdin 按系统 ANSI(GBK) 解，
        #   负载里的中文路径会被读坏。
        raw = sys.stdin.buffer.read().decode("utf-8", "replace")
    except Exception:  # noqa: BLE001
        return []
    if not raw.strip():
        return []
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        return []
    tool_input = payload.get("tool_input") or {}
    fp = tool_input.get("file_path") or tool_input.get("notebook_path")
    return [fp] if fp else []


def main() -> int:
    _reconfigure_utf8()
    rules = load_rules()
    if not rules:
        return 0
    blocks = []
    for path in collect_paths():
        found = lint_file(path, rules)
        if found:
            blocks.append(f"[project-lint] {norm(path)}\n" + "\n".join(found))
    if blocks:
        print("\n\n".join(blocks) + "\n\n" + HINT, file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        sys.exit(0)  # fail-open：引擎自己出错就放行，别卡死会话
