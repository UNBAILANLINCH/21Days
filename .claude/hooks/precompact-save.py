#!/usr/bin/env python3
"""PreCompact 工作态快照钩子。

上下文压缩会丢细节，最先丢的往往是「我到底改了哪些文件」——而这恰恰是
压缩后第一句话就要用的。压缩前把 git 工作态存成快照，并通过 additionalContext
告诉压缩后的 Agent 去哪捞回来。

快照：`git status --short` + `git diff --stat` → `.claude/.cache/precompact-state.txt`。
工作区干净（两条都空）就什么都不做，静默 exit 0。永不阻断压缩。
"""
from __future__ import annotations

import datetime
import json
import subprocess
import sys
from pathlib import Path

# 工程根：本文件在 <root>/.claude/hooks/ 下，往上两层。不写死绝对路径。
ROOT = Path(__file__).resolve().parents[2]
SNAP = ROOT / ".claude" / ".cache" / "precompact-state.txt"
SNAP_REL = ".claude/.cache/precompact-state.txt"


def _utf8_stdio() -> None:
    """Windows 下 stdout/stderr 默认走 cp936，中文提示会乱码。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def _git(*args: str) -> str:
    """跑一条 git，失败就当没输出。`--no-pager` 免得 diff 卡在分页器上。"""
    try:
        proc = subprocess.run(
            ["git", "-C", str(ROOT), "-c", "core.quotepath=false", "--no-pager", *args],
            capture_output=True, timeout=15,
        )
    except Exception:  # noqa: BLE001
        return ""
    return proc.stdout.decode("utf-8", "replace")


def main() -> int:
    _utf8_stdio()
    try:  # 负载读掉就行，本钩子不依赖里面的字段
        sys.stdin.buffer.read()
    except Exception:  # noqa: BLE001
        pass

    status = _git("status", "--short")
    diffstat = _git("diff", "--stat")
    if not status.strip() and not diffstat.strip():
        return 0  # 工作区干净，没什么可存的

    body = (
        "# 压缩前工作态快照（%s）\n\n"
        "## git status --short\n\n%s\n"
        "## git diff --stat\n\n%s\n"
        % (datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"), status, diffstat)
    )
    try:
        SNAP.parent.mkdir(parents=True, exist_ok=True)
        SNAP.write_text(body, encoding="utf-8")
    except Exception:  # noqa: BLE001
        return 0  # 存不下就别提示了，免得指向一个不存在的文件

    ctx = (
        "上下文即将压缩。压缩前的工作态（改了哪些文件、各改了多少行）已存到 "
        + SNAP_REL
        + "，压缩后若记不清此前改动，直接读这个文件，不要凭印象复述。"
    )
    print(json.dumps({
        "hookSpecificOutput": {"hookEventName": "PreCompact", "additionalContext": ctx}
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        sys.exit(0)  # fail-open
