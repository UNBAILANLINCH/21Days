#!/usr/bin/env python3
"""必读文档闸：一个文件三个入口。

| 事件 | 匹配的工具 | 做什么 |
| --- | --- | --- |
| PostToolUse | Read | 把读过的文件记进**本会话**的已读账本（只追加的 JSONL） |
| PostToolUse | Bash | 从 `cat` / `head` / `tail` / `sed -n` / `less` / `type` 命令里解析出被读的文件，同样记账 |
| PreToolUse | Edit / Write / MultiEdit | 查目标文件的必读项读过没有，缺了就 deny |

配置在 `.claude/hooks/required_reads.json`：键是 glob（相对工程根、正斜杠），
值是必读文件列表，支持 `${seg:N}` / `${seg:N:lower}` 占位符（见该文件的 `_说明`）。

设计取舍（照抄不动的三条）：
  1. **必读文件不存在就跳过这一项**，否则是死锁：文件没建出来时它永远不在
     「读过」集合里，于是永远拦、而且读不到，人只能去删配置。
     跳过它，文件一旦建出来闸自动开始生效——新模块建目录当天不会误拦。
  2. **累加所有命中的 pattern**，不是第一个命中就返回。一个文件可能同时落进
     多条规则，先命中谁取决于 JSON 键顺序，那等于另一条规则被静默吃掉。
  3. **写文档不要求先读它自己**：目标就是必读文件本身，或目标是
     `README.md` / `SKILL.md` / `*-module-guide.md` 时豁免。
  4. **记账入口要覆盖所有真实的读法**。这是这道闸能不能硬拦的**全部前提**：
     强制机制的成本不在「拦不拦得住」，在「解锁链路可不可靠」。
     早先只认 Read 工具，于是用 `cat` 读完 guide 再编辑照样被拒——读了也解不开锁，
     反复被拒会把 Agent 推进「换个写法再试」的自主重试循环，最后只能把闸降级成软提示。
     解锁必须是**人/模型打一条确定命令就能完成**的动作，不能靠钩子去推断状态。
     Bash 解析取**保守**策略：只认明确的读取命令，宁可漏记（代价是多读一次）
     也不误记（代价是闸形同虚设）。

本钩子是整套钩子里**唯一会拦**的：只有「明确判定缺必读」才出 deny，
其余一切情况（stdin 空、JSON 坏、配置坏、自身异常）一律静默 exit 0。
"""
from __future__ import annotations

import datetime
import fnmatch
import json
import os
import re
import sys
from pathlib import Path

# 工程根：本文件在 <root>/.claude/hooks/ 下，往上两层。不写死绝对路径。
ROOT = Path(__file__).resolve().parents[2]
CONF = ROOT / ".claude" / "hooks" / "required_reads.json"
READS_DIR = ROOT / ".claude" / ".cache" / "reads"

EDIT_TOOLS = ("Edit", "Write", "MultiEdit")
# 写这些文档时不要求先读必读项：写的就是文档本身，闸会变成自锁。
EXEMPT_NAMES = ("README.md", "SKILL.md")
EXEMPT_SUFFIX = "-module-guide.md"

_SEG_RE = re.compile(r"\$\{seg:(\d+)(?::(lower|upper))?\}")

# Bash 里算「读过」的命令。故意不含 grep / rg：它们只吐命中行，看到的不是这份文档。
BASH_READ_CMDS = ("cat", "head", "tail", "less", "more", "type", "sed")
# 出现这些就整条命令都不记账：重定向、命令替换、tee 都可能把内容写去别处而不是摆到眼前。
_BASH_WRITEY_RE = re.compile(r">|`|\$\(|\btee\b")
# 语句分隔符。`\|\|` 必须排在前面，否则 `a || b` 会被当成管道。
_STMT_SPLIT_RE = re.compile(r"\|\||&&|[;\n\r]")
# 丢 stderr 的重定向先摘掉再判断：`cat x.md 2>/dev/null` 内容照样摆在眼前，
# 不摘的话它会被下面的「有 > 就不记」一刀切掉，而这是最常见的读法之一。
_STDERR_REDIR_RE = re.compile(r"\s2>\s*(?:/dev/null|&1)")
# 极简分词：认成对引号包住的整体，其余按空白切。不用 shlex —— 它的 posix 模式会把
# Windows 路径里的反斜杠当转义吃掉，`E:\proj\a.md` 会变成 `E:proja.md`。
_TOKEN_RE = re.compile(r"\"([^\"]*)\"|'([^']*)'|(\S+)")


def _utf8_stdio() -> None:
    """Windows 下 stdout/stderr 默认走 cp936，中文提示会乱码。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def _payload() -> dict:
    """读 stdin 里的 hook 负载。空 / 坏 JSON 都返回空 dict，让调用方静默放行。"""
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

    按天是最后的兜底：同一天的多个会话会共享记账，于是「上个会话读过这个会话
    就不用读了」——而新会话上下文是空的，正是最该重读的时候。所以优先取真 id。
    """
    sid = str(payload.get("session_id") or "").strip()
    if not sid:
        sid = str(os.environ.get("CLAUDE_SESSION_ID") or "").strip()
    if not sid:
        sid = datetime.datetime.now().strftime("%Y%m%d")
    return re.sub(r"[^\w.\-]", "_", sid)[:100] or "unknown"


def _rel(raw_path: str) -> str:
    """归一成相对工程根的正斜杠路径，好跟配置里的 glob 比。

    hook 负载里的 Windows 路径可能是 `X:\\dir\\file.cs` 形态；相对路径直接用，
    **不做 resolve()**——resolve 会按当前工作目录展开，而钩子的 cwd 不保证是工程根。
    """
    s = str(raw_path or "").replace("\\", "/").strip()
    if not s:
        return ""
    while s.startswith("./"):
        s = s[2:]
    root = ROOT.as_posix()
    if s.lower().startswith(root.lower() + "/"):
        return s[len(root) + 1:]
    if re.match(r"^[A-Za-z]:/", s) or s.startswith("/"):
        # 绝对路径但前缀对不上（大小写/短名/符号链接），再试一次 resolve
        try:
            rp = Path(s).resolve().as_posix()
        except Exception:  # noqa: BLE001
            return s
        if rp.lower().startswith(root.lower() + "/"):
            return rp[len(root) + 1:]
        return s  # 工程根以外的文件，不会命中任何 glob
    return s


def _expand(text: str, rel: str) -> str:
    """把 `${seg:N}` / `${seg:N:lower}` 换成目标路径的第 N 段（0 起）。

    一条规则覆盖现有和将来所有模块，**新建模块自动带闸**；逐模块手写必漏。
    段数不够时原样保留，于是展开后的路径不存在 → 按「必读文件不存在」跳过。
    """
    def sub(m: re.Match) -> str:
        idx = int(m.group(1))
        parts = rel.split("/")
        if idx >= len(parts):
            return m.group(0)
        seg = parts[idx]
        mode = m.group(2)
        if mode == "lower":
            return seg.lower()
        if mode == "upper":
            return seg.upper()
        return seg

    return _SEG_RE.sub(sub, text)


def _tokens(segment: str) -> list[str]:
    """把一段命令切成 token，成对引号里的内容算一个（路径带空格时用得上）。"""
    out: list[str] = []
    for m in _TOKEN_RE.finditer(segment):
        for g in (m.group(1), m.group(2), m.group(3)):
            if g is not None:
                out.append(g)
                break
    return out


def _bash_read_targets(command: str) -> list[str]:
    """从一条 Bash 命令里解析出「确实被读出来摆到眼前」的文件（相对工程根）。

    只认三件事同时成立的读取：

      1. 命令字是 `cat` / `head` / `tail` / `less` / `more` / `type` / `sed -n`；
      2. 它**不在管道里**——管道下游会过滤或吞掉内容，看到的不是整份文档；
      3. 参数指向的文件**真实存在**——这一条顺手挡掉了 `sed -n` 的脚本参数
         （`1,50p`）、`head -n` 的行数、写错的路径，不用逐个特判。

    任一条不成立就漏记，代价是多读一次；误记的代价是整道闸形同虚设。两边不对称，
    所以一律往保守了判。
    """
    cmd = _STDERR_REDIR_RE.sub(" ", str(command or ""))
    if not cmd.strip() or _BASH_WRITEY_RE.search(cmd):
        return []
    found: list[str] = []
    for stmt in _STMT_SPLIT_RE.split(cmd):
        if "|" in stmt:
            continue  # 进了管道，不算读过整份
        toks = _tokens(stmt)
        if not toks:
            continue
        name = toks[0].replace("\\", "/").rsplit("/", 1)[-1].lower()
        if name.endswith(".exe"):
            name = name[:-4]
        if name not in BASH_READ_CMDS:
            continue
        args = [t for t in toks[1:] if not t.startswith("-")]
        if name == "sed":
            # 没有 -n 就不是在按行读文件（可能是在做替换）；第一个非选项参数是脚本，丢掉
            if not any(re.fullmatch(r"-[A-Za-z]*n[A-Za-z]*", t) for t in toks[1:]):
                continue
            args = args[1:]
        for a in args:
            if not a or any(ch in a for ch in "*?[]"):
                continue  # 通配一次可能命中一堆，不逐个猜
            rel = _rel(a)
            try:
                if not (ROOT / rel).is_file():
                    continue
            except Exception:  # noqa: BLE001
                continue
            if rel not in found:
                found.append(rel)
    return found


def _read_log(sid: str) -> Path:
    """每会话一个只追加的 JSONL：追加不用先读，并行的两个钩子最多写出重复行。"""
    return READS_DIR / (sid + ".jsonl")


def _record_read(sid: str, rel: str) -> None:
    if not rel:
        return
    try:
        fp = _read_log(sid)
        fp.parent.mkdir(parents=True, exist_ok=True)
        with fp.open("a", encoding="utf-8") as f:
            f.write(json.dumps([rel], ensure_ascii=False) + "\n")
    except Exception:  # noqa: BLE001
        pass  # 记不下不该挡住干活


def _already_read(sid: str) -> set[str]:
    """本会话读过的文件（小写集合，比较时大小写不敏感）。"""
    got: set[str] = set()
    fp = _read_log(sid)
    try:
        lines = fp.read_text(encoding="utf-8").splitlines()
    except Exception:  # noqa: BLE001
        return got
    for line in lines:
        try:
            items = json.loads(line)
        except Exception:  # noqa: BLE001
            continue  # 半行只丢它自己
        if isinstance(items, str):
            items = [items]
        if isinstance(items, list):
            got.update(str(x).replace("\\", "/").lower() for x in items if x)
    return got


def _is_exempt_target(rel: str) -> bool:
    name = rel.rsplit("/", 1)[-1]
    return name in EXEMPT_NAMES or name.endswith(EXEMPT_SUFFIX)


def _missing(rel: str, sid: str) -> tuple[list[str], list[str]]:
    """返回（命中的 glob 列表，本会话还没读的必读文件列表）。"""
    try:
        conf = json.loads(CONF.read_text(encoding="utf-8"))
    except Exception:  # noqa: BLE001
        return [], []  # 配置缺失/写坏 → fail-open
    if not isinstance(conf, dict):
        return [], []

    done = _already_read(sid)
    zones: list[str] = []
    miss: list[str] = []
    for pattern, musts in conf.items():
        if str(pattern).startswith("_") or not isinstance(musts, list):
            continue  # `_说明` 这类注释键
        pat = _expand(str(pattern), rel)
        if not fnmatch.fnmatch(rel.lower(), pat.lower()):
            continue
        zones.append(str(pattern))
        for must in musts:
            item = _expand(str(must), rel).replace("\\", "/")
            if not item or item in miss:
                continue
            if item.lower() == rel.lower():
                continue  # 目标就是必读文件本身
            if not (ROOT / item).exists():
                continue  # 必读文件还不存在 → 跳过，否则死锁
            if item.lower() in done:
                continue
            miss.append(item)
    return zones, miss


def _deny(rel: str, zones: list[str], miss: list[str]) -> None:
    """拒绝理由 = 事实陈述 + 解锁办法，两样都不能少。

    写成第三人称陈述句，不写「你必须先读」这类祈使：官方文档明示，钩子注入的
    祈使句会触发模型的 prompt-injection 防御，那段话会被当可疑内容 surface 给用户，
    而不是被当约束执行。
    解锁办法也必须写出来——只说「被拦了」的提示会让人去拆护栏，而不是照做。
    """
    lines = ["%s 的必读文档在本会话没有读取记录：" % rel]
    lines += ["  · " + m for m in miss]
    lines.append("这些文件记着这块的约定与实测坑。")
    lines.append("读取记录来自两处：Read 工具，或 Bash 里的 cat / head / tail / sed -n"
                 "（不进管道、不带重定向时才算）。")
    lines.append("任一种方式读过之后，本会话内这次编辑即可放行，每份文档只要求一次。")
    if zones:
        lines.append("命中的必读规则：" + " · ".join(zones))
    lines.append("规则配置在 .claude/hooks/required_reads.json。")
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": "\n".join(lines),
        }
    }, ensure_ascii=False))


def main() -> int:
    _utf8_stdio()
    payload = _payload()
    if not payload:
        return 0
    event = str(payload.get("hook_event_name") or "").strip()
    tool = str(payload.get("tool_name") or "")
    tool_input = payload.get("tool_input") or {}
    if not isinstance(tool_input, dict):
        tool_input = {}
    sid = _session_id(payload)

    # ── 入口一：记账（PostToolUse + Read）──────────────────────────
    # hook_event_name 缺失时按 tool_name 退化判断，免得一字段之差整条闸失效。
    if tool == "Read" and event in ("PostToolUse", ""):
        _record_read(sid, _rel(tool_input.get("file_path") or ""))
        return 0

    # ── 入口二：记账（PostToolUse + Bash）──────────────────────────
    # 只认 Read 工具的话，用 cat 读完文档再编辑照样被拒 —— 解锁链路断了的闸只能被降级，
    # 不能被遵守。解析保守，宁可漏记（多读一次）也不误记（闸形同虚设）。
    if tool == "Bash" and event in ("PostToolUse", ""):
        for item in _bash_read_targets(tool_input.get("command") or ""):
            _record_read(sid, item)
        return 0

    # ── 入口三：查闸（PreToolUse + Edit/Write/MultiEdit）────────────
    if tool not in EDIT_TOOLS or event not in ("PreToolUse", ""):
        return 0
    rel = _rel(tool_input.get("file_path") or "")
    if not rel or _is_exempt_target(rel):
        return 0
    zones, miss = _missing(rel, sid)
    if miss:
        _deny(rel, zones, miss)
    return 0  # deny 靠 JSON 决定，退出码永远 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        sys.exit(0)  # fail-open：钩子自身的 bug 绝不卡死会话
