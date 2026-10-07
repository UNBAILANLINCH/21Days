---
type: module-guide
module: inventory
layer: runtime
maturity: seed
---

# Inventory 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Inventory/` 之前读这份。对外怎么调看
> [`inventory-external-api.md`](inventory-external-api.md)，要加东西看 [`inventory-extension-guide.md`](inventory-extension-guide.md)。
> 来源：`docs/roadmap.md` B1 背包白盒；与源码不符时以源码为准。

## 职责边界

**做**：背包面板（白盒）——按背包键（Gameplay/Inventory：B / 手柄 RB）打开全屏面板，列出玩家拾取到的东西（名字、数量、
品质色占位图标、类别标签），可按「全部 / 物品 / 线索」筛选，右侧显示选中条目的名字、类别、描述；开着时世界暂停、Gameplay 图关闭。
另含两组**纯规则**（S5 波次 1 新增，暂无调用方）：道具使用（`ItemUseRules`，道具 → 效果标识）与合成（`ItemCraftRules`，原料 id + 数量 → 产物 id）。

**不做**：背包数据的写入（归 Loot：`LootService.TryCollect` / `Reset`，本模块只读 `LootService.Items`）、
物品使用 / 合成 / 丢弃的**落地**（两组纯规则只判定不改背包，扣料与落盘归调用方走 Loot 或将来的背包服务）、
堆叠上限、正式图标与美术、落盘存档（归 roadmap E1）、背包 HUD 入口按钮、快捷物品栏（sp04 #9，未做）。

## 运行时类分工

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `InventoryFilter` | 筛选档位枚举：`All` / `Items`（材料 + 消耗品 + 关键物 + 皮 + 面具 + 钥匙）/ `Clues`（线索 + 文书）（`InventoryFilter.cs:10`） | 面板与规则共用 |
| `InventoryEntry` | `readonly struct`：Id、Name、Count、Quality、Category、Desc（`InventoryEntry.cs:8`） | `InventoryRules.Build` 产出 |
| `InventoryRules` | **纯 C#** 规则：字典 + 物品表 → 排序后的条目（按类别再按 id，未知 id 最后）、类别筛选、查不到表项用「#id」占位、类别中文标签（`InventoryRules.cs:12`） | Controller 调；EditMode 直接测 |
| `ItemStack` | `readonly struct`：道具 id + 件数（`ItemStack.cs:11`） | 配方与使用规则共用 |
| `ItemRecipe` | 一条合成配方：id、原料 `IReadOnlyList<ItemStack>`、产物 id 与件数（`ItemRecipe.cs:17`） | 调用方手搓 / 将来进 Luban 表 |
| `ItemUseSpec` | `readonly struct`：道具 id → 效果标识 + 一次消耗件数（`ItemUseSpec.cs:15`） | 同上 |
| `ItemCraftRules` | **纯 C#** 规则：判定配方能不能合成（命中 / 缺料 / 原料为空 / 配方写坏），只回结果不写背包（`ItemCraftRules.cs:19`） | 暂无调用方；EditMode 直接测 |
| `ItemUseRules` | **纯 C#** 规则：判定这件道具能不能用、效果标识是什么，只回结果不执行效果（`ItemUseRules.cs:21`） | 暂无调用方；EditMode 直接测 |
| `InventoryPanelView` | `UIView`（Panel 层、全屏、Esc 可关）：筛选按钮 + 列表（行模板池化）+ 详情 + 返回；只显示与抛事件（`InventoryPanelView.cs:23`） | `IUIService` 实例化 |
| `InventoryPanelController` | 面板会话：持世界暂停令牌 + 关 Gameplay 图；绑数据；开着时订阅 `CrateCollectedEvent` / `LootResetEvent` 刷新；返回 / Esc / 作用域销毁统一收尾（`InventoryPanelController.cs:25`） | 根作用域单例 |
| `InventoryHotkeyPresenter` | 入口点：`BootCompletedEvent` 后订阅 `Gameplay/Inventory.performed`，`ShouldOpen` 为真才开面板（`InventoryHotkeyPresenter.cs:22`） | 根作用域入口点 |
| `InventoryInstaller` | `GameplayInstaller`：注册 Controller 与入口点（`InventoryInstaller.cs:24`） | `Boot.unity` 的 `GameBootstrap` 物体 |

## 数据流

```text
打开：B / 手柄 RB → InventoryHotkeyPresenter.HandleInventory
  → ShouldOpen(!对白中 && !沉浸 && 面板不忙) → InventoryPanelController.OpenAsync
  → ui.OpenAsync<InventoryPanelView> → pause.Acquire → DisableMap(Gameplay) → 挂 View 事件
  → Refresh()：InventoryRules.Build(loot.Items, config.Tables.TbItem, filter, buffer)
     → view.SetFilter / SetList / SetDetail → 订阅开箱 / 重置事件 → 埋点 inventory_opened

交互：筛选按钮 → OnFilterChanged → filter 变化才 Refresh（选中项不在新档位时落到第一条）
      列表行 → OnEntrySelected → Refresh（高亮 + 详情）

关闭：「< 返回」→ OnClose → CloseAsync（先摘监听再 ui.CloseAsync）→ ReleaseSession
      Esc（UI/Cancel）→ UICancelRouter → IUIService.CloseTopAsync → View.OnCloseAsync 触发 OnClosed
        → Controller.HandleViewClosed → ReleaseSession（不再调 ui.CloseAsync）
      ReleaseSession：仅当进来前 Gameplay 图是开的才恢复 → 释放暂停令牌 → 退订 → 埋点 inventory_closed(reason)
```

`Refresh` 只在打开、筛选 / 选中变化、开箱 / 重置事件时调用，不每帧（`InventoryPanelController.cs:147`）。

## 依赖方向

`Game.Inventory → Game.Core`（UI / Config / Input / Timing / Events / Telemetry / Logging / Boot）、
`Game.Inventory → Game.Loot`（只读 `LootService.Items`，订阅 `CrateCollectedEvent` / `LootResetEvent`）、
`Game.Inventory → Game.Dialogue`（只读 `DialogueService.IsRunning`）。Loot / Dialogue / Core 不认识 Inventory。

## 为什么这样设计（源码读不出来的部分）

- **规则吃 `Func<int, cfg.Item>` 而不只吃 `TbItem`**：Luban 生成的 `cfg.Item` / `TbItem` 只能从字节流构造，测试要喂表只能读真实生成物；
  委托入口让「表未就绪 / id 缺失」两种退化不依赖表（`InventoryRules.cs:26`）。`TbItem` 重载只是转发。
- **表未就绪不崩**：直接 Play 玩法场景时 `IConfigService.Tables` 抛 `InvalidOperationException`，Controller 捕获后全部按「#id」显示，
  只记一次 Warn（`InventoryPanelController.cs:171`，同 `LootService.ItemName`）。
- **未知 id（表里查不到）类别为 0**：不在枚举里，只出现在「全部」档并排在最后；显示名「#id」、品质色走 `unknownColor`。
- **背包键挂在独立入口点而不是 Controller**：Controller 不是入口点，没有「启动完成后」的时机（同 `QuestHudPresenter` 挂任务键的理由）。
  面板开着时 Gameplay 图被关，背包键只开不关；关闭走 Esc 与返回按钮。
- **每次打开回到「全部」档、选中第一条**：白盒阶段不记上次状态，免得玩家开出一个空的「线索」档误以为没东西。
- **类别标签写在 `InventoryRules.CategoryLabel`**：白盒阶段不另开 SO；将来要本地化或让策划改文案时再抽 `InventoryConfig`。
- **皮与面具是两个类别、不合并**：`EItemCategory` 用 `Skin = 5` / `Mask = 6` 两个值，出处是
  `docs/design/features-spotlight/00_功能总览.md:224`（「皮」与「面具」是两个独立类别名，与「皮」分开记、不合并）。
  钥匙走 `Pass = 7`（与已有的 `Key`「关键物」分开：钥匙是过关的一次性凭证，关键物是剧情信物，
  出处 `05_皮面具与道具.md:167-170`、`:139`），文书走 `Document = 8`（出处 `05_皮面具与道具.md:190`）。
- **面板筛选档位不动，只补新类别的归属**：`InventoryFilter` 仍是「全部 / 物品 / 线索」这套**已验收**的白盒口径
  （`docs/roadmap.md:210` B1），新类别按下表并档，没有把 sp04 的「关键道具 / 材料 / 文书」三分硬套上来——
  那是一次 UI 口径改动，要策划先拍板（`05_皮面具与道具.md:276`、`13_系统界面清单.md:111` 都写着分类口径与原文不同）：

  | 类别 | 并进哪一档 | 依据 |
  | --- | --- | --- |
  | Skin 皮 / Mask 面具 / Pass 钥匙 | 物品 | `05_皮面具与道具.md:139`（皮、面具、钥匙归「关键道具」） |
  | Document 文书 | 线索 | `05_皮面具与道具.md:133` R25（文书就是可翻页阅读的那批簿册，与线索同属「信息」） |

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动或报错：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `InventoryInstaller`，排在 `LootInstaller` 之后（当前顺序：…Quest → Loot → **Inventory** → Exploration） | 没挂：按 B 无反应；排在 Loot 前：解析 `LootService` 失败 |
| 预制体地址 | Addressables（UI 组）`InventoryPanelView` → `Prefabs/UI/InventoryPanelView.prefab`，**地址等于类名** | `ui.OpenAsync<T>()` 找不到预制体，入口点记 Error |
| 预制体字段 | `InventoryPanelView` 的 12 个引用全接（`Validate` 逐个点名缺失项）；`itemTemplate` 子物体 `Highlight` / `Icon`(Image) / `Name` / `Count` / `Tag`(TMP)；三个筛选按钮各有子物体 `Highlight` | 打开时抛 `InvalidOperationException`，列出缺的字段 |
| 输入 | `GameInput.inputactions` Gameplay 图 `Inventory`（`<Keyboard>/b`、`<Gamepad>/rightShoulder`），`GameInput.cs` 已再生成 | 动作集取不到 `Inventory`，编译错 |
| 物品表 | `Tables/Data/item.xlsx` 的 `desc` / `category` 列（`EItemCategory`：Material / Consumable / Clue / Key / Skin / Mask / Pass / Document） | 查不到的 id 显示「#id」，只在「全部」档 |
| 入口 | 从 Boot → 标题「开始」进场景才有根作用域服务 | 直接 Play 玩法场景：无入口点，按 B 无反应 |

预制体层级（1920×1080 参考）：`Backdrop`（全屏深色 0.92）/ `Header`「背包」左上 (40,-24) 400×64 字号 36 / `CloseButton`「< 返回」右上 (-40,-36) 120×40 /
`Filters` 左上 (40,-100)：`FilterAll`·`FilterItems`·`FilterClues` 各 120×40、间距 16、底部 4px 金色 `Highlight` /
`List` 左侧 560 宽（VerticalLayoutGroup 间距 8）内 `ItemTemplate` 560×44（`Icon` 28×28、`Name` 22、`Count` 22、`Tag` 18 金色右对齐）/
`EmptyLabel`「还没有拾取到任何东西」/ `Detail` 右侧（`Name` 34、`Category` 22 金色、`Description` 22 自动换行）。
按钮 Image 白、底色在 Normal、Highlighted / Selected 淡金 (0.62,0.50,0.18)、Fade 0.08，均挂 `UIButtonFeedback`；`defaultSelected` = `CloseButton`。

示例数据：`SampleScene` 的 `Crate_A` 奖励为 1005 破旧信笺（线索），`Crate_B` 1002、`Crate_C` 1004 ×2，白盒里两档都能看到。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Tests/EditMode/Inventory/InventoryRulesTests.cs`（15） | 排序、三档筛选、缺表项占位与排序、表为空、空 / null 字典、数量 ≤ 0 跳过、类别标签、**四个新类别的解析 / 分组 / 标签** |
| EditMode | `.../ItemCraftRulesTests.cs`（17） | 配方命中与产物、缺料（报出缺哪件）、原料为空、配方写坏、按产物 id 查配方、同一原料写两行相加、判定不改背包、配方复制原料 |
| EditMode | `.../ItemUseRulesTests.cs`（12） | 命中回效果标识与消耗件数、未知 id / 非正 id / 空表、件数不够、使用定义写坏、判定不改背包 |
| EditMode | `.../InventoryHotkeyPresenterTests.cs`（4） | `ShouldOpen` 四个分支 |
| Showcase | 未建 | — |
| 验证场景 | 未建（用 SampleScene 从 Boot 进） | — |

跑 `/unity-test EditMode Inventory`。视觉验收：从 Boot 进 SampleScene，开 `Crate_A`、`Crate_C` 后按 B，看列表 / 筛选 / 详情 / Esc 关闭。

## 已知约束 / 未做

- 白盒：图标是品质色方块，没有正式图标；类别标签与空列表文案写死在代码 / 预制体里。
- 不记忆上次筛选与选中；没有滚动容器，条目多到超出列表高度会溢出。示例表 `Tables/Data/item.xlsx` 已从 6 行长到 16 行
  （新增皮 / 面具 / 钥匙 / 文书四类示例数据），背包实际条目仍取决于玩家拾取到几件。
- **使用与合成只有纯规则、没有调用方**：`ItemCraftRules` / `ItemUseRules` 不写背包、不执行效果，
  配方与使用表也还没有 Luban 表（由调用方传列表）。接线的人要自己扣料 / 加产物 / 落盘，并把 `EffectId` 交给
  S1 换皮与附身后续波次；没有合成界面、没有使用按钮、没有快捷物品栏（sp04 #9）。
- 没有 Showcase 与独立验证场景（DoD 第 3、4 条未达成，maturity 保持 seed）。

## 禁止事项

- 不要在 `InventoryPanelView` 里注入服务或持有状态：只显示与抛事件。
- 不要在本模块写背包数据：写入只走 `LootService.TryCollect` / `Reset`。
- 不要让 `ItemCraftRules` / `ItemUseRules` 去改传进来的背包字典：它们只判定（`TryCraft_DoesNotTouchTheBackpackDictionary`
  与 `TryUse_DoesNotTouchTheBackpackDictionary` 两条用例钉着这件事），扣料与落盘归调用方。
- 不要跨帧缓存 `LootService.Items` 的引用当快照：每次 `Refresh` 重读（分区实例会被读档整体替换）。
- 不要在 `Game.Loot` 里引用本模块类型（依赖方向是 Inventory → Loot）。
