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
> 以 PRP 顶部 2026-09-29 修订和 [`follow-up-integration.md`](../../../../PRP/narrative-dialogue/follow-up-integration.md) 为本批契约；旧 A01–A23 保留追溯。

## 现状一句话

**规则层与运行适配已接入 Boot，自动验证通过，人工视觉验收尚待确认。** 原七个纯规则文件继续复用；新增
`NarrativeCatalog/Service/ConditionSource/Trigger/Installer/ChangedEvent`，JSON/Luban 表已实际生成。
Boot 挂接、定向测试、Showcase 与人工视觉验收的实际状态统一记在
[`tasks.md`](../../../../PRP/narrative-dialogue/tasks.md)，没有结果记录的项目均未通过。模块已在注册表登记为 `seed`。

## 职责边界

**做**：剧情阶段的纯规则迁移（`NarrativeRules`）、遭遇触发的条件仲裁（`EncounterRules`）、
无副作用的条件求值（`NarrativeCondition` / `EncounterContext`）、剧情内容的结构校验（`NarrativeContent`）、
结果身份防迟到回调（`NarrativeIntent`）、剧情进度快照（`NarrativeSaveData`）。

**不做**：

| 不做 | 归属 |
| --- | --- |
| 拉起对白、打字、立绘、选项表现 | Dialogue 模块 |
| 战斗模拟、tick 推进 | Monster / `SimulationRunner` |
| 战斗感知与敌意事实 | 尚未接入；生产 Catalog 拒绝使用，不能猜测为 true |
| 槽位 UI、统一落盘、流程切换 | 已有 Session；Narrative 只负责分区与恢复前校验，不实现全场景回滚事务 |
| 已读历史持久化 | DialogueReadStore 的独立 `dialogue-read` 档案，不进槽位 |
| C5/G5、画皮、收押、三结局及真实章节 | 未定范围；验证样例不构成策划定案 |

## 运行时类分工

下表原七类保持纯规则；运行协调和场景生命周期由后六类承担。

| 类 | 是什么 | 谁在用 |
| --- | --- | --- |
| `EncounterContext` | 条件求值的只读快照：目标身份、玩家/目标状态、剧情标记集合；`Fact` 枚举 + `Read(fact, key)` | 条件求值、遭遇候选与 Dialogue 条件接口 |
| `NarrativeCondition` | 无副作用强类型条件；`Validate()` + 静态 `Matches(NarrativeCondition[][], EncounterContext)`（外层 OR、内层 AND，空外层=无条件，不接受空 OR 分支） | 剧情阶段、遭遇规则与对白选项 |
| `NarrativeContent` | 剧情内容及出口图，构造期校验重复 ID、跳转、条件环路；`Stage.SetFlags` 是进入该阶段时幂等写入的标记 | Catalog 翻译；Stage 仍是可变 DTO，校验后禁止修改 |
| `NarrativeIntent` | `readonly struct`，携带 `Generation` / `ActivationId` / `TargetId` / `RequestId` / `Result` / `Part`，拒绝旧场景/旧读档回调 | NarrativeService 固定异步身份后提交；外部等待结果同样经 SubmitAsync 校验 |
| `NarrativeRules` | 阶段迁移、一层局部遭遇续接、128 步自动条件上限、意图身份校验及深拷贝快照 | NarrativeService 持有，纯规则测试可直接构造 |
| `EncounterRules` | 批量优先级仲裁、稳定目标排序、Once/Reenter/RisingCondition 消费键 | Service 先重新读取候选目标事实，再提交完整批次 |
| `NarrativeSaveData` | 主/父 Frame、请求身份、消费键、条件边沿、标记，ISaveData v1 | Service 每次重新 Get 写回；Session 稳定边界落盘 |
| `NarrativeCatalog` | Luban 翻译、对白出口与任务 ID 交叉校验、无出口等待/未实现能力及优先级冲突检查 | 惰性读取配置；构造时不读表 |
| `NarrativeService` | 调用既有 DialogueService、固定异步身份、Session 重载、Quest 完成标记补齐 | 根作用域 IGameService；不直接改 Quest 进度 |
| `NarrativeConditionSource` | PlayerModel 当前快照 + 当前槽位 StoryFlags + 已登记目标生命周期 | 实现 IDialogueConditionSource；无 Service 反向依赖，避免 DI 环 |
| `NarrativeTrigger` | NPC / BOSS 的稳定 ID、点击入口、距离判定、禁用/销毁取消；**同物体有 `DialogueInteractable` 时绑定即接管交互转交**（交互键 / 提示 / 点击都走它的 `InteractAsync`），且有 `IsReady` 守卫（`NarrativeTrigger.cs:53-58,65`） | 场景加载时绑定；运行时生成对象须 Configure/Bind |
| `NarrativeFlagVisibility` | 场景组件：配一个剧情标记键，标记成立即 `SetActive(false)`；绑定 / 每次 `OnChanged` 重算（`NarrativeFlagVisibility.cs:39-60`） | `NarrativeService` 场景加载时扫描绑定（`NarrativeService.cs:93-94,407-408`）；BOSS NPC 靠它在胜利后退场 |
| `NarrativeChangedEvent` | 分区已经写回的通知 | SaveTriggerBridge 合并保存请求 |
| `BattleStageEnteredEvent` | 剧情停到（或读档恢复到）Battle 阶段的通知，带三项回写身份与 `payload`（`BattleStageEnteredEvent.cs:14`）；订阅方是 `Game.Battle` | `NarrativeInstaller` 注册 broker（`NarrativeInstaller.cs:20`），`NarrativeService` 发布 |
| `NarrativeInstaller` | 根作用域服务及 MessagePipe 注册 | Boot 的 GameBootstrap，配置加载完成后初始化 |

## 数据流（代码路径，资产接线与验证状态见 tasks.md）

```text
NarrativeTrigger.InteractAsync / 外部批量候选：带稳定目标身份
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

Dialogue
    DialogueCatalog 把表里的 anyOf[].all[] 翻成 NarrativeCondition[][]
    DialogueController / DialogueRules 调 NarrativeCondition.Matches(选项条件, IDialogueConditionSource.Snapshot(targetId))
    装有 NarrativeInstaller 时采用 NarrativeConditionSource；未安装时保留原占位实现
```

## 依赖方向

原七个纯规则文件只依赖下列基础能力；不引入 Unity、Dialogue、Quest 或 Session：

| 依赖 | 用来做什么 |
| --- | --- |
| `Game.Core.Save`（`ISaveData`） | `NarrativeSaveData` 的形状 |
| `Game.Core.Telemetry`（`ITelemetryScope`，可为 `null` → `NullTelemetryScope.Instance`） | `NarrativeRules` 埋 `stage_entered` / `restored` / `result_rejected`（Warn） |
| Newtonsoft.Json | `Capture` / `Restore` 的深拷贝 |

`Game.Core` 不认识 Narrative。2026-09-29 修订明确允许同在 Game.Runtime 内的运行协调器引用
DialogueService、Quest 公开事件/保存 DTO、SessionStartedEvent、PlayerModel。条件源不依赖协调器；
纯规则边界不变，不通过新增程序集或通用解释器绕过职责。

## Dialogue 的条件消费点

| Dialogue 里的位置 | 用了什么 | 怎么用 |
| --- | --- | --- |
| `DialogueInstaller` | `IDialogueConditionSource` | 尝试解析 NarrativeConditionSource；未装 Narrative 时使用原占位实现 |
| `DialogueCatalog.cs:200`–`226` | `NarrativeCondition` | 内容表的 `anyOf[].all[]` 翻译为 `NarrativeCondition[][]`；`ConditionFact` 按名字映射到 `EncounterContext.Fact` |
| `DialogueContent.cs:32`、`84` | `NarrativeCondition[][]` | 选项 `Conditions` 字段的类型；构造期用占位上下文校验一次 |
| `DialogueController.cs:538` | `NarrativeCondition.Matches` | 每 0.25 s 刷新选项可用性 |
| `DialogueRules.cs:90` | `NarrativeCondition.Matches` | 提交选项时复验一次 |

NarrativeService 在对白前固定 Generation/ActivationId/TargetId/RequestId，await 返回后构造 NarrativeIntent。
对白取消/异常仍发 OnEnded，但 Completed=false，不推进 Quest TalkTo；正常与跳过抵达出口才推进。

## 当前接线与存档边界

| 项 | 要求 |
| --- | --- |
| Boot 根注册 | GameBootstrap 挂 NarrativeInstaller；Dialogue/Quest/Session/Player 保持既有注册，Narrative 服务排在配置与 Quest 服务后 |
| 配置数据 | 三张 narrative_*.bytes 位于既有 Addressables Config 文件夹；只跑 scripts/gen-tables.ps1 生成，不手改生成物 |
| 场景 NPC | NarrativeTrigger 配唯一稳定 targetId、表中的 targetKind，配碰撞体及相机 Raycaster |
| 运行时 NPC | Configure(id, kind, service, actor)；不要等一次性 sceneLoaded 扫描 |
| 样例 | sample_encounter 用真实对白1002，按 quest_completed_1002 分支；sample_options/对白9001 显示真实任务标记控制的选项；sample_wait 演示稳定等待恢复；均不自动启动或改变正常 SampleScene |
| 取消与失败 | 停留当前 Dialogue 阶段、清除请求标记；同一有效目标的主动 Interact 可重试，沿用原身份和 Once 消费记录；其他触发不自动重播。另保留显式 RetryAsync；未完成对白禁止落盘 |
| 目标生命周期 | Trigger 禁用/销毁取消在途对白；重新启用重新建立取消令牌；不把对象 instance ID 写存档 |

状态改变先写 NarrativeSaveData，再广播 NarrativeChangedEvent，Session 合并自动保存。
QuestCompletedEvent 仅作为同步时机：服务重新读取当前 QuestSaveData 的 Completed 状态，
按 narrative_quest_flags.json 写幂等标记；初始化/SessionStartedEvent 同样补齐，避免事件订阅次序影响旧档恢复。

Session 的自动保存和 SaveNowAsync（含退出/离场）均检查对白与 Narrative 稳定边界。
当前只支持无外部请求的 WaitAction 或整段结束；保存包含主/父 Frame、请求身份、消费键、边沿与全部标记。
对白半途不写盘、不谎报保存成功，待到支持的边界再处理；退出时保留上一次完整槽位。
逐节点/逐字恢复仍未实现，不能用“重播整段对白”替代。

ContinueAsync 先对同一 SaveSnapshot 校验，再 Commit，避免二次读盘切换成另一份候选。
未知剧情/阶段、当前不支持恢复的阶段、父等待帧带 RequestIssued 均拒绝提交；缺 Narrative 分区的旧档视为尚未开始。
这是新增分区的提交前防护，**不是全场景事务回滚**。

## 条件事实范围

- 玩家存活/潜行/伪装来自 PlayerModel.Snapshot；标记来自当前槽位 NarrativeSaveData，读档后不持有旧分区引用。
- 本批目标是无战斗 NPC；TargetAlive 表示登记对象仍激活可用，未登记或已销毁为 false。
- TargetHostile/TargetDetected 尚无接入来源，生产 Catalog 拒绝引用这些条件；纯规则枚举保留供未来扩展。
- DialogueService 的稳定目标重载用于叙事对白；旧重载仍使用 dialogue:<id>，没有登记目标时目标事实不成立。
- 没装 NarrativeInstaller 的项目保持 DefaultDialogueConditionSource 兼容：玩家/目标存活、非潜行、非伪装、
  非敌对、TargetDetected=true、无标记。它是占位，不能拿作真实事实。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/Narrative/NarrativeRulesTests.cs`（6 条） | 局部续接、迟到结果、条件环路、候选仲裁、同链旗标、跨代请求清理 |
| EditMode | `.../NarrativeCatalogTests.cs`（10 例） | 真实生成表、缺失引用、无出口等待、未接入事实/阶段、优先级冲突 |
| EditMode | `.../NarrativeServiceTests.cs`（6 条） | 令牌取消后重试、异常及取消不推进真实 Quest、自动触发/死玩家不重播、旧异步收尾、任务标记和槽位隔离、非法候选 |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/Narrative/NarrativeShowcase.cs`（3 条） | 正常对白→任务→条件选项→稳定保存/继续与 Once 去重；面板打开时真实任务事件刷新选项；目标真实禁用/启用后同激活重试，再跳过确认并验证 Completed/Skipped 与任务推进 |

上述是源码覆盖范围，实际结果见 tasks.md。先跑 `/unity-test EditMode Narrative`；Boot 注册完成后跑
`/verify-module Narrative`。回放经真实 Boot/标题进入 SampleScene，只创建本次运行的验证目标并由 Track 清理。

## 已知约束 / 未做

- **资产接线与实测状态单独追踪**：有协调代码不等于 Boot 已运行；以 tasks.md 的实测记录为准。
- **局部遭遇最多一层**：`EnterEncounter` 在 `CanEnterEncounter`（`state.Parent == null`）为 false 时直接拒绝，不支持递归嵌套（`NarrativeRules.cs:22`、`34`）。
- **`ResolveAutomatic` 有 128 步上限**：连续 `Condition` 阶段超过这个数视为死循环抛 `InvalidOperationException`（`NarrativeRules.cs:71`、`95`），内容设计要避免。
- **`Capture`/`Restore` 走 JSON 深拷贝**，不是引用赋值；`Restore` 会校验 `Current`/`Parent` 的 `Frame` 合法性（阶段存在、`ActivationId` 在范围内、`CompletedParts` 属于 `RequiredParts`），非法直接抛 `ArgumentException`（`NarrativeRules.cs:151`、`163`）。
- **`EncounterContext.Fact` 与 Dialogue 的 `ConditionFact` 按名字映射**：两边任一改名或增项都要同步改（参见 dialogue-module-guide「内容表」一节）。
- **战斗结果消费**：回合制这条路径已由 `Game.Battle` 经 `CompleteBattleAsync` 走通（见下一附录与 [`battle-module-guide.md`](../battle/battle-module-guide.md)）；潜行 / 遭遇的结果（`EncounterStep.PendingResult`）仍没有消费方，roadmap C5 只覆盖了回合制。`Battle` 这个 `StageKind` 最终形态可能随 C90 / C91 变化。
- **章节 / 阶段结构以 [`stage-structure-spec.md`](../../stage-structure-spec.md) 为准**：旧版「章 / 幕 / 场」与聚光灯十二阶段怎么映射、十二段逐条能不能进内容表（`StageKind` 五态见 `NarrativeContent.cs:9`，但 `Battle` 现在已能进表，见下方附录）、旧版 [14] 哪些还成立，都在那份规范里。要落章节卡或阶段内容之前先读它。

## 禁止事项

- 不要把 Dialogue/Quest/Session/Unity 依赖带进原七个纯规则文件；协调调用只放在运行适配层。
- 不要绕过 `NarrativeRules.Apply` 直接改 `NarrativeSaveData` 字段——身份校验（`Generation`/`ActivationId`/`TargetId`/`RequestId`）就是为了拒绝旧读档、旧场景的迟到回调，直接改字段会绕开这层保护。
- 不要把 `DefaultDialogueConditionSource` 的占位行为当真——它不代表任何真实玩法状态。
- 不要在 Boot 没挂 NarrativeInstaller 时假设 NarrativeService 已在游戏里运行。
- 新增条件事实类型或阶段类型前先看 [`narrative-extension-guide.md`](narrative-extension-guide.md) 的扩展点，不要新起一套通用规则解释器（PRP 明确禁止）。

## 附录·三类剧情能力定型（2026-10-07，roadmap C5 数据形状）

> 本节只记这次改动的增量；上面「已知约束 / 未做」里写「`Battle` 被生产校验直接拒绝」等几条已被本节替代。

**`Battle` 进表与结果词汇**：`StageKind.Battle` 不再被 `NarrativeCatalog.Validate` 拒绝。表侧由 `battleResults` 列声明本阶段可能的结果出口键（`Tables/Defines/narrative.xml:14-16`），取值是 `Assets/_Project/Scripts/Runtime/Narrative/BattleOutcome.cs` 的四个常量：`Downed`（击倒）、`Exposed`（暴露）、`BossPhaseChanged`（BOSS 换形态未结束）、`Victory`。换形态要写明形态名，出口键写成 `BossPhaseChanged:<形态>`，`BattleResult.Parse` 是它唯一的产出方。

**回写接口**：`NarrativeRules.CanCompleteBattle(generation, activationId, targetId)` 判定「这场战斗是不是当前阶段」，`NarrativeRules.CompleteBattle(...)` 走完整身份校验后按结果码出口推进；身份取自战斗侧登记的请求身份，场景切换或读档后回来的旧结果返回 `false`（与 `Apply` 同口径，不抛）。运行层入口是 `NarrativeService.CompleteBattleAsync(...)`，它只负责提交、埋点与落盘通知。**不要**绕过它直接改 `NarrativeSaveData`。

**`IssueRequest` / `RequiredParts`**：两列已进 `Tables/Defines/narrative.xml`。`IssueRequest=true` 表示这一阶段会向外部系统发一次请求，校验要求它有至少一个出口；`RequiredParts` 是多部分行为，每部分提交一次 `Success`，全齐才真正迁移，因此必须有 `Success` 出口。

**战斗阶段的可恢复边界（已被下一附录改写）**：`DriveAsync` 仍让 `Battle` 停在原地等回写（`NarrativeService.cs:280-288`）；但 `IsStable` **不再**对 Battle 一律返回 false——战斗没在打时稳定、在途时不稳定，见下一附录「`IsStable` 的新口径」。

**剧情标记键名（V1–V3）**：`NarrativeCatalog.ValidateFacts` 现在按 [`story-facts.md`](../../story-facts.md) §3.2 校验 `StoryFlag` 的 `Key`：格式 `^[a-z][a-z0-9_]*(\.[a-z0-9_]+){0,2}$`（V1，1–3 段，允许下划线）、首段在白名单命名空间内（V2）、带档位段时第二段是字典 §4 登记的状态名（V3）。按 id 生成的历史键（`quest_completed_<id>`）走 `RegisteredIdKeyPrefixes` 通配前缀，不再逐个列举；三份白名单是 `NarrativeCatalog` 里的 `static readonly` 常量，改字典与改常量要同一次提交。

## 附录·战斗阶段通知与战斗在途（2026-10-07，`PRP/turnbased-battle`）

> 本节只记这次改动的增量；与上文冲突处以本节和源码为准。行号均指 `Assets/_Project/Scripts/Runtime/Narrative/NarrativeService.cs`，除非另写文件名。

**`BattleStageEnteredEvent`（D2）**：`readonly struct`，字段 `Generation` / `ActivationId` / `TargetId` / `StageId` / `Payload`（`BattleStageEnteredEvent.cs:14-38`）。
三个发布点，**全部经 `PublishBattleStage(reason)`**（`:359-366`），`reason` 只进埋点 `battle_stage_entered`：

| 发布点 | reason | 位置 |
| --- | --- | --- |
| `DriveAsync` 的循环停到 Battle 阶段（发布后 `return`，剧情不空转） | `entered` | `:284-288` |
| `ReloadFromSave`：读档 / 换槽后恢复到的阶段是 Battle（D9：战斗从头开始） | `restored` | `:116-120` |
| `TryEncounterAsync`：停在 Battle 阶段而战斗没在跑，同目标主动交互 | `retried` | `:159-168` |

注意：**发布发生在 `DriveAsync` 里（此刻 `busy` 仍为 true）**，订阅方不得在回调里同步跑完整场并回写——`CompleteBattleAsync` 会被 `CanAccept` 拒掉（`:329-335`；`Game.Battle` 的世界门闸先让出一帧，`SceneWorldGate.cs:29`）。
`NarrativeService` 构造时 `IPublisher<BattleStageEnteredEvent>` 可空：没装战斗侧时停到 Battle 阶段只埋点、不通知（`:45-46`）。

**`TryBeginBattle` / `ReleaseBattle`（D9）**：

- `TryBeginBattle(generation, activationId, targetId)`（`:227`）：身份三项对得上当前 Battle 阶段（`rules.CanCompleteBattle`）且 `MarkRequestIssued` 成功才登记，复用阶段帧的 `RequestIssued`；
  返回 `false` = 旧身份或已在途，战斗侧不该开仗（埋 `battle_begin_rejected`）。成功后立刻 `Flush`，`CanSave` 随之为 false。
- `ReleaseBattle(generation, activationId)`（`:244`）：没打完就收场（异常 / 取消 / 表现缺失 / 回写被拒）时解除登记，阶段原地不动；身份过期是空操作。
- 两者只管「在途登记」；结果回写仍是 `CompleteBattleAsync`（`:206`），不变。

**`IsStable` 的新口径（PRP §9「W1 偏离①」）**：原来不把 Battle 阶段算稳定，读档会被拒，D9 走不到。现在 `IsStable`（`:345-347`）第三个分支是
`Stage.Kind == Battle && !Current.RequestIssued`——**战斗在途（`TryBeginBattle` 登记的 `RequestIssued`）不稳定，没在打时稳定**。后果：

- 战中不存档：复用 Session 现有的 `NarrativeStable` 闸门（`CanSave` 在 `:69`），Session 没改。
- `ValidateCandidate`（`:100-108`）用同一个 `IsStable` 判候选：存档里停在「没在打」的 Battle 阶段可以提交；带在途标记的候选被拒。
- 读档后 `ReloadFromSave` 先 `ClearRequestIssued` 清掉残留的在途标记（旧进程遗留，`:115-117`），再 `PublishBattleStage("restored")`。

**Interact 纪元（PRP §9「W1 偏离②」）**：主动交互的 `Reenter` 原来只能触发一次——`NarrativeTrigger` 发的候选 `EntryEpoch` 恒为 0，消费键撞车。
现在 `TryEncounterAsync` 对 `EntryEpoch == 0` 且 `TriggerKind == "Interact"` 的候选，用剧情的 `NextActivationId` 当纪元（`:139-152`，常量 `InteractTrigger` 在 `:22`）：
上一段剧情跑过（任何阶段进入都会 +1）才算一次新的交互，同一段剧情里连按不会重复进入；它随 `NarrativeSaveData` 落盘，读档后不与已消费键冲突。
`Once` / `RisingCondition` 的消费键不含纪元，不受影响（`:147` 注释）——所以 BOSS 打输后可再按 E 重打，打赢后靠条件里的标记不再触发。

**Battle 阶段同目标重发**：Battle 阶段 + 战斗没在途（`!RequestIssued`）+ 同目标的 `Interact` 候选 + 玩家与目标存活 → 发布 `retried` 并返回 `true`，不再走仲裁（`:159-168`）。
战斗在途时不重发，免得两场叠在一起。与下面「未完成对白的重试」同一口径。

**`OnChanged` / `HasStoryFlag`**：

- `event Action OnChanged`（`:64`）：剧情分区刚写回时触发，与 `NarrativeChangedEvent` 同一时刻（`Flush` 里，`:368-373`），含读档 / 换槽后的重载（`ReloadFromSave` → `SyncQuestFlags` → `Flush`）。
  给不进容器的场景组件用；订阅方只读状态，**不得在回调里推进剧情**。逐个调用、各自兜住异常并埋 `changed_handler_failed`（`:376-385`）；`Dispose` 清空（`:418`）。
- `bool HasStoryFlag(string key)`（`:73`）：当前槽位 `NarrativeSaveData.StoryFlags` 里有没有；未就绪或空键为 false。只看落进分区的标记，**不含身份 / 遭遇的派生事实**。

**`NarrativeFlagVisibility`**：见上文类分工表。标记成立 → 隐藏（`ShouldBeVisible = !flagSet`，`NarrativeFlagVisibility.cs:63`）；订阅的是 C# 事件而非 `Update`，物体关着照样被调到，所以读档回到「标记没写」的进度时会重新显示（`:8-9` 文件头）。
样例：`sample_boss` 的 `world.sampleboss.defeated`。

**`NarrativeTrigger` 的交互转交**：`Bind` 时若同物体有 `DialogueInteractable`，就 `SetInteractionHandover(handover)`（`NarrativeTrigger.cs:53-58`）——焦点、底部「E 对话 · 名字」提示、头顶标记都由 Dialogue 那套驱动，
按 E / 点提示 / 点 NPC 走本组件的 `InteractAsync`；转交后点击由 `DialogueInteractable` 接，`OnPointerClick` 直接返回（`:82`）；`Unbind` 时只撤自己那一份（`:103`）。
`InteractAsync` 的 `IsReady` 守卫（`:65`）：服务不可用（根作用域已销毁、退出 Play 后场景里的 NPC 还挂着旧绑定、尚未初始化）直接返回 `false`，不往已释放的服务上调。
