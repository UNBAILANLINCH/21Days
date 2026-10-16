---
type: module-guide
module: battle
layer: runtime
maturity: seed
---

# Battle 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Battle/` 之前读这份。对外怎么调看 [`battle-external-api.md`](battle-external-api.md)，
> 要加 BOSS / 换表现 / 加演出看 [`battle-extension-guide.md`](battle-extension-guide.md)。
> 设计与进度：[`PRP/turnbased-battle/prp.md`](../../../../PRP/turnbased-battle/prp.md)（§2 决策 D1–D11、§3 数据流、§9 进度回填）。
> 规则内核在 [`turnbased-module-guide.md`](../turnbased/turnbased-module-guide.md)；与源码不符时以源码为准。
> 本文每条「现在怎样」都带源码出处；`maturity: seed` 是因为视觉验收还没做（审查意见已修完，见 PRP §9「审查意见修正」行）。

## 职责边界

**做**：把 `Game.TurnBased` 的纯规则内核接成一场看得见、打得完的仗——订阅剧情的「停在 Battle 阶段」通知、查 BOSS、
按住世界、黑幕下叠加加载战斗场景、驱动 `BattleSession` 回合循环、翻成舞台演出与界面、收场后按出口键回写剧情
（`BattleFlow.cs:1-9` 文件头概括了全流程）。

**不做**：

| 不做 | 归属 |
| --- | --- |
| 回合规则、数值、醉酒 / 怒气 / 道具判定 | `Game.TurnBased`（本模块只调 `BattleSession`，不改规则） |
| 剧情推进、阶段迁移、结果身份校验 | `Game.Narrative`（本模块只发起 `TryBeginBattle` / `ReleaseBattle` / `CompleteBattleAsync`） |
| 背包数据写入 | `Game.Loot`（本模块经 `LootService.TryConsume` 扣 1，`LootBattleBackpack.cs:38`） |
| 巡逻怪的实时遭遇、潜行、偷袭、处决 | `Game.Monster` / `Game.Stealth`；本模块**按遭遇侧给的局势快照开战**（`BattleSetup.TryCreate(boss, context, ...)`），拿不到快照时退回「正面攻击、玩家先手」（`BattleSetup.cs` 的 `BuildEntryRequest`） |
| 出招动画的正式美术 | 美术（现为位移 + 闪白 + 震屏 + 飘字，见下「占位与待拍板」） |

## 为什么新建模块（D1）

`PRP/turnbased-battle` D1：新建 `Runtime/Battle/`，命名空间 `Game.Battle`，走 `Game.Runtime` 程序集，不建独立 asmdef。
`BattleFlow.cs:11-16` 文件头写明了「加能力的顺序」：

1. 复用不成立：工程里没有「一场回合制战斗怎么开、怎么推、怎么收」的流程；`EncounterStep` 管的是巡逻怪的实时遭遇（确定性 tick）。
2. 扩展不成立：`Runtime/TurnBased` 必须保持纯规则、不依赖 Narrative / Loot / Player / Core.UI（见 turnbased guide「依赖方向」），
   塞进去内核就测不了；`Runtime/Narrative` 只发通知、不管胜负；`Runtime/Monster` 本波禁改。

## 依赖方向

```text
Game.Battle（Runtime/Battle/，走 Game.Runtime）
  ├─► Game.TurnBased      BattleSession / TurnBasedKernel / BattleSettings（BattleSetup.cs 的 TryCreate）
  ├─► Game.Monster        IBattleEntryContext（**只读契约**，`EncounterStep` 实现）+ BattleInstaller 的
  │                       `TryResolve<EncounterStep>` 构建回调 → `BattleFlow.BindEntryContext`——
  │                       加它是为了让 07 的偷袭 / 被打两条分支有生产入口（2026-10-08）。
  │                       ⚠️ 反向不许：Monster 不认识 `Game.Battle`（契约声明在 Monster 侧，
  │                       依赖边只有 Battle → Monster 一条）
  ├─► Game.Narrative      BattleStageEnteredEvent 订阅（`BattleFlow.Start`）+ NarrativeService 三方法（NarrativeBattlePort.cs:21-33）
  ├─► Game.Loot           LootService.Items / TryConsume（LootBattleBackpack.cs:24,38）
  ├─► Game.CharacterPuppet  BattleActor 挂载纸片小人（BattleActor.cs 顶部 using，行为在 BattleActor.cs:271-273）——新增的横向依赖
  ├─► Game.Core.UI        IUIService 开 BattleView、藏 HUD 层（BattleWorldLock.cs:43-44、BattleScenePresenter.cs:96）
  └─► Game.Core.Flow      ILoadingCurtain 黑幕、IGameFlow 门闸（BattleArena.cs:57、SceneWorldGate.cs:22）
      另用 Core.Assets（叠加加载，BattleArena.cs:123）、Core.Input（关 Gameplay 图）、Core.Timing（暂停令牌）、Core.Telemetry
```

- **反向禁止**：`Game.TurnBased` / `Game.Narrative` / `Game.Loot` / `Game.CharacterPuppet` 不认识 `Game.Battle`。
  除 `Game.Battle` 外，`Runtime/` 下没有任何文件引用 `Game.TurnBased`（`grep -rl "Game.TurnBased"` 的结果）。
- **`BattleActor` → CharacterPuppet 是新增的横向依赖**：战斗舞台上的角色是纸片小人，`BattleActor` 要拿到 `ChibiPuppet`
  并**关掉小人自带的驱动层 `ChibiPuppetMotion`**，自己写走跑与朝向（`BattleActor.cs:271-273` 关驱动并取 `ChibiPuppet`、
  `:276-278` 写 `SetMoving`、`:134` 写 `SetFacing`）。原因见 `BattleActor.cs:7-8`：`ChibiPuppetMotion` 在 `timeScale = 0` 时每帧把小人写回待机。
  CharacterPuppet guide 的「同一小人只允许一个驱动者」约束在战斗舞台上是由本组件接手的。
- 两个同名 `BattleOutcome`（`Game.TurnBased` / `Game.Narrative`）：同一文件不要同时 `using` 两个命名空间，
  结果只走 `BattleExitKeys` 字符串；`BattleFlow.cs` 与 `BattleInstaller.cs:27-28` 用别名取剧情侧类型。

## 运行时类分工

| 类 | 是什么 | 出处 |
| --- | --- | --- |
| `BattleFlow` | **流程**（根作用域入口点，`AsSelf`）：订阅通知 → 查 BOSS → 登记在途 → 等门闸 → 按住世界 → 开仗 → 回合循环 → 收场回写 | `BattleFlow.cs`；`IsBattleRunning` `:93`、`IsBattleEngaged` `:96` |
| `BattleSetup` | 开仗装配：本场玩家 / BOSS 快照 + 本地随机流 + 背包口，按**局势快照**（`IBattleEntryContext`，可空）进 `TurnBasedKernel`；含回放测试口 | `BattleSetup.cs`（`TryCreate` 两个重载）、`OverrideSettings` |
| `BattleArena` | 战斗场景宿主：黑幕落 → 叠加加载 `BattleArena` → 表现开场 → 揭；收场对称 | `BattleArena.cs:49`（`EnterAsync`）、`:82`（`ExitAsync`） |
| `BattleWorldLock` | 战斗期间按住世界：暂停令牌 + 关 Gameplay 图 + 藏 HUD 层；Dispose 只恢复进来前的状态 | `BattleWorldLock.cs:34` |
| `SceneWorldGate`（`IBattleWorldGate`） | 开战门闸：当前是 `SceneGameState` 且黑幕没盖才算就绪 | `SceneWorldGate.cs:22` |
| `NarrativeBattlePort`（`IBattleNarrative`） | 剧情口：三方法转 `NarrativeService`；回写前等剧情空闲 | `NarrativeBattlePort.cs:21,24,27` |
| `LootBattleBackpack`（`IBattleBackpack`）+ `BattleItemInventory` | 背包口：Loot 的 int id ↔ 内核字符串 id；只认 Consumable；道具格清单 | `LootBattleBackpack.cs:24-38`、`BattleItemInventory.cs:26,30,36` |
| `BossRoster` / `BossRosterConfig` / `BossDefinition` | BOSS 名册：payload → 定义（id / 显示名 / 生命 / 战斗外醉酒 / 本场玩家生命 / 舞台预制体），**外加两种扩展**：`monsterKindId`（按怪物种类反查，供遭遇升级）与发难五项（`aggroEnabled` / 冲刺 / 半径 / 反应时间 / 拴绳） | `BossRoster.cs:67`（`TryGet`）、`:77`（`TryGetByKind`）、`BossRosterConfig.cs:23`、`BossDefinition.cs` |
| `BattleScenePresenter`（`IBattlePresenter`） | 表现实现：找舞台、接管相机、开 `BattleView`、事件→演出、等玩家指令、收场容错交还 | `BattleScenePresenter.cs:31`；接口 `IBattlePresenter.cs:23` |
| `BattleStage` / `BattleActor` | 战斗场景里的舞台根 / 一个角色（站位、出招位移、闪白、后仰、醉晃、倒地） | `BattleStage.cs:20`、`BattleActor.cs:22` |
| `BattleCameraHandoff` | 相机交接：关世界相机组件、开战斗相机，收场只开回「进来时开着的」 | `BattleCameraHandoff.cs:17` |
| `BattleView` | 战斗界面 `UIView`（Panel 层、全屏、`CloseOnCancel = false`）；只显示与抛事件 | `BattleView.cs:125-130` |
| `BattleCueRules` / `BattleHudRules` | 纯函数：事件→演出种类与字样 / 界面该亮该暗该写什么 | `BattleCueRules.cs:36`、`BattleHudRules.cs:18` |
| `BattleInstaller` | `GameplayInstaller`：注册以上全部，挂 Boot 的 `GameBootstrap` 末尾 | `BattleInstaller.cs:37-86` |

注册的类型没有一个是 `IGameService`（`BattleInstaller.cs:5-6`），所以挂在 `GameBootstrap` 组件列表哪个位置都不改变启动串行；
`Install` 只 Register 不 Resolve。`IBattlePresenter` 用 `TryResolve` 取，取不到时开战报错放弃（`BattleInstaller.cs:84`、`BattleFlow.cs:152-158`）。

## 数据流（对应 PRP §3）

```text
SampleScene 的 Npc_SampleBoss（NarrativeTrigger + DialogueInteractable）── 按 E
  → NarrativeService.TryEncounterAsync → 故事 sample_boss_battle：taunt（对白）→ fight（Battle，payload = sample_boss）
  → DriveAsync 停在 Battle 阶段，同步发布 BattleStageEnteredEvent（NarrativeService.cs:286-290）
BattleFlow.HandleStage（`BattleFlow.cs:277`）→ Begin（`BattleFlow.cs:290`）
  0 payload 为空 → **安静跳过**（活遭遇档，归 Monster 的 EncounterBattleBridge）：一条 Info + 埋点 stage_live_encounter（`BattleFlow.cs:301`），
                  不再记 boss_not_found 的 Error（接线前每次进活遭遇档都会刷一条）
  1 roster.TryGet(payload)                    找不到：Error + 埋点 boss_not_found，不开仗
  2 narrative.TryBeginBattle(stage)           登记「战斗在途」，之后 CanSave = false（`BattleFlow.cs:352`）
  3 RunAsync（`BattleFlow.cs:365`）：gate.WaitUntilReadyAsync → worldLock.Engage → ResolveEntryContext → setup.TryCreate → arena.EnterAsync
    （黑幕落 → 叠加加载 → presenter.OpenAsync → 黑幕揭）
  4 回合循环：BOSS 回合 session.RunBossTurn；玩家回合 presenter.WaitCommandAsync → Apply；每次动作后 presenter.PlayAsync
  5 arena.ExitAsync：黑幕落 → presenter.CloseAsync → 卸场景 → 恢复世界 → 剧情回写 → 黑幕揭
  6 finally：没回写成功就 narrative.ReleaseBattle
Narrative：Victory → cleared（写 world.sampleboss.defeated，NarrativeFlagVisibility 隐藏 BOSS）；Downed → retreat（可再按 E）

**第二条入口（2026-10-14，遭遇版）**：巡逻怪那一路不经过阶段机，但走**同一个内核**——
```text
EncounterStep 打击命中（玩家打中怪 / 怪打中玩家）→ 问 IEncounterEscalationPort.TryEscalate（实现在 BattleFlow.cs:217）
  → 接受：BattleSetup.TryCreate(boss, in request) 开好会话（触发情形→开战方式、实时血量→按比例换算），登记为在途
  → RunEncounterAsync（BattleFlow.cs:402）→ RunCoreAsync（同一内核）
  → 收场幕下 CompleteEscalationUnderCurtain（BattleFlow.cs:523）：回调 EncounterStep.CompleteEscalation(胜, 剩余生命, 上限)
  → 没打完（开场失败 / 取消 / 出错）调 EncounterStep.AbortEscalation
```
两条入口的差别只有「会话从哪来」与「幕下交回给谁」（`BeforeReveal`，`BattleFlow.cs:469`）；剧情三方法在遭遇版里一个字都不碰。
**已有仗在途时拒绝升级**（`IsBattleRunning`），Monster 侧退回原路径。
```

内容出处：`Tables/Data/narrative/sample_boss_battle.json`（四阶段：taunt / fight / cleared / retreat）、
`Tables/Data/narrative_encounters.json` 的 `sample_boss` 行（`trigger: Interact`、`targetKind: SampleBoss`、`repeat: Reenter`、条件「玩家存活 + 标记 `world.sampleboss.defeated` 为假」）。

## 收场时序（PRP §3 第 6、7 步现状）

**现在的顺序是「黑幕落 → 关界面、卸场景、恢复世界 → 剧情回写 → 黑幕揭」**，回写排在揭幕之前（`BattleFlow.cs:1-9`、`BattleArena.cs:75-81`）：

| 步 | 做什么 | 出处 |
| --- | --- | --- |
| 1 | 落幕；落幕失败只记日志、照样收场 | `BattleArena.cs:91-98` |
| 2 | `presenter.CloseAsync`（容错）→ 卸载战斗场景 | `BattleArena.cs:100-106` |
| 3 | `whileCovered`（= 世界锁的 Dispose：恢复 HUD / 输入图 / 暂停令牌） | `BattleArena.cs:109`、`BattleFlow.cs`（`RunCoreAsync` 的 finally） |
| 4 | `beforeReveal`（= `WriteBackUnderCurtainAsync` / 遭遇版 `CompleteEscalationUnderCurtain`）：发起 `CompleteBattleAsync` 或回调遭遇；胜利时 `NarrativeFlagVisibility` 在幕下就把 BOSS 藏好 | `BattleArena.cs:110`、`BattleFlow.cs:533`、`:523` |
| 5 | 揭幕（出错也揭，只留痕） | `BattleArena.cs:112-115` |

- **1 秒兜底**：幕下最多等回写 `DefaultWriteBackCoverSeconds = 1.0` 秒（真实时间，`BattleFlow.cs:78`）。常态下结局是 End 阶段，回写同步走完一帧不等；
  剧情忙（`NarrativeBattlePort` 要等 `IsBusy` 为假，`NarrativeBattlePort.cs:31`）或战后接了对白时才会等满。
- **超时的处理**：先揭幕、回写在幕后继续，打 Warn 并埋 W 级 **`battle/write_back_outlived_curtain`**（`BattleFlow.cs`）；
  此时 BOSS 退场可能落在揭幕之后。揭幕后 `RunAsync` 再 `await run.WriteBack`（`BattleFlow.cs:375`），异常在那里抛出并记日志。
- **回写被拒或抛异常**：照样揭幕；被拒埋 `result_rejected`（`BattleFlow.cs`），finally 里 `ReleaseBattle`，阶段原地不动、可重打。
  没打完（异常 / 取消，`exitKey` 为空）不回写（`BattleFlow.cs:458`）。
- **开战失败也是同一顺序**：`EnterAsync` 的 catch 在黑幕下先恢复世界、再揭幕，不会露出一小段被暂停的世界画面（`BattleArena.cs:62-72`）。
  `BattleFlow.cs:463` 的 `engagement.Dispose()` 是幂等兜底。
- 为什么回写要在幕下：原先揭幕后才回写，玩家会看到 BOSS 凭空消失（`BattleFlow.cs:5-7`）。

## 随机流与「回放不覆盖回合制战斗」（D8）

- 战斗在确定性内核（tick）之外推进，会话状态不进快照；用 `logic.*` 流会让回放在战斗之后哈希对不上，用 `view.*` 又违背「表现流不影响逻辑」的语义
  （`BattleSetup.cs:23-30`）。
- 所以每场开打 `new XorShiftRandomStream(主种子 ^ 盐 ^ 场次)`（`BattleSetup.cs:262`），**不登记进 `IRandomService`、不进任何快照**。
  主种子由 `BattleInstaller` 经 `RandomService.MasterSeed` 提供（`BattleInstaller.cs:216`）。
- **代价：回放不覆盖回合制战斗**（`BattleSetup.cs:27-30`）。这条是 D8 留给 turnbased guide「随机数与回放」一节的结论。
  本流没有测试口（种子取会话主种子），回放要让胜负可控只能靠数值，见下「测试口」。

## 战中不存档（D9）

- 开打前 `TryBeginBattle` 登记「战斗在途」（复用阶段帧的 `RequestIssued`，`NarrativeService.cs:229-240`）→ `NarrativeService.CanSave` 为假
  （`NarrativeService.cs:71`、`IsStable` 在 `:345-347`）→ Session 的 `NarrativeStable` 闸门（`SessionStateAdapter.cs:72`、`GameSession.cs:251,275`）
  挡住自动保存与离场保存。**Session 本身没改**（`BattleWorldLock.cs:7-8` 同样说明）。
  <br>**2026-10-14 起另加一道**：实时遭遇升级那一路不经剧情，`NarrativeStable` 挡不住它，所以 `SessionStateAdapter.BattleResultPending`
  把「升级在途」也并进存档闸（`SessionStateAdapter.cs:88-89`）——战中同样不可存档。
- 战斗会话状态不进快照（不升 `ReplayFormat`）。**读档时若剧情停在 Battle 阶段，`ReloadFromSave` 先清掉存档里残留的在途标记，再重发 `BattleStageEnteredEvent`，战斗从头开始**
  （`NarrativeService.cs:116-122`）。读档恢复时世界可能还在标题 → 靠 `SceneWorldGate` 等「场景状态且黑幕没盖」才开战（`SceneWorldGate.cs:22`、`BattleFlow.cs` 的 `gate.WaitUntilReadyAsync`）。
- 还在等世界就绪时换了身份的新通知会顶掉旧请求；同身份重复通知忽略并埋 `stage_duplicate`；已经按住世界后来的新通知忽略并埋 `stage_ignored_busy`
  （`BattleFlow.cs:322-327`、`:305-314`）。

## 相机接管规则

- **战斗相机不能打 `MainCamera` 标签**：`QuestSceneBinder` 在每次场景加载 / 卸载回调里把 `Camera.main` 缓存进 `SceneCamera`
  （`QuestSceneBinder.cs:57,187,199`），战斗相机若是 `MainCamera`，缓存的就是一台随战斗场景销毁的相机。`BattleArena.unity` 里的标签全是 `Untagged`，
  `BattleStage` 的相机字段 tooltip 也写明「不打 MainCamera 标签、不挂 AudioListener」（`BattleStage.cs:27`）。
- **接管 = 关世界相机的 `Camera` 组件、不关物体**（`BattleCameraHandoff.cs:48`），所以世界相机上的 `AudioListener` 与跟随脚本照常；
  战斗期间 `Camera.main` 为 null（`BattleCameraHandoff.cs:7-9`）。
- 取「世界里正在渲染的相机」用 `Camera.allCameras` 排除战斗场景自己的（`BattleScenePresenter.cs:385-388`）；
  战斗相机的底色（清屏方式、背景色、后处理、Volume 遮罩、抗锯齿）从世界主相机拷一份（`BattleCameraHandoff.cs:86-98`、`BattleScenePresenter.cs:383,390`）。
- 收场交还：战斗相机回到接管前的开关、被关的相机重新打开（`BattleCameraHandoff.cs:61-79`），**在卸载场景之前**——`CloseAsync` 的 finally 里先 `cameras.Release()`（`BattleScenePresenter.cs:183-184`），因为卸载时 `FallbackCamera` 要看到世界相机已经开着，才不会抢回画面。
- 战斗场景叠加加载、**不切 `ActiveScene`**（`BattleArena.cs:4`、`:123`）：否则别的模块 `Instantiate` 会落进战斗场景跟着被卸载。舞台根放在世界原点之外
  （场景里根节点 y = −1000，PRP §9 W2a 行；`BattleStage.cs:7-8`）。实测 `ActiveScene` 一直是 Boot，舞台吃的是 Boot 的 `RenderSettings`（雾 / 环境光，PRP §9 W2a 行）。

## SceneBinder 审计结论

PRP §9「W2a」行：**叠加加载会触发各模块的 `SceneManager.sceneLoaded` 绑定器，逐个审计十项，全部安全**；唯一的前提是上一节的
「战斗相机不能打 MainCamera 标签」（`QuestSceneBinder` 缓存 `Camera.main`）。审计范围是 PRP §7 坑 1 列的 Dialogue / Loot / Mirror / Exploration 罗盘 / World 等绑定器与
`Core/Boot/FallbackCamera`。新增绑定器或改现有绑定器的场景回调时，要重新核「有没有把当前场景改指到战斗场景」「卸载时有没有清掉对世界场景的绑定」。

## 不受缩放的时间（timeScale = 0）

世界暂停令牌把 `Time.timeScale` 置 0（`BattleWorldLock.cs:31`；统一暂停服务是唯一写 timeScale 的地方，PRP D5），所以**战斗场景里一切表现都必须走不受缩放的时间**：

| 表现 | 做法 | 出处 |
| --- | --- | --- |
| 位移 / 后仰 / 醉晃 / 跳 / 倒地 | `LMotion` 用 `MotionScheduler.UpdateIgnoreTimeScale` | `BattleActor.cs:148,193,207,226,244` |
| 闪白 / 顿帧 / 提示停留 | `UniTask.Delay(..., ignoreTimeScale: true, ...)` | `BattleActor.cs:177`、`BattleStage.cs:127`、`BattleView.cs:252` |
| 血条动画 | `UpdateIgnoreTimeScale` | `BattleBarWidget.cs:51` |
| 开战门闸 | 按帧等、不按时间等 | `SceneWorldGate.cs:28-30` |
| 剧情回写的 1 秒兜底 | `UniTask.Delay(..., true, ...)` 真实时间 | `BattleFlow.cs`（`WriteBackUnderCurtainAsync`） |
| 小人 Animator | 必须 `UnscaledTime`（CharacterPuppet 约定；本模块不改） | `characterpuppet-external-api.md` 禁止事项 |

新加任何战斗表现，**不许用 `Time.deltaTime` / `WaitForSeconds` / 默认调度的 `LMotion`**——世界暂停时它们不走。

## Esc 与暂停菜单（D7）

`BattleView.CloseOnCancel = false`（`BattleView.cs:140`）→ `UICancelRouter` 判 Blocked，Esc 既不关战斗界面也不开暂停菜单；P 键在 Gameplay 图上，被世界锁关掉；
进出场黑幕盖着时 `PauseMenuController` 的 `curtain.IsCovered` 条件挡住（`BattleScenePresenter.cs:12-14` 文件头的完整论证）。
键盘 1/2/3 出招借用 Dialogue 图的 Choice1–3，只在战斗期间启用（`BattleScenePresenter.cs:10-11`、`:34`）。

## 测试口 `BattleSetup.OverrideSettings`

- **只在 `Debug.isDebugBuild` 下可用**：正式包一调就抛 `InvalidOperationException`（`BattleSetup.cs:85`）；同一时刻只容一份，上一份没 Dispose 又换会抛（`:89`）。
- **开打时会记埋点**：每场开打若处于替换状态，`BattleFlow` 埋 W 级 **`battle/settings_overridden`**（`BattleFlow.cs:436`），日志里一眼能看出「这一仗用的不是配置资产的数值」。
- 它必须是 `public`：调用方是 `Game.Tests.Showcase`，而 `Game.Runtime` 不对测试程序集开 `InternalsVisibleTo`（`BattleSetup.cs:76-82`）。**玩法代码不得调用。**
- 返回句柄 Dispose 即恢复；只影响之后开打的仗（`BattleSetup.cs:73-75`）。不改任何配置资产。
- 表现层（`BattleScenePresenter`）仍拿装配时那份数值画招式说明（`BattleSetup.cs:20-24`）；只换 BOSS 招式时两边不矛盾，换玩家招式数值会不一致。

## 交互提示「挑战」与交互转向（✅ 已做，原待办「交互动词写死为『对话』」，PRP §9「收尾修正」④）

- **动词**：BOSS NPC 的提示是「[E] 挑战 · 阶段一 BOSS（占位）」：统一交互（`PRP/interaction` D10）给 `DialogueInteractable` 加了 `verb` 字段（默认「对话」），
  SampleScene 的 `Npc_SampleBoss` 配成「挑战」；提示由 `Game.Interaction.InteractPromptHudView` 显示，胶囊宽度随文字伸长（320–640），整句不截断。本模块没有自己的动词机制。
- **转向**：按 E 时统一焦点抛 `OnInteracted`，`InteractionPuppetPresenter` 让 BOSS 小人转向玩家、玩家小人转向 BOSS（朝向保持，D11）。
  **不带进战斗**：`BattleArena` 里的两只小人是另一份实例（BOSS 按 `StagePrefab` 现实例化，玩家是战斗场景里摆好的），`BattleActor.BindVisual`
  关掉它们的 `ChibiPuppetMotion`（朝向保持唯一的读方，`BattleActor.cs:271-272`），`ResetPose` 再 `SetFacing(faceLeft, true)` 立即翻面布台（`BattleActor.cs:134`）。
  所以本模块**不需要**在战斗接管时解除保持，`BattleActor` 一行没改。世界里那只 BOSS 小人回到世界后仍面向玩家上次站的方向（NPC 不走动，保持不解除）。
  回放 `Victory_BossLeavesAndFlagWritten` 断言了这三件事（BOSS 与玩家进入保持、战斗小人不是同一实例且无保持、朝向等于 `BattleActor.FaceLeft`）。

## 占位与待拍板（PRP §8）

| 项 | 现状（出处） | 等 |
| --- | --- | --- |
| 回合制适用范围 | 两条：`sample_boss`（剧情 BOSS 战）与 `monster_1003`（市令，`monsterKindId = 1003`，供实时遭遇升级查，`BossRosterConfig.asset`） | C90 |
| 全部数值（血量、伤害、回合上限、怒气上限）、道具效果、玩家血量是否回写 | **2026-10-16 起数值大半已落地**（招式 18/23/32、BOSS 14/22、阶段一 BOSS 220 血，见 `turnbased-module-guide.md`「配置与占位值」）；玩家血量**不读 Player 模块**，读 `BossDefinition.PlayerHealth`（`BattleSetup.cs:183` 取本场上限、`:191` 按实时比例换算），**120**、战后不回写 | C91（只剩回合上限、百分比基数、醉酒值上限、晕眩口径） |
| 界面细节，与美术需求表 BOSS 血条 / 结算界面怎么合 | 照 07 布局图做白盒（`BattleView.cs:1-8`） | C92 / Q19 |
| BOSS 是谁、外观；战斗外醉酒值 | 「阶段一 BOSS（占位）」，战斗外醉酒值占位 50（`BossRosterConfig.asset:19`；`BossDefinition.cs` 文件头注明口径未明，02:109 写 BOSS 100）；**BOSS 生命 220、本场玩家生命 120**（`BossRosterConfig.asset:18,20`；2026-10-16 起血量有出处 07:149） | 00 §8.1 #1 |
| 发难数值（半径 / 反应时间 / 冲刺 / 拴绳） | **占位已落地**：半径 5、反应 3 秒、冲刺 3、拴绳 = 半径 × 2（`BossRosterConfig.asset` 的 `sample_boss` 行；出处是 2026-10-15 用户拍板，不是策划文档） | 等策划给正式数值 |
| 出招动画 | 位移 + 受击闪白 + 震屏 + 伤害飘字，招式 3 加重版（`BattleStage.cs:42-62`、`BattleCueRules.cs:36-45`） | 美术（D6 角色动画） |
| BOSS 定义进 Luban 表 | D3：本期不进表，`BossRosterConfig` 是过渡（`BossRosterConfig.cs:3-5`） | C90 / C91 拍板后迁表，届时删本资产 |

## 验证入口

- **EditMode**：组 `Game.Tests.EditMode.Battle`（`Assets/_Project/Scripts/Tests/EditMode/Battle/`，例：`BattleFlowTests.cs:28`），
  跑 `run_tests(group_names=["Game.Tests.EditMode.Battle"])`。涉及改动时再加 `Game.Tests.EditMode.TurnBased`（道具效果）、`Game.Tests.EditMode.Narrative`
  （`DriveAsync` 发布点 / `IsStable` 口径）、`Game.Tests.EditMode.Loot`（`TryConsume`）、`Game.Tests.EditMode.Dialogue`（交互转交）、
  `Game.Tests.EditMode.Monster`（升级端口的调用侧）。
- **回放**：`Assets/_Project/Scripts/Tests/Showcase/Battle/BattleShowcase.cs`，**六条用例**，走 Boot 真实流程（`ScenePath => null`）：
  ① `Victory_BossLeavesAndFlagWritten`：打赢后相机 / 输入 / 暂停 / HUD 全部恢复、写 `world.sampleboss.defeated`、BOSS 退场、再按 E 不触发；
  ② `Downed_RetreatsAndCanFightAgain`：被击倒走 retreat、BOSS 还在、标记没写、再按 E 能重打；
  ③–⑤ BOSS 主动发难三条：`BossAggro_ChallengeWithinReaction_FrontalAttack`（反应期内按 E）、`BossAggro_ChargesTauntsAndAmbushes`（磨蹭到计时满 → 冲 → 被打）、
  `BossAggro_RetreatThenRearmsAfterLeaving`（打完撤退 → 解除武装 → 离开察觉范围 → 再进半径重新警觉）；
  ⑥「较量」那两条（`Drill_InteractStartsTheBriefAndEntersTheLiveEncounter` / `Drill_DownedEndsTheStageInsteadOfSticking`）走的是遭遇升级那一侧。
  跑法：`/verify-module Battle`，范围限定在 Battle 单个模块。最近一次记录：2026-10-16 **6/6 PASS、检查点失败 0、异常 0**（`Logs/verify/battle/20261016-235652`）。
- **回放坐标**：SampleScene 的 `Npc_SampleBoss` 在 **(-4, 4.89, -8)**（院子里找不到离出生点与各回放路线都 ≥8 米的位置，用户 2026-10-16 定：挪到村南围栏外，
  去挑战它要先绕过巡逻线之间的缺口）。回放站位随之改到它正东 1.4 米（见 `BattleShowcase.cs` 的站位常量）；发难半径 5 米，所以「进半径 → 走到跟前 → 按 E」要在 3 秒反应期内连着做完。
- **重建战斗白盒**：编辑器菜单「21Days/战斗/重建战斗白盒（界面 + 战斗场景 + 占位 BOSS）」（`BattleWhiteboxBuilder.cs:82`）重建 `BattleView.prefab`、`BattleArena.unity` 与占位 BOSS 预制体。
- 回放截图时 `core.perf/spike` 警告计数会涨，不是功能出问题，见 `ai-docs/pitfalls.md` 对应条目。

## BOSS 主动发难（`BossAggro`，PRP/combat-iteration D6）

`BossAggro`（MonoBehaviour，挂在 BOSS 物体上）是「剧情 NPC 自己冲上来开战」的那一段；**数值全在 `BossDefinition` 那一行**，代码默认关 = 只能按 E 挑战。

| 环节 | 行为 | 出处 |
| --- | --- | --- |
| 武装 | 场景加载时 `BossAggroSceneBinder` 扫场景绑定（余额取外部世界 + 名册那一行）；名册没开 / 局势没接上 / 交互入口缺 → 完全不动作 | `BossAggroSceneBinder.cs:88`（`Start`）、`:120`（`BindScene`） |
| 警觉 | 玩家进**发难半径**（以原位为圆心的水平距离）→ 头顶提示亮、开始计反应时间；守卫挡着时暂停计时 | `BossAggro.cs:266`（`TryAlert`） |
| 反应期内按 E | 理由不动（仍是「玩家先动手」）→ 正面攻击、玩家先手，BOSS 不冲 | `BossAggro.cs:286`（`TickAlerted`） |
| 计时到点 | 冲刺（渲染帧积分，不经逻辑 tick、不拖影）；离原位超过拴绳 → 放弃、走回原位 | `BossAggro.cs:315`（`TickCharging`） |
| 接触 | 先把 `BossEntryContext.Initiator` 置成「BOSS 先动手」，再以剧情入口的名义发起与按 E **完全相同**的交互（嘲讽对白 → Battle 阶段） | `BossAggro.cs:362`（`BeginContact`） |
| 战后 | 理由复位（按 E 挑战仍是正面攻击）、幕下瞬移回原位、转入解除武装，等玩家离开察觉范围才重新待机 | `BossAggro.cs:415`（`TickInBattle`） |

**守卫**（`CheckGuards`，`BossAggroSceneBinder.cs:153`）：对白 / 演出 / 别的战斗 / 世界暂停 / 剧情忙或剧情不可用——挡住时待机不警觉、警觉暂停计时、冲刺原地停住。
**边界**：起冲只看「离原位的水平距离 ≤ 发难半径」+ 反应时间，**不看红区档位**；BOSS 没有视线（纸片角色朝向相机），只比半径；位置不进确定性内核、不进存档（读档后 BOSS 在原位）。

## 埋点（模块名 `battle`，`BattleInstaller.cs:40`）

流程：`stage_received` / `stage_duplicate` / `stage_stale` / `stage_ignored_busy` / `stage_live_encounter` / `stage_failed` / `boss_not_found` / `presenter_missing` /
`battle_started` / `battle_finished` / `battle_cancelled` / `battle_failed` / `result_rejected` / `write_back_outlived_curtain` / `settings_overridden` /
`command_rejected` / `item_consume_failed` / `battle_entry` / `battle_entry_snapshot` / `battle_entry_provider_failed` / `escalation_refused`（均在 `BattleFlow.cs`）；
场景：`arena_loaded` / `arena_load_failed` / `restore_failed` / `presenter_close_failed`（`BattleArena.cs`）；表现：`presenter_opened` / `presenter_closed` / `command_chosen` / `view_close_failed`（`BattleScenePresenter.cs`）；
接线：`boss_entry_context_used` / `boss_entry_context_missing`（`BattleInstaller.cs`）、`boss_entry_context_unbound`（`BossEntryContext.cs`）；
发难：`boss_aggro_bound` / `boss_aggro_state`（只在状态迁移时记）/ `boss_aggro_blocked`（同一守卫持续挡着只记一条）/ `boss_aggro_contact_failed`（`BossAggro.cs`）。
剧情侧另有 `battle_begun` / `battle_released` / `battle_stage_entered`（`NarrativeService.cs:238,251,365`）。
**遭遇侧**（Monster 模块作用域）另有 `escalation_requested` / `escalation_completed` / `escalation_aborted` / `escalation_completion_ignored` / `escalation_abandoned`，见 monster guide。

## 已知约束

- **一次只跑一场**：`BattleArena.EnterAsync` 在场景已在时抛（`BattleArena.cs:53`）；`BattleFlow` 一次一个 `Run`（`BattleFlow.cs:60`）。
- **表现层连续 100 次给被拒指令即放弃这一场**（`BattleFlow.cs:72` 的上限、`:610-611` 的判定），防同步死循环。
- **表现层坏了也不卡剧情**：没有 `IBattlePresenter` 时开战报错放弃、不登记在途，剧情停在 Battle 阶段，可再交互重试（`BattleFlow.cs`、`NarrativeService.cs:161-170`）。
- **未接的路径**：**战斗外醉酒值**没有真实出处（`BossDefinition.OutOfBattleDrunk` 占位）；
  **潜行 / 遭遇的结果回写已接**（2026-10-09，空 payload 的活遭遇档走 `Game.Monster.EncounterBattleBridge`，见下一条）——详见 turnbased guide「接线清单」。
- **三种进入方式（2026-10-08 已全部接通，但有一条推断）**：`BattleSetup.TryCreate(boss, context, ...)`
  按 `EncounterStep` 给的局势快照拼请求，07 的偷袭（`SneakAttack`，BOSS −20% 生命）与被打（`Ambushed`，BOSS 先手）
  第一次有了生产入口。**缺省仍是「正面攻击、玩家先手」**（`context == null`，独立原型场景与纯 Battle 测试作用域走这一支，
  有两条负对照钉住）。
  - ⚠️ **「谁先动手」在遭遇侧仍是推断不是原文**：那条路的开战时机是「剧情停在 Battle 阶段 → 战斗侧订阅通知开打」，
    **没有「玩家物理挥刀」这一路**（`EncounterStep.StartBattle` 现在唯一的调用方是 2026-10-09 接上的活遭遇桥
    `Monster.EncounterBattleBridge`，它登记的是「这一场遭遇算一仗」，不是「玩家挥了刀」）。
    现行口径是「怪物已敌对 + 玩家没潜行 → 判作 BOSS 先动手」，于是玩家在敌对态下主动上前开战也会被判成「被打」。
    依据与代价见 `EncounterStep.DeriveInitiator` 的注释。
    <br>**BOSS 战这一侧 2026-10-10 起不再靠推断**：`BossEntryContext` 自己回答局势，`BossAggro` 冲上来接触时置「BOSS 先动手」——
    另外那一路（遭遇升级）由 `EncounterEscalationTrigger` 直接写明触发情形，也不推断。
  - ⚠️ **偷袭那一支现状几乎到不了**：判据是持久事实 `stealth.assassinated`，而写它的是背后处决，
    处决成功会让目标当场死亡 → BOSS 死了就不会再进战斗阶段。等 `00` §8.1 #5（暗杀交互形式）拍板才有内容可用。
  - ⚠️ **它不解决剧情身份**：回写仍只用事件里的 generation / activationId / targetId，
    **`EncounterStep.EncounterId` 不是那一套 id**（见 battle guide 禁止事项与 `IBattleEntryContext` 文件头）。
  - 埋点：每场开打多一条 `battle/battle_entry`（`kind` = SneakAttack / FrontalAttack / Ambushed、
    `context` = 是否拿到局势快照、**`source` = 谁报的局势**：`boss` = 这只 BOSS 自己 / `encounter` = 巡逻遭遇 /
    `none` = 硬编码缺省）——`context=false` 表示这不是现场判定出来的开战方式。
    真采纳了快照时另有一条 `battle_entry_snapshot`（`sneaking` / `sneak_attack` / `zones` / `aware`），
    用来分清「没交快照」与「交了但判成缺省」。
- **局势快照谁说了算（2026-10-10，③ 方案 2「给 boss 自己的局势」）**：
  - **优先 BOSS 自己**：场景里给 BOSS NPC 挂 `Game.Battle.BossEntryContext`（与 `NarrativeTrigger` 同物体），
    它按 `MonsterRules.SenseFrom` **与巡逻怪同一套档位**回答「这只 BOSS 察觉到玩家没有、玩家进没进它的红 / 橙区」。
    读方 `BattleSetup.IsEncounterEngaged` 认新加的 `IBattleEntryContext.MonsterAware`。
  - **按需解析，不在构建期绑**：BOSS 那份挂在**玩法场景**，而 `BattleInstaller` 的构建回调跑在 **Boot 场景**——
    那一刻 `FindObjectOfType` 只能看见 Boot（2026-10-10 实测拿到 null，日志 `boss_entry_context_missing`）。
    所以走 `BattleFlow.BindEntryContextProvider(Func<string, IBattleEntryContext>)`：开仗那一刻按 payload 现找一次。
  - **没有落回巡逻遭遇**：找到就用 BOSS 那份，找不到 / 不匹配才退回 `EncounterStep`，都没有才算缺省。
  - 边界（都写在 `BossEntryContext` 文件头）：**没有视线，按距离判**（纸片 NPC 的旋转是朝向相机的，不是视线）；
    **不做遮挡查询**；**不查种类表**（用 `MonsterConfig` 全局值）；**组件本身只回答局势、不产生副作用**——
    它的 `Initiator` 默认「玩家先动手」，只有同物体的 `BossAggro` 冲上来接触时才会置成「BOSS 先动手」（2026-10-15 起「被打」这条真的会发生了，见本文「BOSS 主动发难」）；
    偷袭那一支仍要等 `00` §8.1 #5。
- **BOSS NPC 的交互转交**依赖 `NarrativeTrigger` 与 `DialogueInteractable` 同物体（`NarrativeTrigger.cs:52-60`）；根作用域销毁后旧绑定的交互被 `IsReady` 守卫挡掉（`NarrativeTrigger.cs:72`）。

## 禁止事项

- 不要在本模块里改规则数值或判定——走 `TurnBasedConfig` / `Game.TurnBased`；也不要让 `Game.TurnBased` 反过来认识 Battle。
- **不要把「别处的局势」当成这一仗的理由**（2026-10-10 定）：开战判定只认**跟谁打**那一侧的局势。
  BOSS 战用 `BossEntryContext`（这只 BOSS 自己），巡逻遭遇用 `EncounterStep`；拿巡逻怪的状态或
  上一场遭遇的 `stealth.assassinated` 去决定 BOSS 战怎么进，就是把两个不同的对手混成一个。
- 不要自己拼 `NarrativeIntent` 回写，或自创结果键；只走 `IBattleNarrative.CompleteBattleAsync` + `BattleExitKeys`。身份三项用事件里带的值，不是 `EncounterStep` 的 `EncounterId`。
- 不要给战斗相机打 `MainCamera` 标签；不要切 `ActiveScene`。
- 不要在战斗表现里用受时间缩放的计时。
- 不要在玩法代码里调 `BattleSetup.OverrideSettings`。
- 不要直接改 `LootSaveData` 扣道具；走 `LootService.TryConsume`。
