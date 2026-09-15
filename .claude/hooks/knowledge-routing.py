#!/usr/bin/env python3
"""PreToolUse 知识路由钩子（CLAUDE.md「知识检索路由」的 L2 / L3 自动注入）。

编辑某个文件之前，把该读的知识摆到面前，不用 Agent 自己去找：

    L2 模式匹配：`.claude/rules/*.md` 里 glob 命中该文件的规则
                （`alwaysApply: true` 的已经常驻在上下文里，跳过）
    L3 模块级：  `Assets/_Project/Scripts/Runtime/<Module>/` 对应的
                `ai-docs/docs/modules/<模块小写>/<模块小写>-module-guide.md`（存在才提）

通过 PreToolUse 的 additionalContext 注入。同一文件每会话只提示一次（缓存去重），
否则每次编辑刷屏，刷多了就没人看了。

**永不阻断**：无命中、已提示过、自身异常，一律静默 exit 0。
强制「必读」是另一个钩子（required-reads.py）的事，这里只做提示。
"""
from __future__ import annotations

import datetime
import json
import os
import re
import sys
from pathlib import Path

# 工程根：本文件在 <root>/.claude/hooks/ 下，往上两层。不写死绝对路径。
ROOT = Path(__file__).resolve().parents[2]
RULES_DIR = ROOT / ".claude" / "rules"
MODULES_DIR = ROOT / "ai-docs" / "docs" / "modules"
CACHE_DIR = ROOT / ".claude" / ".cache"

MODULE_RE = re.compile(r"^Assets/_Project/Scripts/Runtime/([^/]+)/", re.IGNORECASE)


def _utf8_stdio() -> None:
    """Windows 下 stdout/stderr 默认走 cp936，中文提示会乱码。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def _payload() -> dict:
    try:
        raw = sys.stdin.buffer.read().decode("utf-8", "replace")
    except Exception:  # noqa: BLE001
        return {}
    if not raw.strip():
        return {}
    try:
        data = json.loads(raw)
    except Exception:  # noqa: BLE001
        return {}
    return data if isinstance(data, dict) else {}


def _session_id(payload: dict) -> str:
    """会话标识：负载 `session_id` → 环境变量 → 当天日期（逐级退化）。"""
    sid = str(payload.get("session_id") or "").strip()
    if not sid:
        sid = str(os.environ.get("CLAUDE_SESSION_ID") or "").strip()
    if not sid:
        sid = datetime.datetime.now().strftime("%Y%m%d")
    return re.sub(r"[^\w.\-]", "_", sid)[:100] or "unknown"


def _rel(raw_path: str) -> str:
    """归一成相对工程根的正斜杠路径。负载里可能是 `X:\\dir\\file.cs` 形态。"""
    s = str(raw_path or "").replace("\\", "/").strip()
    if not s:
        return ""
    while s.startswith("./"):
        s = s[2:]
    root = ROOT.as_posix()
    if s.lower().startswith(root.lower() + "/"):
        return s[len(root) + 1:]
    if re.match(r"^[A-Za-z]:/", s) or s.startswith("/"):
        try:
            rp = Path(s).resolve().as_posix()
        except Exception:  # noqa: BLE001
            return s
        if rp.lower().startswith(root.lower() + "/"):
            return rp[len(root) + 1:]
    return s


def _glob_to_regex(glob: str) -> str:
    """glob → 正则：`**` 匹配任意含 `/` 的串，`*` 匹配不含 `/` 的串。

    `**/` 特殊处理成 `(?:.*/)?`，让 `**/*.cs` 也能匹配工程根下的 `Foo.cs`；
    否则 `**` 强行要一层目录，根下的文件会漏掉规则注入。
    """
    out = ""
    i = 0
    n = len(glob)
    while i < n:
        if glob.startswith("**/", i):
            out += "(?:.*/)?"
            i += 3
        elif glob.startswith("**", i):
            out += ".*"
            i += 2
        elif glob[i] == "*":
            out += "[^/]*"
            i += 1
        elif glob[i] == "?":
            out += "[^/]"
            i += 1
        else:
            out += re.escape(glob[i])
            i += 1
    return out


def _parse_frontmatter(text: str) -> dict:
    """只取需要的两个字段：`globs`（JSON 数组）与 `alwaysApply`。

    不引 yaml：标准库没有，规则文件的 frontmatter 也简单到不值得多一个依赖。
    """
    m = re.match(r"^---\s*\n(.*?)\n---", text, re.DOTALL)
    fm: dict = {"globs": [], "alwaysApply": False}
    if not m:
        return fm
    block = m.group(1)
    gm = re.search(r"^globs:\s*(\[.*\])\s*$", block, re.MULTILINE)
    if gm:
        try:
            globs = json.loads(gm.group(1))
            if isinstance(globs, list):
                fm["globs"] = [str(g) for g in globs]
        except Exception:  # noqa: BLE001
            pass
    fm["alwaysApply"] = bool(re.search(r"^alwaysApply:\s*true\s*$", block, re.MULTILINE))
    return fm


def _matching_rules(rel: str) -> list[str]:
    """glob 命中当前文件的规则文件（alwaysApply 的跳过：它们已经常驻）。"""
    hits: list[str] = []
    if not RULES_DIR.is_dir():
        return hits
    for rule in sorted(RULES_DIR.glob("*.md")):
        try:
            fm = _parse_frontmatter(rule.read_text(encoding="utf-8"))
        except Exception:  # noqa: BLE001
            continue
        if fm.get("alwaysApply"):
            continue
        for g in fm.get("globs") or []:
            try:
                if re.fullmatch(_glob_to_regex(g), rel, re.IGNORECASE):
                    hits.append(".claude/rules/" + rule.name)
                    break
            except re.error:
                continue
    return hits


def _module_guide(rel: str) -> str | None:
    """`Assets/_Project/Scripts/Runtime/<Module>/` → 该模块的 guide（存在才返回）。"""
    m = MODULE_RE.match(rel)
    if not m:
        return None
    mod = m.group(1).lower()
    guide = MODULES_DIR / mod / (mod + "-module-guide.md")
    if guide.is_file():
        return "ai-docs/docs/modules/%s/%s-module-guide.md" % (mod, mod)
    return None


def _already_seen(sid: str, key: str) -> bool:
    """本会话是否已对这个文件提示过。记不下就当没提示过（宁可多提一次）。"""
    cache = CACHE_DIR / ("routing-seen-%s.txt" % sid)
    try:
        seen = set(cache.read_text(encoding="utf-8").splitlines())
    except Exception:  # noqa: BLE001
        seen = set()
    if key in seen:
        return True
    try:
        cache.parent.mkdir(parents=True, exist_ok=True)
        with cache.open("a", encoding="utf-8") as f:
            f.write(key + "\n")
    except Exception:  # noqa: BLE001
        pass
    return False


def main() -> int:
    _utf8_stdio()
    payload = _payload()
    if not payload:
        return 0
    if str(payload.get("tool_name") or "") not in ("Edit", "Write", "MultiEdit"):
        return 0
    tool_input = payload.get("tool_input") or {}
    if not isinstance(tool_input, dict):
        return 0
    rel = _rel(tool_input.get("file_path") or "")
    if not rel:
        return 0

    hints: list[str] = []
    rules = _matching_rules(rel)
    if rules:
        hints.append("适用规则: " + " · ".join(rules))
    guide = _module_guide(rel)
    if guide:
        hints.append("模块文档（编辑前必读）: " + guide)
    if not hints:
        return 0
    if _already_seen(_session_id(payload), rel.lower()):
        return 0

    ctx = "【知识路由】编辑 %s 相关知识：\n- %s" % (rel.rsplit("/", 1)[-1], "\n- ".join(hints))
    print(json.dumps({
        "hookSpecificOutput": {"hookEventName": "PreToolUse", "additionalContext": ctx}
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        sys.exit(0)  # fail-open：提示型钩子绝不因自身 bug 挡住编辑
