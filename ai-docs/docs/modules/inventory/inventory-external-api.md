---
type: external-api
module: inventory
layer: runtime
maturity: seed
---

# Inventory 外部接口

> 别的模块要打开背包面板、或复用背包排序 / 筛选规则时查这份。内部结构见 [`inventory-module-guide.md`](inventory-module-guide.md)。

## `Game.Inventory.InventoryPanelController`（根作用域单例，构造注入即可）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `OpenAsync` | `UniTask OpenAsync(CancellationToken ct = default)` | 打开背包面板；已开 / 正在开 / 正在关时直接返回。打开期间持世界暂停令牌并关 Gameplay 图 |
| `CloseAsync` | `UniTask CloseAsync()` | 关闭并收尾（恢复输入图、释放令牌、退订）；没开时空操作 |
| `IsOpen` | `bool IsOpen { get; }` | 打开流程走完、关闭收尾未开始 |
| `IsBusy` | `bool IsBusy { get; }` | 开着或正在开 / 关；做「按键防连按」判断用这个 |

HUD 按钮或别的入口要开背包，一行 `controller.OpenAsync().Forget()`（包 try/catch 记 Error，照 `InventoryHotkeyPresenter.OpenPanelAsync`）。
不要自己 `ui.OpenAsync<InventoryPanelView>()`——那样没有暂停令牌、不关输入图、不绑数据。

## `Game.Inventory.InventoryRules`（纯静态）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Build` | `void Build(IReadOnlyDictionary<int,int> items, Func<int, cfg.Item> lookup, InventoryFilter filter, List<InventoryEntry> result)` | 合并 + 过滤 + 排序（类别再 id，未知 id 最后），数量 ≤ 0 跳过；`result` 先清空 |
| `Build` | `void Build(IReadOnlyDictionary<int,int> items, cfg.TbItem table, InventoryFilter filter, List<InventoryEntry> result)` | 同上，`table` 为空时全部按未知 id |
| `Matches` | `bool Matches(cfg.EItemCategory category, InventoryFilter filter)` | 全部 = 一切；物品 = Material + Consumable + Key + **Skin + Mask + Pass**；线索 = Clue + **Document** |
| `CategoryLabel` | `string CategoryLabel(cfg.EItemCategory category)` | 材料 / 消耗品 / 线索 / 关键物 / 皮 / 面具 / 钥匙 / 文书；未知返回「未知」 |
| `UnknownCategory` | `const cfg.EItemCategory = 0` | 表里查不到的 id 的类别值 |

## `Game.Inventory.InventoryEntry` / `InventoryFilter`

`InventoryEntry`：`readonly struct`，`Id` / `Name`（查不到为「#id」）/ `Count` / `Quality` / `Category` / `Desc`（可空串）。
`InventoryFilter`：`All = 0`、`Items = 1`（材料 + 消耗品 + 关键物 + 皮 + 面具 + 钥匙）、`Clues = 2`（线索 + 文书）。

## 道具使用与合成（纯规则，S5 波次 1；还没有调用方）

| 类型 | 签名 | 说明 |
| --- | --- | --- |
| `ItemStack` | `readonly struct`，`ItemId` / `Count` / `IsValid` | 道具 id + 件数，配方原料与产物共用 |
| `ItemRecipe` | `ItemRecipe(string id, IReadOnlyList<ItemStack> ingredients, int productId, int productCount = 1)` | 一条配方；构造时复制原料，之后不可变；`IsValid` / `RequiredCount(itemId)` |
| `ItemUseSpec` | `readonly struct`，`ItemUseSpec(int itemId, string effectId, int consumeCount = 1)` | 一条使用定义：道具 → 效果标识 + 一次消耗几件 |
| `ItemCraftRules.TryCraft` | `ItemCraftResult TryCraft(IReadOnlyDictionary<int,int> items, ItemRecipe recipe)` | 判定一条配方：`Ok` / `NoRecipe` / `EmptyIngredients` / `InvalidRecipe` / `MissingIngredient`（缺哪件看 `MissingItemId`） |
| `ItemCraftRules.TryCraft` | `ItemCraftResult TryCraft(IReadOnlyDictionary<int,int> items, IReadOnlyList<ItemRecipe> recipes, int productId)` | 按产物 id 找第一条命中的配方再判 |
| `ItemUseRules.TryUse` | `ItemUseResult TryUse(IReadOnlyDictionary<int,int> items, int itemId, IReadOnlyList<ItemUseSpec> specs)` | 判定能不能用：`Ok` / `UnknownItem` / `InvalidSpec` / `NotEnough`；`Ok` 时 `EffectId` 是效果标识 |

两个规则都**只判定、不写背包、不执行效果**：命中后扣料 / 加产物 / 落盘由调用方走 Loot 的公开方法或将来的背包服务；
`EffectId` 的执行者是 S1 换皮与附身后续波次（详见 `ItemUseRules.cs` / `ItemCraftRules.cs` 文件头）。
配方与使用表本波次由调用方传列表，没有 Luban 表。

## 事件

本模块不发布 MessagePipe 事件；面板开关埋点 `inventory_opened` / `inventory_closed`（模块名 `inventory`）。

## 调用时机与前置条件

- 需要 Boot 根作用域已建好且 `LootInstaller` 在 `InventoryInstaller` 之前注册。
- `OpenAsync` 需要 `IUIService` 已初始化（`BootCompletedEvent` 之后）。

## 禁止事项

- 不要绕过 Controller 直接开 `InventoryPanelView`。
- 不要往 `InventoryPanelView` 注入服务；要加显示内容，加 View 的 `SetXxx` 方法由 Controller 调。
