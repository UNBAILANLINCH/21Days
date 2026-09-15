---
name: unity-code-review
description: 触发 Unity 工程的模块级代码审查——依赖方向、每帧开销、订阅退订、运行时改 SO、场景改动方式、.meta 与测试覆盖。委派给 code-reviewer 子代理。注意与全局 /code-review 区分：本 skill 审的是本工程的规则。
---

# unity-code-review（模块级规则审查）

## 三层分工，别越界

| 工具 | 粒度 | 管什么 | 什么时候跑 |
| --- | --- | --- | --- |
| `project-lint` | 行级静态 | 能写成正则的项目反模式：`public` 字段、`Update` 里 `Find`、`.tag ==`、`SendMessage`、Runtime 裸 `using UnityEditor` | 保存 `.cs` 时钩子自动 |
| **本 skill（`code-reviewer` 子代理）** | 模块级推理 | 跨文件、要读上下文才能判的：asmdef 反向依赖、每帧分配、订阅退订是否成对、运行时改 SO、场景改动是否走 MCP、`.meta` 齐不齐、测试有没有覆盖核心规则 | 一波改动收敛后、`/review-change` 之前 |
| 全局 `/code-review` | 通用 diff 审查 | 跨项目通用的 bug / 简化机会 | 想额外过一遍时。**不替代本 skill** —— 它不知道这个工程的规则 |

lint 报过的不要在模块级报告里重复，重复就是噪音。

## 怎么用

把待审范围（改动文件 / 某个模块 / 一次 PRP 执行的产物）交给 `code-reviewer` 子代理。
不指定范围时它自己用 `git status --short` + `git diff` 取工作区改动。

它会：

1. 加载 `CLAUDE.md` 硬规则、`.claude/rules/` 里的相关规则、涉及模块的 module-guide、`ai-docs/pitfalls.md`；
2. 按八项重点逐项推理（详见 `.claude/agents/code-reviewer.md`）：
   依赖方向 / 每帧开销 / 序列化暴露面与配置归属 / 订阅退订与协程句柄 /
   运行时改 SO / 场景预制体改动方式与 guid / 新文件 `.meta` / 测试覆盖；
3. 输出 BLOCK / WARN / INFO 分级报告 + PASS 或 NEEDS-CHANGES 结论；
4. **只读，不改代码**。要改由主流程另行派单。

派单时按 `CLAUDE.md` 的模型路由显式传 `model`；这个子代理的定义里已经写死 `sonnet`
（审查是检索 + 对照，不是设计）。

## 何时触发

- **必须**：`/new-feature` 或 `/execute-prp` 做完之后、`/review-change` 之前。
- **必须**：改动碰到了 asmdef、命名空间、跨模块调用。
- **应该**：改了场景 / 预制体 / ScriptableObject 资产。
- **应该**：新增了 MonoBehaviour，尤其是带 `Update` 或事件订阅的。
- 拿不准「这样拆模块对不对」「这个引用方向是不是反了」的时候。

## 拿到报告之后

- **BLOCK 必须处理**，处理完重跑一次审查。
- WARN 逐条判断：改，或者说明为什么不改（说明要留在对话里，别默默跳过）。
- 结论是 NEEDS-CHANGES 时不进 `/review-change` —— 带着已知问题列提交清单没有意义。
- 报告里反复出现的同一类问题，按 `/learn` 的沉淀路由往上提：能写成正则的进
  `project-lint/rules.json`，写不成的进 `ai-docs/pitfalls.md`。
