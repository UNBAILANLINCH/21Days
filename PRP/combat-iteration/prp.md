# PRP: combat-iteration（战斗系统迭代）

> 2026-10-18范围修订：用户明确普通巡逻只警戒、追逐和抓捕，不进回合制。正式名册解除1003的按种类自动映射（`monster_1003.monsterKindId=0`），保留显式剧情／BOSS定义及通用升级机制。下文D3、D5中的普通市令自动升级与旧回放是历史设计，不能作为现行配置要求。

> 输入：[`prd.md`](prd.md)（用户 2026-10-14 拍板 9 条 + 待确认 5 条全按推荐）。
> 本文只出架构；`tasks.md` 等用户对本文点头后再出（PRP 三道门之三）。
> 路径省略前缀 `Assets/_Project/Scripts/Runtime/`。行号对应 HEAD 8227046，执行前按文件名复核。

## 上下文快照

### 模块约束（摘自三件套与代码，只列影响本次设计的）

| 模块 | 关键约束 |
| --- | --- |
| Monster | `EncounterStep` **构造参数不能变**（VContainer 取参数最多的构造函数），新依赖一律走后置 `BindXxx`（`EncounterStep.cs:15-17`，先例 `BindIdentity` :207、`BindDropSink`）。<br>tick 顺序：玩家 Step → 玩家普攻遍历目标（:457-470）→ `Taming.AdvanceTargets`（怪 Step，怪打人在 `TamingRules.cs:202-206`）→ `SettleStealth`/`SettleChase` → `SettleKillDropOnce` → 主怪 / 玩家生命归零结算（:480-497）。<br>「只结一次」有四道闸：`SettleBattle` 拒重复（:340）、`ConsumeResult`（:318）、`PendingResult==None`（:480）、`StartBattle`（:309），掉落另有 `killDropSettled`。<br>多怪：i==0 是主怪，其余 `CreateForActor(stableId)` 复制主怪种类（`MonsterRules.cs:90-91`）；`SettleStealth/SettleChase/IBattleEntryContext/SettleKillDropOnce`/胜负只看主怪。<br>存档与回放快照要求目标数量与场景一致，否则抛（`EncounterStep.cs:799`、`TamingRules.cs:162`）。 |
| Stealth | `ChaseCaughtRules.Resolve(in ChaseOutcomeSettings, caughtCountSoFar)` 是纯函数，零生产调用方；`StealthKernel` 已暴露 `ChaseOutcomeSettings`（:46、:71），但其中的 `PlayerHealth` 是配置上限，不是实时血量。<br>击倒现已生效：`PlayerRules.ApplyDamage` 每次受击都喂 `KnockdownRules`（`PlayerRules.cs:151-159`），口径就是 `KnockdownThenDeath`（`KnockdownRules.cs:155`）。<br>`ExecutionResolver.cs:148-151` 对**任意**目标处决都 `SettleBattle(Victory)`。 |
| Identity | `ResolveExposure`（`IdentityRules.cs:260-304`）已能判定、调策略、失效身份、记次数；策略位在 `IdentityInstaller.cs:58` 写死 null。<br>`LosesIdentity`（:310-315）**有意把警戒值（红区）排除在失效之外**，理由是原文只说「转敌对」，标了 [推断]。<br>`ExposureSignals` 的红 / 橙区字段零生产者；`IdentityState` 没有事件。 |
| Battle | 唯一开战入口是订阅 `BattleStageEnteredEvent`，`Begin`/`RunAsync` 都是 private（`BattleFlow.cs:194`、:258）。`RunAsync` 剧情耦合三处：`TryBeginBattle`（:247）、`CompleteBattleAsync`（:355）、`ReleaseBattle`。<br>依赖方向 **Battle → Monster 单向**，契约声明在 Monster 侧（`IBattleEntryContext.cs:36`，battle-module-guide :49、:59）。<br>玩家战斗生命取 `BossDefinition.PlayerHealth` 满血开打（`BattleSetup.cs:137`）；`PlayerBattleSnapshot` 已分当前值与上限。<br>`BossEntryContext.Initiator` 写死 `PlayerAttacked`（:113），组件自述「只回答局势、无副作用」（:23-24）。 |
| Narrative | `NarrativeTrigger.InteractAsync(ct)` 是 public，守卫是「距离 ≤ radius」等（:63-78），按 E 走的是同一路。<br>场景物体由 `NarrativeService` 在场景加载时扫描绑定（:93、:407），不在构建回调里找。 |
| Session | 自动存档闸 `SaveGateRules.CanSave(gameplay, dialogue, panel, battlePending)`。其中 `battlePending` 取自 `SessionStateAdapter.cs:82`，即 `step.PendingResult != None && !ResultConsumed`。 |
| Player | `PlayerModel.Health` 是 int，setter 为 internal；上限只在 `PlayerConfig.maxHealth`（默认 3），对外没有 `MaxHealth`，也没有按值或按比例设血的入口。 |
| 状态查询 | `DialogueService.IsRunning`（:70）、`PerformanceService.IsRunning`（:120）、`BattleFlow.IsBattleRunning`（:97）、`IWorldPauseService.IsPaused`、`NarrativeService.IsBusy`（:69）。 |
| Taming | 死亡对象不能被控制，控制期间目标死亡会把控制权还给玩家（taming-module-guide :13）。<br>`Encounter/TamingDemo` 旧挂点保持不激活，不能与现有调度器同时推进（:32），W5 加怪时不许启用它。 |
| Session | 存档一律经 `SaveGateRules` 判断，业务代码不许绕过闸门直接存（session-module-guide :139）。D5 的「升级在途」只经 `SessionStateAdapter` 并入现有闸门，不另开存档通道。 |
| 文档现状 | Identity 只有 module-guide，没有 external-api / extension-guide，D8 的 `/generate-doc sync identity` 要补齐。其余六个模块三件套齐全。 |
| 埋点 | `docs/telemetry.md`：埋点就是格式固定的 Unity 日志 `[Game][T] <级别> <模块>/<事件> \| <JSON>`。模块业务点要自己补；新决策点都要有一条能倒推现场的埋点，各单要埋的事件见 tasks。 |

### 必须规避的 pitfalls（`ai-docs/pitfalls.md`）

- :225 **测试自己把依赖装上，接线缺口全程不报**：每条接线都要有「从真实容器解析」的测试，加负对照。
- :243 **给共享 MonoBehaviour 加序列化字段，默认值静默改掉别的场景**：所有新字段的代码默认值必须等于「旧行为」，新口径只写进资产。
- :64 **SampleScene 是真实距离**：新怪位置不压任何现有回放路线；回放用 `WalkTo` 或锚点走位，不瞬移。
- :578 **构建回调里 `FindObjectOfType` 看不见玩法场景**：场景物体的绑定放到场景加载时，或开仗时现找。
- :584 / :267 **MCP 改场景后要当场 `SaveScene`**，并按脚本 guid grep 校验。
- :590 **纸片角色的 `transform.up` 朝向相机**：BOSS 和新怪的朝向读逻辑朝向，不读 transform。
- :410 **逻辑 tick 与渲染帧脱节会拖影**：BOSS 移动要插值。
- :404 **纯纸片 NPC 的 Visual 在半身高**：新怪沿用现有巡逻怪预制体结构。
- :342 / :377 **走 Boot 的回放会写真实存档槽**：新回放沿用现有隔离做法。
- :524 **回放 `group_names` 按子串匹配**：派单逐模块写全名。
- :261 / :518 **回放中途保存 `.cs` 会吞协程**：回放期间不并行改代码。
- Luban :237 / :317：本次**不改表**（见 D3），这两条不触发。

### 适用规则

- `project-root.md`：Battle → Monster 单向，Monster 不认识 Battle；加能力顺序是「复用 → 扩展 → 新建」；新文件头注释写明前两步为何不行。
- `csharp-code.md`：`[SerializeField] private` 加只读属性；数值进 SO；每帧路径上不 Find、不分配、不打日志；`UnityEngine.Object` 判空只用 `==`。
- `unity-assets.md`：场景和资产改动走 MCP；`.meta` 由 Unity 生成；运行时不改 SO。
- `unity-tests.md`：EditMode 优先，测试类命名 `<被测类>Tests`，方法命名 `行为_条件_期望`；失败时不许改弱断言。
- `model-routing.md`：派单显式传 model；回放逐模块列理由。

## 架构决策

### D0 总原则

1. **全部口径进配置，代码默认值等于旧行为**：新字段的代码默认值保持现状，用户拍板的口径只写进资产。这样 EditMode 里用代码默认值的旧测试不回退，策划答复后只改资产。
2. **不新建模块、不新建 asmdef、不改 Luban 表**。
3. **巡逻者乙零改动**：靠「附加巡逻者默认不参与」的开关实现（D2），不在代码里写死它的 id。

### D1 露馅后果（Identity + Monster）

- **策略**：新增 `Identity/ConfiguredExposurePolicy.cs`，实现 `IExposureOutcomePolicy`，口径读 `IdentitySettings` 的新字段 `exposureOutcome`（None / Death / Chase）。
  - 代码默认值 None，`IdentityConfig.asset` 写 Chase。
  - `IdentityInstaller.cs:58` 改为注入这个策略，替换 null。
  - 为何新建：接口本身就要求一个实现，`IdentitySettings` 是数据块，不该承担决策。
- **失效原因集合可配**：`IdentitySettings` 加 `identityLossCauses`（`ExposureCause` 位掩码）。
  - 代码默认值等于现在的五种规则层原因（`LosesIdentity` 现状），资产写六种全含，满足用户拍板 2。
  - `LosesIdentity` 改为按配置判定。静态版保留并转调默认值，旧测试不动。
- **生产调用点**：`EncounterStep` 加后置 `BindExposure(IdentityRules, IdentityLedger, SuspicionState)`，在 `MonsterInstaller` 构建回调里与 `BindIdentity` 同处装配。每 tick 的处理位于 `AdvanceTargets` 之后、`SettleStealth` 之前：
  1. 玩家**没在借身份**就跳过，这是负对照。
  2. 汇总「主怪 + 完整参与的附加怪」（见 D2）本 tick 的感知档位，得到红区 / 橙区信号，调 `ResolveExposure`。
  3. 结果为 Chase 或身份失效：让**本 tick 察觉到玩家的那几只**转敌对。`MonsterRules` 新增公开的「被惊动」入口，内部走现有 `EnterHostile`，之后的追击沿用现有 ChaseRules。
  4. 结果为 Death：玩家生命置 0，走现有失败流程。剧情阶段内对应 Downed，自由探索对应镜碎页。死亡口径的专门表现不在本次范围。
- **已知行为变化**（用户拍板 2 的必然结果，与策划 `02` R32/R34「伪装免橙区不免红区」一致）：借着身份**站进红区**会立刻露馅，身份只在橙区保护玩家。
  - 现有回放 `Identity/EnterIdentity_StopsEnemyAttack_ExitRestoresIt` 的走位落在怪正前方 1.5 米，即红区，要改到橙区；断言语义不变。

### D2 一场遭遇多种怪（Monster + Taming）

- `TamingActor`（`Taming/TamingActor.cs`）加两个序列化字段：
  - `kindId`：默认 0，表示沿用主怪种类，即现状。
  - `fullParticipant`：默认 false，表示现状的附加巡逻者，只巡逻、感知、攻击。
  - 为何扩展而不新建：它就是「场景里一只巡逻者的作者数据」。
- `EncounterSceneView` 读出这两个数组，经 `ConfigureTaming` 的新可选参数传进来；`MonsterRules.CreateForActor(stableId, kindId)` 加可选的种类覆盖。
- 「完整参与者」的定义：主怪恒是；附加怪要 `fullParticipant == true`。只有完整参与者会：
  - 被背后处决：`MonsterEncounterState.BindExecution` 改用多目标重载，`ExecutionInteractor.cs:171` 已存在。
  - 掉落：闸门从单个布尔改为按 id 各一次。
  - 产生露馅信号、被露馅惊动（D1）。
  - 触发被抓口径（D4）。
  - 升级回合制（D5）。
- **巡逻者乙**两个新字段都保持默认，行为逐字不变。
- **整场遭遇的胜负只认主怪**：主怪死亡判 Victory，玩家生命归零判 Defeat，这条维持现状。
  - 附加怪被处决或在回合制里被打赢，只是它自己死亡并掉落，不结算整场。
  - `ExecutionResolver.cs:148-151` 相应改为只在目标是主怪时 `SettleBattle(Victory)`。
- **存档与回放快照按稳定 id 对齐**：
  - 存档里没有、场景里有的新怪，按场景初始状态开局。
  - 存档里有、场景里没有的，丢弃并记一条 Warn。
  - 不再因为数量不同而抛错。快照结构有变就升 `ReplayFormat`。
  - 这是加怪的前置条件，否则玩家旧存档读档直接报错。
- **执行中修订（2026-10-14）**：
  - W1 发现读档后主怪丢种类（`Restore` 不走 `Begin`）；用户同意并进 W2 修：恢复时重新解析种类、重算视锥。
  - 已死目标的掉落闸门在恢复时直接标为已结算，防止重复发放。
  - 驯服回放 `TamingShowcase.cs:52` 断言巡逻者恰好 2 只，这是协作者 linch 的文件。用户授权只改这一行，改成「至少 2 只，且前两只是原来的主怪和巡逻者乙」，在待审清单里单列，由用户转告 linch。

- **执行中修订（2026-10-15，W6a 实机发现，用户拍板）**：
  - **巡夜人处决**：处决有物种门槛，只认「击败方式 = 暗杀」。巡夜人对应的妖 1 在表里写的是「可击杀（方式没写）」，所以按 F 被拒；EditMode 测试是绕过这道门槛写的，没发现。用户选择改表：把 `Tables/Data/yao/1.json` 的击败方式改为「暗杀」，可普攻击杀的标记保持不变。**这是替策划定的内容**，待审清单里单列，由用户与策划对齐。
  - **巡夜人随后改为「只能暗杀」**：「可普攻击杀 + 暗杀」同时为真，违反 `yao.xml` 对「暗杀」的定义（只能绕背处决），也违反处决功能的不变量「能常规打死的怪绝不该也能被一键处决」。用户拍板把妖 1 的 `killable` 改为 false，与市令同类，不在测试里开例外。
  - **战斗打到一半销毁作用域会抛错**：界面服务比战斗流程先被释放，收场时还在调界面。用户同意并进本轮修。
  - **驯服回放 FAIL，归协作者 linch，只报告**：按 T 时选中的是离得最近的巡逻者乙，而不是主怪。原因是 10-07 主怪巡逻线从 4 米拉长到 8 米；巡夜人在 30 米外，不影响这个选择。该回放 09-28 之后就没人跑过。

### D3 怪物的回合制战斗数据（Battle）

- **复用 `BossDefinition`**，加可选字段 `monsterKindId`（int，默认 0 表示不是怪物行）。
  - `BossRoster` 加「按种类查询」，并校验非 0 的种类 id 不重复。
  - 不动 payload 语义，不进 Luban 表，沿用 `BossDefinition.cs:230-232`「等 C90/C91 再迁表」的既定口径。
- `BossRosterConfig.asset` 新增一行市令：
  - `monsterKindId` 1003；生命为占位值；`outOfBattleDrunk` 0；`playerHealth` 10。
  - 表现沿用 `BattleBoss_Placeholder`。
- 招式与 AI 沿用全局 `TurnBasedConfig`，即 BOSS 三招（Q5）。巡夜人 1001 **不配**。

### D4 被抓后果（Stealth + Monster）

- `EncounterStep.UseStealth` 时把内核的 `ChaseOutcomeSettings` 存下。
- 在「被抓」判定点（`EncounterStep.cs:601-607`）调 `ChaseCaughtRules.Resolve`：用**实时血量**重建 settings，`caughtCountSoFar` 取击倒次数。
  - `EntersKnockdown`：维持现状，受击已经喂了击倒。
  - `IsFailure`：玩家生命置 0，走现有生命归零的收尾。**不新增 Result 值**，快照结构不变。
- 被抓判据从「只认主怪」放宽到「任一完整参与者」，否则巡夜人追上玩家不算被抓。
- `StealthConfig.asset` 的被抓口径写 `KnockdownThenDeath`；代码默认值保持现状。
- **执行中修订（2026-10-14，W0 发现）**：现有 `KnockdownThenDeath` 除了看血量，还会在第 N 次被抓时直接判死（`ChaseCaughtRules.cs:163`），资产里 N = 2，跟用户拍板的「没血才判死」不符。改法：
  - 允许 `FailAfterCaughtCount = 0`，表示「不按次数判死」。现在的构造函数会拒绝 ≤0，要放开 0，负数仍然拒绝。
  - `StealthConfig.asset` 的 `chaseCaughtFailCount` 写 0；代码默认值仍是 2。
  - W2 落地时发现：`caughtCountSoFar` 取的是击倒次数，只在换阶段时清零（`ResetForNewLevel`），所以实际数的是**跨遭遇累计的击倒次数**，不是「同一次追逐里被抓几次」。资产填 0 后这个问题不起作用；策划以后要改回按次数判死，得先重新定义这个计数。
  - W2 的实现取舍（主窗口已验收）：
    - 露馅只在命中原因变化时结算一次，避免站在红区里每 tick 重复结算和刷埋点。
    - 附加的完整参与者「本 tick 打中活着的玩家」就算被抓，不经过只认主怪的 ChaseRules。
    - `chase.caught` 事实照旧每次被抓都写，避免改变剧情侧能读到的事实。
- 对配了战斗数据的怪，「追上并打中」在攻击判定处就被 D5 拦截，进回合制，走不到这里。两条规则天然互斥，无需额外判定。

### D5 实时遭遇升级回合制（Monster ⇄ Battle）

**跨模块契约（声明在 Monster，Battle 实现）**

- 新增 `Monster/IEncounterEscalationPort.cs`，两个方法：
  - 「这个种类能不能升级」：同步回答，用于当 tick 决定是否吞掉这一下伤害。
  - 「发起升级」：同步返回接受或拒绝，战斗本身异步进行。
- 新增 `Monster/EncounterEscalationRequest.cs`（只读 struct），字段：目标 id、种类 id、触发情形（玩家打中未察觉的怪 / 玩家打中已警戒或敌对的怪 / 怪打中玩家）、玩家当前生命与上限。
- 为何新建：`IBattleEntryContext` 是「主怪局势快照」，回答不了「这一下是谁打了谁」，也不能发起开战；Monster 不能引用 Battle，契约只能放在 Monster。
- **装配**：`BattleFlow` 实现该端口（扩展，不新建：「开一场仗」本来就是它的职责），在 `BattleInstaller` 的构建回调里 `step.BindEscalation(flow)`。这与现有 `flow.BindEntryContext(step)` 同处同向，Monster 侧零感知。

**Monster 侧（`EncounterStep` + `TamingRules`）**

- **拦截点一**：玩家普攻打中目标（`EncounterStep.cs:466`）时，在调 `ApplyDamage` **之前**判定是否升级。
- **拦截点二**：怪物攻击判定成立（`TamingRules.cs:202`）时，在 `player.ApplyDamage` **之前**判定。`AdvanceTargets` 加一个可选的「接管这一下」回调，默认 null 时行为不变。
- **升级条件**（全部满足才升级，否则逐字走原路径）：
  - 目标是完整参与者、活着、没被驯服。
  - 玩家不是在操控被驯服的怪。
  - 对应触发在配置里开着：`MonsterConfig` 加三个开关，代码默认关，资产全开。
  - 端口存在且该种类能升级。
  - 没有待消费的结果，也不在升级途中。
- 背后处决走 `ExecutionInteractor` 另一条路径，不经过这两个拦截点，所以天然不升级。
- **升级途中**：`EncounterStep` 进入「升级在途」状态，tick 像「结果待消费」那样早退，防止世界暂停生效前的几帧里重复触发或继续挨打。
  - 这个状态是瞬态，不进快照：期间世界暂停，也不可存档。
  - `SessionStateAdapter.cs:82` 的 `BattleResultPending` 把「升级在途」也算进去，于是不可存档。
- **结果入口**：`EncounterStep` 新增公开的「完成升级」方法，参数为胜或负、玩家剩余生命比例。
  - 胜：目标强制死亡，不看 `killable`，这正是用户拍板 6「视同击杀」。随后按 id 掉落一次。若目标是主怪，生命归零后由现有 :480-497 判 Victory，再由 `EncounterBattleBridge` 原样回写剧情。**桥不改**。
  - 负：玩家生命置 0，走现有失败流程。
  - 胜时按比例写回实时血量。

**Battle 侧（`BattleFlow` + `BattleSetup`）**

- `RunAsync` 抽出与剧情无关的内核，剧情版和遭遇版各包一层：
  - 剧情版行为逐字不变。
  - 遭遇版不调 `TryBeginBattle`、`CompleteBattleAsync`、`ReleaseBattle`，在**幕下**回调 `EncounterStep` 的「完成升级」。
- 若已有一场仗在途（`IsBattleRunning`），拒绝升级，Monster 侧走原路径。
- 触发情形到开战请求的翻译放在 `BattleSetup`（现有翻译层），结果为：

  | 触发情形 | 填入开战请求 | 判定结果 |
  | --- | --- | --- |
  | 玩家打中未察觉的怪 | `Initiator=PlayerAttacked`、`SneakStrikeReady=true` | 偷袭（`BattleEntryRules.cs:31`），怪物先掉 20% |
  | 玩家打中已警戒或敌对的怪 | `Initiator=PlayerAttacked`、`InAlertOrHostileContext=true` | 正面攻击，玩家先手（:42） |
  | 怪打中玩家 | `Initiator=BossAttacked`、`InAlertOrHostileContext=true` | 被打，怪物先手（:44） |

  被规则拒绝时端口返回拒绝，走原路径。
- **血量互通**（Q1）：`BattleSetup.TryCreate` 加可选的玩家当前生命与上限，不传时行为不变。
  - 进战斗：战斗生命 = `ceil(实时比例 × 战斗上限)`，至少 1。
  - 出战斗：胜利时实时生命 = `ceil(战斗剩余比例 × 实时上限)`，至少 1；失败时为 0。
    - **修订（2026-10-16，code-reviewer 指出）**：进出都向上取整会凭空回血，例如实时 1/3 → 战斗 4/10 → 原样打赢出来变成 2/3。改为出场四舍五入（远离零），胜利至少 1，进场仍向上取整。这样 1/3、2/3、3/3 原样往返都不变。
  - `PlayerRules` 新增只读 `MaxHealth`，以及夹在 [0, Max] 的「设定生命」入口。设定时**清掉击倒状态**，因为战后不该还趴着。

### D6 BOSS 主动发难（Battle + 场景）

- **新增** `Battle/BossAggro.cs`，是 MonoBehaviour，挂在 `Npc_SampleBoss` 上。
  - 为何新建：`BossEntryContext` 明确「只回答局势、无副作用」，`NarrativeTrigger` 管剧情入口，把移动塞进任何一个都会破坏职责。
- **状态**：待机 → 冲刺 → 接触 → 战后回位 → 解除武装 → 待机。
  - **待机 → 冲刺**：`BossEntryContext` 判玩家在红区，且守卫全部通过。守卫是：不在对话、演出、战斗中；世界未暂停；剧情不忙；BOSS 未被击败（被击败后物体被 `NarrativeFlagVisibility` 隐藏，组件随之停）。
  - **冲刺**：每帧朝玩家移动，渲染插值；朝向用逻辑方向。
  - **接触**：距离 ≤ 交互半径时，先把 `BossEntryContext` 的开战理由置为「BOSS 先动手」，再调 `NarrativeTrigger.InteractAsync`，走与按 E 完全相同的嘲讽对白和开战链。`BattleFlow` 开仗时现取局势，得到「被打、BOSS 先手」。
  - **战后**：开战理由复位为「玩家先动手」，这样按 E 挑战仍是正面攻击（回归）。若剧情没进击败阶段，BOSS 在幕下瞬移回原位，转入解除武装，直到玩家离开察觉范围才恢复待机（拍板 8）。
- `BossEntryContext` 的开战理由改为可设置，默认「玩家先动手」，即现状。
- **数值进数据**：`BossDefinition` 加 `aggroEnabled`（默认 false）和冲刺速度。`sample_boss` 那一行开启；BOSS 仍然没有视线，只比半径，沿用现有边界。
- **绑定**：服务由 Battle 侧在玩法场景加载时扫描绑定，与 `NarrativeService.cs:93` 同一做法。不在构建回调里找，也不每帧 Find。
- BOSS 位置不进确定性内核，不进存档。读档后 BOSS 在原位。
- **执行中修订（2026-10-15，W4 发现、用户拍板）**：
  - **问题**：红区半径（全局 2 米）与按 E 的交互半径（2 米）重合。发难开着时，走近按 E 必先进红区，所以「按 E 正面攻击」触发不了。
  - **发难半径与反应时间**：BOSS 改用独立的发难半径（`BossDefinition.aggroRadius`，占位 5 米，以原位为圆心）加反应时间（`aggroReactionSeconds`，占位 3 秒）。
    - 玩家进入半径：BOSS 进入「警觉」，头顶出提示，开始计时。
    - 反应期内走到跟前按 E：正面攻击，玩家先手。
    - 计时到点玩家仍在半径内：冲刺、嘲讽，以「被打」开战，BOSS 先手。
  - **拴绳**：`aggroLeashDistance`，占位为发难半径的 2 倍。冲刺离原位超过这个距离就放弃，走回原位。
  - **W4 顺带修的既有缺陷**：`BossEntryContext` 原先把场景的 (x, y) 当逻辑位置，SampleScene 是 XZ 平面，y 是高度。现在改为用场景投影，同 `MirrorSceneBinder`。
  - **BOSS 挪位（2026-10-16，W6b 后核实、用户拍板）**：出生点 (−4, 3.4) 离 BOSS 原位 (−4.2, 8.4) 恰好 5 米，正压在发难半径上，开局往北一步就会触发 BOSS。用户选择保留 5 米半径、把 `Npc_SampleBoss` 挪远；选址依据见 `w7-plan.md`。BOSS 回放的站位随之调整。
    - 院子里找不到离出生点和各回放路线都 ≥8 米的位置。用户在知道代价后仍选院外南面 (−4, −8)：去挑战 BOSS 要先穿过巡逻线之间的缺口，那段离巡逻线端点 2.3 米。BOSS 因此成了「先绕过巡逻怪才见得到」的敌人。
  - **退出报错的第二处（W6b 发现）**：回合制伤害飘字的动画没跟界面生命周期绑定，战斗中途销毁作用域时会往已销毁的界面写。按同一授权并进 W7 修。
  - **W4 新增的状态迁移**：交互被拒，以及按 E 开的仗打完，都转入「回原位 + 解除武装」，避免玩家站在跟前时被反复拉回战斗。

### D7 场景与资产（走 Unity MCP，派 sonnet 按步骤执行，坐标由主窗口在 tasks 里写死）

- `SampleScene`：
  - 新增巡夜人：复制现有附加巡逻者的结构，`kindId=1001`、`fullParticipant=true`，路线放在村子另一侧。具体坐标在出 tasks 前，先由检索任务比对所有 Showcase 回放的走位常量后确定。
  - `Npc_SampleBoss` 挂 `BossAggro`。
  - 每步 `SaveScene` 并做 guid 校验。
- 资产：`IdentityConfig.asset`、`StealthConfig.asset`、`MonsterConfig` 资产、`BossRosterConfig.asset` 按 D1–D6 写入新口径。

### D8 收尾

- 文档三件套同步：identity / stealth / monster / taming / battle / session / player（`/generate-doc sync`）。
- 修正过期描述：
  - `features-spotlight` 02/03/04/06/09 的第 7 节。
  - `HANDOVER.md:7`。
  - `视觉验收清单.md` 第 6 节，并新增四条：回合制 BOSS 战、遭遇升级、BOSS 发难、被抓击倒。
- `docs/roadmap.md` 回填状态列。

### 依赖方向复核

```
Battle ──► Monster（IEncounterEscalationPort / EncounterEscalationRequest / EncounterStep 公开方法）
Battle ──► Narrative / Dialogue / Performance（只读状态与 InteractAsync）
Monster ──► Identity / Stealth / Taming / Player（已有方向）
Session ──► Monster（已有）
Monster ─╳─► Battle（不引用）      Game.Core 不动
```

全部在 `Game.Runtime` 单个 asmdef 内，没有 Runtime → Editor / Tests 引用。

## 波次概要（细节进 tasks.md）

| 波 | 内容 | 档位 | 并行性 |
| --- | --- | --- | --- |
| W0 | 检索：所有 Showcase 回放的走位常量和区域，出新怪的候选坐标；核实 `StealthConfig` 与 `MonsterConfig` 资产的现值 | sonnet | 与 W1 并行 |
| W1 | D2 多种类 + 存档按 id 对齐 + 处决多目标 + 掉落按 id | opus | 先行，后续都建立在「完整参与者」之上 |
| W2 | D1 露馅后果 + D4 被抓后果 | opus | 在 W1 之后（同改 `EncounterStep`） |
| W3 | D3 + D5 升级回合制、血量互通、存档闸 | opus | 在 W2 之后 |
| W4 | D6 BOSS 发难（`BossAggro`、`BossEntryContext`、`BossDefinition` 字段） | opus | W3 之后（出 tasks 时改为串行：只有一个编辑器，两单同时编译和跑测试会互相干扰） |
| W5 | D7 场景与资产（MCP） | sonnet | W3、W4 之后 |
| W6 | Showcase 回放新增与改写（Identity / Stealth / Monster / Battle） | opus | W5 之后 |
| W7 | 跑测试与回放（逐模块写死范围）+ code-reviewer | sonnet | W6 之后 |
| W8 | D8 文档收尾 | sonnet | W7 之后 |

## 验证清单

- [ ] Unity 控制台零编译错误；`project-lint` 零违规；`gc_scan.py` 不新增问题（含 invariants：依赖方向、命名空间、`.meta`）。
- [ ] EditMode 覆盖：
  - 露馅策略三口径与失效掩码（含负对照），被抓口径（含「任一完整参与者」）。
  - 多种类（各用各的种类数据、巡逻者乙不变），存档按 id 对齐（多、少、一致三种情况）。
  - 三种升级与「不升级」的负对照（未配数据、开关关、处决、驯服操控、已有仗在途）。
  - 升级胜负结算只一次，血量换算的取整边界，升级在途时不可存档。
  - BOSS 发难状态机、守卫、重新武装，按 E 回归。
- [ ] 接线测试从**真实容器**解析：露馅策略已注入、`BindExposure` 已装配、`BindEscalation` 已装配、被抓 settings 已存（pitfalls :225）。
- [ ] 全量 EditMode 绿，失败不改弱断言。
- [ ] 回放（范围在 tasks 里逐模块写死，每个模块附影响理由）：
  - Identity：新增露馅用例，改写原用例走位。
  - Stealth：处决多目标、只认主怪结算。
  - Monster：多种类、被抓击倒、三种升级。
  - Battle：BOSS 发难、按 E 回归。
  - 新怪位置可能波及的其他模块，由 W0 检索结果决定名单。
- [ ] PRD 验收 A–F 逐条对应到任务（tasks.md 末尾附对照表）。
- [ ] 无 public 字段；每帧路径无 Find、GetComponent、Log、分配（`BossAggro` 的 Update 重点审）。

## 风险 / 回滚

| 风险 | 应对 |
| --- | --- |
| 加怪让旧存档和回放快照因数量不符而报错 | D2 先做按 id 对齐，W1 必须先于场景改动（W5） |
| 新怪干扰现有回放（上次巡逻者乙就是这样把 Mirror 和 Stealth 回放弄红的） | W0 先比对所有走位常量再定坐标；W7 按名单复跑 |
| 红区露馅改变「借身份不被打」的体验 | 已在 D1 写明是拍板 2 的必然结果；原回放改走位；策划若答「红区不失效」，只改资产掩码 |
| 升级在世界暂停生效前的几帧里重复触发 | 「升级在途」早退，加端口的「已有仗在途」拒绝，两道闸 |
| `BattleFlow` 重构波及剧情版 BOSS 战 | 剧情版逐字保留，`Showcase.Battle` 原四条用例作回归 |
| BOSS 在确定性内核之外移动，回放不稳 | 回放用 `WaitUntil` 加状态检查点，不卡固定时长 |
| 并行会话或协作者同时改 `SampleScene` / `Boot.unity` | 开工前看 `git status`；按路径提交；场景只提交自己的 hunk |

**回滚**：改动全在工作区，按波次分批提交。单波回滚就 `git checkout -- <该波路径>`，已建的场景对象用 MCP 删回。所有新口径都在资产里，策划推翻某条口径时只改资产、不回滚代码。
