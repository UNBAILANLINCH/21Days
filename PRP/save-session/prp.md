# PRP: 存档会话与主界面选槽（save-session）

> 阶段 2 产物，2026-09-26。按 [`prd.md`](prd.md) 生成。先只出本文，架构点头后再出 `tasks.md`。
> 用户决定：不做节点式存档，全状态记录、即存即用，关键节点自动保存，主界面可选存档。

## 上下文快照

### 存档服务现状（`Core/Save/`）
- `ISaveService`：`Get<T>()` 按类型取分区（首次自动建默认）、`SaveAsync(slot)` 写**所有已 Get 过的分区**、`LoadAsync(slot)` 整体替换分区字典、`ReadCandidateAsync(slot)` 只读候选（损坏 / 高版本返回 null，不碰内存）、`Capture / Commit`、`Exists / Delete`、独立档案 `Read/WriteProfileAsync`（带版本信封）。
- 写盘：序列化在主线程、磁盘 IO 在线程池、`.tmp` + `Replace` 原子替换。槽文件 `<SaveRoot>/slot{N}.json`。
- **没有槽位元数据**（时间、进度、时长），信封里没位置；`SaveSnapshot.Contains<T>()` 可判候选里有没有某分区。
- `IClock.UtcNow` 注释已预留给存档时间戳；没有跨会话累计时长。

### 各分区接线现状（本 PRP 的核心缺口）
| 分区 | 所有者 | 现状 |
| --- | --- | --- |
| `QuestSaveData` | `QuestService` | 每次 `Flush` 写回内存分区；启动时 `rules.Restore(saves.Get<>)`；**没有「读档后重载」入口** |
| `LootSaveData` | `LootService` | 开箱即写分区；`LootSceneBinder.BindScene` 按分区恢复箱子开合 |
| `EncounterSaveData`（含 `PlayerSaveData`） | `EncounterStep.Capture(tick) / Restore` | **零接线**；`MonsterEncounterState.PrepareRestore(saved)` 已预留，`OnSceneReadyAsync` 里 `restore != null ? Restore : Begin`，但无人调用 |
| `DialogueSaveData` | `DialogueRules.Capture / Restore` | 零接线；本 PRP 不持久化对白中途状态（对白进行中不存） |
| `DialogueReadData`（已读） | `DialogueRules` | 只在内存，重启清空；文件头写明**不能实现 ISaveData**、要走独立档案 |
| `NarrativeSaveData` | `NarrativeRules` | 零接线，运行时也无人用；留给 Narrative 接线 PRP |
| `SettingsSaveData` | `SettingsService` | 独立档案，不进槽（已完成） |
| Inventory | 无分区，读 `LootService.Items` | 不用管 |

### 状态流与标题页
- `GameFlow.GoToAsync<T>` 串行；`Current` 只在目标 `EnterAsync` 成功后更新；`GameStateChangedEvent(From, To)` 在进入完成后发布。**没有「即将离开某状态」的事件**。
- `SceneGameState` 钩子 `OnSceneReadyAsync / OnSceneUnloadingAsync`；`MonsterEncounterState.SceneKey = "IsometricEncounter"`。
- 标题页（另一会话刚改）：`TitleView` 有 开始 / 设置 / 退出 三键与三个事件，`Buttons` 布局组可追加按钮；`TitleState` 把「开始」转成 `TitleStartClickedEvent`，去向由 `MonsterTitleRouter` 接管（`GoToAsync<MonsterEncounterState>`）。
- 退出：`GameQuit.Quit(reason)` 静态方法，没有「退出前」钩子。
- 确认弹窗：`ExplorationConfirmView`（`Game.IsometricExploration`）形状通用（正文 / 按钮文案 / `WaitAsync` 返回 bool），但是另一模块的私有 View，跨模块复用违反依赖约束。

### 必须规避的 pitfalls
- 两个会话共用一个工作区整份暂存会把对方改动一起提交（本 PRP 跨 TitleView / TitleState / Quest / Monster / Dialogue，提交按文件挑）。
- 测试自己把依赖装上了于是接线缺口全程不报（Capture / Restore 只被单测调过；验收必须从真实容器解析对象核对）。
- Showcase 回放中途别人保存 .cs 会吞掉测试（回放已锁程序集重载，但并行会话仍要约定）。
- MCP 改完场景没当场保存会被别的会话的 PlayMode 冲掉（改 TitleView 预制体后立刻保存）。
- 给共享 MonoBehaviour 加新序列化字段默认值会静默改别的场景（TitleView 新字段默认空、旧行为不变）。
- 未观察 UniTask 异常会砸中无关用例（写盘异步路径要 await 或显式观察）。

### 适用规则
- project-root：Session 是玩法模块（要显示任务标题等玩法名词），放 `Runtime/Session/`，命名空间 `Game.Session`；Core 只加**通用**能力（状态即将切换事件、退出前钩子、通用确认弹窗）；跨模块只走公开接口 / 事件。
- csharp-code：`[SerializeField] private` + 只读属性；Unity 对象判空只用 `== null`；每帧路径零分配。
- unity-assets：预制体经 MCP 改并当场保存；SO 类在模块目录、资产在 `Data/Session/`。
- unity-tests：规则先抽纯逻辑进 EditMode；回放走 `Game.Tests.Showcase`。
- model-routing：跨文件实现与接线 → opus；单点执行 → sonnet。

## 架构决策

### D1 模块归属
- 新建 `Assets/_Project/Scripts/Runtime/Session/`，命名空间 `Game.Session`，asmdef `Game.Runtime`。依赖方向：Session → Quest / Loot / Dialogue / Monster 的公开 API 与事件；Core 不认识 Session。
- 为什么不复用：没有任何现成类型承担「谁在什么时候把哪些分区写到哪个槽」；为什么不扩展 `JsonSaveService`：它是框架层，不能知道任务标题、遭遇、对白这些玩法名词。

### D2 存什么、谁负责
- 进槽的分区：`QuestSaveData`（已接）、`LootSaveData`（已接）、`EncounterSaveData`（本 PRP 接：保存时 `EncounterStep.Capture(clock.Tick)` 写入 `saves.Get<EncounterSaveData>()`；读档后 `MonsterEncounterState.PrepareRestore(saved)` 再进场景）、新增 `SessionSaveData`（槽位元数据：保存 UTC 时间、累计游玩秒数、进度描述、场景键、版本）。
- 不进槽：`SettingsSaveData`（已是独立档案）、`DialogueReadData`（本 PRP 接成独立档案 `dialogue-read`：Dialogue 模块自己在启动时读入、对白结束时写出，不经 Session）、`DialogueSaveData` 与 `NarrativeSaveData`（本期不持久化：对白进行中不存，剧情接线 PRP 再接）。
- 进度描述在保存时由 Session 从 `QuestService` 取当前追踪或最近激活的主线标题拼成字符串；剧情接线后改章节名（只改这一处）。

### D3 稳定边界与合并（纯规则 `SaveGateRules`，EditMode 可测）
- 不保存的条件：对白进行中（`DialogueService.IsRunning`）、有 Panel / Popup 开着（`UIService.TopView != null`，与 `UICancelRouter` 同样按具体类型注入）、当前流程状态不是玩法状态或正在切换、战斗终局待消费（`EncounterStep.PendingResult != None`）。
- 请求进入待处理标志；`SessionAutoSaver : ITickable` 只在有待处理请求时每帧评估闸门（几个布尔比较，零分配），闸门一开就写一次，多次请求合并成一次，原因记为最后一次的原因。
- 保存本身：同步把各所有者的状态捕获进分区（Encounter 捕获、Session 元数据更新），然后 `saves.SaveAsync(slot)` 异步落盘；落盘完成用 `INotificationService.Show("已保存", seconds: 1)`（同标题合并，不刷屏）。

### D4 触发点（`SaveTriggerBridge : IStartable`）
- 任务：`QuestActivatedEvent / QuestCompletedEvent / QuestTrackingChangedEvent`。
- 开箱：`CrateCollectedEvent`。
- 对白结束：`DialogueService.OnEnded`。
- 场景切换完成：`GameStateChangedEvent.To == MonsterEncounterState`（新游戏第一次落盘也在这里）。
- 离开玩法状态：**Core 新增 `GameStateChangingEvent(From, To)`**，`GameFlow` 在调用前一状态 `ExitAsync` 之前发布；Session 收到 `From == MonsterEncounterState` 时同步捕获并写盘（捕获同步完成后 Exit 才跑，落盘异步）。
- 退出游戏：**Core `GameQuit` 加退出前钩子**（`RegisterBeforeQuit(Func<UniTask>)`，`Quit` 改为先 await 全部钩子再退出）；Session 注册钩子做最后一次保存。暂停菜单与标题页的退出按钮不用改。
- 以上都只在 `CurrentSlot != 0` 时生效；标题页阶段不存。

### D5 新游戏 / 继续 / 选槽
- `GameSession`（IGameService，Singleton）：`CurrentSlot`、`NewGameAsync(slot)`、`ContinueAsync(slot)`、`ReadSlotInfosAsync()`（对每个槽 `ReadCandidateAsync`，`Contains<SessionSaveData>` 才算可用，否则「空」或「不可用」）、`DeleteSlotAsync(slot)`。
- 新游戏：把内存分区重置为默认（优先 `Commit` 一个空 `SaveSnapshot`；若其构造不可见则在 `ISaveService` 加 `ResetAll()`），发布 `SessionStartedEvent(slot, isNewGame)`，`MonsterEncounterState.ClearPreparedRestore()`，`GoToAsync<MonsterEncounterState>()`；进场景后的「场景切换完成」触发第一次落盘。
- 继续：`LoadAsync(slot)` 失败则留在标题并通知「存档不可用」；成功后发布 `SessionStartedEvent(slot, false)`，`PrepareRestore(saves.Get<EncounterSaveData>())`（`Validate` 失败则不 PrepareRestore、落出生点并记警告），再 `GoToAsync<MonsterEncounterState>()`。
- `SessionStartedEvent` 由各分区所有者订阅重载自己的运行时状态：**Quest 模块新增 `QuestService.ReloadFromSave()`**（`rules.Restore(saves.Get<>)` + `ActivateAvailable` + `Flush`），Loot 不需要（读分区活值，场景绑定时恢复箱子），Encounter 走 PrepareRestore。为什么用事件不用直接调用：所有者各自负责重载，Session 不枚举模块。
- 标题路由：`MonsterTitleRouter` 退役（从 `MonsterInstaller` 摘除并 `git rm`），由 `SessionTitleRouter : IStartable` 订阅 `TitleStartClickedEvent`（新游戏：有空槽直接用第一个空槽，否则开选槽面板要求覆盖确认）、**Core 新增** `TitleContinueClickedEvent`（继续最近槽）、`TitleLoadClickedEvent`（开选槽面板）。
- `TitleView` 追加 `continueButton`、`loadButton` 与两个事件（预制体在 `Buttons` 布局组复制 `StartButton`，节点名 `ContinueButton` / `LoadButton`；`StartButton` 与类名 / 地址不动）；`TitleState` 转发两个新事件；「继续」在没有可用槽时禁用（Session 路由在 `GameStateChangedEvent.To == TitleState` 后 `ui.Get<TitleView>()` 设置）。
- `SaveSlotsView : UIView`（Session 模块，Panel，`CloseOnCancel` true，`defaultSelected` 返回）：三行槽位（时间 / 进度描述 / 时长，空槽显示「新游戏」，坏槽显示「不可用」且不可选）、每行「删除」、底部「返回」；两种模式（读档 / 新游戏）由 `Bind` 参数决定，只抛事件。
- 二次确认：**Core 新增通用 `ConfirmView`**（Popup，`OnOpenAsync(arg)` 接正文，`SetButtonLabels`，`WaitAsync` 返回 bool，与 `ExplorationConfirmView` 同构）。为什么不复用 `ExplorationConfirmView`：它是探索模块私有 View，跨模块引用违反依赖约束；为什么不把它提升到 Core：那个文件属于正在工作的另一会话，本 PRP 不动它，留一条后续任务把两处都换成 Core 版。

### D6 游玩时长
- `GameSession` 作为 ITickable 在流程状态为玩法状态时累加 `IClock.UnscaledDeltaTime` 到 `SessionSaveData.PlaytimeSeconds`（暂停菜单打开时世界暂停但仍计时，符合「游玩时长」直觉；对白与面板期间同样计）。

### D7 数据形态与暴露面
- `SessionConfig` SO（`Data/Session/SessionConfig.asset`）：槽数（默认 3）、保存提示文案与秒数、进度描述格式、无存档时的进度占位文案。
- 无 Inspector 可调的运行时状态；所有 View 只 `[SerializeField] private` 控件引用。

### D8 场景 / 预制体改动（全部经 MCP，改完当场保存）
- `Prefabs/UI/TitleView.prefab`：`Buttons` 下追加 `ContinueButton`、`LoadButton`（复制 `StartButton`），拖到新字段。
- 新建 `Prefabs/UI/SaveSlotsView.prefab`（地址同类名）、`Prefabs/UI/ConfirmView.prefab`（地址同类名）。
- `Boot.unity`：`GameBootstrap` 挂 `SessionInstaller`，排在最后（`ExplorationInstaller` 之后）；`MonsterInstaller` 不再注册标题路由（代码改动，场景组件不变）。
- `Data/Session/SessionConfig.asset` 新建并拖到 Installer。

### D9 Core 改动清单（都是通用能力，无玩法名词）
1. `Core/Events/GameStateChangingEvent.cs` + `GameFlow` 在 Exit 前发布 + 根作用域 broker。
2. `Core/Boot/GameQuit.cs`：退出前钩子表，`Quit` 先 await 钩子（超时 2 秒兜底）再退出。
3. `Core/Events/TitleContinueClickedEvent.cs`、`TitleLoadClickedEvent.cs` + broker；`TitleView` 两个按钮与事件；`TitleState` 转发。
4. `Core/UI/Views/ConfirmView.cs` + 预制体。
5. 视 `SaveSnapshot` 构造可见性决定是否加 `ISaveService.ResetAll()`（执行时二选一并写明）。

### D10 各模块改动清单
- Quest：`QuestService.ReloadFromSave()`；订阅 `SessionStartedEvent`（在 `QuestInstaller` 里注册的入口点或 Service 内部订阅）。
- Monster：`MonsterInstaller` 摘除 `MonsterTitleRouter` 注册；`MonsterTitleRouter.cs` 删除。
- Dialogue：`DialogueReadStore : IGameService`，启动读 `profile-dialogue-read` 进单例 `DialogueReadData`，`OnEnded` 后写出（节流：同一帧多次只写一次）。
- Loot：不改。

## 任务清单（文件级，有序）
见 `tasks.md`（本 PRP 点头后生成）。

## 验证清单
- [ ] Unity 控制台零编译错误。
- [ ] `project-lint` 零违规。
- [ ] EditMode：`SaveGateRules`（各不存条件、合并）、`SessionSaveData` 版本、槽位信息读取（临时目录 `JsonSaveService`：空 / 正常 / 损坏 / 高版本）、`SessionTitleRouter` 的「继续」启用判定、`GameQuit` 钩子顺序与超时、`GameStateChangingEvent` 发布时机、`DialogueReadStore` 档案往返、`QuestService.ReloadFromSave`。
- [ ] 从真实容器解析核对：`GameSession`、`SaveTriggerBridge`、`DialogueReadStore`、`SessionTitleRouter` 都在根作用域且 `MonsterTitleRouter` 已不在。
- [ ] Showcase `SessionShowcase`：新游戏 → 移动 → 接任务 → 开箱 → 对话 → 触发保存 → 回标题 → 继续 → 位置 / 任务 / 背包 / 箱子 / 已读一致；坏文件与高版本槽在选槽面板显示「不可用」。
- [ ] PRD 验收 A1–A9 逐条对应（A8 的 Profiler 数字回填 prd.md）。
- [ ] asmdef 依赖方向合规；Core 无玩法名词。
- [ ] 无 public 字段；每帧路径无 Find / GetComponent / Log / 分配。
- [ ] 三件套 `ai-docs/docs/modules/session/`、`modules.json`、catalog 登记；`architecture.md` 5.5 / 5.7 补状态即将切换事件、退出钩子、存档会话契约。

## 风险 / 回滚
- 风险：四个并行会话在改 TitleView / TitleState（标题页会话）、Quest 内容类（任务编辑器）、Dialogue 控制器与 Boot（演出管线）、Exploration（探索白盒）。本 PRP 要动 TitleView / TitleState / QuestService / DialogueInstaller / MonsterInstaller / Boot.unity，执行前逐个会话对一次；改前重新 Read。
- 风险：`LoadAsync` 整体替换分区字典后，凡缓存了分区实例的服务会拿到旧对象。现有 Quest / Loot 都是每次 `Get` 取，`SessionStartedEvent` 让各所有者重载；新代码禁止缓存分区实例（写进 guide）。
- 风险：`GameQuit` 改为先 await 钩子，编辑器退出 Play 与真机退出路径都要实测一次。
- 风险：存档格式变动（新增 Session 分区、Encounter 分区首次入槽），旧槽（今天之前没有）不存在兼容问题。
- 回滚：改动仅在工作区，`git checkout -- <路径>`；MCP 建的对象与预制体按清单删回；Boot 上的 Installer 用 MCP 摘掉。
