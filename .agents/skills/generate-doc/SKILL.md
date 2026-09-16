---
name: generate-doc
description: 为 21Days 玩法模块生成、增量同步或只读检查模块说明、公开接口及扩展文档。
---

# generate-doc — Codex 项目入口

先读取仓库根目录的 [AGENTS.md](../../../AGENTS.md) 与其引用的共用项目约定。
随后完整读取 [共用技能正文](../../../.claude/skills/generate-doc/SKILL.md)，按其中与当前任务相关的流程执行。

正文中的仓库路径相对项目根；脚本及配套资源仍在 `.claude/skills/generate-doc/`，不在本入口目录。
正文提到的 `CLAUDE.md` 项目约束使用共用项目约定；模型派单、私有记忆和 hooks 自动运行描述按 `AGENTS.md` 的 Codex 适配规则处理。不得声称未注册的 hooks 已运行。

原文中的 `modules.json` 与 `detect.py` 均位于 `.claude/skills/generate-doc/`。`detect.py` 尚未注册为两端的自动 hook；不要宣称已自动提醒。
