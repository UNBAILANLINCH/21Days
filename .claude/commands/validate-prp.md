---
description: PRP 阶段 3 — 执行前逐项校验 PRP 的完整性、Unity 工程合规性与可行性
argument-hint: <feature-name>
---

# /validate-prp — PRP 阶段 3：验证

特性名：**$1**

动手实现前，对 `PRP/$1/prp.md` 与 `PRP/$1/tasks.md` 做一次硬性校验。发现缺口就回 `/generate-prp $1` 补，**不带病执行**。

## 校验清单

### 上下文与流程
- [ ] **上下文充分**：PRD 涉及的模块三件套（或「文档不存在」的说明）、相关 `.claude/rules/`、`ai-docs/pitfalls.md` 都已纳入，无关键缺失。
- [ ] **复用优先**：已确认能复用 / 扩展的既有脚本、组件、ScriptableObject；走到「新建」的每一处都写了前两步为什么不行。
- [ ] **任务可执行**：tasks.md 每项文件级、有序、产出明确，无「魔法步骤」；新建目录 / asmdef 是独立任务。
- [ ] **验证可度量**：验证清单逐条可观测，覆盖 PRD 全部验收标准。
- [ ] **风险与回滚**：识别了主要风险与回滚路径。

### Unity 工程合规（本工程专属，逐条必查）
- [ ] **asmdef 依赖方向合规**：新增 / 修改的程序集引用满足 `Game.Tests.* → Game.Runtime`、`Game.Editor → Game.Runtime`，`Game.Runtime` 不引用 Editor / Tests、不 `using UnityEditor`（调试 Gizmos 用 `#if UNITY_EDITOR` 包住）。
- [ ] **序列化字段不 public**：Inspector 可调字段全部 `[SerializeField] private`，对外只读用属性；没有 `public` 字段。
- [ ] **数值进 SO**：可调数值落在 ScriptableObject（`Assets/_Project/Data/<Module>/`），不是散在 MonoBehaviour 里的魔法数字。
- [ ] **场景改动走 MCP**：所有场景 / 预制体改动都标注为 Unity MCP 操作（`manage_gameobject` / `manage_scene`），没有直接改 `.unity` / `.prefab` 文本的任务；MCP 没连时该任务改成「列步骤给用户在编辑器里做」。
- [ ] **有 EditMode 测试**：能抽成纯逻辑的部分有对应 EditMode 测试任务，目录 `Assets/_Project/Scripts/Tests/EditMode/<Module>/`；只有必须过 Unity 生命周期的才放 PlayMode。
- [ ] **无每帧反模式**：tasks 描述里没有「每帧 Find / GetComponent / Log / 拼字符串」这类做法。
- [ ] **`.meta` 与生成物**：没有任务要求手改 `.meta`、`Library/`、`*.csproj`、`packages-lock.json`；新建脚本后有「让 Unity 刷新生成 `.meta`」的收尾。
- [ ] **派单标注齐全**：tasks.md 每项都标了 `model`（工程 → `opus`，机械 → `sonnet`），没有 `fable`。

## 输出

- 全部通过 → 标注「PRP $1 验证通过，可 `/execute-prp $1`」。
- 有缺口 → 列出**具体缺什么、在哪一条**，回 `/generate-prp $1` 修订后重新验证。

## 原则

机制大于自觉：宁可在这里多挡一道，也不要执行到一半发现 PRP 有洞——Unity 里返工的成本（场景 / 预制体 / GUID 引用）比纯代码高得多。
