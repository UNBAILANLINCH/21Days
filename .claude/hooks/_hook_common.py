#!/usr/bin/env python3
"""钩子公共件：同会话提示去重。

## 为什么只放去重这一件事

钩子之间不共享内存、只共享缓存文件（见 README「一览」末段），于是「同一条提示
在同一会话里被注入两遍」是每个注入型钩子都会独立犯的同一个错。重复注入不是无害的：
每轮多塞几行，塞到最后模型对所有提示一起脱敏（context rot），真正该看的那条也失效。
这件事值得只写一遍。

其余的 `_payload()` / `_rel()` / `_session_id()` 故意**不**抽出来：它们每个只有十几行，
抽走省不下多少，却让单个钩子不再能被独立读懂——钩子是出事时最需要「打开就看明白」的东西。

## key 是提示文本本身，不是文件名

`knowledge-routing.py` 早先按「文件名」去重，于是同一个文件的**另一条**提示
（规则集变了、模块 guide 刚生成出来）会被旧记录吃掉，而换汤不换药的同一条提示
换个文件名又会重说一遍。按实际文本哈希去重，两头都对。

## 用法

    from _hook_common import should_emit   # 同目录；钩子由 `python <绝对路径>` 直接跑，
                                           # sys.path[0] 就是 .claude/hooks/

    if should_emit(sid, text, "doom-loop"):
        print(text, file=sys.stderr)

**deny / ask 不要用它**：拒绝和确认是决策不是提示，每次都必须重新报——
去重掉的那次就是护栏漏掉的那次。
"""
from __future__ import annotations

import hashlib
from pathlib import Path

# 工程根：本文件在 <root>/.claude/hooks/ 下，往上两层。不写死绝对路径。
ROOT = Path(__file__).resolve().parents[2]
CACHE_DIR = ROOT / ".claude" / ".cache"


def fingerprint(text: str, tag: str = "") -> str:
    """提示文本的指纹：`<标签>:<sha1 前 16 位>`。

    带标签只为出事时一眼看出是谁写的；跨钩子撞文本的概率本来就约等于零。
    """
    digest = hashlib.sha1(str(text).encode("utf-8", "replace")).hexdigest()[:16]
    return "%s:%s" % (str(tag or "-"), digest)


def emitted_file(session_id: str) -> Path:
    """本会话已注入提示的指纹账本。随时可删，删了最多多说一遍。"""
    return CACHE_DIR / ("emitted-%s.txt" % (session_id or "unknown"))


def should_emit(session_id: str, text: str, tag: str = "") -> bool:
    """这条提示本会话还没说过 → True，并就地记账；说过 → False。

    fail-open 的方向是**宁可多说一遍**：账本读不了 / 写不了时返回 True。
    反过来（出错就当说过）会让提示静默消失，而提示消失是没人会发现的故障。
    """
    key = fingerprint(text, tag)
    fp = emitted_file(session_id)
    try:
        seen = set(fp.read_text(encoding="utf-8").splitlines())
    except Exception:  # noqa: BLE001
        seen = set()
    if key in seen:
        return False
    try:
        fp.parent.mkdir(parents=True, exist_ok=True)
        with fp.open("a", encoding="utf-8") as f:  # 追加：并行的钩子互不覆盖
            f.write(key + "\n")
    except Exception:  # noqa: BLE001
        pass  # 记不下就下次再说一遍，不该因此吞掉这一次
    return True
