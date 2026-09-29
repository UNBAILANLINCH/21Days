# Narrative 实施记录（2026-09-29 修订）

用户已授权按修订后的 [prp.md](prp.md) 执行 C1–C3 / B3。
“源码存在”“本次静态核对”“本次运行通过”分别记录；旧未勾选不等于没实现。
第一、二批协作已结束；下方早期记录保留追溯，当前状态以本节及最新证据为准。

## 当前交付状态（2026-09-29）

- C1–C3 / B3 最小接线已实现并提交；`3584d04` 补齐 DialogueService 与三份 Dialogue/Quest 契约文档。此前缺少四参数重载与 Completed 传递的提交缺口已关闭。
- 补交后本聊天刷新脚本，编译控制台 error=0；Narrative EditMode 22/22，零失败零跳过（job `08c054f851f947dd8b59ec28de5731d7`）。
- 三条 Narrative 回放分两批通过：`Logs/verify/narrative/20260929-094939/report.md`（实时刷新）、`Logs/verify/narrative/20260929-095052/report.md`（取消/重试/跳过与保存/继续），报告均为检查点失败0、运行时异常0；后一批工具结果2/2，零失败零跳过。结束后 Boot 闲置、控制台 error=0、倍率恢复1。
- 用户观看上述慢速回放后反馈“好像是没问题的”，本轮人工视觉验收通过，仅限三条用例覆盖的行为，不代表真实章节、全场景恢复或全目标中断均已验收。
- 本轮回放在各截图前增加 `Wait(3f)`，x2 时关键画面约停6秒；该测试脚本改动尚未提交。工作区另有场景、字体、URP、TimeManager 等改动，因此上述结果属于补交后的工作区验证，不能称为独立干净检出的最终提交验证。
- [ ] 最终提交独立验证：在隔离的干净检出上记录提交号、编译及相关联合回归结果。已核对相关聊天的收尾记录，未找到此项已执行的证据；历史647/647及16/16为拆分提交前工作区结果，不重复算作本项完成。
- 本次同步 tasks 与 roadmap；未暂存、提交或推送文档及回放停顿改动。

## 原 T1–T9 对照（实施前快照，非当前待办）

| 原任务 | 当前源码状态 | 本 PRP 剩余工作 |
| --- | --- | --- |
| T1 对白规则 | 已被现行 Dialogue 规则/表现/阅读策略继承或替代 | 复用其入口；不重建已读快进或三槽界面 |
| T2 剧情/遭遇规则 | 七个源文件、四个 NarrativeRulesTests 用例存在 | 补运行接线及针对新行为的验证，四条测试不等于全边界已验收 |
| T3 Core 存储 | 候选读取/Commit/档案/串行 IO、安全写盘及 JsonSaveServiceTests 已存在 | 已读由 DialogueReadStore 使用独立档案；只补 Narrative 接入相关测试 |
| T4 战斗/恢复 | EncounterStep 快照/结果、MonsterEncounterState 恢复路径已有 | C5 结果转 Narrative 未接；先等 G5 定义，不重造伤害/胜败产品规则 |
| T5 白 UI | DialogueService、Controller、View、Transcript、Config、Installer 已由 dialogue-system 替代 | 不按旧 UI 字段接线；本批最多最小条件适配 |
| T6 剧情与存档 | Session 已有 GameSession、槽位 UI、恢复事件；Narrative 控制器/Installer/真实条件源仍缺 | C1/C2/B3、Narrative 分区接 Session；不新建 GameSessionController |
| T7 表与检查 | Dialogue JSON/Luban 与 Catalog 校验存在；Narrative 表缺失，纯规则已有部分结构检查 | C3 最小 Narrative 表、跨内容引用及未接入能力检查 |
| T8 接线/Showcase | Dialogue/Session 的资产与 Showcase 存在；Narrative 无 Installer/Showcase | 根注册与 Narrative Showcase；SampleScene 运行时搭建，不新建独立 Verify 场景 |
| T9 文档/验证 | Narrative 三件套已存在且登记；其中 Session/条件源描述过时 | 第二批同步 Narrative 文档并实际编译、定向测试、回放；不得宣称已有资产已经复验通过 |

## 本次分批任务

N1–N6 已有实现及下文运行证据；原勾选保留，待最终提交独立验证后统一关闭交付门。N7 的模块文档已同步，本次按用户授权同步 roadmap；全仓检查仍有既有问题，不能标全绿。

- [x] R0：完整读取 PRP 与 Narrative/Dialogue/Session/Quest 契约，核对生产源码、调用方和旧待办。
- [x] R1：修订本目录三份文档；明确独立已读档案/槽位剧情进度与 C5/G5 边界。
- [x] R2：运行 gc_scan 并记录实际结果；本目录 diff --check 通过。gc 全局未通过，见下方证据。
- [x] GATE：主聊天已明确解除第二批门禁，Mirror/回放聊天释放 Unity；仅代表协作所有权交接，不代表首批验证 PASS。
- [ ] N1（C1）：Runtime/Narrative 控制器与根注册；批量候选 → EncounterRules → DialogueService.PlayAsync → 身份校验后推进。取消、失败、重入有测试。
- [ ] N2（C2）：真实条件源；标记及 PlayerModel 的存活/潜行/伪装快照与登记目标生命周期；无来源的目标敌意/感知拒绝进入生产内容。Dialogue 注册仅作必要扩展。
- [ ] N3（B3）：QuestCompletedEvent → 配置标记；状态变化写槽位；读档核对持久完成状态，保持幂等。
- [ ] N4（C1 恢复）：按 SessionStartedEvent 重载，稳定状态完整快照及未知内容校验；新游戏、读旧槽、对白中退出不得写不完整快照、迟到回调、独立已读对照。
- [ ] N5（C3）：最小 JSON/Luban 剧情/遭遇/条件/出口/标记映射及验证样例；非法引用、环路、冲突、未实现能力有可运行检查。
- [ ] N6：Narrative Showcase（3–10 步）实际调用真实服务及 Session；必要编译/定向测试/回放完成后读取报告核验。
- [ ] N7：Narrative 模块三件套按源码同步；lint/gc/diff/meta；列 HANDOVER/roadmap 建议同步项，不改共享文档。

N1–N7 对应 prp.md 的 N1–N6 验收。测试放 EditMode/Narrative；回放只写 Showcase/Narrative。
如出现必要但未定的策划规则，记录具体问题并继续独立可做项。

## 未纳入本批的验收欠账

- C5/G5 无血条对抗的结果语义，及其 Battle/追逐/躲藏/辨认阶段接入。
- 原 A09/A11/A12 战斗恢复与结算对照、A13 全场景恢复事务、A15 Android 真机、A21 全目标生命周期中断。
- 原 A10/A22 中对白逐节点/逐字恢复、任意行为请求重发抑制的完整产品覆盖；本批只保证明确支持的阶段边界。
- 画皮、收押、三结局、真实第一章内容。验证样例不代表策划定案。

## 历史证据记录（按当时工作区记录，旧“待确认/未提交”不代表当前状态）

- 第二批第一轮：运行适配/表/Session 防护源码已写入；Luban 5.1.0 由既有脚本生成成功，新增三张表；定向 project-lint 与 diff --check exit 0。
- Unity 编译实际记录：首次 scripts refresh 未导入新增文件，Tables.cs 报三条 CS0246；磁盘 namespace 正确而 Bee 编译清单不含新文件。统一验证聊天随后 force/all 导入，读取控制台 errors=0，开始定向 EditMode 回归。尚无测试结果，不记 PASS。
- new-feature 要求的 code-reviewer 子代理只读审查第一轮 NEEDS-CHANGES：取消后同目标无玩家可达重试、无出口 WaitAction 未被内容校验拒绝。测试期间源码冻结；释放后修复并回归。正常/跳过完成、真实任务→条件选项、服务级迟到回调测试待补。
- 第二批 generate-doc 增量同步已更新 Narrative 三件套及涉及的 Dialogue/Quest/Session 公开契约；仍待最终代码及实测收尾。复跑 gc_scan exit 1：只剩既有三项 Gameplay/LailaFace 命名空间不符与中文动态字体 22691 KB；Mirror 新测试的 meta 已由 Unity 导入生成。
- 第二轮源码冻结：已修取消后的同目标主动交互重试、无出口 WaitAction 校验、父帧 RequestIssued 候选拒绝。新增 Catalog 10 例、Service 6 例、Rules 2 例与 Showcase 3 条；验证对白9001/sample_options 已经既有 Luban 脚本生成。恢复回放先核对完整 Frame/消费键/标记，再验证 Once 不重弹，之后才启动下一段对白。定向 lint/diff exit 0；等待实际 Unity 结果。
- 第二轮 EditMode 首次实跑由统一验证报告 320/321：普通组件在 EditMode 不执行 OnDisable，禁用后任务仍挂起。服务测试改为显式令牌取消；真实禁用/启用、取消不推进任务、同激活及消费集合重试断言迁入原跳过 Showcase，没有改 Runtime 或放宽断言。补丁 lint/diff exit 0，静态复核无新增阻塞。
- 补丁后统一验证报告 EditMode 461/461（Narrative 相关321 + Mirror140），0失败/0跳过；job `0f9f708b6a1e43f1b0786e709e146de6`。这是协作方实跑结果，尚待本聊天/主协调独立核验；PlayMode 与视觉验收未完成。
- 第二轮真实 PlayMode 报告 `Logs/verify/narrative/20260929-061846/report.md` 已独立读取：选项动态刷新、稳定保存/继续两例通过；目标禁用取消检查失败，截图有残留空对白面板，后续同激活重试/跳过通过。不能把本轮标记为通过。
- Core 根因实证：打开过渡在 UIService.OpenNewAsync 原有回滚 try 之外；取消时调用方尚未拿到 view，已登记/入栈实例遗留。新用例漏写 UIView.Layer 的 CS0534 已修正；其前一次零匹配任务不算验证。实际红灯 job `da57b72d54c7465da27aa4bed8e4ef15` 在 `Get<PendingTransitionView>()` 非空处失败。
- 已扩展 Core 现有回滚覆盖打开过渡，恢复字典/栈、下层可见性、HUD、焦点并释放实例；同类型打开先等 opening 完成源，两个调用一致取消。独立回归 job `2b3c177a9e1142cfb4d4ad46cdb34544`：Core、Dialogue、Mirror、Narrative、Quest、Session 共647/647通过，0失败/0跳过；编译零错误。
- 修复后真实 PlayMode job `82c4805c05b04c9ca83a0241b8d51892`：Dialogue 7、Mirror 6、Narrative 3，共16/16通过，运行107.210秒；本聊天与主协调均独立读取结果及控制台 error=0。`Logs/verify/narrative/20260929-063358/report.md` 三条全部 PASS，真实禁用取消在2.2秒完成收尾且任务未推进，同激活重试/跳过完成通过；原空对白残留消失。选项截图右端被调试浮窗遮挡，视觉清晰度另行核对，尚无人工作出视觉确认。
- 选项补充核查：临时Play经公开DialogueService.PlayAsync(9001)打开同一对白，只关闭运行时调试浮窗；未进入槽位、未修改资产。TMP实际字形边界：长选项（含禁用原因）x1434.86~1859.45、短选项x1749.32~1859.60，1920×1080内且isTextOverflowing=false。Unity补拍 `Logs/verify/narrative/20260929-063358/10-选项去调试遮挡.png`，独立看图确认文字完整；因此没有改选项预制体。该图为独立表现核查，任务/恢复状态的证据仍以三条真实Showcase为准，人工视觉确认尚待用户。
- 临时核查的退出异常单独记录：探针用默认令牌 `PlayAsync(9001).Forget()`，对白仍在选项等待时直接停止编辑器Play；随后一条ObjectDisposedException从 `UIService.ThrowIfDisposed:791 → SetLayerVisible:391 → DialogueService.PlayAsync:172 → UniTaskExtensions.Forget → UniTaskScheduler:90` 报出，完整堆栈由本聊天MCP read_console留存。该路径绕过正式Trigger的生命周期令牌及Showcase正常收尾；不能用它改写16/16与Replay实测结束时的零异常，也不能据此宣称任意根作用域突然销毁已安全覆盖。后续重新Play并选择真实“稍后再来”按钮，确认IsRunning=false、DialogueView不存在后才停止，最终控制台error=0。原A21完整中断恢复仍未纳入本批。
- 最终收尾：退出Play、Boot场景闲置、回放倍率恢复1，无InitTestScene残留。gc_scan exit1仍为四项既有（Gameplay三处命名空间、动态字体22699 KB）；定向diff检查exit0，未暂存/提交/推送。主审指出的Session API表格断裂与旧稳定边界描述冲突、Dialogue条件源未来时已修正；Narrative人工视觉确认仍待用户。
- 主审补充：GameSession.NewGameAsync/ContinueAsync在对白运行时的拒绝复用new_game_rejected/continue_rejected，记录slot与reason=dialogue_running。仅补此两处埋点，lint/diff exit0；刷新后编译控制台error=0，Session定向EditMode job `f05993a0e67d4d36a58439a1b2a52edf` 40项完成、任务succeeded、failures为空，结束后控制台error=0；本聊天与主协调均独立核对。工具result为null，不补推精确passed/skipped细分。其他冻结源码未改，不重复全量回放。
- Boot diff 已独立核对，仅新增 GameBootstrap 上的 NarrativeInstaller 组件引用及序列化块，脚本 GUID 匹配。新增 Narrative 源码、测试、生成表与新目录的 meta 均在磁盘存在；当前索引为空。
- review-change 已读 Codex 入口与共享正文；尚未到交付门。Narrative/Dialogue/Quest/Session 埋点扫描已执行，新入口复用 advance span/取消失败埋点，补重试与意图拒绝事件；参数校验异常不逐个重复埋点。新增代码无其余该埋未埋的点；本次无新结论需另写沉淀，生命周期测试分层已有项目规则及相邻用例说明。

- 技能补核对：已完整读取 `.agents/skills/new-feature/SKILL.md`、`.claude/skills/new-feature/SKILL.md`、`.agents/skills/generate-doc/SKILL.md`、`.claude/skills/generate-doc/SKILL.md` 与 `modules.json`。new-feature 第 1/2 步沿用已授权 PRP 的六条验收与六项设计；第 3 步复用现有七文件规则骨架；第 4–8 步尚待第二批，未声称接线/测试/审查/回放或 sync 完成。
- generate-doc check narrative（只读）差异：guide:37/111/129 把 Session 与槽位 UI 列为未实现（实际 `GameSession.cs:36` / `SaveSlotsView.cs:26` 已有）；guide:139 写目标未发现但占位源码第 11 行为 true；external-api:42 写不可变但 `NarrativeContent.cs:12–20/59` 直接暴露可变 Stage。guide/extension 的单向模块依赖约束与拟加入协调器需明确纯规则边界。三件套本批未改，第二批 sync 必须增量修订。
- 2026-09-29 第一批：源码/引用检索完成；未操作 Unity，未运行测试，不把历史勾选当通过证据。
- gc_scan exit 1：三项既有 Gameplay/LailaFace 命名空间不符、既有中文动态字体 22691 KB、协作中新增 Mirror 测试暂缺 meta；未改动这些文件。链接/登记/钩子自测没有另报失败。使用应用提供的 Python 运行时执行（PATH 无 Python，uv 的缓存/安装目录访问受限）。
- hooks 状态：会话注入消息确认项目 hooks 已运行；编辑统一 apply_patch。
- 工作区已有 SampleScene、URP、TMP/中文字体、Laila 场景/资产及文档改动，全部保留。
- 不暂存、不提交、不推送，不改 HANDOVER.md 或 docs/roadmap.md。

## 历史提交审查清单（主体已提交，当前剩余项见顶部）

以下只列本批内容；同一文件跨提交时按内容拆分，不整文件重复提交。新 Unity 资产及目录均连同生成的 `.meta`。

| 文件路径 | 位置 | 改了什么 |
| --- | --- | --- |
| `Assets/_Project/Scripts/Runtime/Dialogue/DialogueEndedEvent.cs` | 构造函数、Completed | 明确正常/跳过完成与取消/失败的区别，归 fix |
| `Assets/_Project/Scripts/Runtime/Dialogue/DialogueService.cs` | PlayAsync 收尾 | 资源释放成功后标记完成，事件携带 Completed，归 fix |
| `Assets/_Project/Scripts/Runtime/Quest/QuestObjectiveDriver.cs` | HandleDialogueEnded | 只让完成的对白推进 TalkTo，归 fix |
| `Assets/_Project/Scripts/Tests/EditMode/Dialogue/{DialogueServiceTests,DialogueReadStoreTests}.cs` | 结束事件断言及调用方 | 覆盖取消/异常 Completed=false 并适配事件签名，归 fix |
| `ai-docs/docs/modules/dialogue/{dialogue-module-guide,dialogue-external-api}.md`、`ai-docs/docs/modules/quest/{quest-module-guide,quest-external-api}.md` | Completed 契约相关段落 | 同步完成语义，归 fix；其余 Narrative 接入说明归 feat |
| `Assets/_Project/Scripts/Runtime/Narrative/{EncounterContext,NarrativeContent,NarrativeRules}.cs` | 标记合并、进入节点、请求收尾 | 支持配置标记及同轮条件读取，旧代际收尾不能清除新请求，归 feat |
| `Assets/_Project/Scripts/Runtime/Narrative/{NarrativeCatalog,NarrativeConditionSource,NarrativeService,NarrativeTrigger,NarrativeInstaller,NarrativeChangedEvent}.cs` | 新接入层 | 校验表、读取真实事实、驱动对白/任务/槽位并处理取消重试，归 feat |
| `Assets/_Project/Scripts/Runtime/Dialogue/{DialogueInstaller,DialogueService}.cs` | 条件源注册、带 targetId 的 PlayAsync | Narrative 条件源与稳定目标身份接入，归 feat |
| `Assets/_Project/Scripts/Runtime/Session/{GameSession,ISessionStateSource,SessionStateAdapter,SessionInstaller,SaveTriggerBridge}.cs` | 存取档与根注册 | 仅保存稳定叙事、校验并提交同一候选、监听叙事变化，归 feat |
| `Assets/_Project/Scenes/Boot.unity` | GameBootstrap | 挂载 NarrativeInstaller，归 feat |
| `Tables/Defines/narrative.xml`、`Tables/Data/narrative/{sample_encounter,sample_options,sample_wait}.json`、`Tables/Data/{narrative_encounters,narrative_quest_flags}.json`、`Tables/Data/dialogue/9001.json` | 新表结构与验证内容 | 遭遇/剧情/任务标记及条件选项样例，归 feat |
| `Assets/_Project/Scripts/Core/Config/Generated/Tables.cs`、`Assets/_Project/Scripts/Core/Config/Generated/narrative/` | Luban 输出 | 接入三张叙事表及其数据类型，归 feat |
| `Assets/_Project/Data/Config/{narrative_tbnarrativestory,narrative_tbnarrativeencounter,narrative_tbnarrativequestflag,dialogue_tbdialogue}.bytes` | Luban 输出 | 生成叙事及新增对白数据，归 feat |
| `Assets/_Project/Scripts/Tests/EditMode/Narrative/{NarrativeRulesTests,NarrativeCatalogTests,NarrativeServiceTests}.cs` | 规则、表、服务边界 | 标记、非法表、取消重试、迟到回调及槽位隔离回归，归 feat |
| `Assets/_Project/Scripts/Tests/EditMode/Session/GameSessionTests.cs` | 保存门禁与非法候选 | 验证旧档不被不完整状态覆盖、候选失败不提交，归 feat |
| `Assets/_Project/Scripts/Tests/Showcase/Narrative/NarrativeShowcase.cs` | 三条真实回放 | 任务与可见选项、稳定保存/继续、Once 去重、目标生命周期及跳过，归 feat |
| `ai-docs/docs/modules/narrative/{narrative-module-guide,narrative-external-api,narrative-extension-guide}.md` | 三件套 | 同步接线、边界、扩展入口与验证状态，归 feat |
| `ai-docs/docs/modules/dialogue/{dialogue-module-guide,dialogue-external-api,dialogue-extension-guide}.md`、`ai-docs/docs/modules/session/{session-module-guide,session-external-api}.md` | Narrative 接入相关段落 | 同步条件源与稳定存取档边界，归 feat |
| `PRP/narrative-dialogue/{prp,follow-up-integration,tasks}.md` | 当前实现、范围与证据 | 修订过时待办，保留 C5/G5 及未验收项，归 feat |

范围外：Mirror、Replay、DialogueShowcase、CharacterPuppetShowcase、气泡预制体、SampleScene、URP、TMP/中文字体、Laila 场景/资产/文档、MCP/harness 文件及其他聊天改动，不混入本清单。既有 Generated 文件中仅换行导致的状态标记不计作功能改动，不手动改写生成物。

协作文档交接：统一验证维护气泡修复；本聊天代为同步 Dialogue guide/extension 中根宽400、scale0.0035及3D回放舞台说明。这几段归气泡修复提交，不归上述 Narrative 两次提交；已核对实际预制体 diff 只有根缩放一行。

埋点门：四个受影响 Runtime 模块已扫描；新增代码无其余该埋未埋的点。沉淀门：本次无新结论需另写沉淀。

拟提交 1：

```text
fix(dialogue): 取消或失败的对白不再推进交谈任务

- DialogueEndedEvent 增加 Completed，正常与跳过完成均在资源收尾成功后确认
- QuestObjectiveDriver 忽略取消和异常结束通知
- 补结束事件回归断言并同步 Dialogue 与 Quest 契约
```

拟提交 2（依赖提交 1）：

```text
feat(narrative): 接通遭遇对白与槽位剧情状态

- 新增叙事表、内容校验、真实条件源和运行服务，并在 Boot 注册
- 同目标交互可重试未完成对白，旧异步结果按代际和激活身份隔离
- 任务完成映射剧情标记，Session 只保存稳定状态并校验读档候选
- 补规则与服务回归、真实选项和读档回放，同步模块文档与实施记录
- 留：C5/G5 对抗接入、完整目标中断恢复及真实章节内容
```

另列独立 Core 修复（不混入 Narrative feat）：

| 文件路径 | 位置 | 改了什么 |
| --- | --- | --- |
| `Assets/_Project/Scripts/Core/UI/UIService.cs` | OpenAsync / OpenNewAsync | 打开过渡失败或取消时完整回滚；并发同类打开等待共同完成源 |
| `Assets/_Project/Scripts/Tests/EditMode/Core/UIServiceTests.cs` | OpenAsync_CancelDuringTransition_RollsBackStackAndInstance | 复现过渡取消，检查两个调用取消、字典/栈清理、下层可见性与实例计数 |

```text
fix(ui): 打开过渡取消时回收未交付的面板

- UIService 将入栈和打开过渡纳入失败回滚，恢复下层界面、HUD与焦点
- 同类型并发打开等待同一完成源，避免返回随后被回收的半初始化实例
- 补取消过渡回归，验证两个调用一致取消及残留清理
```

以上拟提交说明保留追溯；主体及遗漏片段现已入库，人工视觉确认已收到。最终提交独立验证仍待完成，不标记整个模块无条件完成。
