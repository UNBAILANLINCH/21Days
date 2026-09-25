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
任务面板（列表 + 详情 + 追踪切换）；与对话联动（对话结束推进 TalkTo）；场景到达点判定；存档分区。

**不做**（本期非目标，见 [`prd.md`](../../../../PRP/quest-system/prd.md)）：任务奖励、失败与限时、接取 / 完成弹窗通知、
已完成任务列表页、任务对话内容、寻路距离、多语言。

## 运行时类分工

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `QuestRules` | **纯 C#** 规则：激活、`Report` 推进、完成连锁、追踪切换、排序、`Capture/Restore` | 根作用域单例；`QuestService` 调 |
| `QuestContent` / `QuestDefinition` / `QuestObjectiveDefinition` | 与表无关的内容模型，构造时校验 id / 目标 / 前置环 | `QuestCatalog` 产出，测试可直接 `new` |
| `QuestCatalog` | Luban 表 → `QuestContent` 的翻译与缓存，首次访问才读表，额外校验 TalkTo 对话存在 | 根作用域单例 |
| `QuestProgress` | 单条任务运行时进度（状态 / 当前目标 / 计数 / 激活序号），仅 `QuestRules` 能改 | `QuestRules` 持有 |
| `QuestService` | **对外门面**：转发上报 / 追踪、写回存档、按固定顺序发布事件 | 根作用域单例 + `IGameService` |
| `QuestSceneBinder` | 入口点：`sceneLoaded` 扫场景登记 `QuestLocation`；缓存 `Camera.main`；解析目标世界坐标（`QuestTarget`） | 根作用域入口点（`AsSelf`） |
| `QuestTarget` | 只读结构体：测距用 `Position`（脚底/地点）+ 标记与投影用 `Anchor`（头顶） | `QuestSceneBinder.TryResolveTarget` 产出 |
| `QuestTargetMarker` | 世界空间标记 MonoBehaviour：`Show(Vector3)/Hide()`，朝向相机由预制体自带 `CameraBillboard` 负责 | `QuestHudPresenter` 实例化并驱动 |
| `QuestLocation` | 场景组件：地点键 + 半径，供到达判定与指引 | 场景物体 |
| `QuestObjectiveDriver` | 入口点：订阅对话结束 → 上报 TalkTo；每帧对到达型当前目标测距 → 上报 ReachLocation | 根作用域入口点 |
| `QuestGuidanceMath` | 静态纯函数：视口坐标 → 屏内悬浮 / 屏外贴边+箭头；直线距离取整 | `QuestHudPresenter` 调 |
| `QuestHudView` | `UIView`（Hud 层）：任务栏 + 指引标识，只显示与抛事件 | `IUIService` 实例化 |
| `QuestHudPresenter` | 入口点：`BootCompletedEvent` 后开 HUD 常驻并实例化世界标记；订阅四个事件刷新文字；每帧摆标记 / 算指引；对白中隐藏 | 根作用域入口点 |
| `QuestPanelView` | `UIView`（Panel 层）：列表 + 详情 + 追踪按钮，只显示与抛事件 | `IUIService` 实例化 |
| `QuestPanelController` | 开关面板会话：持世界暂停令牌 + 关 Gameplay 图；把面板事件转成 `service.Track/Untrack` | 根作用域单例 |
| `QuestConfig` | SO：指引留白 / 悬浮偏移 / 距离刷新间隔 / 标记抬升与高度 / 标记预制体地址 / HUD 与面板固定文案 | `Data/Quest/QuestConfig.asset` |
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
表现：QuestHudPresenter.Tick → binder.TryResolveTarget（得 Position 测距 / Anchor 标记）→ marker.Show(Anchor) 常驻摆位
      → SceneCamera.WorldToViewportPoint(Anchor) → QuestGuidanceMath.Solve
      → 屏内：hud.HideGuidance，只留世界标记；屏外：hud.SetGuidance 贴边箭头 + 距离，标记仍留在目标处（被相机裁掉）；
      对白中两者都隐藏；HUD 点击 → QuestPanelController.OpenAsync → SetList/SetDetail
```

## 依赖方向

`Game.Quest → Game.Core`（UI / Save / Config / Timing / Input / Events / Telemetry / Boot / `Simulation.GameMath`），
`Game.Quest → Game.Dialogue`（只用 `DialogueService` 事件与 `IsRunning`、`DialogueSceneBinder.Actor/Bound`、
`DialogueInteractable.DialogueId/transform`）。`Game.Dialogue`、`Game.Narrative`、`Game.Core` 不认识 `Game.Quest`。

## 为什么这样设计（源码读不出来的部分）

- **`QuestRules` 不持有存档分区引用**：`ISaveService.Commit` 会整体替换分区实例，长期持有旧引用会把进度写进一份没人读的对象；
  `QuestService.Flush` 每次操作后重新 `saves.Get<QuestSaveData>()` 再 `CaptureInto`（`QuestService.cs:189`）。
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
- **`QuestPanelView.OnClosed`**：面板可能被 `IUIService.CloseTopAsync` 等外部路径关掉（不经过
  `QuestPanelController.CloseAsync`），这个事件让 Controller 仍能收尾暂停令牌与恢复输入图；Controller 自己关时
  先摘掉这个监听，避免同一次关闭收尾两次（`PRP/quest-system/prp.md` 第 7 节）。
- **屏内不用屏幕空间指引标识，改用世界空间头顶标记**：一是要跟 NPC 头顶气泡（对话模块）表现语言一致，玩家已经认
  「头顶浮标记」这个语言（参考明日方舟的目标 / 敌人头顶标）；二是纸片角色体积大，固定像素偏移的屏幕空间标识在
  近距离时会被脸挡住或叠脸上，世界空间标记锚定在头顶碰撞体顶部就没有这问题（`QuestSceneBinder.NpcTarget`）。

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动或报错：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `QuestInstaller`（排在 `DialogueInstaller` 之后），**Config** 拖 `Data/Quest/QuestConfig.asset` | 没挂：解析不到 `QuestService`；没拖：记 Error 并用默认值顶上 |
| 预制体地址 | Addressables（UI 组）`QuestHudView` → `Prefabs/UI/QuestHudView.prefab`；`QuestPanelView` → `Prefabs/UI/QuestPanelView.prefab`。**地址等于类名** | `ui.OpenAsync<T>()` 找不到预制体 |
| 目标标记预制体 | Addressables（UI 组）`QuestTargetMarker` → `Prefabs/World/QuestTargetMarker.prefab`（根 `QuestTargetMarker`，子 `Icon`：SpriteRenderer + `CameraBillboard`） | 找不到 / 根上无组件：画面内标记不显示，记 Error 并埋 `marker_missing`；画面外箭头不受影响 |
| `QuestConfig` 标记字段 | `MarkerLift`(0.3)、`LocationMarkerHeight`(1.5)、`TargetMarkerAddress`("QuestTargetMarker") 需与预制体地址一致 | 地址对不上：走上一行的降级；高度 / 抬升值用默认值不影响编译 |
| 场景地点 | 场景里 `QuestLocation` 的 `locationKey` 要与表里 `ReachLocation` 目标的 `key`（`location` 留空时兜底用 `key`）对应 | 找不到地点：驱动器不判定、指引不显示，`QuestSceneBinder` 记 Warn |
| 玩家标记 | 玩家根挂 `DialogueInteractionActor`（复用对话模块） | 无到达判定、无指引；`QuestObjectiveDriver.Tick` / `QuestHudPresenter.Tick` 直接返回 |
| 入口 | 从 Boot → 标题「开始」进场景才有任务服务 | 直接 Play 玩法场景：服务未初始化，`IsReady == false`，查询返回空、上报被忽略 |

示例接线：`Assets/Scenes/SampleScene.unity` 的 `QuestLocation`（`camp`、`lookout`）；`Scenes/Verify/Quest.unity` 独立验证场景。

## 内容表（Luban）

`Tables/Defines/quest.xml`（module `quest`）+ `Tables/Data/quest/<id>.json`（一任务一文件，字段不许缺省）。
生成物：`Core/Config/Generated/quest/`（`cfg.quest.*`）、`Data/Config/quest_tbquest.bytes`。内容非法（id 重复、
目标为空、前置成环、TalkTo 键非整数、ReachLocation 键为空、TalkTo 对话不存在）在首次访问 `QuestCatalog.Content`
时抛 `ArgumentException`，不落缓存；`QuestService.InitializeAsync` 捕获后记 Error，服务保持 `IsReady == false`。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Tests/EditMode/Quest/QuestRulesTests.cs`（19） | 激活 / 单主线 / 当前目标推进 / 完成连锁 / 追踪切换 |
| EditMode | `.../QuestContentTests.cs`（12） | 内容校验（id / 目标 / 环 / 键格式） |
| EditMode | `.../QuestCatalogTests.cs`（5） | 真实表构造、TalkTo 对话缺失抛错 |
| EditMode | `.../QuestSaveDataTests.cs`（5） | 存档序列化往返、未知 id 跳过 |
| EditMode | `.../QuestGuidanceMathTests.cs`（9） | 屏内 / 屏外贴边 / 相机后翻转 / 距离取整 |
| Showcase | `Tests/Showcase/Quest/QuestShowcase.cs` | HUD 指引、对白与地点联动、面板暂停、追踪切换（肉眼验收） |
| 验证场景 | `Scenes/Verify/Quest.unity` | 独立验证场景（透视相机、`player`、Elder/Traveler、两个 `QuestLocation`） |

跑 `/unity-test EditMode Quest`；视觉验收跑 `/verify-module Quest`（编辑器须打开，本轮尚未跑）。

## 已知约束 / 未做

- 本期没有读档入口：`InitializeAsync` 里 `Restore` 的是内存默认分区，等 `GameSession` 接读档时同一入口生效。
- `Report` 键统一为字符串：TalkTo 的 int → string 只在对白结束时发生一次，不在每帧路径上。
- 指引依赖 `Camera.main`：相机为空时 `HideGuidance` 并只埋一次 Warn。

## 禁止事项

- 不要绕过 `QuestRules.Report` 直接改 `QuestProgress`（属性 setter 是 `internal`）。
- 不要在 `QuestHudView` / `QuestPanelView` 里注入服务或持有状态：只显示与抛事件。
- 不要长期持有 `QuestSaveData` 引用：每次操作重新 `saves.Get<QuestSaveData>()`。
- 不要在 `Game.Core` 里加任务名词；`GameplayInstaller.InstallEvents` 本身不含任务概念。
