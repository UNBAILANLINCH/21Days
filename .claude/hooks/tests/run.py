#!/usr/bin/env python3
"""钩子自测入口：`python .claude/hooks/tests/run.py`

两级：
  · `test_l1_units.py` —— 纯函数（判据算得对不对）
  · `test_l2_e2e.py`   —— 子进程喂真实 stdin JSON（钩子被调起来时行为对不对）

全过 exit 0，有失败 exit 1。`/gc`（`.claude/skills/evolution/gc_scan.py`）会跑它 ——
这是这套测试的**执行载体**：钩子坏了不会报错，只会悄悄不生效，得有人定期问一声。

状态锚点：跑一次看末行 `OK`。退场条件：钩子本身被删时，连同它的用例一起删。
"""
from __future__ import annotations

import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

for _s in (sys.stdout, sys.stderr):
    try:  # Windows 默认 cp936，用例名和中文断言消息会乱码
        _s.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
    except Exception:  # noqa: BLE001
        pass


def main() -> int:
    suite = unittest.TestLoader().discover(
        start_dir=str(HERE), pattern="test_*.py", top_level_dir=str(HERE))
    result = unittest.TextTestRunner(verbosity=2, stream=sys.stdout).run(suite)
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    sys.exit(main())
