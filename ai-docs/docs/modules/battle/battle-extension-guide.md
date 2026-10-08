---
type: extension-guide
module: battle
layer: runtime
maturity: seed
---

# Battle 扩展指南

> 要加一只 BOSS、换战斗表现、加演出种类、换背包口时看这份。架构见 [`battle-module-guide.md`](battle-module-guide.md)，
> 对外签名见 [`battle-external-api.md`](battle-external-api.md)。改规则数值 / 判定走 [`turnbased-module-guide.md`](../turnbased/turnbased-module-guide.md)，不在这里。

## 扩展点一览

| 要加什么 | 扩展点 | 改代码吗 |
| --- | --- | --- |
| 另一只 BOSS（换血量 / 名字 / 外观） | `BossRosterConfig.asset` 加一行 + 剧情里一条 Battle 阶段 | 否（数据 + 场景） |
| 一个新的 BOSS NPC 入口 | 场景物体挂 `NarrativeTrigger` + `DialogueInteractable` + `NarrativeFlagVisibility` | 否（接线） |
| 新的演出种类 | `BattleCueKind` + `BattleCueRules.Cue` + `BattleScenePresenter.PlayCueAsync` 分支 | 是 |
| 换整套表现 | 实现 `IBattlePresenter`，在 `BattleInstaller` 改注册 | 是 |
| 换背包来源 | 实现 `IBattleBackpack`，替换 `LootBattleBackpack` 的注册 | 是 |
| 新的开战方式（偷袭 / 被打） | 改 `BattleSetup.TryCreate` 的入战斗请求，由调用方传入 | 是，且要先有进战斗判定的调用方 |
| 道具效果 | 在 `Game.TurnBased` 的 `BattleItemSettings` 扩，不在本模块 | 见 turnbased guide |

## 新增一只 BOSS（剧情样例：`sample_boss`）

1. `Assets/_Project/Data/Battle/BossRosterConfig.asset` 的 `bosses` 加一行：`id`（全局唯一，将来就是剧情 `payload`）、`displayName`、`maxHealth`、
   `outOfBattleDrunk`、`playerHealth`、可选 `stagePrefab`（`BossDefinition.cs:22-38`）。id 重复 / 数值非法 → 名册构造即抛（`BossRoster.cs:16-26`）。
2. 剧情 JSON（`Tables/Data/narrative/<story>.json`）：一个 `kind: Battle` 阶段，`payload` = 上面的 id，`battleResults` 写 `["Victory", "Downed"]`，
   两个出口各接一个 End 阶段；胜利 End 用 `setFlags` 写「已击败」标记（样例 `sample_boss_battle.json`）。
   Battle 阶段不能直接接另一个 Battle 阶段（narrative extension guide 附录）。
3. `Tables/Data/narrative_encounters.json` 加一条 `trigger: Interact` 的遭遇，`targetKind` 对上场景物体的 `NarrativeTrigger.targetKind`，
   条件里加「标记未成立」让打赢后不再触发（样例 `sample_boss` 行）。只改 json 不改 schema，跑 `scripts/gen-tables.ps1`，**只提交 narrative 相关 bytes**（PRP §7 坑 7）。
4. 场景里 BOSS NPC：同物体挂 `NarrativeTrigger`（`targetId` 场景内稳定唯一）、`DialogueInteractable`（焦点 / 提示 / 头顶标记）、`NarrativeFlagVisibility`（`flagKey` = 第 2 步写的标记）。
   绑定时 `NarrativeTrigger` 自动接管 `DialogueInteractable` 的交互（`NarrativeTrigger.cs:55-60`）；标记成立时 `NarrativeFlagVisibility` 把物体隐藏（`NarrativeFlagVisibility.cs:55-60`）。
5. 补 EditMode（`Game.Tests.EditMode.Battle`）与回放（`Tests/Showcase/Battle/`）；回放控制胜负用 `BattleSetup.OverrideSettings`，不改资产。

交互提示的动词取 `DialogueInteractable` 的 `verb` 字段（默认「对话」，PRP/interaction D10）；新 BOSS 入口把它配成「挑战」。

## 新增一种演出种类

1. `BattleCueKind.cs` 加枚举项（文件头注明对应 07 的哪一行，样例 `BattleCueKind.cs` 各项注释）。
2. `BattleCueRules.Cue`（`BattleCueRules.cs:36`）的 `switch` 加 `case`：纯函数；**文案只搬 07 原文、不在这里改写**（该文件头注释写明）。补 `BattleCueRulesTests`。
3. `BattleScenePresenter.PlayCueAsync` 加分支，舞台动作加在 `BattleStage`（编排）或 `BattleActor`（单个角色）。
4. **所有时间走不受缩放的时间**（`LMotion.WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)` / `UniTask.Delay(..., true, ...)`，出处见 guide「不受缩放的时间」），否则世界暂停时演不动。

`BattleCueRules.Cue` 对不认识的事件返回 `default`，表现层跳过、不抛（`BattleCueRules.cs:65-66`）。

## 换整套表现（`IBattlePresenter`）

实现四个方法（接口说明 `IBattlePresenter.cs:5-42`），在 `BattleInstaller` 把 `IBattlePresenter` 的注册（`BattleInstaller.cs:72-73`）换成新类。约定：

- `OpenAsync` 在黑幕下调，失败一半也会被 `CloseAsync`；`CloseAsync` 必须容错、可重入。
- `WaitCommandAsync` 返回 `BattleCommand`（招式 / 道具 / 结束回合）；被拒时流程会带 `Rejection` 再问一次（`BattleFlow.cs:307-316`），连续 100 次被拒放弃这一场。
- 相机交接用 `BattleCameraHandoff`，**战斗相机不能打 `MainCamera` 标签**；收场要在卸载场景之前交还相机（guide「相机接管规则」）。
- 角色挂纸片小人用 `BattleActor`；它会关掉小人的 `ChibiPuppetMotion` 自己写走跑朝向（`BattleActor.cs:271-278`）。别的驱动方式要避免同一小人两个写参数的地方。

## 新增一种背包口

实现 `IBattleBackpack`（四个成员：`Items` / `IsConsumable` / `NameOf` / `TryConsumeOne`，`IBattleBackpack.cs:9-22`），在 `BattleInstaller` 里换 `LootBattleBackpack` 的注册（`BattleInstaller.cs:61-62`）。
`BattleItemInventory` 负责字符串 id ↔ int id 与「只列 Consumable」的口径（`BattleItemInventory.cs:26-61`），一般不用动。

## 不该从哪扩

- 不要在 `BattleFlow` 里加规则判定（谁先手、伤害多少、几回合）——属于 `Game.TurnBased`。
- 不要让 `Game.TurnBased` 引用 `Game.Battle` / `Game.Narrative`（会倒转依赖方向，`BattleExitKeys.cs:5` 的文件头同样强调）。
- 不要在 `BattleFlow` 里直接改剧情状态 / 标记；只能经 `IBattleNarrative`。
- 不要在 `BossDefinition` 上塞表现细节（动画、特效）；那属于舞台预制体与 `BattleStage`。
- 不要为了加一只 BOSS 去改 `Runtime/Monster/`：BOSS 不是巡逻怪，两者目前没有交集。
- 要让战斗被偷袭 / 被打触发，必须先有「进战斗判定」的调用方（`EncounterStep` 侧），再扩 `BattleSetup`；现在没有。
