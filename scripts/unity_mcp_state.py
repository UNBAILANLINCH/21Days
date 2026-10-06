"""读 Unity 编辑器状态资源（诊断 run_tests 是否被孤儿标志阻塞）。

载体：`python scripts/unity_mcp_state.py`，人工/子 agent 显式调用；锚点：stdout 的 JSON；退场条件同其它探针。
"""
import asyncio
import json
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from unity_mcp_probe import configuration, select_instance, unpack  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]


async def state(clear_stuck, console_size):
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
            snapshot = unpack(await session.read_resource("mcpforunity://editor/state"))
            cleared = None
            if clear_stuck:
                try:
                    cleared = unpack(await session.call_tool("run_tests", {"clear_stuck": True}))
                except Exception as error:
                    cleared = repr(error)
            console = unpack(await session.call_tool(
                "read_console", {"action": "get", "types": ["error"], "count": str(console_size)}))
            compile_errors = [line for line in console if ".cs(" in line and ": error CS" in line]
            return {"instance": target, "tests": snapshot.get("tests"), "activity": snapshot.get("activity"),
                    "compilation": snapshot.get("compilation"), "clear_stuck": cleared,
                    "console_error_count": len(console), "compile_error_count": len(compile_errors),
                    "compile_errors": compile_errors}


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    size = 200
    if "--console" in sys.argv:
        size = int(sys.argv[sys.argv.index("--console") + 1])
    print(json.dumps(asyncio.run(state("--clear-stuck" in sys.argv, size)), ensure_ascii=False, indent=2))
