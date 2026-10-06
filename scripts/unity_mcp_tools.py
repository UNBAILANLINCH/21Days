"""列出 Unity MCP 工具与其输入 schema：`run_tests` / `get_test_job` 的可用参数只能由 schema 决定，
读源码猜参数会走错路。载体：人工/子 agent 显式调用；锚点：stdout 的 JSON；退场条件同 unity_mcp_run_tests.py。
"""
import asyncio
import json
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from unity_mcp_probe import configuration, select_instance, unpack  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
WANTED = ("run_tests", "get_test_job", "read_console", "manage_asset", "refresh_unity", "execute_code")


async def dump():
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
            listing = unpack(await session.read_resource("mcpforunity://instances"))
            target = select_instance(listing["instances"])
            unpack(await session.call_tool("set_active_instance", {"instance": target}))
            try:
                unpack(await session.call_tool("manage_tools", {"action": "activate", "group": "testing"}))
            except Exception as error:
                print(json.dumps({"activate": repr(error)}, ensure_ascii=False), file=sys.stderr)
            tools = await session.list_tools()
            return [{"name": tool.name, "description": (tool.description or "")[:400],
                     "schema": tool.input_schema} for tool in tools.tools if tool.name in WANTED]


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(asyncio.run(dump()), ensure_ascii=False, indent=2))
