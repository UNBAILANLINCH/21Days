#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check —— 行为 eval 的判定器：对一个产出目录跑一条 case 的 deterministic_checks。

## 它解决什么

行为 eval 的老问题是**判定靠人勾选**。人勾选有两个致命伤：勾的人知道这是陷阱测试，
会不自觉往宽里判；而且没人勾的时候整套就停摆了（一套 40 条用例的 eval 体系烂掉，
死因就是这个）。所以本工程的 case 把判定全写成**正则**，机器判，不留人工环节。

判定器只看**产出的文件**，不看对话过程、不看 AI 说了什么。
说得再对、代码写错了照样不通过——这正是要测的东西。

## 载体锚定（`.claude/rules/harness-authoring.md`）

- **执行载体**：`/run-evals`（`.claude/skills/run-evals/SKILL.md`）。本文件自己不定时、
  不自动跑，它是那条流程的一个步骤。
- **状态锚点**（5 秒可证伪）：`python evals/check.py evals/cases/EVAL-001.json <任意空目录>`
  应报「产出目录里一个文件都没有」并 exit 1。
- **退场条件**：`evals/cases/` 空了（用例全删）就连本文件一起删。

## 用法

    python evals/check.py <case.json> <产出目录>

退出码：0 = 全部通过；1 = 有检查未通过；2 = 用法 / case 文件本身有问题。

## deterministic_checks 的字段

    type       "absent"（不该出现）或 "present"（必须出现）
    pattern    Python 正则。默认逐行匹配
    files      产出目录下的 glob，省略则 "**/*.cs"
    why        为什么这条成立——失败时原样打给人看，写清「不这么做会怎样」
    in_method  可选，方法名正则。只看落在这些方法体内的行
               （"Update|LateUpdate|FixedUpdate"、"OnCloseAsync"）。**方法级判据首选这个**
    multiline  可选，true 时整份文件一起匹配（自动开 DOTALL）。
               只在判据跨行、又不是「某个方法体内」形状时才用

纯标准库（方法体边界复用 `project-lint` 的实现，见 `_method_owner`）。
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

MAX_EVIDENCE = 5  # 证据最多列几条：足够定位，又不至于把输出淹掉
DEFAULT_GLOB = "**/*.cs"
LINT_DIR = Path(__file__).resolve().parents[1] / ".claude" / "skills" / "project-lint"


def _method_owner(lines: list) -> list:
    """逐行算出「这一行属于哪个方法」，不在任何方法体里的是 None。

    复用 `project-lint` 里已经在跑的 `build_method_owner`，不另写一份：
    方法体边界要先剥掉注释与字符串再数大括号，是容易写错的活，
    两份实现必然漂移，而漂移的那天两边谁对谁错没人说得清。
    """
    if str(LINT_DIR) not in sys.path:
        sys.path.insert(0, str(LINT_DIR))
    from lint import build_method_owner  # noqa: PLC0415  延迟导入，只有用到 in_method 才需要

    return build_method_owner(lines)


def _reconfigure_utf8() -> None:
    """Windows 的 Python 默认按 cp936 输出，中文会乱码（pitfalls.md 有记）。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def _read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def _rel(base: Path, p: Path) -> str:
    try:
        return str(p.relative_to(base)).replace("\\", "/")
    except ValueError:
        return str(p).replace("\\", "/")


def _line_no(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def _hits(out_dir: Path, files_glob: str, pattern: str, multiline: bool, in_method: str = "") -> tuple:
    """返回 (命中列表, 扫过的文件数)。命中项是 (相对路径, 行号, 行文)。"""
    flags = re.DOTALL if multiline else 0
    rx = re.compile(pattern, flags)
    method_rx = re.compile(in_method) if in_method else None
    hits = []
    scanned = 0
    for path in sorted(out_dir.glob(files_glob)):
        if not path.is_file():
            continue
        scanned += 1
        text = _read(path)
        rel = _rel(out_dir, path)
        if multiline:
            m = rx.search(text)
            if m:
                # 取匹配的**末端**而不是起点：跨行模式通常写成「方法头 …… 违规调用」，
                # 末端落在违规调用那一行，起点只会指到 void Update() 那行，定位没用。
                line = _line_no(text, max(m.start(), m.end() - 1))
                lines = text.splitlines()
                snippet = lines[line - 1].strip() if line <= len(lines) else ""
                hits.append((rel, line, snippet))
            continue
        lines = text.splitlines()
        owner = _method_owner(lines) if method_rx else None
        for i, line in enumerate(lines, 1):
            if owner is not None:
                name = owner[i - 1]
                if not name or not method_rx.fullmatch(name):
                    continue
            if rx.search(line):
                hits.append((rel, i, line.strip()))
    return hits, scanned


def run_case(case: dict, out_dir: Path) -> int:
    checks = case.get("deterministic_checks") or []
    name = case.get("name") or ""
    print(f"[eval] {case.get('id', '?')} {name}")
    if case.get("rule_ref"):
        print(f"       规则依据：{case['rule_ref']}")
    all_files = [p for p in out_dir.rglob("*") if p.is_file()]
    print(f"       产出目录：{out_dir}（{len(all_files)} 个文件）\n")

    if not all_files:
        print("  产出目录里一个文件都没有——被测 agent 没写出任何东西，本条 case 直接判失败。")
        print("  先确认派单时目录传对了、agent 真的落了盘，再谈 checks。\n")
        print(f"[eval] {case.get('id', '?')}：0/{len(checks)} 通过（产出为空）。")
        return 1
    if not checks:
        print("  这条 case 没写 deterministic_checks，判不了。补上再跑。")
        return 2

    passed = 0
    for n, chk in enumerate(checks, 1):
        kind = str(chk.get("type") or "").lower()
        pattern = chk.get("pattern") or ""
        files_glob = chk.get("files") or DEFAULT_GLOB
        multiline = bool(chk.get("multiline"))
        in_method = str(chk.get("in_method") or "")
        why = chk.get("why") or "（这条 check 没写 why —— 补上，失败时没人看得懂）"
        head = f"  [{n}/{len(checks)}]"

        if kind not in ("absent", "present") or not pattern:
            print(f"{head} 失败  check 本身写坏了：type 必须是 absent / present 且 pattern 不能空")
            print(f"        原文：{json.dumps(chk, ensure_ascii=False)}\n")
            continue

        try:
            hits, scanned = _hits(out_dir, files_glob, pattern, multiline, in_method)
        except re.error as exc:
            print(f"{head} 失败  正则编译不过：{exc}")
            print(f"        pattern：{pattern}\n")
            continue
        except ImportError as exc:
            print(f"{head} 失败  in_method 要用 project-lint 的方法体判据，但导不进来：{exc}")
            print(f"        查 {LINT_DIR}/lint.py 还在不在\n")
            continue

        if in_method:
            mode = f"只看 {in_method} 方法体内，逐行"
        else:
            mode = "整份文件" if multiline else "逐行"
        ok = (not hits) if kind == "absent" else bool(hits)
        print(f"{head} {'通过' if ok else '失败'}  {kind:<7} {files_glob}（{scanned} 个文件，{mode}匹配）")
        print(f"        为什么：{why}")
        if ok:
            passed += 1
            if kind == "absent" and scanned == 0:
                print("        注意：这个 glob 一个文件都没匹配到，这条 absent 是空过的，不算证据")
        else:
            print(f"        模式：{pattern}")
            if kind == "absent":
                for rel, line, snippet in hits[:MAX_EVIDENCE]:
                    print(f"        命中：{rel}:{line}  {snippet}")
                if len(hits) > MAX_EVIDENCE:
                    print(f"        （还有 {len(hits) - MAX_EVIDENCE} 处，省略）")
            else:
                print(f"        证据：{files_glob} 下 {scanned} 个文件里一处都没匹配到")
        print()

    total = len(checks)
    verdict = "全部通过" if passed == total else f"{total - passed} 条未通过"
    print(f"[eval] {case.get('id', '?')}：{passed}/{total} 通过，{verdict}。")
    return 0 if passed == total else 1


def main() -> int:
    _reconfigure_utf8()
    if len(sys.argv) != 3:
        print("用法：python evals/check.py <case.json> <产出目录>", file=sys.stderr)
        return 2
    case_path = Path(sys.argv[1])
    out_dir = Path(sys.argv[2])
    if not case_path.is_file():
        print(f"case 文件不存在：{case_path}", file=sys.stderr)
        return 2
    if not out_dir.is_dir():
        print(f"产出目录不存在：{out_dir}", file=sys.stderr)
        return 2
    try:
        case = json.loads(_read(case_path))
    except json.JSONDecodeError as exc:
        print(f"case 不是合法 JSON：{exc}", file=sys.stderr)
        return 2
    return run_case(case, out_dir)


if __name__ == "__main__":
    sys.exit(main())
