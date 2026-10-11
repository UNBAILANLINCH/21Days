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
本文件十一条检查没有一条能写成 `rules.json` 里的正则。

## 载体锚定（`.claude/rules/harness-authoring.md`）

- **执行载体**：`/gc`。`gc_scan.py` 的第 5 项直接 import 本模块跑一遍。
  `/gc` 是本工程已证明在转的载体（改完 harness 结构、`/review-change` 之前都会跑），
  不另设周期、不另加提醒、不做伴生清单。
- **状态锚点**（每段各一条，5 秒可证伪）：
  - 第 1–9 条：把 `Assets/_Project/Prefabs/UI/TitleView.prefab.meta` 临时改个名，
    跑 `python .claude/skills/evolution/invariants.py`，应报 `.meta` 缺失并 exit 1；改回来恢复静默。
  - 第 10 条：把 `ai-docs/docs/modules/performance/performance-module-guide.md:141` 的
    `PerformanceService.cs:621` 改回 `:597`（提交 `f232994` 修正前的值），跑同一条命令，
    应报「引用的 PerformanceService.cs:597 落在没有代码的行上」并 exit 1；改回去恢复静默。
    想单验「成员断言」那条信号就改成 `:606`，应报「文档写的是 `PerformanceService.AttachCamera`，
    但引用的 PerformanceService.cs:606 不在它的范围内（它声明在 PerformanceService.cs:621）」。
    任一锚点报不出来，就说明那一段已经死了，**别信它的沉默**。
- **退场条件**：某条检查连续两次是误报（判据够不着真实结构）就**删掉那条**，
  不要加白名单——白名单攒起来以后没人敢动，而带白名单的检查等于没检查。
  第 10 条另有两条：① 文档改成不带行号的引用方式（只写 `Xxx.cs` / 成员名）之后，
  它无从判定，整条删掉；② 首扫真阳性被修完、之后连续两轮 `/gc` 零命中且期间没有新的
  漂移，说明「引用代码处」这个约定本身已经不写了，也该删。
  整份文件的退场条件是 `Assets/_Project/Scripts/` 不再是这个形状（换结构 / 换引擎），
  届时连同 `gc_scan.py` 第 5 项一起删，而不是留着让它一直报。

## 查十一条（每条都注明依据，便于以后判断该不该删）

    1. asmdef 依赖方向        Game.Core 不引用 Runtime/Editor/Tests；Game.Runtime 不引用 Editor/Tests
    2. 平台宏只在 Core/Platform/   Core（除 Platform/）与 Runtime 下不出现 #if UNITY_ANDROID 等平台宏
    3. 裸 using UnityEditor   Core / Runtime 里的 using UnityEditor 必须被 #if UNITY_EDITOR 包住
    4. 命名空间与目录一致      Core/<X>/ -> Game.Core.<X>；Runtime/<M>/ -> Game.<M>
    5. .meta 配对             Assets/_Project/ 下每个实体有 .meta，每个 .meta 有实体
    6. UI 面板地址等于类名     Prefabs/UI/*.prefab 在 Addressables 里有条目且 address == 文件名
    7. Dynamic 字体未清数据    Dynamic TMP 字体资产超过 200 KB（带着 Play 期字形）
    8. 正式资产不引测试脚本    正式场景 / 预制体的 m_Script 不指向 Scripts/Tests/ 下的脚本
    9. InitTestScene 残留      Assets/ 根下没有 PlayMode 测试中断留下的 InitTestScene*.unity
   10. 文档代码引用失效       ai-docs 里 `Xxx.cs:行号` 的引用还指得到东西（越界 / 落在无代码行 / 与
                             文档自己写的成员名对不上）
   11. VolumeProfile 组件为空     Assets/ 下 VolumeProfile 资产的 components 列表里不出现 {fileID: 0}
                             （组件没存成子资产，profile 空转不报错）

第 11 条的状态锚点：把 `Assets/Settings/ExplorationVolumeProfile.asset` 里 `components:` 下任一行
临时改成 `- {fileID: 0}`，跑同一条命令应报该资产并 exit 1；改回来恢复静默。

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
REF_TEST_SCRIPT_IN_ASSET = "project-root.md #目录与 asmdef 依赖方向 · pitfalls.md #正式场景引用了测试程序集脚本"
REF_INIT_TEST_SCENE = ".gitignore #InitTestScene 注释 · pitfalls.md #InitTestScene 残留堆积"

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


# ------------------------------------------------------------------ 8. 正式资产不引测试脚本
# 依据：project-root.md「Game.Runtime 不引用 Editor / Tests」在资产侧的对应——
#   测试程序集（Game.Tests.*）不进包，正式场景 / 预制体挂了它的脚本，出包后就是 missing script。
# 为什么 lint 做不了：场景 YAML 里只有 m_Script 的 GUID，要和 Tests 目录下的 .cs.meta 对上
#   才知道它是谁；check_asmdef_direction 只拦代码层引用，资产层没人拦。
# 只扫正式资产目录（Assets/Scenes/、_Project/Scenes/、_Project/Prefabs/）；
#   Tests/Showcase/ 下的回放场景本来就该引测试脚本，不在扫描范围。

TESTS_DIR = SCRIPTS_DIR + "/Tests"
FORMAL_ASSET_GLOBS = (
    ("Assets/Scenes", "*.unity"),
    (PROJECT_DIR + "/Scenes", "*.unity"),
    (PROJECT_DIR + "/Prefabs", "*.prefab"),
)
M_SCRIPT_RE = re.compile(
    r"m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-fA-F]{32}),\s*type:\s*3\s*\}"
)


def _test_script_guid_map(root: Path) -> dict:
    """GUID（小写）-> Tests 目录下脚本的相对路径。直接读 .cs.meta，不依赖 asmdef。"""
    out = {}
    base = root / TESTS_DIR
    if not base.is_dir():
        return out
    for meta in sorted(base.rglob("*.cs.meta")):
        m = re.search(r"^guid:\s*([0-9a-fA-F]{32})\s*$", _read(meta), re.MULTILINE)
        if m:
            out[m.group(1).lower()] = _rel(root, meta.with_suffix(""))  # 去掉 .meta
    return out


def check_test_script_in_formal_asset(root: Path) -> list:
    problems = []
    guid2script = _test_script_guid_map(root)
    if not guid2script:
        return problems
    for rel_dir, pattern in FORMAL_ASSET_GLOBS:
        base = root / rel_dir
        if not base.is_dir():
            continue
        for asset in sorted(base.rglob(pattern)):
            rel = _rel(root, asset)
            seen = set()
            for i, line in enumerate(_read(asset).splitlines(), 1):
                m = M_SCRIPT_RE.search(line)
                if not m:
                    continue
                script = guid2script.get(m.group(1).lower())
                if not script or script in seen:
                    continue  # 同一文件同一脚本只报一次
                seen.add(script)
                cls = script.rsplit("/", 1)[-1][: -len(".cs")]
                problems.append(Problem(
                    rel, i,
                    f"引用了测试程序集脚本 `{cls}`（`{script}`），测试程序集不进包，出包后是 missing script",
                    f"把脚本搬到 `{RUNTIME_DIR}/<模块>/`，或把这个组件从正式资产上拿掉",
                    REF_TEST_SCRIPT_IN_ASSET,
                ))
    return problems


# ------------------------------------------------------------------ 9. InitTestScene 残留
# 依据：.gitignore 的 InitTestScene 注释——Unity Test Framework 跑 PlayMode 测试时在 Assets/ 根
#   建 InitTestScene<时间戳>.unity 临时场景，中断就残留；git 看不见，Project 窗口里却堆成一片。
# 只看 Assets/ 根这一层、不递归；刻意不走 _unity_ignored（那是 .meta 配对用的过滤）。
# 每个文件一条，条数即残留数量。

INIT_TEST_SCENE_RE = re.compile(r"^InitTestScene.*\.unity(\.meta)?$")


def check_init_test_scene_leftovers(root: Path) -> list:
    problems = []
    assets = root / "Assets"
    if not assets.is_dir():
        return problems
    for f in sorted(assets.iterdir()):
        if not f.is_file() or not INIT_TEST_SCENE_RE.match(f.name):
            continue
        problems.append(Problem(
            _rel(root, f), 0,
            "PlayMode 测试中断残留的临时场景",
            "编辑器不在 Play 时直接删：`rm Assets/InitTestScene*.unity Assets/InitTestScene*.unity.meta`",
            REF_INIT_TEST_SCENE,
        ))
    return problems

# ------------------------------------------------------------------ 10. 文档代码引用失效
# 依据：generate-doc/SKILL.md #篇幅约束「引用代码用 `path:line`」、#三种模式
#   （`maturity: stable` = 「跟得上代码」；行号漂了就不算跟得上）。
# 为什么 lint 做不了：判据要把**文档行**与**被引 .cs 的内容**对起来（行号越界 / 落在没有代码的
#   行上 / 与文档自己写的成员名对不上），跨两类文件；project-lint 是逐文件逐行正则，
#   看不见隔壁那份文件，也看不见「这个行号指的是谁」。
# 为什么必须有载体：代码改一轮、文档行号静静漂走 —— 不报编译错误、不报测试失败，
#   读的人照行号跳过去读到的是别的代码，比没有引用更坏。
#   generate-doc 的 detect.py 只说「去 sync 一下」，而且**没注册进 settings.json**
#   （NO-CARRIER）；「sync 一下」也不告诉你是哪几处漂了。所以归 `/gc` 的全量重扫。
#
# 只认三条**可机械判定**的信号，判不准的一律不报（harness-authoring.md「宁少勿滥」）：
#   1. 行号超出文件总行数；
#   2. 引用的行（区间取整段）不承载任何代码：空行 / 只有大括号、using、namespace、#region；
#   3. 文档自己写了 `Foo.Bar`（Foo 正好是被引文件的主干名），行号却不在 Bar 的范围内。
# 刻意**不做**的信号（实测误报压不下去）：
#   「引用行是注释」—— 本工程故意指向文件头注释 / XML 注释说明理由；拿刚对齐的三份文档实测，
#     74 处引用里 17 处命中，全是误报；
#   「文档提到的成员名没出现在该行」—— 文档常指向方法体内部某一行，命中率极高、几乎全是误报。
# 判不了就不猜：被引文件名在仓库里找不到（包缓存 / Unity 内置）或同名多份时直接跳过。

#: 只扫知识层：这三类引用的家。docs/planning 那类台账是历史记录，不在这里判。
DOC_SCAN_DIR = "ai-docs"

#: `Xxx.cs:123` / `Xxx.cs:123-130`（连字符也吃全角与波浪号）
DOC_REF_RE = re.compile(
    r"(?P<path>(?:[A-Za-z0-9_.\-]+/)*[A-Za-z0-9_.\-]+\.cs)"
    r":(?P<start>\d+)(?:\s*[-–—~]\s*(?P<end>\d+))?"
)
#: 文档行里的 `` `代码` `` 片段；找成员断言前先抹掉引用本身，
#: 否则 `Foo.cs:12` 会被当成「Foo.cs 的成员 cs」
DOC_TICK_RE = re.compile(r"`([^`]+)`")
#: 成员断言 `Foo.Bar`：Foo 要求 PascalCase，Bar 可以是任意标识符
DOC_MEMBER_RE = re.compile(r"\b([A-Z][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)")
DOC_WORD_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]{2,}")
#: 只有结构、不承载语义的行 —— 引用落在它上面等于没指。
#: **刻意不含属性行**（`[Test]` / `[SerializeField]`）：那是合法锚点，文档常按 `[Test]` 定位用例。
STRUCT_ONLY_RE = re.compile(
    r"^(?:\{|\}|using\s+[\w.]+\s*;|namespace\s+[\w.]+\s*\{?|#\s*(?:region|endregion)\b.*)$"
)
#: 数大括号前先去掉字符串与行注释，否则 `"{"` 会把深度带偏
CS_COMMENT_STRIP_RE = re.compile(r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//.*$', re.MULTILINE)
#: 看起来像「声明行」：带访问修饰符 / 修饰符前缀。用来挑出该报给用户的那一行 ——
#: 一个成员在文件里通常有**调用点**和**声明点**两处名字，报成调用点等于给人指错路
#: （实测 `AttachCamera` 的第一个出现位置是别的方法里的调用，声明在 300 行之后）。
CS_DECL_HINT_RE = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?:(?:public|private|protected|internal|static|sealed|abstract|virtual|override"
    r"|readonly|const|partial|async|extern|unsafe|new|event|ref|in|out)\s+)+"
)

REF_DOC_REF = ("generate-doc/SKILL.md #篇幅约束（引用代码用 path:line）· "
               "#三种模式（maturity: stable = 跟得上代码）")


def _doc_code_refs(root: Path):
    """产出 ai-docs 下每一处 `Xxx.cs:行号` 引用：(文档相对路径, 文档行号, 整行原文, 匹配对象)。"""
    base = root / DOC_SCAN_DIR
    if not base.is_dir():
        return
    for md in sorted(base.rglob("*.md")):
        rel = _rel(root, md)
        for i, line in enumerate(_read(md).splitlines(), 1):
            for m in DOC_REF_RE.finditer(line):
                yield rel, i, line, m


def _cs_by_name(root: Path) -> dict:
    """小写文件名 -> [相对路径]。同名多份一律不敢猜，调用方跳过。"""
    out = {}
    assets = root / "Assets"
    if not assets.is_dir():
        return out
    for p in assets.rglob("*.cs"):
        out.setdefault(p.name.lower(), []).append(_rel(root, p))
    return out


def _is_struct_only(line: str) -> bool:
    t = line.strip()
    return (not t) or bool(STRUCT_ONLY_RE.match(t))


def _member_span_start(lines: list, idx: int) -> int:
    """成员声明的起行（1-based），含上方连续的注释横幅 / 特性 / 没写完的参数列表。

    为什么往前扩：本工程**故意**把行号指向「方法上方那段说明」或「上一个成员末尾之后的
    注释块」，只认声明行会把这类合法引用报成漂移。往前扩是往「少报」的方向偏。
    """
    start = idx + 1
    k = idx - 1
    while k >= 0:
        prev = lines[k].strip()
        if not prev:
            break
        if prev.startswith(("//", "/*", "*", "[")) or prev.endswith((",", "(")):
            start = k + 1
            k -= 1
            continue
        break
    return start


def _member_span_end(lines: list, idx: int) -> int:
    """成员声明的止行（1-based）：按大括号配对；字段 / 表达式体遇到行尾分号就收。"""
    depth = 0
    seen_brace = False
    for j in range(idx, len(lines)):
        line = CS_COMMENT_STRIP_RE.sub("", lines[j])
        depth += line.count("{") - line.count("}")
        if "{" in line:
            seen_brace = True
        if seen_brace and depth <= 0:
            return j + 1
        if not seen_brace and line.rstrip().endswith(";"):
            return j + 1
    return len(lines)


def _claimed_members(doc_line: str, stem: str) -> list:
    """文档自己写下的 `Foo.Bar` 断言里、Foo 正好是被引文件主干名的那些成员名。"""
    stripped = DOC_REF_RE.sub(" ", doc_line)
    out = set()
    for tick in DOC_TICK_RE.findall(stripped):
        for tm in DOC_MEMBER_RE.finditer(tick):
            if tm.group(1).lower() == stem.lower():
                out.add(tm.group(2))
    return sorted(out)


def _covering_member(lines: list, lo: int, hi: int, claimed: list):
    """哪个被点名的成员「包住」了这段行号。

    返回 (包住的那个成员名 或 None, 该报给用户看的行号)。没有成员包住时，第二个值取
    声明点的行号（优先挑带修饰符的声明行，挑不出才退回第一次出现的位置）——
    报调用点会把人引到别处去。
    """
    fallback = (claimed[0], 0)
    for mem in claimed:
        decl_line = 0
        first_line = 0
        pattern = re.compile(rf"\b{re.escape(mem)}\b")
        for j, line in enumerate(lines, 1):
            if not pattern.search(line):
                continue
            if not first_line:
                first_line = j
            if not decl_line and CS_DECL_HINT_RE.match(line) and not line.lstrip().startswith("//"):
                decl_line = j
            if _member_span_start(lines, j - 1) <= lo and hi <= _member_span_end(lines, j - 1):
                return mem, j
        if not fallback[1]:
            fallback = (mem, decl_line or first_line)
    return None, fallback[1]


def _line_names_doc_identifier(lines: list, lo: int, doc_line: str) -> bool:
    """引用行自己提到的标识符，出现在文档行的反引号里 —— 视为对得上。

    收的是「文档行说 `PlayerScenePosition`，引用行也正好在写 PlayerScenePosition，
    只是同一行里前面还写了别的类型名」这种关联错配，机械判不出谁是谁，就放行。
    """
    doc_ids = set()
    for tick in DOC_TICK_RE.findall(DOC_REF_RE.sub(" ", doc_line)):
        doc_ids.update(DOC_WORD_RE.findall(tick))
    return bool(doc_ids & set(DOC_WORD_RE.findall(lines[lo - 1])))


def check_doc_code_refs(root: Path) -> list:
    problems = []
    index = _cs_by_name(root)
    line_cache = {}
    for doc_rel, doc_line, text, m in _doc_code_refs(root):
        name = m.group("path").rsplit("/", 1)[-1]
        cands = index.get(name.lower()) or []
        if len(cands) != 1:
            continue  # 文件不在仓库里、或同名多份：判不了就不猜
        cs_rel = cands[0]
        lines = line_cache.get(cs_rel)
        if lines is None:
            lines = _read(root / cs_rel).splitlines()
            line_cache[cs_rel] = lines
        total = len(lines)
        start = int(m.group("start"))
        end = int(m.group("end")) if m.group("end") else start
        if end < start:  # `X.cs:130-120` 这种写反的区间，先摆正再判，免得报出误导性的现象
            start, end = end, start
        loc = f"{name}:{start}" + (f"-{end}" if end != start else "")

        if start > total or end > total:
            problems.append(Problem(
                doc_rel, doc_line,
                f"引用的 {loc} 超出了 {name} 的总行数（现在 {total} 行），代码改短之后文档没跟上",
                f"按代码现在的实际位置改这个行号；位置常变就别写行号，只写 `{name}` 或成员名",
                REF_DOC_REF,
            ))
            continue

        if all(_is_struct_only(x) for x in lines[start - 1:end]):
            problems.append(Problem(
                doc_rel, doc_line,
                f"引用的 {loc} 落在没有代码的行上（空行 / 只有大括号、using 的接线行），"
                f"照它跳过去看不到任何东西",
                f"改成该内容真正所在的行号；这类位置本来就不该带行号，只写 `{name}` 更耐用",
                REF_DOC_REF,
            ))
            continue

        claimed = _claimed_members(text, name[: -len(".cs")])
        if not claimed:
            continue  # 文档没写「Foo.Bar」形式的断言，判不了是谁，不猜
        covered, decl_line = _covering_member(lines, start, end, claimed)
        if covered or _line_names_doc_identifier(lines, start, text):
            continue
        if not decl_line:
            # 文档点名的成员在这份文件里一次都没出现：可能是继承来的、partial 的兄弟文件里的、
            # 或者扩展方法 —— 机械判不出该不该报，按「判不了就不猜」放过。
            continue
        want = "/".join(f"`{name[: -len('.cs')]}.{c}`" for c in claimed)
        problems.append(Problem(
            doc_rel, doc_line,
            f"文档写的是 {want}，但引用的 {loc} 不在它的范围内（它声明在 {name}:{decl_line}），"
            f"跳过去读到的是别处的代码",
            f"把行号改成 {name}:{decl_line}；这个位置不稳定就别写行号，"
            f"只留 {want} 让读者自己搜",
            REF_DOC_REF,
        ))
    return problems

# ------------------------------------------------------------------ 11. VolumeProfile 组件引用为空
# 依据：pitfalls.md #VolumeProfile 的组件必须存成子资产。脚本 `profile.Add<T>()` 之后没
#   AddObjectToAsset 就保存，序列化出来的 `components:` 全是 `{fileID: 0}`——Volume 照常挂着，
#   后期效果一个不生效，Unity 不报错（ExplorationVolumeProfile 就这样空转过）。
# 为什么 lint 做不了：.asset 是 YAML 且要看多行列表，不是 .cs 行级正则的对象。
# 只认 `components:` 列表里的 `{fileID: 0}`；`components: []`（真空）是合法的，不报。
# 退场条件：Unity 以后对空组件引用自己报错，或工程不再用 Volume 后处理，整条删。

VOLUME_COMPONENTS_RE = re.compile(r"^  components:[ \t]*\r?\n((?:  - .*(?:\r?\n|$))+)", re.MULTILINE)
REF_VOLUME_PROFILE = "pitfalls.md #VolumeProfile 的组件必须存成子资产"


def check_volume_profile_components(root: Path) -> list:
    problems = []
    assets = root / "Assets"
    if not assets.is_dir():
        return problems
    for asset in sorted(assets.rglob("*.asset")):
        text = _read(asset)
        if "components:" not in text:
            continue
        m = VOLUME_COMPONENTS_RE.search(text)
        if not m:
            continue
        null_count = sum(1 for ln in m.group(1).splitlines() if re.search(r"\{fileID:\s*0\}", ln))
        if null_count == 0:
            continue
        problems.append(Problem(
            _rel(root, asset), _line_of(text, "  components:"),
            f"VolumeProfile 的 components 列表里有 {null_count} 项是 {{fileID: 0}}（组件没存成子资产），"
            f"profile 空转：挂着 Volume 却一个后期效果都不生效，Unity 不报错",
            "脚本里对每个组件 AssetDatabase.AddObjectToAsset(component, profile)，再 SaveAssets；"
            "或在 Inspector 里重新 Add Override",
            REF_VOLUME_PROFILE,
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
    ("正式资产不引测试脚本", check_test_script_in_formal_asset),
    ("InitTestScene 残留", check_init_test_scene_leftovers),
    ("文档代码引用失效", check_doc_code_refs),
    ("VolumeProfile 组件引用为空", check_volume_profile_components),
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
