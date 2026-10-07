---
type: module-guide
module: turnbased
layer: runtime
maturity: seed
---

# TurnBased 模块指南

S7「BOSS 战」的**回合制作战内核**（纯规则 + 数据）：三种进战斗方式与先手权、一个回合的推进顺序、
玩家三招式的怒气收支、道具一场一次、BOSS 醉酒四档的跳过回合、BOSS 三招式的 6:3:1 权重。

真源是策划 2026-10-07 补交的 `docs/design/spotlight/07_回合制作战文档.md`（86 行，全篇都对着写）；
转写、规则编号与开放问题见 `docs/design/features-spotlight/09_BOSS战.md` §3.9（R40–R48）与 §9（Q17–Q19 → C90–C92）。

> **接线现状**：本模块是纯规则内核，**唯一的调用方是 [`Game.Battle`](../battle/battle-module-guide.md)**
> （`BattleSetup.cs:102-106` 开仗、`BattleFlow.cs:301,331,337,351` 驱动回合；除 `Game.Battle` 外 `Runtime/` 下没有别的文件引用 `Game.TurnBased`）。
> `Game.Battle` 已挂进 `Boot.unity`，剧情停在 Battle 阶段即可开战、有界面、有回放；但**只有「正面攻击、玩家先手」一种开战方式在用**，
> `EncounterStep` 的进战斗判定（偷袭 / 被打）仍没接。逐项状态见本文最后一节「接线清单」。

## 职责边界

| 类型 | 职责 |
| --- | --- |
| `BattleEntryRequest` / `BattleEntryDecision` / `BattleEntryRules` | 进战斗判定的**输入快照 / 输出 / 纯函数**：偷袭、正面攻击、被打三种方式，含先手权与「偷袭扣 20% 血」 |
| `MonsterAlertState` / `BattleInitiator` | 快照量「怪物警觉档位」「谁先动手」。**本模块不认识 `MonsterModel` / `PlayerModel`**，接线侧翻译 |
| `PlayerBattleRules` | 玩家侧回合状态机：怒气、生命、晕眩、「本回合已出招」、招式 3 的额外伤害次数 |
| `PlayerSkillRules` / `PlayerSkill` / `SkillCastResult` / `SkillCastReject` | 三招式的数值表（消耗 / 产出 / 伤害 / 减疗 / 额外伤害次数）与施放结果、拒绝原因 |
| `BattleItemLedger` / `ItemUseRules` / `IBattleItemInventory` | 道具「拥有 + 本场没用过」的判定与账本；**持有关系靠一方法的接口注入，不读背包模块** |
| `BossDrunkRules` / `DrunkTierRules` / `DrunkSettings` / `DrunkTurnOutcome` | 醉酒值状态机（继承、+30 封顶、酩酊 −50 与持续 2 回合）与四档阈值 / 概率 / 跳过原因 |
| `BossBattleRules` / `BossBattleSnapshot` | BOSS 侧：生命、醉酒（转发 `BossDrunkRules`）、减疗、招式 1 的「下次 +30%」挂起 |
| `BossSkillRules` / `BossSkill` / `BossSkillResult` / `BossSkillSettings` | BOSS 三招式的权重选择（6:3:1）与效果（普攻 / 饮酒 / 高额伤害 + 薄醉晕眩） |
| `BattleItemSettings` | 道具效果设置（**占位，等 C91**）：一条「治疗道具回复玩家生命百分比」——道具 id / 百分比 / 基数；`Heals(itemId)` 判是不是治疗道具（`BattleItemSettings.cs:40`） |
| `BattleSession` | **一场战斗的驱动器**：按 07:46-58 的顺序推回合，每次动作后把 `BattleEvent` 摆出来；用道具时调 `ApplyItemEffect`（`BattleSession.cs:125,367`） |
| `BattleEvent` / `BattleEventKind` / `BattleHintTexts` | 事件与「屏幕中央闪现提示」文案（07:71-73 原文逐字）。**只给结果，不做界面** |
| `TurnBasedKernel` | 装配入口：配置 + `IRandomStream` + 背包口 → `TryStartBattle(...)` 开一场仗 |
| `TurnBasedConfig` / `TurnBasedConfigValidation` | ScriptableObject 配置（**43 个 `[SerializeField]` 字段**，数自 `TurnBasedConfig.cs`，逐个写「出处 07:行号 / 待拍板编号」；最后三个是道具效果占位 `TurnBasedConfig.cs:168,171,174`）/ 它的纯函数校验（含道具百分比 0..100，`TurnBasedConfigValidation.cs:50`） |
| `BattleSettings` + 六份 `*Settings` | 纯值设置包（`BattleEntry` / `PlayerSkill` / `BossSkill` / `Drunk` / `Flow` / `Items`）：**规则与测试都不依赖 ScriptableObject**；`Items` 是构造函数的可选末位参数，不传 = 没有任何道具效果（`BattleSettings.cs:24,58`） |
| `BattleExitKeys` | 战斗结果 → 剧情出口键（`Victory` / `Downed`），对齐 `PRP/battle-to-narrative` 的词汇 |

## 为什么不复用既有模块的能力

1. **复用不成立**：`Runtime/Player/` 没有怒气 / 招式 / 道具这一层，`Runtime/Monster/` 只有「有生命值、会攻击」，
   两份配置谁都不管「三招式、道具一场一次、醉酒四档、6:3:1 权重」。`09_BOSS战.md:271-278` 已把这两个缺口写死
   （Monster 行「缺口：没有 BOSS 种类……没有削弱、跳过阶段」；Player 行「缺口：没有回合制（无怒气、招式、道具使用）」）。
2. **扩展不成立**：这两个模块的目录正被并行波次（接线 / 怪物种类数据化）独占，本次任务明令不改；
   而且「一场战斗怎么推回合」既不属于某只怪，也不属于玩家本体。
3. 因此新建 `Game.TurnBased`，耦合方式统一是**注入快照 + 注入随机源 + 注入背包口**。

## 依赖方向

- 目录 `Assets/_Project/Scripts/Runtime/TurnBased/`，命名空间 `Game.TurnBased`，asmdef 走 `Game.Runtime`（**没有独立 asmdef**）。
- 只依赖 `Game.Core`：`Game.Core.Simulation.IRandomStream`（**只读引用，不改 Core**）与 `Game.Core.Simulation.GameMath`（钳制）；
  外加 `UnityEngine` 一份——只有 `TurnBasedConfig` 是 ScriptableObject。
- **不依赖** Monster / Player / Inventory / Narrative / Stealth / Identity；反向由接线侧调用本模块的公开接口。
  接线侧就是 `Game.Battle`：它依赖本模块，另外还依赖 Narrative / Loot / CharacterPuppet / Core.UI / Core.Flow（见 battle guide「依赖方向」），本模块一概不知。

## 规则落点（对着 07 全文逐条）

| 07 原文 | 落点 |
| --- | --- |
| §一 偷袭：玩家潜行并偷袭成功、玩家攻击 → 先手 + BOSS 生命 −20%（07:15-18） | `BattleEntryRules.cs:31`、`TurnBasedConfig.cs:33`、`BattleSession.cs:218`（开战扣血） |
| §一 正面攻击：在警戒 / 敌对区域或怪物警戒 / 敌对 → 玩家先手（07:20-23） | `BattleEntryRules.cs:40-43` |
| §一 被打：同上前提，玩家被 BOSS 攻击 → BOSS 先手（07:25-28） | `BattleEntryRules.cs:44`、`TurnBasedKernel.cs:83`（先手决定首回合谁动） |
| 招式只能在自己回合使用（07:48） | `PlayerBattleRules.cs:110`、`BattleSession.cs:134`（阶段不对直接拒） |
| 招式 1：无消耗、用后 +1 怒气、造成伤害（07:50） | `PlayerSkillRules.cs:18/34/38`（消耗 / 产出 / 伤害）、`PlayerBattleRules.cs:110` |
| 招式 2：消耗 1、造成伤害并减少对方 50% 治疗效果（07:52） | `PlayerSkillSettings.cs:62`、`BattleSession.cs:154`（把减疗挂到 BOSS）、`BossBattleRules.cs:124` |
| 招式 3：消耗 3、造成伤害、接下来 3 次攻击附带额外伤害（07:54） | `PlayerSkillSettings.cs:77`、`PlayerBattleRules.cs:143-155`（消耗 / 累加次数） |
| 玩家施放招式后进入敌方回合（07:56） | `BattleSession.cs:159` |
| 道具在施放招式之前使用（07:58） | `BattleSession.cs:101`（出招后 / 非玩家回合一律拒） |
| 道具「用过了则置暗；未拥有则不显示」（07:42） | `ItemUseRules.cs:21`、`BattleItemLedger.cs`（`TryUse` 记账、`CanUse` 只判定） |
| 道具效果（07 没写，**占位**）：治疗药水回复玩家生命百分比 | `BattleSession.cs:125`（用掉后调 `ApplyItemEffect`）、`BattleSession.cs:367-371`、`PlayerBattleRules.cs:177`（`Heal`，向下取整、封顶生命上限、已倒下不回）、`BattleItemSettings.cs:40` |
| BOSS 进战斗继承战斗外的醉酒值（07:66） | `BossBattleSnapshot.cs`、`BossBattleRules.cs:31`（构造）、`TurnBasedConfig.cs:89` |
| 正常 0-49 / 微醺 50-79（20%）/ 薄醉 80-99（40%）/ 酩酊 100（100%、−50、持续 2 回合）（07:70-73） | `DrunkTierRules.cs:17`、`DrunkTierRules.cs:33`、`DrunkTierRules.cs:55`、`BossDrunkRules.cs:66` |
| 跳过回合时屏幕中央闪现提示（07:71-73） | `BattleHintTexts.cs`（三句原文逐字）、`BattleEvent.BossTurnSkipped`（带 `HintText`） |
| BOSS 招式 1 普通攻击（07:79） | `BossSkillRules.cs:47`、`BossBattleRules.cs:148`（下次 +30% 只吃一次） |
| BOSS 招式 2 饮酒：+30 醉酒、回 10% 生命、下次招式 1 +30%（07:80） | `BattleSession.cs:298-301`、`BossBattleRules.cs:53/100` |
| BOSS 招式 3：正常 / 微醺 高额伤害；薄醉 高额伤害 + 晕眩玩家 1 回合（07:81-82） | `BossSkillRules.cs:56`、`BattleSession.cs:311`、`PlayerBattleRules.cs:85` |
| 施放比例 6:3:1（07:84） | `BossSkillRules.cs:20`、`TurnBasedConfig.cs:145-151` |
| 玩家状态右下角显示剩余回合（07:43）→ 有回合上限 | `BattleFlowSettings.cs`、`BattleSession.cs:324`（上限口径可配，数值等 C91） |

## 随机数与回放（本模块的取舍）

- **一切概率只走注入的 `IRandomStream`**：跳过回合（`DrunkTierRules.ShouldSkipTurn`）与选招（`BossSkillRules.SelectWeighted`）。
  `TurnBasedKernel` 的构造函数在随机源为 null 时**直接抛异常**——概率要是能用 `UnityEngine.Random` 拿到，回放必然对不上。
- **确定性细节**（回放对不上时先看这两条）：
  1. 概率为 0% 或 100% 时**不消耗随机数**（确定事件不该抽骰子，否则同场战斗里后面所有随机判定的位置会漂移）；
  2. 每次选招**恰好消耗一个**随机数（用 `Range(0, 权重和)` 一次取加权区间，不是三次抽样）。
- **本模块不实现 `IReplayState`**：`BattleSession` 没有进任何 tick，状态全是可重建的整数
  （怒气、生命、醉酒值、次数、已用道具 id）。
- **接线波的结论（PRP `turnbased-battle` D8，原先这里写「必须来自 `logic.*` 流」已作废）**：`Game.Battle` **没有**用 `logic.*` 流——
  战斗在确定性内核之外推进，消耗 `logic.*` 会让回放在战斗之后哈希对不上；`view.*` 又违背「表现流不影响逻辑」。
  现状是每场开打从会话主种子派生一条本地流 `new XorShiftRandomStream(主种子 ^ 盐 ^ 场次)`，不登记进 `IRandomService`、不进快照
  （`BattleSetup.cs:10-14`、`BattleSetup.cs:116-120`）。**代价：回放不覆盖回合制战斗**。本模块「概率只走注入的 `IRandomStream`」的要求照旧成立。
- **战中存档**的结论（D9）：战斗会话状态**不进快照**、不升 `ReplayFormat`；战斗在途时剧情不可保存，读档停在 Battle 阶段则战斗从头开始
  （`NarrativeService.cs:227-238`、`:345-347`、`:114-120`，详见 battle guide「战中不存档」）。

## 事实键纪律

- 本模块**不写任何事实键**：`ai-docs/docs/story-facts.md` §4.5 把 `combat.phase.<n>` / `combat.tooth.weakened` /
  `combat.masks.ready` 的写入方登记为 **Monster / Inventory**，§6.1 是「一个键一个写入方」。
- 战斗结果只经 `BattleSession.ExitKey`（`Victory` / `Downed`）交给**唯一一条路**：`NarrativeService.CompleteBattleAsync` → `NarrativeRules.CompleteBattle`
  （`PRP/battle-to-narrative/prp.md` §2.1）。回合制这条路径现在由 `Game.Battle` 走通：`BattleFlow` 取 `session.ExitKey`（`BattleFlow.cs:321`）、
  收场时经 `NarrativeBattlePort` 回写（`BattleFlow.cs:243`、`NarrativeBattlePort.cs:27-33`）。`EncounterStep.PendingResult`（`EncounterStep.cs:127`）那条路径
  （潜行 / 遭遇的结果）**仍没有人消费**。**不要自创结果键**，也不要在这里改剧情状态。

## 配置与占位值

- 资产：`Assets/_Project/Data/TurnBased/TurnBasedConfig.asset`（43 个字段，与 C# 声明逐一对齐；最后三个 `healItemId` / `healItemPercent` / `healItemBase` 在资产里是 `TurnBasedConfig.asset:55-57`）。
- 每个数值字段的注释都写「出处 `07_回合制作战文档.md:行号` / 待拍板编号」；
  **原文没给的一律是占位值**（怒气上限、三招式伤害、额外伤害值、BOSS 普攻与高额伤害、减疗持续回合、
  回合上限、百分比基数、取整口径、**道具效果**——占位「1004 治疗药水回复 30% 生命上限」，注释标「占位，等 C91」），等 **C91**；
  适用范围（这套回合制是不是所有 BOSS 都用）等 **C90**；界面等 **C92 / Q19**。
- 两处各写一遍默认值是有意的（配置要能在 Inspector 里改，规则要能脱离 Unity 测）：
  `TurnBasedConfig` 字段默认 ↔ `BattleSettings.PlaceholderDefault` 由测试
  `TurnBasedKernelTests.DefaultConfigAsset_MatchesPlaceholderSettings` 钉住，**不许漂移**。

## 已知约束（读代码前先知道这些）

- **`BattleSession` 只被接线侧驱动**：它不会自己往前跑，也没有协程 / 计时器；表现层要按 `Phase` 收输入、
  按 `Events` 播表现（每次动作前事件列表会清空，只反映最近一次动作）。现在的驱动者是 `BattleFlow.FightAsync`（`BattleFlow.cs:291-322`），它在每次动作后立刻拷贝事件交给表现层。
- **玩家血量由外部注入**（`PlayerBattleSnapshot`）：原文没写玩家血量，本模块不设占位血量。`Game.Battle` 注入的是 `BossDefinition.PlayerHealth`
  （占位 10，满血开打，战后不回写），**不读 `PlayerModel`、不是探索血量**（`BattleSetup.cs:95-105`、`BossDefinition.cs:20,35`）。
- **百分比一律向下取整**（整数运算，`BossBattleRules.cs:85/100`）；原文没写取整口径，见交付报告「待拍板」。
- **减疗重复施放是覆盖不是叠加**；「酩酊 −50」占位口径是**进入酩酊时降一次**，两种口径都有配置开关，
  可随 C91 改而不动代码。
- **玩家被晕眩的回合计法**占位「整个玩家回合跳过」（`StunTurnPolicy.SkipTurn`），另一口径
  `ItemsOnly`（不能出招但能用道具）也实现了，切换只需改配置。
- **战斗结束条件**：BOSS 生命归零 → 胜；玩家生命归零 → 负；回合上限（配 0 = 不限）按配置口径判。

## 验证入口

- EditMode：`Assets/_Project/Scripts/Tests/EditMode/TurnBased/`——**12 个测试类**（比旧版多 `BattleItemEffectTests`，道具效果占位）；PRP `turnbased-battle` §9 记 W1 时该组 165/165 通过
  （这是 PRP 的回填数字，不是本文数出来的）。每条判定都配负对照：进战斗的三条路径各配「条件少一条」的反例、怒气不足 / 同回合第二招 / 晕眩中出招、
  未拥有与已用过的道具、醉酒阈值 49/50、79/80、99/100 逐个数到、6:3:1 的分布与同种子可重复、
  BOSS 生命归零结束。
  跑法：`/unity-test EditMode TurnBased`（MCP `run_tests(group_names=["Game.Tests.EditMode.TurnBased"])`）。
- 测试辅助：同目录 `FixedRandomStream.cs`（按剧本吐数，多抽一个就抛）、`FakeBattleItemInventory.cs`。
- **Showcase**：本模块没有独立的 `Showcase/TurnBased/`；回合制战斗的回放在 [`Game.Battle` 的 `Tests/Showcase/Battle/`](../battle/battle-module-guide.md)
  （两条用例：打赢、被击倒，`BattleShowcase.cs`）。**回放不覆盖随机流**（见上「随机数与回放」）。
- 界面（招式格 / 怒气槽 / 血条 / 中央闪现提示）：**已有白盒**，在 `Game.Battle` 的 `BattleView`（`BattleView.cs:1-8` 文件头列了布局），
  细节仍等 C92 / Q19，不属于本模块。

## 接线清单（现状：已由 `Game.Battle` 接上的与仍缺的）

| 环节 | 状态 | 说明 / 出处 |
| --- | --- | --- |
| 开仗装配 | **已接** | `BattleSetup.TryCreate` 拼快照并调 `TurnBasedKernel.TryStartBattle`（`BattleSetup.cs:82-114`） |
| 进战斗判定 | **只接了一种** | 固定拼「正面攻击、玩家先手」的请求（`BattleSetup.cs:104`）；偷袭 / 被打两条路径没有调用方——**`EncounterStep` 的进战斗判定（潜行 / 偷袭是否命中、是否在警戒·敌对区域、怪物警觉档位、谁先动手）仍没接**，需要 Monster 侧的警觉档位与区域查询 |
| 玩家血量快照 | **已接，口径与原计划不同** | 读 `BossDefinition.PlayerHealth`（占位 10），不取 `PlayerConfig.maxHealth`、不读玩家状态（`BattleSetup.cs:95-105`） |
| BOSS 血量与战斗外醉酒值 | **已接，出处是占位** | 读 `BossDefinition.MaxHealth`（`BossDefinition.cs:64`）与 `BossDefinition.OutOfBattleDrunk`（`BossDefinition.cs:67`），经 `BattleSetup.cs:105-106` 喂给内核，资产 `BossRosterConfig.asset:18-19` 为 12 / 50。**战斗外醉酒值仍没有「唯一真实出处」**（02:109 写 BOSS 100，口径未明，`BossDefinition` 文件头注释写明），等 C91 / 00 §8.1 #1；`MonsterConfig.ResolveMaxHealth` 没接 |
| 背包口 | **已接** | `LootBattleBackpack` / `BattleItemInventory` 实现 `IBattleItemInventory`（`BattleItemInventory.cs:15`），扣减走 `LootService.TryConsume`（`LootBattleBackpack.cs:38`） |
| 道具效果 | **占位** | 只有治疗药水回复百分比（`BattleItemSettings`），等 C91 |
| 结果回写（回合制路径） | **已接** | `BattleSession.ExitKey` → `NarrativeService.CompleteBattleAsync`（`BattleFlow.cs:243`），在黑幕下、揭幕之前（`BattleFlow.cs:201-202`） |
| 结果回写（潜行 / 遭遇路径） | **仍缺** | `EncounterStep.PendingResult`（`EncounterStep.cs:127`）没有消费方；roadmap C5 只覆盖了回合制这一条 |
| 界面 | **白盒已接** | `BattleView`，细节等 **C92 / Q19** |
| Boot 接线 | **已接** | `Boot.unity` 的 `GameBootstrap` 挂了 `BattleInstaller`（`BattleInstaller.cs:37`；`Boot.unity` 里能反查到它的 GUID） |
| 适用范围 | 只给 `sample_boss` | 等 C90 |
