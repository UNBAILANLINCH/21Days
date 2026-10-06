# 旧版 G 组（00–15）待完成项规划 · 按《聚光灯》新目标重规划

> **状态：规划稿（2026-10-07 按新目标重写）**
> **用户拍板（2026-10-07）**：**旧版与新目标冲突时，以新目标内容为主，按新目标重新规划本任务。**
> **读者**：主窗口（派单与验收）、subagent（按单执行）、用户（看进度）。
> **进度只看这一份**：第 1 节进度总表 + 第 8 节日志。每波收口由主窗口回填，**必须附证据**（文件路径:行号 / 测试结果）；subagent 自报不算通过（`ai-docs/project-guide.md` 工作纪律 3）。
> **配套判据**：旧版每一项在新目标下的归属、可保留资产、应作废概念，见 [`旧G组-新目标映射与遗留资产.md`](旧G组-新目标映射与遗留资产.md)（已落盘，2026-10-07）。**两者冲突时以判据文档为准，并回填本文**。

---

## 0·新目标与本文口径

**新目标 = 《聚光灯》（S1–S8）**，索引 [`design/features-spotlight/00_功能总览.md`](../design/features-spotlight/00_功能总览.md)，roadmap 落点 [`roadmap.md`](../roadmap.md) 第 3 节 S 组与第 5 节 W5。

**本文回答一个问题**：旧版 00–15 的 12 项待完成项，在「以新目标为准」的前提下还剩什么、谁来做、按什么顺序、卡在哪。

### 0.1 旧版 12 项在新目标下的判据（结论先行）

| 旧版项 | 新目标下的归属 | 处置 |
| --- | --- | --- |
| [04] 收押（Taming） | 聚光灯「收押」只是剧情结局，不做成玩法；Taming 改造为**附身载体** | **判定作废；载体改造并入 S1** |
| [06] 画皮傩演（Disguise） | 同取「傩戏＋画皮」主题，但皮与面具**不要求先收押**，改为附身 / 击杀掉落 / 剥脸取得 | **改名并入 S1 + S5** |
| [08] 追逐躲藏（Monster） | 聚光灯拆成 S3 潜行与暗杀、S4 追逐；「弱点」近似 S7 的牙削弱 | **拆入 S3 + S4 + S7** |
| [13] 妖的化形与破绽（妖表） | 聚光灯无化形 / 破绽 / 执念表，但 S 组步 1 要「怪物种类数据化」 | **改为「物种数据化底座」，服务 S3/S4/S7** |
| [05] 归还与扣押 | 聚光灯未提及去处裁定 | **判定作废；术语表可留档** |
| [07] 两界之账 | 与聚光灯阶段 3「账簿」**同名不同物** | **判定作废；命名需隔离（撞车点 T4）** |
| [09] 镜中窥探 | 聚光灯的镜中妖界是**能进去走动战斗**的另一张地图，不是「能看不能改」的独立层 | **判定作废；并入 [10] 两界与场景结构 / A4** |
| [10] 疑案调查（两个界面） | 聚光灯有 [12] 调查与线索（卷宗 / 文书翻阅 / 推理三块面板都没有） | **保留并改名，合并为一个界面 PRP** |
| [11] 分支与结局 | 聚光灯只有一个结局「结案」，不做三结局 | **判定作废；分支变量能力由 Narrative StoryFlags 承接** |
| [12] 断其去处 | 「轮回」在聚光灯里只出现在剧情 | **判定作废** |
| [14] 流程与章节结构 | 聚光灯是十二阶段、两界往返 | **改名并入聚光灯 [11] + [10]；仅保留规范产出** |
| [15] 叙事呈现 | 聚光灯原文没有叙事呈现设计，只有 UI 表 | **改名并入聚光灯 [13]；缺口只剩污染视角与卷轴插图** |

**净结果**：12 项里 **6 项判定作废**（04 玩法侧、05、07、09、11、12），**4 项改名并入 S 组或聚光灯文档**（06、08、13、10），**2 项只产规范不写代码**（14、15）。

### 0.2 本轮实测发现的四件事（直接决定派单）

**发现 1 — `InputCommand` 只有 7 个按钮位，六个动作采不到，且不报错。**
`Core/Simulation/InputCommand.cs:24,43` 只有 `Confirm / Cancel / Pause / Sneak / Disguise / Attack / Run` 七位 + bit31 QA 标记。`Tame / Mirror / MirrorSelf / Journal / Immersive / Interact / Inventory` 七个动作**没有位**。
→ 任何要进确定性内核 / 回放的新交互，**先扩位掩码**；否则回放静默丢输入。这是 S3 / S4 / S1 开工前的地基活。

**发现 2 — Taming 与 Disguise 的真实体量是「接线」而不是「功能」。**
- Taming：3 个 .cs，**无 Installer**、`Boot.unity` 的 11 个 Installer 里没有它、`InputCommand` 无位、无状态快照；`TamingRules.cs:31` 的可用条件只有双方 `Health > 0`。
- Disguise：`DisguiseRules.cs` 全文 9 行，规则就是 `AllowsEnemyAttack => !isDisguised`；唯一消费点是 `MonsterRules.cs:238` 的攻击许可（间接影响 `:333` 的警戒升级）。
→ S1 的第一步是 **`GameplayInstaller` + `IReplayState` + `InputCommand` 三处接线**，不是加面具 / 时限 / 身份概念。

**发现 3 — 妖表是全工程唯一的「超前设计资产」。**
`Tables/Defines/yao.xml:6-18` 11 列里，`flaw / obsession / clan / sealable / mask` 五列注明「本波只进表不读」；数据只有 `yao/1.json`、`yao/2.json` 两行占位；**唯一读取方是已冻结的 `Game.Mirror.MirrorService`**。
→ 聚光灯 S 组步 1「怪物种类数据化」要的东西，这张表已经做了一半。**应继承这张表，而不是新建一张。**

**发现 4 — Narrative 的承载面比模块文档读起来窄：`Battle` 阶段被生产校验直接挡死。**
`NarrativeContent.cs:9` 的 `StageKind` 有五种，但 `NarrativeCatalog.cs:79-80` 的 `Validate` 对 `Battle`、`IssueRequest`、`RequiredParts` **一律抛 `ArgumentException("尚未接入的剧情能力：…")`**（`:69` 注释同）。
→ 聚光灯四个战斗关阶段（一、三、六、九）**现在连进表都进不去**；序章也进不去（依赖 S1/S3/S7）。能零新增能力进表的是二、四、五、七、八、十、十一、十二这 8 个非战斗阶段。**「[14]/[15] 由现有模块承载、不需要新模块」这句话要加限定**：不新增模块成立，但需先把 `Battle` 阶段与 C5 的接线做掉（归 S3/S4/S7）。
→ 另两个同类发现：**幕级重开在 Narrative 里没有回退接口**（`NarrativeRules` 只有前进，唯一回退是读档，归 S2 + E1）；**存档槽位的「进度描述」取的是 Quest 追踪任务而不是 Narrative 阶段**（`Session/SessionStateAdapter.cs:124-143`），做章节卡时要与它统一口径。

### 0.3 数字口径修正（引用旧版材料时别用错）

| 说法 | 实测 |
| --- | --- |
| 「128 条待定」 | 15 份文档 §9 编号项**逐份与 00 表一致**，合计 128；但性质构成是 **矛盾 55 + 待定 66 + 歧义 7**，当「纯待定」用会高估约 43% |
| 「30 条矛盾」 | 00 第 5 节正好 **30 行**；其中 6 条是先决（#1 #4 #5 #23 #13 #21），**#1 #4 #5 #23 需策划 + 程序一起定** |
| 「都记在总览里」 | **57 / 128 条从未进过 00 的三张表**（第 5 节、第 8 节、第 177 行补充清单），最该补的是 `[06] 9.11`（被识破）、`[04] 9.6`（收押失败反馈）、`[11] 9.9`（C5 戴皮变量缺失） |
| 「§9 之外没有别的待定」 | 错。仅 G 行文档与 [01] 就有 **18 处**行内 `[待定]` 没有对应 §9 条目（如 `03:99` 每道裂痕缩减多少、`04:82` 收押记录无状态变量、`08:94` 面具战胜负条件） |
| 「旧版待定随旧版冻结，不再追」 | `HANDOVER.md:119` 这么写着，但**新目标下这批条目的失效与转化并未逐条判定过**，见配套判据文档 |

---

## 1·进度总表（唯一进度真源）

规模：S ≤ 1 天单点；M 2–4 天多文件；L ≥ 1 周跨模块（走 PRP）。
进度：`未开始` / `策划待拍板` / `PRD 中` / `PRP 中` / `实现中` / `待验收` / `完成` / `已作废`。

### 1.1 新目标下「先做不亏」的地基（两条线都要，已派单）

| 编号 | 任务 | 承载 | 规模 | 前置 | 进度 |
| --- | --- | --- | --- | --- | --- |
| B0 | `InputCommand` 扩按钮位（至少补 `Tame`/`Interact`/`Inventory`，按 S1/S3 需要补附身 / 暗杀位），同步回放快照格式版本与测试 | Core/Simulation + Core/Replay | M | 无 | **完成（Q0）**：补 `ButtonTame/Interact/Inventory` = bit7/8/9，`SerializedSize=31` 与偏移量一位未动；**不升回放格式版本**（主窗口逐字比对 `ReplayFormat.cs:39-47` 后裁定成立）；另加 `FindUnmappedGameplayActions` 守卫 + `ExemptActionNames` 豁免表（4 项逐条附理由）；测试新增 19 条。**该单实测回报**：lint 退出码 0 无输出（并用故意违规探针证明 lint 真在跑）、`run_tests(EditMode, Simulation+Replay)` **60/60 通过 0 失败 0 跳过**。唯一非 `.cs` 改动：测试 asmdef 加一行 `Unity.InputSystem.TestFramework`（主窗口已复核该行，`invariants.py` 未报依赖方向问题） |
| B1 | 妖表底座：五列死列变可查 + 物种数据化结构核对（聚光灯步 1） | Tables + `Runtime/Mirror/`（复用，不新建模块） | M | 无 | **完成（Q0）**：加 `tier / killable / drop_items` 三列（`yao.xml:39-41`）、生成物同步、只读 `YaoCatalog` + `.meta`（guid `9c40f25e…`）、表头列归属按「冲突以聚光灯为准」改正、`YaoTableTests.cs` 扩 197 行。**该单实测回报**：lint 三个 `.cs` 全绿（并用已知违规探针验证 linter 真在工作：退出码 2 + 报 public 字段）；`run_tests(EditMode, group_names="Game.Tests.EditMode.Mirror")` → **150/150 通过、0 失败、0 跳过**（2.1s，job `07dd58e5f1414ffe8d68fee9a8e36bd0`），`compile_errors: []`。**主窗口已复核**：`Game.Tests.EditMode.dll` 03:07:43、`Game.Runtime.dll` 03:17:00、`.meta` guid 一致、`MonsterTier.cs`(+`.meta`) 已摘除、代修处未被回改 |
| B8 | 道具类别补「皮 / 面具 / 钥匙」：`Generated/EItemCategory.cs:17-34` 现只有 `Material / Consumable / Clue / Key`，妖表 `drop_items` 填的掉落物**现在没有对应类别可归** | Tables（item 表定义在 `Tables/Data/item.xlsx`，非 xml） | S–M | 聚光灯 §8.1 #1（阶段一拍板才知掉落物清单） | 未开始 |
| B2 | 旧版 12 项 → 新目标逐项判据（可保留资产 / 应作废概念 / 128 条失效归类） | 文档 | S | 无 | **完成（Q0）**：[`旧G组-新目标映射与遗留资产.md`](旧G组-新目标映射与遗留资产.md)，47 KB / 348 行；12 项归属表 + 逐项判据 + 灰区 A1–A14 + 五个死列处置 + 128 条归类（**93 条已失效 / 11 条仍需新目标答复 / 24 条变成新目标的别的问题**）+ 30 条矛盾逐一归类 |
| B3 | Taming / Disguise 接线（Installer + `IReplayState` + `InputCommand`），使 S1 能落 | Taming / Disguise / Core | M | B0（已完成大半） | 未开始 |
| B4 | 调查界面：卷宗 / 文书翻阅 / 推理（吸收旧版 [10]，即聚光灯 [12] 的三个缺口） | Core/UI + Quest + Narrative + Inventory | L（走 PRP） | 策划答 §8.1 #1 | 未开始 |
| B5 | 阶段 / 章节结构规范（吸收旧版 [14]，映射到聚光灯十二阶段） | Narrative 文档 | S–M | 无 | **完成（Q2）**：[`阶段结构规范-章幕场与十二阶段.md`](阶段结构规范-章幕场与十二阶段.md)，243 行 / 约 40 KB；含术语映射主表 + 无唯一映射待拍板项、十二阶段清单（13 段：序章 + 一~十二，带世界 / 地点 / 战斗关 / 出处）、Narrative 承载能力逐条对照、旧版 [14] 仍成立与已作废两栏、落点建议、待拍板问题。**主窗口抽验六行引用全部为真** |
| B6 | 叙事呈现缺口清单（吸收旧版 [15]：污染视角、卷轴插图、换嗓呈现） | Dialogue / Performance | S | 无 | **完成（Q2）**：[`叙事呈现缺口清单.md`](叙事呈现缺口清单.md)，175 行 / 约 34 KB；含旧版 [15] 逐条对表、新目标 [13] 呈现行 → 工程现状、真实缺口 N1–N10（标归属 / 规模 / 依赖 / 是否单开 PRP）、**明确不做清单 15 条**（逐条给理由与出处）、未核实项。**主窗口抽验为真**（字幕开关确缺、黑边冲突确有原文） |
| B7 | ~~怪物层级子层建模决策（`A·下` / `B·下`）~~ **已销项（有原文支撑，无需决策）** | — | — | — | **已解决**：`features-spotlight/06_怪物分层.md:126` 原文「「层级」一列中的「A·下」表示原文写在 A 条目的下一级，B·下同理」——是排版记号，不是第四层级。`Tables/Defines/yao.xml:39` 已把该结论与出处写进 `tier` 列注释 |
| B9 | 「怎么杀」列：聚光灯表头是「可否击杀 / **怎么杀**」（`06_怪物分层.md:130`），现表只有 `killable` 一个 bool，**「用什么手段杀」没有落点**（例如户绝民「改用湿皮收服」、拾骨人「持有篮子时不可击杀」） | Tables | S | 聚光灯 §8.1 #1（阶段一怪物清单拍板后才知要填什么） | 未开始 |
| B11 | **`mask` 列的语义在换目标后已经变了，却没有新定义**：它按旧版语义是「收押后才能制面具」（`04_收押.md`），而聚光灯不再要求先收押（`features-spotlight/00:356`）。现在这一列**既没有新读者、也没有新定义**，属「保留备查」；真正落地 S5 时要**要么改语义、要么删列** | Tables（S5 的 PRP 内） | S | S5 PRP | 未开始 |
| B12 | **按种类配数值**：步 1 的最后一段。`MonsterConfig` 是 17 字段的 SO、全体共用（`MonsterConfig.cs:9-25`），要拆成「种类 → 数值」需先定「哪些字段按种类、哪些全局」 | Monster + Tables | M–L | 聚光灯 §8.1 #1 | 未开始 |
| B10 | 模块文档行号漂移两处：`dialogue-module-guide.md:192` 写 `DialogueController.cs:31`，实际 `:42` 才是 `ChoiceRefreshInterval`；`performance-module-guide` 写 `PerformanceService.cs:42`，实际 `:59` 才是 `InputMap`。**主窗口已复核两处为真** | ai-docs 模块文档 | S | 无 | 未开始 |
| B13 | **`YaoCatalog.Invalidate()` 与构造后 `IsReady` 会遍历全表校验 tier**，「静态表 + 启动后只校验一次」不是当前唯一路径。眼下无害（表只读、校验是每行一次字符串比较），但将来若有「热重载配置」的调用方需收敛成显式的一次性校验入口 | `Runtime/Mirror/YaoCatalog.cs` | S | 无（等真有热重载需求再做） | 未开始（该单已在类文档里记了，未加代码） |

### 1.2 新目标的 S 项本体（不在本文规划范围内，只记状态，避免两处口径）

| 项 | 前置 | 状态 | 出处 |
| --- | --- | --- | --- |
| 步 1 怪物种类数据化（[06]） | 无硬依赖；内容等 §8.1 #1 | 玩法定义已拆，待策划确认 | `roadmap.md:336` |

**⚠️ 别把 B1 误读成「步 1 做完了」**：B1 只做了「**表结构 + 只读查询层**」（3 个新列 + `YaoCatalog`），**「按种类配数值」这个核心还没做** —— `MonsterConfig` 现在仍是一份 17 字段的 SO、全体共用。B1 主动没改 `roadmap.md` 的 W5 状态列，理由正是这一条，**这个判断是对的**。步 1 完成 = 表结构 + 只读层 + `MonsterConfig` 拆成「种类 → 数值」，最后一段仍待做。
| S3 潜行与暗杀 | §8.1 #4 #5 #6；B1 B0 | 同上 | `roadmap.md:280` |
| [10] 两界与场景结构 | §8.1 #1 #6 #12；并入 A4 / A6 | 同上 | `roadmap.md:287` |
| S1 换皮与附身 | 步 2；§8.1 #1 #2；B3 | 同上 | `roadmap.md:278` |
| S5 皮、面具与道具 | 步 2 / 步 4；§8.1 #10 | 同上 | `roadmap.md:282` |
| S2 身份暴露与怀疑 | 步 4；§8.1 #3 #9 #11 | 同上 | `roadmap.md:279` |
| S4 追逐 | 步 2 / 步 6；§8.1 #3 #4 | 同上 | `roadmap.md:281` |
| S6 关卡专属机制 | 步 3/4/6/7；§8.1 #1 #6 #9 #11 #12 | 同上 | `roadmap.md:283` |
| S8 小游戏 | 步 4；§8.1 #1 #8 | 同上 | `roadmap.md:285` |
| S7 BOSS 战 | 步 5 / 步 8；§8.1 #1 #7 | 同上 | `roadmap.md:284` |

**总前置**：策划先拍 `features-spotlight/00 §8.1` 第 1–6 条，首要是「阶段一用 sp02 五个附身场景 / sp03 坊市暗杀场景 / 拼」（`features-spotlight/00:394`、`roadmap.md:336`）。

### 1.3 已作废项（只留档，不再派单）

| 项 | 作废理由 | 留档位置 |
| --- | --- | --- |
| [04] 收押玩法 | 聚光灯「收押」只是剧情结局；户绝民「收服」、老吏「制服」都不是收押 | `features-spotlight/00:354`、`:98` |
| [05] 归还与扣押 | 聚光灯未提及 | `features-spotlight/00:355` |
| [07] 两界之账 | 同名不同物：聚光灯阶段 3「账簿」记的是用过的身份 | `features-spotlight/00:357` |
| [09] 镜中窥探 | 聚光灯的镜中妖界是可走动战斗的地图 | `features-spotlight/00:359` |
| [11] 三结局硬分支 | 聚光灯只有一个结局「结案」 | `features-spotlight/00:361` |
| [12] 断其去处 | 「轮回」只在剧情出现 | `features-spotlight/00:362` |
| [01] 通灵视、[02] 照镜辨形、[03] 镜之耐久与镜碎 | 聚光灯镜不是核心交互，只在阶段九出现一次 | `features-spotlight/00:351-353`；`roadmap.md:168` |

---

## 2·十二项在新目标下的判据表

**判据真源**：[`旧G组-新目标映射与遗留资产.md`](旧G组-新目标映射与遗留资产.md) 第 1、2 节，每行带 `路径:行号`，派单前先查它。下表是可派单的最小摘要，**冲突以判据文档为准**。

**判据文档补充的三点**（比本文更细，别漏）：
1. **可保留的设计资产不是零**：[04] 的「两个前提、缺一不可」判定形状与「不设血条、削弱是情境状态」保留给 S7 的牙削弱；[06] 的「面具＝演其身份、暂得其记忆与本事」保留给 S1 R14，「携带上限」给 S5 的三分类；[08] 的「按手段分胜负条件」写法给 S4 当模板。
2. **灰区 A1–A14**：新目标既没吸收、也没明文否定的内容（通灵视、随身镜道具位、去处裁定的剧本写法、躲藏、皮面具时限、章/幕/场术语、不配音等），逐条给了处置建议——**没有一条要新开 PRP**。
3. **五个死列的处置与工程现状已经对上了**：`sealable` 改语义为「可击杀 / 需收服 / 需制服 / 不可杀」（判据文档称五列里最该保留的一列），`mask` 改语义为「掉落物 id 列表」——B1 那单加的 `killable` + `drop_items` 正是这两条，方向一致；`clan` 半有用（族/阵营标签）；`flaw` / `obsession` 无玩法读取方，**只降为文案备注，不为它们开 PRP**。

| 旧项 | 载体 | 新目标下做什么 | 卡点 | PRP 档位 |
| --- | --- | --- | --- | --- |
| [04] | Taming（3 .cs，未挂 Boot） | **不做收押**。Taming 作为「附身载体」接线，并入 S1 | §8.1 #1 #2 | 随 S1 PRP |
| [06] | Disguise（9 行） | **不做「收押后面皮」**。改造为「身份状态」，皮与面具来源改为附身 / 剥脸 / 掉落 | §8.1 #1 #2 #10 | 随 S1 + S5 PRP |
| [08] | Monster（14 .cs） | **不做「弱点识破 + 场景利用」的旧胜负条件**。拆为 S3（击倒 / 绕背处决 / 视线遮挡）+ S4（追逐）+ S7（牙削弱） | §8.1 #1 #4 #5 #6 | 随 S3 / S4 / S7 PRP |
| [13] | 妖表（11 列，5 列死列） | **不做化形 / 破绽 / 执念表**。改为物种数据化底座，服务 A/B/C 分层、可否击杀、掉落 | 无硬依赖（内容等 §8.1 #1） | 独立小 PRP 或随步 1 |
| [05] | 无 | 作废 | — | 不立项 |
| [07] | 无 | 作废；命名与聚光灯「账簿」隔离 | — | 不立项 |
| [09] | 无 | 作废；并入 [10] 两界与场景结构 / A4 | — | 不立项 |
| [10] | 无界面 | **保留**：卷宗 / 文书翻阅 / 推理三块面板（聚光灯 [12] 明列「都没有」） | §8.1 #1（内容）、#4（当前身份条件事实） | 独立 PRP（B4） |
| [11] | Narrative StoryFlags | 作废三结局；分支变量能力保留（`narrative.xml:14 setFlags`、`ConditionFact.StoryFlag`） | — | 不立项 |
| [12] | 无 | 作废 | — | 不立项 |
| [14] | Narrative | 只出规范：章 / 幕 / 场 → 聚光灯十二阶段的映射，落地走 W4 | §8.1 #1 | 纯规范（B5） |
| [15] | Dialogue / Performance | 只出缺口清单：污染视角、卷轴插图、换嗓呈现 | 随 S1 / S2 | 纯规范（B6） |

---

## 3·派单波次

**主窗口只做方案、拆解与验收**（`.claude/rules/model-routing.md:10`）。本机 harness 的可用模型以本会话为准（`AGENTS.md` 已注明 model-routing 的 Fable / opus / sonnet 策略只适用于 Claude）。

| 波 | 目标 | 可派任务 | 只能人做 | 验收证据 | 进度 |
| --- | --- | --- | --- | --- | --- |
| **Q0 判据与地基** | 把「旧版还剩什么」与「新目标缺什么」对齐；补上谁都躲不开的位掩码 | `sonnet`：B2 映射判据（已派）；`sonnet`：B1 妖表底座（已派）；`opus`：B0 `InputCommand` 扩位 | 无 | `InputCommand.cs` diff + EditMode `Game.Tests.EditMode` Replay/Simulation 组 | 进行中 |
| **Q1 接线** | 让 S1 能落地 | `opus`：B3 Taming / Disguise 接线 | 无 | Installer 注册 + `IReplayState` 快照 + 回放 `Taming` 模块 | 未开始 |
| **Q2 规范** | 把不需要新模块的两项落成规范 | `sonnet`：B5 阶段结构规范；`sonnet`：B6 叙事呈现缺口清单 | 无 | 两份规范落 `ai-docs/` 或 `docs/` | 未开始 |
| **Q3 界面** | 一个界面同时满足旧版 [10] 与聚光灯 [12] | `opus`：B4 调查界面 PRP（先 PRD，等 §8.1 #1 答复） | 视觉验收 | 回放逐模块：`Quest`、`Narrative`、`Inventory`（各附影响理由） | 阻塞（等策划） |
| **Q4 S 项本体** | 按 `roadmap.md:336` 顺序开 S3 → S1 → S5 … | 随各 S 项 PRP | 策划先答 §8.1 #1–#6；视觉验收 | PRP 三件套 + 独立验证 | 阻塞（等策划） |

**波次纪律（硬规则）**

1. **同一波不派两个 agent 改同一文件**。特别禁止并发改 `Assets/_Project/Scenes/Boot.unity` 与 `Assets/_Project/Data/Input/GameInput.inputactions` —— 项目已为后者立过规矩（`roadmap.md:308` H0：「由一个 sonnet 单独做，避免三个 agent 同改一份 JSON」）。
2. **改回放快照字段必须同步升格式版本**（`Core/Replay/ReplayFormat.cs` 当前 `CurrentFormatVersion = 4`、`MinimumReadableFormatVersion = 4`；`Core/Replay/ReplayStateRegistry.cs:71` 要求增删任何一行都升版）。这正是 `roadmap.md:347` 风险 3 说的「否则回放快照格式会反复升版」。
3. **测试范围写死**：EditMode 写到程序集或 group；回放逐模块列出并各附影响理由；禁止「全跑」。
4. **subagent 自报不算通过**：主窗口复跑一次再采信。

---

## 4·撞车点清单（派单前必读）

| # | 撞车点 | 现状与双方 | 处置 |
| --- | --- | --- | --- |
| T1 | `Runtime/Taming/` | 旧版 G3 要「收押封入镜中 + 接 Boot」；S1 要「改造为附身载体」（`roadmap.md:170`） | **收押侧判定作废**，Taming 只按 S1 改造一次 |
| T2 | `Runtime/Disguise/DisguiseRules.cs` | 旧版 [06] 要「面具 / 时限 / 污染」；S1/S5 要「身份状态 / 皮与面具」 | 同一份 9 行文件只扩一次，按 S1 + S5 定义 |
| T3 | `Runtime/Monster/` + `Player/` | 旧版 G5「弱点识破 + 场景利用」；S3「击倒 / 绕背处决」、S4「追逐」、S7「牙削弱」 | 旧胜负条件作废；攻击 / 生命字段去留一次定死，否则回放反复升版 |
| T4 | 「账簿」同名不同物 | 旧版 [07] 扣押记账；聚光灯 [02] §3.4 / [07] §3.4 记的是用过的身份、超量触发追逐 | **术语必须隔离**：旧版叫「两界账 / 追讨账」（已作废，仅留档）；聚光灯叫「身份簿」 |
| T5 | `Boot.unity` | 现挂 11 个 Installer（实测 guid 反查：Monster / Player / Dialogue / Quest / Exploration / Loot / Inventory / Performance / Session / Mirror / Narrative） | 每波只许一个 agent 改；其余走「接线申请」由主窗口合并 |
| T6 | `GameInput.inputactions` | 单文件 34 个动作；S1/S3 要新增附身 / 暗杀动作 | 每波只许一个 agent 改 |
| T7 | `InputCommand` 位掩码 | 只有 7 位；新交互采不到且**静默**丢输入 | B0 先扩位，再让任何新交互进回放 |
| T8 | 调查类界面 | 旧版 [10] 纸上备忘 / 镜中档案；聚光灯 [12] 卷宗 / 文书翻阅 / 推理 | **合并成一个 PRP（B4）**，按聚光灯的数据形状做 |
| T9 | 章节 / 阶段结构 | 旧版 [14] 章→幕→场；聚光灯 [11] 十二阶段 | Narrative 是同一台阶段机；**以聚光灯为主，旧版只做映射规范（B5）** |
| T10 | 存档分区 | 现有 10 个 `ISaveData` 分区（含 `Settings` v2 与只当档案的 `DialogueReadProfile`）；`MonsterSaveData` / `PlayerSaveData` **不实现 `ISaveData`**，嵌在 `EncounterSaveData` 里 | 新分区必须带版本迁移与测试；想单开 Player / Monster 分区得先改 `EncounterStep` 的 `Capture` |
| T11 | 多人共工作区 | 至少两条会话同改 `SampleScene` 与字体资产（`roadmap.md:349` 风险 5） | 提交按文件挑；场景改动走预制体 |

---

## 5·把 128 条待定推进到「可答复」的路径

工程侧能做的只有两件：**把失效的划掉**、**把仍需答复的整理成一张表**。

1. **划掉已失效的**：三结局相关（#14 #25 #26 #28）、镜裂相关（#1 #4 #5 #6）、收押 / 归还 / 账 / 镜中窥探 / 断其去处相关 —— 逐条归类见配套判据文档第 C 问。
2. **整理仍需答复的**：合并到 `features-spotlight/待策划拍板问题.md`（1754 行）的编号体系，**一处答复、多篇回填**；不要新建第二份真源（`features-spotlight/00:7`）。
3. **补上漏收的**：57 条未表面化条目里，把仍是新目标问题的补进答复表；重点是 `[06] 9.11` 被识破、`[04] 9.6` 收押失败反馈（已作废）、`[11] 9.9` 戴皮变量。
4. **口径说明**：在 00 里加一句，说明「128 条 = 矛盾 55 + 待定 66 + 歧义 7」且不含 §9 之外的行内 `[待定]`，避免后续引用失真。

### 5.1 可以立刻拿去问策划的 11 条（判据已算好，不用再筛）

判据文档把 128 条筛完，得出 **93 条已失效 / 24 条变成新目标的别的问题 / 只剩 11 条仍需新目标答复**。这 11 条逐条带「在新目标里对应什么」与出处，完整表见 [`旧G组-新目标映射与遗留资产.md`](旧G组-新目标映射与遗留资产.md) 第 5.3 节：

| 旧编号 | 问题 | 新目标里归谁 |
| --- | --- | --- |
| [06] 9.1 | 面具库存上限（可同时持有几张） | S5 皮与面具的持有规则 |
| [06] 9.3 | 生效时间与冷却的数值、计时单位 | S5（只有执事皮写了「生效中」） |
| [06] 9.4 | 冷却与剩余时限是否跨场景 / 跨存档 | S5 背包跨阶段与存档 |
| [06] 9.6 | 「记忆」以什么形式交给玩家 | 新 [12] 附身取得线索的呈现 |
| [06] 9.11 | 被识破：谁能识破、识破后果 | S2 六种露馅之外的「谁识破借来的身份」 |
| [06] 9.13 | 「归还面具」是否就是「归还」操作 | S5 道具消耗 / 失效规则 |
| [08] 9.1 | 四种手段各自的胜利判定 | S3 / S4 摆脱与失败判定 |
| [08] 9.9 | 躲藏被发现 → 追逐还是直接一击 | S3 / S4 被抓的后果 |
| [10] 9.1 | 不做强引导 vs 头顶标记 / 边缘箭头 | 新 [12] 交互提示与高亮轮廓 |
| [10] 9.2 | 调查界面的形态、条目结构 | 新 [12] 三块面板的形态与结论用途 |
| [15] 9.6 | 卷轴插图是否含在演出管线里 | D3 已落地待验收；聚光灯 UI 表另有字幕条与章节卡 |

**外加 6 条换了对象的矛盾**（#2 体量、#3 章节叫法、#10 两界切换、#11 分支口径、#13 皮面具来源、#23 正面战 vs BOSS 血量）—— 它们在新目标下仍然存在，只是换了问法，见判据文档第 5.5 节。

**合并原则**：这 17 条按 `features-spotlight/待策划拍板问题.md` 的编号体系回答，**不要新建第二份真源**（`features-spotlight/00:7` 明文要求）。

---

## 6·必须等策划的，与可以并行推进的

| 类别 | 内容 |
| --- | --- |
| **等策划**（阻塞） | §8.1 第 1–6 条；特别是第 1 条「阶段一用 sp02 / sp03 / 拼」——它卡 S1、S3、S6、S7、S8 的阶段一全部内容 |
| **可并行**（工程侧自足） | B0 位掩码扩位、B1 妖表底座、B2 判据、B3 接线、B5 阶段规范、B6 叙事呈现缺口清单 |
| **等美术** | 对话框 / 立绘 / 插图规格（影响 B4 与聚光灯 [13] 系统界面清单） |
| **只能人做** | 各模块视觉验收；`/review-change` 授权提交；策划答复；美术规格 |

---

## 7·验证与交付标准

每波收口沿用 `docs/module-dev-spec.md`：编译零错误、EditMode 全绿、Showcase PASS、code-reviewer PASS、三件套同步。

- C# 改完跑 `python .claude/skills/project-lint/lint.py <文件.cs>`；lint 不代替编译和测试。
- 配置或文档改动后跑 `python .claude/skills/evolution/gc_scan.py`。
- 新资产必须配套 `.meta`；生成物不手改。
- 交付前检查 diff、临时代码、实际测试结果；缺 Unity 连接或解释器时**明确报告未完成的验证**。

---

## 8·进度回填与心跳规范

**回填时机**：每波收口、每次用户批复、每次策划拍板一条。
**回填内容**：第 1 节进度列 + 本节日志一行，**必须附证据**，禁止只写「已完成」。

| 日期 | 波次 | 变更 | 证据 | 回填人 |
| --- | --- | --- | --- | --- |
| 2026-10-07 | — | 建立初稿（旧版 G 组规划） | `git status` 干净；`Boot.unity` guid 反查 11 个 Installer；`yao.xml:6-18` 11 列 | 主窗口 |
| 2026-10-07 | — | 三路调研回收：旧版 128 条逐份核对一致；代码承载力逐条实测；双策划冲突与 PRP 吞吐摸底 | 见本文件 0.2 / 0.3 节引用的行号 | 主窗口 |
| 2026-10-07 | — | 用户拍板「冲突时以新目标为准」；本文按新目标重写；12 项判据表落地 | 本文件第 0.1 / 2 节 | 主窗口 |
| 2026-10-07 | Q0 | 派单三路（在飞）：B2 映射判据 `edf99f1b`、B1 妖表底座 `26031b6b`、B0 位掩码扩位 `363a026e`。**共同边界**：不改 `Boot.unity`、不改 `GameInput.inputactions`、不跑全量 / PlayMode / 回放，测试限 `Game.Tests.EditMode` 对应 group；不 commit | 待回收后逐单核验 | 主窗口 |
| 2026-10-07 | Q0 | 主窗口独立复核 B1 中间产物：`tier / killable / drop_items` 三列方向成立（依据 `features-spotlight/06_怪物分层.md:113` R1、`:121` R9、`:260` 模块缺口「没有怪物种类表」）；`gc_scan` 抓出缺 `.meta`；表头把旧版已冻结文档写成列归属，已推回纠正；另立 B7（子层 `A·下/B·下` 未建模）与 B8（道具类别缺皮/面具/钥匙） | `gc_scan.py` 输出；`Generated/yao/Yao.cs:90,94,98`；`Generated/EItemCategory.cs:17-34` | 主窗口 |
| 2026-10-07 | Q0 | B2 判据完成并验收：`旧G组-新目标映射与遗留资产.md`（348 行）；主窗口抽验四处引用全部为真（`06_怪物分层.md:130` 表头「可否击杀 / 掉落」、`:272` 三层奖励梯度、`09_BOSS战.md:142` R34 牙削弱、`02_身份暴露与怀疑.md:131` R27 龙族揭露）；128 条归类 93 / 11 / 24，算术自洽 | 该文件第 1、3、4、5 节；`pwsh` 逐行比对 | 主窗口 |
| 2026-10-07 | Q2 | 派单两路（在飞）：B5 阶段结构规范 `1f8d1aec`、B6 叙事呈现缺口清单 `6bc0d0c7`。边界：只写 `docs/planning/` 下一份文档，不碰 `Assets/` `Tables/` `Boot.unity` `GameInput.inputactions`，不 commit | 待回收 | 主窗口 |
| 2026-10-07 | Q0 | 主窗口独立复核 B0 中间产物：补 bit7/8/9（Tame/Interact/Inventory）方向成立；**「不升回放格式版本」逐字比对判据后裁定成立**（`ReplayFormat.cs:39-47` 五条全属字节布局变化，本次未动 `SerializedSize` 与偏移；旁证 `InputCommandTests.cs:40` 对 `ButtonRun` 的注释「老录像的位含义不变」）；同时指出测试钉死表 `InputCommandTests.cs:47-52` 必须同步加新位，否则新位不受单一位/重叠校验覆盖——已推回该单 | `git diff InputCommand.cs`；`ReplayFormat.cs:39-54`；`InputCommandTests.cs:34-52` | 主窗口 |
| 2026-10-07 | Q0 | B0 测试同步已落地（`InputCommandTests.cs` 加三位的位置断言 + 进 `ButtonConstants_AreSingleBitsThatNeverOverlap` 数组 + 新增 `NewActionBits_SetAndClearIndependently`）；**.meta 已由 Unity 生成**（`YaoCatalog.cs.meta` guid `9c40f25e…`，Unity 进程在跑）；B0 额外加了 `LiveInputSource.FindUnmappedGameplayActions` 守卫 + `ExemptActionNames` 豁免表，把「动作图加动作而内核没接」从静默丢输入变成点名 Warn；**其守卫用例尚未落地，仍在本轮内** | `git diff InputCommandTests.cs`；`YaoCatalog.cs.meta`；`Get-Process Unity`；`git diff LiveInputSource.cs` | 主窗口 |
| 2026-10-07 | Q0 | 复核 B1 表头归属纠正：已改为「冲突以聚光灯为准」并逐列写明新目标依据，不再把已冻结的旧版稿子写成读者；`tier` 列注释自行解决了子层问题（引 `06_怪物分层.md:126`）——**主窗口验该行原文确为「A·下＝原文写在 A 条目的下一级」，B7 据此销项**；残留缺口另立 B9（「怎么杀」无落点） | `Tables/Defines/yao.xml:1-25,39`；`06_怪物分层.md:126` | 主窗口 |
| 2026-10-07 | Q0 | 验收 B0 守卫用例质量：`LiveInputSourceTests.cs` 用**真 `GameInput` + 真设备事件**（`InputSystem.QueueStateEvent` + `Update`）验「动作名 → 内核位」，并带负对照 `FindUnmappedGameplayActions_WithActionNobodyWired_NamesEveryOneOfThem`（喂合成动作图必须点名报出漏接项）——有负对照，「返回空列表」才不可能是空转 | `git diff LiveInputSourceTests.cs` | 主窗口 |
| 2026-10-07 | Q2 | 验收 B5：文档落盘并抽验六行引用为真（`11_剧情流程与章节结构.md:99` 两界分布原文、`:104` 「### 3.1 阶段总表」、`:124` 3.2 表标题、`:143` R7 战斗关四个、`spotlight/01_剧情大纲.md:5` 阶段一标题、`NarrativeContent.cs:9` 五种 StageKind）；13 段列全并自做分布 / 战斗关核对 | 该文件第 2 节；`pwsh` 逐行比对 | 主窗口 |
| 2026-10-07 | Q2 | **高价值发现入账**：B5 读源码（非转述模块文档）发现 `NarrativeCatalog.cs:79-80` 的 `Validate` 对 `Battle` / `IssueRequest` / `RequiredParts` **一律抛异常**——聚光灯四个战斗关阶段现在进不了表；主窗口验 `:69` 注释与 `:79-80` 断言逐字吻合，已写入 0.2 节「发现 4」，并给「[14]/[15] 不需新模块」这句加了限定 | `NarrativeCatalog.cs:69,79-80`；`NarrativeContent.cs:9` | 主窗口 |
| 2026-10-07 | Q2 | 验收 B6：文档落盘并抽验为真——字幕开关**确缺**（`SettingsView.cs` 只有 `vsyncToggle`；`SettingsSaveData.cs:31-55` 无任何字幕字段）；**N4 冲突确有原文**（`performance-module-guide.md:28`「全屏立绘叠加舞台（含上下黑边）…2026-09-28 用户定：整条路线下架」vs 聚光灯 UI 表要「底部黑边字幕条」）；同一「不做」表独立证实其另三条不做项（`:29` 相机运镜、`:30` 配音、`:32` 演出中途存档） | `SettingsView.cs` grep；`SettingsSaveData.cs:31-55`；`performance-module-guide.md:26-33` | 主窗口 |
| 2026-10-07 | Q0 | **主窗口代修编译阻塞**：`YaoCatalog.cs` 的 `DropItemsOf` 用 `List<int> ?? int[]`（`??` 两向都不能隐式转换，外层 `(IReadOnlyList<int>)` 发生在 `??` 求值之后、救不了），`Editor.log` 实测 `CS0019` + `Tundra build failed`、`Game.Runtime.dll` 卡在 02:14:36。连发三条消息后该文件仍未再动（当时最后写入 02:57:13），遂由主窗口改为显式判空并在代码里留「主窗口代修（2026-10-07）」注释；同步确认本文件其余 `??`（`:51` throw 表达式、`:152` `string ?? string`）均安全 | `Editor.log` 末尾原文；`YaoCatalog.cs:124-137`（03:01:45 复核仍在案） | 主窗口 |
| 2026-10-07 | Q0 | **编译阻塞根因定位**：`Editor.log` 末尾连续三条 `[Game] InputService 就绪，已启用 Gameplay 动作图` → 编辑器在**反复进 Play 模式**；Play 模式下 Unity 不做脚本重编，故主窗口 03:01:45 落盘的修复进不去、`Game.Runtime.dll` 恒停在 02:14:36。已按「退 Play → 强制重编 → 确认 dll 时间戳 → 确认控制台无 `Game.Runtime` 报错 → 再跑测试」的顺序要求 B1 操作，并给出退路：退不出 Play 就如实记「未验证」，不要凑结果 | `Editor.log` 末 3 行；`Library/ScriptAssemblies` dll 时间戳；Unity MCP 实测监听 `127.0.0.1:6401`（PID 11256＝Unity 进程） | 主窗口 |
| 2026-10-07 | Q0 | **编译全部恢复**：B1 按建议把 `YaoTableTests.cs:122` 的 `Is.AnyOf("A","B","C")` 改为 `Is.EqualTo("A").Or.EqualTo("B").Or.EqualTo("C")`，`Game.Tests.EditMode.dll` 于 **03:04:27 编出**，日志末尾只剩正常资产后处理（`PostProcessAllAssets` / `Hotreload`）、无 `error CS`。五个程序集时间戳：Runtime 03:03:52 / Editor 03:03:53 / PlayMode 03:03:52 / Showcase 03:03:53 / **EditMode 03:04:27**。两单（B0 / B1）自此可各自跑限定范围的测试 | `Library/ScriptAssemblies` 时间戳；`Editor.log` 末 8 行；`YaoTableTests.cs:122` | 主窗口 |
| 2026-10-07 | Q0 | **测试已实际执行**：`Editor.log` 出现 `MCP-FOR-UNITY: [TestRunnerNoThrottle] Restored Interaction Mode after test run`，且日志含 `Game.Tests.EditMode.Replay.*` 用例名（`DriftDetectionTests`、`ReplayFormatTests`）→ EditMode 测试确实跑起来了（B0 的 `Replay` 组）。**汇总数字不在 Editor.log、也不落 `TestResults` XML**（MCP 把结果直接回给调用方），故通过/失败计数须以两单回执为准。主窗口不代为推定 | `Editor.log` 中 `Restored Interaction Mode after test run`；`Library/ScriptAssemblies/Game.Tests.EditMode.dll` 03:04:27 | 主窗口 |
| 2026-10-07 | Q0 | **B1 回执收回，两条待补位填满**：B1 报 `run_tests(EditMode, group_names="Game.Tests.EditMode.Mirror")` → **150/150 通过**（2.1s，job `07dd58e5f1414ffe8d68fee9a8e36bd0`）、lint 三个 `.cs` 全绿（并用已知违规探针验证 linter 真在工作）、`compile_errors: []`。**主窗口独立复核五项**：`Game.Tests.EditMode.dll` 03:07:43、`Game.Runtime.dll` 03:17:00、`YaoCatalog.cs.meta` guid `9c40f25e7a78e4744aa615e4fef97045`、`MonsterTier.cs`(+`.meta`) 已摘除、主窗口代修处未被回改。两单合计覆盖 EditMode `Simulation`+`Replay`+`Mirror` 三组 **210 条全绿** | `Library/ScriptAssemblies` 时间戳；`Test-Path` 逐项；`YaoCatalog.cs.meta` | 主窗口 |
| 2026-10-07 | Q0 | **B1 补充了比主窗口诊断更精确的根因**：它按「`manage_editor(action=stop)` 确认不在 Play → `refresh_unity(mode=force, scope=all, compile=request)`」操作，**第一次 refresh 返回 `recovered_from_disconnect: true`**，重试后 dll 才换新 —— 即「不是没人刷新，是刷新没落地（编辑器被别的东西占住过）」。主窗口此前凭 `Editor.log` 里三条 `InputService 就绪` 判定为「反复进 Play」，方向对但不是完整解释；**以 B1 的 MCP 实测为准**。这条对后续「dll 不更新」类问题有复用价值：先试 stop + force refresh，并留意 `recovered_from_disconnect` | B1 回执原文；`Editor.log` 三条 `[Game] InputService 就绪` | B1 / 主窗口 |
| 2026-10-07 | Q0 | **D4 主窗口亲自跑通**：`run_tests(mode=EditMode, group_names="Game.Tests.EditMode.Core")` job `fd220ed03d3a4e33b9f9f1b386476e44` → **234/234 通过、0 失败、0 跳过**（8.59s）。B0 担心的 `InputTestFixture` 与 `Core/InputServiceTests` 全局输入系统冲突未发生。至此 EditMode 四组累计 **444 条全绿**（Core 234 + Mirror 150 + Simulation/Replay 60） | `get_test_job` 返回的 `summary`；Unity MCP stdio 直连 | 主窗口 |
| 2026-10-07 | — | **D6 按路径分三次提交**（用户授权）：`6f069a1` feat(mirror) 14 文件 +987 −28、`bacc357` docs(mirror) 3 文件、`5340627` docs(planning) 5 文件。提交后核对 `git show --stat` 均只有本批文件、无 AI 署名、`git status` 干净。**未 push** | `git log -3`；`git status --short` 空 | 主窗口 |
| 2026-10-07 | — | **D3 开调查界面 PRD**：`PRP/survey-interface/prd.md` 新建（82 行），按「先定要什么、数据形状待 §8.1 第 1 条答复后补」的范围收窄写；含 G1–G7 目标、验收标准 A1–A11（每条标机器可判或肉眼可验）、8 条待确认问题。**主窗口亲写**（PRD 属方案设计，按 `.claude/rules/model-routing.md:10` 不下派） | `PRP/survey-interface/prd.md` | 主窗口 |
| 2026-10-07 | Q2 | 复核 B6 顺手报的两处 guide 行号漂移，**两处均确认为真**：`DialogueController.cs:42` 才是 `ChoiceRefreshInterval`（guide `:192` 写 `:31`）、`PerformanceService.cs:59` 才是 `InputMap`（guide 写 `:42`）→ 立为 B10 | `pwsh` 读两文件对应行 | 主窗口 |

**本文的退场条件**：当第 1.1 节全部为「完成」或「已作废」，且第 1.2 节的 S 项状态由 `roadmap.md` 接管时，本文并进 `roadmap.md` 第 3 节，移入 `docs/history/`。

### 8.1 收口状态（2026-10-07）

**已闭合**：B2 / B5 / B6 三份文档交付并通过主窗口抽验；B0 与 B1 的**代码全部就绪且编译通过**（五个程序集 03:03:52–03:04:27 全绿）。

**未闭合的只有测试计数，且成因已排除**：
- B0 的 EditMode 测试**确实跑过**（`Editor.log` 有 `Restored Interaction Mode after test run`，用例名出现在 `Game.Tests.EditMode.Replay.*`），**日志中没有断言失败**；但**通过 / 失败计数不在日志、也不落 `TestResults` XML**（MCP 把汇总直接回给调用方），所以本文不写一个没人见过数字的「N/N 通过」。
- B1 的 EditMode `Mirror` 组测试结果**尚未回执**。
- 两单的最终回执到达后，请在下列两行补齐数字，再改 `B0` / `B1` 的进度列：

| 待补 | 应填什么 | 证据来源 |
| --- | --- | --- |
| ~~B0 测试计数~~ | ~~EditMode `Game.Tests.EditMode` 的 `Simulation` / `Replay` 组通过数~~ | **已收回（2026-10-07）：60 / 60 通过、0 失败、0 跳过**（`run_tests(mode=EditMode, assembly=Game.Tests.EditMode, groups=Simulation+Replay)`，2.5s）。**性质：该单实测回报**，主窗口无 MCP 工具、无法独立复现该数字；可独立确认的是编译全绿与测试确实执行过（见第 8 节日志） |
| ~~B1 测试计数~~ | ~~EditMode `Mirror` 组通过数 + lint 实际输出~~ | **已收回（2026-10-07）：150 / 150 通过、0 失败、0 跳过**（2.1s，job `07dd58e5f1414ffe8d68fee9a8e36bd0`）+ lint 三个 `.cs` 全绿、`compile_errors: []`。**性质同上（该单实测回报）**；主窗口已独立复核其 dll 时间戳、`.meta` guid、`MonsterTier` 摘除、代修处未被回改 |

**两单的实测计数合并起来覆盖 EditMode 的 `Simulation` + `Replay` + `Mirror` 三组，共 210 条用例全绿**；`Core` / `Monster` / `Dialogue` / `Quest` 等其余组本轮未跑（见 D4）。

**B0 交回时另提两点待判**（原文照录，未改写）：
1. **Core 组未被本轮验证**：`Tests/EditMode/Core/InputServiceTests.cs` 会新建真 `GameInput` 并启停动作图，而 B0 的用例现在包在 `InputTestFixture` 里（会 Save/Reset/Restore 全局输入系统）。B0 已实测跑完后全局设置与设备集均还原（`devices=[Keyboard,Mouse]`），但按写死的测试范围它没跑 Core 组。**要收紧跑一次 `group_names=["Game.Tests.EditMode.Core"]`（约 1 分钟）**。
2. **asmdef 那一行是共享文件**：会影响所有 EditMode 测试的编译面（多引一个 `autoReferenced:false` 的测试专用程序集，方向为 Tests → 测试框架，合规）。**主窗口已复核该行并以 `invariants.py`（含 asmdef 依赖方向检查）未报警作为旁证**。

**B0 交回时的越界声明**（照录）：跑测试用的 MCP `run_tests` 会清控制台，可能刷掉别的会话正在看的日志。

**本次留下的工程侧改动**（工作区未提交，**15 个改动文件 + 2 个新增 + `docs/planning/` 未跟踪**，2026-10-07 终态实测）：

| 归属 | 文件 |
| --- | --- |
| B0（位掩码） | `Core/Simulation/InputCommand.cs`、`Core/Simulation/LiveInputSource.cs`、`Tests/EditMode/Simulation/InputCommandTests.cs`、`Tests/EditMode/Simulation/LiveInputSourceTests.cs`、`Tests/EditMode/Game.Tests.EditMode.asmdef` |
| B1（妖表底座） | `Tables/Defines/yao.xml`、`Tables/Data/yao/{1,2}.json`、`Core/Config/Generated/yao/Yao.cs`、`Data/Config/yao_tbyao.bytes`、`Runtime/Mirror/MirrorInstaller.cs`、`Tests/EditMode/Mirror/YaoTableTests.cs`、**新增** `Runtime/Mirror/YaoCatalog.cs` + `.meta` |
| B1（模块三件套） | `ai-docs/docs/modules/mirror/mirror-module-guide.md`、`mirror-external-api.md`、`mirror-extension-guide.md` |
| 主窗口 | `docs/planning/` 四份规划文档（未跟踪） |

**其中 `YaoCatalog.cs` 的 `DropItemsOf` 由主窗口代修**（见第 8 节日志首条代修记录）。`Boot.unity` / `GameInput.inputactions` / `SampleScene.unity` 全程未被触碰（波次纪律 T5 / T6 / T11 守住）。B1 另报：Luban 重新生成会把产物写成 CRLF，它把本次被重写的换行统一回 LF —— **主窗口已核**：`Generated/narrative/*` 与 `narrative_*bytes` 在 `git status` 中已无改动，即「只有换行变化」的说法成立；`Generated/yao/Yao.cs` 的 diff 为 +24 −6，与新增 3 列相符。

---

## 9·Q1 波（B3 Taming / Disguise 接线）派单前置

**为什么它是下一单**：S1「换皮与附身」的两个候选载体就是 Taming 与 Disguise（`roadmap.md:170`、`:171`），而这两个模块现在的真实体量是「**接线缺失**」而不是「功能缺失」：

| 模块 | 现状（实测） | 缺什么 |
| --- | --- | --- |
| Taming | 3 个 .cs；`TamingRules` / `TamingIntent` / `TamingSceneController` | **无 Installer**（`Boot.unity` 的 11 个 Installer 里没有它）→ 不在容器里，别的模块拿不到；**无 `IReplayState`** → 录制回放拿不到附身状态；`TamingSceneController` 自己 `Instantiate(inputActions)`，与正式输入链是两套 |
| Disguise | 全文 9 行，`AllowsEnemyAttack => !isDisguised` | 状态本体在 `PlayerModel.IsDisguised`（`PlayerModel.cs:27`）；**无身份 / 面具 / 时限 / 冷却 / 污染 / 被识破概念**；只有 `MonsterRules.cs:238` 一个消费点 |

**B0 已为它铺好的路**（可直接用，不必重做）：
- `InputCommand.ButtonTame` = bit7 已存在且有测试守着 → Taming 进确定性内核的位不用再补。
- `LiveInputSource.WiredActionNames` 里已有 `"Tame"` → `Gameplay/Tame` 已经在采样侧接通。
- `LiveInputSource` 的守卫＋豁免表已在，**将来给 Disguise / Taming 加新动作名时会被要求写清理由**，不会又变成静默丢。

**这单要做的三件事**（不要顺手做玩法）：
1. 把 Taming 接进 `GameplayInstaller` / 容器（**先确认注册顺序 = 初始化顺序这条约定**，`GameLifetimeScope` 的注册顺序有语义）。
2. 给 Taming（以及 Disguise 的状态）补 `IReplayState` 快照；**注意 `ReplayFormat.cs:45-47` 的判据——`IReplayStateProvider` 注册顺序或已注册状态的 Serialize 字段一变就必须升 `CurrentFormatVersion`**，这单大概率要升版，升版前先确认 `MinimumReadableFormatVersion` 怎么处理。
3. 拆掉 `TamingSceneController` 自建输入那套，改走正式输入链；保留回放舞台（`SampleScene` 的 `TamingDemo` 挂点）。

**只能人做**：接完的视觉验收（附身切换的镜头与操控是否正常）。

### 9.1 但 B3 有一个真实的返工风险，先说清（这是它还没开派的原因）

`roadmap.md:170` 的要求是「Taming **改造为附身的载体**」—— 这是**语义变化**，不只是接线。如果现在就把 Taming 当下的「按键切换操控对象」语义快照进 `IReplayState`（第 2 件事），等 S1 定了附身语义（§8.1 第 2 条：附身与使用皮 / 面具是一套还是两套、附身条件、被附身者怎样、怎么退出）之后，快照字段要重写一遍，而**每次重写都要升 `CurrentFormatVersion`**——正是 `roadmap.md:347` 风险 3 说的「回放快照格式会反复升版」。

所以 B3 的拆法建议是：**第 1、3 件（接容器、拆自建输入）可以先做**，它们与语义无关、不产生返工；**第 2 件（快照）等 S1 的附身语义定了再做**。派单时按这个拆法派，不要合成一单。

### 9.2 什么时候 B4（调查界面）能开

`B4` 与 `features-spotlight/00 §8.1` 第 1 条（阶段一用哪套设计）绑得比看上去紧：三块面板（卷宗 / 文书翻阅 / 推理）要呈现的信息，取决于阶段一到底是 sp02 的附身对话路线还是 sp03 的文书「对不上」路线。**在策划答复 §8.1 第 1 条之前开 B4 的 PRD，等于照着猜的数据形状写面板**。

但 `/refine-prd` 的门一（需求只写「什么」、不写「怎么」）允许先做一半：面板的**用途、玩家故事、验收标准**可以先定，数据形状留到答复后补。要不要现在开这一半，属于排期偏好，交用户决定。

---

## 10·等你拍板 / 交接

**闭包件状态（2026-10-07 自动轮结束时的实况）**：目标（重写规划 + 12 项判据 + 整合三路调研证据 + 波次派单 + Q1 两单回收合并 + 逐波回填进度）**已全部达成**。收尾时 B0 已回收（60/60），B1 的 `Mirror` 组测试回执仍在飞。此后不再自动派单，下列事项挂起等你定。

| # | 事项 | 为什么要你定 | 成本 |
| --- | --- | --- | --- |
| D1 | **策划答 `features-spotlight/00 §8.1` 第 1–6 条**，首要「阶段一用 sp02 / sp03 / 拼」 | 卡 S1、S3、S6、S7、S8 的阶段一全部内容，也卡 B4 调查界面 PRD | 人 |
| D2 | **黑边字幕条与 2026-09-28 决定冲突**：聚光灯 [13] #12 要「底部黑边」，而 `performance-module-guide.md:28` 记着「全屏立绘叠加舞台（含上下黑边）整条路线下架」 | 按表做＝推翻旧决定；两条路只能选一条 | 一句话 |
| D3 | **B4 调查界面要不要现在开 PRD 的一半**（用途 / 玩家故事 / 验收标准，不碰数据形状） | 纯排期偏好 | 一次 `/refine-prd` |
| D4 | ~~补跑 EditMode `Core` 组~~ | — | **已完成（2026-10-07，主窗口亲自跑）**：`run_tests(mode=EditMode, group_names="Game.Tests.EditMode.Core")` job `fd220ed03d3a4e33b9f9f1b386476e44` → **234/234 通过、0 失败、0 跳过**（8.59s，`resultState Passed`）。**原先担心的 `InputTestFixture` 与 `InputServiceTests` 全局输入系统冲突未发生** |
| D5 | **测试 asmdef 那一行是否接受**：`Game.Tests.EditMode.asmdef` 多引 `Unity.InputSystem.TestFramework` | — | **用户 2026-10-07 决定：接受** |
| D6 | ~~本次工作区改动是否提交~~ | — | **已完成（2026-10-07，用户授权）**：按 `docs/commit-convention.md` 分三次按路径提交 —— `6f069a1` feat（14 文件，运行时 + 表 + 测试）、`bacc357` docs（mirror 三件套）、`5340627` docs（规划四份 + PRD）。提交后 `git status` 干净、无 AI 署名、无他人文件混入。**未 push**（用户未授权） |
