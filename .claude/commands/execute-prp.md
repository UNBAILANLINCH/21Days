---
description: PRP 阶段 4 — 按 tasks 派单执行、跑测试、沉淀文档与教训，最后停下等审
argument-hint: <feature-name>
---

# /execute-prp — PRP 阶段 4：执行

特性名：**$1**

按已验证的 `PRP/$1/prp.md` + `PRP/$1/tasks.md` 实现，完成验证与知识沉淀，最后列清单等审。

## 步骤

1. **确认前置**：`/validate-prp $1` 已通过。否则先去验证。
2. **按 tasks.md 分波派单**（细则 `.claude/rules/model-routing.md`）：
   - 一波派一组互不依赖的任务，**每次派单显式传 `model`**：
     - 工程任务（实现核心逻辑、改接口、根因不明的调试）→ `model: opus`
     - 机械活（建配置类、批量改名、按明确步骤操作 MCP 接线、检索摘要）→ `model: sonnet`
   - 派单时把该任务需要的上下文摘给 subagent：目标文件路径、prp.md 的相关架构决策、适用规则。**不要让 subagent 自己去翻整份 PRP。**
   - 主窗口（Fable）只做拆解与波次验收，不亲自实现，也不把 subagent 派成 `fable`。
   - 每波回来先验收：改了哪些文件、有没有偏离 PRP、lint 是否零违规。通过才打勾进下一波。
3. **过程中的自动反馈**：保存 `.cs` 时 `project-lint` 由钩子自动跑，违规会打回；`knowledge-routing` 会提示适用规则与模块文档；`required-reads` 会拦住「没读 guide 就改模块」。**被护栏挡住先看理由，不拆护栏**；确属误报在该行写 `// lint-ok: <理由>`。
4. **跑验证清单**：逐条核对 prp.md 的验证清单。
   - 编译错误：Unity MCP `read_console`；编辑器没开就请用户看控制台。
   - 测试：`/unity-test`（默认 EditMode）。失败用例**如实报告，不靠改弱断言凑绿**。
   - 新建的 `.cs` 需要 Unity 刷新生成 `.meta`，提醒用户切一下编辑器。
5. **沉淀知识**：
   - 模块接口 / 结构有变化 → `/generate-doc sync <模块>`；模块是本次首次落地 → `/generate-doc <模块>` 生成三件套。
   - 踩到新坑或形成新约定 → `/learn`（写入 `ai-docs/pitfalls.md`，高频可正则检测的升级成 lint 规则）。
   - 改动了 `.claude/` 或 `ai-docs/` 结构 → 跑一次 `/gc`。
6. **收尾**：`/review-change` 列出改动清单（文件 | 位置 | 改了什么），给拟用的提交标题，**停下等用户明确授权**。
   - 「看过了」「流程没问题」都不是授权。
   - **不擅自 `git commit` / `git push`。**

## 输出

- 每个 task 的完成状态（打勾 / 未做 + 原因）。
- 验证清单逐条结果，未通过项如实说明卡在哪。
- 沉淀了什么（新文档 / 新 pitfall / 新 lint 规则）。
- 待审改动清单。

## 原则

- 不偏离 PRP；执行中发现 PRP 有误，**停下来修订 PRP 再继续**，而不是临时拍脑袋。
- 只改本次 PRP 范围内的文件；范围外发现的问题写清「现象 + 根因 + 影响 + 建议改法」交用户评估，不擅自动手。
- 场景 / 预制体改动一律走 MCP 让 Unity 自己序列化，不手改 YAML。
