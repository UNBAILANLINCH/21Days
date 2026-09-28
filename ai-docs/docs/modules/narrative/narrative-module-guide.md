---
type: module-guide
module: narrative
layer: runtime
maturity: seed
---

# Narrative 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Narrative/` 之前读这份。对外怎么调看
> [`narrative-external-api.md`](narrative-external-api.md)，要加东西看 [`narrative-extension-guide.md`](narrative-extension-guide.md)。
> 设计定稿见 [`PRP/narrative-dialogue/prp.md`](../../../../PRP/narrative-dialogue/prp.md)；
> 该 PRP 写于对话系统二期实现**之前**，其 [`follow-up-integration.md`](../../../../PRP/narrative-dialogue/follow-up-integration.md)
> 第 3 节已被取代（见下文「接线顺序」一节）；与源码不符时以源码为准。

## 现状一句话

**这是一个纯 C# 规则库，没有接进 Unity。** 没有 Installer、没有 MonoBehaviour、没有场景对象、
没有 Luban 内容表、没有验证场景、没有 Showcase。7 个源文件全部是可 `new` 的纯逻辑类型，
`Assets/_Project/Scripts/Tests/EditMode/Narrative/NarrativeRulesTests.cs`（4 条用例）是当前唯一的验证证据。
`.claude/skills/generate-doc/modules.json` 此前未登记本模块——本次文档产出的同时补登记为 `seed`。

## 职责边界

**做**：剧情阶段的纯规则迁移（`NarrativeRules`）、遭遇触发的条件仲裁（`EncounterRules`）、
无副作用的条件求值（`NarrativeCondition` / `EncounterContext`）、剧情内容的结构校验（`NarrativeContent`）、
结果身份防迟到回调（`NarrativeIntent`）、剧情进度快照（`NarrativeSaveData`）。

**不做**：

| 不做 | 归属 |
| --- | --- |
| 拉起对白、打字、立绘、选项表现 | Dialogue 模块 |
| 战斗模拟、tick 推进 | Monster / `SimulationRunner` |
| 感知玩家、生成触发候选（谁在场景里报事件） | 各玩法模块（目前无人产出 `EncounterRules.Candidate`） |
| 阶段分派到具体玩法入口、根作用域注册 | 未来的 `NarrativeController` + Installer（未实现，见「未接线清单」） |
| 存读档事务、候选读取与回滚 | 未来的 `GameSessionController`（未实现） |
| 剧情内容进 Luban 表 | `Tables/`（未建） |

## 运行时类分工

全部是构造注入或直接 `new` 的纯 C# 类，**没有生命周期方法**（无 `Awake` / `Start` / `OnEnable`，因为没有 MonoBehaviour）。

| 类 | 是什么 | 谁在用 |
| --- | --- | --- |
| `EncounterContext` | 条件求值的只读快照：目标身份、玩家/目标状态、剧情标记集合；`Fact` 枚举 + `Read(fact, key)`（`EncounterContext.cs:9`） | `NarrativeCondition.Matches` 读取；`EncounterRules.Candidate` 携带；**Dialogue 唯一消费的两个类型之一** |
| `NarrativeCondition` | 无副作用强类型条件；`Validate()` + 静态 `Matches(NarrativeCondition[][], EncounterContext)`（外层 OR、内层 AND，空外层=无条件，不接受空 OR 分支）（`NarrativeCondition.cs:21`） | `NarrativeContent.Stage.Conditions`、`EncounterRules.Rule.Conditions`；**Dialogue 唯一消费的两个类型之二**（选项条件） |
| `NarrativeContent` | 剧情内容：`Stage`（`Condition`/`Dialogue`/`WaitAction`/`Battle`/`End`）及出口图；构造时校验重复 ID、跳转存在、`Condition` 必须有 `True`/`False` 出口、`WaitAction` 多部分行为必须有 `Success` 出口、自动路径（连续 `Condition`）无环路（`NarrativeContent.cs:23`） | `NarrativeRules` 持有的内容目录；目前只有测试构造 |
| `NarrativeIntent` | `readonly struct`，携带 `Generation` / `ActivationId` / `TargetId` / `RequestId` / `Result` / `Part`，拒绝旧场景/旧读档回调（`NarrativeIntent.cs:4`） | 调用方（未来的玩法结果消费者）构造后传给 `NarrativeRules.Apply` |
| `NarrativeRules` | 阶段迁移核心：`Start` / `EnterEncounter`（一层局部遭遇续接父阶段）/ `ResolveAutomatic`（`Condition` 阶段自动推进，上限 128 步）/ `Apply`（校验意图身份后推进）/ `Capture` / `Restore`；持有 `NarrativeSaveData` 状态（`NarrativeRules.cs:9`） | `EncounterRules` 构造时依赖它；目前只有测试驱动，无生产调用方 |
| `EncounterRules` | 触发候选仲裁：同批候选里按 `Priority` 选最高，同优先级冲突抛异常，同优先级同分再按 `TargetId` 字典序稳定排序；`RepeatPolicy`（`Once` / `Reenter` / `RisingCondition`）各自的一次性消费键（`EncounterRules.cs:7`） | 调用方需先由感知/交互模块产出 `Candidate`——**当前没有任何模块产出**，只有测试直接构造 |
| `NarrativeSaveData` | `ISaveData` 实现：`Frame`（`Current` / `Parent`，含 `ActivationId`、`ActionRequestId`、`CompletedParts`）、`ConsumedTriggers`、`ConditionEdges`、`EdgeCounters`、`StoryFlags`（`NarrativeSaveData.cs:8`） | `NarrativeRules.Capture()` 产出、`Restore()` 消费；`ISaveService.Get<T>()` 无需注册即可按需拿到实例，但**没有任何存读档流程调用它**（无 `GameSessionController`） |

## 数据流（设计态，非当前接线态）

```text
感知／交互模块（未实现）：产生带目标身份的 EncounterRules.Candidate
    ↓
EncounterRules.TryActivate(candidates)
    ├─ 按 Priority + RepeatPolicy 仲裁 → narrative.EnterEncounter(storyId, entryStageId, targetId, key)
    └─ 不满足 / 忙碌 → 不缓存，下次重新查询有效候选

NarrativeRules（生产存活于内存，进入/推进/续接/恢复）
    ├─ Start(storyId, targetId)              新开一条主线，Generation++
    ├─ EnterEncounter(...)                    局部遭遇：记 Parent，最多一层，不递归嵌套
    ├─ ResolveAutomatic(context)              Condition 阶段自动判 True/False 出口；End 阶段汇报 Outcome 或回续 Parent
    ├─ Apply(in NarrativeIntent)              校验 Generation/ActivationId/TargetId/RequestId 全匹配才推进
    └─ Capture() / Restore(saved)             深拷贝快照（Newtonsoft.Json 序列化再反序列化）；Restore 校验 Frame 合法性并 Generation++

Dialogue（唯一现存消费者，只用 EncounterContext / NarrativeCondition）
    DialogueCatalog 把表里的 anyOf[].all[] 翻成 NarrativeCondition[][]
    DialogueController / DialogueRules 调 NarrativeCondition.Matches(选项条件, IDialogueConditionSource.Snapshot(targetId))
    条件事实来源当前是占位 DefaultDialogueConditionSource（见下）
```

## 依赖方向

`Game.Narrative`（`Game.Runtime` asmdef 内）只向下依赖：

| 依赖 | 用来做什么 |
| --- | --- |
| `Game.Core.Save`（`ISaveData`） | `NarrativeSaveData` 的形状 |
| `Game.Core.Telemetry`（`ITelemetryScope`，可为 `null` → `NullTelemetryScope.Instance`） | `NarrativeRules` 埋 `stage_entered` / `restored` / `result_rejected`（Warn） |
| Newtonsoft.Json | `Capture` / `Restore` 的深拷贝 |

`Game.Core` 不认识 Narrative。`Game.Narrative` 不反向依赖 `Game.Dialogue`——依赖方向是 **Dialogue → Narrative**，
反过来接条件源实现要放 Dialogue 目录或第三方接线处，不能放进 `Game.Narrative`（见 extension-guide）。

## 谁在用它（当前唯一消费者：Dialogue）

| Dialogue 里的位置 | 用了什么 | 怎么用 |
| --- | --- | --- |
| `DefaultDialogueConditionSource.cs` | `EncounterContext` | 占位实现，`Snapshot()` 永远返回「正向事实全真、无剧情标记」的快照 |
| `DialogueCatalog.cs:200`–`226` | `NarrativeCondition` | 内容表的 `anyOf[].all[]` 翻译为 `NarrativeCondition[][]`；`ConditionFact` 按名字映射到 `EncounterContext.Fact` |
| `DialogueContent.cs:32`、`82` | `NarrativeCondition[][]` | 选项 `Conditions` 字段的类型；构造期用占位上下文校验一次 |
| `DialogueController.cs:330` | `NarrativeCondition.Matches` | 每 0.25 s 刷新选项可用性 |
| `DialogueRules.cs:90` | `NarrativeCondition.Matches` | 提交选项时复验一次 |

**没有任何代码引用** `EncounterRules`、`NarrativeContent`、`NarrativeIntent`、`NarrativeRules`、`NarrativeSaveData` ——
这五个类目前只被 `NarrativeRulesTests.cs` 驱动。

## 未接线清单（对应 `PRP/narrative-dialogue/tasks.md` T3–T9）

`tasks.md` 的进度表未勾选项不代表完全没做，其 2026-09-25 说明已标注哪些被对话系统二期以不同形态实现。按当前源码重新核对：

| 任务 | tasks.md 状态 | 实际情况 |
| --- | --- | --- |
| T3 存储候选读取／提交、独立档案、安全写盘、串行访问 | 未勾选 | `Core/Save`（`ISaveService` / `JsonSaveService`）已有候选读取两段式与独立档案能力，**但这是 Core 通用能力，不是 Narrative 专属**；`NarrativeSaveData` 没有被任何存读档流程接入 |
| T4 Player／Monster 快照、战斗开始／恢复、暂停、终局结果 | 未勾选 | `EncounterStep.PendingResult` 等雏形存在于 Monster 模块，但没有消费方把结果转成 `NarrativeIntent` |
| T5 DialogueController、View／HistoryView、Config、Installer | 未勾选 | **已被 `PRP/dialogue-system/` 以不同表现语义完成**（两槽立绘、跳过取代已读快进），与本 tasks.md 原设计无关；不要再按这条派工 |
| T6 NarrativeController、真实状态／交互接线、GameSessionController、SaveSlotsView | 未勾选 | **完全未做**——这是 Narrative 接 Unity 的核心缺口，对应 `docs/roadmap.md` 差距矩阵 C1 |
| T7 Excel／Luban 内容、校验器、验证对白与遭遇内容 | 未勾选 | 完全未做，对应 roadmap C3 |
| T8 Boot Installer、UI 预制体、Addressables、验证场景及 Showcase | 未勾选 | Dialogue 侧已完成；**Narrative 侧的 Installer、`NarrativeShowcase.cs` 未做（回放统一在 SampleScene 上跑，不再建独立验证场景）** |
| T9 lint、编译、测试、模块审查、模块文档三件套、登记与健康检查 | 未勾选 | 本次三件套生成与 `modules.json` / `catalog.md` 登记是这条的一部分；lint/编译/审查/健康检查仍需在接线后单独跑 |

## 接线顺序（引用 follow-up-integration.md 第 2 节，标注哪些已过时）

`follow-up-integration.md` 第 2 节列了 9 步推荐顺序。对照当前源码：

| 步骤 | 原文 | 当前状态 |
| --- | --- | --- |
| 1 | 建立最小内容对象（Line→Choice→End 对白 + WaitAction→Battle→End 剧情） | 对白半边已由对话系统二期以 JSON 表完成；剧情半边仍待做（对应本表 T7） |
| 2 | 角色表情地址索引 | 已由 `DialogueCatalog` 完成，与 Narrative 无关 |
| 3 | Boot 根作用域注册 `DialogueRules`／`DialogueController`／`NarrativeRules`／`EncounterRules` | **对白半边已注册**（`DialogueInstaller`）；**Narrative 半边（`NarrativeRules`／`EncounterRules`）仍未注册，没有 Installer** |
| 4 | 创建 `DialogueView`／`TranscriptView` 预制体 | 已完成，但字段形状与本文档写的不同（两槽立绘、无 `advance` 按钮），详见 `dialogue-module-guide.md` 开头说明——**这条与 Narrative 无关，不要按这份原文接** |
| 5 | 先接「对白开始、选择、结束」 | 已完成（`DialogueService.PlayAsync`） |
| 6 | `simulation.SetPaused(dialogueController, dialogueController.BlocksWorld)` | **已过时**：实际实现是 `DialogueService` 持有 `IWorldPauseService.Acquire(this)` 引用计数令牌，不是 `SimulationRunner.SetPaused` |
| 7 | 接真实战斗（`StartBattle` → tick 提交后消费 → 转 `NarrativeIntent`） | **未做**，对应 roadmap C5；roadmap 同时提示要先按设计支柱重新审视「无血条对抗」是否还需要 Battle 阶段这种形态 |
| 8 | 接 `GameSessionController` 的保存、候选恢复和失败回滚 | **未做**，对应 roadmap E1（游戏级存档会话），是独立 PRP |
| 9 | 最后接 Excel／Luban、Prefab、Addressables、验证场景和 Showcase | Dialogue 半边已完成；**Narrative 半边未做** |

结论：真正对 Narrative 有效、尚未做的是步骤 3 的 Narrative 半边、步骤 7、步骤 8、步骤 9 的 Narrative 半边——
即 T6（`NarrativeController` + Installer）、T7（内容表）、C5（战斗结果消费）、E1（存档会话）四块，且 C1（`NarrativeController`）依赖本文档先补齐（roadmap C4，即本次任务）。

## `DefaultDialogueConditionSource` 是占位

Dialogue 模块的 `IDialogueConditionSource` 唯一注册实现是 `DefaultDialogueConditionSource`
（`Assets/_Project/Scripts/Runtime/Dialogue/DefaultDialogueConditionSource.cs`）：`Snapshot()` 永远返回
「玩家存活、非潜行、非伪装、目标存活、非敌对、未被发现、无任何剧情标记」的固定快照。

**后果**：任何依赖 `EncounterContext.Fact.StoryFlag` 的选项条件在当前工程里**永远不可用**
（dialogue-module-guide 举例：1001 号对话树的第三个选项）。Narrative 接线（本文档「未接线清单」T6）完成、
有了真实的 `IDialogueConditionSource` 实现之后，才应替换 `DialogueInstaller` 里的注册并删掉这个占位类
（步骤见 [`narrative-extension-guide.md`](narrative-extension-guide.md)）。在此之前不要把这个占位实现的行为当成真实玩法判断。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/Narrative/NarrativeRulesTests.cs`（4 条） | 局部遭遇完成后续接父阶段并保留已完成部分；读档后旧结果被拒绝且不推进；自动条件环路在内容构造期被拒绝；`EncounterRules` 同批候选按优先级+稳定目标 ID 仲裁 |
| Showcase | 无 | 模块未接 Unity，没有可回放的场景；`/verify-module Narrative` 目前不可用 |

跑 `/unity-test EditMode Narrative`。没有 Showcase，不能跑 `/verify-module`。

## 已知约束 / 未做

- **没有生产调用方**：`EncounterRules`、`NarrativeRules`、`NarrativeContent`、`NarrativeIntent`、`NarrativeSaveData` 只被测试驱动，接线前不要假设它们已经在跑。
- **局部遭遇最多一层**：`EnterEncounter` 在 `CanEnterEncounter`（`state.Parent == null`）为 false 时直接拒绝，不支持递归嵌套（`NarrativeRules.cs:22`、`34`）。
- **`ResolveAutomatic` 有 128 步上限**：连续 `Condition` 阶段超过这个数视为死循环抛 `InvalidOperationException`（`NarrativeRules.cs:66`、`88`），内容设计要避免。
- **`Capture`/`Restore` 走 JSON 深拷贝**，不是引用赋值；`Restore` 会校验 `Current`/`Parent` 的 `Frame` 合法性（阶段存在、`ActivationId` 在范围内、`CompletedParts` 属于 `RequiredParts`），非法直接抛 `ArgumentException`（`NarrativeRules.cs:111`）。
- **`EncounterContext.Fact` 与 Dialogue 的 `ConditionFact` 按名字映射**：两边任一改名或增项都要同步改（参见 dialogue-module-guide「内容表」一节）。
- **战斗结果消费未定**：roadmap 提示 C5 之前要先决定 Player/Monster 的生命/伤害字段去留（设计支柱倾向无血条对抗），`Battle` 这个 `StageKind` 最终形态可能变化。

## 禁止事项

- 不要在 `Game.Narrative` 里引用 `Game.Dialogue`——依赖方向是 Dialogue → Narrative，反过来会成环。
- 不要绕过 `NarrativeRules.Apply` 直接改 `NarrativeSaveData` 字段——身份校验（`Generation`/`ActivationId`/`TargetId`/`RequestId`）就是为了拒绝旧读档、旧场景的迟到回调，直接改字段会绕开这层保护。
- 不要把 `DefaultDialogueConditionSource` 的占位行为当真——它不代表任何真实玩法状态。
- 不要在没有 `NarrativeController`/Installer 的前提下假设 `NarrativeRules`/`EncounterRules` 已经在游戏里运行；当前只在 EditMode 测试里被实例化过。
- 新增条件事实类型或阶段类型前先看 [`narrative-extension-guide.md`](narrative-extension-guide.md) 的扩展点，不要新起一套通用规则解释器（PRP 明确禁止）。
