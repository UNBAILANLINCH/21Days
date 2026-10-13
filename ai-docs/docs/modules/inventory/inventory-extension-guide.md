---
type: extension-guide
module: inventory
layer: runtime
maturity: seed
---

# Inventory 扩展指南

> 要在背包面板上加东西时看。架构见 [`inventory-module-guide.md`](inventory-module-guide.md)。

## 扩展点

| 想做 | 从哪接 | 步骤 |
| --- | --- | --- |
| 加一种物品类别 | `Tables/Data/__enums__.xlsx` 的 `EItemCategory` | ① 枚举加一行（英文 name + 值）→ ② 跑 `scripts/gen-tables.ps1` → ③ `InventoryRules.Matches` 决定它进哪一档、`CategoryLabel` 加中文 → ④ `InventoryRulesTests` 补用例 |
| 加一个筛选档位 | `InventoryFilter` + View 按钮 | ① 枚举加值 → ② `Matches` 加分支 → ③ View 加 `[SerializeField] Button` + 子物体 `Highlight`，`OnOpenAsync` / `RemoveListeners` / `SetFilter` / `Validate` 各加一处 → ④ 预制体经 MCP 加按钮并挂 `UIButtonFeedback` |
| 详情加字段（如品质文字、价格） | View `SetDetail` + 预制体 `Detail` 下加 TMP | `InventoryEntry` 已带 `Quality`；要新表字段先改 `__beans__.xlsx` 再在 `InventoryEntry` / `InventoryRules.ToEntry` 带出 |
| **加一个道具效果**（本波次留的口子） | 新写一个 `IItemEffect` 实现 | ① 新文件 `XxxEffect.cs`（`EffectId` 用「域.动作」字面量）→ ② `InventoryInstaller.Install` 里 `effects.Register(new XxxEffect(...))` → ③ 使用表（`InventoryConfig` 或将来迁入的表）里把那件道具的 `effectId` 指过来 → ④ 补一个 `XxxEffectTests` + 在 `ItemEffectRegistryTests` 的护城河之外不用动。**皮 / 面具类效果归 S1「换皮与附身」**，别在这里实现借身份 |
| **给效果加一个新的作用目标** | 扩展 `ItemEffectContext` + 新接口 | 目标口照 `IItemEffectTarget` + `PlayerItemEffectTarget` 的写法：契约声明在本模块、适配器也在本模块、底层转发到目标模块的规则类（依赖方向不许反过来）；接口只暴露效果真正需要的那一两个成员，不要把整个 `PlayerRules` 塞进来 |
| 加一种「使用表 / 配方表」的来源（迁 Luban 表） | `InventoryConfig` | 表落地后 `InventoryInstaller` 改成从表构造 `IReadOnlyList<ItemUseSpec>` / `IReadOnlyList<ItemRecipe>` 传进两个服务，删掉 SO 与占位默认；**两个服务的签名不用动**（它们只吃列表） |
| 另一个入口开背包（HUD 按钮） | 注入 `InventoryPanelController` | 调 `OpenAsync`，判断用 `IsBusy`；判定条件复用 `InventoryHotkeyPresenter.ShouldOpen` |
| 快捷物品栏用道具（sp04 #9） | 注入 `ItemUseService` | 调 `TryUse(itemId, target)`，读 `ItemUseOutcome.Status` 决定提示；**不要自己扣件数或执行效果**，也不要把这条逻辑抄进新界面 |

## 不该从哪扩

- 不要让 `Game.Loot` 引用本模块（例如在 `LootService` 里直接开面板、或让 `GrantItems` 收 `ItemStack`）；需要联动就发事件，本模块订阅。
- 不要在 View 里读 `LootService` 或表：View 只收 `InventoryEntry`。
- 不要把排序 / 筛选挪进 View：规则留在 `InventoryRules`，才能 EditMode 测。
- 不要把效果判定 / 执行塞回 `ItemUseRules`：那两组纯规则的前提是「不依赖执行环境」，一塞就测不动了。
- 不要在 `Game.Inventory` 里**绕过 `PlayerRules`** 改玩家生命（例如直接写 `PlayerModel.Health`——同属 `Game.Runtime` 程序集，
  写得到，但那是在规则类背后开洞：上限、死亡、埋点都归 `PlayerRules`）。要新能力就在 `PlayerRules` 上加口、
  在本模块加适配器。`Game.Player` 不许引用 `Game.Inventory`。
- 平台差异（触屏开背包按钮）不在本模块写 `#if`；触屏入口按 `project-root.md` 走 Platform 层与 Action Map。
