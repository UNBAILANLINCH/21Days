---
description: 主窗口（Fable）与 subagent 的模型派单路由。任何 Agent / Workflow 派单、以及 .claude/agents/ 的 model 声明都适用。
paths: []
globs: []
alwaysApply: true
---

# 模型派单路由（主窗口纪律）

主窗口由 Fable 驱动。**Fable 只做：方案设计、任务规划与拆解、波次验收（审阅 subagent 产物、定下一波）**。除非用户显式说「你直接做」，Fable 不亲自下场做工程。

## 三档路由

| 档位 | model | 适用 |
| --- | --- | --- |
| 主窗口 | fable（仅主窗口，不派单） | 方案设计、规划拆解、波次验收、跨波协调 |
| 工程任务 | `opus` | 跨文件实现、改接口/契约、需要局部设计判断的实现、根因不明的调试 |
| 零散小模块 | `sonnet` | 做法已写死的单点执行、批量机械修改、检索/摸底/摘要 |

## 硬规则

1. **每次派单显式传 `model`**（Agent 工具的 `model` 参数 / Workflow `agent()` 的 `opts.model`）。用户没指定时由 Fable 按上表判定并写明，禁止留空靠默认继承。
2. **subagent 永不升级到 Fable**：任何派单不得传 `model: fable`。需要更高层判断时回主窗口验收，而不是升级 subagent。
3. `.claude/agents/` 自定义 agent 必须在 frontmatter 声明 `model:`（code-reviewer 固定 sonnet）。
4. 档位拿不准 → 就高不就低：先 `opus`；事后发现是机械活，下次降 `sonnet`。
5. Unity MCP 操作编辑器（建物体、挂组件、跑测试）属于「有明确步骤的执行」，可派 `sonnet`；但涉及场景结构设计的，先由主窗口定方案。

## 与 /dev 复杂度分档的对应

- 简单（单点、做法明确）→ 派 `sonnet` 执行。
- 中等（多文件、需要先想清楚）→ Fable 出方案，派 `opus` 实现，Fable 验收。
- 复杂（PRP 四阶段）→ Fable 主导 PRD/PRP 与验收；执行波次按任务粒度混用 `opus`/`sonnet`，逐单标注。

## 检查清单

- [ ] 每个 Agent / `agent()` 调用都带显式 `model`（或该 agent 的 frontmatter 已固定）。
- [ ] 没有任何 subagent 被派成 `fable`。
- [ ] 工程/调试 → `opus`；机械/检索 → `sonnet`；设计/规划/验收留在主窗口。
