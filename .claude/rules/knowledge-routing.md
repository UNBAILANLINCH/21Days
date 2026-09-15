---
description: 知识检索路由细则：什么场景读哪份文档，保证渐进式加载、上下文最小化。
paths: []
globs: []
alwaysApply: true
---

# 知识检索路由

核心：**任何时刻只加载当前任务相关的知识**。三级渐进加载：

| 层级 | 加载时机 | 内容 |
| --- | --- | --- |
| L1 全局 | 会话启动 | `CLAUDE.md`（硬规则、路由、入口） |
| L2 模式匹配 | 编辑匹配文件时 | `.claude/rules/` 按 glob 注入（编辑 `.cs` → csharp-code；编辑场景/预制体 → unity-assets；编辑 Tests → unity-tests） |
| L3 模块级 | 编辑某模块前 | `ai-docs/docs/modules/<模块>/` 三件套；guide 存在时钩子强制先读 |

## 决策树

```
要做的事是……
├─ 改某个玩法模块的代码        → 先读 ai-docs/docs/modules/<模块>/<模块>-module-guide.md（有则必读）
├─ 跨模块调用某能力            → 读对方的 <模块>-external-api.md
├─ 给模块加扩展点 / 新功能     → 读 <模块>-extension-guide.md
├─ 通用 C# / Unity 资产规范    → .claude/rules/（glob 命中自动注入，不用手找）
├─ 操作编辑器 / 跑测试         → .claude/skills/unity-mcp/SKILL.md · /unity-test
├─ 怕重踩坑                    → ai-docs/pitfalls.md
└─ 完全不确定                  → ai-docs/docs/catalog.md
```

## 读取最小化

- 只读当前场景对应的那一份；模块文档读 guide 即可开始，跨模块调用才读对方的 external-api。
- 模块 guide 不存在（模块刚建、还没 `/generate-doc`）时直接动手，写完记得 `/generate-doc <模块>`。
- 规则文件 < 150 行；常驻规则 < 120 行。写不下就下沉到 `ai-docs/`。
