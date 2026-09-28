---
type: module-guide
module: session
layer: runtime
maturity: seed
---

# Session 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Session/` 之前读这份。对外怎么调看
> [`session-external-api.md`](session-external-api.md)，要加东西看 [`session-extension-guide.md`](session-extension-guide.md)。
> 设计定稿见 [`PRP/save-session/prp.md`](../../../../PRP/save-session/prp.md)；与源码不符时以源码为准。

## 职责边界

**做**：当前用哪个槽（`GameSession.CurrentSlot`）；关键节点自动保存的触发、合并与稳定边界闸门；槽位元数据
（保存时间、游玩时长、进度描述、场景键）；新游戏 / 继续 / 删除 / 读槽摘要；标题「开始 / 继续 / 选择存档」的路由；
选槽面板；游玩时长累计；退出前的最后一次保存。

**不存什么**：不持有任何玩法分区的实例（`QuestSaveData` / `LootSaveData` / `EncounterSaveData` 都是每次
`saves.Get<T>()` 现取现写）；不持久化对白已读记录（独立档案 `dialogue-read`，见下）；不持久化对白进行中 / 剧情
逐节点状态（DialogueSaveData 尚未接入）。NarrativeSaveData 已接稳定等待/结束边界，完整保存主/父阶段与标记；对白中途禁止写盘。

**谁负责**：各分区所有者自己 `Capture` / `Restore` 自己的数据，Session 只在稳定点统一喊「存」，不知道分区内部
字段；读档后各所有者自己订阅 [`SessionStartedEvent`](../../../../Assets/_Project/Scripts/Runtime/Session/SessionStartedEvent.cs) 重载运行时状态，Session 不枚举模块。

## 内部结构

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `GameSession` | 门面：槽位状态、新游戏 / 继续 / 删除 / 读槽摘要、`RequestSave` 待处理合并、`ITickable.Tick` 评估闸门并落盘、游玩时长累计 | 根作用域单例 + `IGameService` + `ITickable`（`GameSession.cs:36`） |
| `SaveGateRules` | 纯静态：`CanSave` 四条件、`Request`/`Take` 合并待处理请求 | `GameSession.Tick` 调（`SaveGateRules.cs:10`） |
| `ISessionStateSource` / `SessionStateAdapter` | 把 Quest / Dialogue / Monster / UIService 的具体依赖包成一个接口，供 `GameSession` 在 EditMode 里假实现测试 | `SessionInstaller` 注册适配器为该接口（`ISessionStateSource.cs`、`SessionStateAdapter.cs:18`） |
| `SaveTriggerBridge` | 入口点：把任务三事件 / 开箱 / 对白结束 / 场景切换完成 接成 `RequestSave`；离开玩法状态与退出游戏接成 `SaveNowAsync` | 根作用域入口点（`SaveTriggerBridge.cs:23`） |
| `SessionTitleRouter` | 入口点：标题「开始 / 继续 / 选择存档」三事件路由；回标题时按 `LatestSlot` 显示 / 隐藏「继续」 | 根作用域入口点（`SessionTitleRouter.cs:25`） |
| `SessionTitleRules` | 纯静态：`PickNewGameSlot`、`ShouldShowContinue` | `SessionTitleRouter` 调（`SessionTitleRules.cs:10`） |
| `SaveSlotsController` | 选槽面板的会话控制：开面板、点击分派、覆盖 / 删除二次确认、离开标题时收掉面板 | 根作用域单例，被 `SessionTitleRouter` 按具体类型注入（`SaveSlotsController.cs:24`） |
| `SaveSlotsView` | `UIView`（Panel）：三行槽位 + 删除 + 返回，只显示与抛事件 | `IUIService` 实例化（`SaveSlotsView.cs:26`） |
| `SessionConfig` | SO：槽数、保存提示文案与秒数、读档失败文案、进度占位文案、退出钩子超时 | `Data/Session/SessionConfig.asset`（`SessionConfig.cs:10`） |
| `SessionSaveData` | 槽位元数据分区（纯 DTO，Version 1） | `GameSession.SaveNowAsync` 落盘前更新（`SessionSaveData.cs:15`） |
| `SlotInfo` / `SlotState` / `SlotsMode` | 只读摘要 / 三态枚举 / 面板两种打开模式 | `GameSession.ReadSlotInfosAsync` 产出；`SaveSlotsView` / `SaveSlotsController` 按值消费 |
| `SessionStartedEvent` / `SaveCompletedEvent` | 事实事件（`readonly struct`） | `GameSession` 发布，各分区所有者订阅前者重载 |
| `SessionInstaller` | `GameplayInstaller`：注册以上全部（不 Resolve） | Boot 场景 `GameBootstrap` 物体，排在 `ExplorationInstaller` 之后（`SessionInstaller.cs:25`） |

## 触发点与稳定边界

| 触发点 | 事件 | 走法 |
| --- | --- | --- |
| 任务激活 / 完成 / 追踪变化 | `QuestActivatedEvent` / `QuestCompletedEvent` / `QuestTrackingChangedEvent` | `RequestSave`（合并，等闸门） |
| 开箱 | `CrateCollectedEvent` | `RequestSave` |
| 对白结束 | `DialogueService.OnEnded` | `RequestSave` |
| 叙事分区改变 | `NarrativeChangedEvent`（模块已安装时） | `RequestSave`，仍受稳定边界约束 |
| 场景切换完成（含新游戏首次落盘） | `GameStateChangedEvent.To == MonsterEncounterState` | `RequestSave` |
| 离开玩法状态 | `GameStateChangingEvent.From == MonsterEncounterState` | `SaveNowAsync` 直接存，不等闸门（捕获在 Exit 之前同步完成） |
| 退出游戏 | `GameQuit.RegisterBeforeQuit` 钩子 | `SaveNowAsync`，按 `SessionConfig.QuitHookTimeoutSeconds` 单独限时 |

闸门 `SaveGateRules.CanSave`：必须处于玩法状态，且没有对白（`DialogueService.IsRunning`）、没有面板 / 弹窗
（`UIService.TopView != null`）、没有待消费的战斗终局（`EncounterStep.PendingResult != None && !ResultConsumed`）。
四条件任一不满足就不落盘，请求继续挂着，多次 `RequestSave` 合并成一次（原因取最新一次）。所有触发都只在
`GameSession.CurrentSlot != 0` 时生效，标题页阶段不存。

Narrative 已安装时还必须满足 NarrativeStable。SaveNowAsync（包括退出与离场）也不能绕过对白/Narrative 的
完整恢复边界：不稳定时返回 false 并保留待保存请求，不改旧槽、不发成功通知。允许稳定面板内的退出保存，
但对白逐节点恢复仍未实现；不能把中途剧情只保存阶段 ID 后从头播放当作恢复支持。

## 新游戏 / 继续 / 选槽

- **新游戏**：`GameSession.NewGameAsync(slot)`（`GameSession.cs:106`）—— `saves.ResetAll()` → 槽位元数据置初值 →
  `state.PrepareRestore(false)`（清遭遇恢复准备）→ 发 `SessionStartedEvent(slot, true)` → `GoToAsync<MonsterEncounterState>`。
  首次落盘由「场景切换完成」触发。
- **继续**：`ContinueAsync(slot)`（`GameSession.cs:135`）—— 先 `ReadCandidateAsync` 只读候选校验（缺
  `SessionSaveData` 分区也算失败），失败则通知「存档不可用」、内存与 `CurrentSlot` 都不动、返回 false；成功才
  由 ISessionStateSource.ValidateCandidate 校验 Narrative 后，对刚读出的同一候选 Commit、PrepareRestore(true)、发事件、进场景。
  未知叙事内容/不支持恢复的阶段拒绝提交；旧档缺 Narrative 分区可视为未开始。这不提供全场景失败回滚。
- **选槽**：`SessionTitleRouter` 接管标题三事件——「开始」用第一个空槽直接开局，没有空槽开选槽面板
  `SlotsMode.NewGame`；「继续」读 `LatestSlot`，没有可用存档（`LatestSlot == 0`）时「继续」**隐藏**（不是置灰，
  `TitleView.SetContinueVisible(SessionTitleRules.ShouldShowContinue(...))`；回标题与选槽面板删掉最后一个存档后走同一判定）；「选择存档」开面板 `SlotsMode.Load`。`SaveSlotsController`
  负责面板会话（覆盖 / 删除走 `ConfirmView.WaitAsync`）；流程一旦离开标题（继续 / 新游戏成功），控制器订阅的
  `GameStateChangingEvent(From == TitleState)` 会自己把面板收掉，不会残留盖在玩法画面上（`SaveSlotsController.cs:148`）。
- **面板行的可选 / 可删规则**（`SaveSlotsView.IsRowSelectable` / `IsDeleteVisible`）：读档模式下只有
  `SlotState.Available` 的行能点，空槽 / 坏槽点不动；新游戏模式三态都能点（非空槽由控制器先弹确认覆盖）；
  「删除」按钮只在可读槽（`Available`）上显示，空槽没东西可删、坏槽走新游戏覆盖流程处理。
- 面板与确认弹窗的文案（「取消」「覆盖」「删除」「无法开始新游戏」「删除存档失败」等）是
  `SaveSlotsController` / `SaveSlotsView` 里的私有常量，**没有**进 `SessionConfig`；改文案直接改这两个类。

## 槽位元数据

`SessionSaveData`（`SessionSaveData.cs:15`）：`SavedAtUtcTicks`、`PlaytimeSeconds`、`ProgressText`、`SceneKey`，
Version 1。`ReadSlotInfosAsync` 只读候选，`candidate.Contains<SessionSaveData>()` 才算 `Available`，否则按
`Empty`（无文件）或 `Unavailable`（有文件但读不了/缺元数据）处理，三态见 `SlotState.cs`。进度描述由
`SessionStateAdapter.BuildProgressText()` 在保存时算：追踪中的任务标题 → 进行中第一条主线标题 → `SessionConfig.ProgressPlaceholder`。

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动或报错：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `SessionInstaller`，排在 `ExplorationInstaller` 之后 | 没挂：解析不到 `GameSession`，各触发点全部失效 |
| Config | `SessionInstaller.config` 拖 `Data/Session/SessionConfig.asset` | 没拖：记 Error 并用代码建的默认值顶上 |
| 预制体地址 | Addressables（UI 组）`SaveSlotsView` → `Prefabs/UI/SaveSlotsView.prefab`；`ConfirmView` → `Prefabs/UI/ConfirmView.prefab`。**地址等于类名** | `ui.OpenAsync<T>()` 找不到预制体 |
| `TitleView` 按钮 | 预制体 `Buttons` 组下 `ContinueButton` / `LoadButton` 拖到 `TitleView.continueButton` / `loadButton` | 两个字段可空，不接线时旧行为不变（按钮不显示回调） |

## 验证入口

- EditMode：`Tests/EditMode/Session/`——`GameSessionTests.cs`（新游戏 / 继续 / 坏文件 / 高版本槽 / 删除 / 合并请求）、
  `SaveGateRulesTests.cs`、`SessionSaveDataTests.cs`、`SessionTitleRulesTests.cs`、`SaveSlotsViewTests.cs`。
- Showcase：计划路径 `Tests/Showcase/Session/SessionShowcase.cs`（存 → 退 → 读一致；坏档 / 高版本槽显示「不可用」），
  由 PRP `save-session` 的 T6 产出，**本文档时点已落地**；产出后跑 `/verify-module Session`。

## 禁止事项

- **不要缓存分区实例**：`LoadAsync` / `ResetAll` / `Commit` 都整体替换分区字典，任何持有旧引用的服务会把改动
  写进一份没人读的对象。读档 / 新游戏后想拿最新分区，重新 `saves.Get<T>()`。
- 对白进行中（`DialogueService.IsRunning`）、任意面板打开中（`UIService.TopView != null`）都不保存，交给
  `SaveGateRules` 判断，不要在业务代码里绕过闸门直接调 `saves.SaveAsync`。
- 新增分区想参与「读档后重载」，订阅 `SessionStartedEvent` 自己重载，不要指望 Session 主动调用；Session 不认识
  任何玩法名词以外的重载逻辑。
- 不要把 `DialogueReadData`（已读记录）经 `saves.Get<>()` 放进槽位——它故意不实现 `ISaveData`，独立档案
  `dialogue-read` 由 `Game.Dialogue.DialogueReadStore` 自己读写，见 `dialogue-module-guide.md`。
