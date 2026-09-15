#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""onboard 环境自检脚本（`/onboard` 第 1 步的执行体）。

## 用途

新开发者拉下工程后，机器上一堆前置条件（Unity 版本、git、Python、uv、
.NET SDK、MCP 两侧版本、包缓存、桥接状态……）逐项手工核对太慢也容易漏。
这个脚本一次性跑完，报出每项 [通过]/[失败]/[提示]，失败项附一句修法。

## 怎么跑

    python .claude/skills/onboard/check_env.py          # 人读表格
    python .claude/skills/onboard/check_env.py --json    # 机读 JSON 数组

## 退出码

    0 —— 没有 [失败] 项（可能仍有 [提示]，不阻塞）
    1 —— 至少一项 [失败]，需要按修法处理后重跑

Python 3、无第三方依赖，Windows 优先但不写死盘符（用环境变量推导）。
"""
from __future__ import annotations

import json
import os
import platform
import re
import shutil
import subprocess
import sys
from pathlib import Path

# 工程根：本文件在 <root>/.claude/skills/onboard/ 下，往上三层推导，不写死绝对路径。
ROOT = Path(__file__).resolve().parents[3]

PASS = "通过"
FAIL = "失败"
NOTE = "提示"

STATUS_TAG = {PASS: "[通过]", FAIL: "[失败]", NOTE: "[提示]"}


def _utf8_stdio() -> None:
    """Windows 下 stdout 默认走系统代码页，中文会乱码，显式转 UTF-8。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        except Exception:  # noqa: BLE001
            pass


def norm(path: str) -> str:
    return str(path).replace("\\", "/")


def check_unity_editor() -> dict:
    name = "工程版本对应的 Unity 编辑器"
    vp = ROOT / "ProjectSettings" / "ProjectVersion.txt"
    if not vp.is_file():
        return {"name": name, "status": FAIL, "detail": "找不到 ProjectSettings/ProjectVersion.txt，确认在工程根下运行"}
    text = vp.read_text(encoding="utf-8", errors="replace")
    m = re.search(r"m_EditorVersion:\s*(\S+)", text)
    if not m:
        return {"name": name, "status": FAIL, "detail": "ProjectVersion.txt 里没读到 m_EditorVersion"}
    version = m.group(1)

    env_path = os.environ.get("UNITY_EDITOR_PATH")
    if env_path and Path(env_path).exists():
        return {"name": name, "status": PASS, "detail": f"版本 {version}，UNITY_EDITOR_PATH 已指向可用路径"}

    system = platform.system()
    candidates = []
    if system == "Windows":
        program_files = os.environ.get("ProgramFiles", r"C:\Program Files")
        candidates.append(Path(program_files) / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity.exe")
    elif system == "Darwin":
        candidates.append(Path("/Applications/Unity/Hub/Editor") / version / "Unity.app")

    for c in candidates:
        if c.exists():
            return {"name": name, "status": PASS, "detail": f"版本 {version}，找到 {norm(str(c))}"}

    return {
        "name": name,
        "status": FAIL,
        "detail": f"未找到 Unity {version} 的可执行文件；用 Unity Hub 安装该版本，或设置环境变量 UNITY_EDITOR_PATH 指向 Unity 可执行文件；见 docs/developer-guide.md §1.2",
    }


def check_git() -> dict:
    name = "git"
    path = shutil.which("git")
    if not path:
        return {
            "name": name,
            "status": FAIL,
            "detail": "PATH 里找不到 git；先安装 git 并加入 PATH；见 docs/developer-guide.md §1.3，或跑 install_env.ps1",
        }
    detail = f"在 PATH 中：{norm(path)}"
    try:
        result = subprocess.run(
            ["git", "config", "core.autocrlf"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            timeout=10,
        )
        value = result.stdout.strip().lower()
    except Exception:  # noqa: BLE001
        value = ""
    if value == "true":
        return {
            "name": name,
            "status": NOTE,
            "detail": detail + "；core.autocrlf=true（仓库 .gitattributes 已统一 LF，这项不用你手动改，留意即可）",
        }
    return {"name": name, "status": PASS, "detail": detail}


def check_python() -> dict:
    name = "Python 版本（钩子需要）"
    v = sys.version_info
    if (v.major, v.minor) >= (3, 10):
        return {"name": name, "status": PASS, "detail": f"{platform.python_version()}"}
    return {
        "name": name,
        "status": FAIL,
        "detail": f"当前 {platform.python_version()}，钩子需要 >= 3.10；升级 Python；见 docs/developer-guide.md §1.4，或跑 install_env.ps1",
    }


def check_uv() -> dict:
    name = "uv / uvx（MCP 服务端靠它拉起）"
    uv_path = shutil.which("uv")
    uvx_path = shutil.which("uvx")
    if uv_path and uvx_path:
        return {"name": name, "status": PASS, "detail": f"uv={norm(uv_path)}，uvx={norm(uvx_path)}"}
    missing = [n for n, p in (("uv", uv_path), ("uvx", uvx_path)) if not p]
    return {
        "name": name,
        "status": FAIL,
        "detail": "PATH 里缺 " + "、".join(missing)
        + "；安装：Windows 用 `powershell -ExecutionPolicy ByPass -c \"irm https://astral.sh/uv/install.ps1 | iex\"`，"
        "macOS/Linux 用 `curl -LsSf https://astral.sh/uv/install.sh | sh`"
        "；见 docs/developer-guide.md §1.5，或跑 install_env.ps1",
    }


def check_dotnet() -> dict:
    name = ".NET SDK（Luban 配置表生成，波 2 起必需）"
    anchor = "见 docs/developer-guide.md §1.6，或跑 install_env.ps1"
    try:
        result = subprocess.run(["dotnet", "--list-sdks"], capture_output=True, text=True, timeout=15)
    except FileNotFoundError:
        return {"name": name, "status": NOTE, "detail": f"PATH 里找不到 dotnet；波 2 起需要 .NET SDK 8.0+，现在可以先跳过；{anchor}"}
    except Exception as exc:  # noqa: BLE001
        return {"name": name, "status": NOTE, "detail": f"检查失败（{exc}），波 2 起需要，现在可以先跳过；{anchor}"}

    if result.returncode != 0 or not result.stdout.strip():
        return {"name": name, "status": NOTE, "detail": f"dotnet 在 PATH 但没有已安装的 SDK；波 2 起需要 8.0+，现在可以先跳过；{anchor}"}

    versions = []
    for line in result.stdout.strip().splitlines():
        m = re.match(r"(\d+)\.(\d+)\.\d+", line.strip())
        if m:
            versions.append((int(m.group(1)), int(m.group(2)), line.strip()))
    if not versions:
        return {"name": name, "status": NOTE, "detail": f"未能解析已安装 SDK 版本；波 2 起需要 8.0+；{anchor}"}

    best = max(versions, key=lambda v: (v[0], v[1]))
    if best[0] >= 8:
        return {"name": name, "status": PASS, "detail": best[2]}
    return {"name": name, "status": NOTE, "detail": f"已安装 {best[2]}，低于 8.0；波 2 起需要 8.0+；{anchor}"}


def check_luban_tool() -> dict:
    name = "Luban 工具（配置表生成）"
    dll = ROOT / "Tools" / "Luban" / "Luban.dll"
    if dll.is_file():
        return {"name": name, "status": NOTE, "detail": f"已就位：{norm(str(dll.relative_to(ROOT)))}"}
    return {
        "name": name,
        "status": NOTE,
        "detail": "Tools/Luban/Luban.dll 不存在（该目录已 gitignore，本来就不进库）；"
        "首次跑 `powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1` 或点菜单 21Days/配置表/生成 会自动下载解压，"
        "需要能访问 github.com；只改代码不改配置表的话不用管；见 docs/developer-guide.md 第 8 章",
    }


def check_claude_code() -> dict:
    name = "Claude Code（claude 在 PATH）"
    path = shutil.which("claude")
    if path:
        return {"name": name, "status": PASS, "detail": f"在 PATH 中：{norm(path)}"}
    return {
        "name": name,
        "status": NOTE,
        "detail": "PATH 里找不到 claude；可能在别的终端/方式启动，不阻塞；见 docs/developer-guide.md §1.7",
    }


def check_winget() -> dict:
    name = "winget 可用性（一键安装脚本依赖）"
    path = shutil.which("winget")
    if path:
        return {"name": name, "status": NOTE, "detail": f"在 PATH 中：{norm(path)}，install_env.ps1 可用它自动安装"}
    return {
        "name": name,
        "status": NOTE,
        "detail": "PATH 里找不到 winget；install_env.ps1 会退化为只打印各工具官网地址，需手动下载安装；见 docs/developer-guide.md 第 1 章",
    }


def check_mcp_version_sync() -> dict:
    name = "MCP for Unity 两侧版本一致"
    mcp_json = ROOT / ".mcp.json"
    manifest_json = ROOT / "Packages" / "manifest.json"
    if not mcp_json.is_file() or not manifest_json.is_file():
        return {"name": name, "status": FAIL, "detail": "缺 .mcp.json 或 Packages/manifest.json，无法比对"}
    try:
        mcp_data = json.loads(mcp_json.read_text(encoding="utf-8"))
        manifest_data = json.loads(manifest_json.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return {"name": name, "status": FAIL, "detail": f"JSON 解析失败：{exc}"}

    args = mcp_data.get("mcpServers", {}).get("UnityMCP", {}).get("args", [])
    args_text = " ".join(str(a) for a in args)
    m1 = re.search(r"mcpforunityserver==([\w\.\-]+)", args_text)
    has_stdio = "--transport" in args and "stdio" in args
    if not has_stdio:
        # 兼容 "--transport stdio" 写成一个字符串的情况
        has_stdio = bool(re.search(r"--transport\s+stdio", args_text))

    dep = manifest_data.get("dependencies", {}).get("com.coplaydev.unity-mcp", "")
    m2 = re.search(r"#v([\w\.\-]+)$", dep)

    if not m1 or not m2:
        return {"name": name, "status": FAIL, "detail": ".mcp.json 或 manifest.json 里没找到 unity-mcp 的版本号"}

    v1, v2 = m1.group(1), m2.group(1)
    if v1 != v2:
        return {
            "name": name,
            "status": FAIL,
            "detail": f".mcp.json 是 {v1}，manifest.json 是 {v2}，不一致；两边一起改到同一个版本后重启 Claude Code",
        }
    if not has_stdio:
        return {"name": name, "status": FAIL, "detail": ".mcp.json 里没有 --transport stdio，本工程约定固定用 stdio"}
    return {"name": name, "status": PASS, "detail": f"两侧均为 {v1}，transport=stdio"}


def check_library() -> dict:
    name = "Library/（工程至少打开过一次）"
    if (ROOT / "Library").is_dir():
        return {"name": name, "status": PASS, "detail": "存在"}
    return {"name": name, "status": FAIL, "detail": "不存在；用 Unity Hub 打开一次工程，等包解析与首次导入结束"}


def check_package_cache() -> dict:
    name = "Library/PackageCache 里的 git 包"
    manifest_json = ROOT / "Packages" / "manifest.json"
    cache_dir = ROOT / "Library" / "PackageCache"
    if not manifest_json.is_file():
        return {"name": name, "status": FAIL, "detail": "找不到 Packages/manifest.json"}
    try:
        deps = json.loads(manifest_json.read_text(encoding="utf-8")).get("dependencies", {})
    except (OSError, json.JSONDecodeError) as exc:
        return {"name": name, "status": FAIL, "detail": f"manifest.json 解析失败：{exc}"}

    git_pkgs = [k for k, v in deps.items() if isinstance(v, str) and v.startswith("http")]
    if not cache_dir.is_dir():
        return {"name": name, "status": FAIL, "detail": "Library/PackageCache 不存在；先让 Package Manager 完成解析"}

    cached_dirs = [p.name for p in cache_dir.iterdir() if p.is_dir()]
    missing = [pkg for pkg in git_pkgs if not any(d.startswith(pkg + "@") for d in cached_dirs)]
    if missing:
        return {
            "name": name,
            "status": FAIL,
            "detail": "缺：" + "、".join(missing) + "；检查网络/GitHub 访问后在 Package Manager 里 Resolve",
        }
    return {"name": name, "status": PASS, "detail": f"{len(git_pkgs)} 个 git 包均已缓存"}


def check_mcp_bridge() -> dict:
    name = "MCP 桥接状态（Transport=Stdio）"
    status_dir = Path.home() / ".unity-mcp"
    if not status_dir.is_dir():
        return {
            "name": name,
            "status": FAIL,
            "detail": "~/.unity-mcp 目录不存在，桥接没有以 stdio 启动过；"
            "在 Unity 里 Window -> MCP for Unity，Transport 改成 Stdio（先 Stop Server），见 ai-docs/pitfalls.md 最后一条",
        }
    target = norm(str(ROOT / "Assets"))
    for f in status_dir.glob("unity-mcp-status-*.json"):
        try:
            content = norm(f.read_text(encoding="utf-8", errors="replace"))
        except OSError:
            continue
        if target in content:
            return {"name": name, "status": PASS, "detail": f"{f.name} 已登记本工程"}
    return {
        "name": name,
        "status": FAIL,
        "detail": "没有状态文件登记本工程 Assets 路径；"
        "在 Unity 里 Window -> MCP for Unity，Transport 改成 Stdio（先 Stop Server），见 ai-docs/pitfalls.md 最后一条",
    }


def check_editor_lock() -> dict:
    name = "编辑器是否正开着"
    lock = ROOT / "Temp" / "UnityLockfile"
    if lock.exists():
        return {"name": name, "status": NOTE, "detail": "编辑器正开着，batchmode 打包/测试会失败，请走 MCP"}
    return {"name": name, "status": NOTE, "detail": "编辑器未开"}


CHECKS = (
    check_unity_editor,
    check_git,
    check_python,
    check_uv,
    check_dotnet,
    check_luban_tool,
    check_claude_code,
    check_winget,
    check_mcp_version_sync,
    check_library,
    check_package_cache,
    check_mcp_bridge,
    check_editor_lock,
)


def main() -> int:
    _utf8_stdio()
    as_json = "--json" in sys.argv[1:]

    results = [c() for c in CHECKS]
    failed = [r for r in results if r["status"] == FAIL]

    if as_json:
        print(json.dumps(results, ensure_ascii=False, indent=2))
        return 1 if failed else 0

    print("[onboard] 环境自检")
    for r in results:
        print(f"{STATUS_TAG[r['status']]} {r['name']} —— {r['detail']}")

    print()
    if failed:
        print(f"[onboard] {len(failed)} 项失败，按上面的修法处理后重跑本脚本。")
    else:
        print("[onboard] 没有失败项，可以继续下一步。")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
