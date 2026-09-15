#!/usr/bin/env python3
"""PostToolUse 死循环预警钩子。

追踪同一文件在**同一会话**里被编辑的次数：第 5 次首警，之后每 +3 次再警
（5、8、11、14…）。反复改同一个文件多半是在猜，而不是在改——猜的代价是
上下文被试错填满，最后既没改对也没剩下判断力。

**非阻断**：exit 2 只是把提醒写进 stderr 反馈给 Agent（Claude Code 的约定：
PostToolUse 的 exit 2 = 提醒 Agent，不撤销已完成的编辑）。其余一律 exit 0。
计数缓存在 `.claude/.cache/edit-counts-<会话>.json`，随时可删。
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
CACHE_DIR = ROOT / ".claude" / ".cache"

FIRST = 5   # 第几次首警
EVERY = 3   # 之后每 +N 次再警


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
    """会话标识：负载 `session_id` → 环境变量 → 当天日期（逐级退化）。

    必须按会话隔离：上个会话改了 7 次这个会话第 1 次就报警，是纯噪音。
    """
    sid = str(payload.get("session_id") or "").strip()
    if not sid:
        sid = str(os.environ.get("CLAUDE_SESSION_ID") or "").strip()
    if not sid:
        sid = datetime.datetime.now().strftime("%Y%m%d")
    return re.sub(r"[^\w.\-]", "_", sid)[:100] or "unknown"


def counts_file(sid: str) -> Path:
    """计数文件路径。stop-check.py 也读它，所以这个函数是对外接口。"""
    return CACHE_DIR / ("edit-counts-%s.json" % sid)


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


def _load(fp: Path) -> dict:
    try:
        data = json.loads(fp.read_text(encoding="utf-8"))
    except Exception:  # noqa: BLE001
        return {}
    return data if isinstance(data, dict) else {}


def _save(fp: Path, data: dict) -> None:
    try:
        fp.parent.mkdir(parents=True, exist_ok=True)
        fp.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
    except Exception:  # noqa: BLE001
        pass  # 记不下计数不该挡住干活


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
    key = _rel(tool_input.get("file_path") or "")
    if not key:
        return 0

    fp = counts_file(_session_id(payload))
    counts = _load(fp)
    n = int(counts.get(key, 0) or 0) + 1
    counts[key] = n
    _save(fp, counts)

    if n == FIRST or (n > FIRST and (n - FIRST) % EVERY == 0):
        name = key.rsplit("/", 1)[-1]
        print(
            "[doom-loop] 本会话已对 %s 编辑 %d 次，可能在反复试错。\n"
            "  建议：停下来把现象说清楚，读一遍对应的 .claude/rules/ 与模块 guide，\n"
            "  或换一条修复思路；还是定位不了就把已知信息交回给用户，别继续猜。" % (name, n),
            file=sys.stderr,
        )
        return 2  # 提醒 Agent，不撤销编辑
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        sys.exit(0)  # fail-open
