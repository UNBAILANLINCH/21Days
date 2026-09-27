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
| 另一个入口开背包（HUD 按钮） | 注入 `InventoryPanelController` | 调 `OpenAsync`，判断用 `IsBusy`；判定条件复用 `InventoryHotkeyPresenter.ShouldOpen` |
| 物品使用 / 丢弃 | **不在这里写数据** | 写入走 Loot（或将来的背包服务）的公开方法，本模块只把按钮事件转过去，再靠事件刷新 |

## 不该从哪扩

- 不要让 `Game.Loot` 引用本模块（例如在 `LootService` 里直接开面板）；需要联动就发事件，本模块订阅。
- 不要在 View 里读 `LootService` 或表：View 只收 `InventoryEntry`。
- 不要把排序 / 筛选挪进 View：规则留在 `InventoryRules`，才能 EditMode 测。
- 平台差异（触屏开背包按钮）不在本模块写 `#if`；触屏入口按 `project-root.md` 走 Platform 层与 Action Map。
