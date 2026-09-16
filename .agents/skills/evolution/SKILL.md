---
name: evolution
description: 为 21Days 项目沉淀纠错经验、维护规则或检查 AI 工作流引用完整性。
---

# evolution — Codex 项目入口

先读取仓库根目录的 [AGENTS.md](../../../AGENTS.md) 与其引用的共用项目约定。
随后完整读取 [共用技能正文](../../../.claude/skills/evolution/SKILL.md)，按其中与当前任务相关的流程执行。

正文中的仓库路径相对项目根；脚本及配套资源仍在 `.claude/skills/evolution/`，不在本入口目录。
正文提到的 `CLAUDE.md` 项目约束使用共用项目约定；模型派单、私有记忆和 hooks 自动运行描述按 `AGENTS.md` 的 Codex 适配规则处理。不得声称未注册的 hooks 已运行。

经验沉淀读取 `.claude/commands/learn.md`；健康检查读取 `.claude/commands/gc.md`。项目知识写入仓库共用文档，不写入 Claude 私有记忆。
