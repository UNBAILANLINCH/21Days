---
type: module-guide
module: stealth
layer: runtime
maturity: seed
---

# Stealth 模块指南

S3「潜行与暗杀」与 S4「追逐」共用的**纯规则 / 纯数学内核**：视线遮挡、绕背暗杀、击倒状态机、
追逐触发与摆脱、召唤编队、脚本化固定追逐。**只吃快照结构**（位置、朝向、几个布尔），
不认识 `MonsterModel` / `PlayerModel`，因此可进确定性内核、可用纯逻辑测。

## 职责边界

| 类型 | 职责 |
| --- | --- |
| `StealthOccluder` | 遮挡体的**纯数据形状**（轴对齐矩形 / 圆）+ 工厂与 `Id`。不认识场景、碰撞体、层 |
| `StealthGeometry` | 线段 vs 矩形（板层裁剪）、线段 vs 圆、点到线段距离。纯 `GameMath`，**无物理、无 `Time`、无随机** |
| `StealthSight` | 拿一组遮挡体回答「两点通不通视」；含零分配 `TrySightNonAlloc` |
| `SightSettings` / `SightResult` | 遮挡的两个策略旋钮（`WritesCoverFact` / `CoverImpliesHidden`）/ 查询结果（不把内部数组交出去） |
| `AssassinationRules` | 绕背 + 距离 + **目标未察觉** + 可选潜行的四段判定；`IsBehind` 可单独给附身复用 |
| `ExecutionRules` | 背后处决的**门槛**：位置与察觉（转 `AssassinationRules`）∧ 物种允许被处决（`defeat_method == 暗杀`）。纯逻辑，两个条件分开可调 |
| `ExecutionResolver` | 判定通过之后的**结算三件事**：写 `stealth.assassinated`（只在执行成功之后）→ 让目标死亡（`MonsterRules.ApplyExecution`）→ 承载 `BattleResult(Victory)`；含三个埋点与表现钩子 |
| `ExecutionInteractor` | 交互层的 MonoBehaviour：读 `Gameplay/Execute`、选最近的可处决目标、采快照交给上面两层。**本模块唯一依赖 Monster / Player / Narrative 的文件**（见「依赖方向」） |
| `KnockdownRules` | 击倒状态机（倒地 → 挣扎 → 起身）+ `DownedHitPolicy` 三口径开关 |
| `ChaseRules` | 触发 / 摆脱 / 推进三判定 + `ChaseBaseline` 速度校验（**追兵必须快过步行、不快过奔跑**） |
| `ChaseCaughtRules` | 「被抓」后果的五口径策略（纯函数，只答「该发生什么」） |
| `SummonRules` | 追逐编队数据形状（成员、召唤半径、集群规模、巡逻段）+ 召唤判定 + 落点布局 + 编队静态校验 |
| `FixedChasePlanner` | 脚本化「固定追逐」路线的读法（当前段 / 推进 / 总长 / 终点判定） |
| `StealthFactKeys` | **字典键常量的唯一出处** + 瞬时态内存快照 `StealthFactSnapshot` + 档位换算 |
| `StealthDecisionGate` | 把四件事的结果合成对剧情暴露的一组事实键（本模块**拥有键的唯一出口**） |
| `StealthConfig` / `StealthConfigValidation` | ScriptableObject 配置（28 字段，逐个写「出处 / 待拍板编号」）/ 它的**纯函数**校验（`Validate()` 只转发） |
| `StealthKernel` | 装配入口（容器注册一个单例即可），含 `Validate()` 自检与 `ResetForNewLevel()` |

## 为什么不复用现有 Monster 能力

1. **复用不成立**：Monster 现有遮挡在**表现层**（`EncounterSceneView.obstacleMask` 走 `Physics.CapsuleCast` 挡人、
   `EncounterCollision` 做扫掠），是「**挡人**」不是「挡视线」，且进不了确定性内核；`MonsterRules.Sense` 是
   「距离 + 夹角」的纯数学，没有遮挡概念；工程里没有几何求交类型；Player 侧连「受击状态」都没有。
2. **扩展不成立**：`MonsterConfig` 是**全体共用一份**的 SO，而遮挡体列表是**场景几何**——塞进 Monster
   会让每只怪各带一份场景数据；击倒与追逐后果是**原文未定的策略**，硬塞进主流程等于替策划拍板。
3. 因此新建独立模块，耦合方式是「注入快照」：`AssassinationInput` / `StealthGateInput` 收的是
   位置、朝向、几个布尔。

## 依赖方向

只依赖 `Game.Core`（`GameMath`、遥测）。不依赖 Monster / Player / Identity / Narrative。
反向由那些模块调本模块的公开接口。

**例外（2026-10-07 背后处决波，PRP/stealth-execution §2.3 指定）**：`ExecutionInteractor.cs` /
`ExecutionResolver.cs` 两个文件依赖 `Game.Monster`（`MonsterRules` / `EncounterStep`）与 `Game.Narrative`
（`BattleResult`）。理由：「对谁下刀、把结果交给谁承载」这件事本身跨模块，而 PRP 明确把**处决的门槛**
（`defeat_method == 暗杀` 的语义）留在潜行侧——放去 Monster 会让怪物模块反向认识潜行语义。
约束：**内核文件（`AssassinationRules` 等）仍只依赖 `Game.Core`**，新加的跨模块依赖只许出现在上面两个文件里。
（`ExecutionResolver` 对 `EncounterStep` 的用法是只调 `SettleBattle` 承载结果，不推进剧情。）

## 按键

| 动作 | 键鼠 | 手柄 | 说明 |
| --- | --- | --- | --- |
| `Gameplay/Execute` | **`F`** | **`South`（□/X）** | 背后处决。出处：`docs/design/spotlight/06_怪物状态与交互设计文档.md:69`「玩家可以在怪物背后按F处决」 |

⚠️ **F 与手柄 South 与 `Interact` 共用**（`Interact` = `E` / `F` / `South`，既有约定，本波没动它）。
这不是绑错：两个动作各自有严格的目标门槛——`Interact` 只在焦点非空时生效
（`DialogueInteractionFocus.cs:79` / `SupplyCrateFocus.cs:82` 都要求 `Current != null`），
`Execute` 只在「背后 + 暗杀距离 + 目标未察觉 + `defeat_method = 暗杀`」四条同时成立时才杀人。
改 F 的归属属于内容侧键位决策，见 PRP §5 与交付报告的「待策划拍板」。

## 事实键

对外只经 `Fact.StoryFlag` + 点分小写键，键名见 [`story-facts.md`](../../story-facts.md) §4.2 / §4.3。

**`stealth.*` 由本模块写**：`stealth.hidden`、`stealth.behind`、`stealth.assassinated`、`stealth.knockdown`、
`stealth.knockdownCount.low|mid|high`、`stealth.cover`、`stealth.visionmask`。

⚠️ **`chase.*` 不由本模块写**：`chase.active` / `chase.escaped` / `chase.caught` / `chase.fixed` 的写入方
在字典 §4.3 登记为 **Chase / Monster**。本模块只给常量与判定结果，**不代写**（字典 §6.1「一个键一个写入方」）。

**瞬时态纪律**：`stealth.hidden` / `stealth.behind` / `stealth.knockdown` 是**瞬时态**，只活在内存快照里，
**不许进存档**——否则读档会把玩家恢复成「正被击倒」。因此 `KnockdownRules` **故意没有实现 `IReplayState`**。

## 原文矛盾的两套口径都做成了策略（没硬编一种）

| 矛盾 | 策略 | 出处 |
| --- | --- | --- |
| 挨打：击倒 vs 死亡 | `DownedHitPolicy`：`KnockdownOnly`（sp00）/ `HealthDeath`（mai）/ `KnockdownThenDeath`（折中占位）。**状态机只有一份**，口径只改「血尽死不死」与「再挨打重不重置」 | `00_功能总览.md` §5 **C5**、§8.1 #4 |
| 被抓：击倒 / 死亡 / 追逐 | `ChaseCaughtPolicy`：`Knockdown` / `Death`（变回原主）/ `HealthDeath` / `ScriptedSetpiece` / `None` + 折中 | `00` §5 **C1**、§8.1 #3 |
| 掩体做不做 | `WritesCoverFact=false` 时规则照跑、测试照测，只是**不对外写** `stealth.cover` | `00` §5 **C6**、§8.1 #6 |

## 已知约束

- **追兵速度现状是缺陷**：`04_追逐.md:165` 明写「甩掉追兵毫无难度」——工程里追击速度 2.5 **低于**玩家步行 3。
  `ChaseRules.VerifyChaseSpeed` 就是为抓这个而存在，配置里的 `chaseSpeed` 占位 3.6。
- **S4 不需要寻路**：`04_追逐.md:28`「房间内怪物除巡逻队不可流通」+ `:40`「怪物沿指定路径巡逻」——
  普通怪本来只在本房间直线追（与 `MonsterRules` 现状一致）。**真缺口是「房间边界的表达与遵守」**，归 S6。
  不要照 `roadmap.md` S4 行的「没有寻路」去接 A*。
- 数值多为**占位**，逐条挂 `00` §8.1 的编号（见 `StealthConfig` 字段注释）。
- **不实现 `IReplayState`**：理由同上「瞬时态纪律」；接线波再定「击倒要不要入档」。
- **背后处决不进确定性内核**（PRP/stealth-execution §2.4）：处决是玩家实时按键触发的交互，做成了
  「输入 → 立即结算」，与 `DialogueKeyboardInput` / `MirrorInputPresenter` 同一层，**不进 `InputCommand` 位掩码**。
  **代价：处决不可回放**——回放跑的是确定性 tick，喂不进实时按键。要改就得补一个 `InputCommand` 按钮位、
  把结算挪进 tick，**并在同一次改动里**同步位断言测试（若动到快照字节布局还要升 `ReplayFormat` 版本）。
- **`stealth.assassinated` 只有处决能写**：判定门的 `AssassinationAllowed`（此刻能不能下刀）与这个键
  （已经用暗杀解决过目标）是两件事，拿前者点后者会把「站在守卫背后」写成「已经杀过他」（字典 §4.2、PRP §2.5）。
  写入点是 `ExecutionResolver`，**时机是执行成功之后**。
- **当前表里没有 `defeat_method = 暗杀` 的行**：两行怪（巡夜人 / 井边妇人）都是 `可击杀（方式没写）`，
  所以处决在**当前内容下不可达**（门槛会正确地拒掉它们）。这是内容缺口不是实现缺口——内容侧补一行
  `defeat_method = 暗杀` 的妖物后即可用，见交付报告「待策划拍板」。

## 验证入口

- EditMode：`Assets/_Project/Scripts/Tests/EditMode/Stealth/StealthRulesTests.cs`——**12 个测试类、124 条**，
  每条判定都配负对照（45 组，含遮挡未命中 / 零长线段 / 非法形状、绕背的正前 / 侧面 / 锥外 / 超距 /
  已察觉 / 已死 / 重合 / 零朝向、击倒期间按攻击键 / 负步长 / 未知口径、追兵 2.5 与 3.0 太慢 / 6.0 太快、
  断视线不足 / 距离不足、召唤半径内没人 / 不足一群 / 空编队、`default(StealthOccluder)` 等）。
- 背后处决（2026-10-07 波）：
  - `Tests/EditMode/Stealth/ExecutionRulesTests.cs`——门槛合取 + 四类拒绝负对照 + **用真表两行数据**证明
    `killable = true` 的怪不能被处决；
  - `Tests/EditMode/Stealth/ExecutionResolverTests.cs`——三件事（事实 / 死亡 / 战斗结果）+ 三处埋点 +
    表现钩子，负对照含「不在遭遇里不承载结果」；
  - `Tests/EditMode/Stealth/ExecutionInteractorTests.cs`——交互层选目标与察觉口径，负对照含
    「对着空气按 F 什么都不发生」；
  - `Tests/EditMode/Stealth/ExecuteInputBindingTests.cs`——`Gameplay/Execute` 存在、绑 F + 手柄、
    且**只在 Gameplay 图**；顺带守住「Interact 的 E / F / South 没被动过」；
  - `Tests/EditMode/Monster/MonsterExecutionTests.cs`——`ApplyDamage` 对 `killable = false` 的怪仍拒伤、
    而 `ApplyExecution` 能杀死它的**并存断言**。
- **Showcase（2026-10-07 本波新增）**：`Assets/_Project/Scripts/Tests/Showcase/Stealth/StealthShowcase.cs`
  —— 2 条用例、各 5 步：`Sight_BlockedByCover_HidesFromEnemy`、`Sneak_BehindEnemy_AllowsAssassination`。
  - **回放舞台**：`Assets/Scenes/SampleScene.unity`，走 Boot 真实流程（`ScenePath => null` + `EnterWorldFromTitle`）：
    玩家推摇杆走北侧路线到巡逻线北侧观察点，怪物是场景里那只真实巡逻怪（`enerme`）。
  - **遮挡体就登记在场景里**（这是正式场景的标准接法，正式场景照抄）：
    `Environment_Graybox/Cover_SightDemo`（box，层 Default，逻辑覆盖 4 × 0.8 米）登记为
    `Encounter` 物体 `EncounterSceneView.sightOccluders` 的第 1 条（矩形，尺寸给**全宽 × 全深**）；
    场景就绪时 `MonsterEncounterState.OnSceneReadyAsync` 调 `view.CollectSightOccluders()` →
    `step.Sight.SetOccluders(...)` 一次性喂进内核，tick 路径只做几何求交。**空清单 = 不挡视线**是既有语义。
  - **看得见什么**：① 站在视野锥里、中间没有遮挡 → 被察觉（`stealth.hidden` 假）；把 demo 掩体摆到两人连线上
    并重采集几何 → **不被察觉**（`stealth.hidden` 真、`stealth.cover` 真），画面里玩家与怪物之间横着那只掩体；
    ② 潜行绕到巡逻怪背后 1 米内 → `stealth.behind` 真 → 可处决。截图 `01-掩体挡住视线.png`、
    `03-潜行到背后.png`、`04-可处决.png`。
  - **检查点**：没遮挡时被察觉（实测相隔 2.76 米、近身半径 1.5 之外，证明靠的是视野锥不是贴身）/
    遮挡后 `hidden` + `cover` 同真 / **已知边界**：怪物照样追（遮挡只写 `stealth.*` 事实，不喂 `MonsterRules.Sense`）/
    绕背全程未察觉 / `stealth.behind` 真 / `AssassinationAllowed` 真。
  - **回放特有的一步**：把那只**已经登记好的**掩体挪到玩家与怪物连线的中点再重采集一次几何——
    静态掩体不会自己出现在连线上，而怪物会一路走到玩家脚下（`MonsterRules` 的逻辑移动不吃碰撞）。
    正式场景里掩体是静态摆好的，**接法一致**，只有位置由关卡定。
  - **与处决那一波的分工**：本 Showcase 断言的是 S3 的**绕背判定**（`AssassinationRules` → `AssassinationAllowed`），
    **不含** F 键处决交互（`ExecutionInteractor` / `ExecutionResolver` / `ExecutionRules` 的物种门槛那一层，
    属 `PRP/stealth-execution` 波）。两层的差别：`AssassinationAllowed` 只答「位置 + 察觉」，
    `ExecutionRules` 再叠一条「`defeat_method == 暗杀`」——按本 guide 上面的已知约束，当前内容表里没有这种怪，
    所以**演示怪（可击杀）在 F 键那条路上会被正确拒掉**，本 Showcase 不冒充它。

- 配置资产：`Assets/_Project/Data/Stealth/StealthConfig.asset`（28 字段与 C# 声明逐一对齐）。

## 接线后的表现清单（回放要验什么）

绕背处决的提示、击倒后只能慢走且不能攻击、追逐追得上人、掩体后不被看见。见
[`视觉验收清单.md`](../../../docs/planning/视觉验收清单.md) §6。
