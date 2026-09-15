#!/usr/bin/env python3
"""Stop 收尾自检钩子。

任务结束前扫一遍工作区，把「等会儿一定会后悔」的三类遗留物说出来：

    (a) 改动 / 新增的 `.cs` 里残留调试痕迹：Debug.Break() · // TEMP · // HACK
    (b) `Assets/` 下新增的 .cs/.asset/.prefab/.unity/.asmdef 没有同名 .meta
        —— 缺 .meta 就提交，别人（或换台机器的自己）打开工程会 GUID 变动、引用断裂
    (c) 本会话编辑次数 ≥ 8 的文件，列为「疑未收敛」

**永远 exit 0，不阻断停止**：收尾提醒只是提醒，拦住 Stop 会让人没法结束对话。
没发现问题就一个字都不输出（成功静默）。
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
CACHE_DIR = ROOT / ".claude" / ".cache"

DEBUG_MARKERS = ("Debug.Break()", "// TEMP", "// HACK")
# Unity 会给这些资产生成 .meta；缺了就是还没回编辑器刷新过
META_EXTS = (".cs", ".asset", ".prefab", ".unity", ".asmdef")
HOT_THRESHOLD = 8


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


def _git(*args: str) -> str:
    """跑一条 git，失败就当没输出（不装 git / 不是仓库 / 超时都可能）。

    `core.quotepath=false` 让中文路径原样出来，不被转义成 \\344\\270\\255。
    """
    try:
        proc = subprocess.run(
            ["git", "-C", str(ROOT), "-c", "core.quotepath=false", *args],
            capture_output=True, timeout=10,
        )
    except Exception:  # noqa: BLE001
        return ""
    return proc.stdout.decode("utf-8", "replace")


def _changed() -> list[str]:
    """已跟踪文件里改过的（工作区 + 暂存区）。

    暂存区也算：本工程的约定是「改动攒在工作区、收敛后统一审」，
    审到一半 git add 过的文件同样该查残留调试痕迹。
    """
    names = _git("diff", "--name-only").splitlines()
    names += _git("diff", "--cached", "--name-only").splitlines()
    return [n.strip() for n in names if n.strip()]


def _added() -> list[str]:
    """未跟踪的新增文件（尊重 .gitignore）。"""
    out = _git("ls-files", "--others", "--exclude-standard")
    return [n.strip() for n in out.splitlines() if n.strip()]


def _hot_files(sid: str) -> list[str]:
    fp = CACHE_DIR / ("edit-counts-%s.json" % sid)
    try:
        counts = json.loads(fp.read_text(encoding="utf-8"))
    except Exception:  # noqa: BLE001
        return []
    if not isinstance(counts, dict):
        return []
    hot = [(k, v) for k, v in counts.items() if isinstance(v, int) and v >= HOT_THRESHOLD]
    hot.sort(key=lambda kv: -kv[1])
    return ["%s（%d 次）" % (k, v) for k, v in hot]


def main() -> int:
    _utf8_stdio()
    payload = _payload()
    notes: list[str] = []

    changed = _changed()
    added = _added()

    # (a) 调试痕迹：改动的和新增的 .cs 都查（新写的文件里留 // HACK 一样要提）
    seen: set[str] = set()
    for name in changed + added:
        if not name.lower().endswith(".cs") or name in seen:
            continue
        seen.add(name)
        fp = ROOT / name
        try:
            text = fp.read_text(encoding="utf-8", errors="replace")
        except Exception:  # noqa: BLE001
            continue
        hits = [m for m in DEBUG_MARKERS if m in text]
        if hits:
            notes.append("  %s: 残留调试痕迹 %s" % (name, " / ".join(hits)))

    # (b) 新增资产缺 .meta
    no_meta = []
    for name in added:
        low = name.lower()
        if not low.startswith("assets/") or not low.endswith(META_EXTS):
            continue
        if not (ROOT / (name + ".meta")).exists():
            no_meta.append(name)
    if no_meta:
        notes.append("  新增资产还没有 .meta（%d 个）：" % len(no_meta))
        notes += ["    · " + n for n in no_meta]
        notes.append("    切回 Unity 让它刷新生成 .meta 再提交，否则别人打开工程 GUID 会变、引用断裂。")

    # (c) 疑未收敛
    hot = _hot_files(_session_id(payload))
    if hot:
        notes.append("  高频编辑文件（疑未收敛）：" + "、".join(hot))

    if notes:
        print("[stop-check] 收尾提醒：\n" + "\n".join(notes))
    return 0  # 永不阻断停止


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        sys.exit(0)  # fail-open
