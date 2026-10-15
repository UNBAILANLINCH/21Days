# 本次标题接线用标准 MCP 客户端；现有会话未暴露 Unity 工具，不能扩展无关游戏代码补连接。
import asyncio
import json
import sys
import os
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

async def main():
    server = StdioServerParameters(command='uvx', args=['--offline', '--from', 'mcpforunityserver==10.2.0', 'mcp-for-unity', '--transport', 'stdio', '--default-instance', '21Days'])
    with open(os.devnull, 'w') as quiet:
      async with stdio_client(server, errlog=quiet) as streams:
        async with ClientSession(*streams) as session:
            await session.initialize()
            mode, name = sys.argv[1:3]
            if mode == 'read':
                result = await session.read_resource(name)
            elif mode == 'schema':
                if any(tool in name.split(',') for tool in ('run_tests', 'get_test_job')):
                    await session.call_tool('manage_tools', {'action': 'activate', 'group': 'testing'})
                result = await session.list_tools()
                print(json.dumps([{'name': t.name, 'inputSchema': t.input_schema} for t in result.tools if t.name in name.split(',')], ensure_ascii=False))
                return
            else:
                if mode == 'code':
                    name = 'execute_code'
                    with open(sys.argv[2], encoding='utf-8-sig') as f:
                        arguments = {'action': 'execute', 'code': f.read()}
                elif sys.argv[3].startswith('{'):
                    arguments = json.loads(sys.argv[3])
                else:
                    with open(sys.argv[3], encoding='utf-8-sig') as f:
                        arguments = json.load(f)
                group = {'execute_code': 'scripting_ext', 'run_tests': 'testing', 'get_test_job': 'testing'}.get(name)
                if group:
                    await session.call_tool('manage_tools', {'action': 'activate', 'group': group})
                result = await session.call_tool(name, arguments)
            print(result.model_dump_json())
            await asyncio.sleep(0.2)

asyncio.run(main())
