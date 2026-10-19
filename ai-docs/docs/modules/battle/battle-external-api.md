---
type: external-api
module: battle
layer: runtime
maturity: seed
---

# Battle 外部接口

> 别的模块要触发一场战斗、读「正在战斗吗」、或写测试 / 回放时查这份。内部结构见 [`battle-module-guide.md`](battle-module-guide.md)。
> **触发战斗只有两条正路**：剧情停在 Battle 阶段（BOSS 战），或遭遇侧的实时升级（巡逻怪那一路）。两条都不由别的模块直接调本模块的方法。

## 怎么开一场仗

| 路径 | 谁发起 | 怎么发起 | 开战理由 |
| --- | --- | --- | --- |
| **剧情版**（BOSS 战） | 剧情阶段机 | 阶段 `kind: Battle` + `payload` = BOSS 定义 id → 发布 `BattleStageEnteredEvent` → `BattleFlow` 订阅并开战 | 由 `BossEntryContext` 报的局势定（见下） |
| **遭遇版**（实时升级） | `Game.Monster` 的 `EncounterStep` | 打击命中那一刻问 `IEncounterEscalationPort.TryEscalate`（实现在 `BattleFlow`），接受就开、打完在幕下回调 `CompleteEscalation` | 由三种触发情形翻译（打中未察觉 / 打中已警戒敌对 / 怪打中玩家） |

剧情版四步：

1. 剧情表里写一个 `kind: Battle` 阶段：`payload` = BOSS 定义 id，`battleResults` 声明 `Victory` / `Downed`（样例 `Tables/Data/narrative/sample_boss_battle.json`）。
2. 场景里放一个剧情目标：`NarrativeTrigger`（`targetKind` 对上遭遇表）+ 同物体 `DialogueInteractable`（焦点 / 提示 / 头顶标记；`NarrativeTrigger` 绑定时接管交互）。
3. 剧情 `DriveAsync` 停到 Battle 阶段时同步发布 `Game.Narrative.BattleStageEnteredEvent`（`NarrativeService.cs:286-290`），`BattleFlow` 订阅并开战。
4. 打完 `BattleFlow` 用事件里带的三项身份 + `BattleExitKeys`（`Victory` / `Downed`）回写 `NarrativeService.CompleteBattleAsync`；剧情按出口迁移。

**空 payload**（活遭遇档）由 `Game.Monster` 的 `EncounterBattleBridge` 认领，`BattleFlow` 对它安静跳过——别在这里加第二个订阅方。
BOSS 在 `Assets/_Project/Data/Battle/BossRosterConfig.asset` 里按 id 登记；id 对不上则开战报错、不开仗。

## 开战理由由谁说了算（`Game.Monster.IBattleEntryContext`）

BOSS 物体上的 `BossEntryContext` 实现它，回答「这只 BOSS 察觉到玩家没有 / 玩家进没进它的警戒 / 敌对范围 / 谁先动手」五个量：

- **谁交快照**：`BattleFlow.BindEntryContextProvider` 在**开仗那一刻按 payload 现找**（构建回调里 `FindObjectOfType` 看不见玩法场景，pitfalls.md:578）；
- **遭遇有没有发言权**：`BattleSetup.IsEncounterEngaged` —— 巡逻遭遇只在「偷袭已命中 / 玩家在警戒区 / 玩家在敌对区 / 怪处于警戒或敌对态」时才有话说，否则退回硬编码缺省「正面攻击」；
- **BOSS 自己是唯一权威**：2026-10-10 起 `Npc_SampleBoss` 挂 `BossEntryContext`，开战理由不再由十几米外的巡逻怪决定。

## `Game.Battle.BattleFlow`（根作用域入口点，`AsSelf`，可构造注入）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `IsBattleRunning` | `bool` | 有一场战斗在流程里（含等世界就绪）。`BattleFlow.cs` |
| `IsBattleEngaged` | `bool` | 已越过门闸、世界被按住（舞台 / 界面在场或正在进出）。`BattleFlow.cs` |
| `IsFightingBoss(bossId)` | `bool` | 流程里这一仗是不是跟名册里那一行打的（BOSS 发难组件用它分辨）。`BattleFlow.cs` |
| `CanEscalate(kindId)` | `bool` | 这个怪物种类有没有回合制数据（遭遇侧在 tick 里问）。`BattleFlow.cs` |
| `TryEscalate(encounter, in request, out reject)` | `bool` | 遭遇侧的升级端口实现；见 [`IEncounterEscalationPort`](../monster/monster-external-api.md)。`BattleFlow.cs` |

别的模块想「战斗中不要做 X」读这几个即可，不要自己监听事件拼状态。其余方法都是内部的（`Start` / `Dispose` 由容器调）。

## `Game.Narrative` 里战斗相关的公开入口（本模块是调用方）

| 成员 | 说明 |
| --- | --- |
| `BattleStageEnteredEvent`（`readonly struct`）：`Generation` / `ActivationId` / `TargetId` / `StageId` / `Payload` | 三个发布点见 narrative-external-api「战斗阶段通知」 |
| `NarrativeService.TryBeginBattle(generation, activationId, targetId)` | 开打前登记在途；`false` = 旧身份或已在途，不该开仗。 |
| `NarrativeService.ReleaseBattle(...)` / `CompleteBattleAsync(...)` | 没打完就收场时解除在途 / 按出口键回写。 |

## `Game.Battle.BossRosterConfig` / `BossDefinition`（ScriptableObject 数据）

| 字段 | 含义 | 出处 |
| --- | --- | --- |
| `id` | = 剧情 Battle 阶段的 `payload` | `BossDefinition.cs:41` |
| `displayName` | BOSS 血条上方的名字 | `BossDefinition.cs:44` |
| `maxHealth` | 生命上限，每次开战满血，占位 | `BossDefinition.cs:48` |
| `outOfBattleDrunk` | 战斗外醉酒值，开战继承，占位 | `BossDefinition.cs:51` |
| `playerHealth` | 本场玩家生命（开战满血、战后不回写、与探索血量无关），**默认 120**（`DefaultPlayerHealth`，出处 sp07:129-133） | `BossDefinition.cs:55` |
| `stagePrefab` | 舞台外观预制体；空 = 用场景里的占位外观 | `BossDefinition.cs:58` |
| `monsterKindId` | 这一行同时是哪一种巡逻怪（`monster_species` 主键）的回合制数据；**0 = 不是怪物行**（只给剧情阶段按 id 查）。非 0 的值整份名册不许重复 | `BossDefinition.cs:64` |
| `aggroEnabled` | 开主动发难（BOSS 自己冲上来）；**默认关 = 只能按 E 挑战** | `BossDefinition.cs:70` |
| `aggroChargeSpeed` | 发难冲刺速度（米 / 秒），撤回原位也用它；占位 3 | `BossDefinition.cs:73` |
| `aggroRadius` | 发难半径（米，以**原位**为圆心）；占位 5（用户 2026-10-15 拍板）。应大于交互半径 | `BossDefinition.cs` 的 `AggroRadius` |
| `aggroReactionSeconds` | 反应时间（秒）：警觉后愣这么久才冲，这段里按 E 仍是正面攻击；占位 3 | `BossDefinition.cs:82` |
| `aggroLeashDistance` | 拴绳距离（米）；**0 = 按发难半径 × 2 算** | `BossDefinition.cs:86` |

`BossRoster` 构造时校验整份名册（空行 / 缺 id / id 重复 / 数值非法即抛，`BossRoster.cs:16-26`）；名册写坏时 `BattleInstaller` 记 Error 并退回空名册（`BattleInstaller.cs:99-109`）。

## `Game.Battle.IBattlePresenter`（换表现的唯一接缝）

四个方法（`IBattlePresenter.cs:26,32,38,41`）：`OpenAsync(BattleOpening)` / `PlayAsync(session, events)` / `WaitCommandAsync(BattleCommandMenu)` / `CloseAsync`。
`CloseAsync` 必须容错、可重入（`OpenAsync` 失败一半也会调，`IBattlePresenter.cs:10`）。真实实现 `BattleScenePresenter`；EditMode 流程测试换假表现（`Tests/EditMode/Battle/BattleFakes.cs`）。

## 回放 / 调试测试口

世界舞台接线：`BattleStage.worldView` 指向当前EncounterSceneView，`hideDuringBattle` 只列需局部隐藏的原对象；`IsWorldStage` 在妖界有效。`Prepare(prefab, targetId = null)` 复制实际参战外观，`Release()` 恢复显隐并清理本场实例，幂等且由表现生命周期调用。`BattleOpening.TargetId` 为可选表现信息；不替代结果回写的剧情身份。`BattleActor.ClearMounted()` 只清本场复制外观，保留舞台及探索对象。

| 成员 | 说明 |
| --- | --- |
| `BattleSetup.OverrideSettings(in BattleSettings)` | 临时换回合制数值，返回 `IDisposable`。**只在 `Debug.isDebugBuild` 可用**，否则抛；开打时埋 `battle/settings_overridden`。仅供回放，玩法代码不得调。`BattleSetup.cs:83` |
| `BattleSetup.Settings` / `IsOverridden` | 开仗实际用的数值 / 是否处于替换状态。`BattleSetup.cs:67,70` |
| `BattleScenePresenter.Stage` / `View` / `IsAwaitingCommand` | 回放读舞台、界面与「正在等指令」。`BattleScenePresenter.cs,83,86` |

## 前置条件与注意

- 需要 Boot 的 `GameBootstrap` 挂 `BattleInstaller`，并拖好 `TurnBasedConfig.asset` 与 `BossRosterConfig.asset`；缺了记 Error 并用默认 / 空名册顶上（`BattleInstaller.cs:113-128`）。
- 依赖 `NarrativeInstaller` / `LootInstaller` 已注册（`BattleInstaller.cs:35`）。
- 战斗场景地址 `BattleArena`（`BattleArena.cs`）须在 `AssetGroups/Scenes.asset`，界面地址 `BattleView`（= 类名）须在 `AssetGroups/UI.asset`。
- 开战方式**三种都有**（07:15-28）：偷袭 / 正面攻击 / 被打。剧情版由 `BossEntryContext` 的局势定，遭遇版由触发情形翻译（`BattleSetup.TranslateEscalation`，`BattleSetup.cs:159`）。
- 三个 `TryCreate` 重载（`BattleSetup.cs:106,126,139`）：不带局势 / 带 `IBattleEntryContext` / 带 `EncounterEscalationRequest`（后者才传玩家实时血量做比例换算）。
- 「按 E 挑战」在 BOSS 开了主动发难时要**在反应时间内**完成，否则它先冲上来按「被打」开战（`BossAggro`，见 battle guide）。

## 场外醉酒与隔离舞台

`BattleSetup.BindDrunkSource(Func<string,int?>)` 绑定调用方拥有的醉酒来源，参数为 BOSS id；null 使用配置默认值，0 会覆盖占位值，负值拒绝开战。卸载时绑定 null，不能缓存上一场值或运行时修改 SO。
`BattleArena` 构造函数有可选 sceneKey，默认 `BattleArena`；`BattleInstaller.arenaSceneKey` 提供编辑期引用。五店主场景使用正常 Boot 的 `BattleArena`。旧 `ShopPlaytestBattleScene` 入口已加 `Legacy` 前缀归档，不能再作为当前五店接线或验收舞台。

## 禁止事项

- 不要绕过剧情直接 new `BattleSession` 开一场「世界里的战斗」——世界不会被按住、剧情不会回写。
- 不要订阅 `BattleStageEnteredEvent` 再开第二套战斗流程；一次只允许一个订阅方负责开仗。
- 不要在战斗进行中（`IsBattleRunning`）改背包 / 剧情 / 存档分区。
