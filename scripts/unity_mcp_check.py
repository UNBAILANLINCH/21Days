"""Unity MCP 复核客户端：主窗口自己没有原生 MCP 工具时，用它做独立复核。

区别于 `unity_mcp_probe.py`（只读、只看连接状态），本脚本**能实际调用工具**：
读控制台、刷新编译、跑定向 EditMode 测试并取回计数。用途是让「subagent 自报通过不算通过」
这条纪律在主窗口这一侧真的能执行 —— 主窗口自己跑一次，再采信子代理的回执。

载体：人工复核命令（主窗口 / 开发者手动跑）。
锚点：`--console` 的 errors 数、`--test` 的 total/passed/failed/skipped、退出码。
退场条件：主窗口会话获得原生 UnityMCP 工具（届时不再需要临时 stdio 连接）。

用法（**务必带 `--offline`，并先清掉代理环境变量**）：
    uv run --offline --with mcp python scripts/unity_mcp_check.py --console
    uv run --offline --with mcp python scripts/unity_mcp_check.py --refresh
    uv run --offline --with mcp python scripts/unity_mcp_check.py --test --mode EditMode --group Game.Tests.EditMode.Narrative
    uv run --offline --with mcp python scripts/unity_mcp_check.py --state
    uv run --offline --with mcp python scripts/unity_mcp_check.py --meta

⚠️ **两种会让本脚本「看起来没输出」的环境坑，都在 2026-10-07 实测踩到过**：

1. **不加 `--offline`**：`uv run --with mcp` 会先回 PyPI 解析 `mcp` 包。网络偶发 `tls handshake eof` 时
   `uv` 重试三次后**以退出码 2 结束、stdout 一个字节都没有**——看起来像「脚本没输出」，不像「连不上」。
2. **`NO_PROXY` 里含 `[::1]`**：MCP 服务端在 httpx 里解析端口时崩
   （`httpx … normalize_port: invalid literal for int(): ':1]'`），**退出码 1、输出只剩一段错误 JSON**。
   本机默认 `NO_PROXY=192.168.50.119,198.18.0.1,.local,localhost,127.0.0.1,::1,[::1]` 就带这个。

**PowerShell 里的完整调用姿势**：
```powershell
Remove-Item Env:NO_PROXY,Env:no_proxy,Env:HTTP_PROXY,Env:HTTPS_PROXY,Env:ALL_PROXY -ErrorAction SilentlyContinue
uv run --offline --with mcp python scripts/unity_mcp_check.py --test --mode EditMode
```
**判据**：本脚本任何动作都必定打印一段 JSON；**stdout 为空或明显偏短，先怀疑上面两条**，去看 stderr。
实测对照（`--state`）：带 `NO_PROXY` → 948 字节 / exit=1；清掉后 → 1082 字节 / exit=0。

"""
import argparse
import asyncio
import json
import os
from pathlib import Path
import sys
import tomllib

ROOT = Path(__file__).resolve().parents[1]


def configuration(root=ROOT):
    """复用项目现有的固定版本 uvx STDIO 配置，不另写一份连接参数。"""
    with (root / ".codex/config.toml").open("rb") as stream:
        config = tomllib.load(stream)["mcp_servers"]["UnityMCP"]
    args = config.get("args", [])
    if (config.get("enabled") is False or config.get("command") != "uvx"
            or len(args) != 5 or args[0] != "--from"
            or not args[1].startswith("mcpforunityserver==")
            or args[2:] != ["mcp-for-unity", "--transport", "stdio"]):
        raise ValueError("只支持项目现有的固定版本 uvx STDIO 配置")
    return config


def unpack(result):
    """把 MCP 工具返回值还原成 dict；服务端 success=false 直接抛。"""
    if getattr(result, "isError", False):
        raise RuntimeError(str(result))
    data = getattr(result, "structuredContent", None)
    if data is None:
        blocks = getattr(result, "contents", None) or getattr(result, "content", [])
        data = json.loads(next(block.text for block in blocks if hasattr(block, "text")))
    if not isinstance(data, dict) or data.get("success") is False:
        raise RuntimeError(str(data))
    return data.get("data", data)


def unpack_resource(result):
    """读资源的返回值形状与工具不同：ReadResourceResult 带 .contents，内容块才是 JSON。"""
    blocks = getattr(result, "contents", None)
    if blocks is None:
        blocks = result if isinstance(result, (list, tuple)) else None
    if blocks:
        block = next(item for item in blocks if getattr(item, "text", None))
        payload = json.loads(block.text)
    else:
        payload = result
    if isinstance(payload, dict) and payload.get("success") is False:
        raise RuntimeError(str(payload))
    if isinstance(payload, dict) and "data" in payload:
        return payload["data"]
    return payload


def select_instance(instances, root=ROOT):
    """本工程必须恰好一个 Unity 实例；多个就停下，绝不 set_active_instance 到别人工程。"""
    matches = [item["id"] for item in instances
               if Path(item["path"]).resolve() == (root / "Assets").resolve()]
    if len(matches) != 1:
        raise ValueError(f"目标工程需要恰好一个 Unity 实例，实际找到 {len(matches)} 个")
    return matches[0]


async def with_session(action, timeout=60):
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    config = configuration()
    parameters = StdioServerParameters(
        command=config["command"], args=["--offline", *config["args"]],
        cwd=str(ROOT), env={**os.environ, **config.get("env", {})},
    )
    async with stdio_client(parameters) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            listing = unpack_resource(await session.read_resource("mcpforunity://instances"))
            target = select_instance(listing["instances"])
            unpack(await session.call_tool("set_active_instance", {"instance": target}))
            project = unpack_resource(await session.read_resource("mcpforunity://project/info"))
            if Path(project["projectRoot"]).resolve() != ROOT:
                raise ValueError("MCP 返回了其他工程，停止")
            return await action(session)


async def call(session, name, arguments=None):
    try:
        return unpack(await session.call_tool(name, arguments or {}))
    except Exception as error:  # 工具名/参数不对时报原文，不吞
        return {"_error": f"{name}: {error!r}"}


def read_console(session):
    async def action(s):
        return await call(s, "read_console", {"action": "get", "types": ["error", "warning"], "count": 50})
    return action


async def do_console(session):
    """读控制台。read_console 返回的是日志条目数组，不是 dict —— 别按 dict 解。"""
    data = await call(session, "read_console",
                      {"action": "get", "types": ["error", "warning"], "count": 100})
    if isinstance(data, dict) and "_error" in data:
        return {"ok": False, "phase": "read_console", "detail": data["_error"]}
    logs = data
    if isinstance(logs, dict):
        logs = logs.get("logs") or logs.get("data") or []
    if not isinstance(logs, list):
        return {"ok": False, "phase": "read_console", "detail": f"日志形状异常：{type(logs).__name__}"}

    def level_of(entry):
        if not isinstance(entry, dict):
            return str(entry).lower()
        return str(entry.get("type") or entry.get("level") or entry.get("severity") or "").lower()

    def text_of(entry, limit):
        if isinstance(entry, dict):
            return str(entry.get("message", entry))[:limit]
        return str(entry)[:limit]

    errors = [x for x in logs if level_of(x).startswith("err")]
    warnings = [x for x in logs if level_of(x).startswith("warn")]
    return {"ok": True, "total": len(logs), "errors": len(errors), "warnings": len(warnings),
            "error_samples": [text_of(x, 300) for x in errors[:5]],
            "warning_samples": [text_of(x, 200) for x in warnings[:3]]}


async def do_state(session):
    """编辑器状态走资源；真实嵌套是 editor.play_mode.is_playing，不是顶层字段（实测）。"""
    state = unpack_resource(await session.read_resource("mcpforunity://editor/state"))
    if not isinstance(state, dict):
        return {"ok": False, "detail": f"状态形状异常：{type(state).__name__}"}
    editor = state.get("editor") or {}
    play = editor.get("play_mode") or {}
    activity = state.get("activity") or {}
    scene = editor.get("active_scene") or {}
    return {"ok": True,
            "is_playing": play.get("is_playing"),
            "is_paused": play.get("is_paused"),
            "is_changing": play.get("is_changing"),
            "active_scene": scene.get("path"),
            "phase": activity.get("phase"),
            "reasons": activity.get("reasons"),
            "schema_version": state.get("schema_version")}


async def do_refresh(session):
    result = await call(session, "refresh_unity", {"mode": "force", "scope": "all", "compile": "request"})
    return {"ok": "_error" not in result, "refresh": result}


async def do_test(session, mode, group, assembly, timeout_seconds):
    """跑定向测试：先确保 testing 工具组开着，再 run_tests → 轮询 get_test_job。

    实测形状（2026-10-07）：job 是扁平对象，计数在 progress.completed / progress.total，
    失败明细在 progress.failures_so_far[{full_name, message}]；没有 summary 字段。
    """
    activate = await call(session, "manage_tools", {"action": "activate", "group": "testing"})
    arguments = {"mode": mode}
    if group:
        arguments["group_names"] = [group]
    if assembly:
        arguments["assembly_names"] = [assembly]
    started = await call(session, "run_tests", arguments)
    # 共享编辑器一次只跑一个测试任务：撞上 tests_running 就按它给的 retry_after_ms 退避重试。
    attempts = 0
    while (isinstance(started, dict) and started.get("_error")
           and "tests_running" in str(started.get("_error")) and attempts < 20):
        attempts += 1
        await asyncio.sleep(5)
        started = await call(session, "run_tests", arguments)
    if isinstance(started, dict) and "_error" in started:
        return {"ok": False, "phase": "run_tests", "detail": started["_error"],
                "retries_waited": attempts, "activate": activate}
    job = started.get("job_id") or started.get("id") or (started.get("data") or {}).get("job_id")
    if not job:
        return {"ok": False, "phase": "run_tests", "detail": "没拿到 job_id", "raw": started}
    deadline = asyncio.get_event_loop().time() + timeout_seconds
    last = None
    while asyncio.get_event_loop().time() < deadline:
        await asyncio.sleep(3)
        last = await call(session, "get_test_job", {"job_id": job})
        if isinstance(last, dict) and "_error" in last:
            continue
        if str((last or {}).get("status", "")).lower() in ("succeeded", "failed", "completed", "finished"):
            break
    last = last if isinstance(last, dict) else {}
    progress = last.get("progress") or {}
    failures = progress.get("failures_so_far") or last.get("failures") or []
    completed = progress.get("completed")
    status = last.get("status")
    # 假绿判定：程序集编不出来时 run_tests 会返回 succeeded + completed 极小值，
    # 看着像全过，其实测试根本没跑（2026-10-07 实测踩到过）。
    stale_suspect = (status == "succeeded" and isinstance(completed, int) and completed <= 2)
    orphaned = (not completed) and "cleared" in str(last.get("error") or "").lower()
    return {"ok": True, "job_id": job, "status": status,
            "mode": last.get("mode"),
            "completed": completed,
            "declared_total": progress.get("total"),
            "last_finished": progress.get("last_finished_test_full_name"),
            "stuck_suspected": progress.get("stuck_suspected"),
            "stale_assembly_suspect": stale_suspect,
            "orphaned_job": orphaned,
            "verdict": ("孤儿任务：job 被清（多会话交错跑测试的副作用），本轮结果不可用，重跑一次"
                        if orphaned else
                        ("可疑：只跑了 %s 条，通常是测试程序集没编出来（DLL 陈旧），"
                         "先核对 Library/ScriptAssemblies 时间戳" % completed) if stale_suspect
                        else ("失败：%d 条未通过" % len(failures) if failures else "通过")),
            "failure_count": len(failures),
            "failures": [{"full_name": f.get("full_name"), "message": str(f.get("message"))[:500]}
                         for f in failures[:8] if isinstance(f, dict)],
            "error": last.get("error")}


def missing_meta():
    """查新增/改动文件里缺配套 .meta 的。

    硬规则：`.meta` 由 Unity 生成、不许手写（`ai-docs/project-guide.md` 硬规则 1）。
    缺 `.meta` 的文件 Unity 不认、提交时被 gc_scan 拦；每波都在手工查，容易漏。
    只对 git 未跟踪 + 已修改的 `.cs` 与资产类扩展名做检查（`Assets/` 下）。
    """
    import subprocess
    try:
        out = subprocess.run(
            ["git", "status", "--porcelain", "--untracked-files=all"],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8", timeout=60)
    except Exception as error:
        return {"ok": False, "detail": repr(error)}
    if out.returncode != 0:
        return {"ok": False, "detail": out.stderr.strip()[:200]}
    interesting = (".cs", ".asset", ".prefab", ".unity", ".mat", ".png", ".json", ".xml", ".xlsx")
    missing, checked = [], 0
    for line in out.stdout.splitlines():
        path = line[3:].strip().strip('"')
        if not path.startswith("Assets/"):
            continue
        if not path.endswith(interesting):
            continue
        if path.endswith(".meta"):
            continue
        checked += 1
        if not (ROOT / (path + ".meta")).is_file():
            missing.append(path)
    return {"ok": True, "checked": checked, "missing_count": len(missing), "missing": missing[:30]}


def assembly_stamps():
    """程序集时间戳：判断「测试真的编译进去了」比控制台可靠（控制台会被别的会话清空）。"""
    out = {}
    folder = ROOT / "Library/ScriptAssemblies"
    if not folder.is_dir():
        return out
    for name in ("Game.Core.dll", "Game.Runtime.dll", "Game.Tests.EditMode.dll"):
        path = folder / name
        if path.is_file():
            import datetime
            out[name] = datetime.datetime.fromtimestamp(path.stat().st_mtime).strftime("%Y-%m-%d %H:%M:%S")
    return out


def newest_source_mtime():
    """被测源码里最新的修改时间，用来和程序集时间戳比。"""
    import datetime
    newest = 0.0
    for folder in (ROOT / "Assets/_Project/Scripts/Runtime", ROOT / "Assets/_Project/Scripts/Tests/EditMode"):
        if not folder.is_dir():
            continue
        for path in folder.rglob("*.cs"):
            newest = max(newest, path.stat().st_mtime)
    return datetime.datetime.fromtimestamp(newest).strftime("%Y-%m-%d %H:%M:%S") if newest else None


def main():
    parser = argparse.ArgumentParser(description="Unity MCP 复核客户端")
    parser.add_argument("--console", action="store_true", help="读控制台 error/warning")
    parser.add_argument("--state", action="store_true", help="读编辑器状态（是否在 Play）")
    parser.add_argument("--refresh", action="store_true", help="强制刷新并请求编译")
    parser.add_argument("--meta", action="store_true", help="查新增/改动文件是否缺配套 .meta")
    parser.add_argument("--test", action="store_true", help="跑定向测试")
    parser.add_argument("--mode", default="EditMode", choices=["EditMode", "PlayMode"])
    parser.add_argument("--group", default="", help="EditMode 传 group_names，例如 Game.Tests.EditMode.Narrative")
    parser.add_argument("--assembly", default="", help="PlayMode 回放传 assembly_names")
    parser.add_argument("--test-timeout", type=int, default=600, help="测试最长等待秒数")
    args = parser.parse_args()

    if not any([args.console, args.state, args.refresh, args.test, args.meta]):
        parser.error("至少给一个动作：--console / --state / --refresh / --test / --meta")

    async def run(session):
        output = {}
        if args.refresh:
            output["refresh"] = await do_refresh(session)
        if args.state:
            output["state"] = await do_state(session)
        if args.console:
            output["console"] = await do_console(session)
        if args.test:
            output["test"] = await do_test(session, args.mode, args.group, args.assembly,
                                           args.test_timeout)
        return output

    try:
        output = asyncio.run(asyncio.wait_for(with_session(run), timeout=args.test_timeout + 120))
    except Exception as error:
        output = {"ok": False, "error": repr(error), "native_session_verified": False}
    output["transport"] = "temporary_stdio"
    # 硬锚点：程序集时间戳 vs 被测源码最新时间。DLL 比源码旧 = 测试没编进去 = 结果不可信。
    output["assemblies"] = assembly_stamps()
    output["newest_source"] = newest_source_mtime()
    if args.meta:
        output["meta"] = missing_meta()
    print(json.dumps(output, ensure_ascii=False, indent=2))
    failed = bool(output.get("ok") is False or output.get("error"))
    if isinstance(output.get("console"), dict) and output["console"].get("errors"):
        failed = True
    test = output.get("test")
    if isinstance(test, dict):
        if test.get("stale_assembly_suspect") or test.get("orphaned_job") or test.get("failure_count"):
            failed = True
    meta = output.get("meta")
    if isinstance(meta, dict) and meta.get("missing_count"):
        failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
