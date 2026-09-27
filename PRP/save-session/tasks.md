# Tasks: 存档会话与主界面选槽（save-session）

按 [`prp.md`](prp.md) 执行，用户 2026-09-26 已对架构点头。状态只在有源码 / 检查 / 运行证据时更新。
派单按 `.claude/rules/model-routing.md`；波内任务互不依赖可并行，波间有契约依赖。
编辑器操作（预制体 / Boot / 资产）须在 `editor/state` 的 `is_playing == false && tests.is_running == false` 时做，其它会话正在跑回放时先写代码、后接线。

## 契约（先定死，波内并行照此写）

- Core 事件（`Game.Core.Events`，`readonly struct`）：`GameStateChangingEvent { Type From; Type To; }`（`GameFlow` 在调用前一状态 `ExitAsync` 之前发布）、`TitleContinueClickedEvent`、`TitleLoadClickedEvent`（空结构）。
- `GameQuit`：`static IDisposable RegisterBeforeQuit(Func<UniTask> hook)`；`Quit(reason)` 签名不变，内部先 await 全部钩子（总超时 2 秒）再退出；钩子执行器抽成纯类 `GameQuitHooks` 可测。
- `ISaveService.ResetAll()`：丢弃全部内存分区（下次 `Get<T>` 建默认）；`JsonSaveService` 实现。
- `TitleView`：新增 `continueButton`、`loadButton` 字段；`event Action OnContinueClicked, OnLoadClicked`；`SetContinueEnabled(bool)`。`TitleState` 把两个点击转发成上面两个事件。
- `ConfirmView : UIView`（`Core/UI/Views/`，Popup，`CloseOnCancel => true`）：`OnOpenAsync(arg)` 的 `arg` 是 `ConfirmRequest { string Message; string ConfirmText; string CancelText; }`（`Core/UI/ConfirmRequest.cs`，readonly struct）；`UniTask<bool> WaitAsync(CancellationToken)`，被外部关掉或取消返回 false。
- Session 事件（`Game.Session`）：`SessionStartedEvent { int Slot; bool IsNewGame; }`、`SaveCompletedEvent { int Slot; string Reason; bool Success; }`。
- `ISessionStateSource`（`Game.Session` 接口，由 `SessionStateAdapter` 实现，包住 Quest / Dialogue / Monster / UIService 的具体依赖）：`bool IsGameplayState`、`bool DialogueRunning`、`bool AnyPanelOpen`、`bool BattleResultPending`、`void CaptureEncounter(long tick)`（写进 `saves.Get<EncounterSaveData>()`）、`void PrepareRestore(bool fromSave)`（true → `MonsterEncounterState.PrepareRestore(saves.Get<EncounterSaveData>())`，`Validate` 失败则不准备并记警告；false → `ClearPreparedRestore`）、`string BuildProgressText()`（追踪中的任务标题 → 进行中的第一条主线标题 → 配置占位文案）。
- `GameSession`（IGameService + ITickable）：`int CurrentSlot`、`int LatestSlot`、`UniTask<bool> NewGameAsync(int slot, ct)`、`UniTask<bool> ContinueAsync(int slot, ct)`、`UniTask<IReadOnlyList<SlotInfo>> ReadSlotInfosAsync(ct)`、`UniTask DeleteSlotAsync(int slot, ct)`、`void RequestSave(string reason)`、`UniTask<bool> SaveNowAsync(string reason, ct)`。`SlotInfo { int Slot; SlotState State; DateTime SavedAtUtc; float PlaytimeSeconds; string ProgressText; }`，`SlotState { Empty, Available, Unavailable }`。
- `QuestService.ReloadFromSave()`：`rules.Restore(saves.Get<QuestSaveData>())` + `ActivateAvailable()` + `Flush()`。
- Boot 挂载顺序：`SessionInstaller` 排在 `GameBootstrap` 最后（`ExplorationInstaller` 之后）。

## 波 1（并行，三单）

- [x] **T1 Core 通用能力**（model: opus）
  `Core/Events/GameStateChangingEvent.cs`、`TitleContinueClickedEvent.cs`、`TitleLoadClickedEvent.cs` 新建；`Core/Flow/GameFlow.cs` 在 Exit 前发布；`Core/Boot/GameLifetimeScope.cs` 加三个 broker；`Core/Boot/GameQuit.cs` 加钩子表 + `Core/Boot/GameQuitHooks.cs`；`Core/Save/ISaveService.cs` / `JsonSaveService.cs` 加 `ResetAll()`；`Core/UI/Views/TitleView.cs` 两键两事件 + `SetContinueEnabled`；`Core/Flow/TitleState.cs` 转发；`Core/UI/ConfirmRequest.cs`、`Core/UI/Views/ConfirmView.cs` 新建；预制体 `Prefabs/UI/TitleView.prefab` 在 `Buttons` 布局组复制 `StartButton` 为 `ContinueButton` / `LoadButton`（节点名固定）并拖字段，`Prefabs/UI/ConfirmView.prefab` 新建（PC 尺寸，两个按钮，默认选中取消），Addressables UI 组地址 `ConfirmView`。测试：`Tests/EditMode/Core/GameFlowTests.cs` 补 Changing 在 Exit 前、Changed 在 Enter 后；`GameQuitHooksTests.cs`（顺序、异常不阻断、超时）；`JsonSaveServiceTests.cs` 补 `ResetAll`。
- [x] **T2 对白已读独立档案**（model: opus）
  `Runtime/Dialogue/DialogueReadProfile.cs`（`ISaveData`，Version 1，`List<string> Keys`）、`DialogueReadStore.cs`（IGameService：启动 `ReadProfileAsync<DialogueReadProfile>("dialogue-read")` 灌进单例 `DialogueReadData`；订阅 `DialogueService.OnEnded` 后写出，同帧多次合并一次；`Dispose` 退订）；`DialogueInstaller.cs` 注册（`As<IGameService>`，排在 `DialogueService` 之后）。测试 `Tests/EditMode/Dialogue/DialogueReadStoreTests.cs`（临时目录往返、空档案、合并写）。`dialogue-module-guide.md` 同步「已读记录持久化」。
- [x] **T3 Session 模块核心**（model: opus）
  `Runtime/Session/`：`SessionSaveData.cs`（Version 1：`SavedAtUtcTicks`、`PlaytimeSeconds`、`ProgressText`、`SceneKey`）、`SessionConfig.cs`（SO：`SlotCount=3`、保存提示标题与秒数、进度占位文案、退出钩子超时）、`SlotInfo.cs`、`SlotState.cs`、`SaveGateRules.cs`（纯：四个条件 → 能否存；待处理合并）、`ISessionStateSource.cs`、`SessionStateAdapter.cs`、`GameSession.cs`（槽位、新游戏 / 继续 / 删除 / 读槽信息、`RequestSave` 待处理标志、`Tick` 里评估闸门并累加游玩时长、落盘后发 `SaveCompletedEvent` 并 `INotificationService.Show`）、`SaveTriggerBridge.cs`（IStartable：订阅任务三事件、`CrateCollectedEvent`、`DialogueService.OnEnded`、`GameStateChangedEvent(To==MonsterEncounterState)`、`GameStateChangingEvent(From==MonsterEncounterState)` 同步捕获、`GameQuit.RegisterBeforeQuit`）、`SessionStartedEvent.cs`、`SaveCompletedEvent.cs`、`SessionInstaller.cs`（`InstallEvents` 两个 broker；`Install` 注册以上）。测试 `Tests/EditMode/Session/`：`SaveGateRulesTests`、`SessionSaveDataTests`、`GameSessionTests`（假 `ISessionStateSource` + 临时目录 `JsonSaveService`：新游戏建槽、继续读槽、坏文件 / 高版本槽 `Unavailable`、删除、`RequestSave` 在闸门关时不写开时写一次）。T1 未落地前编译报找不到 `GameStateChangingEvent / RegisterBeforeQuit / ResetAll` 属正常，按契约写。

## 波 2（T1、T3 落地后，并行两单）

- [x] **T4 标题路由与选槽面板**（model: opus）
  `Runtime/Session/SessionTitleRouter.cs`（IStartable：订阅三个标题事件；`GameStateChangedEvent.To == TitleState` 后 `ui.Get<TitleView>()` 调 `SetContinueEnabled(LatestSlot != 0)`；新游戏：有空槽用第一个空槽，否则开选槽面板新游戏模式；继续 → `ContinueAsync(LatestSlot)`；选择存档 → 选槽面板读档模式；失败用通知提示）、`SaveSlotsView.cs`（Panel，`CloseOnCancel` true，三行槽位 + 每行删除 + 返回，`Bind(IReadOnlyList<SlotInfo>, SlotsMode)`，只抛事件）、`SaveSlotsController.cs`（打开 / 绑定 / 覆盖与删除走 `ConfirmView.WaitAsync` / 关闭收尾）；预制体 `Prefabs/UI/SaveSlotsView.prefab`（PC 尺寸）+ Addressables 地址；`Runtime/Monster/MonsterInstaller.cs` 摘除第 37 行路由注册，`git rm` `MonsterTitleRouter.cs` 与 `.meta`；`Data/Session/SessionConfig.asset` 新建；Boot 挂 `SessionInstaller`（最后）并拖配置。测试 `SessionTitleRouterTests`（选空槽、无空槽、继续启用判定）。
- [x] **T5 任务模块重载**（model: sonnet）
  `Runtime/Quest/QuestService.cs` 加 `ReloadFromSave()`；`QuestInstaller.cs` 注册一个订阅 `SessionStartedEvent` 的入口点（或在 `QuestService` 内订阅，写明选择理由）；`Tests/EditMode/Quest/QuestServiceTests.cs` 补「读档后重载得到存档里的进度与追踪」。`quest-module-guide.md` 补一句。

## 波 3（并行两单）

- [x] **T6 Showcase 回放**（model: opus）
  `Tests/Showcase/Session/SessionShowcase.cs`：用例一「存 → 退 → 读一致」：新游戏 → 移动玩家 → 接任务并追踪 → 开一个箱 → 跑完一段对话 → 触发保存并等 `SaveCompletedEvent` → 回标题 → 继续 → 断言玩家位置、任务状态与追踪、背包数量、箱子开合、已读记录；用例二「坏档显示不可用」：写坏文件与高版本文件到两个槽 → 开选槽面板 → 断言两行「不可用」不可选。回放里用 `Stopwatch` 量一次 `SaveNowAsync` 的主线程耗时，写进汇报（A8 数字回填 prd.md 由 T7 做）。
- [x] **T7 文档与登记**（model: sonnet）
  `/generate-doc session` 三件套（maturity seed → 回放过后 stable）、`modules.json`、`catalog.md`；`docs/architecture.md` 5.5 补 `GameStateChangingEvent`、5.7 补存档会话契约与「分区不缓存实例」；`docs/developer-guide.md` 补「存档怎么工作 / 怎么加一个分区」；`docs/roadmap.md` E1 / E2 状态；`prd.md` A8 回填数字。

## 波 4（主窗口验收）

- [ ] **T8 验收**（主窗口 + sonnet 跑命令）
  编译零错误；EditMode 全量；lint；`gc_scan`；从真实容器解析 `GameSession / SaveTriggerBridge / SessionTitleRouter / DialogueReadStore` 且解析 `MonsterTitleRouter` 失败；Showcase 两条 PASS；code-reviewer PASS；`/review-change` 待用户授权。

## 验收覆盖对账（prd.md A1–A9）

| 验收 | 负责任务 |
| --- | --- |
| A1 触发点各一次、同稳定点合并 | T3（SaveTriggerBridge + SaveGateRules）→ GameSessionTests |
| A2 对白 / 面板期间延后并合并 | T3 → SaveGateRulesTests、GameSessionTests |
| A3 存 → 退 → 读一致 | T2、T3、T4、T5 → T6 用例一 |
| A4 槽位面板三态 | T3（ReadSlotInfos）、T4（SaveSlotsView）→ T6 用例二 |
| A5 主菜单五项与键盘 / 手柄 | T1（TitleView / ConfirmView）、T4 → T8 目视 |
| A6 读档失败回滚 | T3（ContinueAsync 失败不动内存、通知）→ GameSessionTests |
| A7 版本与高版本拒绝 | 已有 JsonSaveService 机制 → T3 GameSessionTests 高版本槽 |
| A8 写盘不阻塞主线程 | T6 量数字 → T7 回填 |
| A9 标题路由归主菜单 | T4（MonsterTitleRouter 退役）→ T8 容器解析核对 |

无遗漏。

## 进度

- [x] PRD、PRP、tasks（2026-09-26）。
- [x] T1–T7 全部完成（2026-09-26）：Core 相关组 304、Dialogue 81、Session 38、Quest + Loot 9 条各自全绿；SessionShowcase 2 / 2 PASS（`Logs/verify/session/latest.md`），A8 实测同步段 0.38 ms、整段 6.80 ms。
- [x] T9 追加（执行中发现）：回放统一用 `PlatformServiceBase.SaveRootOverride` 指向临时目录，不再碰真实存档；起因是每条从「开始」进场景的回放都会写满三个槽。
- [x] T8 主窗口复核：编译零错误，EditMode 全量 700 / 700，lint 本波文件零违规，`.meta` 齐全；容器接线由回放从真实容器解析验证（反射确认 `MonsterTitleRouter` 已不在程序集）。code-reviewer 结论见下一条。
- [x] code-reviewer PASS（无 BLOCK / WARN；INFO：`GameQuit` 超时为 Core 常量、`SaveNowAsync` 与 Tick 自动保存重叠只会多写一次不会损坏）。
- [ ] 开发者视觉验收：从 Boot 进 Play 走「开始 → 玩一会 → Esc 回标题 → 继续」「选择存档」「覆盖 / 删除确认」；`/review-change` 授权后提交。

## 执行中确认的事实

- 编辑器曾出现 `isCompiling` 长期 true、控制台错误与磁盘不符（程序集重载锁泄漏），已解开并记入 pitfalls。
- 一个 sonnet agent 误用 `clear_stuck` 清掉了另一会话的 PlayMode 任务，已记入 pitfalls 与派单规矩。
- Play 中曾同时出现两个 GameBootstrap（旧内存 Boot + 新磁盘 Boot），非本波 agent 的场景加载所致；那轮 Play 的观察不作数。
- `SaveSlotsController` 额外订阅 `GameStateChangingEvent(From==TitleState)` 自关面板；`SessionStateAdapter.IsGameplayState` 多判 `EncounterStep.IsActive`；`BattleResultPending` 判「未消费」；未开局或不在玩法状态时 `SaveNowAsync` 不写盘。
