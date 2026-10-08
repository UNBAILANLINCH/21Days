---
type: external-api
module: loot
layer: runtime
maturity: stable
---

# Loot 外部接口

> 别的模块要查背包 / 开箱状态时查这份。内部结构见 [`loot-module-guide.md`](loot-module-guide.md)。
> 焦点、交互键、底部提示归统一交互（`Game.Interaction`，[`interaction-external-api.md`](../interaction/interaction-external-api.md)）：
> `SupplyCrate` 实现 `IInteractable`，由 `LootSceneBinder` 下发参数并登记；本模块不再有自己的焦点类。

## `Game.Loot.LootService`（根作用域单例 + `IGameService`，构造注入即可）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `TryCollect` | `bool TryCollect(SupplyCrate crate)` | 打开箱子：已开 / `crate == null` 返回 `false`；成功顺序为写分区 → 箱子切开 → 上报任务 Counter → 通知 → 发布 `CrateCollectedEvent` |
| `Reset` | `void Reset()` | 清空已开记录与背包，发布 `LootResetEvent`；场景里箱子的合上由订阅方（`LootSceneBinder`）负责，本方法不摸场景 |
| `TryConsume` | `bool TryConsume(int itemId, int count = 1)` | 从背包扣掉 `count` 件（战斗里用掉道具，调用方 `Game.Battle`）：数量不够 / 不拥有 / 参数非法返回 `false` 且不改分区；扣到 0 把该项**从背包删掉**；**不发事件、不弹通知**，落盘由之后的保存请求带上（`LootService.cs:117-134`）。底层 `LootRules.Consume`（`LootRules.cs:56`） |
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

## 箱子作为统一交互的可交互物（`SupplyCrate : IInteractable`）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `BindInteraction` | `void BindInteraction(float radius, InteractionPrompt prompt, Func<SupplyCrate, bool> onCollect)` | 由 `LootSceneBinder` 登记时调（`LootSceneBinder.cs:127`）：半径 `LootConfig.CrateInteractRadius`、提示「打开 · 物资箱」（`PromptVerb` / `PromptName`）、开箱回调 `LootService.TryCollect`；没下发过的箱子不可交互 |
| `CanInteract` | `bool CanInteract { get; }` | 未开且已下发回调；不含距离 |
| `Interact` | `void Interact()` | 调下发的开箱回调；已开 / 未下发时忽略 |
| `Focused` | `bool Focused { get; }` | 是否当前统一焦点（只由焦点系统回调写）；头顶标记不看它 |

要知道「当前焦点是不是箱子」读 `Game.Interaction.IInteractionFocus.Current as SupplyCrate`；按键与提示都由统一焦点负责，外部不要自己判断交互键。

## `Game.Loot.LootSceneBinder`（根作用域入口点，`AsSelf`，可构造注入）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Crates` | `IReadOnlyList<SupplyCrate> Crates { get; }` | 已加载场景里登记的全部物资箱（含未激活），运行时 `Instantiate` 的不登记 |

登记时同时把每只箱子 `Register` 进统一交互登记表；一般不用自己调，除非要做独立于焦点系统的箱子清单（如小地图）。

## 事件（MessagePipe，`IPublisher<T>` / `ISubscriber<T>` 注入，一文件一个 `readonly struct`）

| 事件 | 字段 | 何时发布 |
| --- | --- | --- |
| `CrateCollectedEvent` | `string Key`、`int ItemId`、`int Count` | `LootService.TryCollect` 成功后，在通知之后 |
| `LootResetEvent` | 无载荷 | `LootService.Reset` |

订阅按 `EventConventions.cs` 第 5 条：`ISubscriber<T>.Subscribe(...).AddTo(bag)`，句柄进 `DisposableBag` 自行释放。

## 调用时机与前置条件

- 需要 Boot 根作用域已建好（`LootInstaller`、`InteractionInstaller` 已注册）——直接 Play 玩法场景没有 `LootService` 与统一焦点。
- `TryCollect` 内部会查 `QuestService.IsReady`：任务系统未就绪时开箱仍会成功、写分区、弹通知，只是**不上报任务计数**（记 Warn）。
- `Items` 与 `IsCollected` 随时可查，不需要等某个事件。
- `TryConsume` **没有对应事件**：背包面板（`InventoryPanelController`）订阅的是 `CrateCollectedEvent` / `LootResetEvent`，扣减不会触发它刷新；要在面板开着时扣减，调用方自己负责让面板重读 `Items`。

## 禁止事项

- **不要绕过 `TryCollect` 直接摸 `SupplyCrate.SetOpened`**：会跳过存档 / 任务 / 通知 / 事件。
- **不要长期持有 `LootSaveData`**：本模块不对外暴露该类型，读进度一律走 `LootService.Items` / `IsCollected`。
- **不要在别的模块里给箱子重新做一套焦点选择**：读 `IInteractionFocus.Current` / `OnFocusChanged`。
