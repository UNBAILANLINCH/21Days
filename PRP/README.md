# PRP 工作区

PRP（Product Requirement Prompt）= 复杂功能的工程化产物：**可版本控制、可审、可验，本身就是决策记录**。
一个功能一个子目录，由编排层的 PRP 四阶段命令产出。

```
PRP/<feature>/
├── prd.md     # /refine-prd   产出：问题 / 目标 / 范围 / 玩家故事 / 验收标准 / 待确认
├── prp.md     # /generate-prp 产出：上下文快照 + 架构决策 + 验证清单 + 风险回滚
└── tasks.md   # /generate-prp 产出：有序、文件级、可逐项打勾的任务清单（含派单档位）
```

## 四阶段流程（命令在 `.claude/commands/`）

```
/refine-prd <feature> <需求>   →  prd.md            把模糊需求问清、收敛
        ↓  用户确认待确认问题
/generate-prp <feature>        →  prp.md + tasks.md 强制读模块三件套 + .claude/rules/ + pitfalls
        ↓
/validate-prp <feature>        →  执行前逐项校验，有缺口回上一步
        ↓  通过
/execute-prp <feature>         →  按 tasks 派单实现 + /unity-test + 沉淀 + /review-change 等审
```

每一步的产物都落在 `PRP/<feature>/` 下，提交进库，作为这个功能的决策留痕——半年后想知道「当初为什么这么设计」，看这里而不是靠回忆。

## 什么时候走 PRP

| 任务 | 走哪 |
| --- | --- |
| 单文件、做法明确（调参数、改一处逻辑、补一条测试） | `/dev`，直接做 |
| 多文件、需要先想清楚、单会话能完成 | `/dev`，Plan 模式 |
| 新建一个完整玩法模块 | `/new-feature <模块名>`（模块级固定流程） |
| 跨多模块、改接口契约、需要可追溯与验证 | **PRP 四阶段** |

**简单 / 中等任务不必走 PRP**——四阶段的成本只有在「值得写下来、值得审、做完要能回溯」时才划算。拿不准就先走轻的一档，发现复杂度超预期再升级。

## 约定

- 一个子目录对应一个功能；目录名用小写短横线（`player-dash`、`inventory-stack`）。
- 产物**提交进库**（`.gitignore` 不忽略本目录）。
- 执行中发现 PRP 有误，**停下来改 PRP 再继续**，别让代码和 PRP 各走各的。
- 功能做完后 PRP 目录保留，不删——它是留痕，不是临时草稿。

> 本目录当前只有这份说明；真实 PRP 随 `/refine-prd` 产生。
