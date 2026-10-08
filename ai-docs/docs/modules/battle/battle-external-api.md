---
type: external-api
module: battle
layer: runtime
maturity: seed
---

# Battle 外部接口

> 别的模块要触发一场战斗、读「正在战斗吗」、或写测试 / 回放时查这份。内部结构见 [`battle-module-guide.md`](battle-module-guide.md)。
> **触发战斗的唯一正路是让剧情停在 Battle 阶段**，不是调本模块的方法。

## 怎么开一场仗（剧情侧）

1. 剧情表里写一个 `kind: Battle` 阶段：`payload` = BOSS 定义 id，`battleResults` 声明 `Victory` / `Downed`（样例 `Tables/Data/narrative/sample_boss_battle.json`）。
2. 场景里放一个剧情目标：`NarrativeTrigger`（`targetKind` 对上遭遇表）+ 同物体 `DialogueInteractable`（焦点 / 提示 / 头顶标记；`NarrativeTrigger` 绑定时接管交互，`NarrativeTrigger.cs:55-60`）。
3. 剧情 `DriveAsync` 停到 Battle 阶段时同步发布 `Game.Narrative.BattleStageEnteredEvent`（`NarrativeService.cs:286-290`），`BattleFlow` 订阅并开战。
4. 打完 `BattleFlow` 用事件里带的三项身份 + `BattleExitKeys`（`Victory` / `Downed`）回写 `NarrativeService.CompleteBattleAsync`；剧情按出口迁移。

BOSS 在 `Assets/_Project/Data/Battle/BossRosterConfig.asset` 里按 id 登记；id 对不上则开战报错、不开仗（`BattleFlow.cs:144-150`）。

## `Game.Battle.BattleFlow`（根作用域入口点，`AsSelf`，可构造注入）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `IsBattleRunning` | `bool` | 有一场战斗在流程里（含等世界就绪）。`BattleFlow.cs:83` |
| `IsBattleEngaged` | `bool` | 已越过门闸、世界被按住（舞台 / 界面在场或正在进出）。`BattleFlow.cs:86` |

别的模块想「战斗中不要做 X」读这两个即可，不要自己监听事件拼状态。其余方法都是内部的（`Start` / `Dispose` 由容器调）。

## `Game.Narrative` 里战斗相关的公开入口（本模块是调用方）

| 成员 | 说明 |
| --- | --- |
| `BattleStageEnteredEvent`（`readonly struct`）：`Generation` / `ActivationId` / `TargetId` / `StageId` / `Payload` | 三个发布点见 narrative-external-api「战斗阶段通知」 |
| `NarrativeService.TryBeginBattle(generation, activationId, targetId)` | 开打前登记在途；`false` = 旧身份或已在途，不该开仗。`NarrativeService.cs:229` |
| `NarrativeService.ReleaseBattle(generation, activationId)` | 没打完就收场时解除在途。`NarrativeService.cs:246` |
| `NarrativeService.CompleteBattleAsync(...)` | 按出口键回写。`NarrativeService.cs:208` |

## `Game.Battle.BossRosterConfig` / `BossDefinition`（ScriptableObject 数据）

| 字段 | 含义 | 出处 |
| --- | --- | --- |
| `id` | = 剧情 Battle 阶段的 `payload` | `BossDefinition.cs:22-23` |
| `displayName` | BOSS 血条上方的名字 | `BossDefinition.cs:25-26` |
| `maxHealth` | 生命上限，每次开战满血，占位 | `BossDefinition.cs:28-29` |
| `outOfBattleDrunk` | 战斗外醉酒值，开战继承，占位 | `BossDefinition.cs:31-32` |
| `playerHealth` | 本场玩家生命（开战满血、战后不回写、与探索血量无关），占位 10 | `BossDefinition.cs:34-35` |
| `stagePrefab` | 舞台外观预制体；空 = 用场景里的占位外观 | `BossDefinition.cs:37-38` |

`BossRoster` 构造时校验整份名册（空行 / 缺 id / id 重复 / 数值非法即抛，`BossRoster.cs:16-26`）；名册写坏时 `BattleInstaller` 记 Error 并退回空名册（`BattleInstaller.cs:99-109`）。

## `Game.Battle.IBattlePresenter`（换表现的唯一接缝）

四个方法（`IBattlePresenter.cs:26,32,38,41`）：`OpenAsync(BattleOpening)` / `PlayAsync(session, events)` / `WaitCommandAsync(BattleCommandMenu)` / `CloseAsync`。
`CloseAsync` 必须容错、可重入（`OpenAsync` 失败一半也会调，`IBattlePresenter.cs:10`）。真实实现 `BattleScenePresenter`；EditMode 流程测试换假表现（`Tests/EditMode/Battle/BattleFakes.cs`）。

## 回放 / 调试测试口

| 成员 | 说明 |
| --- | --- |
| `BattleSetup.OverrideSettings(in BattleSettings)` | 临时换回合制数值，返回 `IDisposable`。**只在 `Debug.isDebugBuild` 可用**，否则抛；开打时埋 `battle/settings_overridden`。仅供回放，玩法代码不得调。`BattleSetup.cs:68-75` |
| `BattleSetup.Settings` / `IsOverridden` | 开仗实际用的数值 / 是否处于替换状态。`BattleSetup.cs:52,55` |
| `BattleScenePresenter.Stage` / `View` / `IsAwaitingCommand` | 回放读舞台、界面与「正在等指令」。`BattleScenePresenter.cs:77,80,83` |

## 前置条件与注意

- 需要 Boot 的 `GameBootstrap` 挂 `BattleInstaller`，并拖好 `TurnBasedConfig.asset` 与 `BossRosterConfig.asset`；缺了记 Error 并用默认 / 空名册顶上（`BattleInstaller.cs:113-128`）。
- 依赖 `NarrativeInstaller` / `LootInstaller` 已注册（`BattleInstaller.cs:35`）。
- 战斗场景地址 `BattleArena`（`BattleArena.cs:26`）须在 `AssetGroups/Scenes.asset`，界面地址 `BattleView`（= 类名）须在 `AssetGroups/UI.asset`。
- 只有「正面攻击、玩家先手」一种开战方式（`BattleSetup.cs:104`）。

## 禁止事项

- 不要绕过剧情直接 new `BattleSession` 开一场「世界里的战斗」——世界不会被按住、剧情不会回写。
- 不要订阅 `BattleStageEnteredEvent` 再开第二套战斗流程；一次只允许一个订阅方负责开仗。
- 不要在战斗进行中（`IsBattleRunning`）改背包 / 剧情 / 存档分区。
