# Evals — 给 harness 写的回归测试

> `project-lint` 保**单次改动对**；eval 保**整套规则能让 AI 一次做对**。
> 两者不重复：lint 是编辑后的实时防线，eval 是 harness 自己的回归测试。

## 两类 eval，载体不同，别混

| | **A 静态不变量** | **B 行为 eval** |
| --- | --- | --- |
| 查什么 | 当前仓库的跨文件约束：asmdef 依赖方向、平台宏位置、命名空间、`.meta` 配对、UI 地址 | AI 的行为：同一个任务，改完规则之后它做得对不对 |
| 要不要跑 AI | **不要**，对着仓库扫一遍就有答案，秒级 | **要**，每条用例派一个 subagent 真做一遍 |
| 住在哪 | `.claude/skills/evolution/invariants.py` | `evals/cases/*.json` + `evals/check.py` |
| 载体 | **`/gc`**（`gc_scan.py` 第 5 项调它） | **`/run-evals`**，挂在 `/learn` 的收尾步骤上 |
| 什么时候跑 | 改完 harness 结构、`/review-change` 之前 | 改完 `.claude/rules/` 或 `project-lint/rules.json` 之后 |

分这两半是因为**载体强度不同**。A 挂在一个用户天天会敲的命令上，成本近乎零，能一直活着；
B 每跑一次要派若干个 subagent 做完整开发任务，贵，只有在「刚改完规则、正想知道有没有效」
那一刻才有人真的愿意跑——所以它必须钉在那一刻，而不是钉在日历上。

> 参考教训：一套成熟 harness 的 eval 体系有 40 条用例，全是回归轨，能力轨从未落地，
> 多数文件停在三四月。死因不是用例写得不好，是**没有真载体**。
> 他们唯一没烂掉的形态是「人在有相关意图的那一刻主动调用」。

## A 静态不变量：怎么跑、怎么加

```bash
python .claude/skills/evolution/invariants.py    # 无违规时静默 exit 0
```

加一条检查 = 往 `invariants.py` 加一个 `check_xxx(root)` 函数并挂进 `CHECKS`。要求：
纯标准库、不需要 Unity 开着、每条违规给「路径:行号 + 现象 + 改法 + 依据的规则文件」、
在代码里注明它对应 `project-root.md` / `architecture.md` 的哪一条。

**判断该不该加的尺子**：一行之内判得完的归 `project-lint/rules.json`，
必须把整个仓库摊开才能判的才归这里。两边都写就是噪音。

## B 行为 eval：case 格式

一个 case 两个文件：`cases/<id>.json`（机器判）+ `cases/<id>.notes.md`（人读的设计说明）。

```json
{
  "id": "EVAL-001",
  "name": "每帧检索最近敌人",
  "rule_ref": ".claude/rules/csharp-code.md#反模式",
  "notes": "evals/cases/EVAL-001.notes.md",
  "task": "发给 AI 的自然语言任务，原样发，不加任何提示",
  "context_files": [],
  "deterministic_checks": [
    {"type": "absent",  "pattern": "正则", "files": "glob", "why": "为什么不该出现"},
    {"type": "present", "pattern": "正则", "files": "glob", "why": "为什么必须出现"}
  ]
}
```

`deterministic_checks` 的字段：

| 字段 | 说明 |
| --- | --- |
| `type` | `absent`（不该出现）/ `present`（必须出现） |
| `pattern` | Python 正则 |
| `files` | 产出目录下的 glob，省略则 `**/*.cs`。**收窄 glob 往往是 check 能成立的前提**（`OnEnable` 在面板里是错的，在别处是对的） |
| `why` | 为什么这条成立。失败时原样打给人看，写清「不这么做会怎样」 |
| `in_method` | 可选，方法名正则。只看落在这些方法体内的行。**方法级判据首选它**，别用手写的括号配平正则 |
| `multiline` | 可选，整份文件一起匹配。只在判据跨行、又不是「某方法体内」形状时才用 |

### 两条铁律

1. **`task` 必须是自然的开发需求**，不能带任何「这是陷阱测试」的暗示。
   写「测试你会不会在 Update 里 Find」，AI 会刻意规避，测出来的通过率虚高、跟真实表现对不上。
2. **判定全部机器判**，不留 `- [ ]` 人工勾选项。人勾选的那一刻就知道自己在测什么，会往宽里判；
   而且没人勾的时候整套就停摆了。

### `.notes.md` 写什么

给人看的设计说明：陷阱在哪、为什么这么设计、**不通过时查什么**（一张
「现象 → 是 AI 行为问题还是规则没写清 → 怎么修」的表）。
**绝不发给被测 agent**——它写明了陷阱在哪，看过就不算测了。

## 怎么跑

```bash
/run-evals                                            # 完整流程见 .claude/skills/run-evals/SKILL.md
python evals/check.py evals/cases/EVAL-001.json <产出目录>   # 只判一条
```

`check.py` 逐条打印 通过/失败 + `why` + 证据行，全过 exit 0。它只看落盘的代码，
不看 AI 说了什么：lint 拦下后它自己改对了，照样算通过。

## 现有 case

| case | 验什么 | 为什么 lint 与 `invariants.py` 都够不着 |
| --- | --- | --- |
| [`EVAL-001`](cases/EVAL-001.json) | 每帧路径的性能反模式：`Update` 里 `FindObjectsOfType` | lint 能拦，但拦的是「写出来之后」；eval 测的是「一次写对没有」 |
| [`EVAL-002`](cases/EVAL-002.json) | 玩法模块走 `IAssetService`，不直接用 Addressables | 正则会误伤唯一合法实现 `AddressablesAssetService`；静态扫描不查「该用哪个接口」 |
| [`EVAL-003`](cases/EVAL-003.json) | 面板监听订阅/退订成对，落在 `OnOpenAsync` / `OnCloseAsync` | 这是**常驻规则的例外**（`csharp-code.md` 说用 `OnEnable`），例外只写在非常驻文档里 |

三条都只在**新写代码那一刻**才会犯——这正是行为 eval 唯一该占的位置。
