#!/usr/bin/env python3
"""钩子自测的公共件：加载钩子模块、跑子进程、收拾缓存。

钩子文件名带连字符（`required-reads.py`），不是合法模块名，`import` 不进来，
所以 L1 测试统一走 `load_hook()`（importlib 按路径加载）。
"""
from __future__ import annotations

import importlib.util
import json
import os
import shutil
import subprocess
import sys
import uuid
from pathlib import Path

# 本文件在 <root>/.claude/hooks/tests/ 下，往上三层是工程根。不写死绝对路径。
ROOT = Path(__file__).resolve().parents[3]
HOOKS = ROOT / ".claude" / "hooks"
CACHE = ROOT / ".claude" / ".cache"

# 让被测钩子里的 `from _hook_common import should_emit` 能解析到
if str(HOOKS) not in sys.path:
    sys.path.insert(0, str(HOOKS))

#: 测试用的会话 id 前缀，收尾时按它清缓存
SID_PREFIX = "hooktest-"


def load_hook(stem: str):
    """按文件名加载 `.claude/hooks/<stem>.py`，返回模块对象。"""
    path = HOOKS / (stem + ".py")
    name = "hook_" + stem.replace("-", "_")
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ImportError("加载不了钩子：%s" % path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


def new_sid() -> str:
    """每个用例一个独立会话 id，用例之间不共享缓存。"""
    return SID_PREFIX + uuid.uuid4().hex[:8]


def node_exe() -> str | None:
    return shutil.which("node")


def run_py_hook(stem: str, payload: dict, timeout: int = 60) -> tuple[int, str, str]:
    """子进程跑 Python 钩子，喂真实 stdin JSON。返回（退出码, stdout, stderr）。"""
    return _run([sys.executable, str(HOOKS / (stem + ".py"))],
                json.dumps(payload, ensure_ascii=False).encode("utf-8"), timeout)


def run_py_raw(stem: str, raw: str, timeout: int = 60) -> tuple[int, str, str]:
    """喂原始 stdin（空串、坏 JSON）给 Python 钩子，验 fail-open。"""
    return _run([sys.executable, str(HOOKS / (stem + ".py"))],
                raw.encode("utf-8"), timeout)


def run_js_hook(stem: str, payload: dict, timeout: int = 60) -> tuple[int, str, str]:
    node = node_exe()
    if not node:
        raise RuntimeError("没装 node")
    return _run([node, str(HOOKS / (stem + ".js"))],
                json.dumps(payload, ensure_ascii=False).encode("utf-8"), timeout)


#: require 钩子脚本、调用导出的纯函数；参数与返回值都走 JSON（stdin 进、stdout 出），避开命令行引号转义
_JS_CALL = (
    "const m=require(process.argv[1]);let r='';process.stdin.setEncoding('utf8');"
    "process.stdin.on('data',c=>r+=c).on('end',()=>{"
    "process.stdout.write(JSON.stringify(m[process.argv[2]](...JSON.parse(r))));});"
)


def call_js(stem: str, func: str, *args):
    """L1 测 JS 钩子的判据：`call_js("guard", "gitSubcommands", "git -C x commit")`。"""
    node = node_exe()
    if not node:
        raise RuntimeError("没装 node")
    code, out, err = _run([node, "-e", _JS_CALL, str(HOOKS / (stem + ".js")), func],
                          json.dumps(list(args), ensure_ascii=False).encode("utf-8"), 60)
    if code != 0:
        raise RuntimeError("调用 %s.%s 失败：%s" % (stem, func, err.strip()))
    return json.loads(out) if out.strip() else None


def _run(cmd: list[str], data: bytes, timeout: int) -> tuple[int, str, str]:
    env = dict(os.environ)
    # 父会话的 CLAUDE_SESSION_ID 会污染「没传 session_id」的用例，清掉
    env.pop("CLAUDE_SESSION_ID", None)
    proc = subprocess.run(
        cmd, input=data, capture_output=True, timeout=timeout, cwd=str(ROOT), env=env,
    )
    return (
        proc.returncode,
        proc.stdout.decode("utf-8", "replace"),
        proc.stderr.decode("utf-8", "replace"),
    )


def edit_payload(sid: str, file_path: str, tool: str = "Edit", event: str = "PreToolUse") -> dict:
    return {
        "session_id": sid,
        "hook_event_name": event,
        "tool_name": tool,
        "tool_input": {"file_path": file_path},
    }


def bash_payload(sid: str, command: str, event: str = "PostToolUse") -> dict:
    return {
        "session_id": sid,
        "hook_event_name": event,
        "tool_name": "Bash",
        "tool_input": {"command": command},
    }


def cleanup() -> None:
    """删掉本次测试写出的缓存。删不掉就算了，缓存脏只影响下次测试的噪音。"""
    targets = list(CACHE.glob("*" + SID_PREFIX + "*"))
    reads = CACHE / "reads"
    if reads.is_dir():
        targets += list(reads.glob("*" + SID_PREFIX + "*"))
    # 空 stdin 的用例拿不到 session_id，会退化成 unknown，一并收拾掉
    unknown = CACHE / "emitted-unknown.txt"
    if unknown.is_file():
        targets.append(unknown)
    for p in targets:
        try:
            p.unlink()
        except Exception:  # noqa: BLE001
            pass
