#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""telemetry/analyze.py —— 埋点日志的机器聚合层（`/analyze-telemetry` 的算力部分）。

## 为什么要这一层

原始日志动辄几万行、几百 MB（本机 `Editor.log` 实测 600 MB+）。整份灌进模型上下文
既贵、又会把线索冲淡 —— 真正有用的是「错误前面那 20 条事件」，不是另外 4 万条噪音。
所以分工是：**脚本负责算，模型负责推理**。这里只出压缩过的结构化摘要（默认 200 行以内），
模型看完摘要提假设，再用 `query` 子命令定向回读原始片段去验证或证伪。

契约是 `docs/telemetry.md` 第 1 节：日志行格式与字段含义以那一份为准，
**解析正则全脚本只有 `LINE_RE` 一条**（从那份文档抄下来的），不另造第二条。

## 子命令

    sources                 有哪些日志可分析（含指针文件状态、各候选源的身份校验）
    summarize（默认）        出摘要：会话清单 / 错误聚类 / 错误前序 / 成功失败对照 /
                            状态流转移矩阵 / 慢操作排行 / 性能尖峰
    query                   定向回读原始行（按会话 + 序号窗口 / 模块 / 事件 / 正则）
    --selftest              内置样例日志自检，没有 Unity 也能验证解析是对的

## 载体锚定（`.claude/rules/harness-authoring.md` 要求）

- **执行载体**：`/analyze-telemetry` —— 人主动敲的命令。**不自己跑**：没有钩子、没有定时任务、
  不生成任何需要人维护的清单或快照。
- **状态锚点**：`python .claude/skills/telemetry/analyze.py --selftest`，内置样例日志 + 四十多条断言，
  几秒出结论。没有 Unity、没有日志也能验证这一层是不是还活着。
- **退场条件**：日志格式改了而 selftest 跑不过、或者连着几次分析都卡在「没有埋点数据」——
  后者说明该补的是埋点（`/instrument-module`），不是分析器。真到没人用了就**删掉这一层**，别加提醒。

## 几条刻意的取舍

- **日志定位分四级**：`--source auto`（默认）按 **镜像目录 → 指针文件 → 显式路径 → 猜默认路径**
  走（`docs/telemetry.md`「分析脚本的定位顺序」一节）。编辑器下运行时会把埋点额外镜像一份到
  `Logs/telemetry/<sid>.log`，一段会话一个文件——`Editor.log` 是本机全局的，别的 Unity 工程
  每帧刷日志能把本工程刚写的埋点整段挤出去（2026-09-16 实测三次，连整份文件都被换掉），
  镜像才是本工程独占、干净、可靠的那一份。**只有猜出来的路径做身份硬校验**——第一条
  `session_start` 的 `p.prod` 跟本工程 `productName` 对不上就报错退出（exit 1）；
  用户显式给的路径只警告不中断；镜像目录与指针来源不必校验，它们本来就是本工程写的。
- **镜像目录默认吃整个目录**：多段会话一起分析，不只挑最新那个文件。「成功会话 vs 失败会话
  序列 diff」「同一个错误跨几段会话出现」这类跨会话分析正是靠多段会话才成立的——只吃一个
  文件就把镜像目录相对单份 `Editor.log` 的主要价值扔了。实现上每个文件**单独解析**再按会话
  先后归并（原生报错的堆栈收集绝不跨文件边界），`--last N` 的语义始终是「最近 N **段会话**」，
  不是「最近 N 个文件」；一个文件里有几段、缺不缺 `session_start`，照常规切段逻辑处理。
- **跨文件排序优先用 `session_start` 的 `at`**（墓钟 ISO8601），缺失才回退到文件 mtime。
  `at` 是实证：拷贝、同步、备份、`git checkout` 都改不动它；mtime 是这些操作的第一个受害者。
  两种键都是 epoch 秒，能直接比大小，所以「有的会话有 at、有的没有」不用特判也排得对。
  用到 mtime 兜底时摘要头部会明说「这部分先后不保证可靠」——排序不可靠而不说，比排错更坏。
- **只读日志尾部，落空自动扩大重扫**：只对**单文件大日志**（`Editor.log` / `Player.log`）生效，
  镜像文件一份几 KB 到几 MB，一律整读，`--tail-mb` 对它不起作用。默认 `--tail-mb 64`。6 GB 的 `Editor-prev.log` 全量扫一遍
  要一分多钟，而要查的几乎总是最近几次运行，所以先扫尾部——但尾窗扫空过真事：埋点写完后
  被别的进程（比如 batch 出包）续写了几百 MB 日志，把埋点整段挤出窗口。`summarize` 扫到 0 条
  埋点（或扫不到 `core/session_start`）时不会就地下「没有埋点」的结论，而是按 ×4 自动扩大
  重扫（64→256→…→全量），stderr 打进度提示，摘要头部注明实际扫了多大范围、是否回退过。
  `sources` 为了保持秒级不做这个重扫，扫到 0 条时只在提示里标「未扫全量」。
- **绝不跨会话找因果**：按 `core/session_start` 切段、按 `sid` 区分。文件开头那段没有
  `session_start` 的单独标成「截断的会话」，不丢弃也不并进后面那次运行。
- **非埋点行也抓**：Unity 原生异常 / 编译错误 / Shader 错误即使没走埋点 bridge
  （bridge 还没初始化、或编译期就报了）也归进错误聚类，标来源「原生」。
  最早期的错误恰恰发生在埋点起来之前。
- **fail-soft**：畸形行跳过并计数，末尾报「N 行无法解析」，不静默吞掉、也不因为一行坏数据崩掉。
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import tempfile
import time
from collections import Counter, deque
from datetime import datetime
from difflib import unified_diff
from pathlib import Path

# 工程根：本文件在 <root>/.claude/skills/telemetry/ 下，往上三层。不写死绝对路径。
ROOT = Path(__file__).resolve().parents[3]

# ══════════════════════════════════════════════════════════════════════════
# 契约（docs/telemetry.md 第 1 节）
# ══════════════════════════════════════════════════════════════════════════

#: 唯一解析入口。改这一条之前先改 docs/telemetry.md，两边必须一致。
LINE_RE = re.compile(r"^\[Game\]\[T\] ([DIWE]) ([a-z0-9_.]+)/([a-z0-9_]+) \| (\{.*\})$")

#: 快速预筛：不含这个前缀的行连正则都不用跑（几十万行时省下的是秒级）。
MARK = "[Game][T]"

#: 堆栈在日志行里被压成一行，换行符替换成这个。
ST_SEP = " ⏎ "

# ══════════════════════════════════════════════════════════════════════════
# 原生日志里的报错（没经过埋点 bridge 的那些）
# ══════════════════════════════════════════════════════════════════════════
#
# ⚠ 已知限制：Editor.log / Player.log 里**不带级别标记**，
#   普通 `Debug.LogError("字符串")` 和 `Debug.Log` 长得一模一样，纯文本分不出来。
#   所以这里只认结构上认得出的四类。剩下的要靠 `core.log/unity_error` 埋点 ——
#   这也正是那条埋点存在的理由。
NATIVE_PATTERNS = [
    ("异常", re.compile(
        r"^\s*(?:Unhandled\s+)?[A-Za-z_][\w.<>`+]*Exception\s*:\s*\S")),
    ("编译错误", re.compile(r"^.{0,300}?\(\d+,\d+\):\s*error\s+CS\d+\s*:")),
    ("Shader 错误", re.compile(r"^Shader error in\s")),
    ("断言失败", re.compile(r"^Assertion fail(?:ed|ure)\b")),
    ("致命错误", re.compile(r"^(?:Fatal error!|Crash!!!|Unity has crashed)")),
]

#: 堆栈帧：`Game.Player:Update () (at Assets/..:12)` / `  at Game.Player.Move () [0x0]` 两种都认。
FRAME_RE = re.compile(r"^\s*(?:at\s+)?[\w.<>`+/\[\],]+[.:][\w<>`]+\s*\(")

#: Unity 在堆栈后面补的 `(Filename: xxx Line: 42)`，不是帧，跳过但不终止收集。
FILENAME_TAIL_RE = re.compile(r"^\(Filename:.*Line:\s*\d+\)\s*$")

# ══════════════════════════════════════════════════════════════════════════
# 默认值
# ══════════════════════════════════════════════════════════════════════════

DEFAULT_TAIL_MB = 64      # 只读尾部多少 MB；0 = 全量
DEFAULT_LAST = 3          # 只看最近几段会话
DEFAULT_PRE = 20          # 错误前序取前几条事件
DEFAULT_TOP = 8           # 各类排行取前几名
DEFAULT_MAX_LINES = 200   # 摘要行数上限
CTX = 3                   # 慢操作 / 尖峰 前后各几条事件
MAX_SEQ = 3000            # 每段会话最多记多少条压缩序列（超出截断，防内存爆）
MAX_BEFORE = 8            # 每个错误类最多拿几次前序算公共子序列
SPARK = "▁▂▃▄▅▆▇█"


class LogError(Exception):
    """人话级别的失败：消息里要直接写「怎么办」，main 捕获后打 stderr 并退非 0。"""


# ══════════════════════════════════════════════════════════════════════════
# 小工具
# ══════════════════════════════════════════════════════════════════════════

def _utf8_stdio() -> None:
    """Windows 下 stdout/stderr 默认走 cp936，中文摘要会乱码。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def human_size(n: int) -> str:
    step = float(n)
    for unit in ("B", "KB", "MB", "GB"):
        if step < 1024 or unit == "GB":
            return ("%.0f %s" % (step, unit)) if unit == "B" else ("%.1f %s" % (step, unit))
        step /= 1024
    return "%d B" % n


def fmt_ms(ms) -> str:
    try:
        v = float(ms)
    except (TypeError, ValueError):
        return "?"
    if v < 1000:
        return "%dms" % int(v)
    if v < 60000:
        return "%.1fs" % (v / 1000.0)
    return "%dm%02ds" % (int(v // 60000), int(v % 60000 // 1000))


def fmt_time(path: Path) -> str:
    try:
        return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(path.stat().st_mtime))
    except OSError:
        return "?"


def sparkline(values, width: int = 32) -> str:
    """文本折线。数据点多于 width 时按桶取均值下采样 —— 摘要里不画图，一行说清趋势。"""
    vals = [v for v in values if isinstance(v, (int, float))]
    if not vals:
        return ""
    if len(vals) > width:
        bucket = len(vals) / float(width)
        vals = [
            sum(vals[int(i * bucket):max(int((i + 1) * bucket), int(i * bucket) + 1)]) /
            float(max(len(vals[int(i * bucket):max(int((i + 1) * bucket), int(i * bucket) + 1)]), 1))
            for i in range(width)
        ]
    lo, hi = min(vals), max(vals)
    if hi - lo < 1e-9:
        return SPARK[0] * len(vals)
    return "".join(SPARK[min(int((v - lo) / (hi - lo) * (len(SPARK) - 1)), len(SPARK) - 1)] for v in vals)


def clip(text: str, width: int) -> str:
    s = " ".join(str(text).split())
    return s if len(s) <= width else s[:width - 1] + "…"


def _p_brief(p, skip=(), limit: int = 3, width: int = 56) -> str:
    """把 `p` 压成 `key=a id=3` 这种一小段，摘要里放得下。"""
    if not isinstance(p, dict):
        return ""
    parts = []
    for k, v in p.items():
        if k in skip:
            continue
        parts.append("%s=%s" % (k, clip(v, 28)))
        if len(parts) >= limit:
            break
    return clip(" ".join(parts), width)


def _num(v):
    return v if isinstance(v, (int, float)) and not isinstance(v, bool) else None


# ══════════════════════════════════════════════════════════════════════════
# 日志源定位
# ══════════════════════════════════════════════════════════════════════════

def project_names() -> tuple:
    """从 `ProjectSettings/ProjectSettings.asset` 抓 companyName / productName。

    那是 YAML，但只要这两行，正则够用 —— 标准库没有 yaml，为两行装个依赖不值。
    """
    company, product = "DefaultCompany", "project1"
    path = ROOT / "ProjectSettings" / "ProjectSettings.asset"
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return company, product
    m = re.search(r"^\s*companyName:\s*(.+?)\s*$", text, re.MULTILINE)
    if m:
        company = m.group(1)
    m = re.search(r"^\s*productName:\s*(.+?)\s*$", text, re.MULTILINE)
    if m:
        product = m.group(1)
    return company, product


def candidate_sources() -> list:
    """[(标签, 路径, 说明)]，不判断存在性 —— 不存在时也要把路径打给人看。"""
    home = Path.home()
    if os.name == "nt":
        editor_dir = Path(os.environ.get("LOCALAPPDATA") or (home / "AppData" / "Local")) / "Unity" / "Editor"
        lowlow = Path(os.environ.get("USERPROFILE") or home) / "AppData" / "LocalLow"
    elif sys.platform == "darwin":
        editor_dir = home / "Library" / "Logs" / "Unity"
        lowlow = home / "Library" / "Logs"
    else:
        editor_dir = home / ".config" / "unity3d"
        lowlow = home / ".config" / "unity3d"
    company, product = project_names()
    player_dir = lowlow / company / product
    return [
        ("editor", editor_dir / "Editor.log", "编辑器（本机全局，可能是别的工程写的）"),
        ("editor-prev", editor_dir / "Editor-prev.log", "编辑器上一次启动"),
        ("player", player_dir / "Player.log", "Windows 包（%s / %s）" % (company, product)),
        ("player-prev", player_dir / "Player-prev.log", "Windows 包上一次运行"),
    ]


def resolve_source(spec: str) -> Path:
    """`editor` / `player` / `editor-prev` / `player-prev` / 任意路径 → 具体文件。

    给的是别名而主文件不在时，自动退到 `-prev`；都不在就抛人话错误。
    """
    spec = (spec or "editor").strip()
    table = {label: (path, desc) for label, path, desc in candidate_sources()}
    if spec not in table:
        path = Path(spec).expanduser()
        if path.is_file():
            return path
        raise LogError(
            "找不到日志文件：%s\n"
            "  · 路径写错了？`--source` 也可以直接填别名 editor / editor-prev / player / player-prev\n"
            "  · 先跑 `analyze.py sources` 看这台机器上有哪些日志可用" % path
        )
    path, desc = table[spec]
    if path.is_file():
        return path
    fallback = spec + "-prev"
    if not spec.endswith("-prev") and fallback in table and table[fallback][0].is_file():
        return table[fallback][0]
    if spec.startswith("player"):
        raise LogError(
            "没有播放器日志：%s\n"
            "  Player.log 要**出过包并跑过一次**才会生成（`/build Windows` 然后启动 exe）。\n"
            "  · 只想看编辑器里的运行：`--source editor`\n"
            "  · 已经出过包但路径对不上：确认 ProjectSettings 里的 Company / Product 名字"
            "（当前读到 %s / %s），或直接 `--source <Player.log 的完整路径>`" % ((path,) + project_names())
        )
    raise LogError(
        "没有编辑器日志：%s\n"
        "  Unity 编辑器至少启动过一次才会有这个文件。\n"
        "  · 换个源：`--source player`，或 `--source <日志完整路径>`\n"
        "  · 先跑 `analyze.py sources` 看有哪些" % path
    )


# ── 指针文件：运行时写的「本工程日志在哪」（docs/telemetry.md「日志到底在哪」一节，契约） ──
#
# `Editor.log` 是本机全局的，猜路径可能读到别的工程写的那份（实测发生过）。所以
# `TelemetryService` 初始化时把 `Application.consoleLogPath`（当前实例真正在写的日志）
# 写进 `Logs/telemetry-source.txt`：四行纯文本，依次是日志绝对路径 / productName / sid /
# 写入时刻 ISO8601。这**不是第二条日志通道**，里面没有埋点数据。写入端是 C# 那侧的事，
# 这里只读；文件可能不存在（还没跑过游戏）、可能只写了一半、指向的日志可能已被删——
# 每种都要 fail-soft 退化到下一条定位路径，不能崩。

#: 指针文件的四行，顺序固定。
POINTER_FIELDS = ("log_path", "prod", "sid", "written_at")
POINTER_PATH = ROOT / "Logs" / "telemetry-source.txt"

#: 定位路径的四档来源，`sources` / `summarize` 摘要头部用它显示。
ORIGIN_LABEL = {"mirror": "编辑器镜像目录", "pointer": "指针文件",
                "explicit": "显式路径", "guess": "猜测默认路径"}


def read_pointer_file(path: Path) -> dict:
    """纯函数：读指定路径的指针文件 → dict（四个字段 + `exists`）。

    不存在 / 读不了 / 内容只有一两行都不抛异常，缺的字段给空字符串——调用方靠字段是否为空
    判断「能不能用」，不靠异常。接受任意 `path`，selftest 用临时文件测，不碰真实 `Logs/`。
    """
    info = {k: "" for k in POINTER_FIELDS}
    info["exists"] = path.is_file()
    if not info["exists"]:
        return info
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return info
    lines = text.splitlines()
    for i, key in enumerate(POINTER_FIELDS):
        if i < len(lines) and lines[i].strip():
            info[key] = lines[i].strip()
    return info


def read_pointer() -> dict:
    """本工程真实的指针文件：`Logs/telemetry-source.txt`。"""
    return read_pointer_file(POINTER_PATH)


def pointer_target(info: dict):
    """指针 info → 可用的日志 `Path`，或 `None`（没写路径 / 指向的文件已不在）。

    只看 `log_path` 这一行：`sid` / `written_at` 缺失不影响定位，只是摘要里少显示两个字段。
    """
    log_path = info.get("log_path")
    if not log_path:
        return None
    p = Path(log_path).expanduser()
    return p if p.is_file() else None


def pointer_fallback_reason(info: dict) -> str:
    """指针不可用时，人话说清为什么——`sources` / `summarize` 的退化提示用。纯函数。"""
    if not info.get("exists"):
        return "指针文件不存在（%s，还没在 Unity 里跑过游戏）" % POINTER_PATH
    if not info.get("log_path"):
        return "指针文件内容不全（%s 缺日志路径那一行，可能写了一半）" % POINTER_PATH
    return "指针指向的日志已不在：%s" % info.get("log_path")


def _parse_iso(iso):
    """ISO8601 → `datetime`；空 / 不是字符串 / 格式不对都给 `None`，不抛异常。

    指针文件的 `written_at` 与 `session_start` 的 `at` 都走这一条，别再写第二份解析。
    """
    if not iso or not isinstance(iso, str):
        return None
    try:
        s = iso.strip()
        if s.endswith("Z"):
            s = s[:-1] + "+00:00"
        return datetime.fromisoformat(s)
    except (ValueError, TypeError):
        return None


def _iso_epoch(iso):
    """ISO8601 → epoch 秒（float），解析不了给 `None`。**跨文件排会话就靠它**。

    带时区和不带时区的都收：`datetime.timestamp()` 对 naive 值按本机时区算，
    与文件 mtime 同一把尺子，所以「有 at 的用 at、没有的用 mtime」两种键可以直接比大小。
    """
    dt = _parse_iso(iso)
    if dt is None:
        return None
    try:
        return dt.timestamp()
    except (OverflowError, OSError, ValueError):
        return None


def _ago(iso: str) -> str:
    """ISO8601 → 「N 分钟前」这种人话；解析失败（格式不对 / 空）给空串，不抛异常。"""
    dt = _parse_iso(iso)
    if dt is None:
        return ""
    now = datetime.now(dt.tzinfo) if dt.tzinfo else datetime.now()
    delta = (now - dt).total_seconds()
    if delta < 0:
        return "刚刚（或系统时间有偏差）"
    if delta < 60:
        return "%d 秒前" % delta
    if delta < 3600:
        return "%d 分钟前" % (delta // 60)
    if delta < 86400:
        return "%d 小时前" % (delta // 3600)
    return "%d 天前" % (delta // 86400)


def find_first_session_meta(path: Path, tail_mb: int):
    """在 `--tail-mb` 窗口内找第一条 `core/session_start`，返回它的 `p`（dict）；没找到给 `None`。

    只为工程身份校验用：不做完整解析，扫到第一条命中就提前返回，比 `analyze()` 轻得多。
    """
    try:
        size = path.stat().st_size
    except OSError:
        return None
    tail_bytes = max(0, int(tail_mb)) * 1024 * 1024
    offset = size - tail_bytes if (tail_bytes and size > tail_bytes) else 0
    try:
        with path.open("rb") as f:
            if offset:
                f.seek(offset)
                f.readline()  # 丢掉切在半路的那一行，和 open_log 一致
            for raw in f:
                text = raw.decode("utf-8", "replace").rstrip("\r\n").lstrip("﻿")
                if MARK not in text:
                    continue
                status, ev = parse_line(text)
                if status == "event" and ev.mod == "core" and ev.ev == "session_start":
                    return ev.p
    except OSError:
        return None
    return None


def check_identity(path: Path, tail_mb: int, origin: str) -> str:
    """工程身份校验：`origin == "guess"`（猜出来的路径）硬校验，`prod` 对不上就抛 `LogError`
    （调用方 `main` 捕获后退出 1）；`origin == "explicit"`（用户显式给的路径）只打印警告、
    不中断——他可能就是想看别的工程。`origin == "pointer"` 不该调用这个函数：指针来源
    不必校验，它本来就是本工程写的。

    日志里一条 `session_start` 都没有、或有但没有 `prod` 字段（埋点上线前的老日志），
    都当作「校验不了」放过，不算失败。
    """
    _, product = project_names()
    p = find_first_session_meta(path, tail_mb)
    if not p:
        return "工程身份: 未校验（这段范围里没有 core/session_start，可能是老日志或还没跑过）"
    prod = p.get("prod")
    if not isinstance(prod, str) or not prod:
        return "工程身份: 未校验（session_start 没有 prod 字段，可能是埋点上线前的老日志）"
    if prod == product:
        return "工程身份: 匹配（prod=%s）" % prod
    detail = (
        "这份日志是 %s 写的，不是本工程 %s 的。\n"
        "  · 在 Unity 里跑一次游戏生成指针文件（Logs/telemetry-source.txt），再用默认的 --source auto；\n"
        "  · 或者确实要看这份别的工程的日志，加 --source <完整路径> 显式指定。" % (prod, product)
    )
    if origin == "guess":
        raise LogError("工程身份校验未通过：%s" % detail)
    print("[analyze] ⚠ 工程身份校验未通过（显式指定的路径，仅警告不中断）：%s" % detail, file=sys.stderr)
    return "工程身份: ⚠ 不匹配（prod=%s，本工程=%s，仅警告——是你显式指定的路径）" % (prod, product)


# ── 编辑器镜像目录：`Logs/telemetry/<sid>.log`（docs/telemetry.md「编辑器镜像」一节，契约）──
#
# 指针文件解决「日志在哪」，解决不了「编辑器下 Editor.log 根本不是本工程独占」。所以编辑器下
# 运行时把同一次格式化的结果额外送一份到工程内，**一段会话一个文件**。分析侧因此拿到的是
# 「已经切好段、别的工程插不进来」的输入——这是定位顺序里它排第一的唯一理由。
#
# 目录里除了 `<sid>.log` 还会有 `/analyze-telemetry` 落的报告 `<时间戳>.md`，所以**只认 .log**。

MIRROR_DIR = ROOT / "Logs" / "telemetry"

#: 镜像文件一份几 KB 到几 MB，整读就行；尾窗那套是给几百 MB 的单文件大日志用的。
MIRROR_TAIL_MB = 0


def mirror_files(mirror_dir: Path = None) -> list:
    """镜像目录里的 `*.log`，按最后写入时刻**升序**（最老在前）。

    目录不存在 / 不是目录 / 没有 `.log` 都给 `[]`，不抛异常——调用方靠空列表退到下一档。
    非 `.log`（报告 `.md`、编辑器写了一半的临时文件）一律忽略；拿不到 mtime 的按 0 排，
    再按文件名兜底，保证顺序稳定可复现。`mirror_dir` 可注入，selftest 用临时目录，不碰真实 `Logs/`。
    """
    d = mirror_dir if mirror_dir is not None else MIRROR_DIR
    try:
        if not d.is_dir():
            return []
        entries = [p for p in d.iterdir() if p.is_file() and p.suffix.lower() == ".log"]
    except OSError:
        return []

    def key(p: Path):
        try:
            return (p.stat().st_mtime, p.name)
        except OSError:
            return (0.0, p.name)

    entries.sort(key=key)
    return entries


def mirror_fallback_reason(mirror_dir: Path = None) -> str:
    """镜像目录用不上时的人话理由——`sources` 与 `summarize` 的退化提示用。纯函数。"""
    d = mirror_dir if mirror_dir is not None else MIRROR_DIR
    if not d.exists():
        return "镜像目录不存在（%s，还没在编辑器里跑过游戏——镜像只在编辑器下写）" % d
    if not d.is_dir():
        return "镜像目录的位置被一个文件占了：%s" % d
    return "镜像目录里没有 .log（%s，还没在编辑器里跑过游戏，或会话文件被清理了）" % d


class SourceSet:
    """一次分析的输入：**一个或多个**日志文件。单文件就是 `n == 1` 的特例。

    整条链路（扫描 → 解析 → 聚合 → 渲染 → query）以前都围绕单个 `Path` 写，
    引入镜像目录后统一收口到这里，免得每个子命令各写一套「是目录还是文件」的分支。
    """

    __slots__ = ("paths", "kind", "root")

    def __init__(self, paths, kind: str = "file", root: Path = None):
        self.paths = list(paths)
        self.kind = kind                  # "file" / "mirror"
        self.root = root                  # 镜像目录；单文件时 None

    def __len__(self) -> int:
        return len(self.paths)

    @property
    def primary(self) -> Path:
        """单文件路径。镜像模式下给最新的那份，只用于「必须给一个路径」的老接口。"""
        return self.paths[-1]

    @property
    def total_size(self) -> int:
        total = 0
        for p in self.paths:
            try:
                total += p.stat().st_size
            except OSError:
                pass
        return total

    def label(self) -> str:
        if self.kind == "mirror":
            return "%s（%d 个文件 / 共 %s）" % (self.root, len(self.paths), human_size(self.total_size))
        return str(self.primary)


def mirror_files_for_session(src: "SourceSet", sid: str) -> list:
    """`query --session <sid>` 的取窗口顺序：**先按文件名命中，再退回全目录扫**。

    契约是一个 sid 一个 `<sid>.log`，所以文件名就是索引，O(1) 命中，不用把整个目录读一遍。
    但不假死：同一 sid 重进 Play 写进别的文件、文件被改过名、或者那段会话是从别处拷进来的，
    都会命中不到——这时返回全部文件，由调用方扫完再说没命中。
    """
    if not sid:
        return list(src.paths)
    named = [p for p in src.paths if p.stem == sid]
    rest = [p for p in src.paths if p not in named]
    return named + rest


def _mirror_note(mirror_dir: Path, files: list) -> str:
    size = sum((p.stat().st_size if p.is_file() else 0) for p in files)
    return "%s，%d 个文件 / 共 %s" % (mirror_dir, len(files), human_size(size))


def locate_source(spec: str, tail_mb: int, pointer_info: dict = None,
                  mirror_dir: Path = None) -> tuple:
    """定位顺序（`docs/telemetry.md`「分析脚本的定位顺序」一节的契约）：
    **镜像目录 → 指针文件 → 用户显式 `--source <路径>` → 猜默认路径**。

    `spec == "auto"`（`--source` 的默认值）走完整顺序；`mirror` 显式指定镜像目录；
    `editor` / `player` / `editor-prev` / `player-prev` / 显式路径这几个既有别名行为不变
    （直接走 `resolve_source`，origin 记成 `explicit`）。显式给一个**目录**也认，按镜像目录吃。

    返回 `(SourceSet, origin, note)`，`origin ∈ {"mirror", "pointer", "explicit", "guess"}`。
    `pointer_info` / `mirror_dir` 只给 selftest 用，注入假数据，不碰真实 `Logs/`。
    """
    spec = (spec or "auto").strip()
    mdir = mirror_dir if mirror_dir is not None else MIRROR_DIR

    if spec == "mirror":
        files = mirror_files(mdir)
        if not files:
            raise LogError(
                "用不了编辑器镜像目录：%s\n"
                "  镜像只在**编辑器**下写（`#if UNITY_EDITOR`），在 Unity 里跑一次游戏就有了。\n"
                "  · 看真机 / 出包的日志：`--source player`\n"
                "  · 不挑源就用默认的 `--source auto`（镜像→指针→猜路径，会自动退到能用的那档）"
                % mirror_fallback_reason(mdir))
        return SourceSet(files, "mirror", mdir), "mirror", _mirror_note(mdir, files)

    if spec != "auto":
        as_path = Path(spec).expanduser()
        if as_path.is_dir():
            files = mirror_files(as_path)
            if not files:
                raise LogError(
                    "这个目录里没有 .log 可分析：%s\n"
                    "  给目录时按「一段会话一个 <sid>.log」的镜像目录处理，非 .log 一律忽略。" % as_path)
            return SourceSet(files, "mirror", as_path), "explicit", _mirror_note(as_path, files)
        return SourceSet([resolve_source(spec)], "file"), "explicit", ""

    files = mirror_files(mdir)
    if files:
        return SourceSet(files, "mirror", mdir), "mirror", _mirror_note(mdir, files)
    mirror_reason = mirror_fallback_reason(mdir)

    info = pointer_info if pointer_info is not None else read_pointer()
    target = pointer_target(info)
    if target is not None:
        ago = _ago(info.get("written_at"))
        note = "%s（写入于 %s%s，sid=%s）" % (
            POINTER_PATH, info.get("written_at") or "?",
            ("，" + ago) if ago else "", info.get("sid") or "?")
        return SourceSet([target], "file"), "pointer", note

    reason = "%s；%s" % (mirror_reason, pointer_fallback_reason(info))
    return SourceSet([resolve_source("editor")], "file"), "guess", reason


# ══════════════════════════════════════════════════════════════════════════
# 流式读取
# ══════════════════════════════════════════════════════════════════════════

class Scan:
    """一次扫描的范围。摘要头部要打出来 —— query 才能用同样的参数回读同一段。"""

    __slots__ = ("path", "size", "offset", "partial", "lines", "kind")

    def __init__(self, path: Path, size: int, offset: int):
        self.path = path
        self.size = size
        self.offset = offset
        self.partial = offset > 0
        self.lines = 0
        self.kind = "file"

    def describe(self) -> str:
        if not self.partial:
            return "全量 %s" % human_size(self.size)
        return "尾部 %s（全文件 %s，跳过前 %s；要全量加 --tail-mb 0）" % (
            human_size(self.size - self.offset), human_size(self.size), human_size(self.offset))

    def last_write(self) -> str:
        return fmt_time(self.path)


class MultiScan:
    """一组文件的扫描范围（镜像目录）。**鸭子类型与 `Scan` 对齐**：`path` / `size` /
    `partial` / `describe()` / `last_write()` 都在，`render_summary` 因此不用区分两者。

    `partial` 恒为 False —— 镜像文件一律整读，尾窗对它没有意义（见模块文档的取舍那节）。
    `skipped` 记下读不了而被跳过的文件：一个文件坏掉不该连累其它文件，但也不能悄悄少算。
    """

    __slots__ = ("scans", "root", "size", "partial", "kind", "skipped")

    def __init__(self, scans, root: Path, skipped=None):
        self.scans = list(scans)
        self.root = root
        self.size = sum(s.size for s in self.scans)
        self.partial = False
        self.kind = "mirror"
        self.skipped = list(skipped or [])

    @property
    def path(self):
        return self.root

    def describe(self) -> str:
        tail = "，跳过读不了的 %d 个" % len(self.skipped) if self.skipped else ""
        return "镜像目录 %d 个文件 / 共 %s（每份整读，不走 --tail-mb 尾窗%s）" % (
            len(self.scans), human_size(self.size), tail)

    def last_write(self) -> str:
        """取最新那份文件的写入时刻 —— 目录自己的 mtime 只反映增删，不反映内容更新。"""
        if not self.scans:
            return fmt_time(self.root)
        return max((fmt_time(s.path) for s in self.scans), default="?")


def open_log(path: Path, tail_mb: int):
    """返回 (Scan, 行生成器)。生成器产出 (行号, 文本)。

    行号从**扫描起点**起算：`--tail-mb` 截过的时候它不是文件绝对行号。
    所以引用证据一律用 `sid + s`（会话内序号），行号只是给人肉眼定位用的。
    """
    try:
        size = path.stat().st_size
    except OSError as exc:
        raise LogError("读不到日志文件：%s\n  %s\n  文件被删了、或者这个账号没权限。" % (path, exc))
    tail_bytes = max(0, int(tail_mb)) * 1024 * 1024
    offset = size - tail_bytes if (tail_bytes and size > tail_bytes) else 0
    try:
        handle = path.open("rb")
    except PermissionError:
        raise LogError(
            "没有权限读日志：%s\n"
            "  Unity 正开着并独占写入时偶尔会这样。关掉编辑器再试，"
            "或者把文件复制一份出来 `--source <副本路径>`。" % path)
    except OSError as exc:
        raise LogError("打不开日志：%s\n  %s" % (path, exc))

    scan = Scan(path, size, offset)

    def gen():
        # Unity 正在写这个文件也没关系：只读，读到哪算哪，最后一行可能是半条（会被计成畸形行）。
        try:
            if offset:
                handle.seek(offset)
                handle.readline()  # 丢掉切在半路的那一行
            n = 0
            for raw in handle:
                n += 1
                # 编码是 UTF-8，但真机日志里混进坏字节是常事 → replace，不抛异常。
                yield n, raw.decode("utf-8", "replace").rstrip("\r\n").lstrip("﻿")
            scan.lines = n
        finally:
            handle.close()

    return scan, gen()


def quick_scan(path: Path, tail_mb: int) -> dict:
    """`sources` 用的粗扫：只数字节，不切行、不解码 —— 几百 MB 也就一两秒。"""
    info = {"sessions": 0, "tel_lines": 0, "partial": False, "scanned": 0, "error": ""}
    try:
        size = path.stat().st_size
    except OSError as exc:
        info["error"] = str(exc)
        return info
    tail_bytes = max(0, int(tail_mb)) * 1024 * 1024
    offset = size - tail_bytes if (tail_bytes and size > tail_bytes) else 0
    info["partial"] = offset > 0
    info["scanned"] = size - offset
    start = b"[Game][T] I core/session_start"
    mark = MARK.encode("ascii")
    keep = b""
    try:
        with path.open("rb") as f:
            if offset:
                f.seek(offset)
            while True:
                chunk = f.read(4 * 1024 * 1024)
                if not chunk:
                    break
                buf = keep + chunk
                info["sessions"] += buf.count(start)
                info["tel_lines"] += buf.count(mark)
                keep = buf[-len(start):]  # 跨块边界的标记不能漏
    except OSError as exc:
        info["error"] = str(exc)
    return info


# ══════════════════════════════════════════════════════════════════════════
# 解析
# ══════════════════════════════════════════════════════════════════════════

class Event:
    __slots__ = ("lv", "mod", "ev", "t", "s", "f", "p", "err", "st", "line")

    def __init__(self, lv, mod, ev, data, line):
        self.lv = lv
        self.mod = mod
        self.ev = ev
        self.t = _num(data.get("t")) or 0
        self.s = _num(data.get("s"))
        self.f = _num(data.get("f")) or 0
        self.p = data.get("p") if isinstance(data.get("p"), dict) else {}
        self.err = data.get("err") if isinstance(data.get("err"), str) else ""
        self.st = data.get("st") if isinstance(data.get("st"), str) else ""
        self.line = line

    @property
    def name(self) -> str:
        return "%s/%s" % (self.mod, self.ev)


def parse_line(text: str):
    """→ (状态, Event|None)。状态：`event` / `malformed` / `plain`。

    `malformed` 只给「看起来是埋点行但读不出来」的：JSON 截断、被别的线程插断、级别写错。
    普通 Unity 日志行是 `plain`，不算无法解析 —— 它们走原生报错那条路。
    """
    m = LINE_RE.match(text)
    if m:
        lv, mod, ev, blob = m.groups()
        try:
            data = json.loads(blob)
        except ValueError:
            return "malformed", None
        if not isinstance(data, dict):
            return "malformed", None
        return "event", Event(lv, mod, ev, data, 0)
    if MARK in text:
        return "malformed", None
    return "plain", None


def norm_frame(frame: str) -> str:
    """堆栈帧规范化：去行号、去路径、去参数、去匿名方法编号 —— 指纹才能跨次归并。

    `A:M` 与 `A.M` 统一成 `.`：`Application.logMessageReceived` 给的堆栈用冒号，
    异常自带的堆栈用点，同一个方法必须算同一帧。
    """
    s = frame.strip()
    s = re.sub(r"^at\s+", "", s)
    s = re.sub(r"\s*\(at\s[^)]*\)\s*$", "", s)      # (at Assets/X.cs:12)
    s = re.sub(r"\[0x[0-9a-fA-F]+\]", "", s)         # [0x0001a]
    s = re.sub(r"\s+in\s+.*?:\d+\s*$", "", s)        # in <file>:0
    s = re.sub(r"\(.*?\)", "()", s)                  # 参数列表
    s = re.sub(r"b__\d+(?:_\d+)*", "b__N", s)        # 匿名方法编号
    s = re.sub(r"<([\w]+)>[a-z]__[\w]+", r"<\1>__N", s)  # 闭包 / 迭代器类
    s = s.replace(":", ".")
    return re.sub(r"\s+", " ", s).strip()


def norm_msg(msg: str) -> str:
    """错误消息规范化：数字与地址归一，同一个坑的不同实例才聚得到一起。"""
    s = re.sub(r"0x[0-9a-fA-F]+", "0xN", str(msg))
    s = re.sub(r"\d+", "N", s)
    return clip(s, 160)


class ErrorHit:
    __slots__ = ("sid", "seq", "t", "line", "name", "msg", "frames", "before", "source")

    def __init__(self, sid, seq, t, line, name, msg, frames, before, source):
        self.sid = sid
        self.seq = seq
        self.t = t
        self.line = line
        self.name = name
        self.msg = msg
        self.frames = frames
        self.before = before
        self.source = source

    def fingerprint(self) -> str:
        """指纹 = 规范化消息 + 规范化后的前 3 帧。

        带上消息是为了让「埋点记下的 unity_error」和「Unity 自己写的原生报错」
        （同一次报错在日志里出现两遍）落到同一类里，而不是各算一次。
        """
        return "\n".join([norm_msg(self.msg)] + [norm_frame(f) for f in self.frames[:3]])


class Session:
    """一段会话。**事件绝不跨会话混**——这是整个分析的前提。"""

    def __init__(self, sid: str, line: int, truncated: bool = False):
        self.sid = sid
        self.line = line
        self.truncated = truncated      # 开头被日志轮转 / --tail-mb 截掉了
        self.src = None                 # 这段会话来自哪个文件（镜像目录才有；query 靠它回读）
        self.at_epoch = None            # session_start 的 at（epoch 秒）；没有 at 时为 None
        self.meta = {}
        self.first_t = None
        self.last_t = 0
        self.first_seq = None
        self.last_seq = None
        self.last_line = line
        self.n_events = 0
        self.levels = Counter()
        self.events = Counter()
        self.ended = ""                 # "" / "正常" ；空 = 没见到 session_end
        self.seq_c = []                 # 压缩事件序列 [[名字, 连续次数], ...]
        self.seq_trunc = False
        self.transitions = Counter()
        self.enters = Counter()
        self.exits = Counter()
        self.flow_failed = []
        self.samples = []               # [(fps, mem_mb)]
        self.spikes = []
        self.slow = []                  # [(ms, 详情 dict)]
        self.errors = []
        self.throttled = 0
        self.recent = deque(maxlen=max(DEFAULT_PRE, CTX) * 2)
        self._await = []                # 等后文的槽位

    # ── 观察一条事件 ──────────────────────────────────────────────
    def observe(self, ev: Event, module_filter) -> None:
        name = ev.name
        for slot in self._await:
            slot["after"].append(name)
        self._await = [s for s in self._await if len(s["after"]) < CTX]

        before = list(self.recent)
        self.n_events += 1
        self.levels[ev.lv] += 1
        self.events[name] += 1
        if self.first_t is None:
            self.first_t = ev.t
        self.last_t = max(self.last_t, ev.t)
        if ev.s is not None:
            if self.first_seq is None:
                self.first_seq = ev.s
            self.last_seq = ev.s
        self.last_line = ev.line

        if self.seq_c and self.seq_c[-1][0] == name:
            self.seq_c[-1][1] += 1
        elif len(self.seq_c) < MAX_SEQ:
            self.seq_c.append([name, 1])
        else:
            self.seq_trunc = True

        in_scope = module_filter(ev.mod)

        if ev.mod == "core":
            if ev.ev == "session_end":
                self.ended = "正常"
            elif ev.ev == "throttled":
                self.throttled += 1
        elif ev.mod == "core.flow":
            src = ev.p.get("from")
            dst = ev.p.get("to")
            if ev.ev == "state_enter":
                if dst:
                    self.enters[dst] += 1
                if src and dst:
                    self.transitions[(str(src), str(dst))] += 1
            elif ev.ev == "state_exit":
                leaving = src or ev.p.get("state") or dst
                if leaving:
                    self.exits[str(leaving)] += 1
            elif ev.ev == "state_failed":
                self.flow_failed.append((ev.s, _p_brief(ev.p)))
        elif ev.mod == "core.perf":
            if ev.ev == "sample":
                self.samples.append((_num(ev.p.get("fps")), _num(ev.p.get("mem_mb"))))
            elif ev.ev == "spike":
                slot = {"seq": ev.s, "t": ev.t, "f": ev.f, "p": _p_brief(ev.p),
                        "before": before[-CTX:], "after": []}
                self.spikes.append(slot)
                self._await.append(slot)

        if ev.lv == "E" and in_scope:
            frames = [x for x in ev.st.split(ST_SEP) if x.strip()] if ev.st else []
            self.errors.append(ErrorHit(
                self.sid, ev.s, ev.t, ev.line, name,
                ev.err or name, frames, before[-DEFAULT_PRE:], "埋点"))

        ms = _num(ev.p.get("ms"))
        if ms is not None and ms > 0 and in_scope:
            slot = {"ms": ms, "seq": ev.s, "t": ev.t, "f": ev.f, "name": name,
                    "p": _p_brief(ev.p, skip=("ms",)), "before": before[-CTX:], "after": []}
            self.slow.append((ms, slot))
            self._await.append(slot)
            if len(self.slow) > 200:      # 有界：留够排行用的量就行
                self.slow.sort(key=lambda x: -x[0])
                del self.slow[60:]

        self.recent.append(name)

    # ── 观察一条原生报错 ──────────────────────────────────────────
    def observe_native(self, text: str, kind: str, line: int) -> ErrorHit:
        hit = ErrorHit(self.sid, self.last_seq, self.last_t, line,
                       "(原生)%s" % kind, clip(text, 200), [],
                       list(self.recent)[-DEFAULT_PRE:], "原生")
        self.errors.append(hit)
        return hit

    # ── 派生 ────────────────────────────────────────────────────
    @property
    def duration(self):
        if self.first_t is None:
            return 0
        return max(0, self.last_t - self.first_t)

    @property
    def n_errors(self) -> int:
        return len(self.errors)

    @property
    def finish(self) -> str:
        if self.ended:
            return "正常"
        return "未见 session_end"

    def names(self) -> list:
        return [n for n, _ in self.seq_c]


class Analysis:
    def __init__(self, last: int, module: str):
        self.last = max(1, int(last))
        self.module = (module or "").strip().lower()
        self.sessions = []
        self.dropped = 0          # 因为 --last 被丢掉的旧会话数
        self.malformed = 0
        self.plain = 0
        self.native_hits = 0
        self.cur = None
        self._collect = None      # 正在收集堆栈的原生报错
        self.order_basis = None   # 多文件归并时记 (按 at 的段数, 按 mtime 兜底的段数)

    def module_filter(self, mod: str) -> bool:
        if not self.module:
            return True
        return mod == self.module or mod.startswith(self.module + ".")

    def _ensure(self, line: int) -> Session:
        """还没见过 session_start 就有事件 → 开头被截掉了，单独开一段「截断的会话」。"""
        if self.cur is None:
            self.cur = Session("(截断)", line, truncated=True)
            self.sessions.append(self.cur)
        return self.cur

    def _push(self, sess: Session) -> None:
        self.sessions.append(sess)
        while len(self.sessions) > self.last:
            self.sessions.pop(0)
            self.dropped += 1
        self.cur = sess

    def feed(self, line: int, text: str) -> None:
        status, ev = parse_line(text)

        if status == "event":
            ev.line = line
            self._collect = None
            if ev.mod == "core" and ev.ev == "session_start":
                sid = str(ev.p.get("sid") or "?")
                sess = Session(sid, line)
                sess.meta = dict(ev.p)
                self._push(sess)
                sess.observe(ev, self.module_filter)
                return
            self._ensure(line).observe(ev, self.module_filter)
            return

        if status == "malformed":
            self.malformed += 1
            self._collect = None
            return

        self.plain += 1

        # —— 原生报错。先判「是不是一条新的报错」再判「是不是上一条的堆栈行」：
        #    `Foo.cs(31,9): error CS0103:` 这种编译错误长得很像堆栈帧，顺序反了会被吃进上一条的栈里。
        for kind, pat in NATIVE_PATTERNS:
            if pat.match(text):
                self._collect = self._ensure(line).observe_native(text, kind, line)
                self.native_hits += 1
                return

        if self._collect is not None:
            if FILENAME_TAIL_RE.match(text):
                return
            if text.strip() and FRAME_RE.match(text) and len(self._collect.frames) < 6:
                self._collect.frames.append(text)
                return
            self._collect = None

    def finish(self) -> None:
        """收尾：最后一段会话如果没见到 session_end，就是崩了 / 还在跑 / 被尾部截断。"""
        for i, sess in enumerate(self.sessions):
            if sess.ended:
                continue
            sess.ended = ""
        if self.sessions:
            for sess in self.sessions[:-1]:
                if not sess.ended:
                    sess.ended = ""  # 后面还有别的会话却没 session_end → 崩溃或被强杀

    def all_errors(self) -> list:
        out = []
        for sess in self.sessions:
            out.extend(sess.errors)
        return out


def analyze(lines, last: int, module: str) -> Analysis:
    """核心：吃 (行号, 文本) 序列，吐结构化结果。selftest 直接喂列表，不碰文件。"""
    ana = Analysis(last, module)
    for line, text in lines:
        try:
            ana.feed(line, text)
        except Exception:  # noqa: BLE001  单行出岔子不该让整趟分析挂掉
            ana.malformed += 1
    ana.finish()
    return ana


# ══════════════════════════════════════════════════════════════════════════
# 聚类与序列比对
# ══════════════════════════════════════════════════════════════════════════

class Cluster:
    def __init__(self, key: str, hit: ErrorHit):
        self.key = key
        self.count = 0
        self.first = hit
        self.last = hit
        self.sessions = set()
        self.sources = set()
        self.befores = []
        self.sample = hit

    def add(self, hit: ErrorHit) -> None:
        self.count += 1
        self.last = hit
        self.sessions.add(hit.sid)
        self.sources.add(hit.source)
        if hit.frames and not self.sample.frames:
            self.sample = hit
        if len(self.befores) < MAX_BEFORE and hit.before:
            self.befores.append(list(hit.before))


def build_clusters(hits) -> list:
    table = {}
    for hit in hits:
        key = hit.fingerprint()
        cluster = table.get(key)
        if cluster is None:
            cluster = table[key] = Cluster(key, hit)
        cluster.add(hit)
    out = list(table.values())
    out.sort(key=lambda c: (-c.count, str(c.first.seq)))
    return out


def lcs(a, b) -> list:
    """最长公共子序列。序列长度被前序窗口卡在 20 左右，DP 的开销可以忽略。"""
    if not a or not b:
        return []
    m, n = len(a), len(b)
    dp = [[0] * (n + 1) for _ in range(m + 1)]
    for i in range(m - 1, -1, -1):
        row, nxt = dp[i], dp[i + 1]
        for j in range(n - 1, -1, -1):
            row[j] = nxt[j + 1] + 1 if a[i] == b[j] else max(nxt[j], row[j + 1])
    out = []
    i = j = 0
    while i < m and j < n:
        if a[i] == b[j]:
            out.append(a[i])
            i += 1
            j += 1
        elif dp[i + 1][j] >= dp[i][j + 1]:
            i += 1
        else:
            j += 1
    return out


def common_prefix_seq(windows) -> list:
    """多次出现的共同前序：两两求 LCS 归并。一次出现就直接返回那一条。"""
    if not windows:
        return []
    acc = list(windows[0])
    for w in windows[1:]:
        acc = lcs(acc, list(w))
        if not acc:
            break
    return acc


# ══════════════════════════════════════════════════════════════════════════
# 渲染（带行数预算）
# ══════════════════════════════════════════════════════════════════════════

class Report:
    """分节收集，最后按预算裁剪：优先级数字越大越先被砍，头尾永远留着。"""

    def __init__(self, max_lines: int):
        self.max_lines = max(40, int(max_lines))
        self.sections = []

    def add(self, title: str, lines, prio: int = 5, keep: int = 1):
        self.sections.append({"title": title, "lines": [l for l in lines if l is not None],
                              "prio": prio, "keep": keep, "cut": 0})

    def render(self) -> str:
        def total():
            return sum(len(s["lines"]) + 2 + (1 if s["cut"] else 0) for s in self.sections)

        guard = 0
        while total() > self.max_lines and guard < 10000:
            guard += 1
            pool = [s for s in self.sections if len(s["lines"]) > s["keep"]]
            if not pool:
                break
            victim = max(pool, key=lambda s: (s["prio"], len(s["lines"])))
            victim["lines"].pop()
            victim["cut"] += 1

        out = []
        for sec in self.sections:
            if sec["title"]:
                out.append("## " + sec["title"])
            out.extend(sec["lines"])
            if sec["cut"]:
                out.append("   …（本节省略 %d 行，放宽用 --max-lines）" % sec["cut"])
            out.append("")
        return "\n".join(out).rstrip() + "\n"


def render_summary(ana: Analysis, scan, args, locate: tuple = None, fallback_note: str = "") -> str:
    """`locate`：`(origin, locate_note, identity_note)`，来自 `locate_source` + `check_identity`。
    不传就不打这两行（selftest 里很多用例只关心解析逻辑，不需要凑一份 locate）。
    `fallback_note`：尾窗落空自动回退时的提示，来自 `scan_and_analyze_with_fallback`；
    非空就在头部多打一行，注明这次实际扫了多大范围、是不是回退过。
    """
    rep = Report(args.max_lines)
    sessions = ana.sessions
    clusters = build_clusters(ana.all_errors())

    # ── 头 ─────────────────────────────────────────────────────
    head = []
    if locate is not None:
        origin, locate_note, identity_note = locate
        head.append("定位路径: %s%s" % (
            ORIGIN_LABEL.get(origin, origin), ("（%s）" % locate_note) if locate_note else ""))
        if identity_note:
            head.append(identity_note)
    if scan is not None:
        head.append("源: %s" % scan.path)
        head.append("范围: %s ｜ 最后写入 %s" % (scan.describe(), scan.last_write()))
    if fallback_note:
        head.append(fallback_note)
    head.append("会话: %s ｜ 埋点事件 %d ｜ 错误 %d ｜ 原生报错 %d" % (
        ("未切出任何会话段" if not sessions else "保留最近 %d 段%s" % (
            len(sessions), ("（更早的 %d 段已略过）" % ana.dropped) if ana.dropped else "")),
        sum(s.n_events for s in sessions), sum(s.n_errors for s in sessions), ana.native_hits))
    basis_note = order_basis_note(getattr(ana, "order_basis", None))
    if basis_note:
        head.append(basis_note)
    if args.module:
        head.append("模块过滤: %s（只筛埋点错误与慢操作排行；上下文序列保留全部事件，"
                    "原生报错不受过滤——它们没有模块名）" % args.module)
    rep.add("", head, prio=0, keep=len(head))

    if not sessions or sum(s.n_events for s in sessions) == 0:
        scope = scan.describe() if scan is not None else "这段范围"
        if scan is not None and getattr(scan, "kind", "file") == "mirror":
            # 镜像目录是工程内的、整读的，「别的工程写的」「数据在更早位置」这两条都不成立，
            # 换成它自己的可能原因，别让人照着不适用的提示去排查。
            body = [
                "%s里一条 `%s` 行都没有。镜像目录是工程内独占且整读的，所以可能是：" % (scope, MARK),
                "  1. 镜像文件是空的 / 写了一半（Play 刚起就被中断）；",
                "  2. TelemetryConfig.asset 的总开关关了，或最低级别设得太高；",
                "  3. 这些是更早版本留下的文件 —— 在编辑器里再跑一次生成新的会话文件。",
            ]
        else:
            body = [
                "%s里一条 `%s` 行都没有。可能是：" % (scope, MARK),
                "  1. 这份 Editor.log 是**别的 Unity 工程**写的（Editor.log 是本机全局的，谁最后跑就是谁的）；",
                "  2. 本工程还没跑过，或 TelemetryConfig.asset 总开关是关的；",
            ]
            if scan is None or scan.partial:
                body.append("  3. 想看的那次运行在更早的位置 —— 加大 `--tail-mb`，或换 `--source editor-prev`。")
            else:
                body.append("  3. 已经是全量扫描仍未命中，可以排除「数据在更早位置」——大概率是 1/2 的原因。")
        body.append("原生报错仍会被抓（见下），拿不到序号与会话上下文。")
        rep.add("没有埋点数据", body, prio=0, keep=len(body))

    # ── 会话清单 ────────────────────────────────────────────────
    # 多文件输入（镜像目录）时多一列「文件」：证据要能回指到具体那份 <sid>.log，
    # query 才知道去哪份取窗口。单文件时不加这一列，摘要宽度不变。
    multi_file = any(getattr(s, "src", None) is not None for s in sessions)
    rows = ["| sid | 起→止(t) | 时长 | 事件 | 错误 | 结束方式 |" + (" 文件 |" if multi_file else ""),
            "| --- | --- | --- | --- | --- | --- |" + (" --- |" if multi_file else "")]
    for sess in sessions:
        finish = "正常 session_end" if sess.ended else (
            "日志截断（开头缺 session_start）" if sess.truncated else "未见 session_end（崩溃 / 仍在运行）")
        rows.append(("| %s | %s→%s | %s | %d | %d | %s |" % (
            sess.sid, fmt_ms(sess.first_t or 0), fmt_ms(sess.last_t),
            fmt_ms(sess.duration), sess.n_events, sess.n_errors, finish))
            + ((" %s |" % (sess.src.name if sess.src is not None else "?")) if multi_file else ""))
    for sess in sessions:
        if sess.meta:
            rows.append("  %s: %s" % (sess.sid, _p_brief(
                sess.meta, skip=("sid",), limit=5, width=90)))
        if sess.throttled:
            rows.append("  %s: ⚠ 触发限流 %d 次，中间有事件被丢弃（TelemetryConfig 的每秒条数上限）"
                        % (sess.sid, sess.throttled))
    rep.add("会话清单", rows, prio=1, keep=3)

    # ── 错误聚类 ────────────────────────────────────────────────
    if clusters:
        lines = []
        for i, c in enumerate(clusters[:args.top], 1):
            lines.append("[E%d] ×%d ｜ 会话 %d 段 ｜ 来源 %s" % (
                i, c.count, len(c.sessions), "+".join(sorted(c.sources))))
            lines.append("     %s" % clip(c.sample.msg, 150))
            lines.append("     首 sid=%s s=%s t=%s ｜ 末 sid=%s s=%s t=%s" % (
                c.first.sid, c.first.seq, fmt_ms(c.first.t),
                c.last.sid, c.last.seq, fmt_ms(c.last.t)))
            if c.sample.frames:
                lines.append("     栈顶 %s" % clip(" ← ".join(
                    norm_frame(f) for f in c.sample.frames[:3]), 150))
        rep.add("错误聚类（按堆栈指纹归并，前 %d 类）" % min(args.top, len(clusters)),
                lines, prio=2, keep=4)
    else:
        rep.add("错误聚类", ["这几段会话里没有错误级事件，也没抓到原生报错。"], prio=2, keep=1)

    # ── 错误前序（根因第一嫌疑人）────────────────────────────────
    if clusters:
        lines = []
        for i, c in enumerate(clusters[:min(3, args.top)], 1):
            common = common_prefix_seq(c.befores)
            lines.append("★ [E%d] %s" % (i, clip(c.sample.msg, 100)))
            if common and len(c.befores) > 1:
                lines.append("   %d 次出现的共同前序（长度 %d，取最长公共子序列）：" % (
                    len(c.befores), len(common)))
                lines.append("   " + " → ".join(common[-args.pre:]))
            elif common:
                lines.append("   只出现 1 次，下面是它之前的 %d 条事件（没有第二次可比对，"
                             "相关性无从验证，别当成规律）：" % min(args.pre, len(common)))
                lines.append("   " + " → ".join(common[-args.pre:]))
            else:
                lines.append("   前 %d 条事件没有共同子序列 —— 要么每次触发路径都不同，"
                             "要么埋点太稀（考虑 /instrument-module 补点）。" % args.pre)
        rep.add("错误前序（前 %d 条事件的共同序列 = 根因第一嫌疑人，但只是**相关**）" % args.pre,
                lines, prio=2, keep=3)

    # ── 成功 / 失败对照 ─────────────────────────────────────────
    good = [s for s in sessions if s.n_errors == 0 and s.n_events > 3]
    bad = [s for s in sessions if s.n_errors > 0]
    if good and bad:
        a = max(good, key=lambda s: s.n_events)
        b = max(bad, key=lambda s: s.n_errors)
        diff = list(unified_diff(a.names()[:600], b.names()[:600],
                                 fromfile="成功 %s" % a.sid, tofile="失败 %s" % b.sid,
                                 lineterm="", n=1))
        lines = [clip(d, 110) for d in diff[:24]] or ["两段会话的事件序列完全一致 —— 差异在参数或时序里，不在流程上。"]
        rep.add("成功／失败对照（%s vs %s，事件序列 diff）" % (a.sid, b.sid), lines, prio=6, keep=2)
    else:
        why = "没有出错的会话" if not bad else "这几段会话里没有一段是干净走通的"
        rep.add("成功／失败对照", ["跳过：%s，没有可对照的样本。想拿基线就再跑一次正常流程。" % why],
                prio=6, keep=1)

    # ── 状态流转移矩阵 ──────────────────────────────────────────
    trans = Counter()
    enters, exits = Counter(), Counter()
    failed = []
    for sess in sessions:
        trans.update(sess.transitions)
        enters.update(sess.enters)
        exits.update(sess.exits)
        failed.extend((sess.sid, x) for x in sess.flow_failed)
    if trans or enters:
        lines = ["%-42s ×%d" % ("%s → %s" % (a, b), n) for (a, b), n in trans.most_common(12)]
        dangling = [(st, enters[st] - exits.get(st, 0)) for st in enters
                    if enters[st] - exits.get(st, 0) > 0]
        if dangling:
            lines.append("⚠ 有 enter 无 exit：%s" % "、".join(
                "%s(%d)" % (st, n) for st, n in sorted(dangling, key=lambda x: -x[1])[:6]))
            lines.append("   会话结束时停留的那个状态本来就没有 exit，属正常；**中途**的才是异常终止。")
        for sid, (seq, brief) in failed[:3]:
            lines.append("✗ state_failed sid=%s s=%s %s" % (sid, seq, brief))
        rep.add("状态流转移矩阵", lines, prio=5, keep=2)

    # ── 慢操作排行 ─────────────────────────────────────────────
    slow = []
    for sess in sessions:
        slow.extend((ms, sess.sid, slot) for ms, slot in sess.slow)
    slow.sort(key=lambda x: -x[0])
    if slow:
        lines = []
        for i, (ms, sid, slot) in enumerate(slow[:args.top], 1):
            lines.append("%2d. %-8s %-34s sid=%s s=%s f=%s %s" % (
                i, fmt_ms(ms), clip(slot["name"], 34), sid, slot["seq"], slot["f"], slot["p"]))
            if i <= 3:
                lines.append("    前后: %s ⟨本条⟩ %s" % (
                    clip(" → ".join(slot["before"]) or "—", 48),
                    clip(" → ".join(slot["after"]) or "—", 48)))
        rep.add("慢操作排行（按 p.ms）", lines, prio=4, keep=2)

    # ── 性能尖峰与采样趋势 ──────────────────────────────────────
    perf = []
    for sess in sessions:
        for slot in sess.spikes[:4]:
            perf.append("spike sid=%s s=%s t=%s f=%s %s" % (
                sess.sid, slot["seq"], fmt_ms(slot["t"]), slot["f"], slot["p"]))
            perf.append("    前后: %s ⟨尖峰⟩ %s" % (
                clip(" → ".join(slot["before"]) or "—", 44),
                clip(" → ".join(slot["after"]) or "—", 44)))
        fps = [v for v, _ in sess.samples if v is not None]
        mem = [v for _, v in sess.samples if v is not None]
        if fps:
            perf.append("fps  %s  %s (%d 次采样, 最低 %.0f / 最高 %.0f)" % (
                sess.sid, sparkline(fps), len(fps), min(fps), max(fps)))
        if mem:
            perf.append("内存 %s  %s (%.0f→%.0f MB)" % (
                sess.sid, sparkline(mem), mem[0], mem[-1]))
    if perf:
        rep.add("性能尖峰与采样趋势", perf, prio=4, keep=2)

    # ── 尾 ─────────────────────────────────────────────────────
    tail = []
    if ana.malformed:
        tail.append("⚠ %d 行无法解析（JSON 截断 / 被别的线程插断 / 级别字段不合法），已跳过。"
                    % ana.malformed)
    if scan is not None and scan.partial:
        tail.append("⚠ 只扫了尾部；更早的会话不在这份摘要里，需要就加大 --tail-mb。")
    probe = sessions[-1].sid if sessions else "<sid>"
    tail.append("下一步：基于以上提 2～3 个根因假设，再用 query 定向回读验证 —— 别整份读日志。")
    tail.append('  python .claude/skills/telemetry/analyze.py query --source %s --session %s --around <序号> --window 30'
                % (args.source, probe))
    rep.add("", tail, prio=0, keep=len(tail))
    return rep.render()


# ══════════════════════════════════════════════════════════════════════════
# 尾窗落空自动回退
# ══════════════════════════════════════════════════════════════════════════
#
# 实测现场：埋点写完之后另一个会话起了 batch 出包，几百 MB 出包日志把埋点挤出了默认
# 64MB 尾窗——默认窗口扫到 0 条是真实发生过的，不是理论风险，不能就此判「没有埋点」。

def _tail_scan_empty(ana: "Analysis") -> bool:
    """这次扫描是否「没扫到任何埋点行，或没扫到 core/session_start」。

    后一种情况（只有「(截断)」段）虽然有事件，但组不成会话、身份也校验不了，
    跟没扫到一样不可信——都要触发扩大重扫。
    """
    return not ana.sessions or all(s.truncated for s in ana.sessions)


def _widen_tail_mb(tail_mb: int, size_bytes: int) -> int:
    """尾窗落空时下一档扫描范围：×4；够到文件大小就直接跳到全量（0），不做更多档位。"""
    nxt = max(1, tail_mb) * 4
    return 0 if nxt * 1024 * 1024 >= size_bytes else nxt


def scan_and_analyze_with_fallback(path: Path, tail_mb: int, last: int, module: str):
    """`summarize` 的核心：扫一遍，落空且这次没扫到全量就按 ×4 扩大重扫，直到扫到数据
    或者已经扫过全量为止（`scan.partial` 变 False 就是到头了，不会死循环——`_widen_tail_mb`
    单调递增且有 `size_bytes` 兜底会归零）。全程流式读取，不把文件读进内存。

    返回 `(Analysis, Scan, fallback_from, final_tail_mb)`：`fallback_from` 非 `None` 时
    表示回退过，值是最初那次落空的 `--tail-mb`（MB）；没回退就是 `None`。
    """
    fallback_from = None
    while True:
        scan, lines = open_log(path, tail_mb)
        ana = analyze(lines, last, module)
        if not _tail_scan_empty(ana) or not scan.partial:
            return ana, scan, fallback_from, tail_mb
        nxt = _widen_tail_mb(tail_mb, scan.size)
        if fallback_from is None:
            fallback_from = tail_mb
        print("[analyze] 尾部 %s 内没有埋点数据，扩大到%s重扫……（大日志可能要几秒到几十秒，没有卡死）"
              % (human_size(tail_mb * 1024 * 1024),
                 "全量" if nxt == 0 else human_size(nxt * 1024 * 1024)), file=sys.stderr)
        tail_mb = nxt


# ══════════════════════════════════════════════════════════════════════════
# 多文件输入（镜像目录）
# ══════════════════════════════════════════════════════════════════════════
#
# 为什么是「每份单独解析再归并」，而不是把几个文件拼成一条行流：
#   1. 原生报错的堆栈是**跨行状态**（`Analysis._collect`）。拼流会让 A 文件末尾那条异常
#      把 B 文件开头的行吃进自己的栈里，指纹就脏了。
#   2. 行号从各自文件的扫描起点起算，拼流以后行号没法回指到具体文件。
#   3. 会话「来自哪个文件」在归并那一步一次性盖章，query 才知道该去哪个文件取窗口。
# 代价是要自己做归并与排序，换来的是切段 / 聚类 / 前序这些逻辑一行没动，单文件路径零风险。


def _file_mtime(path: Path) -> float:
    try:
        return path.stat().st_mtime
    except OSError:
        return 0.0


def session_sort_key(sess, file_mtime: float):
    """一段会话的全局时刻，与它是不是实证的。→ `(时刻, 是否来自 at)`。

    **优先 `session_start` 的 `at`**（墙钟 ISO8601，`docs/telemetry.md` 第 1 节会话头）：
    那是这段会话真正开始的时刻，拷贝、同步、备份、`git checkout` 都改不动它。
    没有 `at`（旧镜像文件 / 埋点上线前的日志 / 开头被截断的段）才回退到**文件最后写入时刻**——
    一个文件一段会话时它约等于那段会话的结束时刻，够用，但 mtime 会被拷贝/同步/备份改掉，
    排出来的先后**不保证可靠**，所以摘要头部要如实标出来。

    两种键都是同一把尺子上的 epoch 秒，可以直接比大小，混合情况因此不用特判。
    """
    at = _iso_epoch(sess.meta.get("at")) if sess.meta else None
    if at is not None:
        return at, True
    return file_mtime, False


def merge_analyses(parts, last: int, module: str) -> Analysis:
    """`[(path, Analysis)]` → 一个合并后的 `Analysis`，只留**最近 `last` 段会话**。

    会话的全局先后按 `session_sort_key` 排：有 `at` 的用 `at`，没有的用所在文件的 mtime 兜底。
    完全相等时按 (文件次序, 文件内出现次序) 兜底，保证同一文件内的多段会话不被打乱、
    结果可复现。日志是追加写的，所以「文件内出现次序 = 该文件内的时间顺序」成立。

    `--last N` 因此是「最近 N **段会话**」而不是「最近 N 个文件」：一个文件里有三段，
    `--last 2` 就只取那个文件里最后两段。
    """
    out = Analysis(last, module)
    rows = []
    n_at = n_mtime = 0
    for rank, (path, ana) in enumerate(parts):
        out.malformed += ana.malformed
        out.plain += ana.plain
        out.native_hits += ana.native_hits
        out.dropped += ana.dropped          # 单个文件内部因为 --last 已经丢掉的
        mtime = _file_mtime(path)
        for idx, sess in enumerate(ana.sessions):
            sess.src = path
            when, from_at = session_sort_key(sess, mtime)
            sess.at_epoch = when if from_at else None
            n_at += 1 if from_at else 0
            n_mtime += 0 if from_at else 1
            rows.append((when, rank, idx, sess))
    rows.sort(key=lambda r: (r[0], r[1], r[2]))
    ordered = [r[3] for r in rows]
    keep = max(1, int(last))
    if len(ordered) > keep:
        out.dropped += len(ordered) - keep
        ordered = ordered[-keep:]
    out.sessions = ordered
    out.cur = ordered[-1] if ordered else None
    out.order_basis = (n_at, n_mtime)
    return out


def order_basis_note(basis) -> str:
    """摘要头部那一行：这次跨文件排序按的是什么，可不可靠。`basis` 为 `None` 时给空串
    （单文件源根本不需要排序）。"""
    if not basis:
        return ""
    n_at, n_mtime = basis
    if n_mtime == 0:
        return "会话排序: 按 session_start 的 at（墙钟时刻），%d 段全都有" % n_at
    warn = ("⚠ 文件 mtime 会被拷贝 / 同步 / 备份改掉，这部分的跨文件先后不保证可靠"
            "（运行时补上 at 之后新会话就没这个问题）")
    if n_at == 0:
        return "会话排序: 按文件最后写入时刻兜底，%d 段都没有 at 字段 —— %s" % (n_mtime, warn)
    return "会话排序: %d 段按 at（墙钟时刻），%d 段没有 at、按文件最后写入时刻兜底 —— %s" % (
        n_at, n_mtime, warn)


def analyze_mirror(src: SourceSet, last: int, module: str):
    """镜像目录：逐份整读、逐份解析，再归并。返回 `(Analysis, MultiScan)`。

    单个文件读不了（权限 / 正被写 / 被删）只跳过它并在 stderr 说一声，不让整趟分析挂掉——
    镜像目录常态就有好几段会话，为一份坏文件放弃另外几段不划算。空文件、内容被截断的文件
    走的是正常解析路径：解析不出来的行计进 `malformed`，同样不影响别的文件。
    """
    scans, parts, skipped = [], [], []
    for path in src.paths:
        try:
            scan, lines = open_log(path, MIRROR_TAIL_MB)
        except LogError as exc:
            skipped.append(path)
            print("[analyze] ⚠ 跳过镜像文件 %s（其余文件照常分析）：%s"
                  % (path.name, str(exc).splitlines()[0]), file=sys.stderr)
            continue
        # 每份先各留最近 last 段：全局最近 N 段在单个文件里必然是该文件的后缀，不会误丢。
        parts.append((path, analyze(lines, last, module)))
        scans.append(scan)
    return merge_analyses(parts, last, module), MultiScan(scans, src.root, skipped)


def scan_and_analyze(src: SourceSet, tail_mb: int, last: int, module: str):
    """`summarize` 的统一入口：单文件走尾窗 + 落空自动回退那条老路，镜像目录走整读多文件。

    返回 `(Analysis, scan, fallback_from, final_tail_mb)`，两条路的返回形状一致，
    调用方不用分支。镜像永远不回退（本来就是全量），`fallback_from` 给 `None`。
    """
    if src.kind == "mirror":
        ana, scan = analyze_mirror(src, last, module)
        return ana, scan, None, MIRROR_TAIL_MB
    return scan_and_analyze_with_fallback(src.primary, tail_mb, last, module)


# ══════════════════════════════════════════════════════════════════════════
# 子命令
# ══════════════════════════════════════════════════════════════════════════

def _print_mirror_overview(mirror_dir: Path = None) -> None:
    """`sources` 的第一节：编辑器镜像目录。

    **必须保持秒级**——只 stat + 按字节粗扫（`quick_scan` 不解码不切行），镜像目录一共也就
    几 MB，代价可以忽略；`sources` 真正的开销在后面那几份几 GB 的 Editor.log 上。
    """
    d = mirror_dir if mirror_dir is not None else MIRROR_DIR
    files = mirror_files(d)
    if not files:
        print("编辑器镜像目录: ✗ %s" % mirror_fallback_reason(d))
        print("  镜像只在**编辑器**下写（一段会话一个 <sid>.log）；真机 / 出包走 Player.log，"
              "没有这个目录是正常的。")
        return
    total = sessions = tel = 0
    per = []
    for p in files:
        info = quick_scan(p, MIRROR_TAIL_MB)
        try:
            size = p.stat().st_size
        except OSError:
            size = 0
        total += size
        sessions += info["sessions"]
        tel += info["tel_lines"]
        per.append((p, info["sessions"], size))
    print("编辑器镜像目录: ✓ %s" % d)
    print("  %d 个文件 ｜ 会话 %d 段 ｜ 埋点行 %d 条 ｜ 共 %s"
          % (len(files), sessions, tel, human_size(total)))
    print("  时间范围 %s → %s（按文件最后写入）" % (fmt_time(files[0]), fmt_time(files[-1])))
    for p, n_sess, size in reversed(per[-5:]):
        print("    %-22s %-9s 会话 %d 段 ｜ %s" % (p.name, human_size(size), n_sess, fmt_time(p)))
    if len(per) > 5:
        print("    …… 另有 %d 个更早的文件（默认整个目录一起吃，用 --last N 收窄会话段数）"
              % (len(per) - 5))


def cmd_sources(args) -> int:
    _utf8_stdio()
    print("日志源（会话数为粗扫结果，只数 session_start 标记）\n")

    _print_mirror_overview()
    print()

    ptr_info = read_pointer()
    ptr_target = pointer_target(ptr_info)
    if ptr_target is not None:
        ago = _ago(ptr_info.get("written_at"))
        print("指针文件: ✓ %s" % POINTER_PATH)
        print("  指向 %s ｜ 写入于 %s%s ｜ sid=%s" % (
            ptr_target, ptr_info.get("written_at") or "?",
            ("（%s）" % ago) if ago else "", ptr_info.get("sid") or "?"))
    else:
        print("指针文件: ✗ %s" % pointer_fallback_reason(ptr_info))
    print()

    _, product = project_names()
    found = 0
    for label, path, desc in candidate_sources():
        if not path.is_file():
            print("  ✗ %-12s %s" % (label, path))
            print("      不存在 —— %s" % desc)
            continue
        found += 1
        info = quick_scan(path, args.tail_mb)
        size = path.stat().st_size
        print("  ✓ %-12s %s" % (label, path))
        print("      %s ｜ 最后写入 %s ｜ %s" % (human_size(size), fmt_time(path), desc))
        empty_partial = info["partial"] and info["sessions"] == 0 and info["tel_lines"] == 0
        if empty_partial:
            # 只是这台机器的尾窗快扫没命中，不代表日志里真没有——见过埋点被别的进程续写的
            # 大量日志挤出默认尾窗的现场。sources 图快不做重扫，只如实标注「没扫全量」。
            print("      会话 0 段 ｜ 埋点行 0 条 ｜ ⚠ 尾部 %s 内没有，未扫全量（用 --tail-mb 0 全量确认）"
                 % human_size(info["scanned"]))
        else:
            note = "（只扫了尾部 %s）" % human_size(info["scanned"]) if info["partial"] else ""
            print("      会话 %d 段 ｜ 埋点行 %d 条 %s" % (info["sessions"], info["tel_lines"], note))
        if info["error"]:
            print("      ⚠ 读取时出错：%s" % info["error"])
        origin_tag = "指针" if (ptr_target is not None and ptr_target.resolve() == path.resolve()) else "猜测"
        meta = find_first_session_meta(path, args.tail_mb)
        prod = meta.get("prod") if meta else None
        if meta is None and empty_partial:
            identity = "未知（尾窗内没有 session_start，未扫全量，见上）"
        elif meta is None:
            identity = "未知（没有 session_start）"
        elif not prod:
            identity = "未知（session_start 无 prod 字段，可能是老日志）"
        elif prod == product:
            identity = "匹配（prod=%s）" % prod
        else:
            identity = "✗ 不匹配（prod=%s，本工程=%s）" % (prod, product)
        print("      来源: %s ｜ 工程身份: %s" % (origin_tag, identity))
    print()
    if not found and not mirror_files():
        print("一份日志都没找到。Unity 编辑器至少启动过一次才有 Editor.log；"
              "Player.log 要出包并运行过才有。\n也可以 `--source <日志完整路径>` 直接指一份。")
        return 1
    print("用法：analyze.py summarize [--source auto|mirror|editor|player|<路径>] [--module <模块>] [--last N]")
    print("默认 --source auto：镜像目录 → 指针文件 → 显式路径 → 猜默认路径。")
    print("镜像目录可用时**默认吃整个目录**（多段会话一起分析，跨会话对照才成立）；"
          "--last N 数的是会话段，不是文件数。")
    print("猜出来的路径若工程身份对不上会直接报错退出；镜像目录与指针来源不校验（本来就是本工程写的）。")
    return 0


def cmd_summarize(args) -> int:
    _utf8_stdio()
    src, origin, locate_note = locate_source(args.source, args.tail_mb)
    ana, scan, fallback_from, final_tail_mb = scan_and_analyze(
        src, args.tail_mb, args.last, args.module)
    # 身份校验放在回退之后、用回退落定的窗口去查——尾窗一开始太小连 session_start 都没扫到时
    # 校验不了，放在这也不会白扫一遍；`check_identity` 自己的扫描量比这份 analyze() 小得多。
    if src.kind == "mirror":
        identity_note = "工程身份: 编辑器镜像目录，不校验（工程内目录，只可能是本工程写的）"
    elif origin == "pointer":
        identity_note = "工程身份: 指针文件来源，不校验（本来就是本工程写的）"
    else:
        identity_note = check_identity(src.primary, final_tail_mb, origin)
    fallback_note = ""
    if fallback_from is not None:
        fallback_note = "⚠ 尾部 %s 内没有埋点数据，已自动扩大重扫至 %s" % (
            human_size(fallback_from * 1024 * 1024), scan.describe())
    sys.stdout.write(render_summary(
        ana, scan, args, locate=(origin, locate_note, identity_note), fallback_note=fallback_note))
    return 0


def _no_hit_msg(args, seen_sids) -> list:
    """query 一条都没命中时的人话。单文件与镜像目录共用一份，免得两套提示走样。"""
    msg = ["没有命中。"]
    if args.session and args.session not in seen_sids:
        msg.append("这段日志里没有 sid=%s。扫到的 sid：%s" % (
            args.session, "、".join(seen_sids) or "（一个都没有）"))
        msg.append("单文件源就加大 --tail-mb（镜像目录本来就整读，换个源看）；"
                   "或先跑 summarize 确认 sid。")
    return msg


def run_query(lines, args, quiet_empty: bool = False, seen_out=None) -> list:
    """query 的内核。吃 (行号, 文本)，吐要打印的行 —— selftest 直接喂列表。

    `quiet_empty` / `seen_out` 是给多文件调用方的：镜像目录下一份文件没命中很正常
    （会话在别的文件里），这时别每份各打一遍「没有命中」；把扫到的 sid 攒回 `seen_out`，
    由调用方汇总后统一说一次。默认值保持单文件时的老行为不变。
    """
    out = []
    grep = None
    if args.grep:
        try:
            grep = re.compile(args.grep)
        except re.error as exc:
            raise LogError("--grep 的正则写坏了：%s" % exc)
    lo = hi = None
    if args.around is not None:
        lo, hi = args.around - args.window, args.around + args.window
    seen_sids = []
    cur_sid, cur_seq = None, None
    hits = 0

    for line, text in lines:
        status, ev = parse_line(text)
        if status == "event":
            ev.line = line
            if ev.mod == "core" and ev.ev == "session_start":
                cur_sid = str(ev.p.get("sid") or "?")
                if cur_sid not in seen_sids:
                    seen_sids.append(cur_sid)
            if ev.s is not None:
                cur_seq = ev.s
        if args.session and cur_sid != args.session:
            continue
        if lo is not None:
            if cur_seq is None or not (lo <= cur_seq <= hi):
                continue
        else:
            if status != "event":
                # 非埋点行只有 --grep 模式才捞（原生报错就靠这条路看）
                if grep is None or not grep.search(text):
                    continue
            else:
                if args.level and ev.lv != args.level:
                    continue
                if args.module and not (ev.mod == args.module or ev.mod.startswith(args.module + ".")):
                    continue
                if args.event and ev.ev != args.event:
                    continue
                if grep is not None and not grep.search(text):
                    continue
        hits += 1
        if hits > args.limit:
            out.append("…… 命中超过 --limit %d 条，后面的没打。收窄条件（--window 调小 / 加 --module）再来。"
                       % args.limit)
            break
        out.append("L%-7d %s" % (line, clip(text, 220)))

    if seen_out is not None:
        for sid in seen_sids:
            if sid not in seen_out:
                seen_out.append(sid)
    if not out and not quiet_empty:
        out = _no_hit_msg(args, seen_sids)
    return out


def cmd_query(args) -> int:
    _utf8_stdio()
    if args.around is None and not any([args.session, args.module, args.event, args.grep, args.level]):
        raise LogError("query 至少要给一个定位条件：--session / --around / --module / --event / --grep / --level。\n"
                       "  没有条件就等于整份读日志 —— 那正是这个工作流要避免的。")
    src, origin, locate_note = locate_source(args.source, args.tail_mb)
    if src.kind == "mirror":
        identity_note = "工程身份: 编辑器镜像目录，不校验（工程内目录，只可能是本工程写的）"
    elif origin == "pointer":
        identity_note = "工程身份: 指针文件来源，不校验（本来就是本工程写的）"
    else:
        identity_note = check_identity(src.primary, args.tail_mb, origin)
    print("定位路径: %s%s ｜ %s" % (
        ORIGIN_LABEL.get(origin, origin), ("（%s）" % locate_note) if locate_note else "", identity_note))

    if src.kind != "mirror":
        scan, lines = open_log(src.primary, args.tail_mb)
        print("源: %s ｜ %s" % (scan.path, scan.describe()))
        print("行号从扫描起点起算，仅供肉眼定位；**引用证据请用 sid + 序号 s**。\n")
        for row in run_query(lines, args):
            print(row)
        return 0

    # 镜像目录：契约是一段会话一个 `<sid>.log`，所以先读同名那份（O(1) 命中，不用把整个目录
    # 翻一遍），命中不到再扫其余文件 —— 顺序由 `mirror_files_for_session` 排好。
    # `--limit` 是**跨文件的总预算**，不是每份文件各给一份，否则条件一宽就刷屏。
    targets = mirror_files_for_session(src, args.session)
    print("源: %s ｜ 每份整读（--tail-mb 对镜像不生效）" % src.label())
    print("行号从各自文件的扫描起点起算，仅供肉眼定位；**引用证据请用 sid + 序号 s**。\n")
    seen, printed, remaining = [], 0, max(1, int(args.limit))
    for path in targets:
        if remaining <= 0:
            print("…… 命中已达 --limit %d 条，剩下的文件没扫。收窄条件（--window 调小 / 加 --module）再来。"
                  % args.limit)
            break
        try:
            _, lines = open_log(path, MIRROR_TAIL_MB)
        except LogError as exc:
            print("⚠ 跳过 %s：%s" % (path.name, str(exc).splitlines()[0]))
            continue
        sub = argparse.Namespace(**vars(args))
        sub.limit = remaining
        rows = run_query(lines, sub, quiet_empty=True, seen_out=seen)
        if not rows:
            continue
        print("── %s ──" % path.name)
        for row in rows:
            print(row)
        hits = sum(1 for r in rows if r.startswith("L"))
        remaining -= hits
        printed += hits
    if printed == 0:
        for row in _no_hit_msg(args, seen):
            print(row)
    return 0


# ══════════════════════════════════════════════════════════════════════════
# 自检：内置样例日志，没有 Unity 也能验证解析 / 切段 / 聚类是对的
# ══════════════════════════════════════════════════════════════════════════

SELFTEST_LOG = """\
Initialize engine version: 2022.3.62f2 (39a0a0f0bbb4)
[Game][T] I core.flow/state_enter | {"t":10,"s":880,"f":300,"p":{"from":"TitleState","to":"GameState"}}
[Game][T] I sample/buy_item | {"t":20,"s":881,"f":301,"p":{"id":7,"n":1}}
NullReferenceException: Object reference not set to an instance of an object
  at Game.Sample.Shop.Buy () [0x0001a] in <9f2>:0
UnityEngine.Debug:LogError (object)
(Filename: Assets/_Project/Scripts/Runtime/Sample/Shop.cs Line: 42)

[Game][T] I core/session_start | {"t":0,"s":0,"f":0,"p":{"sid":"aaaa1111","ver":"0.1.0","plat":"WindowsEditor","unity":"2022.3.62f2","scr":"1920x1080","mem":16384}}
[Game][T] I core.boot/step | {"t":12,"s":1,"f":1,"p":{"name":"config","ms":8}}
[Game][T] I core.boot/ready | {"t":40,"s":2,"f":2,"p":{"ms":40}}
[Game][T] I core.flow/state_enter | {"t":50,"s":3,"f":3,"p":{"from":"BootState","to":"TitleState","ms":10}}
[Game][T] I core.asset/load | {"t":80,"s":4,"f":5,"p":{"key":"UI_Title","ms":30}}
[Game][T] I core.ui/open | {"t":95,"s":5,"f":6,"p":{"panel":"TitlePanel","depth":1,"ms":15}}
[Game][T] I core.flow/state_exit | {"t":300,"s":6,"f":40,"p":{"from":"TitleState","to":"GameState"}}
[Game][T] I core.flow/state_enter | {"t":305,"s":7,"f":41,"p":{"from":"TitleState","to":"GameState","ms":5}}
[Game][T] I core.asset/scene_load | {"t":600,"s":8,"f":60,"p":{"key":"SampleScene_Game","ms":290}}
[Game][T] I core.perf/sample | {"t":5000,"s":9,"f":300,"p":{"fps":60,"mem_mb":420}}
[Game][T] I core.perf/sample | {"t":10000,"s":10,"f":600,"p":{"fps":58,"mem_mb":440}}
[Game][T] I core.flow/state_exit | {"t":12000,"s":11,"f":700,"p":{"from":"GameState","to":"TitleState"}}
[Game][T] I core/session_end | {"t":12010,"s":12,"f":700,"p":{"ok":true}}
[Game][T] I core/session_start | {"t":0,"s":0,"f":0,"p":{"sid":"bbbb2222","ver":"0.1.0","plat":"WindowsEditor","unity":"2022.3.62f2","scr":"1920x1080","mem":16384}}
[Game][T] I core.boot/step | {"t":11,"s":1,"f":1,"p":{"name":"config","ms":9}}
[Game][T] I core.boot/ready | {"t":38,"s":2,"f":2,"p":{"ms":38}}
[Game][T] I core.flow/state_enter | {"t":49,"s":3,"f":3,"p":{"from":"BootState","to":"TitleState","ms":11}}
[Game][T] I core.asset/load | {"t":79,"s":4,"f":5,"p":{"key":"UI_Title","ms":31}}
[Game][T] I core.ui/open | {"t":94,"s":5,"f":6,"p":{"panel":"TitlePanel","depth":1,"ms":14}}
[Game][T] I core.flow/state_exit | {"t":290,"s":6,"f":39,"p":{"from":"TitleState","to":"GameState"}}
[Game][T] I core.flow/state_enter | {"t":295,"s":7,"f":40,"p":{"from":"TitleState","to":"GameState","ms":5}}
[Game][T] E core.asset/load_failed | {"t":620,"s":8,"f":61,"p":{"key":"SampleScene_Game"},"err":"InvalidKeyException: SampleScene_Game","st":"Game.Core.Assets.AssetService:LoadAsync (string) (at Assets/_Project/Scripts/Core/Assets/AssetService.cs:88) ⏎ Game.Core.Flow.GameState:OnEnter () (at Assets/_Project/Scripts/Core/Flow/GameState.cs:21)"}
[Game][T] I core.perf/sample | {"t":5000,"s":9,"f":300,"p":{"fps":31,"mem_mb":520}}
[Game][T] W core.perf/spike | {"t":5200,"s":10,"f":310,"p":{"frame_ms":420,"fps":2}}
[Game][T] I sample/buy_item | {"t":5300,"s":11,"f":311,"p":{"id":7,"n":1,"ms":1800}}
[Game][T] E core.asset/load_failed | {"t":6100,"s":12,"f":330,"p":{"key":"SampleScene_Game"},"err":"InvalidKeyException: SampleScene_Game","st":"Game.Core.Assets.AssetService:LoadAsync (string) (at Assets/_Project/Scripts/Core/Assets/AssetService.cs:91) ⏎ Game.Core.Flow.GameState:OnEnter () (at Assets/_Project/Scripts/Core/Flow/GameState.cs:23)"}
[Game][T] E core.asset/load_failed | {"t":6200,"s":13,"f":331,
[Game][T] X core.asset/load_failed | {"t":6300,"s":14,"f":332,"p":{}}
[Game][T] I sa[Worker] 线程把这一行插断了
[Game][T] I core.perf/sample | {"t":10000,"s":15,"f":600,"p":{"fps":18,"mem_mb":690}}
MissingComponentException: There is no 'Rigidbody2D' attached to the "Player" game object
Game.Sample.Mover:Move () (at Assets/_Project/Scripts/Runtime/Sample/Mover.cs:31)
Game.Sample.Mover:Update () (at Assets/_Project/Scripts/Runtime/Sample/Mover.cs:18)
Assets/_Project/Scripts/Runtime/Sample/Mover.cs(31,9): error CS0103: The name 'foo' does not exist
[Game][T] I sample/buy_item | {"t":11000,"s":16,"f":620,"p":{"id":9,"n":2}}
"""


def _selftest_lines():
    return list(enumerate(SELFTEST_LOG.splitlines(), 1))


class _Args:
    def __init__(self, **kw):
        self.__dict__.update(kw)


def cmd_selftest(_args=None) -> int:
    _utf8_stdio()
    checks = []

    def check(name, cond, detail=""):
        checks.append((bool(cond), name, detail))

    ana = analyze(_selftest_lines(), last=10, module="")
    sids = [s.sid for s in ana.sessions]
    check("切出 3 段会话（截断段 + 2 次运行）", sids == ["(截断)", "aaaa1111", "bbbb2222"], str(sids))
    check("开头无 session_start 的那段标为截断", ana.sessions[0].truncated)
    check("正常结束的会话认得出 session_end", ana.sessions[1].ended == "正常")
    check("崩溃/未结束的会话不被当成正常", ana.sessions[2].ended == "")
    check("不跨会话混事件（aaaa1111 共 13 条）", ana.sessions[1].n_events == 13,
          str(ana.sessions[1].n_events))

    check("畸形行被计数而非静默吞掉（JSON 截断 / 级别非法 / 被插断 = 3）",
          ana.malformed == 3, str(ana.malformed))

    clusters = build_clusters(ana.all_errors())
    keyed = {c.sample.msg.split(":")[0]: c for c in clusters}
    load_cluster = next((c for c in clusters if "InvalidKeyException" in c.sample.msg), None)
    check("同一错误不同行号归并成一类且 ×2", load_cluster is not None and load_cluster.count == 2,
          "" if load_cluster is None else "×%d" % load_cluster.count)
    check("原生异常（没走埋点）也进了聚类",
          any(c.sources == {"原生"} and "MissingComponentException" in c.sample.msg for c in clusters))
    check("原生异常带堆栈帧", any(c.sample.frames and "Mover" in c.sample.frames[0]
                             for c in clusters if "MissingComponent" in c.sample.msg))
    check("编译错误被抓成原生报错", any("error CS0103" in c.sample.msg for c in clusters))
    check("截断段里的原生异常归给截断会话", ana.sessions[0].n_errors == 1,
          str(ana.sessions[0].n_errors))

    common = common_prefix_seq(load_cluster.befores) if load_cluster else []
    check("两次错误的共同前序非空且含 state_enter",
          "core.flow/state_enter" in common, " → ".join(common))

    sess_b = ana.sessions[2]
    check("状态转移矩阵记下 TitleState→GameState",
          sess_b.transitions.get(("TitleState", "GameState")) == 1)
    check("有 enter 无 exit 能算出来（GameState）",
          sess_b.enters.get("GameState", 0) - sess_b.exits.get("GameState", 0) == 1)
    check("慢操作排行取到最慢那条（1800ms 的 buy_item）",
          sorted(sess_b.slow, key=lambda x: -x[0])[0][1]["name"] == "sample/buy_item")
    check("尖峰带上了前后文", sess_b.spikes and sess_b.spikes[0]["after"])
    check("perf 采样拿到 fps 序列", len([v for v, _ in sess_b.samples if v]) == 2,
          str([v for v, _ in sess_b.samples]))
    check("sparkline 不因全等值崩掉", sparkline([5, 5, 5]) == SPARK[0] * 3)

    # 模块过滤：只筛错误与排行，不影响切段
    ana_m = analyze(_selftest_lines(), last=10, module="sample")
    check("--module 只筛错误（core.asset 的错误被排除）",
          all("core.asset" not in h.name for h in ana_m.sessions[2].errors))
    check("--module 不影响会话切段", len(ana_m.sessions) == 3)

    qa = _Args(session="bbbb2222", around=12, window=2, module="", event="", grep="", level="", limit=50)
    rows = run_query(_selftest_lines(), qa)
    check("query 按 sid+序号窗口回读（只给 bbbb2222 的 s=10..14）",
          rows and all("aaaa1111" not in r for r in rows) and 3 <= len(rows) <= 10,
          "%d 行" % len(rows))
    qb = _Args(session="", around=None, window=30, module="", event="", grep="MissingComponentException",
               level="", limit=50)
    check("query --grep 捞得到非埋点的原生报错行",
          any("MissingComponentException" in r for r in run_query(_selftest_lines(), qb)))
    qc = _Args(session="nosuch1", around=None, window=30, module="", event="", grep="", level="", limit=50)
    check("query 指到不存在的 sid 时给人话提示",
          any("没有 sid=nosuch1" in r for r in run_query(_selftest_lines(), qc)))

    args = _Args(max_lines=DEFAULT_MAX_LINES, top=DEFAULT_TOP, pre=DEFAULT_PRE,
                 module="", source="editor")
    text = render_summary(ana, None, args)
    n_lines = len(text.splitlines())
    check("摘要渲染不报错且在 --max-lines 以内", 0 < n_lines <= DEFAULT_MAX_LINES, "%d 行" % n_lines)
    tight = _Args(max_lines=45, top=DEFAULT_TOP, pre=DEFAULT_PRE, module="", source="editor")
    n_tight = len(render_summary(ana, None, tight).splitlines())
    check("--max-lines 收紧时真的裁剪", n_tight <= 45, "%d 行" % n_tight)
    check("摘要里带得出会话 sid", "bbbb2222" in text)

    bad = analyze([(1, "随便一行不相干的日志"), (2, "")], last=3, module="")
    check("空日志不崩（0 会话 0 错误）", bad.sessions == [] and bad.malformed == 0)

    # ── 指针文件定位 + 工程身份校验（docs/telemetry.md「日志到底在哪」一节）──────────
    # 全部用临时目录造样例，不碰真实 Logs/ 或真实 Editor.log。
    def _mk_line(mod_ev, p):
        return "[Game][T] I %s | %s" % (mod_ev, json.dumps({"t": 0, "s": 0, "f": 0, "p": p}))

    with tempfile.TemporaryDirectory(prefix="telemetry-selftest-") as tmp:
        tmp_dir = Path(tmp)
        real_log = tmp_dir / "Fake-Editor.log"
        real_log.write_text("哨兵行，不影响解析\n", encoding="utf-8")

        # 1) 指针文件正常读取：四行都在
        ptr_ok = tmp_dir / "pointer_ok.txt"
        ptr_ok.write_text("%s\nproject1\ndeadbeef\n2026-09-16T10:00:00+08:00\n" % real_log,
                          encoding="utf-8")
        info_ok = read_pointer_file(ptr_ok)
        check("指针文件正常读取（四行都解出来）",
              info_ok["exists"] and info_ok["log_path"] == str(real_log)
              and info_ok["prod"] == "project1" and info_ok["sid"] == "deadbeef"
              and info_ok["written_at"] == "2026-09-16T10:00:00+08:00", str(info_ok))
        check("指针指向的文件存在时 pointer_target 能拿到 Path", pointer_target(info_ok) == real_log)

        # 2) 指针文件不存在 → 优雅退化
        info_missing = read_pointer_file(tmp_dir / "nosuchpointer.txt")
        check("指针文件不存在时字段全空、不抛异常",
              not info_missing["exists"] and info_missing["log_path"] == ""
              and pointer_target(info_missing) is None)
        check("……退化理由提示「还没在 Unity 里跑过游戏」",
              "还没在 Unity 里跑过游戏" in pointer_fallback_reason(info_missing))

        # 3) 指针文件内容残缺：只有两行（log_path + prod，缺 sid / written_at）
        #    log_path 本身够用，不该被误判成「不可用」而多余退化到猜路径
        ptr_partial = tmp_dir / "pointer_partial.txt"
        ptr_partial.write_text("%s\nproject1\n" % real_log, encoding="utf-8")
        info_partial = read_pointer_file(ptr_partial)
        check("指针文件内容残缺（只有两行）时不崩、缺的字段留空",
              info_partial["exists"] and info_partial["log_path"] == str(real_log)
              and info_partial["prod"] == "project1"
              and info_partial["sid"] == "" and info_partial["written_at"] == "")
        check("……但 log_path 完整时仍可用于定位（不必要地退化会误判成别的工程）",
              pointer_target(info_partial) == real_log)

        # 3b) 更极端的残缺：指针文件完全是空的
        ptr_empty = tmp_dir / "pointer_empty.txt"
        ptr_empty.write_text("", encoding="utf-8")
        info_empty = read_pointer_file(ptr_empty)
        check("指针文件完全为空时不崩、log_path 留空",
              info_empty["exists"] and info_empty["log_path"] == "" and pointer_target(info_empty) is None)
        check("……退化理由说「内容不全」", "内容不全" in pointer_fallback_reason(info_empty))

        # 4) 指针指向的日志已被删
        gone_path = tmp_dir / "已经不在了.log"
        ptr_gone = tmp_dir / "pointer_gone.txt"
        ptr_gone.write_text("%s\nproject1\nfeed1234\n2026-09-16T09:00:00+08:00\n" % gone_path,
                            encoding="utf-8")
        info_gone = read_pointer_file(ptr_gone)
        check("指针指向的日志已不存在时 pointer_target 给 None（不是抛异常）",
              info_gone["exists"] and pointer_target(info_gone) is None)
        check("……退化理由带上那个已经不在的路径", str(gone_path) in pointer_fallback_reason(info_gone))

        # locate_source：指针可用时直接命中该分支不会碰 resolve_source（不依赖本机真实 Editor.log）。
        # 镜像目录注入一个空的临时目录，保证走到指针那一档——真实 Logs/telemetry 有没有都不影响。
        empty_mirror = tmp_dir / "no_mirror_here"
        src_lc, origin_lc, note_lc = locate_source("auto", 64, pointer_info=info_ok,
                                                  mirror_dir=empty_mirror)
        check("locate_source：镜像目录不可用时退到指针，origin=pointer 且路径就是指针指向的那份",
              origin_lc == "pointer" and len(src_lc) == 1 and src_lc.primary == real_log
              and "deadbeef" in note_lc)

        # ── 工程身份校验 ──────────────────────────────────────────
        _, real_product = project_names()

        log_match = tmp_dir / "match.log"
        log_match.write_text(_mk_line("core/session_start", {"sid": "cccc3333", "prod": real_product}) + "\n",
                             encoding="utf-8")
        note_match = check_identity(log_match, 64, "guess")
        check("工程身份校验放过：prod 与本工程一致时不报错", "匹配" in note_match, note_match)

        mismatched_prod = real_product + "_别的工程"
        log_mismatch = tmp_dir / "mismatch.log"
        log_mismatch.write_text(
            _mk_line("core/session_start", {"sid": "cccc3333", "prod": mismatched_prod}) + "\n",
            encoding="utf-8")
        raised, err_msg = False, ""
        try:
            check_identity(log_mismatch, 64, "guess")
        except LogError as exc:
            raised, err_msg = True, str(exc)
        check("工程身份校验命中：猜出来的路径 prod 不匹配时报错退出",
              raised and real_product in err_msg and mismatched_prod in err_msg, err_msg)

        raised_explicit = False
        try:
            note_warn = check_identity(log_mismatch, 64, "explicit")
        except LogError:
            raised_explicit = True
        check("显式指定的路径 prod 不匹配时只警告、不中断（不像 guess 那样报错退出）",
              not raised_explicit)

        log_no_session = tmp_dir / "no_session.log"
        log_no_session.write_text(_mk_line("sample/buy_item", {"id": 1}) + "\n", encoding="utf-8")
        note_no_session, raised_no_session = None, False
        try:
            note_no_session = check_identity(log_no_session, 64, "guess")
        except LogError:
            raised_no_session = True
        check("日志里一条 session_start 都没有时不误判为身份校验失败",
              not raised_no_session and note_no_session is not None and "未校验" in note_no_session,
              note_no_session)

        log_no_prod = tmp_dir / "no_prod.log"
        log_no_prod.write_text(_mk_line("core/session_start", {"sid": "dddd4444"}) + "\n", encoding="utf-8")
        note_no_prod = check_identity(log_no_prod, 64, "guess")
        check("session_start 有但没有 prod 字段（埋点上线前的老日志）时也不误判为失败",
              "未校验" in note_no_prod, note_no_prod)

        # 摘要头部注明本次用的是哪条定位路径
        text_locate = render_summary(
            ana, None, args, locate=("pointer", "写入于刚刚，sid=deadbeef", "工程身份: 指针文件来源，不校验"))
        check("摘要头部注明本次用的是哪条定位路径",
              "定位路径" in text_locate and "指针文件" in text_locate and "工程身份" in text_locate)

    # ── 尾窗落空自动回退（实测：埋点被巨量无关日志挤出默认尾窗，不能就地判「没有埋点」）──
    def _pad_noise(path: Path, min_bytes: int) -> None:
        chunk = b"Fake Unity noise line, padding only, not a telemetry line at all.\n"
        with path.open("ab") as f:
            written = 0
            while written < min_bytes:
                f.write(chunk)
                written += len(chunk)

    with tempfile.TemporaryDirectory(prefix="telemetry-selftest-fallback-") as tmp2:
        tmp2_dir = Path(tmp2)

        # 场景 A：前部有一段完整埋点，后面跟一大截无关噪声——1MB 的初始尾窗看不到，
        # 回退（这里一步就跳到全量）后应该扫到。
        log_squeezed = tmp2_dir / "squeezed.log"
        log_squeezed.write_text(
            _mk_line("core/session_start", {"sid": "eeee5555", "prod": "any"}) + "\n" +
            _mk_line("core.boot/step", {"name": "config", "ms": 5}) + "\n" +
            _mk_line("core/session_end", {"ok": True}) + "\n",
            encoding="utf-8")
        _pad_noise(log_squeezed, 2 * 1024 * 1024)  # 噪声撑到 2MB+，超过下面用的 1MB 初始尾窗
        ana_fb, scan_fb, from_fb, final_mb_fb = scan_and_analyze_with_fallback(log_squeezed, 1, 10, "")
        check("尾窗落空自动回退：1MB 初始尾窗扫空，扩大后能扫到前部的会话",
              from_fb == 1 and any(s.sid == "eeee5555" for s in ana_fb.sessions),
              "fallback_from=%s sids=%s" % (from_fb, [s.sid for s in ana_fb.sessions]))
        check("……回退到位后事件数正确（session_start/boot.step/session_end 共 3 条）",
              next((s.n_events for s in ana_fb.sessions if s.sid == "eeee5555"), None) == 3)
        text_fb = render_summary(
            ana_fb, scan_fb,
            _Args(max_lines=DEFAULT_MAX_LINES, top=DEFAULT_TOP, pre=DEFAULT_PRE, module="", source="editor"),
            fallback_note="⚠ 尾部 %s 内没有埋点数据，已自动扩大重扫至 %s" % (
                human_size(1 * 1024 * 1024), scan_fb.describe()))
        check("摘要头部会注明这次回退过", "已自动扩大重扫" in text_fb)

        # 场景 B：整份文件确实没有埋点——回退到全量后依然要能正确报「没有埋点」，
        # 不能因为回退过就永远说不出结论，也不能死循环。
        log_empty = tmp2_dir / "empty.log"
        log_empty.write_text("", encoding="utf-8")
        _pad_noise(log_empty, 2 * 1024 * 1024)
        ana_empty, scan_empty, from_empty, final_mb_empty = scan_and_analyze_with_fallback(
            log_empty, 1, 10, "")
        check("全量确实没有埋点时：回退到头也如实报 0 会话（不会因为回退就误报有数据）",
              from_empty == 1 and ana_empty.sessions == [] and not scan_empty.partial)
        text_empty = render_summary(
            ana_empty, scan_empty,
            _Args(max_lines=DEFAULT_MAX_LINES, top=DEFAULT_TOP, pre=DEFAULT_PRE, module="", source="editor"))
        check("……摘要仍然明确给出「没有埋点数据」结论，且注明已是全量扫描",
              "没有埋点数据" in text_empty and "已经是全量扫描仍未命中" in text_empty)

    # ── 编辑器镜像目录：多文件输入（docs/telemetry.md「编辑器镜像」+「分析脚本的定位顺序」）──
    # 全程临时目录造样例，**绝不碰真实 Logs/telemetry**。C# 那侧的镜像 sink 还没落地时，
    # 这一段就是这条链路唯一的验证手段。
    def _ev(lv, mod_ev, s, p, err=None, st=None):
        data = {"t": s * 10, "s": s, "f": s, "p": p}
        if err:
            data["err"] = err
        if st:
            data["st"] = st
        return "[Game][T] %s %s | %s" % (lv, mod_ev, json.dumps(data))

    def _session_text(sid, with_error=False, at=None):
        meta = {"sid": sid}
        if at:
            meta["at"] = at              # 契约：at 紧跟 sid
        meta["prod"] = "project1"
        rows = [_ev("I", "core/session_start", 0, meta),
                _ev("I", "core.boot/step", 1, {"name": "config", "ms": 5}),
                _ev("I", "core.boot/ready", 2, {"ms": 20}),
                _ev("I", "core.flow/state_enter", 3, {"from": "BootState", "to": "TitleState", "ms": 8}),
                _ev("I", "core.asset/load", 4, {"key": "UI_Title", "ms": 12})]
        if with_error:
            rows.append(_ev("E", "core.asset/load_failed", 5, {"key": "SampleScene_Game"},
                            err="InvalidKeyException: SampleScene_Game",
                            st="Game.Core.Assets.AssetService:LoadAsync (string) (at A.cs:88)"
                               + ST_SEP + "Game.Core.Flow.GameState:OnEnter () (at B.cs:21)"))
        else:
            rows.append(_ev("I", "core.asset/scene_load", 5, {"key": "SampleScene_Game", "ms": 90}))
            rows.append(_ev("I", "core/session_end", 6, {"ok": True}))
        return "\n".join(rows) + "\n"

    with tempfile.TemporaryDirectory(prefix="telemetry-selftest-mirror-") as tmp3:
        mdir = Path(tmp3) / "telemetry"
        mdir.mkdir()
        trunc_text = "\n".join([
            _ev("I", "core.flow/state_enter", 880, {"from": "TitleState", "to": "GameState"}),
            _ev("I", "sample/buy_item", 881, {"id": 7, "n": 1}),
        ]) + "\n"
        # 文件写入顺序 = mtime 升序 = 会话的真实先后。故意让 mid.log 里塞两段会话，
        # 这样「最近 2 段会话」与「最近 2 个文件」会给出不同答案，--last 的语义才测得出来。
        plan = [
            ("0trunc.log", trunc_text.encode("utf-8")),                     # 缺 session_start
            ("empty.log", b""),                                             # 空文件
            ("broken.log", b"\xff\xfe\x00 garbage\n"
                           b'[Game][T] E core.asset/load_failed | {"t":1,"s":2,\n'),  # 损坏
            ("m1aaaaaa.log", _session_text("m1aaaaaa").encode("utf-8")),    # 1 段，干净
            ("mid.log", (_session_text("m2bbbbbb")
                         + _session_text("m3cccccc", with_error=True)).encode("utf-8")),  # 2 段
            ("m4dddddd.log", _session_text("m4dddddd", with_error=True).encode("utf-8")),  # 1 段
        ]
        base = time.time() - 6000
        for i, (name, blob) in enumerate(plan):
            p = mdir / name
            p.write_bytes(blob)
            os.utime(p, (base + i * 60, base + i * 60))
        (mdir / "20260916-0130.md").write_text("# 埋点分析报告\n", encoding="utf-8")  # 报告落在同一目录
        (mdir / "notes.txt").write_text("随手记\n", encoding="utf-8")
        (mdir / "sub").mkdir()

        files = mirror_files(mdir)
        check("镜像目录只认 .log（同目录的报告 .md / .txt / 子目录全忽略）",
              [p.name for p in files] == [n for n, _ in plan], str([p.name for p in files]))
        check("镜像文件按最后写入时刻升序排（最老在前，跨文件排序就靠它）",
              files[0].name == "0trunc.log" and files[-1].name == "m4dddddd.log")

        src_m, origin_m, note_m = locate_source("auto", 64, pointer_info={}, mirror_dir=mdir)
        check("定位顺序：镜像目录有 .log 时排第一（origin=mirror，默认吃整个目录）",
              origin_m == "mirror" and src_m.kind == "mirror" and len(src_m) == len(plan),
              "%s / %d 个文件" % (origin_m, len(src_m)))
        check("……定位说明里注明了目录与文件数",
              str(mdir) in note_m and "%d 个文件" % len(plan) in note_m, note_m)
        src_alias, origin_alias, _ = locate_source("mirror", 64, mirror_dir=mdir)
        check("--source mirror 别名显式指定镜像目录",
              origin_alias == "mirror" and len(src_alias) == len(plan))
        src_dir, origin_dir, _ = locate_source(str(mdir), 64)
        check("--source <目录> 也按镜像目录吃（显式来源）",
              src_dir.kind == "mirror" and origin_dir == "explicit" and len(src_dir) == len(plan))

        ana_all, scan_all = analyze_mirror(src_m, 10, "")
        sids_all = [s.sid for s in ana_all.sessions]
        check("镜像目录多文件跨会话聚合：5 段会话（含 1 段截断）按先后排好",
              sids_all == ["(截断)", "m1aaaaaa", "m2bbbbbb", "m3cccccc", "m4dddddd"], str(sids_all))
        check("一个文件里不止一段会话时照常切段（mid.log 切出 m2 / m3 两段）",
              sum(1 for s in ana_all.sessions
                  if s.src is not None and s.src.name == "mid.log") == 2)
        check("缺 session_start 的文件仍标成截断段，不并进后面那次运行",
              ana_all.sessions[0].truncated and ana_all.sessions[0].src.name == "0trunc.log")
        check("每段会话都追溯得到来源文件（query 靠它知道去哪份取窗口）",
              all(s.src is not None for s in ana_all.sessions))
        check("空文件 / 损坏文件不影响其它文件（照样 5 段，坏行只计进 malformed）",
              len(ana_all.sessions) == 5 and ana_all.malformed >= 1,
              "malformed=%d" % ana_all.malformed)

        clusters_m = build_clusters(ana_all.all_errors())
        load_m = next((c for c in clusters_m if "InvalidKeyException" in c.sample.msg), None)
        check("错误聚类跨文件计数：同一错误分处两个文件的两段会话，归并成 ×2 / 2 段会话",
              load_m is not None and load_m.count == 2 and len(load_m.sessions) == 2,
              "" if load_m is None else "×%d / %d 段" % (load_m.count, len(load_m.sessions)))

        args_m = _Args(max_lines=DEFAULT_MAX_LINES, top=DEFAULT_TOP, pre=DEFAULT_PRE,
                       module="", source="mirror")
        text_m = render_summary(ana_all, scan_all, args_m,
                                locate=("mirror", note_m, "工程身份: 编辑器镜像目录，不校验"))
        check("摘要头部注明用的是镜像目录、吃了几个文件",
              "编辑器镜像目录" in text_m and "%d 个文件" % len(plan) in text_m)
        check("成功／失败对照在多文件输入下真的跑起来（不是「没有可对照的样本」）",
              "成功／失败对照（" in text_m and "跳过：" not in text_m)
        check("会话清单带出每段会话来自哪个文件",
              "mid.log" in text_m and "m4dddddd.log" in text_m)

        ana_last2, _ = analyze_mirror(src_m, 2, "")
        check("--last N 跨文件取最近 N **段会话**，不是最近 N 个文件",
              [s.sid for s in ana_last2.sessions] == ["m3cccccc", "m4dddddd"],
              str([s.sid for s in ana_last2.sessions]))
        ana_last3, _ = analyze_mirror(src_m, 3, "")
        check("……--last 3 时把同一个文件里更早那段（mid.log 的 m2）也带上",
              [s.sid for s in ana_last3.sessions] == ["m2bbbbbb", "m3cccccc", "m4dddddd"],
              str([s.sid for s in ana_last3.sessions]))

        # --tail-mb 对镜像不生效：前部有埋点、后面压 2MB 噪声，1MB 尾窗也必须扫得到
        big = mdir / "m5eeeeee.log"
        big.write_text(_session_text("m5eeeeee"), encoding="utf-8")
        _pad_noise(big, 2 * 1024 * 1024)
        os.utime(big, (base + 600, base + 600))
        src_big = SourceSet(mirror_files(mdir), "mirror", mdir)
        ana_big, scan_big, fb_big, _ = scan_and_analyze(src_big, 1, 10, "")
        check("--tail-mb 对镜像文件不生效（每份整读）：1MB 尾窗也扫得到 2MB 噪声前面的会话",
              any(s.sid == "m5eeeeee" for s in ana_big.sessions)
              and fb_big is None and not scan_big.partial)

        order_named = mirror_files_for_session(src_big, "m4dddddd")
        check("query 按 sid 先命中同名的 <sid>.log（不用把整个目录翻一遍）",
              order_named[0].name == "m4dddddd.log"
              and len(order_named) == len(mirror_files(mdir)))
        order_inner = mirror_files_for_session(src_big, "m3cccccc")
        check("……sid 与文件名对不上（m3 藏在 mid.log 里）时退回全目录扫，仍找得到",
              len(order_inner) == len(mirror_files(mdir)))
        qm = _Args(session="m3cccccc", around=5, window=2, module="", event="", grep="",
                   level="", limit=50)
        rows_m, seen_m = [], []
        for p in order_inner:
            _, ls = open_log(p, MIRROR_TAIL_MB)
            rows_m.extend(run_query(ls, qm, quiet_empty=True, seen_out=seen_m))
        check("query 在镜像目录里按 sid + 序号窗口取回 m3cccccc 的行，不混进别的会话",
              rows_m and all("m2bbbbbb" not in r and "m4dddddd" not in r for r in rows_m),
              "%d 行" % len(rows_m))
        check("……quiet_empty 下没命中的文件不各打一遍「没有命中」，sid 攒回 seen_out",
              "没有命中。" not in rows_m and "m1aaaaaa" in seen_m, str(seen_m))

        # 目录不存在 / 只有报告 → 正确退化到下一档
        empty_dir = Path(tmp3) / "nothing_here"
        check("镜像目录不存在时 mirror_files 给空列表、不抛异常", mirror_files(empty_dir) == [])
        check("……退化理由说「还没在编辑器里跑过游戏」",
              "还没在编辑器里跑过游戏" in mirror_fallback_reason(empty_dir))
        md_only = Path(tmp3) / "md_only"
        md_only.mkdir()
        (md_only / "x.md").write_text("报告", encoding="utf-8")
        check("目录在但只有 .md 报告时也算不可用（要退到下一档，不能空转）",
              mirror_files(md_only) == [] and "没有 .log" in mirror_fallback_reason(md_only))

        info_none = read_pointer_file(empty_dir / "nope.txt")
        try:
            _src_fb, origin_fb, note_fb = locate_source(
                "auto", 64, pointer_info=info_none, mirror_dir=empty_dir)
            ok_fb = (origin_fb == "guess" and "镜像目录不存在" in note_fb
                     and "指针文件不存在" in note_fb)
            detail_fb = note_fb
        except LogError as exc:
            # 这台机器上连 Editor.log 都没有：能走到「猜默认路径」那一档并给出人话错误，同样算正确退化
            ok_fb, detail_fb = "没有编辑器日志" in str(exc), str(exc).splitlines()[0]
        check("镜像目录不存在 + 指针也不可用 → 正确退化到「猜默认路径」那一档", ok_fb, detail_fb)

    # ── 跨文件排序：优先 session_start 的 at（墙钟），缺失才回退文件 mtime ──────
    # 为什么非要 at：mtime 会被拷贝 / 同步 / 备份 / git checkout 改掉。下面「矛盾」那一组
    # 就是照这个造的——mtime 顺序整个反过来，只有 at 还能排对。
    with tempfile.TemporaryDirectory(prefix="telemetry-selftest-at-") as tmp4:
        root4 = Path(tmp4)
        base4 = time.time() - 9000

        def _iso(off):
            """相对 base4 的墙钟时刻，写成带时区的 ISO8601（与运行时写的 at 同形状）。"""
            return datetime.fromtimestamp(base4 + off).astimezone().isoformat(timespec="seconds")

        def _mk_mirror(root, items):
            """items: [(文件名, 文本, mtime 相对偏移秒)] → 造好目录、设好 mtime，给 SourceSet。"""
            root.mkdir(parents=True, exist_ok=True)
            for name, body, off in items:
                f = root / name
                f.write_text(body, encoding="utf-8")
                os.utime(f, (base4 + off, base4 + off))
            return SourceSet(mirror_files(root), "mirror", root)

        # A) 全部有 at
        src_a = _mk_mirror(root4 / "all_at", [
            ("s1.log", _session_text("s1aaaaaa", at=_iso(0)), 0),
            ("s2.log", _session_text("s2bbbbbb", at=_iso(600)), 600),
            ("s3.log", _session_text("s3cccccc", at=_iso(1200)), 1200),
        ])
        ana_a, scan_a = analyze_mirror(src_a, 10, "")
        check("全部有 at 时按 at 排序",
              [s.sid for s in ana_a.sessions] == ["s1aaaaaa", "s2bbbbbb", "s3cccccc"]
              and ana_a.order_basis == (3, 0),
              "%s / %s" % ([s.sid for s in ana_a.sessions], ana_a.order_basis))
        check("……排序依据写「按 at」且不带不可靠警告",
              "按 session_start 的 at" in order_basis_note(ana_a.order_basis)
              and "⚠" not in order_basis_note(ana_a.order_basis),
              order_basis_note(ana_a.order_basis))
        text_a = render_summary(ana_a, scan_a, _Args(
            max_lines=DEFAULT_MAX_LINES, top=DEFAULT_TOP, pre=DEFAULT_PRE, module="", source="mirror"))
        check("摘要头部打出这次的会话排序依据",
              "会话排序:" in text_a and "按 session_start 的 at" in text_a)

        # B) at 与 mtime 顺序矛盾 —— 以 at 为准
        src_c = _mk_mirror(root4 / "conflict", [
            ("c1.log", _session_text("c1aaaaaa", at=_iso(0)), 900),     # 最早的会话，mtime 却最新
            ("c2.log", _session_text("c2bbbbbb", at=_iso(600)), 600),
            ("c3.log", _session_text("c3cccccc", at=_iso(1200)), 300),  # 最新的会话，mtime 却最老
        ])
        check("（前提）这组样例的文件 mtime 顺序确实是反的",
              [p.name for p in mirror_files(root4 / "conflict")] == ["c3.log", "c2.log", "c1.log"],
              str([p.name for p in mirror_files(root4 / "conflict")]))
        ana_c, _ = analyze_mirror(src_c, 10, "")
        check("at 与 mtime 矛盾时以 at 为准（拷贝 / 同步把 mtime 改乱也排得对）",
              [s.sid for s in ana_c.sessions] == ["c1aaaaaa", "c2bbbbbb", "c3cccccc"],
              str([s.sid for s in ana_c.sessions]))
        ana_c2, _ = analyze_mirror(src_c, 2, "")
        check("……--last N 也跟着 at 走：取 at 最新的两段，不是 mtime 最新的两段",
              [s.sid for s in ana_c2.sessions] == ["c2bbbbbb", "c3cccccc"],
              str([s.sid for s in ana_c2.sessions]))

        # C) 混合：有的会话有 at、有的没有（旧镜像文件 / 截断段），不能整体退化也不能崩
        src_x = _mk_mirror(root4 / "mixed", [
            ("x1.log", _session_text("x1aaaaaa"), 0),                    # 无 at
            ("x2.log", _session_text("x2bbbbbb", at=_iso(600)), 600),    # 有 at
            ("x3.log", _session_text("x3cccccc"), 1200),                 # 无 at
        ])
        ana_x, _ = analyze_mirror(src_x, 10, "")
        check("混合（部分有 at 部分没有）时两种键混排出完整顺序，不崩也不整体退化",
              [s.sid for s in ana_x.sessions] == ["x1aaaaaa", "x2bbbbbb", "x3cccccc"]
              and ana_x.order_basis == (1, 2),
              "%s / %s" % ([s.sid for s in ana_x.sessions], ana_x.order_basis))
        check("……有 at 的那段记下 at_epoch，没有的留 None",
              [s.at_epoch is not None for s in ana_x.sessions] == [False, True, False])
        note_x = order_basis_note(ana_x.order_basis)
        check("……排序依据说清「几段按 at、几段按 mtime 兜底」，并警告兜底那部分不可靠",
              "1 段按 at" in note_x and "2 段没有 at" in note_x and "⚠" in note_x, note_x)

        # D) 全部没有 at（旧镜像文件 / 埋点上线前的日志）：mtime 兜底，但必须明说不可靠
        note_all_mtime = order_basis_note((0, 4))
        check("全部没有 at 时按 mtime 兜底，并明说「不保证可靠」",
              "都没有 at" in note_all_mtime and "不保证可靠" in note_all_mtime, note_all_mtime)
        check("单文件源不打排序那一行（根本不需要跨文件排序）", order_basis_note(None) == "")

        # E) at 写坏了（写了一半 / 不是 ISO8601）不能让排序崩，退回 mtime 兜底
        src_bad = _mk_mirror(root4 / "bad_at", [
            ("b1.log", _session_text("b1aaaaaa", at="不是时间"), 0),
            ("b2.log", _session_text("b2bbbbbb", at=_iso(600)), 600),
        ])
        ana_bad, _ = analyze_mirror(src_bad, 10, "")
        check("at 写坏了（不是 ISO8601）时那一段退回 mtime 兜底，不抛异常",
              [s.sid for s in ana_bad.sessions] == ["b1aaaaaa", "b2bbbbbb"]
              and ana_bad.order_basis == (1, 1),
              "%s / %s" % ([s.sid for s in ana_bad.sessions], ana_bad.order_basis))

    ok = 0
    for passed, name, detail in checks:
        print("  %s %s%s" % ("✓" if passed else "✗", name, ("  → %s" % detail) if detail else ""))
        ok += 1 if passed else 0
    print("\n自检 %d/%d 通过。" % (ok, len(checks)))
    return 0 if ok == len(checks) else 1


# ══════════════════════════════════════════════════════════════════════════
# CLI
# ══════════════════════════════════════════════════════════════════════════

def build_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(
        prog="analyze.py",
        description="埋点日志分析（契约见 docs/telemetry.md）。先 summarize 拿摘要，再 query 定向回读。")
    ap.add_argument("--selftest", action="store_true", help="内置样例自检，不读真实日志")
    sub = ap.add_subparsers(dest="cmd")

    def common(p, with_filters=False, tail_extra=""):
        p.add_argument("--source", default="auto",
                       help="auto（默认：镜像目录→指针文件→显式路径→猜默认路径）"
                            " / mirror（Logs/telemetry 镜像目录，整个目录一起吃）"
                            " / editor / editor-prev / player / player-prev / 日志完整路径（或目录）")
        p.add_argument("--tail-mb", type=int, default=DEFAULT_TAIL_MB,
                       help=("只读**单文件大日志**尾部多少 MB，0 = 全量（默认 %d）；"
                             "镜像目录每份整读，不受它影响" % DEFAULT_TAIL_MB) + tail_extra)
        if with_filters:
            p.add_argument("--module", default="", help="模块名，如 core.asset / sample")

    p_src = sub.add_parser("sources", help="列出能找到的日志源")
    p_src.add_argument("--tail-mb", type=int, default=DEFAULT_TAIL_MB,
                       help="粗扫单文件大日志尾部多少 MB，0 = 全量（默认 %d）；扫到 0 条不会自动全量"
                            "重扫（图快），提示里会标注，需要就自己传 0。镜像目录一律整读"
                            % DEFAULT_TAIL_MB)
    p_src.set_defaults(func=cmd_sources)

    p_sum = sub.add_parser("summarize", help="出结构化摘要（默认子命令）")
    common(p_sum, with_filters=True,
          tail_extra="；扫空（或没扫到 core/session_start）会自动 ×4 扩大重扫到全量，stderr 有进度提示")
    p_sum.add_argument("--last", type=int, default=DEFAULT_LAST,
                       help="只看最近 N **段会话**（默认 %d）；镜像目录下跨文件按会话先后取，"
                            "不是「最近 N 个文件」" % DEFAULT_LAST)
    p_sum.add_argument("--pre", type=int, default=DEFAULT_PRE,
                       help="错误前序取前几条事件（默认 %d）" % DEFAULT_PRE)
    p_sum.add_argument("--top", type=int, default=DEFAULT_TOP,
                       help="各类排行取前几名（默认 %d）" % DEFAULT_TOP)
    p_sum.add_argument("--max-lines", type=int, default=DEFAULT_MAX_LINES,
                       help="摘要行数上限（默认 %d）" % DEFAULT_MAX_LINES)
    p_sum.set_defaults(func=cmd_summarize)

    p_q = sub.add_parser("query", help="定向回读原始行（验证假设用）")
    common(p_q, with_filters=True)
    p_q.add_argument("--session", default="", help="只看这个 sid")
    p_q.add_argument("--around", type=int, default=None, help="以这个序号 s 为中心")
    p_q.add_argument("--window", type=int, default=30, help="窗口半径（默认 30）")
    p_q.add_argument("--event", default="", help="事件名，如 load_failed")
    p_q.add_argument("--grep", default="", help="正则，对整行原始文本匹配（原生报错靠它捞）")
    p_q.add_argument("--level", default="", choices=["", "D", "I", "W", "E"], help="级别")
    p_q.add_argument("--limit", type=int, default=120, help="最多打多少行（默认 120）")
    p_q.set_defaults(func=cmd_query)
    return ap


def main(argv) -> int:
    """`summarize` 是默认子命令：不写子命令名时补上，直接甩一个日志路径也认。"""
    _utf8_stdio()  # argparse 的 --help 也要中文不乱码，所以在解析前就切
    if "--selftest" in argv:
        return cmd_selftest()
    argv = list(argv)
    known = {"sources", "summarize", "query"}
    if not argv:
        argv = ["summarize"]
    elif argv[0] in ("-h", "--help"):
        pass
    elif argv[0] not in known:
        if not argv[0].startswith("-") and Path(argv[0]).expanduser().exists():
            argv = ["summarize", "--source"] + argv
        else:
            argv = ["summarize"] + argv
    args = build_parser().parse_args(argv)
    if not getattr(args, "func", None):
        build_parser().print_help()
        return 0
    return args.func(args)


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except LogError as exc:
        _utf8_stdio()
        print("[analyze] %s" % exc, file=sys.stderr)
        sys.exit(1)
    except KeyboardInterrupt:
        sys.exit(130)
    except BrokenPipeError:
        sys.exit(0)
