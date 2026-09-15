---
name: unity-mcp
description: Unity 编辑器操作能力层——工具组开关、常用工具速查、改场景/预制体的纪律、编译错误怎么看、编辑器没开时的退路与故障排查。要读场景层级、建改 GameObject、挂组件赋引用、跑测试、看控制台、刷资产时先读这份。
---

# unity-mcp（编辑器操作能力层）

工程已接 MCP for Unity：Unity 侧包 `com.coplaydev.unity-mcp` v10.2.0（`Packages/manifest.json`），
Claude Code 侧在 `.mcp.json` 用 `uvx` 拉起同版本的 `mcpforunityserver`，服务器名 `UnityMCP`。
一次性接入步骤见 `docs/ai-setup.md`，这份讲**接好之后怎么用**。

> 下表工具名与工具组均已核实到源码：Unity 侧
> `Library/PackageCache/com.coplaydev.unity-mcp@*/Editor/Tools/` 里的 `[McpForUnityTool(...)]` 特性，
> 服务端 `mcpforunityserver` 10.2.0 的 `services/tools/`（`@mcp_for_unity_tool` 装饰器）与
> `services/registry/tool_registry.py`。参数细节以工具自身的 schema 为准。

## 连接状态怎么看

1. `/mcp` —— `UnityMCP` 显示 `connected` 才算通。没连上先看下面的「故障排查」。
2. 通了之后验一下：`manage_editor(action="telemetry_status")` 或直接读资源
   `mcpforunity://editor/state`，能返回就是真通到编辑器了。
3. **前提**：Unity 编辑器必须开着，且 `Window → MCP for Unity` 里 bridge 状态是运行中。
   MCP 是「遥控编辑器」，编辑器没开就什么都做不了。

## 工具组与 `manage_tools`

工具按组划分，**默认只开 `core` 一组**（源码里 `DEFAULT_ENABLED_GROUPS = {"core"}`）。
其它组的工具在列表里根本不出现，不是"调用失败"，是"看不见"。

| 组 | 装什么 | 默认 |
| --- | --- | --- |
| `core` | 场景、GameObject、组件、资产、脚本、编辑器状态、控制台、刷新、构建、包、材质、相机、物理、图形 | 开 |
| `testing` | `run_tests`、`get_test_job` | **关** |
| `scripting_ext` | `manage_scriptable_object`、`execute_code` | 关 |
| `docs` | `unity_reflect`、`unity_docs`（Unity API 反射与文档查询） | 关 |
| `vfx` | `manage_vfx`、`manage_shader`、`manage_texture` | 关 |
| `animation` | `manage_animation` | 关 |
| `ui` | `manage_ui`（UI Toolkit：UXML / USS / UIDocument） | 关 |
| `profiling` | `manage_profiler` | 关 |
| `probuilder` | `manage_probuilder`（需要 ProBuilder 包，实验性） | 关 |
| `asset_gen` | `generate_image` / `generate_audio` / `generate_model` / `import_model`（要自备 API key） | 关 |

开关用 `manage_tools`（它和 `set_active_instance` 一样永远可见，不属于任何组）：

| 调用 | 作用 |
| --- | --- |
| `manage_tools(action="list_groups")` | 列出所有组、各自的工具与当前开关状态 |
| `manage_tools(action="activate", group="testing")` | 打开一组（本 session 生效） |
| `manage_tools(action="deactivate", group="testing")` | 关掉一组 |
| `manage_tools(action="sync")` | 从 Unity 编辑器窗口里的勾选状态刷新一遍 —— 在编辑器 GUI 里改过开关后用这个 |
| `manage_tools(action="reset")` | 恢复默认（只剩 `core`） |

也可以读资源 `mcpforunity://tool-groups` 看全量清单。
**跑测试前必须先 `activate testing`**，否则 `run_tests` 压根不在工具列表里。

## 常用工具速查

### 读场景层级 / 找物体

| 要做什么 | 用什么 |
| --- | --- |
| 看当前场景结构 | `manage_scene(action="get_hierarchy")`；另有 `get_active`、`get_loaded_scenes`、`get_build_settings` |
| 按名字 / tag / layer / 组件类型 / 路径找物体 | `find_gameobjects` —— 返回 instance id（分页） |
| 拿某个物体的完整数据 | 资源 `mcpforunity://scene/gameobject/{instance_id}` |
| 拿某个物体挂的所有组件 | 资源 `mcpforunity://scene/gameobject/{instance_id}/components` |
| 拿单个组件的字段 | 资源 `mcpforunity://scene/gameobject/{instance_id}/component/{component_name}` |

**`manage_gameobject` 不负责搜索**，搜索一律走 `find_gameobjects`；
**组件数据用资源读，不用工具读**。这是这套工具的设计分工，走错了会绕远路。

### 建 / 改 GameObject，挂组件赋引用

| 要做什么 | 用什么 |
| --- | --- |
| 建、改、删、复制物体；相对移动；朝向某处 | `manage_gameobject(action="create"\|"modify"\|"delete"\|"duplicate"\|"move_relative"\|"look_at")` |
| 挂组件 / 摘组件 / 给组件字段赋值（含赋引用） | `manage_components(action="add"\|"remove"\|"set_property")`，要 `target`（instance id 或名字）+ `component_type` |
| 场景增删改 | `manage_scene(action="create"\|"load"\|"save"\|"close_scene"\|"set_active_scene"\|"move_to_scene"\|"validate")` |
| 预制体 | `manage_prefabs`：`get_info` / `get_hierarchy` / `create_from_gameobject` / `modify_contents`（无头改，适合脚本化）/ `open_prefab_stage` + `save_prefab_stage` + `close_prefab_stage`（交互式改） |
| tag / layer 增删、进出 Play 模式、undo/redo | `manage_editor(action="add_tag"\|"add_layer"\|"play"\|"pause"\|"stop"\|"undo"\|"redo"…)` |

赋引用的常见形态：`manage_components(action="set_property", target=<id>, component_type="PlayerMovement", ...)`，
被赋的值传目标物体的 instance id 或资产路径。参数名以工具 schema 为准。

### 脚本与资产

| 要做什么 | 用什么 |
| --- | --- |
| 新建 C# 脚本 | `create_script(path=..., contents=..., namespace=...)` |
| 改脚本 | 优先 `script_apply_edits`（结构化 / 锚点式，安全）；`apply_text_edits` 是精确行列替换，改前先读确认 |
| 在脚本里定位模式 | `find_in_file` |
| 删 / 校验脚本 | `delete_script` / `validate_script` |
| 资产增删改查 | `manage_asset(action="import"\|"create"\|"modify"\|"delete"\|"duplicate"\|"move"\|"rename"\|"search"\|"get_info"\|"create_folder")` |
| ScriptableObject 资产 | `manage_scriptable_object`（`scripting_ext` 组，先开）|
| 材质 | `manage_material` |

> **脚本文件用普通 Edit/Write 工具改也完全可以**，而且本工程的钩子（guard / lint / required-reads）
> 只挂在 Edit/Write 上 —— 走 MCP 改脚本会绕过这些护栏。
> **结论：`.cs` 用 Edit/Write 改，改完 `refresh_unity` 让 Unity 编译。** MCP 的脚本工具留给
> 「编辑器开着、要立刻看编译结果」的场合。

### 看控制台 / 刷新 / 跑测试

| 要做什么 | 用什么 |
| --- | --- |
| 读控制台（编译错误、运行时报错） | `read_console(action="get")`，默认最近 10 条，用 `page_size` / `cursor` 翻页；`count` 传字符串（如 `"5"`）兼容性最好 |
| 清控制台 | `read_console(action="clear")` —— 只清编辑器里的缓冲，日志文件还在 |
| 刷资产 / 触发编译 | `refresh_unity(mode="if_dirty"\|"force", scope="assets"\|"scripts"\|"all", compile="none"\|"request", wait_for_ready=true)` |
| 跑测试 | `run_tests(mode="EditMode"\|"PlayMode", test_names=..., group_names=...)` —— **异步**，立刻返回 `job_id` |
| poll 测试结果 | `get_test_job(job_id=...)` |
| 看有哪些测试 | 资源 `mcpforunity://tests` / `mcpforunity://tests/{mode}` |

`run_tests` / `get_test_job` 在 `testing` 组，**先 `manage_tools(action="activate", group="testing")`**。
跑测试的完整流程见 `.claude/skills/unity-test/SKILL.md`。

### 其它有用的资源

`mcpforunity://editor/state`（编辑器状态）、`mcpforunity://editor/selection`（当前选中）、
`mcpforunity://project/tags`、`mcpforunity://project/layers`、`mcpforunity://menu-items`、
`mcpforunity://prefab/{encoded_path}/hierarchy`、`mcpforunity://instances`（多开的 Unity 实例）。

多开编辑器时用 `set_active_instance` 指定这个 session 操作哪一个。

## 纪律（违反会留下别人收拾不了的烂摊子）

1. **改场景 / 预制体一律走 MCP，不手改 YAML。** `.unity` / `.prefab` 是 Unity 序列化出来的，
   里面全是 `fileID` 与 `guid` 的交叉引用，手改必然对不上，表现是 Inspector 上一片
   `Missing (Mono Script)`，而且 git 历史里看不出哪里错了。编辑器没开就把接线步骤列给用户做，
   **不要退而求其次去手改 YAML**。
2. **`.meta` 与 guid 永不手写。** `CLAUDE.md` 硬规则 1，钩子也会拒。新建的 `.cs` 需要 Unity
   刷新一次才有 `.meta`，没有 `.meta` 就提交，别人拉下来 guid 全变，所有引用断光。
3. **编译错误先 `read_console`，不要靠猜。** 改完 `.cs` 的顺序固定：
   `refresh_unity(scope="scripts", compile="request", wait_for_ready=true)` → `read_console(action="get")`
   → 有错就按报错行改。没这一步就宣称「改好了」是空头支票。
4. **不在 Play 模式下改资产。** Play 模式里对场景 / SO 的改动退出时会被丢弃（对 SO 反而会被写进资产，
   更糟），两种结果都不是你想要的。先 `manage_editor(action="stop")` 退出 Play 再改。
   动手前不确定在不在 Play 模式，读 `mcpforunity://editor/state`。
5. **批量操作前先读现状。** `manage_gameobject(action="modify")` 是覆盖式的，
   先 `find_gameobjects` + 读组件资源确认目标与当前值，再改。
6. **`execute_code` 是最后手段。** 它能在编辑器里跑任意 C#，绕过一切护栏。
   有专门工具就用专门工具；真要用，先说清楚为什么别的工具做不到。

## 编辑器没开时的退路

| 想做的事 | 编辑器没开时怎么办 |
| --- | --- |
| 写 / 改脚本 | 照常用 Edit/Write 改，工程里写完就行。攒着，等编辑器开了再刷新编译 |
| 建物体、挂组件、赋引用 | **做不了**。把接线步骤逐条列给用户（物体名 / 组件 / 字段 / 要赋的值），让他在编辑器里做 |
| 看编译错误 | 做不了。让用户打开编辑器看控制台，或把报错贴过来 |
| 跑测试 | 批处理模式（`Unity.exe -batchmode -runTests …`，见 `unity-test` skill）。**编辑器开着时工程被锁，批处理必然失败，别重试** |

判断编辑器开没开：`/mcp` 显示 connected 但工具调用超时/报 bridge 错，多半是编辑器关了或 bridge 停了。

## 故障排查

| 现象 | 多半是 | 怎么办 |
| --- | --- | --- |
| `/mcp` 里 `UnityMCP` 不是 connected | `uvx` 拉不起服务端（没装 uv、没网、版本名写错） | 本机装 `uv`；手动跑一次 `uvx --from mcpforunityserver==10.2.0 mcp-for-unity --transport stdio` 看报什么 |
| connected 但工具调用报连不上 Unity | Unity 侧 bridge 没启动 | 打开 Unity，`Window → MCP for Unity`，确认 bridge 运行中；Unity 编译中 / 域重载时会短暂断开，等一下重试 |
| 工具列表里没有要用的工具 | 它所在的组没开 | `manage_tools(action="list_groups")` 看，再 `activate` 对应组 |
| 在编辑器 GUI 里勾了工具但这边还是看不到 | 开关没同步过来 | `manage_tools(action="sync")` |
| 行为诡异、报错提到版本 / 协议不匹配 | 两侧版本不一致 | **`Packages/manifest.json` 的 `#v<版本>` tag 与 `.mcp.json` 的 `mcpforunityserver==<版本>` 必须同时改、保持一致**。改完重启 Claude Code，并让 Unity 重新解析包 |
| 工具返回超时 | Unity 正在编译或域重载 | 等编译完；长任务（构建、测试）本来就是异步的，用 poll 而不是干等 |
| 编辑器里点了「Configure All Detected Clients」 | 它往用户级配置写东西 | 本工程用项目级 `.mcp.json`，不需要点。已经点了就检查用户级配置有没有写进冲突的 `UnityMCP` 条目 |
| connected 但实例列表 `instance_count: 0`，Unity 窗口却显示 Session Active | Unity 侧 `Transport` 是 HTTPLocal，与 `.mcp.json` 的 stdio 不一致 | 窗口里改成 `Stdio`（先 `Stop Server`）；多个 Unity 同开时只 `set_active_instance` 到本工程。细节见 `ai-docs/pitfalls.md #MCP for Unity 两侧传输方式不一致` |

排查时不要顺手改 `Packages/manifest.json` —— 那是 `CLAUDE.md` 硬规则 3 的范围，
先说明为什么要改，钩子也会弹确认。
