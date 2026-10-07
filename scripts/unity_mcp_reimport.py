"""请求 Unity 重新导入指定资产，促使脚本重新编译（编辑器未自动感知外部改动时用）。

载体：`python scripts/unity_mcp_reimport.py <资产路径>...`，人工/子 agent 显式调用，不后台保活。
锚点：stdout 的 JSON 与随后 `Library/ScriptAssemblies/*.dll` 的时间戳变化；退场条件：编辑器能自动刷新外部改动后删除。
"""
import asyncio
import json
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from unity_mcp_probe import configuration, select_instance, unpack  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]


async def reimport(paths):
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
            results = []
            for path in paths:
                try:
                    results.append({"path": path, "result": unpack(await session.call_tool(
                        "manage_asset", {"action": "import", "path": path}))})
                except Exception as error:
                    results.append({"path": path, "error": repr(error)})
            return {"instance": target, "imports": results}


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(asyncio.run(reimport(sys.argv[1:])), ensure_ascii=False, indent=2))
