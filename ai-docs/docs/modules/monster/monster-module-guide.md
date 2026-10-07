---
type: module-guide
module: monster
layer: runtime
maturity: stable
---

# Monster 模块指南

## 职责与边界

Monster 在遭遇场景中沿巡逻点移动，感知 Player，累积或消退警戒值，追击、攻击、受伤和死亡。
首版是开放地形原型：没有障碍物视线遮挡、寻路、背后处决或正式美术。
需求来源、暂定参数、已确认取舍见 `PRP/monster-ai/`。

| 层 | 类型 | 职责 |
| --- | --- | --- |
| 配置（全局） | `MonsterConfig` | **全局**默认值：巡逻与转态计时、速度倍率、敌对半径，兼按种类数值的兜底 |
| 配置（按种类） | `MonsterKind` | 一种怪的**数值**只读视图（生命 / 伤害 / 攻击距离与冷却 / 视野角度 / 橙区半径 / 背后近距半径 / 警戒升满与丢失目标时长），跨表的三列转问妖物表 |
| 配置（按种类） | `MonsterKindCatalog` | `monster_species` 表的只读查询：按种类 id 建索引、读表那一次校验全部数值与 `yao_id` 引用 |
| 状态名 | `MonsterMode` | 巡逻走、巡逻停、警戒、敌对、死亡 |
| 固定输入 | `MonsterIntent` | 本 tick 玩家快照及固定步长 |
| 运行数据 | `MonsterModel` | 位置、上一 tick 位置（渲染插值用，不进快照）、朝向、生命、警戒值与计时器 |
| 规则 | `MonsterRules` | 纯 C# 巡逻、感知、转态、战斗与快照 |
| 同 tick 调度 | `EncounterStep` | 先 Player 后 Monster，处理双方命中 |
| 场景状态 | `MonsterEncounterState` | 加载和退出遭遇场景 |
| 场景表现 | `EncounterSceneView` | 巡逻点引用、占位图与状态界面、两 tick 间渲染插值、XZ 贴地投影、玩家白盒遮挡碰撞、状态色染色、按移动方向翻转纸片 |
| 白盒碰撞 | `EncounterCollision` | 静态：玩家场景位移先 X 后 Z 胶囊扫掠（贴墙滑动），表现层用，不进确定性内核 |
| 独立场景入口 | `StandaloneEncounterController` | 直接播放原型场景时读取输入并推进同一套遭遇规则 |
| 根注册 | `MonsterInstaller` | 玩法逻辑步骤和回放状态接线；按种类数值在此接入（种类 Id / 表读不到时的全局兜底） |

## 按种类配数值（2026-10-07，聚光灯「怪物种类数据化」）

真源 `docs/design/features-spotlight/06_怪物分层.md`。字段归属按该文归纳，**不是拍脑袋分的**：

| 归属 | 字段 | 依据 |
| --- | --- | --- |
| **按种类**（表 `monster_species`） | 生命、每次伤害、攻击距离、攻击冷却、视野角度、橙区半径、背后近距半径、警戒升满时长、丢失目标时长 | :121 R9（同层怪的「可否击杀 / 怎么杀」各不相同）、:117 R5（C 层分 BOSS 与关键 NPC / 执法者两类）、:184「较强」、:186「战斗难度极高」、:113 R1（A / B / C 三层本身就是按层配数值的口径） |
| **全局**（`MonsterConfig.asset`） | 巡逻速度、警戒与敌对速度倍率、敌对（红区）半径、警戒衰减时长、停步时长、随机停步区间 | :119 R7「『驻地』『驻守』『守卫』『守门』是不是同一类行为，原文没区分」——原文没给这些行为分级，工程先按一套节奏跑 |

- 数据在 **Luban 表**（`Tables/Defines/monster_species.xml`、`Tables/Data/monster_species/<id>.json`），不是 SO 资产：
  数值按种类配，同一种怪在每个场景里必须一致；SO 没有主键、跨场景无法保证同一种怪只配一份，策划也改不了。
- **不重复妖物表的列**：`tier` / `killable` / `defeat_method` / `drop_items` 只在 `Tables/Data/yao/*.json`，
  `MonsterKind` 经 `YaoCatalog` 按 `yao_id` 转问，所以同一件事只有一处能写歪。
- 取值链：`MonsterRules` 构造时把「种类优先、否则全局」解析一次（`MonsterConfig.Resolve*`），存进只读字段。
  **`kind` 为 null 就整只怪走全局默认**——旧场景、旧测试、独立原型场景都落在这一支，行为与拆分前逐位一致。
- 未知种类：`MonsterKindCatalog.Get` 抛 `KeyNotFoundException`（`TryGet` 是宽容版）；
  `MonsterInstaller` 自己用 `TryGet`，查不到就记 Error 后退回全局默认，不让整场遭遇进不去。
- `killable = false` 的种类**不吃常规伤害**（`MonsterRules.ApplyDamage` 返回 false，`06_怪物分层.md:121` R9）；
  暗杀 / 特殊条件 / 需收服各自的路由归后续波次（S3），不要复用 `ApplyDamage`。
- 掉落只交 id：`MonsterRules.DropItemIds`（= `YaoCatalog.DropItemsOf(yao_id)`），入背包走 `Game.Loot` 的
  `LootService.SettleMonsterDrop(yaoId, dropItemIds)`。规则层不写背包、不发事件（确定性内核里不放副作用）。
- 接线：`Boot` 场景 `GameBootstrap` 的 `MonsterInstaller` 上，`Kind Id` 填 `monster_species` 的主键，
  **0 = 取表里第一条**；`MonsterConfig` 资产照旧拖在原字段上，作为全局值与兜底。

Monster 与 Player 均在 `Game.Runtime` 程序集。Monster 依赖 Player 的公开快照与伤害意图，
按种类数值经 `Game.Mirror.YaoCatalog` 读妖物表（同程序集内的只读查询，不经容器也能建）。
Game.Core 不引用玩法模块；规则类不读取场景组件、不用 `Time.deltaTime` 或全局随机数。

## 状态规则

| 状态 | 进入原因 | 行为 | 离开原因 |
| --- | --- | --- | --- |
| `PatrolWalk` | 开局、停步结束、警戒清零 | 顺序巡逻并计时 | 7–10 秒后停步；感知玩家 |
| `PatrolPause` | 巡逻计时达到随机阈值 | 原地停 2 秒 | 计时结束；感知玩家 |
| `Alert` | 橙区或背后近距察觉、敌对丢失目标 | 累积/消退警戒，发现目标时接近 | 警戒满值或清零；红区出现 |
| `Hostile` | 红区、警戒满值、受击 | 追击最后已知位置并尝试攻击 | 丢失目标 2 秒；死亡 |
| `Dead` | 生命为零 | 不再更新移动或攻击 | 下次遭遇 `Reset` |

警戒与敌对是怪物内部状态，不新增 `GameFlow` 状态；GameFlow 仅管理标题和遭遇场景。
受击和攻击是规则的输入与动作结果，没有为它们再增设常驻状态。

## 感知与警戒

- 前方扇区总角度 75°，红区半径 2，橙区半径 6，比例 1:3。
- 红区先判定；伪装或潜行都不能阻止红区立即敌对。
- 伪装阻止橙区警戒，但不阻止红区与背后近距判定。
- 背后 1.5 单位内可近距察觉；潜行阻止该近距判定。
- 橙区连续暴露 4 秒令警戒升满；脱离后按平方时间衰减，满值 6 秒清零。
- 敌对丢失目标 2 秒后转为满值警戒，再按上述规则消退。
- 感知是位置和朝向的数学判定，没有 Physics 查询或遮挡物判断。
- 扇区本身不在游戏画面显示，状态颜色与警戒条可见。

`MonsterRules.Sense` 在规则类内部完成上述优先级；表现层不能额外判定一次。
攻击仅在敌对、能感知到活目标、目标未伪装、进入攻击距离且冷却结束时触发。
伪装的攻击保护由独立 `DisguiseRules` 统一判断，已敌对或受击的敌人同样不能攻击伪装玩家；感知、警戒与追击规则不变。
玩家攻击命中 Monster 时，`EncounterStep` 限制距离和前半平面，再把伤害意图交给 Monster。

## 巡逻和数值

巡逻点按场景中 `EncounterSceneView.patrolPoints` 的数组顺序读取；至少一个非空点。
单点路径会在该点附近维持巡逻计时，多点路径依序循环。
随机停步区间使用 `logic.monster.patrol` 专用确定性随机流，当前抽整数秒 7、8、9、10。
停步时长 2 秒；巡逻速度 2 单位/秒，警戒与敌对速度倍率 1.1、1.25。
全球默认值：生命 3、每次命中伤害 1、攻击距离 0.8、攻击冷却 1 秒、视野 75°、橙区 6、红区 2、背后近距 1.5；
这些里**按种类的那几个**（生命 / 伤害 / 攻击距离与冷却 / 视野角度 / 橙区半径 / 背后近距 / 警戒升满与丢失目标时长）
以种类表为准，见上一节「按种类配数值」。
玩家 `Health` 因此归零时，若 `Mirror` 模块已接线，会弹出镜碎页并调 `EncounterStep.End()` 结束本场遭遇、重进场景，而不是让双方停在原地，见 [`mirror-module-guide.md`](../mirror/mirror-module-guide.md)。
工作簿未规定攻击冷却；它是待试玩校准的原型值。
数值分两处，都不要在视图或场景脚本里复制一份：全局与兜底在 `Assets/_Project/Data/Monster/MonsterConfig.asset`，
按种类在 `Tables/Data/monster_species/<id>.json`（改完跑 `scripts/gen-tables.ps1`）。
改全局：选中 `MonsterConfig.asset` 改 Inspector，重开场景生效。
改某一种怪：改它的种类 JSON → 跑生成脚本 → 重开场景；只想让某只怪用另一种数值时改 `MonsterInstaller` 的 `Kind Id`。
`MoveControlled` 供独立驯服原型驱动敌人位置，使用 `PatrolSpeed`；驯服不接入当前遭遇或回放注册。

## 回放状态

| 数据 | 所属 | 快照 |
| --- | --- | --- |
| 位置、朝向、最后已知目标 | `MonsterModel` | 是 |
| 状态、生命、巡逻点索引 | `MonsterModel` | 是 |
| 警戒值与升降、失目标计时 | `MonsterModel` | 是 |
| 攻击冷却、停步剩余与下次停步时间 | `MonsterModel` | 是 |
| 上一 tick 位置 `PreviousPosition` | `MonsterModel` / `PlayerModel` | **否**（纯表现辅助，恢复快照 / 读档后对齐为 `Position`） |
| 巡逻随机流状态 | `MonsterRules` | 是 |
| 当前巡逻点数组 | `MonsterRules` | 是 |
| 遭遇是否激活 | `EncounterStep` | 是 |
| 静态调参 | `MonsterConfig` | 否 |

`MonsterInstaller` 注册回放状态的顺序为 `PlayerModel` → `MonsterRules` → `EncounterStep`。
它同时把 `EncounterStep` 加入 `SimulationRunner`。顺序影响快照字节布局，不可随意变更。
本次新注册已将回放文件格式版本提升为 2，最低可读版本也为 2。
恢复快照后巡逻点和随机流随状态一起恢复；配置资产仍由场景外的模块注册负责。

## 场景与生命周期

Boot `GameBootstrap` 已挂 `PlayerInstaller` 和 `MonsterInstaller`，并已移除 `SampleInstaller` 的入口接线。
等距原型场景应以地址 `IsometricEncounter` 加入 Addressables `Scenes` 组。
场景中的 `EncounterSceneView` 显式引用玩家出生点、巡逻点、`player`、`enerme` 及其纸片；
逻辑 XY 由该视图投影到场景 XZ。缺少显式接线时状态会报错并返回标题，不再运行时按对象名补建。
直接播放该场景时，`StandaloneEncounterController` 使用场景内 `PlayerInput` 推进同一个 `EncounterStep`；
若检测到 Boot 的 `GameBootstrap`，该控制器立即停用，避免与正式 `SimulationRunner` 重复推进。
**感知范围可视化（2026-10-07）**：`MonsterAwarenessRanges`（`Assets/_Project/Scripts/Runtime/Monster/`）是新建的
白盒表现组件，画出前方视野扇形（橙区半径）、红区扇形与背后贴身察觉圈——`03_潜行与暗杀.md:234` 把
「**视野扇形不画在画面上**」列为已知缺口，它补的就是这一条；同文 `:251` 那条待拍板（「潜行速度下绕过去要几秒？
要和巡逻停步的 2 秒对一下」）也因为这个可视化才第一次变得可判断。数值与 `MonsterRules.Sense` **同源**：
视野角 / 橙区 / 贴身察觉取 `MonsterKind`（按种类），红区取 `MonsterConfig.HostileRadius`（全局），
由 `EncounterSceneView.BindAwarenessRanges(kind, config)` 在 `Begin`（即 `Reset` 查表）之后接一次——
`MonsterConfig` 由 `MonsterEncounterState` 从容器注入，**不靠 Inspector 手拖**（漏拖会静默少一层可视化）。
顶点在局部坐标只建一次、每帧只写 transform；`LineAlignment` 用默认的 `View`（贴地环用 `TransformZ`
在斜俯视下会「平躺」看不见）。把 `EncounterSceneView.showAwarenessRanges` 取消勾选即完全不画，判定零影响。

**巡逻线（2026-10-07 调整）**：`SampleScene` 的 `PatrolPoint0/1` 由 **4 米**（x 13.86..17.86）拉到 **8 米**
（x 13.86..21.86）。原线在 `patrolSpeed 2` 下**每 2 秒就折返一次**（每次转身都让 75° 视野锥扫过），
拉长后是 **4 秒**；曾试过 14 米但**实测不可行**——玩家潜行 1.5 m/s 追不上巡逻 2 m/s 的怪，线越长怪越长时间
在走，绕背在数学上办不到。改这两个点**必须按世界坐标设**：`EncounterSceneView.PatrolPositions()` 读的是
`Transform.position`（世界）再转逻辑 XY，父物体一旦带旋转，`localPosition` 与它就不等价。
回放的观察点也随之从 `(15.86, 5.4)` 挪到 `(15.5, 7.6)`：安全站位要满足「被视野锥排除（|dx| < 1.304·D，
1.304 = 1/tan37.5°）**或**距离超过橙区（|dx|² + D² > 36）」，两者覆盖全部 |dx| 解出 **D ≥ 3.65 米**。

`MonsterEncounterState` 在场景就绪后 `Begin`，绑定视图；离场时 `End`、解绑。**背后处决（2026-10-07 接线波）**：
同一个 `OnSceneReadyAsync` 在场景根里找 `ExecutionInteractor`（与找 `EncounterSceneView` 同一模式），
再由公开的 `BindExecution` 把它 Configure 上——玩家 / `MonsterRules` / `step.FactSink`（写
`stealth.assassinated` 的那一侧契约）/ 可选的 `StealthKernel.Assassination` / `EncounterStep` /
`stealth` 作用域埋点 / 从 `IInputService` 拿的动作资产。场景侧已在 `SampleScene` 的 `Encounter` 物体上挂好
该组件（`inputActions` 字段**留空即可**：接线方传进来的动作资产优先，Inspector 那个字段只在没有接线方时兜底）；
直接播放该场景那条路由 `StandaloneEncounterController.Awake` 接，但它**没有种类表**，物种门槛恒拒。
组件缺位时只打一条 Warn（遭遇照跑）——「按 F 没反应」与「功能坏了」在现场要分得开。
触屏虚拟摇杆与
潜行 / 伪装 / 攻击按钮已不再由本状态创建（原 `EncounterTouchControls` 已删除），改由
`Game.IsometricExploration` 的探索 HUD 预制体（`OnScreenStick` / `OnScreenButton`）按
`IPlatformService.IsTouchPrimary` 显隐提供，映射到同一 Gameplay 动作；见
`ai-docs/docs/modules/isometricexploration/isometricexploration-module-guide.md` 的「探索控件与万向标」。
占位表现以玩家蓝/青/绿和怪物灰/橙/红/黑区分状态（状态色优先染 `playerStateIndicator` /
`monsterStateIndicator` 指示环，当前场景接线为脚下 `SelectRing`；为空才回退染本体纸片），并显示
生命与警戒条。
标题「开始」由存档会话路由（`Game.Session`）接管。

### 两逻辑 tick 之间的渲染插值

逻辑位置只在 60 Hz 固定 tick 里跳变（`SimulationRunner` 累加器推进），而相机 `SmoothCameraFollow` 每个渲染帧都在追；
视图若直接抄 `Position`，渲染帧率 ≠ 60 时角色会一帧动一帧不动（拖影 / 抖动）。因此 `EncounterSceneView.LateUpdate`
（`EncounterSceneView.cs:220`）先算本帧逻辑位置 `Lerp(PreviousPosition, Position, alpha)`，再走下面的贴地 / 障碍滑动流程。

- **上一 tick 位置**：`PlayerModel.PreviousPosition`（`PlayerModel.cs:16`）/ `MonsterModel.PreviousPosition`（`MonsterModel.cs:15`），
  由 `PlayerRules.Step` / `MonsterRules.Step` / `MonsterRules.MoveControlled` 在推进位置**之前**（且在死亡等提前返回之前）写入；
  `EncounterStep.Step` 未激活或结果待结算、双方都不推进时也对齐。**不进存档、不进回放快照**；
  `PlayerRules.Reset`、`MonsterRules.Reset`、两者的 `Restore`（读档）与 `Deserialize`（快照恢复）都把它对齐为 `Position`，不跨瞬移插值。
- **alpha 从哪来**：`Bind(player, monster, Func<float> alphaSource = null)`（`EncounterSceneView.cs`；此签名在 Q1 接线后行号已漂，故不写行号）。
  正式流程 `MonsterEncounterState.ReadInterpolationAlpha`（`MonsterEncounterState.cs:178`）取
  `EncounterProjection.InterpolationAlpha(runner.Accumulator, runner.Clock.FixedDeltaTime)`；`SimulationRunner` 处于 `Driven`
  （重放播放器逐 tick 推进、余量恒 0）时返回 1，直接显示当前 tick。独立场景 `StandaloneEncounterController` 用
  FixedUpdate 相位（`Time.time − Time.fixedTime`）/ `Time.fixedDeltaTime`，`ManualSimulation` 时返回 1。
  不传 `alphaSource` 按 1，行为与接入插值前一致——目前只有 Taming（未接正式流程，独立绑定视图）与验框架机制的
  自检回放（`ScenePath` 返回 `DemoScenePath`、不经 `MonsterEncounterState`）落在这一支；Player / Monster /
  IsometricExploration / Disguise / CharacterPuppet 五份 Showcase 已改走 `EnterWorldFromTitle` 进 Boot 真实流程，
  实际绑定走的是 `MonsterEncounterState.Bind`，与正式游玩共用同一条 `ReadInterpolationAlpha`。
- **时停 / 暂停**：`timeScale = 0` 或 `SimulationRunner.IsPaused` 时余量不变、alpha 恒定，画面静止不抖。
- **传送保护**：`|Position − PreviousPosition|` 超过 `obstacleTeleportDistance`（1.5）不插值，直接取 `Position`（漏同步时的兜底）。
- 纯函数在 `EncounterProjection`：`InterpolationAlpha`（`EncounterProjection.cs:37`）、`InterpolatePosition`（`:58`），
  碰撞回写分轴裁决 `ResolveBlockedAxis`（`:85`）、`CorrectPreviousAxis`（`:95`）。

`EncounterSceneView.ToScenePosition` 在 XZ 模式下额外做贴地投影：
`groundMask` 非 0 时，从 `当前 Y + groundProbeHeight` 向下 Raycast（`QueryTriggerInteraction.Ignore`），
最大探测距离 `groundProbeHeight + groundProbeDepth`；命中后交给
`public static float ResolveGroundY(currentY, groundY, maxStepHeight)`（`EncounterProjection.cs:13`）
裁决：`groundY - currentY <= maxStepHeight` 才采用新高度，否则保留当前高度（视为墙顶/家具）；
下落方向不受该上限约束。`groundMask` 为 0 时完全不贴地，行为与旧版一致。
同文件的 `ResolveFlipX(previousX, currentX, currentFlipX, threshold)`（`EncounterProjection.cs:17`）
是纯翻转规则，供 `flipByMoveDirection` 复用。

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `groundMask` | 空（不贴地） | 贴地射线只打这一层；场景把 `Environment_Graybox` 下物体统一放在 `Ground`（layer 8） |
| `groundProbeHeight` | 2 | 射线起点相对当前 Y 的上偏移 |
| `groundProbeDepth` | 4 | 射线在起点之下的最大探测距离 |
| `maxStepHeight` | 0.32 | 单帧允许的最大抬升；楼梯每级 0.3 可上，长椅 0.45 / 路障 0.35 会被拒绝，墙顶不会被“跳”上去；下落不限 |
| `playerStateIndicator` / `monsterStateIndicator` | 空 | 状态色优先染色目标；为空回退染本体 SpriteRenderer |
| `flipByMoveDirection` | false | 按本帧场景 X 位移翻转纸片 `flipX`（逻辑坐标系不受影响）；只在 XZ 等距场景勾选，SampleScene 已勾；旧 2D 验证场景已删，回放现在也在 SampleScene 上跑 |

### 白盒遮挡碰撞（PRP/exploration-whitebox 波 9）

`EncounterSceneView.LateUpdate` 的玩家投影改走 `ResolvePlayerScenePosition`：先按本帧（插值后）逻辑位置贴地得到 `desired`；
`obstacleMask` 非 0、XZ 模式、且本帧场景位移不超过 `obstacleTeleportDistance` 时，调
`EncounterCollision.Slide(上一帧场景位置, desired, obstacleRadius, obstacleBottomOffset, obstacleTopOffset, obstacleMask)`：
沿 X、再沿 Z 各做一次 `Physics.CapsuleCast`（`QueryTriggerInteraction.Ignore`），撞到就停在 `hit.distance − 0.02`。
胶囊竖直覆盖「脚底 + bottomOffset」到「脚底 + topOffset」（端点球心各往里收一个半径），所以 0.3 的台阶 / 坡面不算障碍，
离地 1.5 m 以上的桥底 / 甲板底不挡人。XZ 有修正时对修正后的 XZ 重新贴地，写回纸片，并发
`event Action<Vector2> OnPlayerBlocked`（参数 = 修正后的逻辑 XY，**分轴合成**：扫掠后偏离插值点超过 0.0001 的轴取修正值，
其余轴原样是当前逻辑值）。怪物不解算。

分轴的原因：插值点落后逻辑位置最多一个 tick，整点回写会把沿墙那一轴也拉回插值点，贴墙滑动每 tick 丢一截速度
（首帧 alpha 越小丢得越多，高刷屏上接近停住）。`EncounterStep.CorrectPlayerPosition` 对被改写的轴把
`PreviousPosition` 设成同一值（下一帧不从墙里倒插），没改写的轴保留 `PreviousPosition`，沿墙方向继续平滑插值。

回写：`MonsterEncounterState.OnSceneReadyAsync`（`view.Bind` 之后）与 `StandaloneEncounterController.Awake` 都订阅
`view.OnPlayerBlocked += step.CorrectPlayerPosition`，离场 / 销毁时退订。`EncounterStep.CorrectPlayerPosition(Vector2)`
只在 `IsActive` 时写 `player.Model.Position`，只给这条回写用（挪人仍用 `PlayerRules.Reset`）；
EditMode `EncounterStepTests` 的 `CorrectPlayerPosition_WhenActive_OverridesPlayerPosition` / `_WhenInactive_IsIgnored` /
`_AlignsPreviousOnlyOnCorrectedAxis` 覆盖。

**取舍**：逻辑层只有 XY、没有障碍数据，碰撞在表现层用 Unity 物理解算再回写——同机同场景可复现，
跨机 / 跨平台回放不保证逐位一致（PhysX 浮点）。正式版要把关卡障碍放进确定性内核，届时删掉这条回写。

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `obstacleMask` | 空（不碰撞，旧场景行为不变） | 挡人的层；SampleScene 勾 `Ground` + `Obstacle`（1280）：灰盒在 `Ground`（8），挡人的道具（物资箱 `Crate_A/B/C` 的根节点，BoxCollider 0.8×0.6×0.8）在 `Obstacle`（10）——语义是「挡人、但不是地面也不是遮挡物的道具」。道具不放 `Ground`（会被 `OccluderFadePresenter` 当遮挡物淡出换材质、被贴地射线打到顶面把人抬上去），也不勾 `Default`（玩家、巡逻怪、NPC 都在 Default）。**NPC 不挡人**（用户 2026-09-30 定）：可对话角色根节点留在 Default，玩家可以穿过；两张纸片前后分明靠玩家材质的深度偏移（`M_SpriteDepthClip_Player`，见 `isometricexploration-module-guide.md`「表现层」），不靠碰撞隔开；旧验证场景已删 |
| `obstacleBottomOffset` | 0.35 | 胶囊下沿离脚底高度；须高于单级台阶 |
| `obstacleTopOffset` | 1.5 | 胶囊上沿离脚底高度；更高的悬空几何不挡人 |
| `obstacleRadius` | 0.3 | 胶囊半径，与 player 的 CapsuleCollider 一致 |
| `obstacleTeleportDistance` | 1.5 | 单帧场景位移超过它视为瞬移（读档 / 重置 / 回放挪位），不解算只贴地；两 tick 逻辑位置相距超过它时也不插值 |

调试块（`OnGUI`，返回标题按钮 + 玩家 / 怪物状态两行 + 警戒条）波 12 起挪到**左下角**、左对齐、像素坐标
（不随画布缩放）：按钮 `Rect(16, Screen.height − 56, 114, 40)`，两行状态文字在按钮上方
（`y = Screen.height − 56 − 28 − 28` 与 `− 56 − 28`，宽 480），警戒条再上方
（`y = Screen.height − 56 − 28 − 28 − 24`）。挪到左下角是为了把右上角一列让给
`Game.IsometricExploration` 的沉浸 / 重置按钮（见 `isometricexploration-module-guide.md` 的
「探索 HUD 与沉浸模式」）。`Time.timeScale <= 0f`（对白 / 面板暂停期间）整块不画，避免压在对话框
或暂停面板上；不再使用右对齐 `GUIStyle`，`rightAlignedLabel` 字段已删除。玩家状态行已按 Mirror 的 V8 验收去掉生命数字，只显示潜行 / 伪装
（`Assets/_Project/Scripts/Runtime/Monster/EncounterSceneView.cs:257-258`）；怪物状态行仍保留生命数字（调试用）。

`PlayerScenePosition`（`EncounterSceneView.cs:84`）暴露玩家纸片贴地后的场景坐标（插值后的渲染位置，
正式流程下最多落后逻辑位置一个 tick），供 Showcase 与跨模块只读取用，不需要碰视图私有字段。

## Showcase 回放（2026-09-28 重写，走 Boot 真实流程）

`Assets/_Project/Scripts/Tests/Showcase/Monster/MonsterShowcase.cs` 四条用例，全部标题「开始」进 SampleScene
（`EnterDemoWorld`），虚拟手柄推摇杆、虚拟键盘按 Gameplay 动作驱动真实玩家 `player`，巡逻怪 `enerme` 由容器里的
`MonsterRules` 自己跑逻辑 tick（不再自己 `new` 规则、不再手动喂 `MonsterIntent`、不再瞬移玩家）：

| 用例 | 演什么 |
| --- | --- |
| `Patrol_WalksAlongPointsAndTurnsBack` | 巡逻沿点位走动、到端点折返 |
| `Sense_SneakBehindStaysCalm_FrontRedZoneTurnsHostile` | 背后潜行不警戒、正面红区照样敌对 |
| `Hostile_ChasesFleeingPlayerAndHitsIt` | 敌对后追击逃跑的玩家并出手 |
| `Attacked_TakesHitsUntilDead` | 挨打直到生命归零倒地 |

## 已知集成状态

脚本、输入映射、配置资产、Boot、遭遇场景和 Addressables 均已接线。
2026-09-20 验证结果：Unity 编译无错误，相关工程 EditMode 全量 181/181 通过，资产体检四项全过；视觉表现仍需开发者确认。

**已知限制**：贴地是表现层行为——`PlayerModel`/`MonsterModel` 的逻辑坐标只有 XY，没有高度、
不做视线遮挡；玩家的障碍判定只有上文的表现层白盒回写，怪物仍穿墙；`Reset` 或任意跨点瞬移只改变逻辑 XY，视图在下一帧仍按
`maxStepHeight` 裁决贴地高度，不会把角色“抬升”到远高于当前值的高台，只有逐帧连续行走、
每步抬升不超过阈值才会一路爬升（对应 Showcase 用例 `WalkOntoStairs_RaisesBody`）。

## 修改时检查

- 改感知优先级：保留红区先于伪装与潜行，并在 EditMode 用例加反例。
- 改状态：同步 `MonsterMode`、模型快照、视图反馈和回放格式版本。
- 改随机巡逻：继续使用命名逻辑流，把影响未来抽样的状态放进快照。
- 改路径：维持场景按序配置，`Reset` 必须收到非空巡逻点。
- 改攻击：仅让规则返回攻击动作，由遭遇步骤给玩家施加伤害。
- 改触屏：触屏控件已迁到探索 HUD（`Game.IsometricExploration`），先改 Gameplay 输入绑定，
  控件只模拟同一游戏手柄路径；本模块不再挂触屏组件。
- 改贴地/状态色/翻转字段：跑 `EncounterSceneViewTests.cs` 里 `EncounterProjection` 的 `ResolveGroundY` /
  `ResolveFlipX` 用例与 IsometricExploration Showcase 的 `WalkOntoStairs_RaisesBody`，确认逻辑坐标（XY）
  没有被贴地投影反向影响。
- 改渲染插值或新增「整体改写 `Position`」的入口（传送、剧情挪人等）：新入口必须同步 `PreviousPosition`
  （调模型的 `SyncPreviousPosition`，或走 `PlayerRules.Reset` / `MonsterRules.Reset`）；跑 `EncounterSceneViewTests` 的
  `InterpolationAlpha_*` / `InterpolatePosition_*` / `LateUpdate_UsesAlphaSource_*`、`PlayerRulesTests` / `MonsterRulesTests` 的
  `PreviousPosition` 用例与 `EncounterStepTests`，并在 60 Hz 以外的刷新率下目测跑动与贴墙滑动。
- 改遮挡碰撞字段或 `EncounterCollision`：跑 `EncounterStepTests` 与 Exploration Showcase 的
  `Collision_FenceBlocksPlayer` / `MultiLevel_RampLeadsToDeck`，并跑 IsometricExploration Showcase 确认潜行走廊（z 3.4）没被挡。
- 往 SampleScene 加挡人道具（物资箱等）：① 根节点放 `Obstacle` 层（第 10 层，不放 `Ground`）；② 根上留启用的非 trigger 碰撞体，
  竖直范围要够得着扫掠胶囊——根节点贴地时盒顶须高过 `obstacleBottomOffset`（0.35），箱子 0.6 高（center.y 0.3）即可；
  ③ 子物体（外观、头顶标记）不挂碰撞体，层保持 Default；④ 跑 EditMode `SampleSceneObstacleWiringTests`，它检查凡挂 `SupplyCrate`
  的物体都有这样的碰撞体、层在 `obstacleMask` 里且不在 `Ground`；新道具不是箱子时照同样的接法，并把它加进该测试的检查范围；
  ⑤ 回放的走位要绕开它（`WalkTo` 是真走，会被挡），开箱站位离箱子中心 ≥ 半宽 + 胶囊半径 0.3。
- NPC 不挡人：可对话角色（`DialogueInteractable`）的碰撞体留在 Default 层、不进 `obstacleMask`（同一测试有反向用例守着）；
  玩家与 NPC 同排重叠时的前后关系靠纸片深度偏移，改角色材质前读 `isometricexploration-module-guide.md`「表现层」。
- 完成场景接线后：跑 Monster Showcase、资产体检、lint、文档检查并让开发者看画面。
