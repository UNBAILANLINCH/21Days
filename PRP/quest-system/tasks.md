# Tasks: 任务系统（主线 / 支线 · 追踪 · 指引）

> 设计见 [`prp.md`](prp.md)，验收见 [`prd.md`](prd.md)。每项：**文件 → 做什么 → 产出 → model**。
> 所有 `.cs` / JSON / XML 用 Write / Edit 写（不走 Bash heredoc）；场景与预制体走 Unity MCP。
> 主窗口对每波产物独立复核（编译 `read_console`、`run_tests` 用例总数、`stat` 文件），subagent 自报不算数。

## 波次与覆盖

| 波 | 任务 | model | 覆盖 PRD |
| --- | --- | --- | --- |
| W0 | T1 内容表与生成 · T2 Core 事件注册重载 | sonnet · opus | A4（数据）、3.5 |
| W1 | T3 规则 / 内容模型 / 存档 / 事件（含 EditMode）· T4 指引数学（含 EditMode）· T5 内容目录（含 EditMode） | opus · sonnet · opus | A1 A2 A3 A5 · A6 · A4 |
| W2 | T6 服务 / 场景侧 / 注册 · T7 两个 View / HUD 呈现器 / 面板控制器 | opus · opus | A7 A8 A9 的代码侧 |
| W3 | T8 预制体 / 配置资产 / Addressables / Boot（MCP）· T9 SampleScene 地点 + Verify 场景（MCP） | opus · sonnet | A7 A8 接线 |
| W4 | T10 Showcase · T11 审查 / 文档 / 登记 · T12 待审清单 | opus · sonnet · 主窗口 | A7 A8 A9 · 通用 |

---

## W0 目录、内容表与 Core 扩展（T0 先做，T1 / T2 可并行）

- [x] **T0** 新建目录（无新 asmdef，全部落在既有程序集内）：`Assets/_Project/Scripts/Runtime/Quest/`（`Game.Runtime`）、`Assets/_Project/Scripts/Tests/EditMode/Quest/`（`Game.Tests.EditMode`）、`Assets/_Project/Scripts/Tests/Showcase/Quest/`（`Game.Tests.Showcase`）、`Assets/_Project/Data/Quest/`、`Tables/Data/quest/`。空目录不进 git，各目录的第一个文件由后续任务写入；`.meta` 由 Unity 刷新生成。（model: sonnet，与 T1 合并派单）
- [x] **T1** `Tables/Defines/quest.xml`、`Tables/Data/quest/1001.json`、`1002.json`、`2001.json` — 按 PRP 3.1 写 schema（module `quest`，enum `QuestKind` / `ObjectiveKind`，bean `Objective` / `Quest`，table `TbQuest` `input="quest"`）与三条示例任务（JSON 每个字段显式写：`prerequisites: []`、`location: ""`、`count: 1`）。跑 `powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1`。产出：`Assets/_Project/Scripts/Core/Config/Generated/quest/`（`cfg.quest.*`）、`Assets/_Project/Data/Config/quest_tbquest.bytes`、`Generated/Tables.cs` 多出 `TbQuest`。复核：三个产出 `stat` 存在，`grep TbQuest Tables.cs` 命中。（model: sonnet）
- [x] **T2** `Assets/_Project/Scripts/Core/Boot/GameplayInstaller.cs`、`Core/Boot/GameLifetimeScope.cs`、`Core/Events/EventConventions.cs` — `GameplayInstaller` 加 `public virtual void InstallEvents(IContainerBuilder builder, MessagePipeOptions options) { }`（文件头注释补「为什么扩展」）；`GameLifetimeScope.Configure` 把 `options` 传给 `InstallGameplay(builder, options)`，对每个 installer 先 `InstallEvents` 再 `Install`；`EventConventions.cs` 第 4 条补「模块事件在自己 Installer 的 `InstallEvents` 里注册 broker（拿不到根作用域 options 的问题已由此解决）」。不改任何既有 Installer。复核：`read_console` 零编译错误；EditMode 全量仍通过。（model: opus）

## W1 纯逻辑层（T3 / T4 可并行；T5 依赖 T1 生成物与 T3 的内容模型）

- [x] **T3** `Assets/_Project/Scripts/Runtime/Quest/` 新建：`QuestKind.cs`、`QuestObjectiveKind.cs`、`QuestState.cs`、`QuestObjectiveDefinition.cs`、`QuestDefinition.cs`、`QuestContent.cs`、`QuestProgress.cs`、`QuestRules.cs`、`QuestProgressData.cs`、`QuestSaveData.cs`、`QuestActivatedEvent.cs`、`QuestObjectiveProgressedEvent.cs`、`QuestCompletedEvent.cs`、`QuestTrackingChangedEvent.cs`（命名空间 `Game.Quest`，纯 C#，不引用 UnityEngine；契约按 PRP 3.2–3.5）。测试 `Assets/_Project/Scripts/Tests/EditMode/Quest/QuestContentTests.cs`（重复 id / 环 / 自环 / TalkTo 非数字 / ReachLocation 空 key / 目标为空各抛，合法内容可构造）、`QuestRulesTests.cs`（A1：前置激活、开局无前置激活、单主线只激活 id 最小、当前目标累计与推进、`RequiredCount` 多次、任务完成级联激活、非当前目标不计、`amount <= 0` 忽略；A2：`GetOrdered` 主线置顶 / 支线按接取序 / 无主线从支线起；A3：首次初始化追踪当前主线、`Track` 非 InProgress 返回 false、切换与 `Untrack`、追踪的支线完成回主线、无主线为 0、`Untrack` 后新主线不抢回、事件只在变化时抛）、`QuestSaveDataTests.cs`（A5：`Game.Tests.EditMode` 不引用 Newtonsoft，照 `Core/JsonSaveServiceTests` 的做法用 `JsonSaveService` 在临时目录存到槽位、用新实例 `LoadAsync` 读回 `Get<QuestSaveData>()`，再 `Restore` 到新 Rules，状态一致；空分区 `Initialized == false` → 全新开始；分区里未知 id 跳过；追踪的任务已完成时 `TrackedId` 归 0）。测试用 `NullTelemetryScope.Instance`。产出：14 个源文件 + 3 个测试文件；`/unity-test EditMode Quest` 全绿。（model: opus）
- [x] **T4** `Assets/_Project/Scripts/Runtime/Quest/QuestGuidance.cs`（readonly struct）、`QuestGuidanceMath.cs`（静态纯函数，只引用 `UnityEngine.Vector2/Vector3/Mathf`）— 按 PRP 3.8 的 `Solve` / `DistanceMeters` 规则实现。测试 `Tests/EditMode/Quest/QuestGuidanceMathTests.cs`（A6：屏内中心点 → `OnScreen`、位置 = 投影 + hover、无箭头；屏内边界 `(0, 1)` 仍算屏内；屏外右 / 上 / 左下 → 贴内缩矩形对应边、箭头角度指向目标；`z < 0` 翻转后视为屏外；距离 `(0,0,0)-(3,4,0)` = 5、`2.4` → 2、`2.5` → 3）。产出：2 源 + 1 测试；EditMode 全绿。（model: sonnet）
- [x] **T5** `Assets/_Project/Scripts/Runtime/Quest/QuestCatalog.cs` — 惰性读 `config.Tables.TbQuest`（`global::cfg.quest.*`）翻译成 `QuestContent`；TalkTo 的对话编号在 `Tables.TbDialogue` 里不存在则抛 `ArgumentException`（带任务 id）；不在构造函数读表；埋 `catalog_built` / `catalog_invalid`。测试 `Tests/EditMode/Quest/QuestCatalogTests.cs`（A4：照 `DialogueCatalogTests` 的方式加载真实生成物构造 `Tables`，断言三条示例任务都能翻译、主线 1002 前置为 1001、每个 TalkTo 的对话存在；用假表数据验证「对话不存在 → 抛」）。产出：1 源 + 1 测试；EditMode 全绿。（model: opus）

## W2 服务、场景侧与表现（T6 / T7 可并行，都按 PRP 契约写；合并后一起编译）

- [x] **T6** `Runtime/Quest/QuestConfig.cs`（SO，PRP 3.9）、`QuestService.cs`（PRP 3.6：`IGameService`、`IsReady`、`Report / Track / Untrack`、查询、每次变化 `CaptureInto(saves.Get<QuestSaveData>())`、C# event → `IPublisher<T>` 在方法末尾统一发布、埋点 `initialized / activated / objective_progressed / completed / tracked / untracked / report_ignored`）、`QuestLocation.cs`（PRP 3.7，`#if UNITY_EDITOR` Gizmos）、`QuestSceneBinder.cs`（扫描 / `Locations` / `TryGetLocation` / `SceneCamera` / `PlayerAnchor` / `TryResolveTarget`）、`QuestObjectiveDriver.cs`（`OnEnded` → `Report(TalkTo)`；`Tick` 测距 → `Report(ReachLocation)`；无分配）、`QuestInstaller.cs`（PRP 3.10：`InstallEvents` 注册四个 broker；`Install` 注册全部；缺 Config 时 Error + 默认顶上）。产出：6 源文件；`read_console` 零错误；`python .claude/skills/project-lint/lint.py` 零违规。（model: opus）
- [x] **T7** `Runtime/Quest/QuestHudView.cs`、`QuestPanelView.cs`、`QuestHudPresenter.cs`、`QuestPanelController.cs` — 按 PRP 3.8：View 只显示与抛事件、`Validate()` 逐字段点名、列表项复用池；Presenter 在 `BootCompletedEvent` 后开 HUD（含 disposed 兜底）、订阅四个任务事件与对白 `OnStarted / OnEnded`、`Tick` 算指引并按间隔节流距离文本、HUD 点击开面板；Controller 持暂停令牌 + 输入图记录 / 恢复（`input.Actions == null` 跳过）、面板事件 → `service.Track / Untrack`。产出：4 源文件；`read_console` 零错误；lint 零违规。（model: opus）

## W3 资产与场景（MCP；两项顺序执行，避免两个代理同时写编辑器）

- [x] **T8** MCP：`Assets/_Project/Prefabs/UI/QuestHudView.prefab`、`QuestPanelView.prefab`（结构按 PRP 第 4 节，字段全部拖赋；TMP 用工程现有字体；Hud 预制体左上锚点、面板全屏半透明底）；Addressables UI 组登记地址 `QuestHudView` / `QuestPanelView`；`Assets/_Project/Data/Quest/QuestConfig.asset`；`Scenes/Boot.unity` 的 `GameBootstrap` 加 `QuestInstaller` 并拖 Config（改前 `mcpforunity://editor/state` 确认不在 Play、Boot.unity 在 git 里干净）。每步存盘并读回核对。产出：2 预制体 + 1 配置资产 + Boot 改动 + Addressables 条目。（model: opus）
- [x] **T9**（Verify 场景与 SampleScene 两个地点均已建并读回核对）MCP：`Assets/Scenes/SampleScene.unity` 加空物体 `QuestLocation_Camp`（`QuestLocation` key `camp`，放营地附近）、`QuestLocation_Lookout`（key `lookout`，远离出生点），半径 2；新建 `Assets/_Project/Scenes/Verify/Quest.unity`：透视相机（照 SampleScene：FOV 28、旋转 (38,0,0)、`MainCamera` tag、`PhysicsRaycaster`）、`player`（`DialogueInteractionActor`）、`Elder`（`BoxCollider` + `DialogueInteractable` id 1001）、`Traveler`（1002）、`QuestLocation` `camp`（画面内、距玩家 8 米）与 `lookout`（画面外 20 米）。不进 Build Settings。产出：SampleScene 两个物体、Verify 场景一个。（model: sonnet）

## W4 验证、沉淀、待审

- [x] **T10**（3 条回放 PASS，报告 Logs/verify/quest/20260925-104851；首跑抓到 ReachLocation 无指引缺陷已修） `Assets/_Project/Scripts/Tests/Showcase/Quest/QuestShowcase.cs` — 复制 `DialogueShowcase.cs` 的骨架（`LoadBootScene`、`WaitForBootReady`、`DestroyBootScope`、`ResolveService<T>`），三条用例：① 「追踪与指引」：启动后 HUD 显示「找到落脚处 / 与长者谈谈」（A8），指引悬浮在 Elder 上方（`OnScreen`），把 `player` 挪到 Elder 出画面外 → 贴边 + 箭头 + 距离文本（A6 肉眼）；② 「联动推进」：`DialogueService.PlayAsync(1001)` 后照 `DialogueShowcase` 的跳过路径驱动到结束 → 目标 ① 完成、HUD 切到「前往营地」（A7）；把 `player` 挪进 `camp` 半径 → 1001 完成、1002 激活、HUD 切到「与旅人叙旧」；`service.Report(Counter, "x", 2)` 对无匹配目标返回 0；③ 「面板与暂停」：模拟 HUD 点击（调 `QuestHudView.OnClicked` 或 `QuestPanelController.OpenAsync`）→ 面板开、列表第一项是主线、`IWorldPauseService.IsPaused == true` 且 Gameplay 图关（A9）；选支线 2001 → 「追踪」→ HUD 切到支线；再点 → 「取消追踪」→ HUD 占位「未追踪任务」、指引隐藏；关面板 → 暂停释放、输入恢复；把 `player` 挪进 `lookout` → 2001 完成，追踪回到主线（A3 肉眼）；末尾 `Snapshot`。主窗口用 MCP 跑 PlayMode 复核。（model: opus）
- [x] **T11**（code-reviewer PASS + Flush 重入护栏已补；三件套 / catalog / modules.json 已登记；gc_scan 唯一告警是他人的字体资产） 审查与沉淀：对 `Runtime/Quest/`、Core 两个文件、测试跑 `project-lint`；派 `code-reviewer` 审模块（无 BLOCK）；`/unity-test EditMode` 全量（用例总数比改前增加）；`/generate-doc quest` 生成三件套，`ai-docs/docs/catalog.md` 模块表补 Quest 一行，`.claude/skills/generate-doc/modules.json` 登记；`python .claude/skills/evolution/gc_scan.py` 无失效引用；新文件 `.meta` 已由 Unity 生成。（model: sonnet）
- [x] **T13**（视觉验收后：屏内改世界空间头顶标记 `QuestTargetMarker`、屏外贴边箭头；`QuestTarget` 锚点解析；面板「< 返回」按钮；预制体 `Prefabs/World/QuestTargetMarker.prefab` 地址 `QuestTargetMarker`；回放 3/3 PASS 20260925-161334；三件套已同步）（model: opus + sonnet）
- [x] **T12**（用户口头连续授权「提交」，未走 `/review-change`；按 commit-convention 分三次提交 22f3cc5 feat(core) / c363891 feat(quest) / 7d02757 docs(quest)，2026-09-26 核实已推送 origin/main）（主窗口）
  - [x] 2026-09-25 用户口头授权后按 commit-convention 分三次提交：22f3cc5 feat(core)、c363891 feat(quest)、7d02757 docs(quest)；`/review-change` 未走（该命令只能用户输入）。**核实修正（2026-09-26）**：`git merge-base --is-ancestor <hash> origin/main` 对三个提交均返回已推送，与本文件顶部小结「未推送」及原始任务指令的措辞不一致——以此处 git 核实结果为准，三个提交已在 `origin/main` 上。

## 验收覆盖对照

| PRD | 任务 |
| --- | --- |
| A1 规则 | T3 `QuestRulesTests` |
| A2 排序 | T3 `QuestRulesTests.GetOrdered_*` |
| A3 追踪 | T3 `QuestRulesTests.Track_*`；T10 ③ 肉眼 |
| A4 内容 | T1 数据、T3 `QuestContentTests`、T5 `QuestCatalogTests` |
| A5 存档 | T3 `QuestSaveDataTests` |
| A6 指引数学 | T4 `QuestGuidanceMathTests`；T10 ① 肉眼 |
| A7 联动 | T6 驱动器、T10 ② |
| A8 表现 | T7 View / Presenter、T8 预制体、T10 ①③ |
| A9 暂停 | T7 Controller、T10 ③ |

每条验收标准至少一项任务负责；没有孤儿任务。
