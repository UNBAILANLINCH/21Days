#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""detect —— 文档新鲜度提醒（generate-doc 的配套钩子）。

## 干什么

PostToolUse 形态：改完 `Assets/_Project/Scripts/Runtime/<Module>/*.cs` 之后，
如果这个模块**已经有 module-guide**，就提醒一句「涉及公开接口或架构的话，
跑 /generate-doc sync <模块> 同步一下」。

只提醒，从不阻断 —— 改个私有字段也弹「去更文档」是噪音，
所以它只在**文档已存在**时才开口：文档都没有，谈不上过期。

## 默认不注册

`.claude/settings.json` 里**没有**挂这个钩子。工程现在是空骨架，一个模块都没有，
挂上去只有噪音没有信号。等第一个模块的 guide 写出来之后，再把下面这段加进
`.claude/settings.json` 的 `PostToolUse` -> `matcher: "Edit|Write|MultiEdit"` 数组里：

    {
      "type": "command",
      "command": "python \"$CLAUDE_PROJECT_DIR/.claude/skills/generate-doc/detect.py\"",
      "timeout": 10,
      "statusMessage": "detect 提醒同步模块文档"
    }

用法（手动跑）：python detect.py < payload.json
无需提醒时静默 exit 0。
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
REGISTRY = Path(__file__).with_name("modules.json")


def _reconfigure_utf8() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
    except Exception:  # noqa: BLE001
        pass


def norm(path: str) -> str:
    return str(path).replace("\\", "/")


def load_modules() -> dict:
    try:
        data = json.loads(REGISTRY.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    modules = data.get("modules")
    return modules if isinstance(modules, dict) else {}


def main() -> int:
    _reconfigure_utf8()
    try:
        # ⚠ 从 buffer 读再按 UTF-8 解码：Windows 下 sys.stdin 按系统 ANSI(GBK) 解，
        #   负载里的中文路径会被读坏。
        raw = sys.stdin.buffer.read().decode("utf-8", "replace")
    except Exception:  # noqa: BLE001
        return 0
    if not raw.strip():
        return 0
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        return 0

    fp = (payload.get("tool_input") or {}).get("file_path")
    if not fp:
        return 0
    npath = norm(fp)
    if not npath.lower().endswith(".cs"):
        return 0

    for name, info in load_modules().items():
        if not isinstance(info, dict):
            continue
        src = norm(info.get("src") or "")
        if not src:
            continue
        if f"/{src}/" not in npath and not npath.endswith(f"/{src}") and not npath.startswith(f"{src}/"):
            continue
        guide = ROOT / (info.get("docs") or "") / f"{name}-module-guide.md"
        if guide.is_file():
            print(
                f"[generate-doc] 改动了 {name} 模块的运行时代码。"
                f"若涉及公开接口、依赖方向或数据结构，跑 /generate-doc sync {name} 把文档同步上。"
            )
        return 0  # 一个文件只属于一个模块，匹配上就收工
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        sys.exit(0)  # fail-open：提醒钩子出错不该阻断写入
