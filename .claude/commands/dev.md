---
description: 统一开发入口——按任务复杂度路由到 直接做 / Plan 模式 / PRP 四阶段
argument-hint: <要做的事>
---

# /dev — 统一开发入口

任务：**$ARGUMENTS**

先判定复杂度，再走对应路径。判定参考：

| 复杂度 | 特征 | 走 |
| --- | --- | --- |
| 简单 | 单文件、做法明确、低风险（调参数、改一处逻辑、补一条测试） | 直接做，做完跑 `/unity-test` |
| 中等 | 多文件、需要先想清楚、单会话能完成（新增一个组件并接线） | **Plan 模式**：先列方案，确认后实现 |
| 复杂 | 新玩法模块、跨多模块、改接口契约、需可追溯与验证 | **PRP 四阶段** |

新建一个完整玩法模块时，中等档也可以直接走 `/new-feature <模块名>`，它是模块级的固定流程。

## 执行步骤

1. **加载上下文**（只读当前任务相关的那一份，见 `.claude/rules/knowledge-routing.md`）：
   - 读 `CLAUDE.md`（会话启动时已在上下文里，确认硬规则）。
   - 若触碰某玩法模块，且 `ai-docs/docs/modules/<模块>/<模块>-module-guide.md` 存在 → **编辑前必读**（`required-reads` 钩子会强制）。
   - 扫一眼 `ai-docs/pitfalls.md`，确认本次要规避哪几条。
   - `.claude/rules/` 的规则由 `knowledge-routing` 钩子按 glob 自动提示，不用手找。
2. **判定复杂度**，按上表选路径：
   - 简单 → 直接实现。
   - 中等 → 进入 Plan 模式，列方案（改哪些文件、依赖方向、验证方式），确认后实现。
   - 复杂 → 依次 `/refine-prd` → `/generate-prp` → `/validate-prp` → `/execute-prp`。
3. **实现时守住三条工程约束**（细则见 `.claude/rules/project-root.md`、`.claude/rules/csharp-code.md`）：
   - asmdef 依赖方向：`Game.Runtime` 不引用 Editor / Tests，不 `using UnityEditor`。
   - 序列化暴露面：Inspector 字段一律 `[SerializeField] private` + 只读属性，不用 `public` 字段。
   - 场景 / 预制体改动走 Unity MCP（`manage_gameobject`、`manage_scene`），不手改 `.unity` / `.prefab` 文本。
4. **完成后**：
   - 跑 `/unity-test`（能抽成纯逻辑的部分应有 EditMode 测试）。
   - 保存 `.cs` 时 `project-lint` 由钩子自动跑，按反馈修到零违规；误报在该行写 `// lint-ok: <理由>`。
   - 新增 `.cs` 需要 Unity 刷新生成 `.meta` 才算完整，提醒用户切一下编辑器。
   - 模块接口有变化 → `/generate-doc sync <模块>`。
   - 有新教训 → `/learn`。
   - 收敛后 `/review-change` 列清单，**停下等审，不擅自提交**。

## 派单

按 `.claude/rules/model-routing.md`，**每次派 subagent 显式传 `model`**：

- 工程任务（跨文件实现、改接口、根因不明的调试）→ `model: opus`。
- 机械活（单点执行、批量修改、检索摘要、按明确步骤操作 MCP）→ `model: sonnet`。
- 主窗口（Fable）只做方案设计、拆解、波次验收，**不派 `fable`，也不亲自下场做工程**（除非用户说「你直接做」）。

## 原则

- 不确定走哪档？倾向更轻的一档，发现复杂度超预期再升级。
- 护栏（钩子 / lint）挡住时先看理由，不拆护栏。
- 改动只落在用户当次指定的范围内，范围外发现的问题写给用户评估，不擅自动手。
