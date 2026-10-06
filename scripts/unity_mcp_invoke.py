"""通用 Unity MCP 调用器：一次连接里调一个工具（或读一个资源），把服务端返回原样打到 stdout。

为什么单开一个（复用 → 扩展 → 新建）：
  1. 复用不行：`unity_mcp_probe` 只探连接、`unity_mcp_state` 只读编辑器状态、`unity_mcp_run_tests`
     只跑测试、`unity_mcp_reimport` 只导资产，没有一个能调任意工具（读场景层级、找物体、
     改组件字段都要用）。
  2. 扩展不行：把「任意工具」塞进上面任一个，都会让那个脚本的职责（探针 / 状态 / 测试 / 导入）变糊；
     它们的目的正是「窄而可核对」。
  3. 新建：以上两条都不成立，故新建一个只做「传参 → 调用 → 打印」的薄壳。

载体：`python scripts/unity_mcp_invoke.py <tool> '<json 参数>'`，或
      `python scripts/unity_mcp_invoke.py --resource mcpforunity://editor/state`，或
      `python scripts/unity_mcp_invoke.py --list manage_scene`（列工具与输入 schema）；
      人工 / 子 agent 显式调用，不后台保活。
锚点：stdout 的 JSON（服务端原文）与退出码；不本地推断编辑器状态。
退场条件：harness 原生提供 Unity MCP 工具后删掉本脚本。
"""
import argparse
import asyncio
import json
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from unity_mcp_probe import configuration, select_instance, unpack  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]

# 本机代理变量会让「桥断了」变成假象：带着 NO_PROXY/HTTP_PROXY/HTTPS_PROXY 调 MCP 时，
# 服务端与编辑器之间的 stdio 会话当场断掉，客户端只看到 `MCPError(-32000, 'Connection closed')`
# ——看起来像 Unity 桥挂了，其实桥一直活着（2026-10-07 用「同机同时刻、只改这几个变量」的对照实验定案：
# 带变量 exit=1，清掉 exit=0 且返回一千多字节的 JSON）。
# 所以本脚本在起连接之前先把它们从**本进程**剥掉，子进程（uvx / mcp-for-unity）自然继承干净的环境。
_PROXY_VARS = ("NO_PROXY", "no_proxy", "HTTP_PROXY", "HTTPS_PROXY",
               "http_proxy", "https_proxy", "ALL_PROXY", "all_proxy")
for _name in _PROXY_VARS:
    os.environ.pop(_name, None)


def tolerant_unpack(result):
    """`unpack` 的宽松版：服务端返回非 JSON 文本时原样带出来，方便定位（不吞掉原文）。"""
    try:
        return unpack(result)
    except Exception as error:
        blocks = getattr(result, "contents", None) or getattr(result, "content", []) or []
        texts = [getattr(block, "text", None) for block in blocks]
        return {"unpack_error": repr(error),
                "isError": bool(getattr(result, "isError", False)),
                "structuredContent": getattr(result, "structuredContent", None),
                "text": [text for text in texts if text is not None]}


def _params():
    from mcp import StdioServerParameters

    config = configuration()
    return StdioServerParameters(
        command=config["command"], args=["--offline", *config["args"]],
        cwd=str(ROOT), env={**os.environ, **config.get("env", {})},
    )


async def _connect(session):
    listing = unpack(await session.read_resource("mcpforunity://instances"))
    target = select_instance(listing["instances"])
    unpack(await session.call_tool("set_active_instance", {"instance": target}))
    return target


async def invoke(tool, arguments, resource):
    from mcp import ClientSession
    from mcp.client.stdio import stdio_client

    async with stdio_client(_params()) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            target = await _connect(session)
            if resource:
                return {"instance": target, "resource": resource,
                        "result": unpack(await session.read_resource(resource))}
            return {"instance": target, "tool": tool,
                    "result": tolerant_unpack(await session.call_tool(tool, arguments))}


async def list_tools(filter_text):
    from mcp import ClientSession
    from mcp.client.stdio import stdio_client

    async with stdio_client(_params()) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            target = await _connect(session)
            tools = await session.list_tools()
            picked = [{"name": tool.name, "schema": tool.input_schema,
                       "description": (tool.description or "")[:300]} for tool in tools.tools
                      if not filter_text or filter_text in tool.name]
            return {"instance": target, "count": len(picked), "tools": picked}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("tool", nargs="?", help="MCP 工具名，如 manage_scene")
    parser.add_argument("arguments", nargs="?", default="{}", help="工具参数的 JSON 文本")
    parser.add_argument("--resource", default=None, help="改读资源 URI，如 mcpforunity://editor/state")
    parser.add_argument("--list", default=None, help="列出工具及其输入 schema；可给名字子串过滤")
    parser.add_argument("--args-file", default=None,
                        help="从文件读工具参数 JSON（多行 / 含引号的载荷走这条，避开 PowerShell 的原生参数转义）")
    args = parser.parse_args()
    if not args.list and not args.resource and not args.tool:
        parser.error("要么给工具名，要么给 --resource，要么给 --list")

    payload = args.arguments
    if args.args_file:
        with open(args.args_file, "r", encoding="utf-8") as stream:
            payload = stream.read()

    async def run():
        if args.list is not None:
            return await list_tools(args.list)
        return await invoke(args.tool, json.loads(payload), args.resource)

    try:
        output = asyncio.run(run())
    except Exception as error:
        print(json.dumps({"ok": False, "error": repr(error)}, ensure_ascii=False))
        return 2
    print(json.dumps(output, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
