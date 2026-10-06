---
type: module-guide
module: identity
layer: runtime
maturity: seed
---

# Identity 模块指南

S1「换皮与附身」与 S2「身份暴露与怀疑」共用的**纯规则内核**：管「玩家当前是什么身份」这一状态，
以及它怎么露馅、怎么记账、怎么被剧情读到。**不碰场景、不碰 UI、不碰 Taming**。

聚光灯里「换皮」是总称，包含附身与使用皮 / 面具（`docs/design/features-spotlight/00_功能总览.md:218`）；
本模块只提供**身份状态机与判定**，具体「从谁身上借、怎么借、借了之后被附身者怎样」由接线侧与 S1 的 PRD 决定。

## 职责边界

| 类型 | 职责 |
| --- | --- |
| `IdentityId` | 身份 id 强类型：小写 / 数字 / 下划线，**禁点号**（id 会拼进事实键 `identity.<id>`，点号会破坏键的段结构） |
| `IdentityOrigin` | 借用来源（附身 / 皮 / 面具 / 丹），决定写 `identity.skin` 还是 `identity.mask` |
| `IdentityDefinition` | 身份定义：来自谁、显示名、来源、能力键、通行权限键 |
| `IdentityCatalog` | 定义表：注册与查询；**拒收与固定事实键撞名的 id**（`borrowed`/`skin`/`mask`/`memory`/`exposed`/`exposedCount`/`suspicion`/`ledger`/`dead`） |
| `IdentitySettings` | 全部数值（时限、冷却、账簿上限与口径、怀疑度上限/回落开关/速率、露馅惩罚、档位阈值），每个字段注释写明出处与待拍板编号 |
| `IdentityConfig` | ScriptableObject：数值块 + 定义数组，`CreateCatalog()` 建表 |
| `IdentityState` | 当前身份、剩余时限、冷却、**是否「生效中」**、露馅计数、记忆、死亡；`Capture` / `Restore` |
| `IdentityRules` | 纯规则：借 / 退 / 时限结算、**六种露馅判定**、后果分派（策略注入）、存档读写、埋点 |
| `IdentityLedger` | 账簿：两种计数口径 + 超量判定（阈值外部注入，`≤0` 表示关闭） |
| `SuspicionState` | 怀疑度：只增 / 可回落由配置开关选，上限封顶，档位 + 埋点 |
| `ExposureCause` / `ExposureSignals` | 露馅判定的输入（六位 `[Flags]` 原因 / 原始事实） |
| `ExposureOutcome` / `ExposureResolution` / `IExposureOutcomePolicy` | 后果的**可注入策略**：`ResolveExposure` 只回答「该发生什么」，走什么流程由接线侧定 |
| `IdentitySaveData` | `ISaveData` 存档分区（版本 1）：只存原始量，**不存任何 `identity.*` 事实键** |
| `IdentityAttackRules` | 集成接口①：敌人攻击许可（旧 `DisguiseRules` 语义逐字保留，身份作**新增层**） |
| `IdentityFacts` / `IdentityFactSnapshot` | 集成接口②：字典键常量 + 查询期投影（`CollectTrueKeys` / `IsTrue`） |

**关键区分**：`IdentityState.IsBorrowing`（正持有借来的身份）与 `IsInEffect`（该身份当前生效）
不是同一件事——皮 / 面具有时限，持有不等于生效。攻击许可与露馅判定都只看后者。

## 依赖方向

只依赖 `Game.Core`（存档、遥测）与 `Game.Disguise` 的既有静态规则（`IdentityAttackRules` 直接调用
`DisguiseRules.AllowsEnemyAttack`，**不改 Disguise 一个字**）。不依赖 Monster / Player / Narrative / Dialogue。
反向由那些模块调本模块的公开接口。

## 事实键（跨模块状态的唯一出口）

对外只经 `Fact.StoryFlag` + 点分小写键，**不给 `EncounterContext.Fact` 枚举加值**。
键名与写入纪律见 [`ai-docs/docs/story-facts.md`](../../story-facts.md) §4.1。本模块写这 10 组：

`identity.borrowed` · `identity.<id>` · `identity.skin` · `identity.mask` · `identity.memory` ·
`identity.exposed` · `identity.exposedCount.low|mid|high` · `identity.suspicion.low|mid|high` ·
`identity.ledger.over` · `identity.dead`

**三条硬纪律**：
1. **阈值不进表**：档位由 `IdentitySettings` 的阈值 + `FactTierMath.TierOf` 算出后写成布尔键；表里只写
   `identity.suspicion.high` 这种谓词。策划改阈值改配置，不改表。
2. **档位三互斥**：同一时刻只有一个档位为真；写之前先用 `IdentityFacts.CollectTierKeys` 清掉旧档
   （`IdentityFactSnapshot.Emit` 有这个行为，有测试守）。
3. **瞬时态不入档**：`IdentitySaveData` 只存原始量；事实键是**查询期投影**，不进存档。

## 六种露馅

逐条落点与真源（`docs/design/features-spotlight/02_身份暴露与怀疑.md`）：

| 露馅 | 判定要点 | 真源 |
| --- | --- | --- |
| ① 人物特性 | 身份生效中 + 禁区限制的正是当前身份 + 被看见 | `:96-97`（R4/R5） |
| ② 身份核验关口 | 走到需要核验的关口 | `:107-110`（R12–R15） |
| ③ 账簿 | 用过的身份**超量** | `:114-118`（R16–R20） |
| ④ 怀疑度 | 怀疑度到顶 | `:122-127`（R21–R26） |
| ⑤ 揭露 | 被揭露（**蜃师面具可挡一次**） | `:131-134`（R27–R30） |
| ⑥ 警戒值 | 红区一律；橙区只在**身份不生效**时 | `:142-145`（R32–R35） |

**不做的事**：不裁「被发现之后是死亡还是追逐」——`00_功能总览.md` §5 **C1** 是原文矛盾，
所以后果走 `IExposureOutcomePolicy` 策略注入；**不注入策略时 `outcome` 恒为 `None`**（不硬编一种）。

## 已知约束

- **不注册 `IReplayState`**：身份本波没接进任何 tick 路径，注册只会让快照布局变长；按
  `Core/Replay/ReplayFormat.cs` 的判据④，注册或改已注册状态的字段就必须升版并同步回放测试。
  接线波要一次做完这件事（`CurrentFormatVersion` 4 → 5）。
- **`Taming` 一个字没动**：`taming-module-guide.md:11` 写明「按用户要求暂不接入 SampleScene/Boot」，
  在附身语义定下来前不许动它。
- 数值多为**占位**，逐条挂在 `00_功能总览.md` §8.1 的编号上（见 `IdentitySettings` 字段注释与
  `S组落地总规划.md` 第 4 节）。

## 待接线的位置（本模块只提供接口，不接线）

| 接什么 | 落点 | 备注 |
| --- | --- | --- |
| 敌人攻击许可 | `MonsterRules` 的攻击许可判定 | 把 `IsInEffect` 作为**新增层**喂给 `IdentityAttackRules`；给 `MonsterIntent` 加可选字段，**不动 `MonsterRules` 构造签名** |
| 剧情条件 | `NarrativeConditionSource.Snapshot` | 用现成 `EncounterContext.WithStoryFlags(...)`，**不加 `Fact` 枚举值**；复用缓冲避免每次分配 |
| 读档恢复 | `GameSession` / `ISessionStateSource` | `IdentitySaveData` 已就位，等接线波把服务接进生命周期 |

## 验证入口

- EditMode：`Assets/_Project/Scripts/Tests/EditMode/Identity/`（6 个测试类，**52 条**）
  `IdentityRulesTests`（六种露馅各一条、非当前身份不触发、时限归零自动退出、无效/未知 id、策略注入、埋点）、
  `IdentityLedgerTests`、`SuspicionStateTests`、`IdentityFactSnapshotTests`（字典对齐 + 档位互斥）、
  `IdentitySaveDataTests`（含经 `JsonSaveService` 真落盘往返）、`IdentityTestContent`（共用表）。
- **Showcase（本波新增）**：`Assets/_Project/Scripts/Tests/Showcase/Identity/IdentityShowcase.cs`
  —— 1 条用例 `EnterIdentity_StopsEnemyAttack_ExitRestoresIt`、5 步、5 个检查点。
  - **回放舞台**：`Assets/Scenes/SampleScene.unity`，走 Boot 真实流程（`ScenePath => null` + `EnterWorldFromTitle`）：
    标题「开始」→ 进世界 → 推摇杆沿北侧路线绕开村口触发区，走到巡逻线北侧观察点 → 走到场景里那只巡逻怪（`enerme`）正前方。
  - **接线走真实容器**：`IdentityRules` / `IdentityState` / `IdentityLedger` 全部 `ResolveService` 拿，
    与 `MonsterInstaller` 构建回调 `BindIdentity` 给 `EncounterStep` 的是**同一只** `IdentityState`；
    回放里没有手工 `BindIdentity`、没有自己 new 规则（取不到就直接 `Assert.Fail`）。
  - **看得见什么**：怪物转敌对（状态色红）→ 本体先挨一下 → 借入「都统面具」→ 贴着它站 2.5 秒**一滴血不掉** →
    主动退出身份 → 下一 tick 立刻恢复挨打。截图 `01-身份生效中，怪物不出手.png`、`02-身份失效后恢复挨打.png`。
  - **检查点**：怪物转敌对且身份未生效 / 本体挨打（生命 −1）/ `IsInEffect` 且 `CurrentOrigin == Mask` /
    生效期间生命不下降且怪物仍敌对 / `TryExit` 后立刻恢复挨打且玩家仍活着。
  - **表现缺口（要看回放的人先知道）**：身份本身在画面上**没有表现**——`EncounterSceneView.LateUpdate`
    只按 `IsDisguised`（绿）与 `IsSneaking`（青）染色，没有身份那一层。所以「身份生效中」只能靠回放叠加层文字
    与「不掉血」判读；HUD 显示「我现在是谁」是 `02_身份暴露与怀疑.md` 没写的待定项（`01_换皮与附身.md:109`）。
  - **两条失效路径的现状**：本波只能演**主动退出**（`IdentityRules.TryExit`，口径 [推断]，出处 `01_换皮与附身.md:165` R26 [待定]）。
    **时限到期演不了**：`IdentityRules.AdvanceIdentity` 在 `Runtime/` 下一个调用方都没有（没有 tick 路径），
    时限永远不会自己走完——这是接线缺口，记在 Showcase 交付报告的「建议补丁」，回放**不替它兜底**。
- 配置资产：`Assets/_Project/Data/Identity/IdentityConfig.asset`。
  - `definitions` 本波补了 2 条**占位**定义（补之前是空数组 = 任何身份都借不到，Showcase 无从演示）：
    | id | 显示名 | 来源 | 通行权限键 | 出处 |
    | --- | --- | --- | --- | --- |
    | `dutong` | 都统 | `Mask`（面具） | `patrol_blend` | `01_换皮与附身.md:85`（sp03 阶段九 B 都统「击杀后获得都统面具，可隐匿在巡逻队中」）、`:152` R19 |
    | `zhishi_pi` | 执事皮 | `Pelt`（皮） | `fenfu_shrine` | `01_换皮与附身.md:81`（暗杀后获得）、`:82`（生效中可免去追逐战）、`:166` R27（有「生效中」时段） |
  - **占位纪律**：这两条只填了能演回放的最小内容（能力数组留空），数值仍走 `IdentitySettings` 的占位值
    （`defaultDurationSeconds = 0` = 无时限，与「面具持有即生效」自洽）。**等 `00_功能总览.md` §8.1 #2
    拍板**后再按策划内容定稿；`IdentityInstallerTests` 只断言「表按资产的 definitions 建」，补内容不会红。

