---
type: module-guide
module: quest
layer: runtime
maturity: seed
---

# Quest 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Quest/` 之前读这份。对外怎么调看
> [`quest-external-api.md`](quest-external-api.md)，要加东西看 [`quest-extension-guide.md`](quest-extension-guide.md)。
> 设计定稿与执行中的修订见 [`PRP/quest-system/prp.md`](../../../../PRP/quest-system/prp.md) 第 7 节；与源码不符时以源码为准。

## 职责边界

**做**：任务表 → 激活（前置 + 主线唯一）→ 目标推进 → 完成连锁；HUD 常驻任务栏 + 屏幕指引（悬浮 / 贴边箭头）；
任务面板（列表 + 详情 + 追踪切换）；与对话联动（对话结束推进 TalkTo）；场景到达点判定；存档分区；接取 / 完成通知（经 Core `INotificationService`）。

**不做**（本期非目标，见 [`prd.md`](../../../../PRP/quest-system/prd.md)）：任务奖励、失败与限时、
已完成任务列表页、任务对话内容、寻路距离、多语言。

## 运行时类分工

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `QuestRules` | **纯 C#** 规则：激活、`Report` 推进、完成连锁、追踪切换、排序、`Capture/Restore` | 根作用域单例；`QuestService` 调 |
| `QuestContent` / `QuestDefinition` / `QuestObjectiveDefinition` | 与表无关的内容模型，构造时校验 id / 目标 / 前置环 | `QuestCatalog` 产出，测试可直接 `new` |
| `QuestCatalog` | Luban 表 → `QuestContent` 的翻译与缓存，首次访问才读表，额外校验 TalkTo 对话存在 | 根作用域单例 |
| `QuestProgress` | 单条任务运行时进度（状态 / 当前目标 / 计数 / 激活序号），仅 `QuestRules` 能改 | `QuestRules` 持有 |
| `QuestService` | **对外门面**：转发上报 / 追踪、写回存档、按固定顺序发布事件 | 根作用域单例 + `IGameService` |
| `QuestSceneBinder` | 入口点：`sceneLoaded` 扫场景登记 `QuestLocation`；缓存 `Camera.main`；解析目标世界坐标（`QuestTarget`）。NPC 头顶锚点三级（纯静态 `ResolveNpcAnchor`）：① NPC 根上 `DialogueInteractableMarker.TryGetIconAnchor` → 与对话「…/!」图标同位；② 根 Collider 顶部 + `MarkerLift`；③ 位置上方 `LocationMarkerHeight`。Collider 与标记组件首次遇到时各 `GetComponent` 一次并缓存（含 null），场景卸载清已销毁项 | 根作用域入口点（`AsSelf`） |
| `QuestTarget` | 只读结构体：测距用 `Position`（脚底/地点）+ 标记与投影用 `Anchor`（头顶）+ `Interactable`（TalkTo 目标的 NPC 交互组件，有无碰撞体都带；地点目标为 null） | `QuestSceneBinder.TryResolveTarget` 产出 |
| `QuestTargetMarker` | 世界空间标记 MonoBehaviour：`Show(Vector3)/Hide()`，朝向相机由预制体自带 `CameraBillboard` 负责；摆到 NPC 头顶时替代该 NPC 的对话「…/!」图标（由 `QuestHudPresenter` 接管，不叠加） | `QuestHudPresenter` 实例化并驱动；沉浸模式（`IHudVisibility.IsHudHidden`）下与对白期间一样隐藏，贴边箭头随 Hud 面板由 UIService 隐藏 |
| `QuestLocation` | 场景组件：地点键 + 半径，供到达判定与指引 | 场景物体 |
| `QuestObjectiveDriver` | 入口点：订阅对话结束 → 上报 TalkTo；每帧对到达型当前目标测距 → 上报 ReachLocation | 根作用域入口点 |
| `QuestGuidanceMath` | 静态纯函数：视口坐标 → 屏内悬浮 / 屏外贴边+箭头；直线距离取整 | `QuestHudPresenter` 调 |
| `QuestHudView` | `UIView`（Hud 层）：任务栏 + 指引标识，只显示与抛事件 | `IUIService` 实例化 |
| `QuestHudPresenter` | 入口点：`BootCompletedEvent` 后开 HUD 常驻并实例化世界标记、挂任务键（`Gameplay/Journal`）订阅并把第一条键盘绑定写进 HUD 键位提示；订阅四个事件刷新文字；每帧摆标记 / 算指引；对白中隐藏；点任务栏或按任务键开面板 | 根作用域入口点 |
| `QuestNotificationPresenter` | 入口点：订阅 `QuestActivatedEvent` / `QuestCompletedEvent`，经 `service.Content.TryGet` 取任务标题，按 `QuestConfig` 的两条格式拼好交给 `INotificationService.Show`；`BootCompletedEvent` 之前的事件（启动时激活首条任务）不弹 | 根作用域入口点 |
| `QuestPanelView` | `UIView`（Panel 层）：列表 + 详情 + 追踪按钮，只显示与抛事件 | `IUIService` 实例化 |
| `QuestPanelController` | 开关面板会话：持世界暂停令牌 + 关 Gameplay 图；把面板事件转成 `service.Track/Untrack`；被 Esc（Core `UICancelRouter`）从外部关掉时经 `OnClosed` 收尾 | 根作用域单例 |
| `QuestConfig` | SO：指引留白 / 悬浮偏移 / 距离刷新间隔 / 标记抬升与高度 / 标记预制体地址 / HUD 与面板固定文案 / 接取与完成通知文案格式 | `Data/Quest/QuestConfig.asset` |
| `QuestSaveData` / `QuestProgressData` | 存档分区（纯 DTO） | `rules.CaptureInto` 产出 |
| 四个事件（`QuestActivatedEvent` 等） | `readonly struct`，一文件一个 | `QuestInstaller.InstallEvents` 注册 broker |
| `QuestInstaller` | `GameplayInstaller`：注册以上全部 | Boot 场景 `GameBootstrap` 物体 |

## 数据流

```text
内容：Tables/Data/quest/<id>.json ─Luban→ cfg.quest.TbQuest + quest_tbquest.bytes
      ─IConfigService.Tables→ QuestCatalog ─翻译+校验→ QuestContent

进度：QuestService.InitializeAsync
  → QuestRules(content) → rules.Restore(saves.Get<QuestSaveData>()) → rules.ActivateAvailable() → Flush()
  Report/Track/Untrack → QuestRules 处理 → Flush()：CaptureInto(saves.Get<QuestSaveData>())（每次重取分区）
    → 按 Activated → Progressed → Completed → TrackingChanged 顺序发布 MessagePipe 事件

场景侧：QuestObjectiveDriver.Tick 测距 InProgress 里的 ReachLocation 当前目标 → service.Report
        DialogueService.OnEnded → service.Report(TalkTo, dialogueId.ToString())
表现：QuestHudPresenter.Tick → binder.TryResolveTarget（得 Position 测距 / Anchor 标记；NPC 的 Anchor 优先取其对话图标锚点，
      次选碰撞体顶部 + MarkerLift，再次位置上方 LocationMarkerHeight）→ marker.Show(Anchor) 常驻摆位
      → SceneCamera.WorldToViewportPoint(Anchor) → QuestGuidanceMath.Solve
      → 屏内：hud.HideGuidance，只留世界标记；屏外：hud.SetGuidance 贴边标识（中心距屏幕边 EdgeMargin 72）+ 旋转
        Guidance/ArrowPivot，箭头绕标识公转到朝向目标一侧、距离文本落在对面并反向旋转保持正立，标记仍留在目标处（被相机裁掉）；
      NPC 目标：marker.Show 之后 SetOverridden(target.Interactable) 接管其对话「…/!」图标（marker 实例化失败时不接管）；
      HideMarker（对白 / 沉浸 / 解析失败 / 缺相机）与 Dispose 交还，换目标时先还旧再接新；
      对白中两者都隐藏；HUD 点击 / 任务键 Gameplay/Journal → QuestPanelController.OpenAsync → SetList/SetDetail
      关闭：面板返回按钮 → QuestPanelController.CloseAsync；Esc（UI/Cancel）→ UICancelRouter → IUIService.CloseTopAsync → QuestPanelView.OnClosed → 控制器收尾
```

## 重置进度（ResetProgress）

`QuestService.ResetProgress()`（`QuestService.cs:192`，与读档共用 `RestoreAndBroadcast`）把全部任务打回「新开局」：清空待发布的事件缓冲 →
`rules.Restore(new QuestSaveData())`（一份全新的空分区）→ `rules.ActivateAvailable()` → 写回存档分区 →
补发事件（每条重新激活的任务一条 `Activated`，进行中任务各一条计数归零的 `Progressed`，至少一条
`TrackingChanged` 让 HUD 与面板整体刷新）。`IsReady == false` 时记 Warn 并忽略，不抛异常。这是**内存重置**，
不涉及读盘 / 写盘（本期没有落盘存档）。首个调用方是 `Game.IsometricExploration.ExplorationControlsPresenter`
的「重置进度」确认流程（见 `isometricexploration-module-guide.md`），随后紧跟 `Game.Loot.LootService.Reset()`
把物资箱一起合上、`IGameFlow.GoToAsync<MonsterEncounterState>()` 重进场景。

## 依赖方向

`Game.Quest → Game.Core`（UI / Save / Config / Timing / Input / Events / Telemetry / Boot / `Simulation.GameMath`），
`Game.Quest → Game.Dialogue`（只用 `DialogueService` 事件与 `IsRunning`、`DialogueSceneBinder.Actor/Bound`、
`DialogueInteractable.DialogueId/transform`）。`Game.Dialogue`、`Game.Narrative`、`Game.Core` 不认识 `Game.Quest`。

## 为什么这样设计（源码读不出来的部分）

- **`QuestRules` 不持有存档分区引用**：`ISaveService.Commit` 会整体替换分区实例，长期持有旧引用会把进度写进一份没人读的对象；
  `QuestService.Flush` 每次操作后重新 `saves.Get<QuestSaveData>()` 再 `CaptureInto`（`QuestService.cs:302`）。
- **事件走 MessagePipe，Core 因此新增 `GameplayInstaller.InstallEvents`**：`Install` 拿不到根作用域的
  `MessagePipeOptions`，Dialogue 因此退回 C# `event`（违反 `EventConventions.cs` 第 1 条）。Quest 不想再开这个例外，
  于是给 `GameplayInstaller` 加一个默认空实现的虚方法，在 `Install` 之前把 `options` 递进来，既有注册器零改动
  （`GameplayInstaller.cs`；`GameLifetimeScope.InstallGameplay`）。
- **玩家原点复用 `DialogueSceneBinder.Actor.Anchor`**：SampleScene 玩家根已挂 `DialogueInteractionActor`，
  再加一个空标记组件是重复登记，扫场景只该有一处（`QuestSceneBinder.PlayerAnchor`）。代价是没有对话模块的场景
  任务系统就没有到达判定与指引；将来要解耦再在 Core 抽 `IPlayerAnchor`，只改这一处。
- **指引数学用 `Game.Core.Simulation.GameMath` 而非 `Mathf`/`Math`**：Runtime 有「重放确定性」lint 规则禁用原生数学库，
  `QuestGuidanceMath` 改用 `GameMath.Atan2/Min/Abs/Floor`；弧度转角度的换算常量本地写字面值
  （`QuestGuidanceMath.cs:18`），半数进位用 `(int)GameMath.Floor(d + 0.5f)` 而非原生四舍五入（会做银行家舍入）。
- **任务键挂在 `QuestHudPresenter` 而不是 `QuestPanelController`**：控制器不是入口点，没有「启动完成后」的时机挂动作订阅；
  HUD 入口点已有 `BootCompletedEvent` 时机、「点任务栏开面板」的同一条入口与错误处理，键位提示也写在它持有的 HUD 上。
  面板开着时 Gameplay 图被关，所以任务键只开不关；关闭交给 Esc 与返回按钮（`QuestHudPresenter.ShouldOpenOnJournal`）。
- **`QuestPanelView.OnClosed`**：面板可能被 `IUIService.CloseTopAsync` 等外部路径关掉（不经过
  `QuestPanelController.CloseAsync`），这个事件让 Controller 仍能收尾暂停令牌与恢复输入图；Controller 自己关时
  先摘掉这个监听，避免同一次关闭收尾两次（`PRP/quest-system/prp.md` 第 7 节）。
- **屏内不用屏幕空间指引标识，改用世界空间头顶标记**：一是要跟 NPC 头顶气泡（对话模块）表现语言一致，玩家已经认
  「头顶浮标记」这个语言（参考明日方舟的目标 / 敌人头顶标）；二是纸片角色体积大，固定像素偏移的屏幕空间标识在
  近距离时会被脸挡住或叠脸上，世界空间标记锚定在头顶就没有这问题（`QuestSceneBinder.NpcTarget`）。
  **锚点与尺寸对齐被替代的对话图标**：最初锚在「碰撞体顶部 + `MarkerLift`」（y 1.9）、`Icon` 缩放 0.6，比对话图标
  （`MarkerIdle` / `MarkerFocus` 局部 y 2.15、缩放 0.45）低 0.25、大 33%，接管时像换了个东西、还压头发。现在锚点优先取
  `DialogueInteractableMarker.TryGetIconAnchor`（焦点图优先，其次可交互图），预制体 `Icon` 缩放改为 0.45，贴图同一张，
  看起来就是同一个气泡换成黄色；碰撞体规则只作没有对话标记时的降级，`MarkerLift` 因此保留。
  标记摆到 NPC 头顶时**替代**而不是叠在对话模块的「…/!」图标上：`QuestHudPresenter.SetOverridden` 调
  `DialogueInteractable.SetMarkerOverridden(true)`，NPC 的 `DialogueInteractableMarker` 只隐两张图、焦点名字与台词气泡照常；
  标记隐藏、换目标、Dispose 时成对交还（切换逻辑是纯静态 `QuestHudPresenter.SwitchIconOverride`，已销毁的旧 NPC 跳过）。
  **标记实例化失败（`marker == null`）时不接管**，否则 NPC 头顶什么都没有。依赖方向不变：Quest 认识 Dialogue，
  Dialogue 只暴露与任务无关的「图标被外部标记接管」开关（同 asmdef 内 `internal`，同 `SetHiddenByHud` 模式）。

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动或报错：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `QuestInstaller`（排在 `DialogueInstaller` 之后），**Config** 拖 `Data/Quest/QuestConfig.asset` | 没挂：解析不到 `QuestService`；没拖：记 Error 并用默认值顶上 |
| 预制体地址 | Addressables（UI 组）`QuestHudView` → `Prefabs/UI/QuestHudView.prefab`；`QuestPanelView` → `Prefabs/UI/QuestPanelView.prefab`。**地址等于类名** | `ui.OpenAsync<T>()` 找不到预制体 |
| 贴边指引层级 | `QuestHudView.prefab` 的 `Guidance`（锚点画布中心）下：`Icon`(56×56) + 空 RectTransform `ArrowPivot`（0,0，尺寸 0，pivot 0.5）；`ArrowPivot` 下 `Arrow`(28×28, 0,48) 与 `Distance`(120×30, 0,-60)。组件接线 `guidanceRoot`→Guidance、`guidancePivot`→ArrowPivot（旋转它让箭头公转、文本在对面）、`guidanceArrow`→Arrow（只管显隐，可空）、`distanceLabel`→Distance。`QuestConfig.EdgeMargin` 72 须 ≥ 公转半径 48 + 箭头半宽 14 + 余量（文本总在内侧不参与） | 缺 `guidancePivot`：打开 HUD 时 `Validate` 抛「引用未接线」；箭头直接挂 `Guidance` 下：只会原地自转、永远在标识正上方；EdgeMargin 小于 62：箭头转到朝外一侧时被屏幕边裁掉 |
| 目标标记预制体 | Addressables（UI 组）`QuestTargetMarker` → `Prefabs/World/QuestTargetMarker.prefab`（根 `QuestTargetMarker`，子 `Icon`：SpriteRenderer + `CameraBillboard`，缩放 0.45 与 NPC 对话标记 `MarkerIdle` / `MarkerFocus` 一致，贴图同为 `Marker_Focus.png` 染黄；改对话标记尺寸要同步改这里） | 找不到 / 根上无组件：画面内标记不显示，记 Error 并埋 `marker_missing`；画面外箭头不受影响 |
| `QuestConfig` 标记字段 | `MarkerLift`(0.3，仅 NPC 没有对话标记图标时的降级)、`LocationMarkerHeight`(1.5)、`TargetMarkerAddress`("QuestTargetMarker") 需与预制体地址一致 | 地址对不上：走上一行的降级；高度 / 抬升值用默认值不影响编译 |
| 场景地点 | 场景里 `QuestLocation` 的 `locationKey` 要与表里 `ReachLocation` 目标的 `key`（`location` 留空时兜底用 `key`）对应 | 找不到地点：驱动器不判定、指引不显示，`QuestSceneBinder` 记 Warn |
| 玩家标记 | 玩家根挂 `DialogueInteractionActor`（复用对话模块） | 无到达判定、无指引；`QuestObjectiveDriver.Tick` / `QuestHudPresenter.Tick` 直接返回 |
| 入口 | 从 Boot → 标题「开始」进场景才有任务服务 | 直接 Play 玩法场景：服务未初始化，`IsReady == false`，查询返回空、上报被忽略 |

示例接线：`Assets/Scenes/SampleScene.unity` 的 `QuestLocation`（`camp`、`lookout`）；`Scenes/Verify/Quest.unity` 独立验证场景。

## 内容表（Luban）

`Tables/Defines/quest.xml`（module `quest`）+ `Tables/Data/quest/<id>.json`（一任务一文件，字段不许缺省）。
生成物：`Core/Config/Generated/quest/`（`cfg.quest.*`）、`Data/Config/quest_tbquest.bytes`。内容非法（id 重复、
目标为空、前置成环、TalkTo 键非整数、ReachLocation 键为空、TalkTo 对话不存在）在首次访问 `QuestCatalog.Content`
时抛 `ArgumentException`，不落缓存；`QuestService.InitializeAsync` 捕获后记 Error，服务保持 `IsReady == false`。

编辑期校验入口 `Game.Editor.Quest.QuestTableValidator`（聚合报全部问题，交叉校验对话编号 / 场景地点键 / 计数键，
最后用 `QuestContent` 兜底），`GenerateTablesMenu.TryGenerate` 生成前自动跑，有 Error 不生成；错误文案里的目标序号从 1 起。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Tests/EditMode/Quest/QuestRulesTests.cs`（19） | 激活 / 单主线 / 当前目标推进 / 完成连锁 / 追踪切换 |
| EditMode | `.../QuestContentTests.cs`（12） | 内容校验（id / 目标 / 环 / 键格式） |
| EditMode | `.../QuestCatalogTests.cs`（5） | 真实表构造、TalkTo 对话缺失抛错 |
| EditMode | `.../QuestSaveDataTests.cs`（5） | 存档序列化往返、未知 id 跳过 |
| EditMode | `.../QuestGuidanceMathTests.cs`（9） | 屏内 / 屏外贴边 / 相机后翻转 / 距离取整 |
| EditMode | `.../QuestHudPresenterTests.cs`（4） | 任务键开面板判定、键位提示取第一条键盘绑定 |
| EditMode | `.../QuestNotificationPresenterTests.cs`（4） | 通知文案格式化：正常 / 空格式 / 格式写坏不抛 / 标题为空 |
| Showcase | `Tests/Showcase/Quest/QuestShowcase.cs` | HUD 指引、对白与地点联动、面板暂停、追踪切换（肉眼验收） |
| 验证场景 | `Scenes/Verify/Quest.unity` | 独立验证场景（透视相机、`player`、Elder/Traveler、两个 `QuestLocation`） |

跑 `/unity-test EditMode Quest`；视觉验收跑 `/verify-module Quest`（编辑器须打开，本轮尚未跑）。

## 已知约束 / 未做

- 读档 / 新游戏：`GameSession` 发布 `Game.Session.SessionStartedEvent`（分区已就位之后），`QuestService`
  在 `InitializeAsync` 里内部订阅，收到后调用 `ReloadFromSave()`；它与 `ResetProgress()` 共用私有的
  `RestoreAndBroadcast`（`QuestService.cs:215`）：清掉未发布事件 → `rules.Restore(saves.Get<QuestSaveData>())` →
  `ActivateAvailable()` → 补发进行中任务的 `Progressed` 与至少一条 `TrackingChanged` → `Flush()`。必须补发：
  `Restore` 不抛事件，`ActivateAvailable` 在已初始化的档上不改追踪，不补发 HUD 会停在读档前的文本。
  不另起入口点，重载就是门面自己的职责。
- `Report` 键统一为字符串：TalkTo 的 int → string 只在对白结束时发生一次，不在每帧路径上。
- 指引依赖 `Camera.main`：相机为空时 `HideGuidance` 并只埋一次 Warn。

## 禁止事项

- 不要绕过 `QuestRules.Report` 直接改 `QuestProgress`（属性 setter 是 `internal`）。
- 不要在 `QuestHudView` / `QuestPanelView` 里注入服务或持有状态：只显示与抛事件。
- 不要长期持有 `QuestSaveData` 引用：每次操作重新 `saves.Get<QuestSaveData>()`。
- 不要在 `Game.Core` 里加任务名词；`GameplayInstaller.InstallEvents` 本身不含任务概念。
