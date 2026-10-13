# 21Days 交接文档 — 未完成事项

> **给谁看**：接手本项目的开发者。
> **前置阅读**：`CLAUDE.md`（硬规则与目录约定）→ `ai-docs/project-guide.md`（共用约定）→ `docs/architecture.md`（框架层与各服务契约）。
> **现状口径**：当前接线以源码与场景为准，剩余验收见本文；测试数字只对应所列日期与范围。
> **系统层**：加载黑幕相位守卫、回放等待揭幕与不进队列的存档角落提示已实现。2026-10-07 历史记录：EditMode 1115/1115、Session 回放 2/2、Exploration 8/8；视觉验收仍待确认。
> **S 组**：Identity / Stealth 已挂 Boot，遭遇接了身份禁攻、遮挡与处决；World 已挂 Boot 并有两张灰盒；TurnBased 仍为纯规则加白盒回放，正式战斗入口 / UI 未接。
> **S 组历史验证（2026-10-07）**：EditMode 1836/1836；identity / stealth / turnbased 回放 PASS，world 回放 FAIL（13 个失败检查点）。此记录不能替代之后的回归。
> **2026-10-09 接线波（主窗口独立复跑）**：**全量 EditMode 2417/2417**（0 失败 0 跳过，含三方包用例；锚点 `Game.Tests.EditMode.dll` 新于最新源码）；回放 `Game.Tests.Showcase.Stealth` **3/3 PASS**（连跑两次，39 个检查点、失败 0、异常 0）、`Game.Tests.Showcase.Inventory` **PASS**（2/2，26 检查点）。本波关掉的是「有代码没接上」的六项（见 §1.11）——其中五项当日落地并验证，**第 ④ 项当日判定契约不成立、等策划拍板，同日第二波按用户决定接通**（复跑见下一行）。**这不代表全仓所有 Showcase 回放都已重新跑过**。
> **2026-10-10 复跑与 demo 收口**：全量 EditMode **2439 / 2439**（0 失败 0 跳过）；`Showcase.Battle` **4 / 4 PASS**、`Showcase.Stealth` **3 / 3 PASS**、`Showcase.Inventory` **2 / 2 PASS**；`gc_scan` 只剩字体资产一项。§1.11 ④ 的复跑因此关闭，「较量」那一局原先卡在战斗阶段的真因是**内容只声明了 `Victory`**（机制没坏），细节与证据见 §1.11 末与 §1.12。
> **依据**：[S 组规划](docs/planning/S组落地总规划.md)、[状态字典](ai-docs/docs/story-facts.md)、[World PRP](PRP/world-scenes/prp.md)、[处决 PRP](PRP/stealth-execution/prp.md)；报告为 `Logs/verify/` 生成物。

## 0. 现状一句话

主流程已有通过记录：标题页 → 进入探索场景 → 潜行 / 战斗 / 对话 / 演出 / 任务 / 存档。Narrative 依赖的 DialogueService 遗漏片段已补交；补交后的定向回归与人工回放验收已有记录，但尚未在独立干净检出上完成最终提交验证。本轮验证不代表全仓所有测试已重新跑过。

**本轮验证范围**：Performance EditMode 150 项、演出 Showcase 6 项通过，失焦键盘自测与输入设置恢复检查通过；演出回放报告为 `Logs/verify/performance/20260929-042650/report.md`，失焦自测报告为 `Logs/verify/selftest/20260929-025916/report.md`（报告为生成物，不入库）。角色隐藏已覆盖直接播放、对白插入及场景触发；演出期间世界 Tick / 玩家位置不变、Timeline 继续推进，结束后世界恢复。时停已验证，无需进一步修改。用户已完成一次视觉验收，暂未发现问题。

**后续验证与当前交付边界**：

- Core/Dialogue/Mirror/Narrative/Quest/Session 联合 EditMode 647/647、Dialogue/Mirror/Narrative PlayMode 16/16，均零失败零跳过；这是该轮联合回归结果。Narrative 最新记录另有补交后 EditMode 22/22、三条回放通过及用户人工确认，详见 §1.2 和 `PRP/narrative-dialogue/tasks.md`。
- 气泡补修已提交并完成 review-change：井边妇人旧副本接回共用预制体，组件增加视口约束与临时缩小/恢复。Dialogue EditMode 125/125，零失败零跳过；两条气泡回放任务成功，报告 `Logs/verify/dialogue/20260929-075616/report.md` 为 PASS，覆盖村民侧面、妇人侧面与正面。最终 MCP result=null，不补推 passed/skipped 细分；该报告检查点失败 0、运行时异常 0。
- 气泡姓名、正文与边框的截图核对已有记录；用户视觉确认仍待完成。
- 提交拆分依据见 `PRP/commit-batches-2026-09-29.md`。它是历史清单：C2 气泡及顶部四文件补交均已入库，不再列为待办；其它批次以仓库记录核对。

**框架可运行与正式内容完成是两回事。** 当前已有 `Game.Core`、运行时模块、主流程、编辑器工具、回放与埋点体系，外加照镜 demo（`PRP/mirror-core`）；剩余验收和内容工作见下文。

**没做的分两层，量级差很远：**

- **§1 / §2 是已有模块的收尾与待拍板事项**，具体范围见下面的清单。
- **§3 是内容层，仍有大量待定**：2026-10-06 起策划以《聚光灯》为准（`docs/design/spotlight/`，玩法拆分见 `docs/design/features-spotlight/`），照镜 demo（`PRP/mirror-core`）随旧版策划冻结。`docs/roadmap.md` 的 C1–C3/B3/W2 状态已同步，旧“20 条完全没开始”不再作为当前统计。这一片仍需策划与美术输入。
- **S 组的剩余工作**：区分接线、白盒验收与正式内容。Identity / Stealth / World 的 Installer 和相关 Showcase 已存在，不重新派发“从零接线 / 新建回放”。World 回放尚有失败，新开局 / 读档进入世界场景仍有缺口；TurnBased 正式入口与 UI 未接。**（2026-10-09 更正）**「处决受怪物表 `defeat_method` 内容限制」这条已过期：`Tables/Data/yao/3.json` 的市令就是 `defeat_method = 暗杀`，`Boot.unity` 的 `kindId` 也已指到 1003，接线与数据两侧都通；剩下的只是**回放验收**（见 §1.11）。
- **正式内容**：真实章节、怪物数值和场景 / UI 美术仍需外部输入；S6 / S8 / B4 的待定事项按策划总览处理。两份补交的怪物交互与回合制原件已入库，歧义见 `docs/design/features-spotlight/待策划拍板问题.md`。

---

## 1. 可以直接开工

### 1.1 最终提交的独立验证

- 原“遗漏提交”已关闭：DialogueService 的稳定 targetId 重载、Completed 传递及三份契约文档均已入库，不再要求补交。
- 剩余工作是在隔离的干净检出上记录最终提交号，完成编译和相关联合回归；不通过清理或回滚共享工作区来制造干净状态。
- 补交后 22/22 与三条 Narrative 回放、此前 647/647 与 16/16 均不能代替最终提交的独立检出验证。尚无该项验证已执行的证据。
- 完成后回填 `PRP/narrative-dialogue/tasks.md` 的最终提交独立验证项，并关闭本节；不重复实现已有接线。

### 1.2 Narrative 接线的剩余验收

C1–C3/B3 的运行接线、真实条件源、最小表/校验器、任务完成映射剧情标记与 Session 稳定状态保存已实现并有回归证据，不要再创建第二套控制器或存档系统。

- 人工视觉确认已通过：最新实施记录载明用户观看慢速回放后反馈“好像是没问题的”。对应 `Logs/verify/narrative/20260929-094939/report.md`（实时刷新）与 `Logs/verify/narrative/20260929-095052/report.md`（取消/重试/跳过、保存/继续），记录均为检查点失败 0、运行时异常 0；验收仅限这三条覆盖行为。
- 回放关键画面的 `Wait(3f)` 阅读停顿已入库，x2 时约停 6 秒。上述运行证据来自 Narrative 实施记录，仅覆盖该轮验证范围。
- 未覆盖：C5/G5 对抗结果、全目标生命周期中断、全场景恢复事务、对白逐节点/逐字恢复及真实章节内容；详见 PRP 的“未纳入本批的验收欠账”。
- 调试探针曾在未传生命周期令牌、对白仍运行时直接停止 Play，出现 ObjectDisposedException；正式回放正常收尾为零异常，不据此宣称任意根作用域突然销毁均已覆盖。
- `DefaultDialogueConditionSource` 保留作未装 NarrativeInstaller 的兼容来源；不要按旧交接要求直接删除。

### 1.3 仍待处理的小修与偶发问题

| 位置 | 问题 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs:4`、`FaceDragHandle.cs:4`、`MuralFaceController.cs:3` | `Game.LailaFace` 与 `Runtime/Gameplay/` 不符，静态检查持续报告三处；注册表仍按实际目录定位，不把这条记录作为自动迁移指令 |

`ProjectStructureMenu.cs` 的 TODO 位于 `BuildRulesTemplate` 生成字符串中，是新模块骨架提示，不是现有服务缺陷。

Mirror 句柄释放/相机朝向、CharacterPuppet 重复辅助方法、对白气泡查找和失焦输入经验文档已处理并提交，不再列待办。
Performance EditMode 曾出现记录面板关闭等待 5 秒超时；单独复跑与随后 150 项全量复跑均通过，原因未定位。再次出现时保留状态与日志，不只增大超时。

### 1.4 Replay：实录已补，性能与窗口验收未完

五分钟六实体 DemoWorld 实录已提交：300.096 秒、18000 tick、691931 B；RecordTick 调用内分配 0 B，最大 0.221900 ms，有一次超过 0.2 ms。
不能把局部采样写成整帧性能验收通过。T18/G3 仍需真实玩法开关录制的逐帧 Profiler 对照、稳态 GC，以及 ReplayWindow 的真实交互验收。
报告 `Logs/verify/replay/20260929-064018/report.md`，细节见 `PRP/replay/tasks.md`。

### 1.5 UI 与探索的剩余验收

| 条目 | 尾巴 |
| --- | --- |
| A2 沉浸模式 | 玩家 / 巡逻者 NameTag 未随沉浸隐藏 |
| E8 分辨率基准与画面适配 | `SetResolution` 与窗口拖拽**未出包实测**（须构建 Player 后验证，编辑器里测不出来） |
| H7 无鼠标悬停反馈 | NPC 悬停高亮未做 |

D4 / D5 的旧「未采用」待办已过期：`DialogueView.prefab` 已采用 SlideUp（`transition: 1`），多个 UI 预制体已挂 UIButtonFeedback。已有接线不等于新一轮视觉验收；Inventory 的回放与视觉验收仍需独立核对。

### 1.6 Codex MCP 与 hooks 工作流

- 手动诊断工具 `scripts/unity_mcp_probe.py` 与说明已入库。协议客户端可用不等于各聊天原生 MCP 均可用；接手时需实际读取目标编辑器确认连接。
- “所有项目聊天自动注册 MCP、持续保活”尚未完成；不能把已注册项目 hooks 当成该能力已实现。连接成功以实际读取目标编辑器为准，测试/域重载期间出现短暂断连时先等待再重连，不反复重启健康 bridge。
- `.codex/hooks/README.md`、`adapter.py`、`test_adapter.py` 的长文记账兼容修复已入库。项目 hooks 是否生效以各会话实测为准；操作限制与处理方式见该 README，不关闭护栏绕过。

### 1.7 Laila 捏脸与表情识别：已接独立研究试玩，继续补组合测试

- **已实现**：31 个形态、17 个交互轴的采样与五类 Sentis 推理；自动更新、错误清理、稳定反馈与展示迟滞；标注工具、匿名采集、反馈保存和有界候选训练。唯一场景 `LailaRecognitionPlaytest.unity` 默认定向修复 59D，保留上一版反馈、r779 与原 51D 对照；重复旧场景及一次性搭建代码已移除。17 轴是模型输入，51D / 59D 是图内特征空间，输出仍是中性、高兴、悲伤、惊恐、愤怒五类。
- **入口与维护**：Unity 菜单 `21Days/Laila/候选试玩（含旧版对照）`；接手先读 [spec §17](PRP/laila-expression-recognition/spec.md#17-当前工程候选接手入口2026-10-04)，再读[识别模块指南](ai-docs/docs/modules/lailafacerecognition/lailafacerecognition-module-guide.md)与[试玩 / 标注说明](ML/expression-recognition/analysis/laila_v2_candidate/ANNOTATOR.md)。旧 12 轴 / v1 阶段门留在规格中作历史复现，不再照旧清单重新派工。
- **已有验证记录**：候选的 205 条 Sentis / ONNX 数值对照、21 档实际 UI、四版切换 / 清理和抓点 hover 回归见模块指南；三个新增怒点属于训练拟合，同原型预设属于暴露开发回归。2026-10-07 标注与反馈修复相关的四份 Python 测试文件共 **36/36 通过**；该结果不包含 Unity 或人工语义验收。
- **剩余工作**：冻结组合姿态反例，复核脸型表达、左右响应和跨帧稳定性；独立人工语义验收、物理鼠标与 Player 仍未完成，研究拒识未校准。先补测试缺口，再讨论场景整合；不自动追加训练、扩类或接关卡判定。公开集来源的研究模型不按正式发布模型交付。

### 1.8 Rhythm 四轨音游：曲库、校准、同局暂停已实现

- **已实现**：独立 `RhythmDemo.unity` 的等宽 DFJK 四轨、Tap / Hold、三首音乐与单谱曲库、完整单局结果、v2 个人纪录和解锁；设置草稿明确保存 / 取消，演奏中暂停 / 继续 / 重试 / 退出。Practice、校准、Combat 与自由局纪录分开；不做多难度，不改两首新曲听感。
- **校准与外部能力**：固定主轮、分级可信度、原值 / 候选试听与明确采用；低可信 / Drift 只供比较与用户选择，不自动替换。保存先落盘再改内存，失败保留原值与候选。固定谱 Combat 复核 song / chart / revision / ruleset / scoring 全身份并去重，不写自由局纪录；真实乐师 adapter、伤害与失败惩罚未接。
- **校准返回修复**：校准取消、保留和采用成功返回曲库，关闭旧设置 / 暂停 / 候选覆盖层，准备与结算按钮布局统一。定向 Showcase **1/1 通过**、两个尺寸的渲染与按钮命中记录，以及测试包已构建但完整 Player 往返未验收的边界，见[模块指南末段](ai-docs/docs/modules/rhythm/rhythm-module-guide.md#2026-10-07-校准返回页面修复)。验证仅覆盖该记录中的版本与行为。
- **接手与剩余验收**：[四轨后续规格](PRP/rhythm-followup/spec.md)、[曲库实施](PRP/rhythm-library-integration-20261005/implementation-report.md)、[分级校准 / Combat](PRP/rhythm-final-regression-fix-20261005/confidence-combat-implementation-report.md)、[页面 / 暂停实施](PRP/rhythm-ui-navigation-20261006/implementation-report.md)。Boot 与正式 UI prefab 迁移后置；真人校准有效性、跨设备 / 物理延迟、音乐贴合度和 Player 实测仍欠。现有四轨实现不能直接算作《聚光灯》S8 已接入。

### 1.9 Taming 多目标控制：已有正式接线，保留与 S 组的兼容

多目标驯服已接 Boot → SampleScene：两名巡逻者使用独立路线和稳定 ID，T 驯服 / 返回玩家、UI 定向选择、镜头跟随、死亡 / 不可用时回退；存档 v2 与回放 v5 保存各目标及控制归属。入口与边界见[Taming 模块指南](ai-docs/docs/modules/taming/taming-module-guide.md)。这不代表聚光灯附身条件与身份继承规则已完成。

控制路由已接身份禁攻、怪物种类配置与潜行 / 战斗结算；输入位分配为驯服 7–9、交互 / 背包 10–11，每个巡逻者每 tick 只推进一次。多目标身份禁攻、单 tick 推进与输入位不重叠的回归检查已通过，场景回放验收仍待完成。

**集成验证边界（2026-10-07）**：相关 C# 文件 lint 通过，Unity 脚本重编成功；Taming / Monster / Simulation 定向 EditMode **157/157 通过**，新回归测试已核对进入程序集，含 v1 遭遇旧档兼容。该结果不包含全仓测试或 Showcase 场景回放验收。

### 1.10 中文字体补字

**已有验证（2026-10-07）**：使用 TMP API 在已有 318 个字符上追加 328 个字符，共 646 个。编辑器与保存后的资产均已核对：原字形数据、图集位置和 `.meta` 保持不变；尚未进行界面显示的人工验收。动态图集体积仍属于健康检查已知项，不清空已有字形。

### 1.11 六项「有代码没接上」的接线波（2026-10-09）

上一轮盘点出六处「规则写完了、生产里没人调」的地方，本波逐项处理。**收口证据**：EditMode 1231/1231（10 组）、`Showcase.Inventory` PASS（2/2、26 检查点）、`Showcase.Stealth` 的掩体那条 19/19 PASS、`gc_scan` 从 17 条降到 1 条（仅剩已知的字体资产项）。

| # | 项 | 结论 |
| --- | --- | --- |
| ① | 背包的合成与使用 | **闭环**。使用表/配方落 `InventoryConfig`（SO 占位，不建表）；效果执行口 = `IItemEffect` + 注册表，查不到回 `NoExecutor` 带中文原因；按裁决补了 `PlayerRules.Heal` + `PlayerItemEffectTarget`。**合成原本是个死循环**（`AlreadyOwned` 拒绝已持有产物、而列表只列已持有项 → 不持有选不中、持有被拒），补 `ItemCraftService.FindRecipeUsingIngredient` 走「选中原料 → 合成产物」闭合 |
| ② | 按种类怪物掉落 | **闭环**。`LootMonsterDropSink` 实现 `IMonsterDropSink`、`LootInstaller` 注册、`MonsterInstaller` 构建回调绑定；`EncounterStep.SettleKillDropOnce` 一次性闸门（先落闸再调外部口）。数据：yao 1/2 掉 `1101 普通皮`，yao 3（市令）保持 `[]`。⚠️ **两条已知边界**：<br>（a）**当前内容里观察不到掉落**——`Boot.unity` 的 `MonsterInstaller.kindId` 是 **1003（市令）**，而市令 `killable = false`（`ApplyDamage` 一律拒伤，见 `MonsterShowcase:173-202`）且 `drop_items = []`，所以「打死 → 掉落」这条链在现场景里既打不死、也没有掉落内容。**这是内容问题不是接线问题**：机制与真实表数据由 `EncounterDropTests`（含真表用例 kind 1001 → `[1101]`）验过。要让它在游戏里看得见，需要往场景里放一只**可常规击杀**的怪（如 kind 1001 巡夜人），属内容/场景决策。<br>（b）只结主怪；掉落是写背包的副作用、不进快照，回放重放死亡那一 tick 会再掉一次。<br>**已补（2026-10-09）**：容器级接线测试 `Tests/EditMode/Loot/MonsterDropSinkWiringTests.cs`——按 Boot 的接法装 `MonsterInstaller`，注册了 `IMonsterDropSink` 时主怪一死就结算一次（带真表那行的 `yaoId` 与 `drop_items`），**外加一条负对照**（不注册时不掉也不抛）。Loot 组 50/50 通过。补它的理由：`EncounterDropTests` 测调用侧、`LootServiceTests` 测实现侧，而两端之间的**注册与绑定**此前一条用例都没有——那一层坏掉的表现是「编译不报、单测不报、回放也看不出，只是怪死了什么都不掉」 |
| ③ | 偷袭 / 被打两种开战 | **已接**。`IBattleEntryContext`（Monster 侧声明、`EncounterStep` 实现、`BattleInstaller` 装配），`BattleSetup.TryCreate(boss, context, …)` 把五个量翻成 `BattleEntryRequest`；**旧签名保留并转调 null**，缺省行为与接线前逐字相同。⚠️ 两个缺口：「谁先动手」是从怪物状态**反推**的（本工程没有「玩家物理挥刀」这条路）；**偷袭那一支现状几乎到不了**（判据 `stealth.assassinated` 由处决写入，而处决会让目标当场死亡）。<br>**2026-10-10 接了方案 2「给 boss 自己的局势」**：新增 `Game.Battle.BossEntryContext`（挂在 BOSS NPC 上，按 `MonsterRules.SenseFrom` **与巡逻怪同一套档位**回答「这只 BOSS 察觉到玩家没有」），`IBattleEntryContext` 加一条 `MonsterAware`，`BattleSetup.IsEncounterEngaged` 认它；解析走 `BattleFlow.BindEntryContextProvider`（**开仗那一刻按 payload 现找**——构建回调在 Boot 场景里 `FindObjectOfType` 看不见玩法场景的 NPC，实测拿到 null）。**结果**：开战理由不再由十几米外巡逻怪的心情决定，实测 `battle_entry {context:true, source:"boss", kind:"FrontalAttack"}`。**仍到不了的两条**（不是漏，是缺玩法）：BOSS 先手要「BOSS 攻击玩家」这条状态机（S7 内容）；偷袭要「对 BOSS 的偷袭」这条玩法（`00` §8.1 #5 未定），`SneakAttackLanded` 恒 false。**边界**：BOSS 没有视线（纸片角色的旋转是朝向相机的）→ 只比半径；不做遮挡查询；不查种类表 |
| ④ | 遭遇 / 潜行结果回写剧情（C5 剩下一半） | **已按用户 2026-10-09 的决定接通，2026-10-10 主窗口复跑通过**。决定原文：**「一场实时遭遇打赢或被击倒，等于这一关结束」**——于是「恢复条件」里那个问题有了答案，原先暂挂的四条依据逐条被消掉：① 身份三项不再问 `EncounterStep` 要：桥订阅 `BattleStageEnteredEvent`，把事件里的 `Generation` / `ActivationId` / `TargetId` **原样带回** `CompleteBattleAsync`；② `EncounterStep.StartBattle` 从此有了生产调用方（`EncounterBattleBridge`，两个参数都用事件的 `activationId`——遭遇侧那两个编号只服务于 `ConsumeResult`）；③ 两个写入方按**事件 payload 分工**、不再争同一帧：非空 payload（BOSS 战）归 `BattleFlow`（它现在对空 payload 安静跳过，不再记 `boss_not_found` 的 Error），空 payload（活遭遇档）归本桥；④ 失败侧不再留空：玩家生命归零 → `SettleBattle(Downed)`（`00` §8.1 #3 的 `Exposed` 仍没有遭遇侧生产者）。<br>**判据**：空 payload 阶段 + 遭遇结果码在阶段 `battleResults` 里 + 身份三项对得上 → 推进；三者缺一即 `CompleteBattleAsync` 返回 false 并埋 `battle_result_rejected`（**正常拒绝**，桥不预判、不重试、不抛）。<br>**边界（刻意）**：不调 `TryBeginBattle`（不改存档边界，战中存档闸门仍只剩 tick 内窗口）；`Aborted`（收服）只消费不回写；阶段没声明结果码时阶段原地不动、同一场遭遇不会再结算第二次；取走结果发生在**结算的同一个 tick**（紧跟 `EncounterStep` 的 `AddStep`），因为镜碎会在同一帧更晚的渲染回调里 `End()`、而下一场 `Begin()` 会把结果抹掉。**修法与测试**：`Runtime/Monster/EncounterBattleBridge.cs` + `Tests/EditMode/Monster/EncounterBattleBridgeTests.cs`（行为 + 容器接线，含负对照）|
| ⑤ | 掩体贯通到怪物察觉 | **闭环**。`MonsterRules.Sense` 的**视锥两档**（红区 / 橙区）过 `HasSightNonAlloc`（零分配），**贴身那一档有意不受遮挡**（`06:71-73`「要潜行才免」）。事实层改为**直接抄行为层的答案**（`perceives = MonsterRules.Detects`），两层结构上不可能再分叉。⚠️ 代价（有意接受）：遮挡现在只有一个装配点 `BindOcclusion`，漏了不再被事实层兜住——由容器级测试与一条负对照各守一道 |
| ⑥ | 背后处决端到端验收 | **通过（2026-10-09）**。`ExecuteKey_BehindEnemy_ExecutesTheTarget` **PASS**，报告 `Logs/verify/stealth/20261009-095317`：检查点失败 0、异常 0、**39 个通过检查点**，此前全红的六条现在全绿——含「按 F 之前面板说可处决」「按 F 那一刻焦点为空、这一下归处决不让位」「最近一次结果是**允许**（位置条件成立过 **True**）」「目标生命归零进 Dead」「`stealth.assassinated` 写进正式流程事实集」「处决承载了这一场（`BattleSettlement = Victory`）」。**连跑两次 3/3 全绿**（稳定性）。<br>修法：这一条**不是判定坏了**，是回放的走位在「两只巡逻怪」的内容下靠相位运气。从观察点 (15.5,7.6) 下到主怪背后**必须横穿 `z=5`**，而第二只巡逻者 `Patrol_B`（`x 13.86..17.86`、`z=5`）正好压在这条走廊上、离主怪巡逻线只有 1.6 米，东边又被楼梯占掉（`x 16.5..19.5`，`DemoScene.cs:81` 记着「5 秒内走不到」）——**没有第二条路**，于是「不被察觉地走过去」退化成停顿窗口的相位巧合（实测两轮差别只是死亡时刻 22.8s→23.9s，签名逐字相同）。现在改成：**等到一个「主怪背后有落脚点、且不被任何一只巡逻怪察觉」的时刻原子地落位**（搜索 → 落位 → 复核放在同一个重试循环里），随后立刻按 F、按完就退场。走位本身没有失去覆盖——`Sneak_BehindEnemy_AllowsAssassination` 走的就是真走位且 PASS。**两轮修复过程中前置断言两次如实点出真问题**：主怪自己的贴身圈（1 米 ≤ 1.5，不潜行不免）、主怪走到东段时背后那点落进 `Patrol_B` 的视锥 |

**本波顺带修掉的**：`StealthDecisionGate.PerceivesThroughCover` 已不在感知生产路径上（会掐掉贴身档、且走会分配的 `HasSight`），文档已写明处境与退场条件；`EncounterSightOccluderWiringTests` 的夹具补齐 `BindOcclusion` 并加了负对照。**待拍板**：`Data/Inventory/InventoryConfig.asset` 的数值（治疗量、配方件数）、「合成本身在哪个界面 / 有没有合成台」（`05:107 R8` 的「原料合成后是否消失」已按用户 2026-10-10 拍板落定为**消失**）、`PerceivesThroughCover` 是否退役。

**补齐（2026-10-10）：demo 场景「较量」那一局的失败出口**。① 的机制当时已经通了，但**demo 用的内容只声明了 `Victory`**——玩家站在巡逻线边上被击倒才是常态，于是那一局每次都以 `battle_result_rejected {result: Downed}` 收场、剧情永远停在战斗阶段，再按 E 只是把对白重放一遍（现场证据：`Logs/telemetry/270e5c40.log`，00:52 那一局）。修法是**加内容不改代码**：新建 `Tables/Data/narrative/sample_battle_drill.json`（与 `sample_battle` 同形，但 fight 同时声明 `Victory` 与 `Downed`，出口分别到 `cleared` / `downed`），遭遇表 `sample_battle_drill` 行改指它，跑 `scripts/gen-tables.ps1` 重生成。**旧 `sample_battle` 一个字没动**：`EncounterBattleBridgeTests.DownedOnAVictoryOnlyStage_IsRejectedAndTheStageStays`（拒绝路径）与 `DemoShape_...` 两条都靠它的「只声明 Victory」这个形状当负对照。

**复跑（2026-10-10，主窗口独立跑）**：全量 EditMode **2439 / 2439**（0 失败 0 跳过，比 2438 多一条新用例 `DrillStoryDowned_AdvancesToTheDownedEnd`）；`Showcase.Battle` **4 / 4 PASS**（两条老 BOSS 用例 + 两条新增的「较量」用例）、`Showcase.Stealth` **3 / 3 PASS**、`Showcase.Inventory` **2 / 2 PASS**；`gc_scan` 只剩已知的字体资产一项（顺手清掉 18 个 `InitTestScene*` 残留、修掉 battle guide 一处失效行号引用）。新增的两条回放：`Drill_InteractStartsTheBriefAndEntersTheLiveEncounter`（走到 (0.6, 8.8) → 提示「较量 · 演练桩」→ 按 E 拉起对白 1002 → 剧情走到 brief → 对白收起 → 停在空 payload 的 fight 档）与 `Drill_DownedEndsTheStageInsteadOfSticking`（走巡逻线老路被打倒 → 剧情走到 `downed`、写 `world.samplebattle.downed`，**不再卡在战斗阶段**），报告 `Logs/verify/battle/20261010-010605`、`20261010-010704`。

**给 ③ 方案 2 留的现场数据（2026-10-10）**：`BattleFlow` 的 `battle_entry` 旁新增一条 `battle_entry_snapshot`（`sneaking` / `sneak_attack` / `zones` / `monster`），**只在快照真被采纳时记**——原来的 `battle_entry` 只写得出来 `context=false` 的硬编码缺省，分不清「没交快照」与「交了但落进缺省分支」。**仍缺一块**：`IBattleEntryContext` 没有接口暴露 `Initiator`，所以「遭遇侧报的开战理由是否等于玩家动手」这条在日志里还看不见；要不要把「按 boss 自己的局势判开战」做下去，等这条遥测先跑一局再看。

### 1.12 demo 场景（SampleScene）的示例内容（2026-10-09）

功能落地之后，**demo 场景里要真的看得见、做得到**。本轮补的内容与验证：

| 功能 | demo 里怎么看 | 状态 |
| --- | --- | --- |
| **背包 · 使用** | `Crate_C`（16, -8）给 1004 治疗药水 ×2；被怪打掉血后按 **B** 开面板 → 选中 → 详情区「使用」→ 回血。满血时会失败（配置口径，不是坏了） | 场景本来就有 |
| **背包 · 合成** | **新加两个箱子**：`Crate_D`（12, -8）给 1401 水痕 ×1、`Crate_E`（13.5, -8）给 1102 湿皮 ×1。两样拿到后按 B → 选中水痕或湿皮 → 详情区「合成」→ 得 1701 进入废弃官署的钥匙。配方出处 `05_皮面具与道具.md:106 R7`（水痕 + 湿皮 → 钥匙） | **已补并验证** |
| **按种类掉落** | 潜行绕到市令背后按 **F** 处决 → 它倒下时掉落 **1101 普通皮**，背包 +1 并弹「获得」通知 | **已补并验证**（见下） |
| **掩体** | `Cover_SightDemo`（14.2, 6.3）挡在巡逻线与观察点之间：躲在它南侧，巡逻怪不涨警戒、不变敌对（`Sight_BlockedByCover_HidesFromEnemy` 19/19） | 场景本来就有 |
| **背后处决** | 潜行（按住潜行键）绕到市令背后 1 米内 → F。它是 `killable=false` + `defeat_method=暗杀`，**只能这样解决** | 场景本来就有 |
| **偷袭 / 被打开战** | **入口本来就有，而且 UI 是通的**（上一轮我写「demo 里进不去」是错的，2026-10-09 更正）：`Npc_SampleBoss`(-4.2, 8.4) 挂 `DialogueInteractable`（动词「挑战」）+ `NarrativeTrigger`(targetId `sample_boss_npc`) → 按 **E** → 剧情停到 Battle 阶段 → `BattleFlow` 开打（`BattleInstaller:72` 已注册 `BattleScenePresenter`）。**本轮修的是它被弄坏了**：见下面「⚠️ 一处回归」 | **已修复并验证** |
| **实时遭遇（C5 剩一半）** | **村北的「较量」演示桩**：`Npc_BattleDrill`(1.5, 8.8) 挂 `DialogueInteractable`（动词「较量」、显示名「演练桩」）+ `NarrativeTrigger`(targetKind `SampleBattleDrill`) → 走过去的焦点提示是「较量 · 演练桩」→ 按 **E** → 对白 1002 → 剧情走到**空 payload 的战斗档** `fight` → 十几米外的巡逻怪那一仗由 `EncounterBattleBridge` 认领；**打赢（处决）或被击倒**都会让这一关结束 | **2026-10-10 补上失败出口并验证**（见下） |

**2026-10-10：这一局原先卡死，修的是内容不是代码**。① 的机制早就通了，但 demo 用的 `sample_battle` 那三阶段里 **`fight` 只声明了 `Victory`**，而玩家在 demo 里站上巡逻线被击倒才是常态——于是每局都以 `narrative/battle_result_rejected {result: Downed}` 收场、剧情**永远停在战斗阶段**，再按 E 只是把对白重放一遍（现场证据 `Logs/telemetry/270e5c40.log` 的 00:52 那一局：23 次交互全落在同一个 `fight` 上）。按叙事指南那半句口径（「内容侧只需要给活遭遇档声明它可能产出的结果码」）补内容即可：新建 `Tables/Data/narrative/sample_battle_drill.json`（与 `sample_battle` 同形，`fight` 声明 `Victory` + `Downed`，出口到 `cleared` / `downed`）→ 遭遇表那一行改指它 → `scripts/gen-tables.ps1` 重生成。**旧 `sample_battle` 一个字没动**——`EncounterBattleBridgeTests` 的拒绝路径与 demo 形状两条用例都靠它「只声明 Victory」当负对照。

**⚠️ 一处回归（2026-10-09，本轮发现并修好）**：六项波次给 `BattleFlow` 接上了「遭遇局势快照」（`BattleInstaller` 的构建回调 → `flow.BindEntryContext(step)`），但 **boss 战是剧情 NPC 触发的，跟十几米外的巡逻遭遇是两回事**——巡逻怪平静、玩家没潜行时，快照被原样翻成请求，`BattleEntryRules` 判「不在警戒 / 敌对区域、怪物也未察觉」（07:22、:27）**把整场仗拒掉**。表现是 `Showcase.Battle` 两条回放全红（`「阶段一 BOSS（占位）」开不了仗：进战斗判定被拒`），而它们上一次通过是 **2026-10-08**、早于那次接线，所以一直没人发现。
修法在**翻译层**、规则层一个字没动：新增 `BattleSetup.IsEncounterEngaged(context)`——遭遇**只有说得上话时才有发言权**（偷袭已命中 / 玩家在警戒区域 / 玩家在敌对区域 / 怪物处于警戒或敌对态），否则退回硬编码缺省「正面攻击」，与 `context == null` 同一口径。**刻意不含「正在潜行」**：潜行但没偷袭命中按 07 确实是「拒绝开战」，但那说的是「潜行去袭击这只怪」，跟玩家恰好潜行着去按 E「挑战」无关。`BattleFlow` 据此决定交不交快照。
**验证**：`Showcase.Battle` 两条 **PASS**（0 失败 0 异常）；全量 EditMode **2422 / 2422**（新增 5 条 `IsEncounterEngaged` 判定测试，含「平静巡逻无发言权」「潜行本身不算发言权」两条负对照）。

**在 demo 里怎么看到三种开战**（都走同一个入口：走到 `Npc_SampleBoss` 按 **E**）：
**开战理由自 2026-10-10 起由这只 BOSS 自己说了算**（③ 方案 2，`BossEntryContext`），与巡逻怪无关。

| 开战方式 | 怎么弄出来 | 会看到 |
| --- | --- | --- |
| **正面攻击** | 直接走过去按 E（在它的红 / 橙区内就行，本来就是） | 玩家先手 |
| **被打** | **demo 里到不了**：要「BOSS 攻击玩家」这条玩法，本工程没有 BOSS 状态机（S7 内容） | — |
| **偷袭** | **demo 里也到不了**：要「对 BOSS 的偷袭」这条玩法（`00` §8.1 #5 未定）；`BossEntryContext.SneakAttackLanded` 恒 false | — |

⚠️ 上一版表格里「用巡逻怪敌对 / 用市令处决换开战方式」两行**已作废**（2026-10-10）：那是拿**别处的局势**给这一仗找理由——巡逻怪的状态与十几米外的 BOSS 无关，而市令的 `stealth.assassinated` 是**上一场遭遇**的瞬时事实。现在 `context=true / source=boss` + `zones=-+hostile`（玩家在 BOSS 红区内）就是现场判出来的正面攻击。

**掉落这条为什么要在数据里改一行**：场景里唯一的怪种是 `Boot.unity` → `MonsterInstaller.kindId = 1003`（市令），而 `yao/3` 原本 `drop_items: []`。**一个 `MonsterInstaller` 只带一个 `kindId`、`MonsterRules` 是单例**——整个遭遇只有一种怪，所以演示掉落只能让市令掉。而设计文档里市令的掉落物是「通行任何区域的道具」、**item id 未定（Q9）**，所以本轮按用户 2026-10-09 的决定**用 `1101 普通皮` 占位**（与 `yao/1`、`yao/2` 一致）。**这是占位，不是定稿**：Q9 定了之后改 `Tables/Data/yao/3.json` 一行 + 重跑 `scripts/gen-tables.ps1` 即可。

**验证**：全量 EditMode **2417/2417**；`Showcase.Exploration` 68 检查点 / `Showcase.Session` 31 / `Showcase.Inventory` 26 **全部 0 失败**（加箱子之后复跑，确认没弄坏）；掉落由遥测实证——处决那一刻有 `loot/monster_dropped {"yao":3,"kinds":1}`。另外全仓按坐标搜过，**没有别的回放会路过 z≈-8 那片地**。

✅ **既有脆弱已修（2026-10-09 晚）**：`Showcase.Stealth` 的 `Sneak_BehindEnemy_AllowsAssassination`（**真实走位**那条）原先是**三次红一次**（2026-10-08 就记录过、与接线波无关）：玩家在接近途中被第二只巡逻者 `Patrol_B` 打死，死因是**怪在巡逻线端点掉头**——落点判定只看「此刻会不会被察觉」，挡不住它一转身把刚在背后的玩家摆到正面，1.6 米内即红区 → 敌对 → 击倒 → 三刀死 → 镜碎 → `facts.Clear()`，检查点全空转。
修法（都在 `StealthShowcase.cs`，判据与断言一条没动）：**动身前等一个「别的巡逻者停稳了」的窗口**（`PatrollersPausedLongEnough`，横穿它们的巡逻线才需要窗口），**一旦动身就不再等**（`atLookout` 之外不再要求停顿——停在巡逻线上比走过去更糟）。两处踩过的坑都记在代码注释里：① 全程等会把玩家**停在全场最危险的那一格**（实测死亡时刻 23.8s，与修前 23.9s 逐字相同）；② 把**主怪**也算进闸门会让窗口变成两个独立 14 秒周期的相位对齐，期望等待 ≈78 秒 ⇒ 必然超时（遥测里没有 `player/dead`，纯粹没动身）。**验证：连跑 4 次全绿**（`Logs/verify/stealth/20261009-131901 / 132119 / 132321 / 132437`）。`ExecuteKey_BehindEnemy_ExecutesTheTarget`（F 键那条）自改用「原子落位」后一直稳。

---

## 2. 等拍板，不要自己决定

| 事项 | 卡在谁 | 说明 |
| --- | --- | --- |
| 两个杂散场景副本 | 项目负责人 | `Assets/Scenes/SampleScene 1.unity`、`SampleScene1 1.unity` 已被上一次提交大改并入库；`Assets/Scenes/` 是模板目录、规则要求原位不动，删还是留未定 |
| 任务「!」标记是否给台词气泡让位 | 项目负责人 | 灰色「…」会避让，任务黄「!」不让位，无树 NPC 冒气泡时可能被压住 |
| 任务面板底板透底 | 需实机复现 | 回放里正常、实机透底；怀疑与 `ProjectSettings/EditorSettings.asset` 的 Enter Play Mode Options 被测试运行器打开有关，未定位 |
| 聚光灯玩法拆分的矛盾与待定 | 策划 | 见 `docs/design/features-spotlight/00_功能总览.md`；旧版 `docs/design/features/` 的 30 条矛盾 + 128 条待定随旧版冻结，不再追 |
| 四本素材 PDF（共 152 MB） | 项目负责人 | 移出仓库放共享盘，还是走 Git LFS |
| Run 动画帧、探索 3D 环境资产 | 美术 | 序列帧小人暂无 Run 动画（现复用 walk 剪辑）；探索环境仍是灰盒 |

---

## 3. 长线与内容层（等外部输入）

**这一节是长期工作**：3.1 已废弃，3.2–3.3 是两条长线，3.4 记录尚待推进的内容及系统项，不把已有接线重新列为未实现。

**3.1 Live2D** —— 已废弃（2026-09-28 用户定）：演出只保留世界舞台 + 序列帧小人，Live2D 适配层与叠加模式代码已随 `161c690` 删除，不再导入 Cubism SDK。

**3.2 移动端移植** —— 现阶段是 PC 游戏：触屏摇杆、触屏三键、走跑按钮、Android 画质档、安全区实机统一，全部后置到移植阶段。注意玩法代码里**禁止**平台条件编译与平台专属 API，这类只允许出现在 `Scripts/Runtime/Platform/`，输入只读 Input System 的 Action Map。

**3.3 美术替换** —— 探索场景从灰盒换成 3D 环境资产（模块化 Prefab + Lightmap）。纸片角色按**正面平视**画，不要按要求俯视 3/4 出图。

**3.4 内容与系统剩余方向**（roadmap 的 C1–C3/B3/W2 状态已同步，不沿用旧总数）

| 组 | 待推进或核对 |
| --- | --- |
| A 探索层 | A3 物资箱 / 最小背包已有接线，泛化交互仍需核对；A4 / A6 机制与灰盒见顶部 S 组记录，正式内容及最终验收另计 |
| B 任务系统 | B3 已有接线与验证，最终提交独立验证见 §1.1；B4 进度重置与已完成列表 |
| C 叙事接线 | C1–C3 转 §1.1/§1.2 收尾；C5 战斗结果 → 剧情**两条路都已接通**（回合制 2026-10-07、实时遭遇 2026-10-09），剩下的欠账是「S2 的露馅 / S7 的 BOSS 换形态还没有遭遇侧生产者」与实测复跑 |
| D 演出与 UI 动效 | D6 角色动画补齐，D4/D5 资产采用状态见 §1.5 |
| E 系统与流程 | E4 加载过渡已收尾待视觉验收（2026-10-07，相位守卫与回放等待），E8 出包验证 |
| F 内容与美术 | F1 真实剧本进表、F2 环境替换、F3 美术皮肤；F4 已有三首音游音乐，环境音 / SFX / 正式内容音频仍缺；F5 已有 Narrative 最小校验，完整内容校验工具未验收 |
| S 聚光灯核心机制 | S1 换皮与附身、S2 身份暴露与怀疑、S3 潜行与暗杀、S4 追逐、S5 皮面具与道具、S6 关卡专属机制、S7 BOSS 战、S8 小游戏；旧 G 组随旧版冻结 |
| H PC 适配 | 主要路径已实现，NPC 悬停与窗口实测仍需逐项确认，不能写全部验收完成 |

**玩法内容清单 `docs/design/features-spotlight/`（聚光灯）**

13 份功能文档：01 换皮与附身、02 身份暴露与怀疑、03 潜行与暗杀、04 追逐、05 皮面具与道具、06 怪物分层、07 关卡专属机制、08 小游戏、09 BOSS 战、10 两界与场景结构、11 剧情流程与章节结构、12 调查与线索、13 系统界面清单；状态均为待策划确认，原文矛盾与待定见 00 总览。
旧版 `docs/design/features/` 01–15 与照镜 demo（`PRP/mirror-core`）已冻结，保留备查。精炼与开 PRP 的顺序见 `docs/roadmap.md` 第 5 节 W5。

---

## 4. 已知坑（别重踩）

- **生成物不手改**：`Library/` `Temp/` `Logs/` `obj/` `UserSettings/`、`*.meta`、`*.csproj/*.sln`、`packages-lock.json` 都由 Unity 生成。
- **共享工作区**：本项目常有多个会话 / 多人共用同一个工作区与 git 索引。提交一律按路径（`git commit -F <信息文件> -- <路径…>`），不要用不带路径的 `git commit` 或 `-a`；同一文件里混有别人未提交的段落时用临时索引出提交。细节见 `ai-docs/pitfalls.md`。
- **`invariants.py` 最近四项常红**：字体资产一项、LailaFace 命名空间三项（§1.3）。这是两类问题，不能将全仓检查写成通过；须区分既有问题与新增问题。
- **气泡不能只测村民**：保留共用预制体连接，并走真实探索测试不同 NPC 与接近方向。只修缩放仍会漏掉正面站位的上沿裁切；视口约束不负责其它 HUD 的遮挡避让。
- **移动 / 删除 / 重命名资产**用 `git mv` / `git rm`，必须连 `.meta` 一起。
- **角色纸片不要给 `CameraBillboard` 加 `ExecuteAlways`**：会把场景标脏，二十多个物体旋转进 diff。
- **演出隐藏的覆盖边界**：`PerformanceService.PlayAsync` 在加载舞台前统一快照场景中的 `ChibiPuppet`，包含未激活角色，排除舞台演员，并在退出时恢复 Renderer / Canvas 原状态。演出期间新生成的角色不在快照内；无 `ChibiPuppetMotion.TrackedRoot` 时退回顶层根，角色放在公共容器下时应配置角色根，避免连带隐藏其他对象。表情轨与 `PerformanceActor` 已删除，不要再配置旧绑定。
- **工具与 hooks 排查**：见 [.codex/hooks/README.md](.codex/hooks/README.md) 与 [坑册](ai-docs/pitfalls.md)，本文不重复会话诊断步骤。
- 更多踩过的坑见 `ai-docs/pitfalls.md`，动手前值得扫一遍。

---

## 5. 工具速查

| 要做的事 | 怎么做 |
| --- | --- |
| 看编译错误 | Unity MCP 的 `read_console`，或编辑器 Console。**控制台不可靠**（会被别的会话清空、也会读到编译中间态）——真源是 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 里的 `error CS` 与 `## Script Compilation Error for: … <程序集>.dll` |
| 项目 lint（保存 `.cs` 时自动跑） | `python .claude/skills/project-lint/lint.py <file.cs>` |
| 健康度 / 跨文件不变量 | `python .claude/skills/evolution/gc_scan.py` |
| 跑测试 | `/unity-test [EditMode\|PlayMode] [过滤]`；**没有原生 MCP 工具时**用 `uv run --with mcp python scripts/unity_mcp_check.py --test --mode EditMode --group <组>` |
| **复核「测试真的跑了吗」** | `--test` 输出里看 `stale_assembly_suspect` / `orphaned_job` / `verdict` 三个字段；**再加字节检索 DLL**：`$t=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes('Library/ScriptAssemblies/Game.Tests.EditMode.dll')); $t.Contains('你的新测试类名')`。三者都过才采信 |
| **查新增文件缺不缺 `.meta`** | `uv run --with mcp python scripts/unity_mcp_check.py --meta`（扫 `git status` 里所有改动/新增的 `Assets/` 资产） |
| 强制 Unity 重编 | `refresh_unity(mode=force, scope=all, compile=request, wait_for_ready=true)`；**第一次常返回 `recovered_from_disconnect: true` 而没落地，重试一次**；仍不行就 touch 一个生产 `.cs` 再刷新 |
| 模块回放验证 | `/verify-module <模块>` |
| 构建 Player（编辑器须关闭） | `scripts/build.ps1` |

---

*本文是未完成事项的活文档：每关掉一项就删掉对应条目，别让它积累成第二份现状描述。*
*当前状态一律以 `git log` 为准，本文不记录提交号。*
