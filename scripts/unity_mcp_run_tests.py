"""Unity MCP 定向验证：run_tests / read_console 目前只有跨会话 CLI 通道，独立脚本便于复跑。

载体：`python scripts/unity_mcp_run_tests.py [--group <组名>] [--test <全名>] [--console N]`，人工/子 agent 显式调用，不后台保活。
锚点：stdout 的 JSON（测试计数、逐条失败原文与编辑器 error 日志由服务端返回，不本地推断）。
退场条件：harness 原生提供 Unity MCP 工具后删掉本脚本。

为什么单开一次连接跑测试、另开连接取结果：域重载会掐断 stdio 会话，
把「发起」和「取回」放同一次连接里会连着丢两次结果（实测 2026-10-07）。
"""
import argparse
import asyncio
import json
import os
from pathlib import Path
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
from unity_mcp_probe import configuration, select_instance, unpack  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]


async def _connect(session):
    listing = unpack(await session.read_resource("mcpforunity://instances"))
    target = select_instance(listing["instances"])
    unpack(await session.call_tool("set_active_instance", {"instance": target}))
    return target


async def start(group, tests, mode):
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    config = configuration()
    params = StdioServerParameters(
        command=config["command"], args=["--offline", *config["args"]],
        cwd=str(ROOT), env={**os.environ, **config.get("env", {})},
    )
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            target = await _connect(session)
            try:
                unpack(await session.call_tool("manage_tools", {"action": "activate", "group": "testing"}))
            except Exception as error:
                print(json.dumps({"manage_tools": repr(error)}, ensure_ascii=False), file=sys.stderr)
            request = {"mode": mode, "include_failed_tests": True, "init_timeout": 120000}
            if group:
                request["group_names"] = group
            if tests:
                request["test_names"] = tests
            started = unpack(await session.call_tool("run_tests", request))
            await asyncio.sleep(0.2)
            return target, started.get("job_id") or started.get("jobId"), started


async def wait_and_collect(job, console_size):
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    config = configuration()
    params = StdioServerParameters(
        command=config["command"], args=["--offline", *config["args"]],
        cwd=str(ROOT), env={**os.environ, **config.get("env", {})},
    )
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            await _connect(session)
            snapshot = None
            for _ in range(900):
                state = unpack(await session.read_resource("mcpforunity://editor/state"))
                if not (state.get("tests") or {}).get("is_running"):
                    snapshot = state.get("tests")
                    break
                await asyncio.sleep(1)
            result = None
            if job:
                try:
                    result = unpack(await session.call_tool(
                        "get_test_job", {"job_id": job, "include_failed_tests": True}))
                except Exception as error:
                    result = {"error": repr(error)}
            console = unpack(await session.call_tool(
                "read_console", {"action": "get", "types": ["error"], "count": str(console_size)}))
            return {"tests_state": snapshot, "tests": result, "console": console}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--group", default="Game.Tests.EditMode.Narrative")
    parser.add_argument("--test", action="append", default=None)
    parser.add_argument("--mode", default="EditMode")
    parser.add_argument("--console", type=int, default=20)
    args = parser.parse_args()
    try:
        target, job, started = asyncio.run(start(args.group, args.test, args.mode))
        print(json.dumps({"instance": target, "job_id": job, "start": started}, ensure_ascii=False))
        time.sleep(2)
        collected = None
        last = None
        for attempt in range(8):
            try:
                collected = asyncio.run(wait_and_collect(job, args.console))
                break
            except Exception as error:
                last = error
                print(json.dumps({"reconnect": attempt, "error": repr(error)}, ensure_ascii=False), file=sys.stderr)
                time.sleep(10)
        if collected is None:
            raise last
    except Exception as error:
        print(json.dumps({"ok": False, "error": repr(error)}, ensure_ascii=False))
        return 2
    print(json.dumps(collected, ensure_ascii=False, indent=2))
    tests = collected.get("tests") or {}
    failed = tests.get("failed") or tests.get("failedCount") or 0
    return 0 if tests and not failed else 1


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
