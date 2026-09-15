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
import os
import re
import subprocess
import sys
from pathlib import Path

# 工程根：本文件在 <root>/.claude/hooks/ 下，往上两层。不写死绝对路径。
ROOT = Path(__file__).resolve().parents[2]
SNAP = ROOT / ".claude" / ".cache" / "precompact-state.txt"
SNAP_REL = ".claude/.cache/precompact-state.txt"

sys.path.insert(0, str(Path(__file__).resolve().parent))
try:
    from _hook_common import should_emit
except Exception:  # noqa: BLE001  去重件缺失时宁可多注入一次
    def should_emit(session_id: str, text: str, tag: str = "") -> bool:  # type: ignore[misc]
        return True


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
    payload: dict = {}
    try:  # 负载只用来取 session_id（去重用），取不到就退化成 unknown
        raw = sys.stdin.buffer.read().decode("utf-8", "replace")
        if raw.strip():
            data = json.loads(raw)
            if isinstance(data, dict):
                payload = data
    except Exception:  # noqa: BLE001
        pass

    status = _git("status", "--short")
    diffstat = _git("diff", "--stat")
    if not status.strip() and not diffstat.strip():
        return 0  # 工作区干净，没什么可存的

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    body = (
        "# 压缩前工作态快照（%s）\n\n"
        "## git status --short\n\n%s\n"
        "## git diff --stat\n\n%s\n"
        % (stamp, status, diffstat)
    )
    try:
        SNAP.parent.mkdir(parents=True, exist_ok=True)
        SNAP.write_text(body, encoding="utf-8")
    except Exception:  # noqa: BLE001
        return 0  # 存不下就别提示了，免得指向一个不存在的文件

    # 文案是第三人称事实陈述，不写「你去读」：祈使句会触发模型的 prompt-injection 防御。
    # 文案里带快照时间是有意的：去重按**文本**做，带上时间就意味着「同一份快照只提一次、
    # 新一次压缩会重新提」——每次压缩都是一个新的上下文窗口，上一次注入的那句已经不在了。
    ctx = (
        "上下文即将压缩。压缩前的工作态（改了哪些文件、各改了多少行）已存到 %s（%s）。"
        "压缩后关于此前改动的问题，这个文件里有原始答案，比凭印象复述准。" % (SNAP_REL, stamp)
    )
    sid = str(payload.get("session_id") or os.environ.get("CLAUDE_SESSION_ID") or "unknown")
    sid = re.sub(r"[^\w.\-]", "_", sid)[:100] or "unknown"
    if not should_emit(sid, ctx, "precompact"):
        return 0
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
