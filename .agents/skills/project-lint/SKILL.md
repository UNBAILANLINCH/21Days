---
name: project-lint
description: 运行或维护 21Days Unity/C# 项目语义 lint，检查每帧查找、序列化暴露和运行时依赖等反模式。
---

# project-lint — Codex 项目入口

先读取仓库根目录的 [AGENTS.md](../../../AGENTS.md) 与其引用的共用项目约定。
随后完整读取 [共用技能正文](../../../.claude/skills/project-lint/SKILL.md)，按其中与当前任务相关的流程执行。

正文中的仓库路径相对项目根；脚本及配套资源仍在 `.claude/skills/project-lint/`，不在本入口目录。
正文提到的 `CLAUDE.md` 项目约束使用共用项目约定；模型派单、私有记忆和 hooks 自动运行描述按 `AGENTS.md` 的 Codex 适配规则处理。不得声称未注册的 hooks 已运行。

`lint.py` 与 `rules.json` 均位于 `.claude/skills/project-lint/`。Codex 已注册补丁后的自动 lint hook，须在 `/hooks` 信任后生效；手动调用仍按原文 CLI 执行。不能把读取技能或仅注册 hook 当作 lint 已通过。
