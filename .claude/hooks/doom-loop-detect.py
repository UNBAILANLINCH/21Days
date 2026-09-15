#!/usr/bin/env python3
"""PostToolUse 死循环预警钩子。

追踪**连续**编辑同一个文件的次数：中间只要编辑过别的文件，这个文件就从头数起。
非文档文件连续第 5 次首警，之后每 +3 次再警（5、8、11…）；`.md` 放宽到连续 8 次。

## 为什么是「连续」而不是「累计」

累计判据实测三连误报：`GameLifetimeScope.cs` 跨 4 个开发波次累计编辑 14 次
（每加一个模块都要回来注册一次，完全正常）、`architecture.md` 累计 8 次
（每波同步契约）、`README.md` 5 次。这些都是**回来做一件新事**，不是在猜。

真正的死循环长得不一样：连着改同一个文件五次、中间碰都没碰别的东西——
那是在试错，而试错的代价是上下文被填满，判断力先于问题耗尽。
连续性判据抓得到后者，放得过前者。

`.md` 的阈值更宽，因为文档就是一边做一边补的：写一段、回头补一段、再补一段，
连续多次编辑是它正常的工作形态，不是症状。

**非阻断**：exit 2 只是把提醒写进 stderr 反馈给 Agent（Claude Code 的约定：
PostToolUse 的 exit 2 = 提醒 Agent，不撤销已完成的编辑）。其余一律 exit 0。
同一条提醒同会话只说一次（`_hook_common.should_emit`）。
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

sys.path.insert(0, str(Path(__file__).resolve().parent))
try:
    from _hook_common import should_emit
except Exception:  # noqa: BLE001  去重件缺失时宁可多说一遍，也不能把提醒整个吞掉
    def should_emit(session_id: str, text: str, tag: str = "") -> bool:  # type: ignore[misc]
        return True

FIRST = 5       # 连续第几次首警
EVERY = 3       # 之后每 +N 次再警
DOC_FIRST = 8   # 文档（.md 等）的首警阈值：文档是边做边补的，连续多次编辑是正常形态
#: 连续状态存在计数文件的这个键下。路径不可能长这样，撞不上。
#: stop-check.py 读同一个文件时按 `isinstance(v, int)` 过滤，天然会跳过它。
STREAK_KEY = "__streak__"
DOC_SUFFIXES = (".md", ".markdown", ".txt")


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


# ── 判据本体：两个纯函数，可单独测试（.claude/hooks/tests/）──────────────

def is_doc(rel: str) -> bool:
    return str(rel).lower().endswith(DOC_SUFFIXES)


def first_threshold(rel: str) -> int:
    return DOC_FIRST if is_doc(rel) else FIRST


def bump(state: dict, key: str) -> tuple[dict, int]:
    """并入一次编辑，返回（新状态，该文件当前的**连续**编辑次数）。

    同时维护两份数：
      · `state[相对路径]` 累计次数 —— 给 stop-check.py 的收尾统计用，语义不变；
      · `state[STREAK_KEY]` 连续状态 —— 换文件即清零，本钩子的判据只看它。
    """
    st = dict(state) if isinstance(state, dict) else {}
    prev = st.get(key)
    st[key] = (int(prev) if isinstance(prev, int) else 0) + 1

    cur = st.get(STREAK_KEY)
    if isinstance(cur, dict) and str(cur.get("file") or "") == key:
        n = int(cur.get("n", 0) or 0) + 1
    else:
        n = 1  # 换了文件：上一串到此为止，从 1 重新数
    st[STREAK_KEY] = {"file": key, "n": n}
    return st, n


def should_warn(rel: str, n: int) -> bool:
    """连续第 n 次编辑 rel 该不该报：首警阈值一次，之后每 EVERY 次一次。"""
    first = first_threshold(rel)
    return n == first or (n > first and (n - first) % EVERY == 0)


def warn_text(rel: str, n: int) -> str:
    """提醒文案：第三人称事实陈述 + 可选的下一步，不写祈使句。

    祈使会触发模型的 prompt-injection 防御（官方文档明示），把这段话当可疑内容
    surface 给用户而不是当信息看；陈述句才会被当上下文吸收。
    """
    name = str(rel).rsplit("/", 1)[-1]
    return (
        "[doom-loop] 本会话已连续 %d 次编辑 %s，中间没有改过别的文件。\n"
        "  连续改同一个文件通常是在猜而不是在改：上下文被试错填满之后，判断力先于问题耗尽。\n"
        "  常见的出路：写下现象与已排除的原因 / 重读 .claude/rules/ 与模块 guide /\n"
        "  换一条修复路径 / 把已知信息交回用户。" % (n, name)
    )


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

    sid = _session_id(payload)
    fp = counts_file(sid)
    counts, n = bump(_load(fp), key)
    _save(fp, counts)

    if should_warn(key, n):
        text = warn_text(key, n)
        # 去重按文本做：同一轮打转里 5 / 8 / 11 次的文案各不相同，升级提醒照样发得出去；
        # 被吃掉的只有「同一个文件、同一个次数」的原样重复——那条说第二遍也不会有新信息。
        if should_emit(sid, text, "doom-loop"):
            print(text, file=sys.stderr)
            return 2  # 提醒 Agent，不撤销编辑
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        sys.exit(0)  # fail-open
