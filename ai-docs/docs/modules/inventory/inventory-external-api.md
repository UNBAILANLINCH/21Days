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

## 道具使用与合成（S5 波次 1 立规则，2026-10-07 补上调用方）

| 类型 | 签名 | 说明 |
| --- | --- | --- |
| `ItemStack` | `readonly struct`，`ItemId` / `Count` / `IsValid` | 道具 id + 件数，配方原料与产物共用 |
| `ItemRecipe` | `ItemRecipe(string id, IReadOnlyList<ItemStack> ingredients, int productId, int productCount = 1)` | 一条配方；构造时复制原料，之后不可变；`IsValid` / `RequiredCount(itemId)` |
| `ItemUseSpec` | `readonly struct`，`ItemUseSpec(int itemId, string effectId, int consumeCount = 1)` | 一条使用定义：道具 → 效果标识 + 一次消耗几件 |
| `ItemCraftRules.TryCraft` | `ItemCraftResult TryCraft(IReadOnlyDictionary<int,int> items, ItemRecipe recipe)` | 判定一条配方：`Ok` / `NoRecipe` / `EmptyIngredients` / `InvalidRecipe` / `MissingIngredient`（缺哪件看 `MissingItemId`） |
| `ItemCraftRules.TryCraft` | `ItemCraftResult TryCraft(IReadOnlyDictionary<int,int> items, IReadOnlyList<ItemRecipe> recipes, int productId)` | 按产物 id 找第一条命中的配方再判 |
| `ItemUseRules.TryUse` | `ItemUseResult TryUse(IReadOnlyDictionary<int,int> items, int itemId, IReadOnlyList<ItemUseSpec> specs)` | 判定能不能用：`Ok` / `UnknownItem` / `InvalidSpec` / `NotEnough`；`Ok` 时 `EffectId` 是效果标识 |
| `ItemUseService.TryUse` | `ItemUseOutcome TryUse(int itemId, IItemEffectTarget target)` | **完整的使用通路**（判定 → 找执行者 → 执行 → 扣件数）。`NoExecutor` / `EffectRejected` 是新增的两个失败码；`ConsumedCount` 是实际扣掉的件数 |
| `ItemCraftService.TryCraft` | `ItemCraftResult TryCraft(int productId)` | **完整的合成通路**（找配方 → 判定原料 → 可选扣料 → 产物入包）。新增失败码 `AlreadyOwned` |
| `ItemCraftService.FindRecipeUsingIngredient` | `ItemRecipe FindRecipeUsingIngredient(int itemId)` | 找一条**把这件东西当原料**的配方（取第一条命中；找不到返回 null）。**给「选中原料 → 合成产物」那条路用**：面板列表只列已持有的东西，而 `TryCraft` 拒绝重复持有，不给这条路就选不中也合不了。同一原料出现在多条配方时只取第一条，属占位口径（`05:107 R8` 待拍板） |
| `ItemEffectRegistry` | `void Register(IItemEffect)` / `ItemEffectOutcome Execute(string effectId, ItemEffectContext)` | 效果执行口：按 `effectId` 分发；查不到回 `ItemEffectStatus.NoExecutor` + 中文原因 |
| `IItemEffect` | `string EffectId { get; }` / `ItemEffectOutcome Apply(ItemEffectContext)` | 一个效果的执行者；**皮 / 面具类归 S1「换皮与附身」**，本波次只有占位治疗 `HealPlayerEffect` |
| `IItemEffectTarget` | `int CurrentHealth { get; }` / `int TryHeal(int amount)` | 效果目标口（玩家生命）。契约声明在本模块，实现方是 `PlayerItemEffectTarget`（也在本模块，持 `PlayerRules` 转发）；**Player 侧不认识 Inventory** |
| `PlayerItemEffectTarget` | `PlayerItemEffectTarget(PlayerRules rules)` | 上面那个口的具体实现；底层 `PlayerRules.Heal(int)`（2026-10-07 新增，封顶 `PlayerConfig.MaxHealth`）。由 `InventoryInstaller` 构造 |
| `InventoryConfig` | SO，`CreateUseSpecs()` / `CreateRecipes()` / `ConsumeIngredients` | 占位使用表与配方表（`Data/Inventory/InventoryConfig.asset`）；**没填任何行时回占位默认**，不返回空表；不建 Luban 表 |

两个纯规则都**只判定、不写背包、不执行效果**；落盘与扣减由 `ItemUseService` / `ItemCraftService` 走 Loot 的公开口
（`TryConsume` / `GrantItems`）。**别的模块要用道具 / 合成，注入这两个服务，不要自己拼这几步**。
配方与使用表现在是 SO 占位配置，没有 Luban 表（理由见 `InventoryConfig` 文件头）。

## 事件

本模块**不发布** MessagePipe 事件；面板开关埋点 `inventory_opened` / `inventory_closed`（模块名 `inventory`），
使用 / 合成埋点 `item_used` / `item_crafted` 与失败埋点 `use_rejected` / `craft_rejected`（W 级）。
本模块**订阅** Loot 的 `CrateCollectedEvent` / `LootResetEvent` / `BackpackChangedEvent`（面板开着时刷新）。

## 调用时机与前置条件

- 需要 Boot 根作用域已建好且 `LootInstaller` 在 `InventoryInstaller` 之前注册。
- `OpenAsync` 需要 `IUIService` 已初始化（`BootCompletedEvent` 之后）。

## 禁止事项

- 不要绕过 Controller 直接开 `InventoryPanelView`。
- 不要往 `InventoryPanelView` 注入服务；要加显示内容，加 View 的 `SetXxx` 方法由 Controller 调。
- **不要自己拼「判定 + 扣件数 + 执行效果」这几步**：注入 `ItemUseService` / `ItemCraftService`，它们只走 Loot 的公开口写背包。
- **不要在效果没有执行者时静默失败**：读 `ItemUseOutcome.Status`，`NoExecutor` 要显示成给玩家看的中文短句。
- **不要在本模块复制一份玩家生命 / 效果实现**：需要的效果写一个 `IItemEffect` 并注册进 `ItemEffectRegistry`。
