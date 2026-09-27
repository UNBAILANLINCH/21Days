---
type: external-api
module: loot
layer: runtime
maturity: stable
---

# Loot 外部接口

> 别的模块要查背包 / 开箱状态、驱动焦点提示时查这份。内部结构见 [`loot-module-guide.md`](loot-module-guide.md)。

## `Game.Loot.LootService`（根作用域单例 + `IGameService`，构造注入即可）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `TryCollect` | `bool TryCollect(SupplyCrate crate)` | 打开箱子：已开 / `crate == null` 返回 `false`；成功顺序为写分区 → 箱子切开 → 上报任务 Counter → 通知 → 发布 `CrateCollectedEvent` |
| `Reset` | `void Reset()` | 清空已开记录与背包，发布 `LootResetEvent`；场景里箱子的合上由订阅方（`LootSceneBinder`）负责，本方法不摸场景 |
| `IsCollected` | `bool IsCollected(string key)` | 该箱子键是否已开过 |
| `Items` | `IReadOnlyDictionary<int, int> Items { get; }` | 最小背包：tbitem id → 累计数量，随分区变化；**内部按需重取分区，不要跨帧持有引用当缓存** |

`TryCollect` 是打开箱子的**唯一入口**，不要绕过它直接调 `SupplyCrate.SetOpened(true)`——那样不会写存档、
不会上报任务、不会弹通知，下次场景重载会按存档判定「未开」又把它打开一次。

## `Game.Loot.SupplyCrate`（场景组件）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Key` / `ItemId` / `Count` | `string` / `int` / `int` | 箱子键、奖励物品 id（tbitem 主键）、数量 |
| `IsOpened` | `bool IsOpened { get; }` | 当前开合状态 |
| `Position` | `Vector3 Position { get; }` | `transform.position` |
| `OnOpenedChanged` | `event Action<SupplyCrate> OnOpenedChanged` | 开合**真正变化**时触发（幂等调用 `SetOpened` 同值不触发） |
| `SetOpened` | `void SetOpened(bool value)` | 切外观 + 触发事件；**只应由 `LootService` / `LootSceneBinder` 调**，其余代码只读不写 |

## `Game.Loot.SupplyCrateFocus`（根作用域入口点，`AsSelf`，可构造注入）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Current` | `SupplyCrate Current { get; }` | 当前焦点箱子；无焦点为 `null`（用 `== null` 判） |
| `OnFocusChanged` | `event Action<SupplyCrate> OnFocusChanged` | 只在焦点变化（含变为 `null`）时触发，不每帧触发 |
| `SelectNearest` | `static SupplyCrate SelectNearest(Vector3 origin, float radius, IReadOnlyList<SupplyCrate> candidates)` | 纯选择逻辑：半径内 / 未开 / 激活里选最近一个；供测试与自定义焦点逻辑复用 |

开箱触发键为交互键 `Gameplay/Interact`（E / F / 手柄 A），由 `SupplyCrateFocus.Tick` 内部读取，外部不需要自己判断按键。

探索 HUD（`ExplorationControlsPresenter`）订阅 `OnFocusChanged` 只用于**显示提示文字**，不重复做选择判断；
提示文案取 `LootConfig.PromptText`（当前「E 打开物资箱」，位置与对白交互提示同为屏幕下方居中），不要在别处另写一份。

## `Game.Loot.LootSceneBinder`（根作用域入口点，`AsSelf`，可构造注入）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Crates` | `IReadOnlyList<SupplyCrate> Crates { get; }` | 已加载场景里登记的全部物资箱（含未激活），运行时 `Instantiate` 的不登记 |

`SupplyCrateFocus` 用它做候选列表；一般不用自己调，除非要做独立于焦点系统的箱子清单（如小地图）。

## 事件（MessagePipe，`IPublisher<T>` / `ISubscriber<T>` 注入，一文件一个 `readonly struct`）

| 事件 | 字段 | 何时发布 |
| --- | --- | --- |
| `CrateCollectedEvent` | `string Key`、`int ItemId`、`int Count` | `LootService.TryCollect` 成功后，在通知之后 |
| `LootResetEvent` | 无载荷 | `LootService.Reset` |

订阅按 `EventConventions.cs` 第 5 条：`ISubscriber<T>.Subscribe(...).AddTo(bag)`，句柄进 `DisposableBag` 自行释放。

## 调用时机与前置条件

- 需要 Boot 根作用域已建好（`LootInstaller` 已注册）——直接 Play 玩法场景没有 `LootService` / `SupplyCrateFocus`。
- `TryCollect` 内部会查 `QuestService.IsReady`：任务系统未就绪时开箱仍会成功、写分区、弹通知，只是**不上报任务计数**（记 Warn）。
- `Items` 与 `IsCollected` 随时可查，不需要等某个事件。

## 禁止事项

- **不要绕过 `TryCollect` 直接摸 `SupplyCrate.SetOpened`**：会跳过存档 / 任务 / 通知 / 事件。
- **不要长期持有 `LootSaveData`**：本模块不对外暴露该类型，读进度一律走 `LootService.Items` / `IsCollected`。
- **不要在别的模块里给箱子重新做一套焦点选择**：复用 `SupplyCrateFocus.OnFocusChanged` / `Current`。
