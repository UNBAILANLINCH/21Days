---
type: module-guide
module: dialogue
layer: runtime
maturity: stable
---

# Dialogue 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Dialogue/` 之前读这份。对外怎么调看
> [`dialogue-external-api.md`](dialogue-external-api.md)，要加东西看 [`dialogue-extension-guide.md`](dialogue-extension-guide.md)。
> 设计定稿与执行中的修订见 [`PRP/dialogue-system/prp.md`](../../../../PRP/dialogue-system/prp.md) 第 7 节（一期）、第 8 节（二期：范围交互、跳过确认、选项图标、无树气泡）；与源码不符时以源码为准。

## 职责边界

**做**：按对话树编号拉起一段对白并跑完——查内容、暂停世界、关 Gameplay 输入图、打字机展示台词、
头像显隐（同一头像位，只显示当前说话者）、条件选项（右侧竖排胶囊 + 图标）、三连点补全 / 倍速 / 自动 / 跳过（先弹确认）、历史面板，
结束时恢复现场、广播事件、返回出口。
场景侧：范围交互焦点（离玩家最近的可交互 NPC，交互键 E / F / 手柄 A 或底部居中交互提示「[E] 对话 · 名字」触发）、NPC 头顶「…/!」标记与名字、
无对话树 NPC 的头顶常驻台词气泡（不暂停世界）。也可点击 NPC 或代码调用拉起。
沉浸模式（Core `IHudVisibility` / `HudVisibilityChangedEvent`）：`DialogueSceneBinder` 订阅事件后对已登记物体调 `DialogueInteractable.SetHiddenByHud`，头顶标记、名字、台词气泡随之隐藏、点击 NPC 不响应；`DialogueInteractionFocus` 沉浸时不选焦点（`Current` 为 null，交互键 / HUD 点击无效），交互提示 HUD 由 UIService 一并隐藏。代码直接调 `Interact()` 不受影响。

**不做**（都归 Narrative 或后续工作）：

| 不做 | 归属 |
| --- | --- |
| 遭遇触发（什么时候、对谁自动开对白）、剧情阶段推进、选项结果驱动的玩法行为 | Narrative 后续接线（[`follow-up-integration.md`](../../../../PRP/narrative-dialogue/follow-up-integration.md) 第 4 节） |
| 存读档 UI、槽位、候选读取事务 | Narrative / GameSession 后续（同上第 6 节）；已读档案持久化已由本模块 `DialogueReadStore` 自己做（独立档案，不经 Session） |
| 条件事实的真实来源（玩家是否潜行、剧情标记……） | Narrative 接线后替换 `IDialogueConditionSource` |

> `follow-up-integration.md` 第 3 节写于本模块落地前，其中「三槽立绘」「`advance` 按钮」「已读快进 Toggle」
> 「调用方自己 `rules.Start` + `PresentAsync(Func<EncounterContext>)`」「`SetPaused(dialogueController, BlocksWorld)`」
> 均已被本次实现取代；接 Narrative 时按本文与 external-api 走，只沿用它第 4、6 节的剧情 / 存档部分。

## 运行时类分工

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `DialogueRules` | **纯 C#** 推进规则：阶段机（`Preparing → Typing → AwaitAdvance / AwaitChoice → Completed`，另有 `Closed`）、选项条件复验、历史、已读键、`Skip` 同步快进、`Capture / Restore` | 根作用域单例；Service 调 `Start / Cancel`，Controller 调其余 |
| `DialoguePlaybackSettings` | 表现参数的**校验后快照**（`readonly struct`），非法值构造时抛 `ArgumentException` | `DialogueConfig.ToPlaybackSettings()` 产出 |
| `DialoguePlaybackPolicy` | **纯 C#** 表现策略：点击何时算补全 / 推进、倍速档、自动计时、跳过标记；时间由调用方传入 | Service 惰性建一个，每段对白 `ResetForDialogue`（`DialogueService.cs:138`） |
| `DialogueMotionSettings` | 面板动效参数的校验后快照（`readonly struct`，不引 UnityEngine；构造函数为 `(portraitSlideDistance, portraitSlideSeconds, portraitCrossfadeSeconds, nameTagPunchSeconds, nameTagPunchScale)`，不含压暗参数），挂在 `DialoguePlaybackSettings.Motion` 上 | Controller 打开面板后 `view.SetMotion(policy.Settings.Motion)` |
| `Game.Core.UI.TypingCadence`（`Assets/_Project/Scripts/Core/UI/TypingCadence.cs`，已下沉到 Core，供 Performance 字幕逐字复用） | **纯 C#** 打字节奏：本帧字符预算 → 新显示字数，标点后先耗停顿预算 | Controller 每段对白按 `policy.Settings` 建一个，每句 `Reset`、每帧 `Advance` |
| `DialoguePortraitSlot` | View 私用的普通类（`internal`，非组件）：一个头像槽的入场 / 退场 / 交叉淡化状态机（无压暗、无缩放），持有该槽的补间句柄与运行时残影 | `DialogueView` 每槽一个，首次打开时建 |
| `DialogueCatalog` | Luban 表 → `DialogueContent` / `DialogueCharacter` 的翻译与缓存，首次访问才读表 | 根作用域单例 |
| `DialogueContent` | 与表无关的内容模型（节点、选项、立绘指令），构造时校验跳转 / 槽位 / 出口 | Catalog 产出，测试可直接 new |
| `DialogueCharacter` | 角色 → 表情 → Addressables 地址的只读索引 | Catalog 产出 |
| `DialogueController` | 表现驱动：每帧打字、立绘与选项图标的加载与释放、选项刷新、历史面板、跳过确认、把 View 事件翻成意图 | 根作用域单例；只被 Service 调 |
| （插播）`IPerformanceService`（`Game.Performance`，可空） | 节点带 `PerformanceId` 时，Controller 摆台词前先 `await PlayAsync`；`Performing` 期间语义同覆盖中（`DialogueController.cs:151`、`325`） | `DialogueInstaller` 用 `resolver.TryResolve` 注入，Boot 没挂 `PerformanceInstaller` 时为 null |
| `DialogueView` | `UIView`（Popup 层）：显示文字 / 立绘 / 选项（含图标位）/ 控件，只抛事件，不注入服务 | `IUIService` 按地址实例化 |
| `DialogueSkipConfirmView` | `UIView`（Popup 层）：「是否跳过剧情？」确认 / 取消，只抛 `OnConfirm / OnCancel` | Controller 在点跳过时开关 |
| `Game.Core.UI.Views.TranscriptView`（Core，原 `DialogueHistoryView` 下沉重命名，见下「为什么这样设计」） | `UIView`（**Top 层**：演出会整层藏 Popup 层，只有 Top 层能压在演出与对白之上；不进 UI 栈、不改 EventSystem 选中，Esc 与关闭都由调用方处理）：`Show(lines, truncated)` + 纯函数 `Format` 显示记录，格式来自静态 `DialogueController.BuildTranscript`（选择项说话者写「选择」） | Controller 按需开关（`history` 字段） |
| `DialogueService` | **对外入口**：重入保护、世界暂停、输入图切换、藏探索 Hud 层（按进来前的值恢复）、事件广播、结果返回 | 根作用域单例 |
| `IDialogueConditionSource` / `DefaultDialogueConditionSource` | 选项条件的事实快照来源；默认实现是占位 | 根作用域单例，Service 传给 Controller |
| `DialogueInteractable` | 场景组件：对话树编号（`0` = 无树）+ 显示名 + 常驻台词 + 交互半径 + 点击入口；`Focused` 由焦点系统写 | 场景物体；`DialogueSceneBinder` 注入 Service 与场景 Actor |
| `DialogueSceneBinder` | 入口点：启动时与每次 `sceneLoaded` 扫场景，有树的 `Bind`、全部登记进 `Bound`，找玩家标记 `Actor`；`sceneUnloaded` 清已销毁项 | 根作用域入口点（`AsSelf`，焦点系统注入它） |
| `DialogueKeyboardInput` | `ITickable` 入口点：对白进行中每帧读 `Dialogue` 动作图 + `UI/Cancel`，经静态纯函数 `Map(key, state)` 翻成处理动作，交给 `DialogueController.HandleKey` 调与点击同一套处理函数；首次拿到动作集时把按钮键位提示交给 Controller | 根作用域入口点（`DialogueInstaller`） |
| `DialogueInteractionActor` | 玩家根上的空标记：测距原点 | 场景玩家物体 |
| `DialogueInteractionFocus` | `ITickable` 入口点：每帧在 `Bound` 里选最近且 `CanInteract` 的为焦点（`SelectNearest` 静态纯函数，无分配）；交互键（`Gameplay.Interact`）/ HUD 点击 → `Current.Interact()`；驱动 HUD 显隐，打开后给 HUD 赋一次键位显示串 | 根作用域入口点（`AsSelf`） |
| `DialogueInteractHudView` | `UIView`（Hud 层）：底部居中 48 px 高胶囊「[键位] 对话 · NPC 名」，常驻打开，显隐只切 `root`；整条是按钮，抛 `OnInteract`；拼字符串是静态纯函数 `FormatKeyText` / `FormatLabel` | 焦点系统在 `BootCompletedEvent` 后打开 |
| `DialogueInteractableMarker` | NPC 头顶三态标记：不可交互全隐 / 可交互灰「…」/ 焦点白「!」+ 名字；气泡显示中让位；被外部标记接管（`DialogueInteractable.MarkerOverridden`，如任务目标标记）时「…/!」图标隐藏、名字照常；`TryGetIconAnchor(out Vector3)` 公开图标世界锚点（焦点图优先、其次可交互图，都没配返回 false；每帧可调、无分配），供接管方摆到同一位置 | 场景 NPC（取代已删除的 `DialogueInteractableHint`） |
| `DialogueSpeechBubble` | 世界空间气泡：订阅 `OnBubbleRequested`，逐字 → ▼ → 停留 `holdSeconds` → 淡出，全程 unscaled；高度随正文行数自适应 | 预制体 `Prefabs/World/DialogueSpeechBubble.prefab` 根上，实例挂 NPC 子物体 |
| `DialogueInstaller` | `GameplayInstaller`：注册以上全部 | Boot 场景 `GameBootstrap` 物体 |
| `DialogueConfig` | SO：打字速度、历史上限、倍速档、三连点、自动间隔 | `Data/Dialogue/DialogueConfig.asset` |
| `DialogueSaveData` | `ISaveData`：对白稳定恢复点（节点、阶段、解析后文本、历史、立绘） | `rules.Capture()` 产出；**尚未接存档** |
| `DialogueReadData` | 跨槽位已读键集合，**故意不实现 `ISaveData`**（加载旧槽位不能让已读倒退） | 根作用域单例（`DialogueInstaller.cs:41`），由 `DialogueReadStore` 原地填充 |
| `DialogueReadStore` / `DialogueReadProfile` | 已读记录的**独立档案 `dialogue-read`**（`profile-dialogue-read.json`，不进槽位）：`InitializeAsync` 读档案并把键**并集**灌进 `DialogueReadData` 同一实例；订阅 `DialogueService.OnEnded` 标记脏，下一帧合并成一次 `WriteProfileAsync`（同帧多次结束只写一次，写失败记 Error 留脏待下次）；`Dispose` 退订并补写（`Forget`，退出时可能来不及落盘）。`DialogueReadProfile` 是落盘 DTO（`ISaveData` v1，`List<string> Keys`） | `IGameService`，排在 `DialogueService` 之后（`DialogueInstaller.cs:69`） |
| `DialogueIntent` | View → Controller 的意图（`Advance / Choose`），带 `Generation / Visit` 身份防迟到回调 | 现 new 现用 |
| `DialogueResult` / `DialogueStartedEvent` / `DialogueChoiceSelectedEvent` / `DialogueEndedEvent` | 返回值与三个广播载荷（`readonly struct`，一文件一个） | Service 产出 |

Core 侧配套：`Game.Core.Boot.FallbackCamera`（`Assets/_Project/Scripts/Core/Boot/FallbackCamera.cs:18`）挂 Boot 的 `Main Camera`，
`sceneLoaded / sceneUnloaded` 时有别的启用相机就禁用自己、没有就恢复。不属于本模块，但没有它玩家看到的是 Boot 灰底、点击射线却走场景相机。

## 数据流

```text
焦点：DialogueInteractionFocus.Tick（每帧）
        Actor 为空或 service.IsRunning → 无焦点；否则 SelectNearest(Actor, binder.Bound)
        焦点变化 → 旧.Focused=false / 新.Focused=true → HUD Show/Hide → 埋 focus_changed → OnFocusChanged
        Gameplay.Interact 按下（E / F / 手柄 A）/ HUD.OnInteract → Current.Interact()
触发：DialogueInteractable.Interact() / OnPointerClick
        ├─ 无树（dialogueId==0）且有台词 → OnBubbleRequested(下一句，循环) → DialogueSpeechBubble.Show
        │     （不暂停世界、不切输入图、不开面板；到此为止）
        └─ 有树且已绑定 ─────────────────────────┐
      其他模块直接 await DialogueService.PlayAsync(id) ───┤
                                                           ▼
DialogueService.PlayAsync
  ├─ DialogueCatalog.TryGet(id)          （首次访问：IConfigService.Tables → 翻译 → 缓存）
  ├─ DialogueRules.Start(content)        + policy.ResetForDialogue()
  ├─ IWorldPauseService.Acquire(this)    （timeScale=0 + 逻辑 tick 停）
  ├─ IInputService.DisableMap(Gameplay) + EnableMap(Dialogue)  （UI 图不动，EventSystem 靠它）
  ├─ 读 IUIService.IsLayerVisible(Hud) 记进来前的值，再 SetLayerVisible(Hud, false)（藏探索 HUD，与演出观感一致；不动 Popup 层——对白框就在 Popup 层）
  ├─ OnStarted → DialogueController.PresentAsync(conditions, "dialogue:<id>", policy, ct)
  │       每帧：Skipping? → rules.Skip ； Visit 变 → PrepareAsync（SetLine + 立绘）→ rules.Ready
  │       Visit 变 → 节点有 PerformanceId（且非跳过中、Preparing）→ Performing=true → await IPerformanceService.PlayAsync → 复位 → 比对 generation / visit → PrepareAsync
  │             Typing → rules.RevealTo(cps × unscaledΔ) ； policy.TickAuto → Submit(Advance)
  │       View.OnTap → policy.RegisterTap → Submit(Advance) ； View.OnIntent → Submit(Choose)
  │       DialogueKeyboardInput.Tick → HandleKey → Map → 同一套处理函数（Tap / 按钮 / Submit(Choose)）
  │       View.OnSkip → 开 DialogueSkipConfirmView（覆盖中）→ 确认 policy.BeginSkip / 取消 关闭恢复
  │       选项行：RefreshChoices → ResolveChoiceIcon(IconKey) 异步加载 → View.SetChoiceIcon 回填
  │       Submit → rules.Apply(intent, conditions.Snapshot(target))
  │       收尾（finally）：先退订全部面板事件，只关真正打开过的面板（跳过确认 / 历史 / 主面板），再释放立绘与图标句柄
  └─ finally：按读到的值恢复 Hud 层 → 关 Dialogue 图、恢复 Gameplay 图（仅当进来前是开的）→ 释放暂停 → 非正常结束则 rules.Cancel → OnEnded
返回 DialogueResult(id, outcome, skipped)

内容：Tables/Data/dialogue/*.json ─Luban→ cfg.dialogue.* + dialogue_tbdialogue*.bytes
      ─IConfigService.Tables→ DialogueCatalog ─翻译→ DialogueContent / DialogueCharacter
```

`DialogueRules.OnChoiceSelected` 在跳转**之后**才触发，Service 从刚写入的那条选择历史取节点 id 再转发
（`DialogueService.cs:153`）——改规则里事件的触发时机要同步改这里。

## 依赖方向

`Game.Dialogue`（在 `Game.Runtime` asmdef 内）只向下依赖：

| 依赖 | 用来做什么 |
| --- | --- |
| `Game.Core.UI`（`IUIService` / `UIView`） | 开关 `DialogueView` / `TranscriptView` / `DialogueSkipConfirmView` / `DialogueInteractHudView`；`IsLayerVisible` / `SetLayerVisible` 藏 / 恢复探索 Hud 层 |
| `Game.Core.Assets`（`IAssetService` / `AssetHandle<Sprite>`） | 立绘与选项图标加载与释放 |
| `Game.Core.Input`（`IInputService`） | 关 / 恢复 Gameplay 动作图；焦点系统读 `Gameplay.Interact`（按下 + 第一条键盘绑定的显示串） |
| `Game.Core.Events`（`BootCompletedEvent`，经 MessagePipe `ISubscriber`） | 焦点系统等启动完成再开 HUD |
| LitMotion | 气泡淡出（`UpdateIgnoreTimeScale`） |
| `Game.Core.Timing`（`IWorldPauseService` / `IClock`） | 世界暂停；unscaled 时间驱动打字与自动 |
| `Game.Core.Telemetry` | 模块名 `dialogue` 的埋点 |
| `Game.Core.Config`（`IConfigService`）+ 生成物 `cfg.dialogue.*` | 内容表 |
| `Game.Core.Save`（`ISaveData`） | `DialogueSaveData` 的形状 |
| `Game.Narrative`（**只用** `EncounterContext` / `NarrativeCondition`） | 选项条件的值类型与匹配 |
| `Game.Performance`（**只用** `IPerformanceService`，可为 null） | 对白节点前插播演出；Performance 不反向引用 Dialogue |

Core 不认识本模块；`IWorldPauseService` 里没有对话名词。Narrative 不反向引用 Dialogue。
`CameraBillboard`（IsometricExploration）只在场景里挂到标记 / 气泡子物体上，本模块代码不引用它。

## 世界时停与输入图：责任归属

| 资源 | 谁持有 | 位置 | 为什么 |
| --- | --- | --- | --- |
| 世界暂停令牌 | `DialogueService` | `DialogueService.cs:92` | 会话级资源；放 Controller 会让表现层依赖暂停服务 |
| Dialogue 动作图 | `DialogueService` | 与关 Gameplay 同处 `EnableMap(DialogueService.InputMap)`（常量在本模块，Core 不带玩法名词），内层 `finally` 先 `DisableMap(InputMap)` 再恢复 Gameplay | 对白键位只在对白期间有效；启动时不启用（`InputService.InitializeAsync` 只开 Gameplay）；`Actions == null` 时同样跳过 |
| Gameplay 动作图 | `DialogueService` | `DialogueService.cs:102`–`121` | 只恢复进来前的状态：调用方本来关着（如过场）就不擅自打开；`input.Actions == null`（输入服务未初始化 / EditMode）时记录、禁用、恢复一并跳过 |
| 探索 Hud 层 | `DialogueService` | `DialogueService.cs:117`–`134`：开面板前读 `IsLayerVisible(Hud)`、`SetLayerVisible(Hud, false)`，内层 `finally` 按读到的值恢复 | 与演出观感一致；只藏 Hud，不动 Popup（对白框在 Popup 层）；嵌套（演出里插播对白）时不会把外层已经藏掉的层重新亮出来 |
| 重入保护 | `DialogueService` | `DialogueService.cs:70`–`74` | 同一时刻只允许一段对白 |
| 历史面板开关、`Suspended` | `DialogueController` | `DialogueController.cs:79` | 纯表现；挂起期间不推进不收输入（留给存档等外部挂起） |
| 跳过确认弹窗、「覆盖中」 | `DialogueController` | `DialogueController.cs:148`–`171`、`248` | `Overlaid = historyOpen \|\| skipConfirmOpen`：覆盖中不打字、不自动、不跳过，主面板输入关 |
| 交互焦点、交互键 | `DialogueInteractionFocus` | `DialogueInteractionFocus.cs:68`–`82` | 对白进行中焦点清空；Gameplay 图此时已关，不会重入 |

Controller、View、Rules、Focus、键位入口、气泡一律不碰 `Time.timeScale` / 输入图（Focus 与 `DialogueKeyboardInput` 只读动作，不开关图）。

## 键盘 / 手柄操作

对白期间 `DialogueService` 开 `Dialogue` 动作图（`GameInput.inputactions`），`DialogueKeyboardInput.Tick` 读动作 →
`DialogueController.HandleKey` → 静态 `DialogueKeyboardInput.Map(key, KeyState)` → 调**与点击同一个**处理函数（`Tap` / `ToggleAuto` / `CycleSpeed` / `RequestSkip` / `RequestHistory` / `DismissHistory` / `CancelSkip` / `Submit(Choose)`）。

| 动作 | 键盘 | 手柄 | 等价于 | 何时有效 |
| --- | --- | --- | --- | --- |
| `Advance` | 空格 / 回车 / 小键盘回车 | A（South） | 点对话框 `tapArea`（三连点补全同样计数） | 主面板、已就绪、**非选项期** |
| `Auto` | A | Y（North） | 点「自动」 | 主面板 |
| `Speed` | S | X（West） | 点倍速 | 主面板 |
| `Skip` | 左 / 右 Ctrl | RB | 点「跳过」（先弹确认） | 主面板 |
| `History` | H | LB | 点「LOG」；历史开着时再按 = 关闭 | 主面板 / 历史面板 |
| `Choice1`–`Choice4` | 数字 1–4 / 小键盘 1–4 | — | 点显示中的第 N 行选项 | 选项期；越界或该行不可用（置灰）忽略；隐藏的不可用选项不占行号 |
| `UI/Cancel` | Esc | B（UI 图默认） | 历史开着 = 关闭；跳过确认开着 = 点取消 | 只在这两个弹窗；**主面板上什么都不做（Esc 不关对白）** |

- 弹窗优先：跳过确认开着只认 `Cancel`；历史开着只认 `History` / `Cancel`；其余键全部忽略（`DialogueKeyboardInput.Map`）。
- 选项期 `Advance` 不推进：回车同时是 UI `Submit`，选项出现后 `DialogueView.SelectChoice` 在**下一帧**把 EventSystem 选中设到第一个可用项（资格刷新重建行时按选项 id 保留原选中），回车 / 手柄 A 由 UI Submit 点中选中项、方向键导航。延后一帧是为了让「补全出选项的那次回车」不被 UI Submit 当成立刻选第一项。
- 跳过确认弹窗打开即选中「取消」（预制体 `defaultSelected` = `CancelButton`，由 UIService 打开后设选中），误按回车不会跳过。历史 / 跳过弹窗关闭后（UIService 把选中清空之后）`SelectChoice` 下一帧把选中还给选项。
- 三个对白面板都重写 `CloseOnCancel => false`：通用 `UICancelRouter` 不能越过 Controller 直接关它们（主面板关了流程卡死；弹窗被直接关会让 Controller 的覆盖中标记与事件退订对不上）。Esc 统一走 `DialogueKeyboardInput` → Controller 的正常收尾。
- 鼠标点过的控件按钮（含 `tapArea`）点完即取消 EventSystem 选中，否则之后按回车会被 UI Submit 再点一次；`tapArea` 的导航设为 None，方向键不会落到透明点击区上。
- 按钮键位提示（如「自动 A」「x1 S」「跳过 Ctrl」「LOG H」）取该动作**第一条键盘绑定**的 `GetBindingDisplayString`（`DialogueKeyboardInput.KeyboardHint`；绑定没分控制方案组，按路径前缀 `<Keyboard>` 筛），跟随系统键盘布局；只在首次拿到动作集时取一次。键位显示在主标签还是单独的 Hint 小字由 `DialogueView` 对应按钮是否接了 `xxxHintLabel` 决定：接了 = 主标签只写文案（「跳过」「LOG」固定预制体文案不受影响）、键位单独显示在 Hint；没接 = 键位拼进主标签（`DialogueView.WithHint`）。选项文字前缀「1.」「2.」在建行时拼一次。
- 读动作的行带 `// lint-ok`：对白表现层不进确定性模拟、不影响回放（同焦点系统读 `Gameplay.Confirm` 的例外）。

## 表现语义

| 行为 | 语义 | 落点 |
| --- | --- | --- |
| 打字 | 每帧预算 `CharactersPerSecond × 倍速 × UnscaledDeltaTime` 交给 `Game.Core.UI.TypingCadence.Advance` 换算显示字数，按 TMP 解析后的可见字符数计（富文本标签不计，`DialogueView.VisibleText` 给出解析后的字符序列） | `DialogueController.cs`（Typing 分支）；`Core/UI/TypingCadence.cs` |
| 标点停顿 | 打出 `punctuationChars` 里的字后停 `punctuationPauseSeconds`（按「秒 × 基础速度」个字符预算扣，所以倍速下同比缩短，与自动间隔语义一致）；连续标点只在最后一个后停一次；句末标点不停；三连点补全 / 跳过直接 `RevealTo` 全量，不经节奏 | `Core/UI/TypingCadence.cs` |
| 三连点补全 | Typing 中**相邻两次**点击间隔 ≤ `tapWindowSeconds` 才累计，满 `revealTapCount` 次补全；超窗从 1 重计 | `DialoguePlaybackPolicy.cs:57` |
| 推进 | AwaitAdvance 单点即推进；Reveal / Advance 都提交 `Advance` 意图，规则自己区分补全与推进 | `DialogueRules.cs:76`；`DialogueController.cs:238` |
| 倍速 | `speedSteps` 循环（默认 x1/x2/x4），同时缩放打字速度与自动间隔；每段对白回 0 档 | `DialoguePlaybackPolicy.cs:38` |
| 自动 | AwaitAdvance 停留满 `autoAdvanceSeconds / 倍速` 自动推进；换节点清零 | `DialoguePlaybackPolicy.cs:78` |
| 跳过 | 点跳过先开确认弹窗，**确认**后才开始；一经开始持续到本段结束；同步快进所有台词（记历史、写已读），**停在选项**；选完后继续跳到下一个选项或结束；`DialogueResult.Skipped = true`。取消则关弹窗照常继续 | `DialogueRules.cs:105`；`DialogueController.cs:117`、`398` |
| 头像 | 按说话者站位分左右两槽：槽 0 = 左侧头像位、从左滑入淡入；槽 1 = 右侧头像位、从右滑入淡入（角色表 `side: Left/Right` 决定用哪槽，即头像出现在面板左侧还是右侧）。同一时刻只显示说话者那一槽：`speaking == false`（非说话者、旁白）视作该槽收起，等同传 null。Controller 先把新节点头像**全部加载完**再逐槽 `SetPortrait`（加载期间旧图照常显示），由 View 按变化选动效：空 → 有 = 从本槽方向滑入 `portraitSlideDistance` + 淡入（`portraitSlideSeconds`，OutCubic）；有 → 空 = 反向滑出 + 淡出（InCubic）后隐藏；同槽换图 = 交叉淡化（`portraitCrossfadeSeconds`，运行时残影 Image `<槽名>Ghost` 显示旧图淡出）；**没有压暗、没有缩放**；头像框 `avatarFrame` 只跟左槽（槽 0）显隐、`avatarFrameRight` 只跟右槽（槽 1）显隐，各自独立 | `DialoguePortraitSlot.cs`；`DialogueController.cs`（`PrepareAsync`） |
| 立绘动效时序 | 全部 `UpdateIgnoreTimeScale`；同槽再触发先 Cancel（入场中换表情先 `Complete` 入场）；面板 `OnDisable` 全部掐断并直接置终态、`OnOpenAsync` 清空两槽；存档恢复（此刻已是 Typing / AwaitAdvance）传 `instant: true` 直接置终态。上一节点的立绘句柄留到再下一次换节点 / 收尾才释放，保证退场与残影期间旧图有效 | `DialogueView.cs`（`OnDisable`、`OnOpenAsync`）；`DialogueController.cs`（`retiring`） |
| 名牌 | `SetLine` 里说话者名与上一句不同才 punch：缩放 `nameTagPunchScale` → 1 + 透明度 0 → 1，`nameTagPunchSeconds`；同名连续不动；打开面板时清空记忆，第一句必 punch。名牌即 `Speaker` TMP 本身 | `DialogueView.cs`（`PunchName`） |
| 对话框开合 | 预制体 `UIView.transition = SlideUp`（D4 的通用预设：从下方 40 px 滑入 + 淡入，关时反向），无额外代码 | `Prefabs/UI/DialogueView.prefab` |
| 表情缺失 | 回退角色默认表情并埋 Warn；默认图也失败则隐藏该槽并埋 Error，不中断对白 | `DialogueController.cs:302` |
| 选项 | 按 unscaled 时间每 0.25 s（`ChoiceRefreshInterval`）取一次条件快照刷新可用性，进入节点与提交后强制重算；不可用选项按 `hideWhenUnavailable` 隐藏或置灰并拼上原因；提交时规则再复验一次 | `DialogueController.cs:31`、`316`；`DialogueRules.cs:90` |
| 选项图标 | `Choice.IconKey` 空 = 无图标（隐藏 `Icon`）；非空时先无图显示、异步加载完经 `SetChoiceIcon` 按选项 id 回填；按地址在**当前节点**内缓存，换节点 / 收尾整体释放；加载失败埋 Warn 不重试 | `DialogueController.cs:347`–`384`；`DialogueView.cs:147` |
| 交互焦点 | 候选 = `Bound` 里激活、启用且 `CanInteract` 的；按到 Actor 的三维距离取最近 | `DialogueInteractionFocus.cs:80` |
| `CanInteract` | 在范围内、没有对白进行，且「有树已绑定」或「无树有台词」 | `DialogueInteractable.cs:59` |
| 范围 | Inspector `actor` 优先，否则场景 Actor（Binder 注入）；两者皆空或半径 ≤ 0 恒在范围；三维距离 | `DialogueInteractable.cs:62`–`74` |
| 头顶标记 | 焦点优先于可交互；气泡 `IsShowing` 时标记与名字全隐；名字只在焦点时显示；`MarkerOverridden` 时只隐「…/!」两张图、名字仍随焦点（判定在纯静态 `ResolveVisibility`） | `DialogueInteractableMarker.cs:53` |
| 气泡 | 逐字（TMP 可见字符）→ ▼ → 停留 `holdSeconds`（默认 4）→ 淡出 `fadeSeconds`；显示中再交互直接换句重来；unscaled。换句时设完文本即 `LayoutRebuilder.ForceRebuildLayoutImmediate` 强制重排，高度当帧跟上新句 | `DialogueSpeechBubble.cs:88`、`108`、`114`、`136` |

### 为什么这样设计（源码读不出来的部分）

- **表现策略做成纯 C# 可测**：点击节奏、倍速、自动计时如果塞进 `DialogueRules`，规则就得依赖表现时间；
  塞进 Controller（依赖 `IUIService` / TMP）又没法做 EditMode 测试。所以拆出 `DialoguePlaybackPolicy`，
  时间一律由调用方传入，`DialoguePlaybackPolicyTests` 直接钉住三连点窗口、倍速循环、自动计时。
- **`Time.timeScale` 只由 `IWorldPauseService` 持有**：timeScale 是全局单值，对话、暂停菜单各自改会互相覆盖；
  原先 `BlocksWorld` + 外部 `SimulationRunner.SetPaused` 的方案只停逻辑 tick、不停 timeScale，且要调用方记得接。
  现在收口到 Core 的引用计数服务，最后一个持有者释放时恢复**进来前**的 timeScale（不是置 1）。
  对应地，本模块所有表现都读 `IClock.UnscaledDeltaTime` / `UnscaledTime`，改成 scaled 会在对白里直接冻住。
- **已读快进被跳过取代，但已读记录保留**：旧版「已读快进 Toggle」逐句定时推进、依赖 `CanSkip`；新需求是
  一键跳过整段并停在选项，与读没读过无关，于是删了 `CanSkip` 与快进表现路径。`DialogueReadData` 与规则里的
  已读键写入保留（`DialogueRules.cs:223`），留给将来「只允许跳过已读」或已读标色。
- **跳过先于准备**（`DialogueController.cs:116`）：被跳过的句子不等 TMP 排版和立绘加载，规则同步快进；
  `Skip` 超过 4096 句视为内容成环直接抛（`DialogueRules.cs:13`），避免死循环。
- **事件用 C# `event` 不用 MessagePipe**：`GameplayInstaller.Install` 拿不到根作用域的 `MessagePipeOptions`，
  重复 `RegisterMessagePipe` 会冲突（`DialogueService.cs:4`）。二选一，不要再补一套 broker。
- **Catalog 与角色索引都惰性建**：`DialogueSceneBinder` 是入口点，容器构建期就会连带解析 Service → Controller，
  那时 `IConfigService` 还没初始化，构造时读表必抛（`DialogueController.cs:22`）。
- **播放策略惰性建**：非法 `DialogueConfig`（如空倍速表）在首次 `PlayAsync` 时以 `ArgumentException` 暴露，
  不让启动崩（`DialogueService.cs:137`）。
- **`Generation` / `Visit` 身份**：每次 `Start / Restore / Cancel` 递增 Generation、每进一个节点递增 Visit；
  意图与异步立绘加载完成后都要比对，防止上一句 / 上一段的迟到回调改到当前状态。选项图标另用 `choiceIconEpoch`，同理。
- **焦点系统直接读 `Actions.Gameplay.Interact`**（`DialogueInteractionFocus.cs:79`）：交互用独立动作（E / F / 手柄 A），不再复用 Confirm（回车 / 空格）——Confirm 是 `LiveInputSource` 采样的确定性输入位，两者分开后按回车不会误拉起对话。项目约定玩法不直接读输入、走确定性模拟，
  但「触发一段对话」不进逻辑帧、不影响回放，属于允许的例外（行尾 `lint-ok` 注释说明）。别把它当先例用在模拟逻辑里。
- **HUD 等 `BootCompletedEvent` 再开**（`DialogueInteractionFocus.cs:60`）：入口点 `Start` 在容器构建完就跑，早于 `UIService` 初始化，
  那时 `OpenAsync` 必失败。HUD 开不出来只记 Error，交互键与点击 NPC 照常可用。
- **HUD 打开与 `Dispose` 竞态**（`DialogueInteractionFocus.cs:152`）：`OpenHudAsync` await 期间作用域可能已 `Dispose`，
  那时 `hud` 字段还没赋值、`Dispose` 关不到它；所以 await 回来先看 `disposed`，是就立刻关掉刚开的面板，避免孤儿 HUD。
- **面板收尾对称退订、只关打开过的**（`DialogueController.cs:143`、`207`–`217`）：历史面板关闭前先退订 `OnDismiss`（与跳过确认弹窗对称）；
  `finally` 里对 `null`（从未打开或已关）的面板不调 `CloseAsync`，中途取消时不会对未打开的面板报错。
- **历史面板下沉到 Core、改名 `TranscriptView`、放 Top 层**：Performance 模块的 LOG 功能要显示同一种「说话者 + 正文」记录，
  但玩法模块之间不能互相引用私有实现，于是把面板原样下沉（序列化字段名与脚本 / 预制体 GUID 不变，预制体接线不丢），
  调用方各自把自己的记录转成 `TranscriptLine` 交给它。放 Top 层是因为演出会整层隐藏 Popup 层，只有 Top 层能压在演出与对白面板之上；
  Top 层不进 UI 栈，所以 `CloseOnCancel` 恒为 `false`，Esc 与「关闭」按钮都由调用方（`DialogueController` / `PerformanceService`）读到事件后自己收尾
  （`Assets/_Project/Scripts/Core/UI/Views/TranscriptView.cs:38`–`47`）。
- **HUD 常驻、只切 `root`**：每次进出范围都走 `UIService` 开关会反复实例化 / 淡入淡出。
- **气泡组件挂预制体根、自包含**：`target` 为空时向父级找 `DialogueInteractable`，放进 NPC 子物体即生效；
  不塞进 `DialogueInteractable`（逻辑组件不依赖 Canvas / TMP / LitMotion），也不塞进标记（将来单换气泡样式不动标记）。
- **气泡高度自适应靠预制体布局 + 强制重排**：根与 `Content` 两级 `VerticalLayoutGroup`（控制子高度）、根上 `ContentSizeFitter`（纵向 Preferred），
  高度随 `Body` 行数伸缩；世界空间 Canvas 设完文本当帧不会自动重排，`Show` 里 `ForceRebuildLayoutImmediate`（`DialogueSpeechBubble.cs:108`）补上，
  否则换句那一帧高度 / 位置还是上一句的。
- **选项图标按节点缓存**：条件资格每 0.25 s 变化就会 `ClearChoices` 重建选项行，不缓存会反复加载 / 释放同一张图；
  缓存跨节点又会让句柄活过对白，所以换节点即释放。
- **Boot 相机让位放 Core**（`FallbackCamera`）：`SceneGameState` 只管场景加载卸载、不知道相机；`GameBootstrap` 只管启动。
  挂在相机物体上的事件驱动小组件，不每帧轮询。

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动或报错：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Assets/_Project/Scenes/Boot.unity` 的 `GameBootstrap` 物体挂 `DialogueInstaller`，**Config** 字段拖 `Assets/_Project/Data/Dialogue/DialogueConfig.asset` | 没挂：解析不到 `DialogueService`；没拖：记 Error 并用默认值顶上（`DialogueInstaller.cs:72`） |
| 面板地址 | Addressables（UI 组）`DialogueView` → `Prefabs/UI/DialogueView.prefab`；`TranscriptView` → `Prefabs/UI/TranscriptView.prefab`（对白与演出共用，Core）。**地址等于类名** | `ui.OpenAsync<T>()` 找不到预制体 |
| 立绘地址 | 角色表里每个 `sprite` 在 Addressables 有同名地址（现为 `Dialogue/Portrait_<角色>_<表情>`，UI 组）。当前四张占位图内容 = 方舟头像（长者 = 斯卡蒂、旅人 = 德克萨斯，同名替换 `Portrait_elder_default/angry.png`、`Portrait_traveler_default/smile.png`），两表情暂同图，美术按表情出图时同名替换即可 | 回退默认表情；默认也缺则隐藏该槽 |
| 交互 HUD / 跳过确认地址 | `DialogueInteractHudView` → `Prefabs/UI/DialogueInteractHudView.prefab`；`DialogueSkipConfirmView` → `Prefabs/UI/DialogueSkipConfirmView.prefab`（UI 组，地址等于类名） | HUD：记 Error，无底部交互提示；弹窗：点跳过时对白抛异常收尾 |
| 选项图标地址 | 表里 `icon` 非空时 Addressables 有同名地址（现为 `Dialogue/ChoiceIcon_Go` / `_Leave`，UI 组） | 埋 `choice_icon_failed`，该选项无图标 |
| 玩家标记 | 玩家根挂 `DialogueInteractionActor`（场景里一个） | 没有焦点、没有 HUD、交互键无效；NPC 未配 `actor` 时不限距离 |
| NPC | 根上 `BoxCollider`（3D）或 `Collider2D`（2D）+ `DialogueInteractable` + `DialogueInteractableMarker`；子物体 `MarkerIdle` / `MarkerFocus`（SpriteRenderer）/ `NameLabel`（TMP 3D），3D 场景子物体再挂 `CameraBillboard` | 缺碰撞体：点不中；缺标记：无头顶提示（交互照常）；被任务目标标记接管时「…/!」隐藏、名字照常，属预期 |
| 无树 NPC | `dialogueId = 0`、`bubbleLines` 非空，再放一个 `DialogueSpeechBubble.prefab` 实例作子物体，并拖进标记的 `speechBubble` | 缺气泡实例：交互只抛事件没人显示；没拖进标记：「!」与气泡重叠 |
| 点击拉起 | 场景相机挂 `PhysicsRaycaster`（3D 碰撞体）或 `Physics2DRaycaster`（`Collider2D`）；EventSystem 由 `UIService` 创建 | `OnPointerClick` 静默不触发（仍可交互键 / HUD / 代码调 `Interact()`） |
| Boot 相机 | Boot 的 `Main Camera` 挂 `FallbackCamera`；玩法场景自带启用的相机 | Boot 相机在玩法相机之上重画，灰底 |
| 绑定 | `DialogueSceneBinder` 只在启动与 `sceneLoaded` 时扫描（含未激活物体，不含 DontDestroyOnLoad） | **运行时 `Instantiate` 的 `DialogueInteractable` 要手动 `Bind(service)`**，否则交互只记 Warn；且**不进 `Bound`、不参与焦点** |
| 入口 | 从 Boot → 标题「开始」进入玩法场景才有对白服务、焦点系统与 EventSystem | 直接 Play 玩法场景：有树 NPC `Start` 记 Warn，焦点 / HUD / 点击都不生效；只剩无树气泡链路本身可用（代码调 `Interact()`） |

示例：`Assets/Scenes/SampleScene.unity`（3D）的 `player`（Actor）、`Npc_Elder`（1001）、`Npc_Traveler`（1002）、`Npc_Villager`（无树 + 气泡）；
三个 NPC 的 `interactRadius` 均为 2.0（约一个多身位）。玩家 `player` 与巡逻者现已换成拼接小人（`Visual` 下的纸片隐藏、小人挂其下），
对白时停期间小人回到待机呼吸，见 [`characterpuppet-module-guide.md`](../characterpuppet/characterpuppet-module-guide.md)。

`DialogueView` 序列化字段（`DialogueView.cs:38`–`68`）与预制体物体的对应（面板整体节点 / 锚点 / 颜色见下「对白面板视觉与演出面板共用规范」）：

| 字段 | 预制体物体 | 备注 |
| --- | --- | --- |
| `speaker` / `body` | `Speaker`、`Body` | TMP；`body` 开富文本 |
| `portraits[0]` / `portraits[1]` | `PortraitLeft` / `PortraitRight` | `Image`，**长度必须为 2**；槽 0 = 左侧头像位、从左侧进出；槽 1 = 右侧头像位、从右侧进出（角色 `side` 决定用哪槽）。同一时刻只显示说话者那一槽，非说话者 / 旁白传 `speaking = false` 即收起（等同 null）；首次换表情时运行时在同级复制出 `PortraitLeftGhost` / `PortraitRightGhost` 残影（不进预制体，美术同名替换不受影响）；**没有压暗、没有缩放** |
| `avatarFrame` / `avatarFrameRight`（均可选） | `AvatarFrame` / `AvatarFrameRight` | `avatarFrame` 只跟槽 0（左框）显隐、`avatarFrameRight` 只跟槽 1（右框）显隐；不接则对应头像框不随内容显隐 |
| `holdPrompt`（可选） | `HoldPrompt`（连同子物体 `HoldHint` 一起显隐） | 等待推进（`AwaitAdvance`）时显示，由 `SetAdvancePrompt(bool)` 切换，值没变不重写；打开面板时强制隐藏一次 |
| `tapArea` | `TapArea` | 全屏透明 Button，**层级在 `ChoiceRoot` 与控件按钮之下**，否则吞掉选项点击 |
| `history` | `HistoryButton` | 标签固定文案「LOG」 |
| `auto` / `autoLabel` | `AutoButton`、`AutoButton/Label` | 标签「自动」/「自动中」 |
| `speed` / `speedLabel` | `SpeedButton`、`SpeedButton/Label` | 标签 `x1` / `x2` / `x4` |
| `skip` / `skipLabel` | `SkipButton`、`SkipButton/Label` | 跳过中置灰；标签固定文案「跳过 ▶」 |
| `historyLabel` | `HistoryButton/Label` | |
| `autoHintLabel` / `speedHintLabel` / `skipHintLabel` / `historyHintLabel`（均可选） | 对应按钮下的 `Hint` | 接了：键位写这里、主标签只写文案（跳过 / LOG 仍是固定文案）；不接：键位拼进主标签 |
| `autoLabelShadow` / `speedLabelShadow`（均可选） | `AutoButton/LabelShadow`、`SpeedButton/LabelShadow` | 接了就镜像主标签文案 |
| `choiceRoot` / `choiceTemplate` | `ChoiceRoot`（VerticalLayoutGroup，右侧竖排：anchor / pivot (1, 0.5)、右边距 40、宽 560）/ `ChoiceTemplate`（白色圆角胶囊，同一张 `ui_perf_panel_round.png` Sliced，`pixelsPerUnitMultiplier = 2`，560×44，文字字号 22 自适应到 16；悬停淡金 + `UIButtonFeedback`） | 模板下须有子物体 `Icon`（Image，名字写死）与 `Label`（TMP）；运行时隐藏 |

漏接任一必需字段（含模板缺 `Icon`），`OnOpenAsync` 的 `Validate()` 会逐个点名抛出（`DialogueView.cs:355`）；可选字段漏接只是没有对应的显隐 / 键位小字 / 投影，不抛异常。

其余预制体字段（HUD / 弹窗漏接在打开时点名抛出；气泡找不到交互组件记 Warn）：

| 预制体 | 字段 → 物体 |
| --- | --- |
| `Prefabs/UI/DialogueInteractHudView.prefab` | `root` / `button` → `Root`（整条胶囊按钮：锚点底部居中、`anchoredPosition (0, 72)`、`sizeDelta (320, 48)`，`HorizontalLayoutGroup`）；`keyLabel` → `KeyBadge/KeyText`（徽章 48×40）；`label` → `Label`（弹性宽度、单行省略号）；`icon` → `Icon`（可空，默认隐藏、不参与布局）。放底部居中而非右下角：右下角已被探索 HUD 的攻击 / 潜行 / 跑步按钮占住，PC 游戏交互提示也惯例在屏幕下方中央；与探索 HUD 的 `InteractPrompt`（y 160）不重叠 |
| `Prefabs/UI/DialogueSkipConfirmView.prefab` | `message` → `Message`；`confirm` → `ConfirmButton`；`cancel` → `CancelButton`；`defaultSelected`（UIView 通用字段）→ `CancelButton`（打开即选中取消） |
| `Prefabs/World/DialogueSpeechBubble.prefab`（世界空间 Canvas） | `root` → `Content`；`nameLabel` → `Name`；`body` → `Body`；`arrow` → `Arrow`；`group` → 根 `CanvasGroup`；`target` 留空（向父级找）。布局：根 `VerticalLayoutGroup` + `ContentSizeFitter`（纵向 Preferred）、`Content` 再一层 `VerticalLayoutGroup`，别给它们写死高度 |

## 对白面板视觉与演出面板共用规范

`DialogueView.prefab` 与 `Prefabs/UI/PerformanceView.prefab` 是同一套视觉语言（白色圆角面板、左右两个头像位——谁说话头像就在谁那一侧、深灰字、底部居中 ▼ 继续提示、右上白字投影控件），2026-09-28 起以演出面板为基准。改样式先看
[`performance-module-guide.md` 的「对白面板」一节](../performance/performance-module-guide.md#对白面板prefabsuiperformanceviewprefab)
里的节点 / 锚点 / 颜色表，两边共用节点名与样式值。`Tests/EditMode/Dialogue/TalkPanelConsistencyTests.cs` 读两份真实预制体逐节点比对 Rect、Image、TMP 属性；改任一面板必须同步另一份，否则这组测试会挂。样式全在预制体，代码（`DialogueView.cs` / `PerformanceView.cs`）只填文字与显隐。

## 内容表（Luban）

| 项 | 约定 |
| --- | --- |
| schema | `Tables/Defines/dialogue.xml`（module `dialogue`），**不改** `__tables__.xlsx` / `__beans__.xlsx` |
| 对话树 | `Tables/Data/dialogue/<id>.json`，**一文件一棵树**（JSON 对象，文件名 = id） |
| 角色表 | `Tables/Data/dialogue_character.json`，一个文件放数组，table 的 `input` 写 `*@dialogue_character.json` |
| 字段 | **JSON 不允许缺字段**：`revision`、`blocking`、空数组、空串都要显式写 |
| 生成 | `powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1` |
| 生成物 | 代码 `Assets/_Project/Scripts/Core/Config/Generated/dialogue/`（`cfg.dialogue.*`，代码里写 `global::cfg.dialogue.` 避免与 `DialogueContent.Node` 撞名）；数据 `Assets/_Project/Data/Config/dialogue_tbdialogue*.bytes`；都不手改 |
| 翻译规则 | `side` → 该槽 `Show`；`clearOther` → 另一槽 `Clear`（旁白忽略）；`expression` 空取默认；`speakerName` 空取角色显示名；`revision < 1` 按 1；条件 `anyOf[].all[]` = 外层 OR 内层 AND（`DialogueCatalog.cs:170`、`200`） |
| 条件事实 | `ConditionFact` 与 `EncounterContext.Fact` **按名字**映射，改名 / 增项两边一起改（`DialogueCatalog.cs:242`） |
| 选项图标 | `Choice.icon`（Addressables 地址，空串 = 无图标，**JSON 必须显式写 `"icon": ""`**）→ `DialogueContent.Choice.IconKey`（`DialogueCatalog.cs:226`） |
| 插播演出 | `Node.performance`（演出 id = Addressables 地址，空串 = 不插播，**JSON 必须显式写 `"performance": ""`**）→ `DialogueContent.Node.PerformanceId`（去首尾空白，`DialogueCatalog.cs:164`）；验证树 `1003.json` 第 2 句带 `perf_sample_greeting` |

内容非法（跳转不存在、选项 `next` 与 `outcome` 不是恰好一个、表情不存在、选择节点无选项）在首次访问 Catalog 时抛
`ArgumentException`，消息带对话 id；不会半途落缓存。

## 埋点（模块名 `dialogue`）

| 事件 | 位置 |
| --- | --- |
| `play_requested` / `play_rejected`（busy / unknown_id / invalid_config）/ `play_failed` / `ended` | `DialogueService` |
| `started` / `node_entered` / `choice_selected` / `choice_rejected` / `skipped` / `restored` / `cancelled` | `DialogueRules` |
| `portrait_missing` / `expression_fallback` / `portrait_load_failed` / `portrait_fallback_failed` / `choice_icon_failed`（Warn，带 `key`） | `DialogueController` |
| `performance_unavailable`（Warn，`node`、`performance`；演出服务未注册）/ `performance_failed`（Error，`node`、`performance`、异常；演出抛非取消异常，对白照常继续） | `DialogueController` |
| `focus_changed`（`target` 物体名、`id` 对话树；只在变化时埋） | `DialogueInteractionFocus` |

契约见 [`docs/telemetry.md`](../../../../docs/telemetry.md)。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/Dialogue/DialogueRulesTests.cs`（8 条） | 补全 / 恢复不重记历史、选项复验、Skip 停在选项 / 写已读 / 环路抛错 / Generation 不符 |
| EditMode | `.../DialoguePlaybackPolicyTests.cs`（14 条） | 三连点窗口、倍速循环、自动计时、重置、非法参数（含标点停顿为负、标点字符 null、动效参数缺省 / 未初始化 / 越界） |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/Core/TypingCadenceTests.cs`（12 条，已随类下沉到 Core） | 无标点时与旧「累加取整」一致、标点后停顿、停顿中显示字数不变、停顿 0 / 标点表空退化、x2 下停顿减半、连续标点只停一次、句末不停、单帧大预算、Reset、非法参数 |
| EditMode | `.../DialogueCatalogTests.cs`（8 条） | 读真实 `.bytes`：1001 / 1002 结构、立绘指令、每个表情有地址、条件选项、选项图标键 |
| EditMode | `.../DialogueInteractableTests.cs`（17 条，含参数化） | 三维距离判范围、无树台词按序循环、有树未绑定 / 无树无台词不可交互、`SelectNearest` 跳过超范围；交互提示键位显示串为空回退「E」、「对话 · 名字」拼接；`TryGetIconAnchor` 焦点图优先 / 只配可交互图 / 都没配 |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/Dialogue/DialogueServiceTests.cs`（6 条） | 进行中重入抛 `InvalidOperationException`；未知 id 抛 `ArgumentException` 且不碰暂停 / 输入；Present 异常时清理并发 `OnEnded`；对白期间 Dialogue 图开、Gameplay 图关、Hud 层隐藏，取消 / 异常后对称恢复，进来前关着的 Gameplay 与已隐藏的 Hud 不被打开 / 不被误亮 |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/Dialogue/DialogueControllerTests.cs`（3 条） | 静态 `BuildTranscript`：选择项记为说话者「选择」；转换后经 `TranscriptView.Format` 拼出的文本与旧历史面板逐字节一致；`null` / 空历史返回空列表 |
| EditMode | `.../DialogueReadStoreTests.cs`（3 条） | 空档案读入为空；写 3 个键后新 store 读回一致且原地填充同一实例；同帧两次对白结束只写一次（计数 `ISaveService` 装饰器包临时目录 `JsonSaveService`） |
| EditMode | `.../DialogueKeyboardInputTests.cs`（9 个方法 / 30 例） | 键位映射：主面板各键、未激活 / 未就绪忽略、选项期 Advance 忽略、Choice N 越界 / 不可用 / 空行忽略、历史与跳过确认期只放行弹窗键 |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/Core/WorldPauseServiceTests.cs` | 暂停引用计数与 timeScale 恢复（Core 侧） |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/Dialogue/DialogueShowcase.cs`（7 条） | `Portraits_SpeakerAvatarSwapsAndCrossfades`（长者入场 → l2 只显示旅人（头像在右侧，x<0）、长者收起 → l3 反转（长者头像在左侧，x>0）→ c1 残影交叉淡化）；交互 → 打字 → 选项 → 结束且全程时停，「对话拉起·世界时停」检查 Hud 层不可见、「结束·世界恢复」检查 Hud 层恢复可见；历史（LOG）改用 `TranscriptView` 后新增三步：点左上「LOG」打开 → 内容含第一句「说话者：正文」→ 点「关闭」，对白照常停在原节点；跳过（经确认）停在选项；点击旅人拉起 1002；`SkipCancelled_DialogueContinues`；`Focus_ShowsHudButton_AndHudClickStartsDialogue`；`Bubble_ShowsAboveHead_WithoutPausing` |
| 回放舞台 | 回放在 `Assets/Scenes/SampleScene.unity` 上跑（2D） | `Main Camera`（`Physics2DRaycaster`）、`Player`（Actor）、`Elder`（1001）、`Traveler`（1002）、`Villager`（无树 + 气泡） |

跑 `/unity-test EditMode Dialogue`；视觉验收跑 `/verify-module Dialogue`（编辑器须打开）。

## 已知约束 / 未做

- **非阻塞旁白未实现**：节点 `blocking` 字段与 `DialogueRules.Blocking`（`DialogueRules.cs:39`）保留，但
  Service 对整段对白一律暂停世界、一律等点击推进。
- **跳过确认后不可撤销**：确认后本段内一直跳过。
- **交互提示只显示键盘键位**：徽章文字取 `Interact` 第一条键盘绑定的 `GetBindingDisplayString()`（`DialogueInteractionFocus.cs:199`），打开时赋一次；取不到回退「E」（`DialogueInteractHudView.cs:90`）。手柄为主输入时不切换成手柄键位（要跟踪最近输入设备，暂不做）；运行中改键也不会刷新（尚无改键 UI）。
- **常驻台词未进表**：`bubbleLines` 在 Inspector 配，不走 Luban、未本地化。
- **`FallbackCamera` 只在场景加载 / 卸载时判断**：玩法场景加载后才启用的相机不会让它让位。
- 对白与美术装饰（手绘边框、贴纸、背景模糊）仍是占位（PRP 8.4）。
- **已读记录走独立档案 `dialogue-read`**：启动读入、对白结束写出（`DialogueReadStore.cs`）；只在对白结束时落盘，对白进行中强退会丢这一段的已读键。
- **存读档未接**：`Capture / Restore` 与 `DialogueSaveData` 已有且有测试，但没有调用方；`Preparing` 阶段不能 `Capture`。
- **`DefaultDialogueConditionSource` 是占位**：所有正向事实为真、无剧情标记，所以依赖 `StoryFlag` 的选项
  （如 1001 的第三个选项）当前永远不可用。Narrative 接线后替换注册并删掉本类。
- **既有 `CanSkip` 已删**；`DialogueConfig.skipInterval` 同删。别按旧文档找它们。
- `DialogueConfig.slotCount` 已删除（运行时从未读取）；槽位数固定为 `DialogueContent.SlotCount = 2`。

## 禁止事项

- 不要在 Controller / View / Rules 里改 `Time.timeScale` 或输入图——只在 `DialogueService`。
- 不要在 `DialogueView` 里持有状态或注入服务：它由 Addressables 实例化、不经容器。
- 不要把表现参数（点击、速度、计时）塞回 `DialogueRules`；规则只管内容推进与存档语义。
- 不要在构造函数里访问 `DialogueCatalog.Characters` / `TryGet`：容器构建期配置服务未就绪。
- 不要在 `Game.Core` 里加对白名词；Core 只提供通用的暂停、UI、资源服务（`FallbackCamera` 也不认识对白）。
- 不要在别处写 `DialogueInteractable.Focused`：只有焦点系统能写（`internal set`），多个写者会让标记与 HUD 打架。
- 不要让焦点系统每帧 `Find`：候选只从 `DialogueSceneBinder.Bound` 读。
