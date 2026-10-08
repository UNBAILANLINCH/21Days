# AI 协作接入（一次性）

## Claude Code 与 Codex 共用项目

同一 Git 分支维护两套客户端入口，共用 [项目约定](../ai-docs/project-guide.md)、`ai-docs/` 模块文档和 `PRP/`。

| 项目 | Claude Code | Codex |
| --- | --- | --- |
| 启动指令 | `CLAUDE.md` | `AGENTS.md` |
| Unity MCP | `.mcp.json` | `.codex/config.toml` |
| 项目规则与流程 | 原有 `.claude/` 入口 | 按 `AGENTS.md` 主动读取相同文件 |
| 自动检查 | `.claude/settings.json` hooks | `.codex/hooks.json` 注册适配器；在 `/hooks` 信任后生效 |

### Codex 首次使用

项目技能注册在 `.agents/skills/`，原文及脚本仍在 `.claude/skills/` 共用。Codex 未显示新增技能时，重新开启会话或重启客户端。
可输入 `$unity-mcp 检查连接并读取当前场景层级`、`$project-lint 检查本次修改的 C#`、`$unity-code-review 审查当前改动` 或 `$evolution 检查引用完整性`。

全部九项：`build`、`evolution`、`generate-doc`、`new-feature`、`project-lint`、`review-change`、`unity-code-review`、`unity-mcp`、`unity-test`。
其中 `build`、`new-feature`、`review-change`、`unity-test` 保留 Claude 原有的仅显式调用策略，用 `$技能名` 选择；其余允许按描述自动选择。

### 自动机制状态

Codex 已通过 `.codex/hooks.json` 注册自动生成物拦截、必读文档账本与闸、规则提示、补丁后 lint、重复编辑提醒、收尾检查及压缩快照恢复。检查逻辑复用 `.claude/` 脚本，缓存隔离在 `.codex/.cache/`。
**首次启用：重新开启会话，在 `/hooks` 审查并信任本项目的六个事件定义。** `enabled` 但 `untrusted` 的 hooks 不会执行；未完成信任前不能称为自动机制已启用。
有两处适配差异：必读记账只接受约定的独立完整读取命令；原 Claude 的 ask 确认在 Codex 前置 hook 尚不支持，故改为阻止并提示用户手动执行敏感操作。完整覆盖范围与测试见 [hooks 说明](../.codex/hooks/README.md)。
原 `generate-doc/detect.py` 文档同步提醒在 Claude 的 `settings.json` 中也未启用；不能把脚本存在视为自动机制已运行。
参考：[Codex skills](https://learn.chatgpt.com/docs/build-skills)、[Codex hooks](https://learn.chatgpt.com/docs/hooks)。

### 连接验证步骤

1. 从本仓库根目录打开 Codex，按客户端提示将项目设为可信；项目级配置仅在可信项目中加载。
2. 新开会话，让 Codex「读取 AGENTS.md，说明共用规则的来源」。应读取 `ai-docs/project-guide.md`。
3. 在 MCP 设置中确认 `UnityMCP` 已加载；CLI 可用 `codex mcp list` 检查配置，TUI 可用 `/mcp` 查看状态。
4. 打开 Unity 并启动 MCP bridge，再让 Codex 读取当前场景层级；实际返回层级才算端到端连接成功。
5. 直接用自然语言要求测试、构建、审查或同步文档；原 Claude 斜杠命令没有自动注册到 Codex。

Codex 复用项目检查逻辑，但不继承 Claude 的模型派单和私有记忆。hooks 经信任后才提供程序检查；它们不是覆盖任意 shell、MCP 和外部工具的完整安全边界。
若 `python` 不在 PATH，但已通过 uv 安装 Python，PowerShell 可用 `$projectPython = uv python find`，再用 `& $projectPython .claude/skills/evolution/gc_scan.py` 执行检查。
若 MCP 列表另有用户级 `unityMCP`（小写）HTTP 连接，需区分它与本项目的 `UnityMCP` STDIO 配置；只选一个连接操作目标编辑器，不要重复执行写操作。
两个客户端交替操作同一分支即可；不要同时修改同一文件或同时写入同一 Unity 编辑器。

修改配置后重新开启会话。升级 Unity MCP 时同步修改 `Packages/manifest.json`、`.mcp.json` 和 `.codex/config.toml` 的版本。
配置参考：[Codex 项目指令](https://learn.chatgpt.com/docs/agent-configuration/agents-md)、[Codex MCP](https://learn.chatgpt.com/docs/extend/mcp)。

### 多窗口 MCP 维护与恢复

项目已用 `.codex/config.toml` 统一声明 UnityMCP；从同一可信项目启动的会话复用这份配置，无需每个窗口重新注册。配置存在、Unity bridge 运行、当前 Codex 会话握手成功是三个不同状态。只有当前会话实际读取编辑器成功，才能报告原生 MCP 已连接。

发生 `connection closed: initialize response` 时，先在 Codex 的 MCP 设置中重启 `UnityMCP` 连接，再在同一会话重新读取编辑器。当前会话没有重连工具时，使用客户端设置；仍未恢复则保存测试 job id 后重新开启会话。CLI 的 `codex mcp list/get` 检查配置，不能代替握手验证。不要反复重启已在工作的 Unity bridge。

可用只读探针区分 bridge 故障与 Codex 会话故障（使用已缓存的项目版本，不联网下载）：

```powershell
uvx --offline --from mcpforunityserver==10.2.0 python -B scripts/unity_mcp_probe.py
```

探针从项目配置启动临时 STDIO 客户端，按工程路径选择唯一实例，核对工程并读取编辑器状态；12 秒内未完成协议检查则失败。`ok: true` 且 `native_session_verified: false` 只证明临时通道可用，不会恢复 Codex 工具列表。若 uv 缓存被沙箱拒绝访问，按客户端权限流程处理，不能据此认定 Unity bridge 离线。缓存未准备好时，离线诊断失败，不自动安装。

**自动启动状态：探针尚未接入 SessionStart。** 本次修改受保护的 `.codex/hooks/adapter.py` 时，自动审批及一次重试均超时，未落地。拟接入已有 SessionStart（startup/resume）：每个会话只探测一次，外层限时 20 秒，将带时间的结果写入该会话缓存 `mcp-status.json`；失败只提示，压缩/清除上下文不重跑。接入后须验证实际启动输出，不能只凭脚本自测宣称自动启用。

Codex 的 MCP 类型 hooks 只调用已连接的服务器，**不能启动或重连服务器**；SessionStart 也可能早于 MCP 就绪，因此不适合作为永久保活机制。依据：[官方 hooks 限制](https://learn.chatgpt.com/docs/hooks)、[官方 MCP 管理](https://learn.chatgpt.com/docs/extend/mcp)。

多窗口采用以下顺序：各窗口独立检查自己的连接；指定一个窗口负责刷新、编译、测试和回放；测试期间其他窗口冻结 C# 与表生成物。保存测试 job id，连接中断后先查询原 job，避免重复启动测试。每批结束记录真实 passed/failed/skipped 和编译错误数；临时通道结果与原生恢复分开记录。升级 MCP 时同步 Unity 包和两端 server 版本，再跑探针及一次实际测试。

探针逻辑自测（无需连接 Unity）：

```powershell
$projectPython = uv python find
& $projectPython -B scripts/unity_mcp_probe.py --self-test
```

## Claude Code 的 Unity MCP 接入

工程已在 `Packages/manifest.json` 声明 `com.coplaydev.unity-mcp`（v10.2.0），Claude Code 侧在 `.mcp.json` 用 `uvx` 拉起同版本的 `mcpforunityserver`。

前置：本机有 `uv`（提供 `uvx`）和 git。

1. 打开 Unity，等 Package Manager 解析完 git 依赖，菜单出现 `Window → MCP for Unity`。
2. 打开该窗口，确认 Unity 侧 bridge 状态为运行中。窗口里的「Configure All Detected Clients」会往用户级配置写东西，本工程已用 `.mcp.json` 走项目级配置，可以不点。
3. 在工程目录启动 Claude Code，输入 `/mcp` 确认 `UnityMCP` 状态为 connected。
4. 验证：让 Claude 用 `manage_scene` 读当前场景层级。

版本升级：同时改 `Packages/manifest.json` 的 tag 和 `.mcp.json` 的 `mcpforunityserver==` 版本，两边保持一致。

非 core 工具组（testing / ui / animation 等）默认关闭，需要时让 Claude 用 `manage_tools` 打开。

### 传输与代理（2026-09-29 实测）

- **Unity 侧 bridge 走 Stdio 传输**（`127.0.0.1:6401`）：只有 stdio 的 MCP 客户端连得上。用 `--transport http` 起的服务端即使 `/health` 通过，也看不到实例（`/api/instances` 返回空、`/api/command` 返回 503）；包自带的 `unity-mcp` CLI 依赖这个 HTTP 模式，所以在当前配置下 CLI 用不了。要让某个客户端连上，用与 `.mcp.json` 一致的 stdio 配置，不要去改 Unity 窗口里的传输方式（会踢掉正在用的客户端）。
- **本机 `NO_PROXY` 含 `[::1]` 会让 httpx 直接崩**（Windows 代理覆盖表常见的 IPv6 回环写法）：`--transport http` 与 `unity-mcp` CLI 在打印版本横幅那一步就抛 `Invalid port: ':1]'`。要跑它们，先把环境变量收敛成 `NO_PROXY=localhost,127.0.0.1` 并在同一个 shell 里执行。
- 没有 MCP 客户端、又要动编辑器时，可以直接连桥：握手 `WELCOME UNITY-MCP 1 FRAMING=1\n`，之后每条消息 = 8 字节大端长度 + UTF-8 JSON 负载（`{"type":"<命令>","params":{…}}`），负载为纯文本 `ping` 时回 `pong`。这仍然是「写入编辑器」，照样受「同一时间只写一个编辑器」的约定约束。

## Blender MCP（Claude Code 与 Codex 共用）

工程在 `.mcp.json` 与 `.codex/config.toml` 各声明了一条 `blender`，两端都锁 `mcp-for-blender==2.1.9`。
与 UnityMCP 不同，这条**不依赖工程内任何包**——Blender 侧插件装在 Blender 自己的用户目录里，
服务端经 `localhost:9876` 连它，所以三个 agent（含 DSH）可以指向同一个 Blender 实例。

前置：

1. Blender 4.5（本机在 `D:\Blender`，绿色版）。
2. Blender 侧插件：`blender_mcp.py` 已在用户插件目录并启用。装的命令是
   `dsh-blender-mcp install-addon`（包在 `D:\work\dsh-blender-mcp`，独立仓库，不在本工程内）；
   它会让 Blender 自己报告真实插件目录，绿色版/装机版都适用。
3. **必须是 GUI 模式的 Blender**。插件在 `blender -b` 下会拒绝启动服务端
   （源码原话：`commands would never execute`），所以无头渲染自动化用不了这条通道。

验证：

1. 打开 Blender（插件默认自动启动服务端，无需手动点）。
2. `dsh-blender-mcp doctor` —— 逐项报告 Blender 定位、插件版本、端口握手，退出码 0 才算通。
   它完全只读，不装插件、不改偏好。
3. 在客户端确认 `blender` 已加载，再让它读当前场景层级；实际返回才算连上。

两个容易踩的点：

- **多个 agent 可以同时连**（插件 `listen(5)`，每连接一个线程），但命令都排进同一个队列、
  由 Blender 主线程串行执行。所以并发安全，但一个 agent 跑长任务时另一个会排队等。
- 端口 9876 由两侧共同决定：`.mcp.json` / `.codex/config.toml` 里服务端连的端口，与 Blender
  插件面板里监听的端口。改一处必须同时改另一处。

## Claude Code 钩子

钩子在 `.claude/settings.json` 注册，无需额外安装，只要本机有 python3 与 node 就会自动生效。七个钩子各管一件事，详细说明与调试方法见 [`.claude/hooks/README.md`](../.claude/hooks/README.md)。

| 钩子 | 一句话 |
| --- | --- |
| `guard.js` | 写入 / Bash 前拦截：Unity 生成物与 `.meta` 直接拒绝，`ProjectSettings/`、`Packages/`、`git commit` / `git push` 弹确认。 |
| `required-reads.py` | 编辑某模块前查「该模块的 guide 读了没」，没读就拦下并指出该读哪份（Read 工具读、或 `cat` / `head` / `sed -n` 读都算数）。 |
| `knowledge-routing.py` | 编辑匹配文件时提示适用的 `.claude/rules/` 规则与模块文档，省得手找。 |
| `project-lint`（`skills/project-lint/lint.py`） | 保存 `.cs` 后跑 C# 语义 lint，违规给行号 + 原因 + 修复建议。 |
| `doom-loop-detect.py` | **连续**编辑同一文件到阈值时预警（中间改过别的就清零），防止在错误方向上空转。 |
| `stop-check.py` | 会话收尾检查：改动的 `.cs` 里残留 `Debug.Break()` / `// TEMP` / `// HACK`、新资产缺 `.meta`、同一文件高频编辑未收敛。 |
| `precompact-save.py` | 上下文压缩前保存工作态快照，压缩后可恢复。 |

拦截理由都会显示在对话里。**被挡住先看理由再换做法，不拆护栏**；lint 确属误报时在该行写 `// lint-ok: <理由>` 放行。
