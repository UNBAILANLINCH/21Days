---
name: unity-mcp
description: 操作 21Days Unity 编辑器，检查 MCP 连接、读取场景与控制台、修改对象及管理工具组。
---

# unity-mcp — Codex 项目入口

先读取仓库根目录的 [AGENTS.md](../../../AGENTS.md) 与其引用的共用项目约定。
随后完整读取 [共用技能正文](../../../.claude/skills/unity-mcp/SKILL.md)，按其中与当前任务相关的流程执行。

正文中的仓库路径相对项目根；脚本及配套资源仍在 `.claude/skills/unity-mcp/`，不在本入口目录。
正文提到的 `CLAUDE.md` 项目约束使用共用项目约定；模型派单、私有记忆和 hooks 自动运行描述按 `AGENTS.md` 的 Codex 适配规则处理。不得声称未注册的 hooks 已运行。

Codex 项目连接定义在 `.codex/config.toml`；以本会话实际工具和参数为准。Codex 自动检查覆盖 `apply_patch` 与 Bash 指定路径；MCP 资产写入不在该适配器的自动文件检查范围。实际读取编辑器成功后才能报告已连接。
