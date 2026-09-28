"""Unity MCP 只读诊断：已有检查没有端到端探针，独立脚本便于跨会话复用。

载体：人工诊断命令；尚未接入 SessionStart，不提供后台保活。
锚点：本次命令的 JSON 输出与退出码；不把临时连接记作 Codex 原生连接。
退场条件：Codex 原生诊断可稳定核对目标工程、编辑器状态及握手失败原因。
"""
import asyncio
import json
import os
from pathlib import Path
import sys
import tomllib

ROOT = Path(__file__).resolve().parents[1]


def configuration(root=ROOT):
    with (root / ".codex/config.toml").open("rb") as stream:
        config = tomllib.load(stream)["mcp_servers"]["UnityMCP"]
    args = config.get("args", [])
    if (config.get("enabled") is False or config.get("command") != "uvx"
            or len(args) != 5 or args[0] != "--from"
            or not args[1].startswith("mcpforunityserver==")
            or args[2:] != ["mcp-for-unity", "--transport", "stdio"]):
        raise ValueError("此诊断仅支持项目现有的固定版本 uvx STDIO 配置")
    return config


def unpack(result):
    if getattr(result, "isError", False):
        raise RuntimeError(str(result))
    data = getattr(result, "structuredContent", None)
    if data is None:
        blocks = getattr(result, "contents", None) or getattr(result, "content", [])
        data = json.loads(next(block.text for block in blocks if hasattr(block, "text")))
    if not isinstance(data, dict) or data.get("success") is False:
        raise RuntimeError(str(data))
    return data.get("data", data)


def select_instance(instances, root=ROOT):
    matches = [item["id"] for item in instances
               if Path(item["path"]).resolve() == (root / "Assets").resolve()]
    if len(matches) != 1:
        raise ValueError(f"目标工程需要恰好一个 Unity 实例，实际找到 {len(matches)} 个")
    return matches[0]


async def probe():
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    config = configuration()
    params = StdioServerParameters(
        command=config["command"], args=["--offline", *config["args"]],
        cwd=str(ROOT), env={**os.environ, **config.get("env", {})},
    )
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            async def check():
                await session.initialize()
                listing = unpack(await session.read_resource("mcpforunity://instances"))
                target = select_instance(listing["instances"])
                unpack(await session.call_tool("set_active_instance", {"instance": target}))
                project = unpack(await session.read_resource("mcpforunity://project/info"))
                if Path(project["projectRoot"]).resolve() != ROOT:
                    raise ValueError("MCP 返回了其他工程，停止诊断")
                state = unpack(await session.read_resource("mcpforunity://editor/state"))
                return {"ok": True, "instance": target, "state": state,
                        "transport": "temporary_stdio", "native_session_verified": False}

            result = await asyncio.wait_for(check(), timeout=12)
            # 接收关闭前的末尾通知，避免 SDK 的读取任务先于通知退出。
            await asyncio.sleep(0.1)
            return result


def self_test():
    from types import SimpleNamespace

    configuration()
    assert unpack(SimpleNamespace(contents=[SimpleNamespace(text='{"success": true}')]))["success"]
    assert unpack(SimpleNamespace(structuredContent={"success": True, "data": {"projectRoot": "test"}}))["projectRoot"] == "test"
    assert select_instance([{"id": "target", "path": str(ROOT / "Assets")}]) == "target"
    for entries in ([], [{"id": "other", "path": str(ROOT / "elsewhere")}],
                    [{"id": str(i), "path": str(ROOT / "Assets")} for i in range(2)]):
        try:
            select_instance(entries)
        except ValueError:
            continue
        raise AssertionError("缺失、错误或歧义实例不得放行")
    try:
        unpack(SimpleNamespace(structuredContent={"success": False}))
    except RuntimeError:
        return {"self_test": "passed"}
    raise AssertionError("服务端失败不得放行")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    try:
        output = self_test() if sys.argv[1:] == ["--self-test"] else asyncio.run(probe())
    except Exception as error:
        output = {"ok": False, "error": repr(error), "native_session_verified": False}
    print(json.dumps(output, ensure_ascii=False))
    sys.exit(0 if output.get("ok") or output.get("self_test") == "passed" else 1)
