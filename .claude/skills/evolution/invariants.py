#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""invariants —— Unity 工程的跨文件静态不变量扫描（`/gc` 的第 5 项检查）。

## 为什么要这个（和 project-lint 不重复在哪）

`project-lint` 是**逐文件、逐行的正则**：它看得见一行代码长什么样，看不见
「这个 namespace 跟它所在的目录对不对得上」「asmdef 的 references 里有没有反向依赖」
「这个 prefab 在 Addressables 里挂的地址是不是类名」。这些约束**跨文件、跨资产**，
单文件正则天生够不着。

而它们恰恰是坏掉以后**不报编译错误、只在运行时静默失效**的那一类：
引用断链、地址找不到面板、Editor 代码混进包体、平台宏渗进玩法层。
静默失效没人发现，才是最贵的。

所以分工是：**能在一行里判完的归 lint，必须把整个仓库摊开才能判的归这里**。
本文件六条检查没有一条能写成 `rules.json` 里的正则。

## 载体锚定（`.claude/rules/harness-authoring.md`）

- **执行载体**：`/gc`。`gc_scan.py` 的第 5 项直接 import 本模块跑一遍。
  `/gc` 是本工程已证明在转的载体（改完 harness 结构、`/review-change` 之前都会跑），
  不另设周期、不另加提醒、不做伴生清单。
- **状态锚点**（5 秒可证伪）：把 `Assets/_Project/Prefabs/UI/TitleView.prefab.meta`
  临时改个名，`python .claude/skills/evolution/invariants.py` 应报 `.meta` 缺失并 exit 1；
  改回来应恢复静默 exit 0。报不出来就说明这套检查已经死了，别信它的沉默。
- **退场条件**：某条检查连续两次是误报（判据够不着真实结构）就**删掉那条**，
  不要加白名单——白名单攒起来以后没人敢动，而带白名单的检查等于没检查。
  整份文件的退场条件是 `Assets/_Project/Scripts/` 不再是这个形状（换结构 / 换引擎），
  届时连同 `gc_scan.py` 第 5 项一起删，而不是留着让它一直报。

## 查六条（每条都注明依据，便于以后判断该不该删）

    1. asmdef 依赖方向        Game.Core 不引用 Runtime/Editor/Tests；Game.Runtime 不引用 Editor/Tests
    2. 平台宏只在 Core/Platform/   Core（除 Platform/）与 Runtime 下不出现 #if UNITY_ANDROID 等平台宏
    3. 裸 using UnityEditor   Core / Runtime 里的 using UnityEditor 必须被 #if UNITY_EDITOR 包住
    4. 命名空间与目录一致      Core/<X>/ -> Game.Core.<X>；Runtime/<M>/ -> Game.<M>
    5. .meta 配对             Assets/_Project/ 下每个实体有 .meta，每个 .meta 有实体
    6. UI 面板地址等于类名     Prefabs/UI/*.prefab 在 Addressables 里有条目且 address == 文件名

第 2 条**放行 `#if UNITY_EDITOR`**：`project-root.md` 明确允许 Runtime 里用它包
调试 / Gizmos。把它一起报了就是误报，而误报会逼人整条关掉。

纯 Python 标准库，只读文本，**不需要 Unity 编辑器开着**、不 import 任何 Unity 相关东西。
无违规时**静默 exit 0**（成功静默、失败冗余）；有违规逐条打印并 exit 1。

用法：python invariants.py [工程根]（省略则从本文件位置往上推）
"""

from __future__ import annotations

import json
import os
import re
import sys
from collections import namedtuple
from pathlib import Path

# ------------------------------------------------------------------ 路径常量

PROJECT_DIR = "Assets/_Project"
SCRIPTS_DIR = PROJECT_DIR + "/Scripts"
CORE_DIR = SCRIPTS_DIR + "/Core"
RUNTIME_DIR = SCRIPTS_DIR + "/Runtime"
PLATFORM_DIR = CORE_DIR + "/Platform"
#: Luban 生成物：namespace 是 `cfg`，由生成器决定，手改会被下次生成覆盖
GENERATED_DIR = CORE_DIR + "/Config/Generated"
UI_PREFAB_DIR = PROJECT_DIR + "/Prefabs/UI"
ADDRESSABLE_GROUPS_DIR = "Assets/AddressableAssetsData/AssetGroups"

# ------------------------------------------------------------------ 依据出处

REF_ASMDEF = "project-root.md #目录与 asmdef 依赖方向 · architecture.md #3 分层与依赖方向"
REF_PLATFORM = "project-root.md #平台差异只在框架层 · architecture.md #3 硬约束"
REF_EDITOR_USING = "project-root.md #目录与 asmdef 依赖方向"
REF_NAMESPACE = "project-root.md #目录约定 · architecture.md #4 目录"
REF_META = "project-root.md #生成物边界 · pitfalls.md #.meta 没提交"
REF_UI_ADDRESS = "architecture.md #5.6 UI（预制体 Addressables key 等于类名）"
FONT_ASSET_MAX_BYTES = 200 * 1024   # Dynamic 字体资产的干净基线是几 KB；超过这个数说明带着运行时字形
REF_FONT_BLOAT = "Art/Fonts/README.md · pitfalls.md #Dynamic 字体资产污染 git"

Problem = namedtuple("Problem", "path line symptom fix ref")


def render(p: Problem) -> str:
    """一条违规渲染成四行：位置 / 现象 / 改法 / 依据。"""
    loc = f"{p.path}:{p.line}" if p.line else p.path
    return (
        f"{loc}\n"
        f"    现象：{p.symptom}\n"
        f"    改法：{p.fix}\n"
        f"    依据：{p.ref}"
    )


# ------------------------------------------------------------------ 小工具


def _reconfigure_utf8() -> None:
    """Windows 的 Python 默认按 cp936 输出，中文会乱码（pitfalls.md 有记）。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def _norm(path) -> str:
    return str(path).replace("\\", "/")


def _rel(root: Path, p: Path) -> str:
    try:
        return _norm(p.relative_to(root))
    except ValueError:
        return _norm(p)


def _read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def _unity_ignored(name: str) -> bool:
    """Unity 自己就不给这些生成 .meta，别拿它们当缺失报。"""
    low = name.lower()
    return name.startswith(".") or name.endswith("~") or low == "cvs" or low.endswith(".tmp")


def _cs_files(root: Path, rel_dir: str):
    """按相对路径顺序产出某目录下的 .cs，输出稳定便于 diff。"""
    base = root / rel_dir
    if not base.is_dir():
        return
    for p in sorted(base.rglob("*.cs")):
        yield p, _rel(root, p)


# ------------------------------------------------------------------ 1. asmdef 依赖方向
# 依据：project-root.md「Game.Runtime 不引用 Editor / Tests」；
#       architecture.md 第 3 节「Game.Core ... 不引用 Runtime / Editor / Tests」。
# 为什么 lint 做不了：references 是 JSON 数组，且可能写成 "GUID:xxxx"，
#   要先把全仓库的 asmdef 与 .meta 的 GUID 对上才知道它指的是谁。

ASMDEF_FORBIDDEN = {
    "Game.Core": ("Game.Runtime", "Game.Editor", "Game.Tests"),
    "Game.Runtime": ("Game.Editor", "Game.Tests"),
}


def _asmdef_guid_map(root: Path) -> dict:
    """GUID（小写）-> asmdef 名。references 里的 "GUID:xxxx" 靠它翻译。"""
    out = {}
    assets = root / "Assets"
    if not assets.is_dir():
        return out
    for meta in assets.rglob("*.asmdef.meta"):
        m = re.search(r"^guid:\s*([0-9a-fA-F]{32})\s*$", _read(meta), re.MULTILINE)
        if not m:
            continue
        asmdef = meta.with_suffix("")  # 去掉 .meta，剩 xxx.asmdef
        try:
            name = json.loads(_read(asmdef)).get("name")
        except (json.JSONDecodeError, AttributeError):
            continue
        if isinstance(name, str) and name:
            out[m.group(1).lower()] = name
    return out


def _line_of(text: str, needle: str) -> int:
    for i, line in enumerate(text.splitlines(), 1):
        if needle in line:
            return i
    return 0


def check_asmdef_direction(root: Path) -> list:
    problems = []
    guid2name = _asmdef_guid_map(root)
    base = root / PROJECT_DIR
    if not base.is_dir():
        return problems
    for asmdef in sorted(base.rglob("*.asmdef")):
        rel = _rel(root, asmdef)
        text = _read(asmdef)
        try:
            data = json.loads(text)
        except json.JSONDecodeError as exc:
            problems.append(Problem(
                rel, 0,
                f"asmdef 不是合法 JSON（{exc.msg}），Unity 会静默忽略它，程序集边界等于不存在",
                "按 JSON 语法修正，或在 Unity 里重新保存这份 asmdef",
                REF_ASMDEF,
            ))
            continue
        name = str(data.get("name") or "")
        banned = ASMDEF_FORBIDDEN.get(name)
        if not banned:
            continue
        for raw in data.get("references") or []:
            if not isinstance(raw, str):
                continue
            target = guid2name.get(raw[5:].lower(), raw) if raw.startswith("GUID:") else raw
            hit = next((b for b in banned if target == b or target.startswith(b + ".")), None)
            if not hit:
                continue
            problems.append(Problem(
                rel, _line_of(text, raw),
                f"{name} 引用了 {target}，依赖方向反了（下层引用了上层不该被它知道的程序集）",
                f"把 references 里的 \"{raw}\" 删掉；确实要用对方的能力，就在下层定接口让上层来实现，"
                f"或把这段逻辑挪进 {name} 自己的目录",
                REF_ASMDEF,
            ))
    return problems


# ------------------------------------------------------------------ 2. 平台宏只在 Core/Platform/
# 依据：project-root.md「平台差异只在框架层」、architecture.md 第 3 节硬约束。
# 为什么 lint 做不了：判据是「这个文件在哪个目录」与「哪个子目录被豁免」的组合，
#   lint 的 path_contains 只能做包含判断，做不了「Core 下但排除 Platform 子目录」。

#: 平台宏表。**UNITY_EDITOR 系刻意不在表里**：
#: project-root.md 明确允许 Runtime 里用 #if UNITY_EDITOR 包调试 / Gizmos。
PLATFORM_SYMBOL_RE = re.compile(
    r"\bUNITY_(?:ANDROID|IOS|IPHONE|TVOS|VISIONOS|WEBGL|WSA\w*|STANDALONE\w*"
    r"|PS4|PS5|PSP2|XBOXONE|GAMECORE\w*|SWITCH|EMBEDDED_LINUX|QNX|LUMIN)\b"
)
PREPROC_COND_RE = re.compile(r"^\s*#\s*(if|elif)\b(.*)$")


def check_platform_macros(root: Path) -> list:
    problems = []
    for rel_dir in (CORE_DIR, RUNTIME_DIR):
        for path, rel in _cs_files(root, rel_dir):
            if rel.startswith(PLATFORM_DIR + "/"):
                continue  # 平台实现的家，唯一允许出现平台宏的地方
            for i, line in enumerate(_read(path).splitlines(), 1):
                m = PREPROC_COND_RE.match(line)
                if not m:
                    continue  # 注释里提到 UNITY_ANDROID 不算，只看真的条件编译行
                sym = PLATFORM_SYMBOL_RE.search(m.group(2))
                if not sym:
                    continue
                problems.append(Problem(
                    rel, i,
                    f"条件编译用了平台宏 {sym.group(0)}，平台差异渗进了框架 / 玩法层",
                    f"把这段挪进 {PLATFORM_DIR}/ 的某个实现里，对外只暴露与平台无关的 "
                    f"IPlatformService 成员；调用方按接口写，不判平台",
                    REF_PLATFORM,
                ))
    return problems


# ------------------------------------------------------------------ 3. 裸 using UnityEditor
# 依据：project-root.md「Runtime 不 using UnityEditor；确需编辑器逻辑用 #if UNITY_EDITOR 包住」。
# 与 lint 规则 using-unityeditor-in-runtime 的分工：lint 那条是**文件级**判断
#   （整份文件只要出现过 #if UNITY_EDITOR 就整条跳过），且只管 /Scripts/Runtime/。
#   这里是**块级**判断（using 必须真的落在 UNITY_EDITOR 块内），并覆盖 Core。
#   「文件别处有 #if UNITY_EDITOR、这条 using 却在块外」正是 lint 必然放过的那种。

USING_UNITYEDITOR_RE = re.compile(r"^\s*using\s+(?:static\s+)?UnityEditor\b")
#: 前面不能紧跟 ! 或字母，排掉 `!UNITY_EDITOR` 与 `UNITY_EDITOR_WIN` 之类
EDITOR_GUARD_RE = re.compile(r"(?<![!\w])UNITY_EDITOR\b")
PREPROC_ELSE_RE = re.compile(r"^\s*#\s*else\b")
PREPROC_ENDIF_RE = re.compile(r"^\s*#\s*endif\b")


def _editor_guard_scan(text: str):
    """产出 (行号, 行文, 当前是否被 #if UNITY_EDITOR 包着)。

    写成纯函数是为了能直接喂字符串验证，不必造临时文件
    （hook-injection-style.md 第 7 条「判据写成纯函数」的同一条理由）。
    """
    stack = []
    for i, line in enumerate(text.splitlines(), 1):
        m = PREPROC_COND_RE.match(line)
        if m:
            guarded = bool(EDITOR_GUARD_RE.search(m.group(2)))
            if m.group(1) == "if":
                stack.append(guarded)
            elif stack:
                stack[-1] = guarded
            continue
        if PREPROC_ELSE_RE.match(line):
            if stack:
                stack[-1] = False  # #if UNITY_EDITOR 的 else 分支恰恰是非编辑器侧
            continue
        if PREPROC_ENDIF_RE.match(line):
            if stack:
                stack.pop()
            continue
        yield i, line, any(stack)


def check_using_unityeditor(root: Path) -> list:
    problems = []
    for rel_dir in (CORE_DIR, RUNTIME_DIR):
        for path, rel in _cs_files(root, rel_dir):
            for i, line, guarded in _editor_guard_scan(_read(path)):
                if guarded or not USING_UNITYEDITOR_RE.match(line):
                    continue
                problems.append(Problem(
                    rel, i,
                    "using UnityEditor 没有被 #if UNITY_EDITOR 包住，出包时这行会让整个程序集编译不过",
                    "整段（using 加用到它的代码）用 #if UNITY_EDITOR / #endif 包起来，且只放调试与 Gizmos；"
                    f"业务逻辑挪去 {SCRIPTS_DIR}/Editor/",
                    REF_EDITOR_USING,
                ))
    return problems


# ------------------------------------------------------------------ 4. 命名空间与目录一致
# 依据：project-root.md「命名空间与目录一致」、architecture.md 第 4 节目录表。
# 为什么 lint 做不了：期望值由**文件路径**算出来，逐行正则拿不到「这里应该是什么」。

NAMESPACE_LINE_RE = re.compile(r"^\s*namespace\s+([A-Za-z_][\w.]*)\s*[{;]?\s*$")
BLOCK_COMMENT_RE = re.compile(r"/\*.*?\*/", re.DOTALL)
LINE_COMMENT_RE = re.compile(r"//.*$", re.MULTILINE)
TYPE_DECL_RE = re.compile(r"\b(?:class|struct|interface|enum|record|delegate)\s+[A-Za-z_]\w*")


def _declares_type(text: str) -> bool:
    """去掉注释后还有类型声明，才算「这份文件有东西需要归属命名空间」。

    为什么要这一步：`Core/Events/EventConventions.cs` 是**纯注释**的约定文件，
    一个类型都没有，报它缺 namespace 是误报。误报会逼人把整条检查关掉，
    所以判据宁可收紧（harness-authoring.md「lint 频繁误报就收紧或删掉」）。
    """
    return bool(TYPE_DECL_RE.search(LINE_COMMENT_RE.sub("", BLOCK_COMMENT_RE.sub("", text))))


def _expected_namespaces(rel: str):
    """返回 (首选命名空间, 可接受集合)；不在管辖范围内返回 (None, None)。

    可接受集合允许「上卷」到子系统根：`Core/UI/Views/` 里的类写 `Game.Core.UI`
    也算对（架构只要求玩法模块那一层是 `Game.<Module>`，更深的分目录是组织手段）。
    """
    for base_dir, prefix in ((CORE_DIR, "Game.Core"), (RUNTIME_DIR, "Game")):
        if not rel.startswith(base_dir + "/"):
            continue
        segs = rel[len(base_dir) + 1:].split("/")[:-1]  # 去掉文件名，剩目录段
        full = ".".join([prefix] + segs)
        accepted = {full}
        for k in range(len(segs) - 1, 0, -1):
            accepted.add(".".join([prefix] + segs[:k]))
        return full, accepted
    return None, None


def check_namespace_matches_dir(root: Path) -> list:
    problems = []
    for rel_dir in (CORE_DIR, RUNTIME_DIR):
        for path, rel in _cs_files(root, rel_dir):
            if rel.startswith(GENERATED_DIR + "/"):
                continue  # Luban 生成物，namespace 由生成器定（cfg），手改会被覆盖
            expected, accepted = _expected_namespaces(rel)
            if not expected:
                continue
            text = _read(path)
            found = None
            found_line = 0
            for i, line in enumerate(text.splitlines(), 1):
                m = NAMESPACE_LINE_RE.match(line)
                if m:
                    found, found_line = m.group(1), i
                    break
            if found is None:
                if not _declares_type(text):
                    continue  # 纯注释 / 纯指令文件，没有类型要归属
                problems.append(Problem(
                    rel, 0,
                    "整个文件没有 namespace 声明，类型会落进全局命名空间，跨程序集重名时先到先得",
                    f"加 namespace {expected}",
                    REF_NAMESPACE,
                ))
                continue
            if found in accepted:
                continue
            problems.append(Problem(
                rel, found_line,
                f"namespace 是 {found}，与目录 {rel.rsplit('/', 1)[0]}/ 对不上",
                f"改成 namespace {expected}（或把文件挪到与现命名空间相符的目录）",
                REF_NAMESPACE,
            ))
    return problems


# ------------------------------------------------------------------ 5. .meta 配对
# 依据：project-root.md「生成物边界」、pitfalls.md「.meta 没提交，别人打开工程引用全断」。
# 为什么 lint 做不了：判据是「隔壁有没有另一个文件」，单文件正则永远看不到隔壁。


def check_meta_pairing(root: Path) -> list:
    problems = []
    base = root / PROJECT_DIR
    if not base.is_dir():
        return problems

    entries = [(base, True)]  # (实体绝对路径, 是否目录)
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = sorted(d for d in dirnames if not _unity_ignored(d))
        for d in dirnames:
            entries.append((Path(dirpath) / d, True))
        for f in sorted(filenames):
            if _unity_ignored(f):
                continue
            entries.append((Path(dirpath) / f, False))

    metas = []
    for entity, is_dir in entries:
        if entity.suffix.lower() == ".meta":
            metas.append(entity)
            continue
        if entity.with_name(entity.name + ".meta").exists():
            continue
        kind = "目录" if is_dir else "文件"
        problems.append(Problem(
            _rel(root, entity), 0,
            f"这个{kind}没有配套 .meta，别人拉下来 Unity 会重新生成新 GUID，所有指向它的引用静默失效",
            "在 Unity 里刷新一次资产数据库让它生成 .meta，再把 .meta 与实体一起提交"
            "（移动 / 改名 / 删除也要 git mv / git rm 连 .meta 一起）",
            REF_META,
        ))

    for meta in metas:
        if meta.with_name(meta.name[: -len(".meta")]).exists():
            continue
        problems.append(Problem(
            _rel(root, meta), 0,
            "孤儿 .meta：它描述的文件 / 目录已经不在了",
            "用 git rm 删掉这个 .meta；若是误删了实体，先把实体找回来再说",
            REF_META,
        ))
    return problems


# ------------------------------------------------------------------ 6. UI 面板地址等于类名
# 依据：architecture.md 5.6「预制体 Addressables key 等于类名」——
#   UIService.OpenAsync<T> 走的是 assets.InstantiateAsync(typeof(T).Name)。
# 为什么 lint 做不了：要把 prefab 的 .meta GUID 跟 Addressables 组资产的 YAML 对上，
#   跨三个文件。读纯文本判断，**不依赖 Unity 开着**。

ENTRY_GUID_RE = re.compile(r"^\s*-\s*m_GUID:\s*([0-9a-fA-F]{32})\s*$")
ENTRY_ADDRESS_RE = re.compile(r"^\s*m_Address:\s*(.*?)\s*$")


def _addressable_entries(root: Path) -> dict:
    """GUID（小写）-> (address, 组文件相对路径, 行号)。组资产不在就返回空。"""
    out = {}
    groups = root / ADDRESSABLE_GROUPS_DIR
    if not groups.is_dir():
        return out
    for asset in sorted(groups.glob("*.asset")):  # 不递归：Schemas/ 里的不是组
        rel = _rel(root, asset)
        lines = _read(asset).splitlines()
        for i, line in enumerate(lines):
            m = ENTRY_GUID_RE.match(line)
            if not m:
                continue
            for look in lines[i + 1: i + 4]:  # m_Address 紧跟在 m_GUID 后面
                a = ENTRY_ADDRESS_RE.match(look)
                if a:
                    out[m.group(1).lower()] = (a.group(1), rel, i + 1)
                    break
    return out


def check_ui_addressable_address(root: Path) -> list:
    problems = []
    ui_dir = root / UI_PREFAB_DIR
    if not ui_dir.is_dir() or not (root / ADDRESSABLE_GROUPS_DIR).is_dir():
        return problems  # 没有面板目录或还没启用 Addressables，无从判起
    entries = _addressable_entries(root)
    for prefab in sorted(ui_dir.glob("*.prefab")):
        rel = _rel(root, prefab)
        expected = prefab.stem
        meta = prefab.with_name(prefab.name + ".meta")
        m = re.search(r"^guid:\s*([0-9a-fA-F]{32})\s*$", _read(meta), re.MULTILINE)
        if not m:
            continue  # .meta 缺失由第 5 条报，这里不重复报
        found = entries.get(m.group(1).lower())
        if found is None:
            problems.append(Problem(
                rel, 0,
                f"面板预制体没进 Addressables，UIService.OpenAsync<{expected}>() 运行时按地址 "
                f"\"{expected}\" 找不到它，报的是资源缺失而不是代码错误",
                f"在 Addressables 窗口里把它标成 Addressable，地址填 {expected}（必须等于类名）",
                REF_UI_ADDRESS,
            ))
            continue
        address, group_rel, group_line = found
        if address == expected:
            continue
        problems.append(Problem(
            group_rel, group_line,
            f"{rel} 的 Addressables 地址是 \"{address}\"，不等于类名 \"{expected}\"；"
            f"UIService 按 typeof(T).Name 找预制体，开这个面板必然失败",
            f"把地址改成 {expected}；若是类名改了，预制体文件名、类名、地址三者要一起改",
            REF_UI_ADDRESS,
        ))
    return problems


def check_dynamic_font_bloat(root: Path) -> list:
    """Dynamic 模式的 TMP 字体资产不该把运行时攒下的字形数据带进 git。

    为什么要机器查：Dynamic 字体按需栅格化，每跑一次 Play 就往资产里塞新字形，
    体积从几 KB 涨到几 MB。靠人「提交前记得点 Clear Dynamic Data」是无载体约定，
    必然有人忘；忘了的代价是每人每次 Play 都产生巨大 diff，还会在多人分支间冲突。
    阈值取 200 KB：干净基线是几 KB，跑过一轮 Play 就上 MB，中间留足余量不误伤。
    退场条件：TMP 若改成把动态图集存到资产外部，这条检查连同阈值一起删。
    """
    problems = []
    base = root / PROJECT_DIR
    if not base.is_dir():
        return problems
    for asset in sorted(base.rglob("*.asset")):
        name = asset.name
        if "SDF" not in name and "FontAsset" not in name:
            continue
        try:
            size = asset.stat().st_size
        except OSError:
            continue
        if size <= FONT_ASSET_MAX_BYTES:
            continue
        text = _read(asset)
        # 只管 Dynamic（m_AtlasPopulationMode: 1）；Static 资产本来就该是大的
        if "m_AtlasPopulationMode: 1" not in text:
            continue
        problems.append(Problem(
            _rel(root, asset), 0,
            f"Dynamic 字体资产 {size // 1024} KB，里面攒着 Play 期栅格化出来的字形数据，"
            f"提交进去每人每次运行都会产生巨大 diff 并在分支间冲突",
            "在 Inspector 里选中该资产 → 右键菜单 / 齿轮里点 Clear Dynamic Data，"
            "回到几 KB 的基线再提交（字形运行时会自动重建，清掉不影响显示）",
            REF_FONT_BLOAT,
        ))
    return problems


# ------------------------------------------------------------------ 汇总

CHECKS = (
    ("asmdef 依赖方向", check_asmdef_direction),
    ("平台宏只在 Core/Platform/", check_platform_macros),
    ("裸 using UnityEditor", check_using_unityeditor),
    ("命名空间与目录一致", check_namespace_matches_dir),
    (".meta 配对", check_meta_pairing),
    ("UI 面板地址等于类名", check_ui_addressable_address),
    ("Dynamic 字体资产未清动态数据", check_dynamic_font_bloat),
)


def scan(root) -> list:
    """跑全部检查，返回 Problem 列表。`gc_scan.py` 第 5 项调的就是这个。"""
    root = Path(root).resolve()
    problems = []
    for _, fn in CHECKS:
        problems.extend(fn(root))
    return problems


def main() -> int:
    _reconfigure_utf8()
    root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[3]
    problems = scan(root)
    if not problems:
        return 0  # 成功静默
    print(f"[invariants] 发现 {len(problems)} 处跨文件约束违规：\n")
    for n, p in enumerate(problems, 1):
        body = render(p).splitlines()
        print(f"  {n}) {body[0]}")
        for line in body[1:]:
            print(f"  {line}")
        print()
    print("这些是 project-lint 的逐行正则够不着的跨文件约束；逐条按「改法」修，改完重跑。")
    return 1


if __name__ == "__main__":
    sys.exit(main())
