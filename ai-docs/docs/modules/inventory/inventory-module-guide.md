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
另含两组**纯规则**（`ItemUseRules` / `ItemCraftRules`）与**它们的生产调用方**（2026-10-07 补上）：
`ItemUseService`（查使用表 → 找效果执行者 → 执行 → 扣件数）与 `ItemCraftService`（找配方 → 判定原料 → 产物入包），
面板详情区有「使用」「合成」两个按钮，结果经 `INotificationService` 用中文短句告诉玩家。

**不做**：背包数据的写入（归 Loot：`LootService.TryCollect` / `TryConsume` / `GrantItems` / `Reset`，本模块只读 `LootService.Items`）、
**皮 / 面具类的使用效果**（归 S1「换皮与附身」，本波次遇到这类 `effectId` 明确回「没有执行者」）、
丢弃、堆叠上限、正式图标与美术、落盘存档（归 roadmap E1）、背包 HUD 入口按钮、快捷物品栏（sp04 #9，未做）、
合成界面（合成按钮挂在详情区；05:107 R8 的「原料合成后是否消失」已按用户 2026-10-10 拍板落定为**消失**，
「合成本身在哪个界面 / 有没有合成台」仍待拍板）。

## 运行时类分工

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `InventoryFilter` | 筛选档位枚举：`All` / `Items`（材料 + 消耗品 + 关键物 + 皮 + 面具 + 钥匙）/ `Clues`（线索 + 文书）（`InventoryFilter.cs:10`） | 面板与规则共用 |
| `InventoryEntry` | `readonly struct`：Id、Name、Count、Quality、Category、Desc（`InventoryEntry.cs:8`） | `InventoryRules.Build` 产出 |
| `InventoryRules` | **纯 C#** 规则：字典 + 物品表 → 排序后的条目（按类别再按 id，未知 id 最后）、类别筛选、查不到表项用「#id」占位、类别中文标签（`InventoryRules.cs:12`） | Controller 调；EditMode 直接测 |
| `ItemStack` | `readonly struct`：道具 id + 件数（`ItemStack.cs:11`） | 配方与使用规则共用 |
| `ItemRecipe` | 一条合成配方：id、原料 `IReadOnlyList<ItemStack>`、产物 id 与件数（`ItemRecipe.cs:17`） | 调用方手搓 / 将来进 Luban 表 |
| `ItemUseSpec` | `readonly struct`：道具 id → 效果标识 + 一次消耗件数（`ItemUseSpec.cs:15`） | 同上 |
| `ItemCraftRules` | **纯 C#** 规则：判定配方能不能合成（命中 / 缺料 / 原料为空 / 配方写坏），只回结果不写背包（`ItemCraftRules.cs:19`） | `ItemCraftService` 调；EditMode 直接测 |
| `ItemUseRules` | **纯 C#** 规则：判定这件道具能不能用、效果标识是什么，只回结果不执行效果（`ItemUseRules.cs:21`） | `ItemUseService` 调；EditMode 直接测 |
| `IItemEffect` / `ItemEffectRegistry` | **效果执行口**：按 `effectId` 分发的执行者注册表（`ItemEffectRegistry.cs`）；查不到执行者回 `ItemEffectStatus.NoExecutor` + 中文原因，**不静默失败** | `ItemUseService` 调；`InventoryInstaller` 装配期注册 |
| `HealPlayerEffect` | 本波次**唯一接了执行者的效果**（占位治疗：用一件 1004 治疗药水，回固定点数生命；`HealPlayerEffect.cs`） | 注册在 `heal.player_hp`；数值占位等 05 拍板 |
| `IItemEffectTarget` | 效果目标口（当前只有玩家生命 `TryHeal`）；实现方是 `PlayerItemEffectTarget` | `InventoryInstaller` 把实现传给面板控制器 |
| `PlayerItemEffectTarget` | **薄适配器**：持 `PlayerRules`，`CurrentHealth` / `TryHeal` 转发（`PlayerItemEffectTarget.cs`） | `InventoryInstaller` 在工厂 lambda 里构造（`Install` 期解析不了服务） |
| `InventoryConfig` | SO：使用表（`ItemUseEntry[]`）+ 配方表（`ItemRecipeEntry[]`）+ 是否扣原料；**空表时回占位默认**（`Data/Inventory/InventoryConfig.asset`） | `InventoryInstaller` 的 Config 字段；不建 Luban 表（理由见该类文件头） |
| `ItemUseService` | **使用通路**：查使用表 → 找执行者 → 执行 → 扣件数；没有执行者时**不扣件数**（`ItemUseService.cs`） | `InventoryPanelController` 调 |
| `ItemCraftService` | **合成通路**：找配方 → 判定原料 → （按配置）扣原料 → 产物入包；已有产物时回 `AlreadyOwned`（`ItemCraftService.cs`） | `InventoryPanelController` 调 |
| `InventoryPanelView` | `UIView`（Panel 层、全屏、Esc 可关）：筛选按钮 + 列表（行模板池化）+ 详情 + 返回 + 详情区「使用」「合成」按钮与配方说明；只显示与抛事件（`InventoryPanelView.cs:23`） | `IUIService` 实例化 |
| `InventoryPanelController` | 面板会话：持世界暂停令牌 + 关 Gameplay 图；绑数据；开着时订阅 `CrateCollectedEvent` / `LootResetEvent` / `BackpackChangedEvent` 刷新；承接「使用」「合成」两个动作（合成按钮指向哪条配方由**选中项**决定：选中产物就合它，选中原料就合用到它的那条配方）；返回 / Esc / 作用域销毁统一收尾（`InventoryPanelController.cs`） | 根作用域单例 |
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
      列表行 → OnEntrySelected → Refresh（高亮 + 详情 + 两个动作按钮的可点性与说明）

使用（2026-10-07）：点「使用」→ Controller.HandleUseRequested
  → ItemUseService.TryUse(selectedId, effectTarget)
     → ItemUseRules.TryUse（查使用表 / 件数够不够）→ ItemEffects.Execute(effectId, context)
        → HealPlayerEffect.Apply（目标口为 null 时回 BadTarget「治疗口还没接上」；满血回 Rejected「生命已经满了」）
     → 只有效果真的生效才 loot.TryConsume(itemId, ConsumeCount)（顺序见 ItemUseService 文件头）
  → notifications.Show(中文短句) → Refresh

合成（2026-10-07；2026-10-09 补上「选中原料」这条路）：点「合成」→ Controller.HandleCraftRequested
  → ItemCraftService.TryCraft(craftProductId)
     **craftProductId 由 RefreshActions 算**：选中项有配方就是它自己（选中产物）；
     **没有配方时退一步问「这件东西是不是某条配方的原料」**（`FindRecipeUsingIngredient`）→ 合那条配方的产物。
     没有这一步，「选中产物才合成、而列表只列已持有的东西」会与 `AlreadyOwned` 组成死循环：
     不持有就选不中、持有又被拒（回放 `InventoryShowcase` 的两条用例就是这么撞出来的）。
     → 已有产物？→ AlreadyOwned；没有配方？→ NoRecipe
     → ItemCraftRules.TryCraft（原料齐不齐）→（InventoryConfig.consumeIngredients = **true**，用户 2026-10-10 拍板 R8）扣原料
     → loot.GrantItems(productId, productCount)（发布 BackpackChangedEvent → 面板订阅后 Refresh）
  → notifications.Show(中文短句) → Refresh

关闭：「< 返回」→ OnClose → CloseAsync（先摘监听再 ui.CloseAsync）→ ReleaseSession
      Esc（UI/Cancel）→ UICancelRouter → IUIService.CloseTopAsync → View.OnCloseAsync 触发 OnClosed
        → Controller.HandleViewClosed → ReleaseSession（不再调 ui.CloseAsync）
      ReleaseSession：仅当进来前 Gameplay 图是开的才恢复 → 释放暂停令牌 → 退订 → 埋点 inventory_closed(reason)
```

`Refresh` 只在打开、筛选 / 选中变化、开箱 / 重置事件时调用，不每帧（`InventoryPanelController.cs:147`）。

## 依赖方向

`Game.Inventory → Game.Core`（UI / Config / Input / Timing / Events / Telemetry / Logging / Boot）、
`Game.Inventory → Game.Loot`（只读 `LootService.Items`，写入只走 `TryConsume` / `GrantItems`；订阅 `CrateCollectedEvent` / `LootResetEvent` / `BackpackChangedEvent`）、
`Game.Inventory → Game.Dialogue`（只读 `DialogueService.IsRunning`）。Loot / Dialogue / Core 不认识 Inventory。
**`Game.Inventory → Game.Player`（2026-10-07 由主窗口裁决新增的一条边）**：本模块的效果目标口 `IItemEffectTarget`
声明在本模块，实现方是 **`PlayerItemEffectTarget`**（也在本模块，持 `PlayerRules` 转发），**Player 侧不认识 Inventory**。
之所以适配器放在消费方这一侧：让 `PlayerRules` 去实现 `IItemEffectTarget` 会新开一条 **Player → Inventory** 的反向边
（Player 是更底层的玩法规则）；`Inventory → Player` 才是正确方向，且效果执行口因此仍能在 EditMode 里用假目标测
（同 `Game.Monster` 声明 `IMonsterDropSink`、`Game.Loot` 实现的接法）。回放脚本（`Game.Tests.Showcase`）为了站到箱子旁
会直接读 `PlayerRules` / `PlayerModel`，那是测试程序集的事。

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
| 预制体字段 | `InventoryPanelView` 的 12 个引用全接（`Validate` 逐个点名缺失项）；`itemTemplate` 子物体 `Highlight` / `Icon`(Image) / `Name` / `Count` / `Tag`(TMP)；三个筛选按钮各有子物体 `Highlight`。**另 3 个动作字段可缺**（`useButton` / `craftButton` / `recipeLabel`）：缺了只记一条 Warn，面板照开，但「使用」「合成」不可点 | 12 个引用缺任一：打开时抛 `InvalidOperationException`，列出缺的字段；3 个动作字段缺：只少两个动作 |
| 道具配置 | `Data/Inventory/InventoryConfig.asset` 拖到 `InventoryInstaller` 的 Config 字段（不拖就用代码里的占位默认值并记 Error） | 不拖：占位治疗 + 占位配方仍可用，只是策划改不了数值 |
| 玩家生命口 | `PlayerItemEffectTarget`（持 `PlayerRules`）由 `InventoryInstaller` 在工厂 lambda 里构造。底层是 **2026-10-07 新增**的 `PlayerRules.Heal(int)` | 真接不上（例如 `PlayerInstaller` 没挂进 Boot）：容器解析失败、面板打不开——这一条是硬前置，不是软降级 |
| 输入 | `GameInput.inputactions` Gameplay 图 `Inventory`（`<Keyboard>/b`、`<Gamepad>/rightShoulder`），`GameInput.cs` 已再生成 | 动作集取不到 `Inventory`，编译错 |
| 物品表 | `Tables/Data/item.xlsx` 的 `desc` / `category` 列（`EItemCategory`：Material / Consumable / Clue / Key / Skin / Mask / Pass / Document） | 查不到的 id 显示「#id」，只在「全部」档 |
| 入口 | 从 Boot → 标题「开始」进场景才有根作用域服务 | 直接 Play 玩法场景：无入口点，按 B 无反应 |

预制体层级（1920×1080 参考）：`Backdrop`（全屏深色 0.92）/ `Header`「背包」左上 (40,-24) 400×64 字号 36 / `CloseButton`「< 返回」右上 (-40,-36) 120×40 /
`Filters` 左上 (40,-100)：`FilterAll`·`FilterItems`·`FilterClues` 各 120×40、间距 16、底部 4px 金色 `Highlight` /
`List` 左侧 560 宽（VerticalLayoutGroup 间距 8）内 `ItemTemplate` 560×44（`Icon` 28×28、`Name` 22、`Count` 22、`Tag` 18 金色右对齐）/
`EmptyLabel`「还没有拾取到任何东西」/ `Detail` 右侧（`Name` 34、`Category` 22 金色、`Description` 22 自动换行）。
按钮 Image 白、底色在 Normal、Highlighted / Selected 淡金 (0.62,0.50,0.18)、Fade 0.08，均挂 `UIButtonFeedback`；`defaultSelected` = `CloseButton`。

示例数据：`SampleScene` 的 `Crate_A` 奖励为 1005 破旧信笺（线索），`Crate_B` 1002、`Crate_C` 1004 ×2，白盒里两档都能看到。
**合成示例的原料在 `Crate_D`（1401 水痕 ×1）与 `Crate_E`（1102 湿皮 ×1）**（2026-10-09 加，(12, -8) / (13.5, -8)）——
原先三只箱子一件原料都不给，占位配方合不出来；「使用」用 `Crate_C` 的治疗药水（满血时会被拒，先挨一下再喝）。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Tests/EditMode/Inventory/InventoryRulesTests.cs`（15） | 排序、三档筛选、缺表项占位与排序、表为空、空 / null 字典、数量 ≤ 0 跳过、类别标签、**四个新类别的解析 / 分组 / 标签** |
| EditMode | `.../ItemCraftRulesTests.cs`（17） | 配方命中与产物、缺料（报出缺哪件）、原料为空、配方写坏、按产物 id 查配方、同一原料写两行相加、判定不改背包、配方复制原料 |
| EditMode | `.../ItemUseRulesTests.cs`（12） | 命中回效果标识与消耗件数、未知 id / 非正 id / 空表、件数不够、使用定义写坏、判定不改背包 |
| EditMode | `.../ItemUseServiceTests.cs`（11） | **使用通路**：效果有执行者时生效并扣一件、扣到 0 删项、**没执行者不扣件数**、不在使用表 / 件数不够 / 非正 id 都不改背包、满血与已倒下拒绝且不扣道具、**目标口为 null 时报「治疗口还没接上」**、`CanUse` / `HasExecutor` 查询 |
| EditMode | `.../ItemCraftServiceTests.cs`（9） | **合成通路**：原料齐时产物入包（默认不扣原料）、开启扣料时两样原料都扣、**缺料被拒且背包一件不动**、已有产物被拒、没有配方 / 非正 id、`HasRecipe` / `FindRecipe`、**占位配置空表时回占位默认** |
| EditMode | `.../ItemEffectRegistryTests.cs`（8） | 注册表分发、**查不到执行者回 `NoExecutor` + 中文原因**、空标识 / null 上下文、上下文透传、重复注册与空标识在装配期抛 |
| EditMode | `.../HealPlayerEffectTests.cs`（7） | 占位治疗：受伤回血并报实际点数、满血与已倒下各回各的中文原因、**目标口缺失回 `BadTarget`**、非正治疗量退回占位值 |
| EditMode | `.../PlayerItemEffectTargetTests.cs`（9） | **适配链**：`TryHeal` 转发 `PlayerRules.Heal`（真 `PlayerRules`，不是假目标口）、封顶到 `MaxHealth`、**满血 / 已倒下 / `amount <= 0` 三条负对照都返回 0 且生命不变**、`CurrentHealth` 转发、null 规则抛异常、**端到端：`ItemUseService` 用一件 1004 → 扣件数 → 回血** |
| EditMode | `.../InventoryHotkeyPresenterTests.cs`（4） | `ShouldOpen` 四个分支 |
| Showcase | `Tests/Showcase/Inventory/InventoryShowcase.cs`（2 条用例） | **开箱拿到物品 → 打开背包看到它 → 原料不够时合成被拒（负对照）→ 用掉一件（数量减一）**；另一条塞齐原料合成成功、产物入包、已有产物再合被拒 |
| 验证场景 | 未建（用 SampleScene 从 Boot 进） | — |

**本波次的测试全部未执行**（共享编辑器由主窗口串行使用，派单明确不跑 Unity）；跑法：
`/unity-test EditMode Inventory`、`/unity-test EditMode Loot`，回放跑 `/verify-module Inventory`。
视觉验收：从 Boot 进 SampleScene，开 `Crate_C`（1004 治疗药水 ×2）后按 B，看列表 / 筛选 / 详情 / 使用 / 合成 / Esc 关闭。

## 已知约束 / 未做

- 白盒：图标是品质色方块，没有正式图标；类别标签与空列表文案写死在代码 / 预制体里。
- 不记忆上次筛选与选中；没有滚动容器，条目多到超出列表高度会溢出。示例表 `Tables/Data/item.xlsx` 已从 6 行长到 16 行
  （新增皮 / 面具 / 钥匙 / 文书四类示例数据），背包实际条目仍取决于玩家拾取到几件。
- **使用与合成已有调用方，闭环已通**（2026-10-07）：
  ① 玩家生命口**已经接上**——`PlayerRules.Heal(int)`（与 `ApplyDamage` 对称、封顶 `PlayerConfig.MaxHealth`、
     参数非法 / 满血 / 已倒下返回 0 不抛）+ 本模块的薄适配器 `PlayerItemEffectTarget`。
     `PlayerRules.Heal` 是本波唯一一处改到 `Runtime/Player/` 的代码（主窗口裁决授权，只加这一个方法）。
  ② **使用表与配方表仍是 SO 占位配置**（`Data/Inventory/InventoryConfig.asset`），不是 Luban 表：改表 schema 会与另一路正在改的
     `Tables/Data/yao/*.json` 冲突（同 `BossRosterConfig` / `TurnBasedConfig` 的先例）。拍板后迁表、删 SO。
     **该资产在本波次只由安装器兜底（没拖就记 Error 并用代码占位默认值），资产文件本身未创建**（由主窗口用 MCP 建）。
- **本波次的效果执行者只有一个**（占位治疗 `heal.player_hp`）；皮 / 面具类 `effectId` 没有执行者，
  面板会明确说「这件道具的效果还没有接上」。接法见 `inventory-extension-guide.md`。
- **占位数值**：治疗量 `HealPlayerEffect.PlaceholderHealAmount = 3`（点，配到 3 点血上限等于回满），
  配方 `水痕 1 + 湿皮 1 → 进入废弃官署的钥匙 1`，产物重复持有被拒（`AlreadyOwned`，堆叠规则未定）。
  这些都要等 `docs/design/features-spotlight/05_皮面具与道具.md` 的类型 / 数值 / R8 拍板。
- 没有 Showcase 与独立验证场景以外的东西（Showcase 本波次已补，仍未执行）；maturity 保持 seed。

## 禁止事项

- 不要在 `InventoryPanelView` 里注入服务或持有状态：只显示与抛事件。
- **不要在本模块写背包数据：写入只走 Loot 的公开口** —— 开箱 `TryCollect`、加物品 `GrantItems`、扣物品 `TryConsume`、整体清空 `Reset`
  （`LootService.cs`）。本模块的 `ItemUseService` / `ItemCraftService` 是这两个口的**唯一**新增调用方，
  它们不许直接改 `LootSaveData`、也不许再引一条别的写入路径。
- 不要让 `ItemCraftRules` / `ItemUseRules` 去改传进来的背包字典：它们只判定（`TryCraft_DoesNotTouchTheBackpackDictionary`
  与 `TryUse_DoesNotTouchTheBackpackDictionary` 两条用例钉着这件事），扣料与落盘归调用方。
- 不要让 `ItemUseRules` / `ItemCraftRules` 认识效果执行者、玩家或场景：执行口在 `ItemEffectRegistry` + `IItemEffect`，
  进了纯规则那两组 EditMode 用例的前提（不依赖执行环境）就没了。
- **不要在效果没有执行者时静默失败**：必须回 `ItemUseStatus.NoExecutor` / `ItemEffectStatus.NoExecutor` 并带上中文原因；
  「点了没反应」是本工程明确要避免的失败方式。
- 不要跨帧缓存 `LootService.Items` 的引用当快照：每次 `Refresh` 重读（分区实例会被读档整体替换）。
- 不要在 `Game.Loot` 里引用本模块类型（依赖方向是 Inventory → Loot）。
