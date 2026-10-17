# Tasks: combat-iteration（战斗系统迭代）

> 依据：[`prd.md`](prd.md)（验收 A–F）、[`prp.md`](prp.md)（决策 D0–D8，用户 2026-10-14 点头）。
> 路径省略前缀 `Assets/_Project/Scripts/`。每单写明：动哪些文件 → 做什么 → 产出 → 派单档位。
> **派单通用要求**：
> - 改 `Runtime/<Module>/**` 前必读该模块的 module-guide（钩子强制）。
> - 新字段的代码默认值等于旧行为（D0-1）。
> - 新建 `.cs` 后让 Unity 刷新出 `.meta`。
> - 测试失败不许改弱断言。
> - 开工前 `git status`，不碰别人未提交的改动。
> - **不提交**。
> - 只有一个编辑器，所以波次之间串行，同一时刻只有一单在编译或跑测试。
> - 每波派一单，波末标的档位适用于该波全部任务。
> - Inspector 字段一律 `[SerializeField] private` 加只读属性，不用 public 字段；`[Serializable]` 数据类（如 `BossDefinition`）同样如此。
> - 每 tick 和每帧的路径零分配（不用 LINQ，不 new 集合，不拼字符串），不 Find、不 GetComponent、不打 Log。
> - 新决策点按 `docs/telemetry.md` 埋点，用模块现有的 `ITelemetryScope`，事件名和必带字段见各单「埋点」行；同一事件不得每 tick 刷屏，只在状态变化时记。

## W0 检索（sonnet）

- [x] **T0.1** 只读 → 产出 `PRP/combat-iteration/w0-placement.md`。（model: sonnet）
  - 扫 `Tests/Showcase/**` 全部回放：列出每个模块在 SampleScene 里的走位常量、锚点和停留区域，坐标落到 x/z。
  - 叠加 SampleScene 现有的巡逻路线（主怪、巡逻者乙）、各触发区和 NPC 位置。
  - 给出巡夜人 2–3 套候选巡逻路线（各 2–4 个路点），要求：
    - 离所有回放路线、触发区、NPC 至少一个察觉半径（巡夜人橙区 6 + 余量）。
    - 地面可走。
  - 附「若选该路线，哪些回放模块离它最近」的名单。
- [x] **T0.2** 只读，并入同一文件，读出以下资产的现值：
  - `Data/Stealth/StealthConfig.asset` 的被抓口径与击倒口径。
  - `Data/Monster/MonsterConfig.asset`。
  - `Data/Identity/IdentityConfig.asset` 的露馅相关字段。
  - `Data/Battle/BossRosterConfig.asset` 的 `sample_boss` 行。
  - （model: sonnet）
- **主窗口验收 W0**：从候选路线里定下巡夜人坐标，写进 T5.1。

## W1 一场遭遇多种怪（D2，opus，单派一单）

- [x] **T1.1** `Runtime/Taming/TamingActor.cs`
  - 加 `[SerializeField] private int kindId`（默认 0 = 沿用主怪种类）和 `[SerializeField] private bool fullParticipant`（默认 false），配只读属性与 Tooltip。
  - 注释写明巡逻者乙依赖这两个默认值保持行为不变。
- [x] **T1.2** `Runtime/Monster/EncounterSceneView.cs`：读出 `patrolActors` 的 kindId 与 fullParticipant 两个数组，经 `ConfigureTaming` 传入。
- [x] **T1.3** `Runtime/Monster/MonsterRules.cs`：`CreateForActor(stableId, kindIdOverride = 0)`，非 0 时用覆盖的种类，不复制已解析的种类。
- [x] **T1.4** `Runtime/Monster/EncounterStep.cs`
  - `ConfigureTaming` 加可选的 kindIds 与 fullParticipant 参数；新增「是否完整参与者」查询，主怪恒为真。
  - 掉落闸门改为按 id 各一次，只对完整参与者生效。
  - 回放快照（`IReplayState`）与存档恢复改为**按稳定 id 对齐**：
    - 存档里没有的怪按场景初始状态开局。
    - 场景里没有的存档目标丢弃并记 Warn。
    - 结构有变就升 `ReplayFormat`。
- [x] **T1.5** `Runtime/Taming/TamingRules.cs`：`ValidateRestore` 改为按 id 校验，不再要求数量相等。
- [x] **T1.6** `Runtime/Monster/MonsterEncounterState.cs`：`BindExecution` 改用多目标重载，目标为主怪 + 完整参与者。
- [x] **T1.7** `Runtime/Stealth/ExecutionResolver.cs`：只有目标是主怪时才 `SettleBattle(Victory)`；处决附加怪只让它自己死亡并掉落。
- [x] **T1.8** 测试：
  - 新建 `Tests/EditMode/Monster/EncounterMultiKindTests.cs`，覆盖：
    - 不同种类各用各的数值、可否击杀、掉落。
    - 两个字段为默认值的附加怪行为与改动前一致，这是巡逻者乙的回归。
    - 掉落按 id 各一次。
    - 处决附加怪不结算整场。
  - 新建 `Tests/EditMode/Monster/EncounterRestoreAlignmentTests.cs`：存档或快照的目标数比场景多、少、一致三种情况。
  - `Tests/EditMode/Stealth/ExecutionResolverTests.cs` 补「非主怪不结算」。
- **埋点**（monster 模块）：
  - `encounter_restore_aligned {saved, scene, dropped, fresh}`：仅在数量不一致时记 Warn。
  - `encounter_target_drop {target, kind}`：掉落改按 id 后，补上目标 id 字段。
- **跑测试**：EditMode，限定 group 为以下四个，各自的理由：
  - `Game.Tests.EditMode.Monster`（本波主体）
  - `Game.Tests.EditMode.Stealth`（ExecutionResolver）
  - `Game.Tests.EditMode.Taming`（ValidateRestore）
  - `Game.Tests.EditMode.Replay`（快照格式）
- 档位：（model: opus）

## W2 露馅后果 + 被抓后果 + 玩家血量入口（D1、D4，opus，单派一单）

- [x] **T2.1** `Runtime/Identity/IdentitySettings.cs`
  - 加 `exposureOutcome`（None / Death / Chase，默认 None）。
  - 加 `identityLossCauses`（`ExposureCause` 位掩码，默认等于现有五种规则层原因）。
- [x] **T2.2** 新建 `Runtime/Identity/ConfiguredExposurePolicy.cs`：实现 `IExposureOutcomePolicy`，读 T2.1 的口径；文件头写明为何新建（见 prp D1）。
- [x] **T2.3** `Runtime/Identity/IdentityRules.cs`：`LosesIdentity` 按配置掩码判定；静态版保留并转调默认掩码。
- [x] **T2.4** `Runtime/Identity/IdentityInstaller.cs:58`：注入 `ConfiguredExposurePolicy`，替换 null；同步改那段注释。
- [x] **T2.5** `Runtime/Monster/MonsterRules.cs`
  - 新增公开的「被惊动」入口，内部走 `EnterHostile`。
  - 公开本 tick 的感知档位（红 / 橙 / 无）只读查询，零分配。
- [x] **T2.6** `Runtime/Monster/EncounterStep.cs`：加后置 `BindExposure(IdentityRules, IdentityLedger, SuspicionState)`。每 tick 在 `AdvanceTargets` 之后、`SettleStealth` 之前：
  - 没在借身份就跳过。
  - 汇总主怪与完整参与者的红 / 橙信号，调 `ResolveExposure`。
  - 结果为 Chase 或身份失效：惊动本 tick 察觉到玩家的完整参与者。
  - 结果为 Death：玩家生命置 0。
- [x] **T2.7** `Runtime/Monster/MonsterInstaller.cs`：构建回调里与 `BindIdentity` 同处 `TryResolve` 三件套，再调 `BindExposure`。
- [x] **T2.8** `Runtime/Monster/EncounterStep.cs`（`UseStealth` 与被抓判定点，约 :601-607）
  - 存下内核的 `ChaseOutcomeSettings`。
  - 被抓时用实时血量调 `ChaseCaughtRules.Resolve`：`IsFailure` 则生命置 0，`EntersKnockdown` 维持现状。
  - 被抓判据放宽为任一完整参与者。
- [x] **T2.9** `Runtime/Player/PlayerRules.cs`：新增只读 `MaxHealth`，以及夹在 [0, Max] 的「设定生命」入口，设定时清掉击倒状态。T2.6、T2.8 和 W3 都用它。
- [x] **T2.11** `Runtime/Stealth/ChaseCaughtRules.cs`：`ChaseOutcomeSettings` 允许 `failAfterCaughtCount = 0`，表示不按次数判死，负数仍抛。`Resolve` 在 0 时只看血量。
  - 同时检查 `StealthConfig` 与 `StealthConfigValidation` 对这个字段的校验，一并放开 0。
  - 注释写明出处：用户 2026-10-14 拍板「没血才判死」。
- [x] **T2.12** `Runtime/Monster/EncounterStep.cs` 与 `Runtime/Monster/MonsterRules.cs`，修读档后的两个缺陷（用户 2026-10-14 同意并进本波）：
  - 读档恢复（`Restore` / `Deserialize`）后主怪重新解析种类、重算视锥，与 `Begin` 走过的结果一致。
  - 恢复时已死的目标把掉落闸门直接标为已结算。
- [x] **T2.10** 测试：
  - 新建 `Tests/EditMode/Identity/ConfiguredExposurePolicyTests.cs`：三种口径。
  - `IdentityRulesTests.cs` 补失效掩码，默认五种、配置六种两组断言。
  - `IdentityInstallerTests.cs` 补「从真实容器解析出的规则已注入策略」（pitfalls :225）。
  - 新建 `Tests/EditMode/Monster/EncounterExposureTests.cs`：
    - 红区露馅后身份失效、察觉者转敌对。
    - 未借身份不触发（负对照）。
    - 默认字段的附加怪不产生信号。
    - Death 口径使生命归零。
  - 新建 `Tests/EditMode/Monster/EncounterChaseCaughtTests.cs`：
    - 口径 None 与 KnockdownThenDeath。
    - 完整参与者追上算被抓。
    - 剧情阶段内归零走 Downed。
  - `EncounterKernelWiringTests.cs` 补 `BindExposure` 与被抓 settings 的容器接线断言。
  - `Tests/EditMode/Player/PlayerRulesTests.cs` 补 `MaxHealth` 与设定生命（含清击倒）。
  - `StealthRulesTests.cs` 或 ChaseCaught 相关测试补「次数为 0 时第 N 次被抓仍只击倒，血量归零才判死」。
  - `EncounterRestoreAlignmentTests.cs` 补三条：读档后主怪种类与 killable 正确、视锥是种类配置值而不是 180°、已死目标读档后不再掉落。
- **埋点**：
  - monster 模块 `exposure_applied {cause, outcome, identity_lost, provoked}`：露馅结算那一刻记一条。
  - monster 模块 `chase_caught {policy, failure, health, count}`：被抓那一刻记一条。
  - identity 模块已有的 `identity_exit` / `identity_chase` 保留不动。
- **跑测试**：EditMode，限定 group 为以下五个，各自的理由：
  - `Game.Tests.EditMode.Identity`（策略与掩码）
  - `Game.Tests.EditMode.Monster`（接线与每 tick 判定）
  - `Game.Tests.EditMode.Stealth`（被抓口径）
  - `Game.Tests.EditMode.Player`（血量入口）
  - `Game.Tests.EditMode.Narrative`（Narrative 读身份事实，回归）
- 档位：（model: opus）

## W3 实时遭遇升级回合制（D3、D5，opus，单派一单）

- [x] **T3.1** 新建契约，文件头写明为何新建（见 prp D5）：
  - `Runtime/Monster/IEncounterEscalationPort.cs`：「该种类能否升级」「发起升级」两个方法。
  - `Runtime/Monster/EncounterEscalationRequest.cs`：只读 struct，字段为目标 id、种类 id、触发情形、玩家当前生命与上限。
  - `Runtime/Monster/EncounterEscalationTrigger.cs`：枚举，三种触发情形。
- [x] **T3.2** `Runtime/Monster/MonsterConfig.cs`：加三个升级触发开关，代码默认全关。需要嵌套 Settings 时按一文件一类拆文件。
- [x] **T3.3** `Runtime/Taming/TamingRules.cs`：`AdvanceTargets` 加可选的「接管这一下」回调。回调返回 true 时不调 `player.ApplyDamage`；默认 null 时行为不变。
- [x] **T3.4** `Runtime/Monster/EncounterStep.cs`
  - 加后置 `BindEscalation(IEncounterEscalationPort)`。
  - 两个拦截点：玩家普攻 `ApplyDamage` 之前；经 T3.3 回调拦怪物攻击。升级条件按 prp D5 全部满足。
  - 「升级在途」状态：tick 早退，公开 `IsEscalating`，不进快照。
  - 公开的「完成升级」入口：
    - 胜：目标强制死亡，不看 killable；按 id 掉落一次；按比例写回血量，至少 1。
    - 负：生命置 0。
    - 只接受一次。
- [x] **T3.5** `Runtime/Session/SessionStateAdapter.cs:82`：`BattleResultPending` 并入 `step.IsEscalating`。
- [x] **T3.6** `Runtime/Battle/BossDefinition.cs` 与 `Runtime/Battle/BossRoster.cs`
  - `BossDefinition` 加 `monsterKindId`，默认 0。
  - `BossRoster` 加「按种类查询」，校验非 0 的种类 id 不重复。
- [x] **T3.7** `Runtime/Battle/BattleSetup.cs`
  - 触发情形到 `BattleEntryRequest` 的翻译，按 prp D5 的表。
  - `TryCreate` 加可选的玩家当前生命与上限；进战斗时取 `ceil(比例 × 战斗上限)`，至少 1。
- [x] **T3.8** `Runtime/Battle/BattleFlow.cs`
  - `RunAsync` 抽出与剧情无关的内核：剧情版逐字不变；遭遇版不调剧情的三个方法，在幕下回调「完成升级」，并带回剩余生命比例。
  - 实现 `IEncounterEscalationPort`，已有仗在途或规则拒绝时返回拒绝。
- [x] **T3.9** `Runtime/Battle/BattleInstaller.cs`：构建回调里 `step.BindEscalation(flow)`，与 `BindEntryContext` 同处。
- [x] **T3.10** 测试：
  - 新建 `Tests/EditMode/Monster/EncounterEscalationTests.cs`：
    - 三种触发各自产出对应请求，且这一下伤害被吞掉。
    - 负对照：未配数据、开关关、背后处决、驯服操控中、已在途或结果待消费、附加怪字段为默认值。
    - 完成升级：胜时 killable=false 也死、掉落一次、主怪判 Victory 并由桥回写；负时归零；重复调用无效。
  - 新建 `Tests/EditMode/Battle/BattleFlowEncounterTests.cs`：
    - 遭遇版不触碰剧情。
    - 已有仗在途时拒绝。
    - 回调恰好一次。
    - 三种开战方式的先手与开局扣血。
  - `BattleSetupTests.cs` 补翻译表与血量取整边界：1/3、满血、残血。
  - `BossRosterTests.cs` 补按种类查询与重复拒绝。
  - 新建 `Tests/EditMode/Session/SessionStateAdapterTests.cs`：断言升级在途时 `BattleResultPending` 为真、结束后为假。若构造 adapter 所需的夹具在 `GameSessionTests.cs` 里已有，复用它，不要另起一套假服务。
  - 容器接线测试补「`BattleInstaller` 已绑定升级端口」。
- **埋点**：
  - monster 模块 `escalation_requested {target, kind, trigger, accepted, reject}`：拒绝时带原因，覆盖未配数据、开关关、在途、规则拒绝、已有仗在途。
  - monster 模块 `escalation_completed {target, victory, hp_before, hp_after}`。
  - battle 模块：沿用 `battle_entry`，补 `source=encounter` 字段，区分剧情版与遭遇版。
- **跑测试**：EditMode，限定 group 为以下六个，各自的理由：
  - `Game.Tests.EditMode.Monster`（拦截与结算）
  - `Game.Tests.EditMode.Battle`（BattleFlow 重构与剧情版回归）
  - `Game.Tests.EditMode.TurnBased`（开战规则被新翻译层调用）
  - `Game.Tests.EditMode.Session`（存档闸）
  - `Game.Tests.EditMode.Taming`（AdvanceTargets 回调）
  - `Game.Tests.EditMode.Narrative`（剧情版回写不变）
- 档位：（model: opus）

## W4 BOSS 主动发难（D6，opus，单派一单）

- [x] **T4.1** `Runtime/Battle/BossEntryContext.cs`：开战理由改为可设置，默认「玩家先动手」，提供复位方法。
- [x] **T4.2** `Runtime/Battle/BossDefinition.cs`：加 `aggroEnabled`（默认 false）与冲刺速度（占位）。
- [x] **T4.3** 新建 `Runtime/Battle/BossAggro.cs`（MonoBehaviour），文件头写明为何新建（见 prp D6）。
  - 状态：待机 / 冲刺 / 接触 / 战后回位 / 解除武装。
  - 守卫：对话、演出、战斗、世界暂停、剧情忙、已击败。
  - 接触时置开战理由，再调 `NarrativeTrigger.InteractAsync`；战后复位开战理由；撤退后幕下回原位。
  - 离开察觉范围后重新武装。
  - 移动做渲染插值；Update 里零 Find、零分配、零日志。
- [x] **T4.4** 绑定：Battle 侧在玩法场景加载时扫描 `BossAggro` 并注入服务。
  - 先定位 `NarrativeService.cs:93` 用的场景加载时机，复用同一时机。
  - 优先扩展 `BattleInstaller` 或 `BattleFlow`，新建文件要在文件头说明理由。
  - 不在构建回调里找（pitfalls :578）。
- [x] **T4.5** 测试：
  - 新建 `Tests/EditMode/Battle/BossAggroTests.cs`：
    - 状态迁移。
    - 每个守卫各一条负对照。
    - 重新武装：未离开不再发难，离开再进来会发难。
    - 接触只触发一次交互。
    - 开战理由置位与复位。
  - `BossEntryContextTests.cs` 补「可设置、默认值不变」。
  - 接线测试：场景加载后组件已绑定服务。
- **埋点**（battle 模块）：
  - `boss_aggro_state {boss, from, to, reason}`：只在状态迁移时记。
  - `boss_aggro_blocked {boss, guard}`：守卫第一次挡住时记一条，同一守卫持续挡住不重复记。
- **跑测试**：EditMode，group 限定 `Game.Tests.EditMode.Battle`（本波只动 Battle）。
- 档位：（model: opus）

## W5 场景与资产（D7，sonnet，按步骤走 Unity MCP）

> 开工前先读 `.claude/skills/unity-mcp/SKILL.md` 与 `.claude/rules/unity-assets.md`。不许启用 `Encounter/TamingDemo` 旧挂点（taming-module-guide :32）。
> MCP 没连上或编辑器没开时，不手改 `.unity` / `.asset` 文本，把本波改成逐步操作清单交给用户在编辑器里做。

- [x] **T5.1** `Assets/Scenes/SampleScene.unity`
  - 新增巡夜人：结构复制现有附加巡逻者，**不改巡逻者乙本身**。
  - `TamingActor` 填：stableId `patrol_xunyeren`、displayName「巡夜人」、`kindId=1001`、`fullParticipant=true`。
  - `patrolPoints` 用 W0 推荐的东南角路线 A：(x,z) = (29,-10) → (32,-10) → (32,-6)，落在 `Ground_East` 上。依据见 `w0-placement.md`；坐标是 x/z，z 对应 2D 逻辑的 y（`EncounterSceneView.cs:617`）。
  - 追加进 `EncounterSceneView.patrolActors` 末尾。
  - `SaveScene` 后 grep 校验 guid 与字段值。
- [x] **T5.2** `SampleScene.unity`：`Npc_SampleBoss` 挂 `BossAggro`，按 W4 交付的字段填（如 bossId=`sample_boss`），`SaveScene` 后校验。
- [x] **T5.3** 资产（MCP 改 SO，存盘后 grep 校验）：
  - `Data/Identity/IdentityConfig.asset`：`exposureOutcome=Chase`，`identityLossCauses=六种全含`。
  - `Data/Stealth/StealthConfig.asset`：被抓口径 `KnockdownThenDeath`（现值已经是 5），`chaseCaughtFailCount=0`（现值 2，见 T2.11）。
  - `Data/Monster/MonsterConfig.asset`：三个升级开关全开。
  - `Data/Battle/BossRosterConfig.asset`：
    - 新增 `monster_1003` 行：`monsterKindId=1003`、显示名「市令」、生命占位 6、`outOfBattleDrunk=0`、`playerHealth=10`、`stagePrefab=BattleBoss_Placeholder`。
    - `sample_boss` 行：`aggroEnabled=true`、冲刺速度占位 3.0。
- 档位：（model: sonnet）

## W6 回放与场景接线测试（opus，单派一单）

- [x] **T6.1** `Tests/Showcase/Identity/IdentityShowcase.cs`
  - 原用例 `EnterIdentity_StopsEnemyAttack_ExitRestoresIt` 的走位改到橙区，断言语义不变。
  - 新增 `Exposure_RedZone_LosesIdentityAndMonsterChases`。
- [x] **T6.2** `Tests/Showcase/Monster/MonsterShowcase.cs`，新增：
  - `Escalation_FrontalAttack_PlayerFirst`
  - `Escalation_MonsterStrikes_Ambushed`
  - `Escalation_UnawareStrike_SneakAttack`
  - `Caught_Xunye_KnockdownThenDowned`
  - 每条都要检查开战方式、先手、开局扣血、战后结局和血量写回。
- [x] **T6.3** `Tests/Showcase/Stealth/StealthShowcase.cs`：新增 `ExecuteKey_OnXunye_DropsWithoutEndingEncounter`。
- [x] **T6.4** `Tests/Showcase/Battle/BattleShowcase.cs`
  - 新增 `BossAggro_ChargesTauntsAndAmbushes`、`BossAggro_RetreatThenRearmsAfterLeaving`，以及 `BossAggro_ChallengeWithinReaction_FrontalAttack`。
  - 「离开」的判据是同时离开察觉范围（6 米）和发难半径（5 米），只退出红区不够。
  - **原四条**（主窗口 2026-10-15 定）：BOSS 开了发难，带 5 米发难半径和 3 秒反应时间，原用例「走近再按 E」必须在 3 秒反应时间内完成。改法是把「进半径 → 走到站位 → 按 E」压进同一步内连续完成，检查点放在按 E 之后，断言语义不变（仍是正面攻击、玩家先手）。
  - 不为回放在运行时代码里加关掉发难的测试口。标准节奏下压不进 3 秒就如实报告，不要硬凑。
- [x] **T6.6** `Tests/Showcase/Taming/TamingShowcase.cs:52`（协作者 linch 的文件，用户 2026-10-14 授权**只改这一行**）：
  - 把 `Is.EqualTo(2)` 改为「至少 2 只，且第 0、1 位的 stableId 是原来的主怪与巡逻者乙」。
  - 该文件其余内容一律不动，在回复里单列这一处 diff。
- [x] **T6.7** `Tests/Showcase/Stealth/StealthShowcase.cs` 的 `PatrollersPausedLongEnough`：改为只等「离绕背路线一个察觉半径以内」的巡逻者停稳。离得远的巡夜人不参与相位对齐，否则窗口可能永远凑不齐（W0 风险 1）。
- [x] **T6.5** 新建 `Tests/EditMode/Monster/SampleSceneCombatWiringTests.cs`，读场景断言：
  - 巡夜人存在且两个字段正确。
  - 巡逻者乙两个字段仍为默认值。
  - `Npc_SampleBoss` 挂了 `BossAggro`。
  - 四份资产的口径值正确。
- 档位：（model: opus）

## W7 验证（sonnet）

- [x] **T7.1** EditMode 跑整个 `Game.Tests.EditMode` 程序集（PRD F 要求全量），汇报总数与失败用例。
- [x] **T7.2** 回放逐模块跑，每个模块的理由如下：
  - **Identity**：露馅新用例，原用例改了走位。
  - **Stealth**：处决改多目标、只认主怪结算、新怪处决用例。
  - **Monster**：多种类、被抓、三种升级。
  - **Battle**：BOSS 发难，以及 BattleFlow 重构后的原四条回归。
  - **〈W0 名单〉**：离巡夜人路线最近的模块，主窗口在 W0 验收后填写，每个附距离理由。
  - 不跑「全部」；`PerformanceEvidence` 不跑。
- [x] **T7.3** `python .claude/skills/evolution/gc_scan.py`，再对本次改动的 `.cs` 跑 project-lint。
- [x] **T7.4** 派 `code-reviewer`（frontmatter 固定 sonnet）审本次全部改动，产出 BLOCK / WARN / INFO。
  - 审查结论 **PASS，无阻断项**；警告项由收尾单逐条落地（见 W8 前的「审查收尾」一节），验证见 `HANDOVER.md` 顶部 2026-10-16/17 那行。
- 档位：（model: sonnet）

## 审查收尾（2026-10-16/17，主窗口单派一单）

审查的 7 条警告全部落地（每条都带判据与出处，实测记录见 `HANDOVER.md`）：

- [x] **C1 被抓口径的代码默认值改成「无后果」**：`StealthConfig.chaseCaughtFailCount` 由占位 `2` 改 `0`
      （占位 2 会让任何新建的配置继承被推翻的口径）；`ChaseOutcomeSettings` 的注释同步；新增用例钉住代码默认值
      （`StealthRulesTests.CodeDefaults_DoNotFailByCaughtCount`），与钉资产现值的 `SampleSceneCombatWiringTests` 配成一对。
- [x] **C2 回放期间关掉升级**：`EncounterStep.BindReplay(ReplayPlayer)` + `IsReplaying`（判 `IsActive`），
      `MonsterInstaller` 构建回调接线；回放接管时清掉实时玩法留下的「在途」并埋 `escalation_abandoned`。
- [x] **C3 读回放快照改「先校验、后修改」**：`MonsterRules` 拆 `ReadSerialized` / `ApplySerialized`（`SkipSerialized` 复用前者，
      字节布局只剩一处）、`MonsterModel.CopyFrom`、`TamingRules.ValidateReplayFlags` 单列、`EncounterStep.Deserialize` 改暂存区。
- [x] **C4 血量换算出场改四舍五入**：新增 `EncounterEscalationRequest.ScaleHealthBack`（进场仍 `ScaleHealth` 的 ceil），
      1/3 → 4/10 → 原样打完回到 1/3；`MonsterShowcase` 的写回断言跟着改口径。
- [x] **C5 补两处测试**：`Tests/EditMode/Battle/BossAggroSceneBinderTests.cs`（5 条，真场景扫描）+
      `EncounterEscalationTests` 的回放四条（含「没在放仍升级」负对照）。
- [x] **C6 修过期 / 不符注释**：`BossEntryContext` 的 BOSS 旧坐标（标注为「当时量到的现场值」）、
      `EncounterBattleBridge` 文件头漂掉的行号、`MonsterShowcase` 的口径说明。
- [x] **C7 两条遗留项记进 HANDOVER**：`EncounterStep` 体积待拆、「遭遇结算时序」在第二个登记方出现时的隐患（§1.3）。
- **验证**：全量 EditMode **2703 / 2703**（2026-10-17 00:23，锚点 `Game.Tests.EditMode.dll` 新于全部源码）；
  `Showcase.Battle` **6 / 6 PASS**（`Logs/verify/battle/20261016-235652`）、`Showcase.Monster` **8 / 8 PASS**（`Logs/verify/monster/20261017-001349`）；
  本波改动的 `.cs` 跑 project-lint 零违规。**已知偶发**：全量跑里 `UIServiceTests.OpenAsync_WhenOnOpenAsyncThrows_LeavesNoUnobservedTaskException`
  会因前后用例留下的 `GameInput.Gameplay.Disable()` 未调用告警而红（单跑该组 19/19 通过），与 combat-iteration 无关。

## W8 文档收尾（D8，sonnet；写手每条现状描述都要附 HEAD 源码 path:line）

> ⚠️ 2026-10-14 执行开始时，工作区里有别处带进来的策划更新：`features-spotlight/06`、`09` 与 `spotlight/07` 及其 xlsx 新增了阶段九 BOSS，尚未提交。
> W8 开工先 `git status`：若仍未提交，只改 06/09 的第 7 节，不动别人的段落；提交时按 hunk 只交自己的部分。

- [x] **T8.1** `/generate-doc sync`，模块：identity、stealth、monster、taming、battle、session、player。（2026-10-17 完成）
  - battle：新增「BOSS 主动发难」一节、第二条入口（遭遇版）的链路图与收场时序、`BossDefinition` 表的扩展字段、
    `BattleFlow` 的升级端口成员、埋点清单补齐、回放入口改成六条；`battle-external-api` 改成「两条开战正路」并补 `IBattleEntryContext` 一节。
  - monster：新增「实时遭遇升级回合制」附录（含回放关升级）、回放状态补「按 id 对齐 / 先校验后修改 / 在途不进快照」、
    Showcase 从四条改八条；`monster-external-api` 补五个升级口。
  - identity：新增「后果口径与哪些原因让身份失效」一节（两张配置表 + 行为变化 + 埋点）、`ConfiguredExposurePolicy` 进类型表、Showcase 改两条。
  - stealth：新增「被抓的后果」一节（口径 + 次数两张表、`caughtCountSoFar` 的真实语义、生产调用点）、Showcase 与配置现值。
  - session：闸门那条从 `EncounterStep.PendingResult` 改指 `SessionStateAdapter.BattleResultPending`，并写清它收两个来源。
  - taming：`TamingActor` 两个新字段（kindId / fullParticipant）与「一场遭遇多种怪」一节、`TamingShowcase` 那处授权改动、T 选中最近者的已知欠账。
  - player：`MaxHealth` / `SetHealth` 的契约与调用方。
- [x] **T8.2** 修正过期描述：（2026-10-17 完成）
  - `docs/design/features-spotlight/` 02/03/04/06/09 的第 7 节：02/03/04/06 各加一段「2026-10-17 更正」并列出**已关闭的缺口**（遮挡贯通、处决、击倒、掉落、多怪、身份模块与露馅后果、被抓口径、固定追逐）；09 在「按 BOSS 覆盖开战效果」那条下补 `monsterKindId` 与发难五项。
  - `HANDOVER.md`：顶部加 2026-10-16/17 combat-iteration 那行（2703/2703、Battle 6/6、Monster 8/8、文档已 sync）；§1.3 记两条遗留项。
  - `docs/roadmap.md`：C5、S2、S3、S4、S7 行回填本轮状态。
- [x] **T8.3** `docs/planning/视觉验收清单.md`：（2026-10-17 完成）
  - 第 6 节原本写「还没接进场景」已过期，改成「已经接进场景了」并列出各模块的回放命令与最近一次记录。
  - 新增四条（第 27–30 条）：回合制 BOSS 战、遭遇升级回合制、BOSS 主动发难、被抓击倒，每条写明在 demo 里怎么看、该看到什么。
- 顺带修掉：`ai-docs/docs/modules/` 里 16 处失效行号引用（battle / session / turnbased 三份，`invariants.py` 复扫为 0）；
  清掉 126 个 `InitTestScene*` 残留（`gc_scan` 从 143 降到只剩字体资产一项）。
- 档位：（model: sonnet）

## 收口（主窗口）

- 逐波验收产物；全部完成后 `/review-change` 列清单，**停下等审，不提交**。

## 验收覆盖对照（PRD → 任务）

| PRD 验收 | 任务 |
| --- | --- |
| A1 露馅：身份失效 + 察觉者起追 | T2.3、T2.5、T2.6、T2.10 |
| A2 后果三口径可配 | T2.1、T2.2、T2.4、T2.10、T5.3 |
| A3 未借身份负对照 | T2.6、T2.10 |
| A4 回放：红区露馅起追 | T6.1、T7.2 |
| B1 未配数据的怪追上 → 击倒、不结算 | T2.8、T2.10 |
| B2 击倒中归零 → Downed / 剧情 Downed | T2.8、T2.9、T2.10 |
| B3 被抓口径可切换 | T2.8、T5.3、T2.10 |
| B4 回放：巡夜人追上击倒 | T6.2、T7.2 |
| C1 三种触发 → 三种开战方式 | T3.4、T3.7、T3.10 |
| C2 负对照：未配战斗数据 | T3.10 |
| C3 背后处决不进回合制 | T3.4、T3.10 |
| C4 三个触发开关 | T3.2、T3.10、T5.3 |
| C5 打赢视同击杀、掉落一次、剧情 Victory | T3.4、T3.10 |
| C6 打输等同实时归零 | T3.4、T3.10 |
| C7 只结算一次 | T3.4、T3.8、T3.10 |
| C8 暂停、不可存档、位置不变 | T3.5、T3.8、T3.10 |
| C9 血量互通 | T2.9、T3.4、T3.7、T3.8、T3.10 |
| C10 回放：三种升级 | T6.2、T7.2 |
| D1 红区 → 冲刺 → 嘲讽 → 被打 | T4.1、T4.3、T4.5、T3.7 |
| D2 按 E 仍是正面攻击（回归） | T4.1、T4.5、T6.4 |
| D3 撤退回原位、离开后重新武装 | T4.3、T4.5、T6.4 |
| D4 守卫 | T4.3、T4.5 |
| D5 回放：BOSS 发难 | T6.4、T7.2 |
| E1 不同种类各用各的数据 | T1.3、T1.4、T1.8 |
| E2 巡逻者乙不回退 | T1.1、T1.8、T6.5、T7.1 |
| E3 demo 新怪且不压回放路线 | T0.1、T5.1、T6.5 |
| E4 受影响回放复跑 PASS | T7.2 |
| F1 过期描述修正 | T8.2、T8.3 |
| F2 视觉验收新增四条 | T8.3 |
| F3 文档三件套同步 | T8.1 |
| F4 全量 EditMode、lint、gc_scan | T7.1、T7.3 |
