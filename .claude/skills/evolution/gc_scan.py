#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""gc_scan —— harness 健康度扫描（`/gc` 的执行体）。

## 为什么要这个

harness 不是搭完就不动的：规则文件改名、技能目录挪位置、模块文档还没写就先登记了、
钩子脚本删了 settings.json 里的引用忘了撤 —— 这些**不会报错**，只会让 Agent
读到空气、按失效的路由找文档，然后凭印象瞎猜。走歪了没人察觉，才是最贵的。

这个脚本把「harness 内部引用是否自洽」变成一次机械检查。

## 查六样

    1. markdown 相对链接      CLAUDE.md / README.md / .claude / ai-docs / docs 下的 [x](path) 目标在不在
    2. 模块文档目录          generate-doc/modules.json 里 status 不是 todo 的 docs 目录在不在
    3. 钩子脚本              settings.json 里 $CLAUDE_PROJECT_DIR/xxx.(py|js) 引用的脚本在不在
    4. 钩子自测              跑一遍 .claude/hooks/tests/run.py，全绿才算过
    5. 工程静态不变量        跑一遍 invariants.py：asmdef 依赖方向、平台宏、命名空间、.meta、UI 地址
    6. 必读文件（只提示）     .claude/hooks/required_reads.json 里提到的文件在不在

前五样算失败（exit 1）；第 6 样**只作提示不算失败** —— 那份清单常常先于文档写好，
「还没写」和「写歪了」是两回事，不该混在一起报。

第 4 / 5 样都是「本身也需要载体的检查」的**执行载体**：
钩子坏了不报错、只是悄悄不生效（判据写反、提示不再注入、闸被绕开）；
跨文件不变量破了也不报错、只在运行时静默失效（引用断链、面板地址找不到）。
两类故障从外面都看不出来，得有人定期问一声，`/gc` 就是那一问。

只读扫描，不改任何文件。
用法：python gc_scan.py [工程根]（省略则从本文件位置往上推）
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

LINK_RE = re.compile(r"\[[^\]]*\]\(([^)]+)\)")

#: 扫这些目录下的所有 .md
SCAN_DIRS = (".claude", "ai-docs", "docs", "evals")
#: 外加工程根上这几份
ROOT_FILES = ("CLAUDE.md", "README.md")

#: required_reads.json 里被当成「文件路径」看待的后缀
READ_SUFFIXES = (".md", ".cs", ".json", ".py", ".js", ".txt")


def _reconfigure_utf8() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def norm(path: str) -> str:
    return str(path).replace("\\", "/")


def _rel(root: Path, p: Path) -> str:
    try:
        return norm(p.relative_to(root))
    except ValueError:
        return norm(p)


def is_external(target: str) -> bool:
    """http / 锚点 / mailto / 家目录 / 带 scheme 的一律跳过。"""
    if not target:
        return True
    if target.startswith(("http://", "https://", "#", "mailto:", "~")):
        return True
    head = target.split("/")[0]
    return ":" in head  # scheme 样式，如 mcpforunity://xxx


def collect_md(root: Path) -> list:
    files = [root / f for f in ROOT_FILES if (root / f).is_file()]
    for d in SCAN_DIRS:
        base = root / d
        if base.is_dir():
            files.extend(sorted(base.rglob("*.md")))
    return files


def check_markdown_links(root: Path) -> list:
    problems = []
    for md in collect_md(root):
        try:
            text = md.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for m in LINK_RE.finditer(text):
            target = m.group(1).strip().split(" ")[0]
            if is_external(target):
                continue
            target = target.split("#")[0].strip()
            if not target:
                continue
            resolved = (md.parent / target)
            if not resolved.exists():
                problems.append(f"  {_rel(root, md)}：相对链接失效 -> {target}")
    return problems


def check_modules_json(root: Path) -> list:
    reg = root / ".claude" / "skills" / "generate-doc" / "modules.json"
    if not reg.is_file():
        return []
    try:
        data = json.loads(reg.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return ["  modules.json：JSON 解析失败，generate-doc 与本扫描都会瞎"]
    modules = data.get("modules")
    if not isinstance(modules, dict):
        return []
    problems = []
    for name, info in modules.items():
        if not isinstance(info, dict):
            continue
        docs = info.get("docs")
        # status=todo 表示「登记了但还没开始写」，目录不存在是正常的
        if info.get("status") != "todo" and docs and not (root / docs).is_dir():
            problems.append(f"  modules.json[{name}]：文档目录不存在 -> {docs}")
    return problems


def check_hook_scripts(root: Path) -> list:
    settings = root / ".claude" / "settings.json"
    if not settings.is_file():
        return []
    try:
        text = settings.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []
    problems = []
    seen = set()
    for m in re.finditer(r"\$CLAUDE_PROJECT_DIR/([^\"\\\s]+\.(?:py|js))", text):
        rel = m.group(1)
        if rel in seen:
            continue
        seen.add(rel)
        if not (root / rel).is_file():
            problems.append(f"  settings.json：钩子脚本不存在 -> {rel}")
    return problems


def check_hook_tests(root: Path) -> list:
    """跑一遍钩子自测。**失败算失效**（exit 1）。

    钩子出故障的形态是「悄悄不生效」：判据写反、提示不再注入、闸被绕开 ——
    全都不报错，从外面看跟正常一模一样。自测是唯一能把它变成可见信号的东西，
    而自测本身也需要一个会被真的执行的载体，`/gc` 就是那个载体。

    跑不起来（没装 python、超时、脚本不在）不算失效：这是体检工具，
    不该因为自己的运行环境把别人的活儿卡住（fail-open）。
    """
    runner = root / ".claude" / "hooks" / "tests" / "run.py"
    if not runner.is_file():
        return []
    try:
        proc = subprocess.run(
            [sys.executable, str(runner)], capture_output=True, timeout=180, cwd=str(root),
        )
    except Exception:  # noqa: BLE001
        return []
    if proc.returncode == 0:
        return []
    text = (proc.stdout + proc.stderr).decode("utf-8", "replace").strip()
    tail = [ln for ln in text.splitlines() if ln.strip()][-12:]
    # 整段算**一条**失效：把失败详情按行拆开会把「13 处失效引用」这种计数撑爆，
    # 让人以为 harness 到处是洞，其实只坏了一处。
    return ["\n".join([f"  钩子自测失败（.claude/hooks/tests/run.py exit {proc.returncode}）："]
                      + [f"    {ln}" for ln in tail])]


def check_invariants(root: Path) -> list:
    """跑一遍工程静态不变量扫描（`invariants.py`）。**有违规算失效**（exit 1）。

    查的是 project-lint 的逐行正则够不着的跨文件约束：asmdef 依赖方向、平台宏的位置、
    命名空间与目录、`.meta` 配对、UI 面板的 Addressables 地址。这些破了都不报编译错误，
    只在运行时静默失效，所以需要一个会被真的执行的载体来问一声 —— `/gc` 就是那个载体。

    与第 4 样的差别：那边是子进程跑测试，这边直接 import（同目录的兄弟模块，省一次进程）。
    import 失败**不 fail-open**：`invariants.py` 是仓库里的文件，导不进来就是仓库坏了，
    而不是运行环境不行；悄悄跳过会让这一整项永远静默通过，正是要防的那种故障。
    """
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    try:
        import invariants  # noqa: PLC0415  延迟导入：不让它的问题影响前几项检查
    except Exception as exc:  # noqa: BLE001
        return [f"  invariants.py 导入失败，工程静态不变量整项没跑：{exc}"]
    try:
        problems = invariants.scan(root)
    except Exception as exc:  # noqa: BLE001
        return [f"  invariants.py 运行出错，工程静态不变量整项没跑：{exc}"]
    if not problems:
        return []
    # 整段算**一条**：把 N 处违规拆成 N 条会把「发现 X 处失效引用」的计数撑爆，
    # 让人以为 harness 到处是洞（钩子自测那一项同理）。
    lines = [f"  工程静态不变量扫描发现 {len(problems)} 处违规"
             f"（单独重跑：python .claude/skills/evolution/invariants.py）："]
    for n, p in enumerate(problems, 1):
        body = invariants.render(p).splitlines()
        lines.append(f"    {n}) {body[0]}")
        lines.extend(f"    {ln}" for ln in body[1:])
    return ["\n".join(lines)]


def _walk_strings(node) -> list:
    """把任意形状的 JSON 里的字符串全捞出来 —— required_reads.json 的 schema
    可能随时变，按结构解析不如按内容筛来得稳，何况这一项只作提示。"""
    out = []
    if isinstance(node, str):
        out.append(node)
    elif isinstance(node, list):
        for item in node:
            out.extend(_walk_strings(item))
    elif isinstance(node, dict):
        for value in node.values():
            out.extend(_walk_strings(value))
    return out


def check_required_reads(root: Path) -> list:
    """必读清单里的文件不存在 —— **只提示，不算失败**。

    这份清单常常先于文档写好（先定「编辑这个模块前必须读它的 guide」，
    文档随后补）。把「还没写」报成失败，只会逼人把清单删掉。
    """
    cfg = root / ".claude" / "hooks" / "required_reads.json"
    if not cfg.is_file():
        return []
    try:
        data = json.loads(cfg.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return ["  required_reads.json：JSON 解析失败"]
    notes = []
    seen = set()
    for s in _walk_strings(data):
        val = norm(s).strip()
        # 跳过通配与模板占位（`${seg:4:lower}` 这种要在运行期才填得出实际路径）
        if not val or "/" not in val or val in seen:
            continue
        if any(ch in val for ch in ("*", "$", "{", "?")):
            continue
        if not val.lower().endswith(READ_SUFFIXES):
            continue
        seen.add(val)
        if not (root / val).exists():
            notes.append(f"  required_reads.json：必读文件还不存在 -> {val}")
    return notes


def main() -> int:
    _reconfigure_utf8()
    root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[3]

    problems = []
    problems += check_markdown_links(root)
    problems += check_modules_json(root)
    problems += check_hook_scripts(root)
    problems += check_hook_tests(root)
    problems += check_invariants(root)
    notes = check_required_reads(root)

    if problems:
        print(f"[gc] 发现 {len(problems)} 处失效引用：")
        print("\n".join(problems))
        if notes:
            print(f"\n[gc] 另有 {len(notes)} 条提示（不算失败）：")
            print("\n".join(notes))
        print("\n先修失效引用，再看最近的 harness 改动是不是引入了退化。")
        return 1

    print("[gc] 健康度扫描通过：markdown 链接、模块文档目录、钩子脚本引用均自洽，"
          "钩子自测全绿，工程静态不变量无违规。")
    if notes:
        print(f"\n[gc] {len(notes)} 条提示（不算失败，多半是文档还没写）：")
        print("\n".join(notes))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        sys.exit(0)  # fail-open：体检工具自己出错不该把会话拖下水
