---
type: external-api
module: quest
layer: runtime
maturity: seed
---

# Quest 外部接口

> 别的模块要上报任务进度、查询任务状态时查这份。内部结构见 [`quest-module-guide.md`](quest-module-guide.md)。

## `Game.Quest.QuestService`（根作用域单例 + `IGameService`，构造注入即可）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `IsReady` | `bool IsReady { get; }` | 内容表校验通过且初始化完成才 true；未就绪时下列方法记 Warn / 静默返回默认值 |
| `Report` | `int Report(QuestObjectiveKind kind, string key, int amount = 1)` | 上报一次目标事实，返回本次被推进的目标数；`amount <= 0` 或 `key == null` 被忽略；未就绪返回 0 |
| `Track` | `bool Track(int id)` | 追踪一条进行中的任务；不存在 / 不在进行中 / 未就绪返回 `false` |
| `Untrack` | `void Untrack()` | 取消追踪；未就绪空操作 |
| `TrackedId` | `int TrackedId { get; }` | 当前追踪任务 id；0 = 无追踪或未就绪 |
| `CurrentMainId` | `int CurrentMainId { get; }` | 唯一进行中的主线 id；无或未就绪为 0 |
| `TryGetTracked` | `bool TryGetTracked(out QuestProgress progress)` | 取当前追踪任务的进度；无追踪或未就绪返回 `false` |
| `TryGet` | `bool TryGet(int id, out QuestProgress progress)` | 按 id 查进度（含未激活以外的所有已知任务） |
| `GetOrdered` | `void GetOrdered(List<QuestProgress> buffer)` | 清空后填入：当前主线（若有）→ 支线按接取序号升序；只含进行中；未就绪只清空 |
| `InProgress` | `IReadOnlyList<QuestProgress> InProgress { get; }` | 进行中任务，按接取序号升序；**内部缓存，`Report` 后会重建，不要跨上报持有遍历** |
| `Content` | `QuestContent Content { get; }` | 任务内容；未就绪时抛 `InvalidOperationException` |

`Report` 是其它模块上报进度的**唯一入口**：无论对话联动、场景到达点还是自定义计数，都调这一个方法，
不各自维护任务状态。键的语义由 `QuestObjectiveKind` 决定：`TalkTo` 传对话编号的字符串形式，
`ReachLocation` 传场景地点键，`Counter` 传调用方自定义键（同一模块内约定一致即可）。

## 四个事件（MessagePipe，`IPublisher<T>`/`ISubscriber<T>` 注入，一文件一个 `readonly struct`）

| 事件 | 字段 | 何时发布 |
| --- | --- | --- |
| `QuestActivatedEvent` | `int QuestId` | 任务从 Inactive 变 InProgress |
| `QuestObjectiveProgressedEvent` | `int QuestId`、`int ObjectiveIndex`（推进前的当前目标下标）、`int Count`（推进后累计）、`int Required`、`bool ObjectiveCompleted` | 当前目标被 `Report` 命中 |
| `QuestCompletedEvent` | `int QuestId` | 任务全部目标完成 |
| `QuestTrackingChangedEvent` | `int QuestId`（新追踪 id，0 = 无追踪） | 追踪目标变化 |

四个事件在同一次操作（初始化 / 上报 / 追踪）结束、存档分区写回**之后**统一按
`Activated → Progressed → Completed → TrackingChanged` 顺序发布，订阅者读到的一定是操作后的完整状态。
订阅按 `EventConventions.cs` 第 5 条：`ISubscriber<T>.Subscribe(...).AddTo(bag)`，句柄进 `DisposableBag` 自行释放。

## `Game.Quest.QuestLocation`（场景组件）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Key` | `string Key { get; }` | 地点键，对应表里 `ReachLocation` 目标的 `key` / `location` |
| `Radius` | `float Radius { get; }` | 抵达判定半径（米） |
| `Position` | `Vector3 Position { get; }` | `transform.position` |

由 `QuestSceneBinder` 在场景加载时登记（含未激活物体），运行时 `Instantiate` 的不登记、不参与判定与指引。

## `Game.Quest.QuestSceneBinder`（根作用域入口点，`AsSelf`，可构造注入）与 `QuestTarget`

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `TryResolveTarget` | `bool TryResolveTarget(in QuestObjectiveDefinition objective, out QuestTarget target)` | 解析目标世界坐标：显式 `LocationKey` → `ReachLocation` 用 `Key` → `TalkTo` 找 NPC；解析不到（含 `Counter`）返回 `false` |
| `QuestTarget.Position` | `Vector3 Position { get; }` | 测距用：地点位置 / NPC 根物体位置（脚底） |
| `QuestTarget.Anchor` | `Vector3 Anchor { get; }` | 标记 / 屏幕投影用：地点位置 + `QuestConfig.LocationMarkerHeight`；NPC 为 Collider 顶部 + `QuestConfig.MarkerLift`（无 Collider 同地点规则） |

`QuestHudPresenter` 用它摆世界标记与算屏幕指引；一般不用自己调，除非要做独立于 HUD 的目标提示（如小地图）。
锚点高度由 `QuestConfig` 的 `MarkerLift`（NPC 抬升量）、`LocationMarkerHeight`（地点离地高度）控制；
标记预制体地址见 `QuestConfig.TargetMarkerAddress`（Addressables 地址，非注入契约）。

## `Game.Quest.QuestPanelController`（根作用域单例）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `IsOpen` | `bool IsOpen { get; }` | 面板是否开着 |
| `OpenAsync` | `UniTask OpenAsync(CancellationToken ct = default)` | 已开或正在开时直接返回；开着期间持世界暂停令牌 + 关 Gameplay 输入图 |
| `CloseAsync` | `UniTask CloseAsync()` | 恢复输入图（仅当进来前是开的）→ 释放暂停 → 退订；没开时空操作 |

一般不直接调：HUD 点击已接好 `QuestHudPresenter → panel.OpenAsync()`。要加快捷键 / 代码触发才直接调这个。

## 调用时机与前置条件

- 需要 Boot 根作用域已建好（`QuestInstaller` 已注册）且 `IConfigService` 已初始化——Title 之后任意时刻都满足。
  直接 Play 玩法场景没有这些，`QuestService.IsReady` 恒为 `false`。
- 上报不需要调用方判断当前目标是什么：`Report` 内部只对「当前目标匹配」的任务生效，其余静默忽略。
- 追踪变化 / 完成事件发布时存档分区已写回，订阅者里再读 `QuestService` 的查询方法看到的是最新状态。

## 禁止事项

- **不要绕过 `Report` 直接改 `QuestProgress`**：`State`/`ObjectiveIndex`/`Count`/`AcceptOrder` 的 setter 是 `internal`，
  只有 `QuestRules` 能写；外部拿到的 `QuestProgress` 只读。
- **不要在 `QuestHudView` / `QuestPanelView` 里注入服务**：它们由 `IUIService` 按地址实例化，只显示与抛事件。
- **不要长期持有 `QuestSaveData` 分区实例**：`ISaveService.Commit` 会整体替换，写完要 `Get<QuestSaveData>()` 再写。
- **不要跨帧持有 `InProgress` / `GetOrdered` 的结果做遍历**：任何一次 `Report` 都可能重建缓存。
