# Tasks: 演出管线（performance-pipeline）

> 有序、文件级、可逐项打勾。契约以 [`prp.md`](prp.md) 为准。每波收口：编译零错误 → EditMode 全绿 → 主窗口复核。
> 验收覆盖：A1 ← T1/T2/T5；A2/A3 ← T6–T11/T25；A4 ← T12–T14/T25；A5 ← T20–T23/T25；A6 ← T15–T19；A7 ← T24；A8 ← T27–T29。

## 波 1 运行时核心（model: opus，一人）

- [x] T0 `Assets/_Project/Scripts/Runtime/Game.Runtime.asmdef` — `references` 加 `Unity.Timeline`、`Unity.RenderPipelines.Universal.Runtime`；`Game.Tests.EditMode.asmdef` 加 `Unity.Timeline`。
- [x] T1 `Runtime/Performance/PerformanceRules.cs`、`PerformancePolicy.cs`、`PerformanceOutcome.cs`、`PerformanceResult.cs` — 纯 C# 阶段机与策略快照（prp 2.4），只注入 `ITelemetryScope`。
- [x] T2 `Runtime/Performance/PerformanceSaveData.cs` — 存档分区（`PlayedIds`，版本 1）。
- [x] T3 `Runtime/Performance/PerformanceConfig.cs` — ScriptableObject（`[CreateAssetMenu(menuName = "21Days/Performance/PerformanceConfig")]`）。
- [x] T4 `Runtime/Performance/IPerformanceService.cs`、`PerformanceStartedEvent.cs`、`PerformanceEndedEvent.cs`、`PerformanceIdAttribute.cs`。
- [x] T5 `Scripts/Tests/EditMode/Performance/PerformanceRulesTests.cs`、`PerformancePolicyTests.cs`、`PerformanceSaveDataTests.cs` — 覆盖阶段迁移 / 停顿确认 / 长按计时 / 重入 / 取消 / 非法策略 / 去重。
- [x] T6 `Runtime/Performance/Timeline/SubtitleTrack.cs`、`SubtitleClip.cs`、`SubtitleBehaviour.cs`、`SubtitleMixerBehaviour.cs`、`IPerformanceSubtitleSink.cs` — 字幕轨（mixer 经 director 物体上的 `PerformanceStage` 拿 sink）。
- [x] T7 `Runtime/Performance/Timeline/ExpressionTrack.cs`、`ExpressionClip.cs`、`ExpressionBehaviour.cs`、`ExpressionMixerBehaviour.cs` — 表情轨，绑定 `PerformanceActor`。
- [x] T8 `Runtime/Performance/Timeline/HoldMarker.cs` — `Marker, INotification`（不在编辑模式触发）。
- [x] T9 `Runtime/Performance/PerformanceActor.cs`、`SpritePerformanceActor.cs`、`PerformanceStage.cs` — 演员抽象、占位演员、舞台（`INotificationReceiver`，`Play / Resume / Stop`，`OnHold / OnFinished`，`BuildPolicy(config)`）。
- [x] T10 `Runtime/Performance/PerformanceView.cs` — Panel 全屏、`CloseOnCancel = false`、实现 sink；黑边 / 字幕 / ▼ / 跳过进度环 / 黑场，LitMotion unscaled。
- [x] T11 `Runtime/Performance/PerformanceService.cs` — prp 2.5 流程含相机叠加与退路、输入图只恢复进来前状态、埋点、事件。
- [x] T12 `Runtime/Performance/PerformanceTrigger.cs`、`PerformanceTriggerActor.cs`、`PerformanceTriggerRules.cs`（纯 C# 判定 once / busy / played）+ `Tests/EditMode/Performance/PerformanceTriggerRulesTests.cs`。
- [x] T13 `Runtime/Performance/PerformanceSceneBinder.cs` — 入口点，启动与 `sceneLoaded` 扫描绑定、`OnSceneStart` 触发。
- [x] T14 `Runtime/Performance/PerformanceInstaller.cs` — 事件 broker、配置、服务（工厂式）、Binder；接线要求写在类注释。
- [x] 波 1 收口：编译零错误；`.meta` 齐全；lint 零违规；主窗口复跑 Performance + Dialogue 组 EditMode 132/132（2026-09-26）。追加：`IPerformanceService.Confirm() / Skip()` 供回放与编辑器试播；`PerformancePhase.cs` 单独文件；`Outcome` 为可空。

## 波 2A 编辑器与 Live2D 适配（model: opus）

- [x] T15 `Scripts/Editor/Game.Editor.asmdef` — 加 `Unity.Timeline`、`Unity.Timeline.Editor`。
- [x] T16 `Scripts/Editor/Performance/PerformanceTemplateFactory.cs` — 建时间轴 + 预制体 + 层 + Addressables 登记（prp 2.8）；`Tests/EditMode/Editor/Performance/PerformanceTemplateFactoryTests.cs`（临时目录，TearDown 删干净）。
- [x] T17 `Scripts/Editor/Performance/PerformanceValidator.cs`、`PerformanceIssue.cs` + `Tests/EditMode/Editor/Performance/PerformanceValidatorTests.cs`。
- [x] T18 `Scripts/Editor/Performance/PerformanceEditorWindow.cs`（菜单 `21Days/演出/演出编辑器`）、`PerformanceStageEditor.cs`、`PerformanceIdDrawer.cs`、`SubtitleClipEditor.cs`、`ExpressionClipEditor.cs`、`HoldMarkerEditor.cs`。
- [x] T19 `Scripts/Editor/Performance/Live2DDefineSync.cs` — SDK 在则加 `LIVE2D_CUBISM` 符号，否则移除；文件头写明会写 PlayerSettings；菜单 `21Days/演出/检查 Live2D 符号` 为状态锚点。
- [x] T24 `Runtime/Live2D/Game.Live2D.asmdef`（已建并实测：`defineConstraints` 未满足时 Unity 跳过整个 asmdef，缺失引用零错误零警告，`GetAssemblies` 里无 `Game.Live2D`，退路未触发）、`Live2DAssemblyInfo.cs` 占位（已建）；`Live2DPerformanceActor.cs` 已写（`#if LIVE2D_CUBISM`，API 已对照官方源码，未本地编译）。**修正**：官方运行时 asmdef 名是 `Live2D.Cubism`（非 Core / Framework），asmdef 引用与 `Live2DDefineSync` 检测名改为 `Live2D.Cubism`（波 3b 顺手改）。
- [x] T18（部分）`PerformanceIdDrawer.cs` 已建：读 Addressables `Performance` 组做下拉，未登记红字。

## 波 2B 对白节点插播（model: opus，与 2A 并行）

- [x] T20 `Tables/Defines/dialogue.xml` — `Node` 加 `performance`（string）；`Tables/Data/dialogue/1001.json`、`1002.json` 每个节点补 `"performance": ""`；新建 `1003.json`（验证用：3 句，第 2 句 `performance = "perf_sample_greeting"`，说话人 `elder` / `traveler`）；跑 `scripts/gen-tables.ps1`。（生成物只变 `Generated/dialogue/Node.cs` 与 `dialogue_tbdialogue.bytes`）
- [x] T21 `Runtime/Dialogue/DialogueContent.cs`（`Node.PerformanceId`）、`DialogueCatalog.cs`（翻译）。
- [x] T22 `Runtime/Dialogue/DialogueController.cs`（`PrepareAsync` 前插播、`Performing` 期间不收输入不推进）、`DialogueInstaller.cs`（`TryResolve<IPerformanceService>`）。插播点 `DialogueController.cs:151` → `PerformBeforeNodeAsync`（:325）；只在 `Phase == Preparing` 且非跳过快进时播；`Overlaid` 含 `performing`。
- [x] T23 `Tests/EditMode/Dialogue/DialogueCatalogTests.cs` 补翻译断言（+3 条，Dialogue 过滤 84/84）；`ai-docs/docs/modules/dialogue/` guide 与 extension-guide 已同步「插播演出」。
- 发现：`DialogueView` 在 Popup 层，Panel 全屏盖不住 → 决定由服务在 `HideHud` 时同时关 Popup 层（已通知波 1，prp 1.1 / 2.5 已改）。

## 波 3 资产、验证场景、回放（model: opus，需 Unity MCP）

- 波 2A 收口：编辑器组 EditMode 35/35（agent 自报），主窗口复跑见 T30；`Game.Editor.asmdef` 未加 URP 引用，工厂 / 校验器用类型名反射读写 `m_CameraType`。

- [x] T25a 工程资产：图层 `Performance`（第 9 槽，`TagManager.asset` 曾被并行会话改写两次、已补回）；`Data/Performance/PerformanceConfig.asset`；Boot `GameBootstrap` 顺序 Player → Monster → Dialogue → **Performance** → Quest → Loot → Inventory → Exploration。Addressables `Performance` 组交给模板工厂建。
- [x] T25b `Prefabs/UI/PerformanceView.prefab`（UI 组，地址 `PerformanceView`），10 个字段全接上。
- [x] T25c 示例演出 `perf_sample_greeting`：用 `PerformanceTemplateFactory` 建，演员用 `Portrait_elder_default / angry` 两个表情，Animation 轨做入场平移，字幕 3 条，1 个 `HoldMarker`，时长约 8 s。
- [x] T25d `Scenes/Verify/Performance.unity`：Main Camera（剔除去掉第 9 层）、Player（Kinematic Rigidbody2D 开 `useFullKinematicContacts`，否则触发器不回调）、Elder（1003）、Trigger_Intro（OnEnter，once）。
- [x] T25e `Scripts/Tests/Showcase/Performance/PerformanceShowcase.cs` — 4 条（A2–A5），确认 / 跳过走 `service.Confirm() / Skip()`；停顿判定读容器里的 `PerformanceRules.Phase`。
- [x] T26 `/verify-module Performance` PASS 4/4（第 3 轮，`Logs/verify/performance/20260926-062316/`），主窗口读过报告与 01 / 02 / 05 / 06 四张截图：黑边、字幕、▼、对白框隐藏与恢复都在；相机叠加生效（无 `camera_stack_unavailable`）。迭代中发现：Timeline 资产被 Undo 回滚成空壳（推断是别的测试运行器收尾回滚 Undo 组），重建后 `Undo.ClearUndo`；Animation 轨 `ApplyTransformOffsets` 需把轨道偏移设为片段首帧。遗留：舞台立绘底边被下黑边遮约 0.3 单位（示例内容，可调）；模板工厂收尾宜清 Undo。

## 波 4 文档、审查、埋点核对（sonnet 为主）

- [x] T27 `/generate-doc Performance` 三件套 + `ai-docs/docs/catalog.md` 加行 + `.claude/skills/generate-doc/modules.json` 登记（sonnet）。已完成：guide 234 行 / external-api 88 / extension-guide 78，catalog 与 modules.json 已登记，gc_scan 无本模块告警。
- [x] T28 `docs/modules/performance.md` 策划 / 美术说明 + `docs/modules/README.md` 加行；`docs/developer-guide.md` 加「Live2D 接入与演出制作」一节；`docs/artist-guide.md` 提一句演出目录；`docs/roadmap.md` D3 行与 W3 进度回填（sonnet）。已完成：`docs/modules/performance.md` 八节（含「给动画师」）、README 加行、developer-guide 6.16、artist-guide 第 3 章两行 + 6.11、roadmap D3 / W3。
- [x] T29 code-reviewer（sonnet）：**PASS**，无 BLOCK / WARN；一条 INFO——`Game.Tests.EditMode` 引用 `Game.Editor`（任务编辑器会话所加，本模块的编辑器测试也用到），`project-root.md` 依赖图未画这条边，下次改规则时补。
- [x] T30 主窗口复核（2026-09-26）：控制台无 `error CS`；EditMode 全量 645/645；`invariants.py` / `gc_scan` 仅 ToastView 未登记与字体 Dynamic Data 两条他人遗留；埋点门：`scan.py Performance` 的 10 条候选全是参数校验 throw / 普通 `return false` / 已走 Log 桥的分支，无该埋未埋；沉淀门：pitfalls 追加 3 条；模板工厂已补 `Undo.ClearUndo`。清单已列，停下等授权。

## 波 5 后续（2026-09-26 用户追加，提交 c40b51c 之后）

- [x] T31 SampleScene 挂示例演出：`player` 根加 `PerformanceTriggerActor`、`Trigger_VillageEntrance` 放村民东南 (9.5, 4.888, 2.5)（避开出生点→长者 / 物资箱路径与各 Showcase 传送点）、主相机剔除遮罩 -1 → -513；冒烟起播 / 停顿 / 跳过埋点齐全。**发现 bug**：主相机用 1 号 UniversalRenderer、舞台相机默认 0 号 Renderer2D，URP 拒绝叠加（每帧告警、舞台不画），服务未走退路 → T31b 修。
- [x] T32 示例演出立绘上移 0.5 单位（y 曲线 -1.5→-1.0，轨道偏移同步），t=6 s 采样 y=-0.983；校验零问题。
- [x] T31b `PerformanceService.AttachCamera` 运行时把舞台相机渲染器对齐主相机（反射读 `m_RendererIndex` + 类型比较退路 `renderer_mismatch`）；`HoldMarker` 3.1 s → 2.9 s（落在字幕 1 内）；SampleScene 冒烟复验立绘可见（model: opus）。已完成：反射读 `m_RendererIndex` 对齐、`renderer_mismatch` 退路；标记 2.9 s；冒烟截图里舞台在 3D 灰盒上完整可见、无渲染器告警（主 / 舞台相机均 UniversalRenderer idx 1）。
- [x] T33 Dialogue D1 / D2：立绘入场 / 退场滑动、表情交叉淡化、说话者压暗；对话框 `SlideUp` 开合、名牌 punch、标点停顿可配（`DialogueTypingCadence` 纯 C# + 测试）；Showcase 加一条；三件套 / 策划说明 / roadmap 同步（model: opus）。已完成：`DialogueTypingCadence` / `DialogueMotionSettings` / `DialoguePortraitSlot` 新建，`DialogueView` 预制体 Transition=SlideUp，EditMode Dialogue 98/98（agent 自报），回放 7/7 PASS（`Logs/verify/dialogue/20260926-141646/`，主窗口看过 12 / 13 两张）。出入：控制器改为先加载完新立绘再逐槽切换、旧句柄延迟释放；恢复路径判定用 Typing / AwaitAdvance；旁白两侧都压暗。
- [x] T34 主窗口复核（2026-09-26 14:48）：控制台无 `error CS`；EditMode 全量 718/718；回放 Performance 4/4、Dialogue 7/7、Session 2/2（并行会话的用例，确认打字节奏与触发区没把它弄挂）；`ProjectSettings/` 无副作用。清单已列，等授权。
