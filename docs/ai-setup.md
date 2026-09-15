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

`.claude/hooks/guard.js` 在每次文件写入 / Bash 前运行：Unity 生成物与 `.meta` 直接拒绝，`ProjectSettings/`、`Packages/`、`git commit` / `git push` 弹确认。拦截理由会显示在对话里。
