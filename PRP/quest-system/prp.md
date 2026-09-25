# PRP: 任务系统（主线 / 支线 · 追踪 · 指引）

> 日期：2026-09-25。输入 [`prd.md`](prd.md)（7 条待确认项用户已「按默认走」）。模块 `Game.Quest`，目录 `Assets/_Project/Scripts/Runtime/Quest/`，首次落地：无既有三件套，完成后 `/generate-doc quest`。
> 文档与源码不符时以源码为准；执行中的修订记在第 7 节。

## 1. 上下文快照

### 1.1 既有模块的关键约束（只摘要）

| 来源 | 本次要遵守的点 |
| --- | --- |
| Dialogue external-api | `DialogueService.OnEnded(DialogueEndedEvent{DialogueId, Outcome, Skipped})` 是 C# `event`，订阅方自己退订；`DialogueInteractable.DialogueId`（0 = 无树）、`transform` 可作指引目标；`DialogueService.IsRunning` 判对白进行中。不要自己开对话面板，不要各自改 `Time.timeScale`（走 `IWorldPauseService.Acquire(this)`） |
| Dialogue module-guide | `DialogueSceneBinder`（`AsSelf` 注册）：`Bound` 是场景里全部可交互 NPC、`Actor.Anchor` 是玩家测距原点，只在 `sceneLoaded` 时扫描；HUD 常驻件在 `BootCompletedEvent` 之后才 `OpenAsync` 一次，之后只切 `root` 显隐（`DialogueInteractionFocus.cs`）；View 只显示与抛事件，不注入服务；`Validate()` 逐字段点名漏接 |
| Dialogue 内容表 | 嵌套结构走 `Tables/Defines/<module>.xml` + `Tables/Data/<dir>/<id>.json`（一文件一记录）；`luban.conf` 的 `schemaFiles` 已含整个 `Defines` 目录，新 xml 自动纳入；JSON **每个字段都要写**；生成 `powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1`，代码落 `Core/Config/Generated/<module>/`，数据落 `Data/Config/<module>_<table>.bytes`（Addressables `Config` 组是文件夹条目，新 `.bytes` 自动带 `config` 标签） |
| Core UI | 预制体地址 = 类名（UI 组）；`UILayer.Hud` 不进栈、常驻切显隐；`UILayer.Panel` 走 `OpenAsync / CloseAsync`，`IsFullScreen` 默认 true；淡入淡出用 unscaled 时间 |
| Core Save | `ISaveService.Get<T>()` 首次 `new`；`Commit` 会**整体替换分区实例**——规则层不能长期持有分区引用，要「每次取、写回」 |
| Core Boot | `IGameService` 按注册顺序串行初始化，玩法注册器在 Configure 末尾 → 玩法服务天然排在 Config / Save 之后；`Install` 只 Register 不 Resolve；`GameplayInstaller.Install(IContainerBuilder)` 拿不到 `MessagePipeOptions`（Dialogue 因此改用 C# event） |
| Core Events | 事件 `readonly struct XxxEvent`，MessagePipe `IPublisher / ISubscriber`，订阅句柄进 `DisposableBag` |
| IsometricExploration | SampleScene 相机透视（FOV 28、俯角 38°），`Main Camera` 带 `MainCamera` tag，`FallbackCamera` 让位后 `Camera.main` 就是它；玩家根名 `player`，已挂 `DialogueInteractionActor`；NPC `Npc_Elder`(1001) / `Npc_Traveler`(1002) / `Npc_Villager`(无树) |
| Showcase 框架 | 复制 `ShowcaseSelfTest.cs` 起步；`LoadBootScene = true` 时覆写 `WaitForBootReady`；多条用例要 `DestroyBootScope`（照 `DialogueShowcase.cs`）；跨帧等待用 `Check(timeout:)`；`ResolveService<T>` 从真实容器取服务 |

### 1.2 本次必须规避的 pitfalls

| 条目 | 对本次的含义 |
| --- | --- |
| 往 `.cs` 里写含正则 / 反斜杠的代码不能走 Bash heredoc | 所有 `.cs`、JSON、XML 一律用 Write / Edit 工具写 |
| Luban JSON 字段不能缺省、一文件多记录要写 `*@` | 任务 JSON 每个字段显式写（空数组、空串、`count: 1`）；一任务一文件放 `Tables/Data/quest/` |
| 测试自己把依赖装上了，接线缺口全程不报 | Showcase 必须从真实容器 `ResolveService<QuestService>()`，并检查 HUD 是容器开出来的 |
| EditMode 用例总数不涨其实是域没重载 | 跑测试前 `refresh_unity(force, compile="request")`，核对用例总数增加 |
| 一条用例遗留的未观察 UniTask 异常会砸中别的用例 | 异步 `Forget()` 都带异常处理 |
| 只给 InputSystemUIInputModule 赋 actionsAsset UI 收不到点击 | 不自建 EventSystem，用 `UIService` 的 |
| 编辑器停在未保存空场景进 Play 什么都不发生 | MCP 验证前 `manage_scene(get_active)` 确认是 Boot |
| 给共享 MonoBehaviour 加新序列化字段默认值静默改别的场景 | 本次不改任何既有 MonoBehaviour 的序列化字段 |
| 中文显示成 `□` | 新预制体 TMP 字体用现有中文 fallback，不改 TMP Settings |
| `.meta` 没提交引用全断 | 新文件等 Unity 刷新出 `.meta` 再进审查清单 |
| 两个会话共用工作区 | 只动本轮文件；改场景前读 `mcpforunity://editor/state` |

### 1.3 适用规则

`project-root.md`（目录、asmdef 方向、复用 → 扩展 → 新建）、`csharp-code.md`（`[SerializeField] private`、`== null`、每帧路径无 Find / Log / 分配、`Config / Settings / Data / Info` 后缀各管一事）、`unity-assets.md`（场景 / 预制体走 MCP、SO 只读、`.meta` 成对）、`unity-tests.md`（EditMode 优先、`<行为>_<条件>_<期望>`）、`module-verify.md`（Showcase 写法）、`model-routing.md`（写 tasks 时用）。

## 2. 架构

```text
内容：Tables/Defines/quest.xml + Tables/Data/quest/<id>.json ─Luban→ cfg.quest.TbQuest + quest_tbquest.bytes
        ─IConfigService.Tables→ QuestCatalog ─翻译 + 校验→ QuestContent（QuestDefinition / QuestObjectiveDefinition）

进度：QuestService（根作用域单例，IGameService）
  ├─ InitializeAsync：Catalog 建索引 → rules.Restore(saves.Get<QuestSaveData>()) → rules.ActivateAvailable()
  ├─ Report(kind, key, amount) / Track(id) / Untrack()          ← 其他模块 / 场景侧 / 面板 调
  ├─ QuestRules（纯 C#）：激活 / 推进 / 完成 / 排序 / 追踪；C# event 抛变化
  ├─ 每次变化后 rules.CaptureInto(saves.Get<QuestSaveData>())   （分区按次取，不长期持有）
  └─ 变化 → IPublisher<QuestActivatedEvent / QuestObjectiveProgressedEvent / QuestCompletedEvent / QuestTrackingChangedEvent>

场景侧（根作用域入口点）
  QuestSceneBinder     sceneLoaded 扫 QuestLocation 登记；缓存 Camera.main；玩家原点复用 DialogueSceneBinder.Actor
  QuestObjectiveDriver 订阅 DialogueService.OnEnded → Report(TalkTo, id)；每帧对「当前目标 = 到达地点」的任务测距 → Report(ReachLocation, key)
  QuestHudPresenter    BootCompleted 后开 QuestHudView（Hud 层常驻）；订阅四个事件刷新文字；每帧算指引（QuestGuidanceMath）；对白中隐藏；点任务栏 → QuestPanelController.OpenAsync
  QuestPanelController 开 / 关 QuestPanelView（Panel 层）；持世界暂停令牌 + 关 Gameplay 图；把面板事件转成 service.Track / Untrack

表现（UIView，由 Addressables 实例化，不注入服务，只显示与抛事件）
  QuestHudView   左侧任务栏（标题 + 当前目标，整条可点）+ 指引标识（图标 + 箭头 + 距离，屏幕空间）
  QuestPanelView 列表（主线置顶）+ 详情（标题 / 类型 / 描述 / 目标清单）+ 追踪按钮 + 关闭
```

依赖方向：`Game.Quest → Game.Core`（UI / Save / Config / Timing / Input / Events / Telemetry / Boot），`Game.Quest → Game.Dialogue`（只用 `DialogueService` 事件与 `IsRunning`、`DialogueSceneBinder.Actor / Bound`、`DialogueInteractable.DialogueId / transform`）。`Game.Dialogue`、`Game.Narrative`、`Game.Core` 不认识 `Game.Quest`。Core 的唯一改动是 3.5 的注册器重载，不含任务名词。

### 复用 → 扩展 → 新建

| 能力 | 复用 / 扩展 / 新建 | 理由 |
| --- | --- | --- |
| 玩家位置 | **复用** `DialogueSceneBinder.Actor.Anchor` | SampleScene 玩家根已挂标记；再加一个空标记组件是重复登记。将来要解耦再在 Core 抽 `IPlayerAnchor` |
| NPC 位置（TalkTo 指引） | **复用** `DialogueSceneBinder.Bound` 按 `DialogueId` 查 | 扫场景只该有一处 |
| 世界暂停、输入图切换 | **复用** `IWorldPauseService`、`IInputService` | 与对白同一套，避免互相覆盖 |
| 模块事件 | **扩展** `GameplayInstaller` 加 `InstallEvents(builder, options)` 虚方法（Core 两个文件） | 不扩展就只能沿用 Dialogue 的 C# event 例外，等于让 `EventConventions` 第 1 条继续失效；重载对既有 Installer 零影响 |
| HUD 常驻 / 面板 / 存档分区 / 内容目录 | **新建**（照 Dialogue 同名角色的形状） | 职责与对白不同，塞进 Dialogue 说不通 |
| 场景到达点、指引数学 | **新建** | 框架里没有屏幕边缘指示与到达判定 |

## 3. 契约（并行对齐点）

### 3.1 内容表（`Tables/Defines/quest.xml`，module `quest`；数据 `Tables/Data/quest/<id>.json`）

```xml
<enum name="QuestKind">      Main=0  Side=1
<enum name="ObjectiveKind">  TalkTo=0  ReachLocation=1  Counter=2
<bean name="Objective">  text:string  kind:ObjectiveKind  key:string  count:int  location:string
<bean name="Quest">      id:int  kind:QuestKind  title:string  description:string  prerequisites:list,int  objectives:list,Objective
<table name="TbQuest" value="Quest" index="id" input="quest" group="c"/>
```

- `key`：TalkTo 填对话树编号（字符串形式，如 `"1001"`）；ReachLocation 填场景地点编号；Counter 填自定义键。
- `count`：需要次数，JSON 必须写（`1` 起）；`location`：指引地点编号，空串 = ReachLocation 用自己的 `key`、TalkTo 指向该对话的 NPC、Counter 无指引。
- 生成物：`Core/Config/Generated/quest/`（`cfg.quest.*`，代码里写 `global::cfg.quest.`）、`Data/Config/quest_tbquest.bytes`；`Tables.cs` 自动多一个 `TbQuest`。
- 示例数据（SampleScene 用，也是 Showcase 的内容）：`1001` 主线「找到落脚处」目标 ①「与长者谈谈」TalkTo `1001` ②「前往营地」ReachLocation `camp`；`1002` 主线「与旅人叙旧」前置 `[1001]`，目标「和旅人聊聊」TalkTo `1002`；`2001` 支线「观察神秘生物」前置 `[]`，目标「前往瞭望点」ReachLocation `lookout`。

### 3.2 内容模型（`QuestContent.cs` + `QuestCatalog.cs`，新）

- `QuestKind` / `QuestObjectiveKind`：与表同名同序的 C# 枚举（`QuestKind.cs`、`QuestObjectiveKind.cs`，一文件一个）。
- `QuestDefinition`（sealed class，只读属性）：`Id`、`Kind`、`Title`、`Description`、`IReadOnlyList<int> Prerequisites`、`IReadOnlyList<QuestObjectiveDefinition> Objectives`。
- `QuestObjectiveDefinition`（readonly struct）：`Text`、`Kind`、`Key`、`RequiredCount`（`< 1` 按 1）、`LocationKey`（空 = 无）；`Matches(kind, key)`。
- `QuestContent`：`IReadOnlyList<QuestDefinition> All`、`TryGet(id, out def)`；**构造时校验**并抛 `ArgumentException`（消息带任务 id）：id 唯一；目标非空且每个 `Text` 非空；`Kind` 合法；TalkTo 的 `Key` 可解析为 int；ReachLocation 的 `Key` 非空；前置引用存在、无自环与环（DFS）。测试可直接 `new`。
- `QuestCatalog`（根作用域单例，惰性）：`Content`（首次访问读 `config.Tables.TbQuest`）；额外校验 TalkTo 的对话编号存在于 `Tables.TbDialogue`，缺失抛 `ArgumentException`。不在构造函数里读表。

### 3.3 规则（`QuestRules.cs`，纯 C#，新）

状态：`Dictionary<int, QuestProgress>`（`QuestProgress.cs`：`Definition`、`State`（`QuestState.cs`：`Inactive / InProgress / Completed`）、`ObjectiveIndex`、`Count`、`AcceptOrder`）；`TrackedId`（0 = 无）；`nextAcceptOrder`。

| 成员 | 语义 |
| --- | --- |
| `QuestRules(QuestContent content, ITelemetryScope telemetry)` | 全部任务建 `Inactive` 进度 |
| `ActivateAvailable()` | 把「Inactive 且前置全部 Completed」的任务激活；**主线同一时刻最多一个 InProgress**：多条主线同时可激活时只激活 id 最小的；激活顺序给 `AcceptOrder`；首次初始化（见「追踪」）时追踪当前主线 |
| `Report(QuestObjectiveKind kind, string key, int amount = 1)` → `int` | 对每个 InProgress 任务，只看**当前目标**（`Objectives[ObjectiveIndex]`）匹配则 `Count += amount`；满 `RequiredCount` → 目标完成、`ObjectiveIndex++`、`Count = 0`；越过末尾 → 任务 `Completed` → `ActivateAvailable()` → 若 `TrackedId` 是它 → 切到当前主线（无则 0）。返回被推进的目标数；`amount <= 0` 忽略 |
| `Track(int id)` / `Untrack()` | `Track` 要求 InProgress，否则返回 false；两者只在值变化时抛事件 |
| `CurrentMainId` | 唯一 InProgress 的主线 id，无则 0 |
| `GetOrdered(List<QuestProgress> buffer)` | 清空后填入：当前主线（若有）→ 支线按 `AcceptOrder` 升序；只含 InProgress |
| `TryGet(id, out QuestProgress)` / `InProgress`（`IReadOnlyList<QuestProgress>`，无分配缓存，变化时重建） | 面板与驱动读 |
| `CaptureInto(QuestSaveData)` / `Restore(QuestSaveData)` | 写回 / 恢复：`Restore` 跳过表里不存在的 id（埋 Warn）、追踪的任务不再 InProgress 时置 0 |
| 事件 | `OnActivated(int)`、`OnProgressed(int questId, int objectiveIndex, int count, int required, bool objectiveCompleted)`、`OnCompleted(int)`、`OnTrackingChanged(int)` |

追踪的默认规则（对应 PRD A3）：首次初始化（存档 `Initialized == false`）时追踪当前主线；此后**只在「被追踪的任务完成」时自动切换**到当前主线；玩家 `Untrack` 后保持无追踪，新主线激活不会抢回。

### 3.4 存档（`QuestSaveData.cs` + `QuestProgressData.cs`，新，纯 DTO）

`QuestSaveData : ISaveData`：`Version => 1`、`Migrate` 空实现；属性 `bool Initialized`、`int TrackedId`、`int NextAcceptOrder`、`List<QuestProgressData> Quests`。`QuestProgressData`：`Id`、`State`（int）、`ObjectiveIndex`、`Count`、`AcceptOrder`。
`QuestService` 每次规则变化后 `rules.CaptureInto(saves.Get<QuestSaveData>())`（按次取分区，不缓存实例）。本期没有读档入口（工程里还没有任何 `LoadAsync` 调用），`InitializeAsync` 里 `Restore` 的是内存里的默认分区，等 GameSession 接读档时同一入口生效。

### 3.5 事件与 Core 扩展（`Core/Boot/GameplayInstaller.cs`、`Core/Boot/GameLifetimeScope.cs`，改；四个事件文件，新）

```csharp
// GameplayInstaller 新增（默认空实现，既有 Installer 不用动）
public virtual void InstallEvents(IContainerBuilder builder, MessagePipeOptions options) { }
// GameLifetimeScope.InstallGameplay(builder, options)：对每个 installer 先 InstallEvents 再 Install
```

事件（`readonly struct`，一文件一个，`Game.Quest`）：`QuestActivatedEvent{QuestId}`、`QuestObjectiveProgressedEvent{QuestId, ObjectiveIndex, Count, Required, ObjectiveCompleted}`、`QuestCompletedEvent{QuestId}`、`QuestTrackingChangedEvent{QuestId}`（0 = 无追踪）。由 `QuestInstaller.InstallEvents` 注册 broker，`QuestService` 经 `IPublisher<T>` 发布；模块内订阅走 `ISubscriber<T>` + `DisposableBag`。`EventConventions.cs` 第 4 条补一句「模块事件在自己 Installer 的 `InstallEvents` 里注册」。

### 3.6 服务（`QuestService.cs`，新，根作用域单例 + `IGameService`）

构造：`QuestCatalog`、`ISaveService`、四个 `IPublisher<T>`、`ITelemetryScope("quest")`。Catalog 惰性，所以 Rules 在 `InitializeAsync` 里 `new`，不在容器构建期。

| 成员 | 说明 |
| --- | --- |
| `InitializeAsync(ct)` | 建 Rules（Catalog 非法则记 Error 并让服务处于 `IsReady == false`，不炸启动）→ `Restore` → `ActivateAvailable` → 写回分区 → 埋 `initialized` |
| `bool IsReady` | Rules 建好才 true；未就绪时下面方法记 Warn 并忽略 |
| `int Report(QuestObjectiveKind kind, string key, int amount = 1)` | 转发规则，变化后写回分区并发布事件 |
| `bool Track(int id)` / `void Untrack()` / `int TrackedId` / `int CurrentMainId` | 同规则 |
| `bool TryGetTracked(out QuestProgress)`、`void GetOrdered(List<QuestProgress>)`、`bool TryGet(id, out QuestProgress)`、`IReadOnlyList<QuestProgress> InProgress`、`QuestContent Content` | 只读查询 |

Rules 的 C# event → Service 里订阅一次，转成 MessagePipe 发布（不在事件回调里再同步发布同类事件：Report 内部的级联激活先收集、方法末尾统一发布）。

### 3.7 场景侧（新）

- `QuestLocation`（MonoBehaviour，场景组件）：`[SerializeField] string locationKey; float radius = 1.5f`；只读 `Key` / `Radius` / `Position`；`#if UNITY_EDITOR` 画 Gizmos 圆。
- `QuestSceneBinder`（`IStartable, IDisposable`，`AsSelf`）：照 `DialogueSceneBinder` 扫描；`IReadOnlyList<QuestLocation> Locations`、`bool TryGetLocation(string key, out QuestLocation)`、`Camera SceneCamera`（`sceneLoaded` / `sceneUnloaded` 时重取 `Camera.main`，不每帧）、`Transform PlayerAnchor => dialogueBinder.Actor == null ? null : dialogueBinder.Actor.Anchor`。
- `QuestObjectiveDriver`（`IStartable, ITickable, IDisposable`）：`Start` 订阅 `DialogueService.OnEnded` → `service.Report(TalkTo, e.DialogueId.ToString())`（跳过也算，Q7）——转字符串只在对白结束时发生，不在每帧；`Tick`：`PlayerAnchor` 为空或 `!service.IsReady` 直接返回；遍历 `service.InProgress`，当前目标是 ReachLocation 且 `TryGetLocation` 命中且 `sqrMagnitude <= radius²` → `Report(ReachLocation, key)`。无分配。
- 指引目标解析（放 `QuestSceneBinder.TryResolveTarget(in QuestObjectiveDefinition, out Vector3)`）：`LocationKey` 非空 → 地点；否则 ReachLocation → 用 `Key` 查地点；否则 TalkTo → `dialogueBinder.Bound` 里 `DialogueId == key` 的 NPC `transform.position`；都没有 → false。

### 3.8 表现（新）

- `QuestGuidanceMath`（静态纯函数，留在 `Game.Quest`，不进 Core）：
  `static QuestGuidance Solve(Vector3 viewportPoint, Vector2 canvasSize, float edgeMargin, float hoverOffset)`；`QuestGuidance`（readonly struct）：`OnScreen`、`AnchoredPosition`（画布中心为原点）、`ArrowAngleDeg`（0 = 朝上，逆时针）、`ShowArrow`。规则：`z < 0`（目标在相机后）把 `(x, y)` 关于 `(0.5, 0.5)` 翻转并视为屏外；屏内（`0 ≤ x, y ≤ 1` 且 `z ≥ 0`）→ 位置 = 目标投影 + `(0, hoverOffset)`，无箭头；屏外 → 取画布中心到该点的射线与「内缩 `edgeMargin` 的矩形」的交点，箭头指向该方向。距离 `static int DistanceMeters(Vector3 a, Vector3 b)` = 直线距离四舍五入。
- `QuestHudView : UIView`（`Layer = Hud`，`IsFullScreen = false`）：字段 `root`、`button`（整条任务栏）、`title`、`objective`、`guidanceRoot`（RectTransform）、`guidanceArrow`（RectTransform，旋转）、`distanceLabel`；`OnOpenAsync` 里 `Validate()`；API `SetTracked(string title, string objective)`、`SetUntracked(string placeholder)`、`SetGuidance(in QuestGuidance g, string distance)`、`HideGuidance()`、`SetVisible(bool)`；事件 `OnClicked`。
- `QuestPanelView : UIView`（`Layer = Panel`）：字段 `listRoot`、`itemTemplate`（子物体 `Title`、`Kind`、`Highlight`，运行时隐藏模板）、`emptyLabel`、`detailRoot`、`detailTitle`、`detailKind`、`detailDescription`、`objectiveRoot`、`objectiveTemplate`（子物体 `Mark`、`Text`）、`trackButton`、`trackLabel`、`closeButton`；API `SetList(IReadOnlyList<QuestProgress> ordered, int selectedId, string mainLabel, string sideLabel)`、`SetDetail(QuestProgress p, bool tracked, ...)`（null = 清空详情并禁用追踪按钮）、事件 `OnQuestSelected(int)`、`OnTrackToggled`、`OnClose`。列表项复用池（按需实例化模板、多余的隐藏），不每次销毁重建。
- `QuestHudPresenter`（`IStartable, ITickable, IDisposable`）：`BootCompletedEvent` 后 `OpenAsync<QuestHudView>()` 一次（照 `DialogueInteractionFocus.OpenHudAsync` 含 disposed 兜底）；订阅四个任务事件与 `DialogueService.OnStarted / OnEnded` → 刷新文字 / 显隐；`Tick`：HUD 未开或对白中或无追踪 → `HideGuidance`；否则 `TryResolveTarget` + `SceneCamera.WorldToViewportPoint` + `Solve` → `SetGuidance`；距离文本按 `QuestConfig.distanceRefreshInterval` 节流（unscaled），且只在整数米变化时才改字符串（避免每帧分配）；HUD 点击 → `panelController.OpenAsync()`。
- `QuestPanelController`（根作用域单例，`IDisposable`）：`OpenAsync()`（已开则忽略）→ `OpenAsync<QuestPanelView>()` → `pause.Acquire(this)` + 记录并 `DisableMap("Gameplay")`（`input.Actions == null` 时跳过，照 `DialogueService`）→ `SetList` / `SetDetail`（默认选中追踪的，否则第一项）；`OnQuestSelected` → `SetDetail`；`OnTrackToggled` → `service.Track / Untrack` → 刷新；`OnClose` → `CloseAsync` → 恢复输入图 → 释放暂停。面板开着时订阅任务事件刷新列表。

### 3.9 配置（`QuestConfig.cs` → `Data/Quest/QuestConfig.asset`）

`[CreateAssetMenu(menuName = "21Days/Quest/QuestConfig")]`：`edgeMargin`（48）、`hoverOffset`（80）、`distanceRefreshInterval`（0.2）、`untrackedLabel`（「未追踪任务」）、`mainKindLabel`（「主线」）、`sideKindLabel`（「支线」）。只读属性；`Installer` 缺失时 Error + 默认顶上（照 `DialogueInstaller.ResolveConfig`）。

### 3.10 注册（`QuestInstaller : GameplayInstaller`，新，挂 Boot 的 GameBootstrap）

`InstallEvents`：四个 `RegisterMessageBroker<T>(options)`。`Install`：`RegisterInstance(config)`、`QuestCatalog`、`QuestService`（工厂，`.As<IGameService>().AsSelf()`）、`QuestPanelController`、`RegisterEntryPoint<QuestSceneBinder>().AsSelf()`、`RegisterEntryPoint<QuestObjectiveDriver>()`、`RegisterEntryPoint<QuestHudPresenter>()`。埋点模块名 `quest`。

## 4. 资产与场景（全部经 Unity MCP 创建，改前读 `mcpforunity://editor/state`）

- `Prefabs/UI/QuestHudView.prefab`（地址 `QuestHudView`，UI 组）：根 `CanvasGroup` + 安全区内左上锚点；`Root`（整条按钮，半透明深色条）内 `Title`（TMP，含「‹」图标位）、`Objective`（TMP，前缀「★」）；`Guidance`（RectTransform，锚点中心）内 `Icon`（「!」图）、`Arrow`（三角）、`Distance`（TMP）。
- `Prefabs/UI/QuestPanelView.prefab`（地址 `QuestPanelView`）：全屏半透明底；左列 `List`（VerticalLayoutGroup + 隐藏 `ItemTemplate`：`Title` / `Kind` / `Highlight`）与 `EmptyLabel`；右侧 `Detail`（`Title` / `Kind` / `Description` / `Objectives`（隐藏 `ObjectiveTemplate`：`Mark` / `Text`）/ `TrackButton`(`TrackLabel`)）；右上 `CloseButton`。
- `Data/Quest/QuestConfig.asset`。
- `Scenes/Boot.unity`：`GameBootstrap` 加 `QuestInstaller` 并拖 Config（改前确认 Boot.unity 干净）。
- `Assets/Scenes/SampleScene.unity`：加 `QuestLocation` 两个（`camp` 在营地附近、`lookout` 在远离出生点的高处），半径 2。
- `Scenes/Verify/Quest.unity`：透视相机（照 SampleScene 参数，`MainCamera` tag、`PhysicsRaycaster`）、`player`（`DialogueInteractionActor`，Showcase 里直接改坐标）、`Elder`（`DialogueInteractable` 1001 + `BoxCollider`）、`Traveler`（1002）、`QuestLocation` `camp`（画面内）与 `lookout`（画面外 20 米）。
- 表：`Tables/Defines/quest.xml`、`Tables/Data/quest/{1001,1002,2001}.json`，跑生成，生成物一起进审查清单。

## 5. 验证清单（对应 PRD A1–A9）

| PRD | 验证 |
| --- | --- |
| A1 规则 | `QuestRulesTests`：激活 / 单主线 / 当前目标累计与推进 / 完成级联 / 非当前目标不计 |
| A2 排序 | `QuestRulesTests.GetOrdered_*` |
| A3 追踪 | `QuestRulesTests.Track_*`：默认、切换、完成回主线、无主线为 0、Untrack 后不抢回 |
| A4 内容 | `QuestCatalogTests`：真实 `TbQuest` 可构造；合成非法内容各抛（重复 id、环、TalkTo 非数字、ReachLocation 空 key、对话不存在） |
| A5 存档 | `QuestSaveDataTests`：`CaptureInto` → Newtonsoft 序列化 → 反序列化 → `Restore` 一致；空分区 → 全新开始；未知 id 跳过 |
| A6 指引 | `QuestGuidanceMathTests`：屏内、四边贴边、相机后翻转、距离取整 |
| A7 联动 | `QuestShowcase`：对白结束 → 目标完成；进入地点 → 目标完成；`Report(Counter)` 累计 |
| A8 表现 | `QuestShowcase`：任务栏文字、开面板、主线置顶、追踪按钮切换、指引悬浮 ↔ 贴边、取消追踪占位、支线完成回主线；末尾 `Snapshot` |
| A9 暂停 | `QuestShowcase` 检查点：面板开着时 `IWorldPauseService.IsPaused` 且 Gameplay 图关；关后恢复 |
| 通用 | 控制台零编译错误；`project-lint` 零违规；EditMode 全量通过且用例总数增加；`code-reviewer` 无 BLOCK；`/generate-doc quest`、`catalog.md`、`modules.json`；`gc_scan.py` 无失效引用；`/review-change` 停下等审 |

## 6. 风险与默认决策

- **Core 改动只有 `InstallEvents` 重载**：零任务名词；既有 Installer 不受影响。若审查不接受，退路是照 Dialogue 用 C# event（Service 暴露四个 `event`），Presenter / Controller 改订阅方式，规则与 View 不动。
- **指引依赖 `Camera.main`**：`sceneLoaded` 时取；Verify 场景与 SampleScene 相机都带 `MainCamera` tag。相机为空时 `HideGuidance` 并埋 Warn 一次，不每帧。
- **玩家原点复用对话模块的标记**：没有 `DialogueInteractionActor` 的场景无到达判定与指引（记 Warn 一次）；将来抽 `IPlayerAnchor` 时只改 `QuestSceneBinder.PlayerAnchor` 一处。
- **`Report` 键为字符串**：TalkTo 的 int → string 只在对白结束时发生一次；大量 Counter 上报走同一入口，热点路径没有拼接。
- 回滚：改动仅在工作区，按路径用 git 还原；MCP 建的场景对象与预制体用 MCP 删回；生成物随表一起回退。

## 7. 执行中的修订（以此为准）

- **3.10 注册**：容器里没有注册 `ITelemetryScope`，凡构造函数带它的类（`QuestCatalog`、`QuestPanelController`、`QuestHudPresenter`、`QuestObjectiveDriver`）一律工厂注册并传 `ITelemetryService.Scope("quest")`；`QuestService` 用一条链 `.AsSelf().As<IGameService>()`（同实现类型分两条注册会撞键，见 `GameLifetimeScope.RegisterSimulationDriver`）。
- **3.8 指引数学**：Runtime 有「重放确定性」lint 禁用 `Mathf.` / `Math.`，`QuestGuidanceMath` 改用 `Game.Core.Simulation.GameMath`（`Atan2 / Min / Abs / Floor`），弧度转角度用本地常量，半数进位用 `(int)GameMath.Floor(d + 0.5f)`。
- **3.8 面板关闭**：`QuestPanelView` 多一个 `OnClosed` 事件（`OnCloseAsync` 里触发），供面板被 `IUIService.CloseTopAsync` 等外部路径关掉时，`QuestPanelController` 仍能释放暂停令牌与恢复输入图；Controller 自己关时先摘掉这个监听，避免收尾两次。
- **3.7 驱动器**：对话编号转字符串用 `InvariantCulture`；到达判定命中后当帧 `return`（`Report` 会重建 `InProgress` 缓存）。
- **5 A4 测试**：`Content_TalkToDialogueMissing_Throws` 用 `ByteBuf` 手写一条假任务替换 `quest_tbquest` 字节、其余表用真实生成物，验证「对话缺失 → 抛且不落缓存」；Luban 字段顺序变了该测试会在读假表时先失败。
- **3.7 指引解析（回放抓到）**：首版契约漏了「到达地点目标 `location` 为空时用自己的 `key` 指引」，Showcase 第 ① 条第 9 检查点失败（追踪支线后无指引）；已补到 `TryResolveTarget` 的解析顺序里，3.1 / 3.7 与模块文档同步改。
- **3.8 指引表现（视觉验收后改，用户选方案 1）**：画面内不再画屏幕空间标识——目标在画面内时只显示世界空间头顶标记（新 `QuestTargetMarker` 预制体，地址 `QuestTargetMarker`，NPC 锚在根 Collider 顶部 + `MarkerLift`，地点锚在位置 + `LocationMarkerHeight`），画面外才用 HUD 贴边箭头 + 距离；`TryResolveTarget` 改为输出 `QuestTarget{Position, Anchor}`。原因：与 NPC 头顶气泡一致（参考图），且固定像素偏移会把标识压在大体积纸片的脸上。`QuestPanelView` 的关闭按钮改为左上角深底「< 返回」（原「✕」字形工程字体没有，12% 透明底看不见）。
- **W1 结果**：Quest 的 EditMode 51 条全绿；全量 308 条只剩协作者遗留的 2 条失败（`ReplayFormatTests` 版本 2 对 3、`JsonSaveServiceTests` 迁移次数），与本轮无关。
