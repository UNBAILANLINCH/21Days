---
name: new-feature
description: 按 21Days 项目流程实现新玩法模块：范围、设计、实现、Unity 接线、测试与审查。
---

# new-feature — Codex 项目入口

先读取仓库根目录的 [AGENTS.md](../../../AGENTS.md) 与其引用的共用项目约定。
随后完整读取 [共用技能正文](../../../.claude/skills/new-feature/SKILL.md)，按其中与当前任务相关的流程执行。

正文中的仓库路径相对项目根；脚本及配套资源仍在 `.claude/skills/new-feature/`，不在本入口目录。
正文提到的 `CLAUDE.md` 项目约束使用共用项目约定；模型派单、私有记忆和 hooks 自动运行描述按 `AGENTS.md` 的 Codex 适配规则处理。不得声称未注册的 hooks 已运行。

需要模块审查时读取 `$unity-code-review` 对应的项目技能；只使用当前会话支持的执行方式，不指定 Claude 的 sonnet 模型。
