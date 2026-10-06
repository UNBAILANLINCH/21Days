# 剧情事实字典（跨模块状态唯一真源）

> **状态：定稿（2026-10-07，S 组落地波建立）**
> **本文回答**：S1–S8 各模块的跨模块状态（当前身份、暴露、怀疑度、水位、醉酒、账簿、牙……）**叫什么名字、谁写、什么时候清、怎么被剧情和对白读到**。
> **本文不是什么**：不是玩法设计。规则与数值以 `docs/design/features-spotlight/` 01–13 为准，本文只定**状态在工程里的表达与命名**。
> **配套**：[`S组落地总规划.md`](../../docs/planning/S组落地总规划.md)（波次与文件所有权）、[`stage-structure-spec.md`](stage-structure-spec.md)（阶段结构）、模块三件套 `modules/`。

---

## 1·为什么需要这份文档

聚光灯的机制几乎全是**跨阶段、跨模块**的：用过的身份要进账簿（阶段3 查）、水位要影响阶段3、牙要从阶段3 带到阶段六和阶段九、空白皮 + 泾龙丹要在阶段九合成泾龙面具（`00_功能总览.md` §3.2 的依赖图）。

如果每个模块自己起名、自己存，会出现三种坏结果：

1. **同名不同物**：镜像旧版踩过的坑——旧版「两界账」与聚光灯「账簿」同名不同物（`00_功能总览.md` §4.2）。
2. **拼错静默失效**：现状 `NarrativeCatalog.ValidateFacts` 对 `StoryFlag` 只校验「键非空」，**键名写错不会报任何错**，条件永远为假，表现为「这段剧情就是不触发」，极难查。
3. **存档不一致**：状态散在各分区，读档后一部分恢复、一部分没恢复。

所以先定字典，再写实现。

---

## 2·现有事实机制（读源码得到的实况）

`Assets/_Project/Scripts/Runtime/Narrative/EncounterContext.cs`：

| 项 | 实况 | 行号 |
| --- | --- | --- |
| 事实种类 | `enum Fact { PlayerAlive, PlayerSneaking, PlayerDisguised, TargetAlive, TargetHostile, TargetDetected, StoryFlag }` | `:9` |
| 读取 | `bool Read(Fact fact, string key)` —— **只有布尔** | `:40-55` |
| 任意命名状态 | 只走 `Fact.StoryFlag`，`flags.Contains(key)` | `:50-52` |
| 快照性质 | 不可变快照，`WithStoryFlags` 产出新实例 | `:33-39` |
| 数据来源 | `NarrativeConditionSource.Snapshot(targetId)` 每次现取 | `NarrativeConditionSource.cs:91` |
| 持久化 | `NarrativeSaveData.StoryFlags` 是 `HashSet<string>`（`StringComparer.Ordinal`），**随槽位存档** | `NarrativeSaveData.cs:28` |
| 当前写入方 | `NarrativeRules` 按配置写幂等标记（B3）、`NarrativeSaveData.ConsumedTriggers` / `ConditionEdges` / `EdgeCounters` | — |
| 当前校验 | `NarrativeCatalog.ValidateFacts`：`StoryFlag` 只查非空；`TargetHostile` / `TargetDetected` **无真实来源，拒绝进入生产内容** | `NarrativeCatalog.cs:180` |

**由此得出三条硬约束**，本字典必须在它们之内工作：

- **C-1 表结构不改**：条件列复用对白 schema（`Tables/Defines/narrative.xml` 首行注释已写明「条件复用对白 schema，避免两份事实枚举漂移」）。新增事实种类**不动表结构**。
- **C-2 只有布尔谓词**：`Read` 返回 `bool`，所以**数值状态一律预计算成布尔事实**，阈值固定在写方代码或配置里，不在表里做数值比较。策划要改阈值＝改配置，不是改表。
- **C-3 键名是唯一识别**：`Key` 是 `string`，且 `StringComparer.Ordinal`。**大小写敏感、点号是层级分隔符**，不许用空格或中文键名。

---

## 3·事实分类与命名规范

### 3.1 三类事实

| 类别 | 表达方式 | 例子 | 谁定义 |
| --- | --- | --- | --- |
| **A 内置布尔事实** | 用 `Fact` 枚举已有值 | `PlayerSneaking`、`TargetAlive` | Core 侧，已定型 |
| **B 命名状态谓词** | `Fact=StoryFlag` + 点分键 | `identity.exposed`、`water.high` | 本字典 |
| **C 剧情推进标记** | `Fact=StoryFlag` + 点分键 | `stage.p3.passed` | 本字典（B3 已实现的写入路径） |

**A 类不要扩**：`Fact` 加一个枚举值要动 `EncounterContext`、`NarrativeCondition`、表翻译三处，且会与「条件复用对白 schema」的约定冲突。**凡能用 B 类表达的，一律用 B 类。**

### 3.2 键名规范（校验器要照这个查）

```
<命名空间>.<状态名>[.<档位>]
```

| 规则 | 要求 |
| --- | --- |
| 命名空间 | 白名单：`identity` / `stealth` / `chase` / `world` / `stage` / `item` / `combat` / `route` |
| 字符集 | 小写 `a-z`、数字、点号、**下划线**；不许大写、空格、中文。<br>⚠️ **下划线只允许出现在单段键里**（存量键如 `quest_completed_1002`）。**点分键的每一段都不许带下划线**——多词状态名用驼峰（`queueConsumed`、`jinglongMask`），因为下划线在段内会让「段」的边界变得靠猜，历史上出过 `item.jinglong_mask.crafted`（4 段、永远过不了 V1）这类错。 |
| 段数 | 1～3 段（单段用于存量键，见下方「为什么允许下划线」） |
| 档位段 | 只允许 `low` / `mid` / `high` 或 `none` / `some` / `full`（同一键只用一套）。<br>⚠️ **档位段必须是第三段（最后一段）**：`<命名空间>.<状态名>.<档位>`。状态名本身**不许再带点**——所以「队列消耗」的状态名写成 `queueConsumed`（驼峰）而不是 `queue.consumed`，否则整个键会变成 4 段、永远过不了 V1（2026-10-07 修正，原文写的 `world.queue.consumed.low` 就是这个错）。 |
| 唯一性 | 同一个键全项目只有一处写入 |

**为什么允许下划线（2026-10-07 修订，重要）**：本节初版把下划线列为非法字符，实施时发现**存量键本来就在用下划线**——`quest_completed_1001` / `quest_completed_1002` 是 B3 已落地的任务→剧情标记（`Tables/Data/narrative_quest_flags.json` 的 `flag` 列），且被 `Tables/Data/dialogue/9001.json:13` 的选项条件读取。初版规则会把它判死，实施方一度按规则改了 5 处（含一个不归它管的 Dialogue 数据文件），造成写入侧与读取侧不一致、那条对白选项永远不可用。

**结论：规则应当描述现状，不是重新发明现状。**把一条正则用来逼停在生产使用的键名，属于 `harness-authoring.md` 警告的过度约束。故：

- V1 定为 `^[a-z][a-z0-9_]*(\.[a-z0-9_]+){0,2}$`——`quest_completed_1002`（单段含下划线）与 `world.quest1001.done`（三段）**都合法**，存量键零迁徙。
- **新键推荐**用点分命名空间（`<命名空间>.<状态名>[.<档位>]`），因为 V2 的命名空间白名单只对点分键生效；但**不强制**单段键改形。
- V3 白名单里，按 id 生成的键（`quest_completed_<id>`）记作**通配前缀**，不逐个列举。

**仍然强制的两条**：不许大写（`Identity.Suspicion` 一律拒）、不许未经登记的命名空间（`foo.bar` 一律拒）——这两条才是「拼错静默失效」的实际来源。

**为什么强制白名单**：见第 1 节第 2 条。校验器必须把「未知命名空间」「格式不合规」当场报错，**不能只查非空**。这条改进落在 `NarrativeCatalog.ValidateFacts`，见第 5 节。

---

## 4·事实字典

「写入方」列是**唯一 owner**：同一个键不许两个模块写。「写入时机 / 清理」列决定存档与读档行为。「真源」列给出设计文档出处。

### 4.1 S1 换皮与附身 / S2 身份暴露与怀疑

| 键 | 含义 | 写入方 | 写入时机 / 清理 | 真源 |
| --- | --- | --- | --- | --- |
| `identity.borrowed` | 当前处于借来的身份（附身或使用皮/面具） | Identity | 进入身份状态时写；退出/失效时清 | `01_换皮与附身.md`；`00` §8.1 #2 |
| `identity.<id>` | 当前身份是哪一个（一身份一键，如 `identity.yaoshi_clerk`） | Identity | 同上，与 `identity.borrowed` 同时写、同时清 | `01` 的身份表；`00` §4.1 |
| `identity.skin` | 正在以「皮」的形式借用身份 | Identity | 用皮时写 | `05_皮面具与道具.md` |
| `identity.mask` | 正在以「面具」的形式借用身份 | Identity | 用面具时写 | 同上 |
| `identity.memory` | 已获得所借身份的记忆（线索来源） | Identity | 附身/用皮成功时写，**不清**（跨阶段） | `01`；`00` §8.1 #2 |
| `identity.exposed` | 身份已被看穿（露馅已发生） | Identity | 露馅判定命中时写 | `02_身份暴露与怀疑.md` 六种露馅 |
| `identity.exposedCount.low/mid/high` | 本阶段露馅次数的档位 | Identity | 每次露馅重算三档（阈值可配） | `02`；`00` §8.1 #9 |
| `identity.suspicion.low/mid/high` | 怀疑度档位 | Identity | 怀疑度变化时重算（可回落由配置开关决定） | `02`；`00` §8.1 #9 |
| `identity.ledger.over` | 用过的身份**超量**（触发追逐的开关） | Identity | 账簿条目数越界时写 | `02` §3.4、`07` §3.4 |
| `identity.dead` | 玩家因暴露而死亡（失败态） | Identity | 走死亡分支时写 | `00` §5 C1（**死亡 vs 追逐是原文矛盾，由策略注入**） |
| `item.<id>.owned` | 持有某件关键道具（如 `item.tooth.owned` 牙） | Inventory | 获得时写；**跨阶段不清** | `05` 跨阶段携带 |
| `item.craftsmanMask.owned` | 查勘使面具 | Inventory | 获得即写 | `05` |
| `item.blankSkin.owned` | 空白皮 | Inventory | 获得即写 | `05`；§3.2 链 |
| `item.jinglongPill.owned` | 泾龙丹 | Inventory | 获得即写 | `05`；`09_BOSS战.md` |
| `item.jinglongMask.crafted` | 泾龙面具已合成（跳阶段用） | Inventory | 合成成功时写 | `00` §3.2 |

### 4.2 S3 潜行与暗杀

| 键 | 含义 | 写入方 | 写入时机 / 清理 | 真源 |
| --- | --- | --- | --- | --- |
| `stealth.hidden` | 当前未被任何敌人察觉 | Stealth | 每次感知结算时写/清（**瞬时态，不入档**） | `03_潜行与暗杀.md` |
| `stealth.behind` | 目标当前背对玩家且在暗杀距离内 | Stealth | 同上（瞬时态） | `03` |
| `stealth.assassinated` | 已用背后暗杀解决过目标（本阶段计数用） | Stealth | 命中即写 | `03`；`00` §8.1 #5 |
| `stealth.knockdown` | 玩家处于被击倒状态 | Stealth | 进入击倒写、起身清（瞬时态） | `03`；`00` §5 C5 |
| `stealth.knockdownCount.low/mid/high` | 本阶段被击倒次数档位 | Stealth | 每次击倒重算 | `03`；`00` §8.1 #4 |
| `stealth.cover` | 当前处于掩体遮挡下（遮挡做不做未定，未做时该键恒不写） | Stealth | 遮挡判定命中时写 | `03`；`00` §8.1 #6 |
| `stealth.visionmask` | 有敌人正在寻找玩家（搜捕态） | Stealth | 进入/退出搜捕时写清 | `04_追逐.md` |

### 4.3 S4 追逐

| 键 | 含义 | 写入方 | 写入时机 / 清理 | 真源 |
| --- | --- | --- | --- | --- |
| `chase.active` | 追逐进行中 | Chase / Monster | 触发时写、摆脱时清 | `04` |
| `chase.escaped` | 已成功摆脱过追逐（本阶段） | Chase | 摆脱成功时写 | `04` |
| `chase.caught` | 已被追上（后果由策略注入：死亡或别的） | Chase | 被抓判定时写 | `04`；`00` §5 C1 |
| `chase.fixed` | 当前是脚本化的「固定追逐」（区别于自主追击） | Chase | 进入固定追逐时写 | `04`（工程原本没有） |

### 4.4 S5 皮、面具与道具

见 4.1 的 `item.*` 五行。补充：

| 键 | 含义 | 写入方 | 写入时机 / 清理 | 真源 |
| --- | --- | --- | --- | --- |
| `item.<id>.used` | 一次性道具已用掉 | Inventory | 使用时写 | `05`；`00` §8.1 #10 |
| `item.craft.failed` | 最近一次合成失败（给反馈用） | Inventory | 失败时写，下次成功清 | `05` |

### 4.5 S6 关卡专属机制 / S7 BOSS

| 键 | 含义 | 写入方 | 写入时机 / 清理 | 真源 |
| --- | --- | --- | --- | --- |
| `world.water.low/mid/high` | 阶段3 水位档位 | Stage（World） | 水位变化时重算 | `07_关卡专属机制.md` |
| `world.drunk.low/mid/high` | 醉酒档位（sp02 酒肆） | Stage（Player 侧写入） | 饮酒时重算 | `07`；`00` §8.1 #1 |
| `world.queueConsumed.low/mid/high` | 阶段六被消耗的特定信众数量档位（决定分福身强度） | Stage | 每消耗一个重算 | `07`；§3.2 链 |
| `world.perception.seen` | 敌人感知到玩家（阶段九） | Stage / Stealth | 感知命中时写 | `07` |
| `world.illusion.active` | 幻象生效中（阶段九） | Stage | 进出幻象区写清 | `07` |
| `world.maze.solved` | 镜子迷宫已解 | Stage | 解出时写 | `07` |
| `combat.phase.<n>` | BOSS 当前阶段（如 `combat.phase.2`） | Monster | 转阶段时写、上一阶段清 | `09_BOSS战.md`；`00` §8.1 #7 |
| `combat.tooth.weakened` | BOSS 的牙已被削弱 | Monster | 削弱命中时写 | `09`；`05` |
| `combat.masks.ready` | 三面具已齐（开战前置） | Inventory / Monster | 集齐时写 | `09`；`05` |

### 4.6 剧情推进与路线

| 键 | 含义 | 写入方 | 写入时机 / 清理 | 真源 |
| --- | --- | --- | --- | --- |
| `stage.p<n>.passed` | 第 n 阶段已通过（n = 1..12，序章用 `stage.p0.passed`） | Narrative | 阶段迁移时写，**不清** | `11_剧情流程与章节结构.md` |
| `stage.p<n>.entered` | 已进入第 n 阶段 | Narrative | 进入时写 | 同上 |
| `route.stage1.sp02` | 阶段一采用 sp02 套 | Narrative | 内容侧配置决定，**不由玩家写** | `00` §6（**等 §8.1 #1 拍板**） |
| `route.stage1.sp03` | 阶段一采用 sp03 套 | Narrative | 同上 | 同上 |

> **命名隔离提醒**：聚光灯的「账簿」是**玩家用过的身份记录**（`identity.ledger.over`），旧版「两界之账」是扣押记账、已随旧版冻结。**不要用 `ledger` 之外的词，也不要给旧版概念建键**（`00` §4.2）。

---

## 5·校验要求（必须落地，否则字典只是纸面约定）

`NarrativeCatalog.ValidateFacts`（`Assets/_Project/Scripts/Runtime/Narrative/NarrativeCatalog.cs:180`）现在只做两件事：`StoryFlag` 查非空、`TargetHostile`/`TargetDetected` 拒绝进生产。**要补三条**：

| # | 校验 | 为什么 |
| --- | --- | --- |
| V1 | `StoryFlag` 的 `Key` 必须匹配 `^[a-z][a-z0-9_]*(\.[a-z0-9_]+){0,2}$` | 防大小写/空格/中文键名漂移。**下划线与单段键合法**（存量键 `quest_completed_<id>` 就是这个形状，理由见第 3.2 节） |
| V2 | `Key` 的第一段必须是第 3.2 节白名单之一 | 防未知命名空间的静默失效 |
| V3 | 带档位段的键（`*.low/mid/high`、`*.none/some/full`）第二段必须是本字典登记的状态名 | 防拼错（`suspicion` 写成 `suspision` 现在**完全静默**） |

**每条都要有负对照测试**：喂 `Identity.Suspicion`（大写）、`foo.bar`（未知命名空间）、`identity.suspision.high`（拼错）三种坏键，必须各自抛异常并点名是哪一条。

**已登记键的来源**：V3 的白名单从本字典第 4 节生成，**不要另建一份伴生清单**（`.claude/rules/harness-authoring.md` 禁止建伴生清单）。做法：在 `NarrativeCatalog` 里放一个 `static readonly` 白名单常量，注释指向本文件与版本；改字典与改常量必须是同一次提交。

---

## 6·写入侧纪律

1. **一个键一个写入方**。第 4 节的「写入方」列就是 owner。要两个模块都写同一个键时，改成两个键。
2. **瞬时态不入档**。标了「瞬时态」的键（`stealth.hidden` / `stealth.behind` / `stealth.knockdown`）只活在内存快照里，**不要进 `NarrativeSaveData.StoryFlags`**——否则读档会把玩家恢复成「正被击倒」。
3. **跨阶段携带的键不清**。`identity.memory`、`item.*.owned`、`stage.*.passed` 一律只在获得时写，阶段迁移不清理。
4. **阈值不进表**。C-2 的结果：档位由写方按配置算，表里只写 `identity.suspicion.high`。策划要改阈值改 `IdentityConfig`，不是改表。
5. **新增键先改本文**。加键＝改第 4 节 + 改 V3 白名单常量，同一次改动里完成。

---

## 7·未定项（等策划拍板，不影响本字典成立）

| 项 | 影响哪些键 | 拍板编号 |
| --- | --- | --- |
| 暴露后果：死亡还是追逐 | `identity.dead` 还是 `chase.active` 先写 | `00` §8.1 #3、§5 C1 |
| 挨打：击倒还是死亡；击倒多久 | `stealth.knockdown` 的时长与档位阈值 | `00` §8.1 #4、§5 C5 |
| 掩体与视线遮挡做不做 | `stealth.cover` 是否恒不写 | `00` §8.1 #6 |
| 账簿 / 怀疑度怎么计、阈值、能否回落 | `identity.ledger.over`、`identity.suspicion.*` 的阈值与回落 | `00` §8.1 #9 |
| 道具规则：一次性与持有型、合成后原料是否消失 | `item.*.used` / `item.craft.failed` 的语义 | `00` §8.1 #10 |
| 状态要不要上 HUD | 不影响键，只影响表现 | `00` §8.1 #11 |
| 阶段一用哪套（sp02 / sp03） | `route.stage1.*` 与所有 `world.*` 的内容侧 | `00` §8.1 #1 |
| BOSS 形式 | `combat.phase.*` 的推进方式 | `00` §8.1 #7 |

**这些未定项不阻塞字典**：键名与写入时机已定，拍板后只改配置值与阈值，不改键名、不改表。
