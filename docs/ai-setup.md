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

## Claude Code 的 Unity MCP 接入

工程已在 `Packages/manifest.json` 声明 `com.coplaydev.unity-mcp`（v10.2.0），Claude Code 侧在 `.mcp.json` 用 `uvx` 拉起同版本的 `mcpforunityserver`。

前置：本机有 `uv`（提供 `uvx`）和 git。

1. 打开 Unity，等 Package Manager 解析完 git 依赖，菜单出现 `Window → MCP for Unity`。
2. 打开该窗口，确认 Unity 侧 bridge 状态为运行中。窗口里的「Configure All Detected Clients」会往用户级配置写东西，本工程已用 `.mcp.json` 走项目级配置，可以不点。
3. 在工程目录启动 Claude Code，输入 `/mcp` 确认 `UnityMCP` 状态为 connected。
4. 验证：让 Claude 用 `manage_scene` 读当前场景层级。

版本升级：同时改 `Packages/manifest.json` 的 tag 和 `.mcp.json` 的 `mcpforunityserver==` 版本，两边保持一致。

非 core 工具组（testing / ui / animation 等）默认关闭，需要时让 Claude 用 `manage_tools` 打开。

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
