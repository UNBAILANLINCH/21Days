---
name: unity-code-review
description: 审查 21Days Unity 模块的依赖方向、每帧开销、生命周期、资产引用和测试覆盖；只读报告问题。
---

# unity-code-review — Codex 项目入口

先读取仓库根目录的 [AGENTS.md](../../../AGENTS.md) 与其引用的共用项目约定。
随后完整读取 [共用技能正文](../../../.claude/skills/unity-code-review/SKILL.md)，按其中与当前任务相关的流程执行。

正文中的仓库路径相对项目根；脚本及配套资源仍在 `.claude/skills/unity-code-review/`，不在本入口目录。
正文提到的 `CLAUDE.md` 项目约束使用共用项目约定；模型派单、私有记忆和 hooks 自动运行描述按 `AGENTS.md` 的 Codex 适配规则处理。不得声称未注册的 hooks 已运行。

同时读取 `.claude/agents/code-reviewer.md` 的审查清单。Claude 的代理名称、sonnet 模型和派单机制不适用于 Codex；按当前会话授权决定自行审查或委派，保留只读与分级报告要求。
