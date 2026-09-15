# AI 协作接入（一次性）

## Unity MCP

工程已在 `Packages/manifest.json` 声明 `com.coplaydev.unity-mcp`（v10.2.0），Claude Code 侧在 `.mcp.json` 用 `uvx` 拉起同版本的 `mcpforunityserver`。

前置：本机有 `uv`（提供 `uvx`）和 git。

1. 打开 Unity，等 Package Manager 解析完 git 依赖，菜单出现 `Window → MCP for Unity`。
2. 打开该窗口，确认 Unity 侧 bridge 状态为运行中。窗口里的「Configure All Detected Clients」会往用户级配置写东西，本工程已用 `.mcp.json` 走项目级配置，可以不点。
3. 在工程目录启动 Claude Code，输入 `/mcp` 确认 `UnityMCP` 状态为 connected。
4. 验证：让 Claude 用 `manage_scene` 读当前场景层级。

版本升级：同时改 `Packages/manifest.json` 的 tag 和 `.mcp.json` 的 `mcpforunityserver==` 版本，两边保持一致。

非 core 工具组（testing / ui / animation 等）默认关闭，需要时让 Claude 用 `manage_tools` 打开。

## 钩子

钩子在 `.claude/settings.json` 注册，无需额外安装，只要本机有 python3 与 node 就会自动生效。七个钩子各管一件事，详细说明与调试方法见 [`.claude/hooks/README.md`](../.claude/hooks/README.md)。

| 钩子 | 一句话 |
| --- | --- |
| `guard.js` | 写入 / Bash 前拦截：Unity 生成物与 `.meta` 直接拒绝，`ProjectSettings/`、`Packages/`、`git commit` / `git push` 弹确认。 |
| `required-reads.py` | 编辑某模块前查「该模块的 guide 读了没」，没读就拦下并指出该读哪份。 |
| `knowledge-routing.py` | 编辑匹配文件时提示适用的 `.claude/rules/` 规则与模块文档，省得手找。 |
| `project-lint`（`skills/project-lint/lint.py`） | 保存 `.cs` 后跑 C# 语义 lint，违规给行号 + 原因 + 修复建议。 |
| `doom-loop-detect.py` | 同一文件反复编辑到阈值时预警，防止在错误方向上空转。 |
| `stop-check.py` | 会话收尾检查：改动的 `.cs` 里残留 `Debug.Break()` / `// TEMP` / `// HACK`、新资产缺 `.meta`、同一文件高频编辑未收敛。 |
| `precompact-save.py` | 上下文压缩前保存工作态快照，压缩后可恢复。 |

拦截理由都会显示在对话里。**被挡住先看理由再换做法，不拆护栏**；lint 确属误报时在该行写 `// lint-ok: <理由>` 放行。
