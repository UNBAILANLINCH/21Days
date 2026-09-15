---
name: run-evals
description: 跑行为 eval——把用例的任务原样派给 subagent 真做一遍，再用 check.py 机器判产出，汇总通过率并判断「是 AI 行为问题还是规则没写清」。改完 .claude/rules/ 或 project-lint/rules.json 之后跑。
disable-model-invocation: true
---

# /run-evals — 行为回归

> **载体**：用户改完 `.claude/rules/` 或 `.claude/skills/project-lint/rules.json` 之后主动跑；
> `/learn` 的收尾步骤会问一句「跑了没」。不设周期、不加提醒——没人跑的 eval 就该删。
> **状态锚点**（5 秒可证伪）：`ls evals/reports/` 看最新一份报告的日期。目录不存在 = 从来没跑过。
> **退场条件**：连续两个季度没跑过，**把整套 `evals/` 删掉**，而不是加提醒催人跑。
> 一套没人跑的 eval 比没有 eval 更坏——它让人以为行为有回归保护。

静态自洽归 `/gc`（`invariants.py`），这里只管**行为**：同一个任务，改完规则之后 AI 做得对不对。

## 步骤

1. **列用例**：`ls evals/cases/*.json`。改了某条规则就只跑 `rule_ref` 指向它的那几条，全量才跑全部。
2. **建产出目录**：每条用例一个独立空目录，放 scratchpad，例如 `<scratchpad>/evals/EVAL-001/`。
3. **逐条派单**（可并行）。给 subagent 的提示**只有两段**：
   - 一句中性的落盘说明：「把代码写到 `<产出目录>` 下，不要改仓库里的文件。」
   - 用例的 `task` 字段，**原样粘贴**。`context_files` 非空时附上那几个路径。
   - **`model` 显式传 `opus`**（`model-routing.md`：跨文件实现属工程任务档），并且**每次都用同一档**——
     换了模型再比通过率就不是在比 harness 了。
4. **判定**：`python evals/check.py evals/cases/<id>.json <产出目录>`，exit 0 为通过。
5. **汇总**：通过率 + 每条未通过的 check 与它的 `why`，写进 `evals/reports/<日期>.md`。

## 纪律（违反了这次就白跑）

- **绝不把 `notes` / `deterministic_checks` / `rule_ref` 透露给执行任务的 subagent**，
  也不要提「这是测试」「注意别踩坑」。AI 一旦知道在被测，就会刻意规避，
  测出来的通过率虚高、跟真实表现对不上——这是最容易毁掉一套 eval 的操作。
- **不替 subagent 补话**。它问「要不要用 IAssetService」时回「你按工程规范判断」，别给答案。
- 判定只看落盘的代码，不看它说了什么。lint 拦下后它自己改对了，照样算通过。
- 产出目录只在 scratchpad，**不往仓库里写**。

## 失败了怎么判（关键一步，别跳）

| 线索 | 结论 | 下一步 |
| --- | --- | --- |
| 同一条 check 连续两轮都挂 | **规则没写清 / 送不到** | 看用例的 `.notes.md`「不通过时查什么」那张表，按它的修法改规则或路由 |
| 只挂一次、重跑就过 | AI 行为波动 | 不动规则。单次失败不足以改 harness |
| 挂的是 lint 已覆盖的点 | 路由或 lint 正则漏了 | 查钩子有没有在那条路径上触发、`rules.json` 的 `pattern` 覆盖到没有 |
| check 本身误判（代码其实是对的） | **用例写坏了** | 改 `deterministic_checks`，并在 `.notes.md` 记一句为什么当初写歪了 |

结论写进报告，别只写「3/4 通过」——通过率不带判断就没人会照着改任何东西。

## 加一条用例

照 `evals/README.md` 的格式写 `evals/cases/<id>.json` + 同名 `.notes.md`。
`task` 必须是**自然的开发需求**，不带任何陷阱暗示；判定全写进 `deterministic_checks`，不留人工勾选项。
