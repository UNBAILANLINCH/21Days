---
type: module-guide
module: loot
layer: runtime
maturity: stable
---

# Loot 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Loot/` 之前读这份。对外怎么调看
> [`loot-external-api.md`](loot-external-api.md)，要加东西看 [`loot-extension-guide.md`](loot-extension-guide.md)。
> 设计定稿见 [`PRP/exploration-whitebox/prp.md`](../../../../PRP/exploration-whitebox/prp.md) 第 3.1 节；与源码不符时以源码为准。

## 职责边界

**做**：场景里的物资箱——靠近成为焦点、交互键（Gameplay/Interact，E / F / 手柄 A）开箱、写最小背包（tbitem id → 数量）、按幂等写存档分区、
上报任务 Counter、弹奖励通知、发布事件；重置进度时清空分区并把场景里的箱子全部合上。

**不做**：背包面板 UI（本 PRP 只做数据层，`LootService.Items` 供以后的面板读）、货币 / 装备等具体道具类型
（奖励只是 tbitem 的 id + 数量，不认识具体品类）、落盘存档（分区机制齐备，读写盘归 roadmap E1）、
泛化 `IInteractable`（`SupplyCrateFocus` 只认 `SupplyCrate`，Dialogue 的交互焦点是另一套）。

## 运行时类分工

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `LootConfig` | SO：交互半径、任务上报键、提示 / 通知文案、标记抬高 | `Data/Loot/LootConfig.asset` |
| `LootRules` | **纯 C#** 规则：按键幂等记录、物品累加、整体清空、**扣减**（`Consume`，`LootRules.cs:56`）（`LootRules.cs:9`） | `LootService` 调；EditMode 直接测 |
| `MonsterLootRules` | **纯 C#** 规则：怪物按种类掉落——把一批 item id 累加进 `Items`、不写箱子键、不管幂等（同一只种类会死多次）（`MonsterLootRules.cs`） | `LootService.SettleMonsterDrop` 调；EditMode 直接测 |
| `MonsterDroppedEvent` | 怪物掉落已入背包的事实事件：`YaoId` + `ItemIds`（只交 id，不写剧情事实键）（`MonsterDroppedEvent.cs`） | `LootInstaller.InstallEvents` 注册 broker |
| `LootSaveData` | 存档分区：`CollectedCrates`（已开箱子键列表）+ `Items`（tbitem id → 数量）（`LootSaveData.cs:9`） | `ISaveService.Get<LootSaveData>()` 产出 |
| `LootService` | **对外门面**：`TryCollect` 按幂等开箱、`SettleMonsterDrop` 按种类结算怪物掉落，两者都串起存档 / 通知 / 事件 / 埋点；`Reset` 清空分区；`TryConsume` 扣减背包物品（`LootService.cs:25,122`） | 根作用域单例 + `IGameService` |
| `SupplyCrate` | 场景组件：箱子键 + 奖励（itemId × count）+ 开合外观切换，发 `OnOpenedChanged`（`SupplyCrate.cs:15`） | 场景物体；由 `LootSceneBinder` / `LootService` 调 `SetOpened` |
| `SupplyCrateMarker` | 场景组件：箱子头顶标记，未开且非沉浸时显示（`SupplyCrateMarker.cs:15`） | 与 `SupplyCrate` 同物体；沉浸状态由 `LootSceneBinder` 逐个推 |
| `LootSceneBinder` | 入口点：`sceneLoaded` 扫场景登记箱子 / 标记（含未激活）、按存档恢复开合、重置时全部合上、沉浸切换时刷标记（`LootSceneBinder.cs:22`） | 根作用域入口点（`AsSelf`） |
| `SupplyCrateFocus` | 入口点：每帧在已登记箱子里选最近且半径内的未开箱为 `Current`，交互键（Gameplay/Interact）`TryCollect`；对白 / 暂停 / 沉浸时让位（`SupplyCrateFocus.cs:21`） | 根作用域入口点（`AsSelf`）；探索 HUD 订阅 `OnFocusChanged` |
| `CrateCollectedEvent` / `LootResetEvent` | 一文件一个 `readonly struct` 事件 | `LootInstaller.InstallEvents` 注册 broker |
| `LootInstaller` | `GameplayInstaller`：注册以上全部（`LootInstaller.cs:27`） | `Boot.unity` 的 `GameBootstrap` 物体 |

## 数据流

```text
接近：SupplyCrateFocus.Tick（玩家锚点取 DialogueSceneBinder.Actor.Anchor）
  → SelectNearest(半径内 + 未开 + 激活，最近一个) → Current 变化才触发 OnFocusChanged

交互：Gameplay/Interact 按下（E / F / 手柄 A，焦点非本帧新变化）→ LootService.TryCollect(Current)
  → LootRules.Collect(分区, key, itemId, count) 幂等校验，已开 / 非法参数直接 false
  → 成功：crate.SetOpened(true) → quest.Report(Counter, config.CrateQuestKey)（quest 未就绪只记 Warn，不阻断开箱）
    → notifications.Show(RewardTitle, ComposeBody(...)) → 发布 CrateCollectedEvent → 埋点 crate_collected

重置：LootService.Reset() → LootRules.Reset(分区) → 发布 LootResetEvent
  → LootSceneBinder 订阅后 CloseAll()（场景箱子全部 SetOpened(false)）+ 刷新未激活标记

怪物掉落（2026-10-07，聚光灯「怪物种类数据化」）：
MonsterRules.DropItemIds（= YaoCatalog.DropItemsOf(yao_id)，来自 Tables/Data/yao/*.json 的 drop_items）
  → LootService.SettleMonsterDrop(yaoId, dropItemIds)
  → MonsterLootRules.Grant(分区, yaoId, dropItemIds)：逐件 +1，跳过非正数 id；空列表 / 非法 yaoId 直接 false
  → 成功：逐件查 tbitem（查不到只 Warn，不拦）→ notifications.Show(RewardTitle, 逐件「名 ×1」)
    → 发布 MonsterDroppedEvent(yaoId, itemIds) → 埋点 monster_dropped
```

扣减（2026-10-07，回合制战斗用道具，`PRP/turnbased-battle` D10）：
`Game.Battle` 的 `LootBattleBackpack.TryConsumeOne` → `LootService.TryConsume(itemId, count = 1)`（`LootService.cs:122`）
  → `LootRules.Consume(分区, itemId, count)`（`LootRules.cs:56`）：数量不够 / 不拥有 / `data` 为 null / `count ≤ 0` 返回 false 且不改数据；
    扣到 0 时把这一项从背包里**删掉**（背包面板不显示 0 件）；不碰 `CollectedCrates`（扣的是物品，不是「箱子没开过」）
  → 成功埋 `item_consumed`，失败埋 W 级 `consume_rejected`。
  **扣减不发事件、不弹通知**（以源码为准：`LootService.cs:117-121` 注释写明原因——调用方是战斗流程，那时背包面板关着；
  落盘由战斗结束后剧情回写触发的保存请求带上，分区每次现取）。所以背包面板若恰好开着，不会收到刷新事件。

**怪物掉落不写箱子键、不推任务计数、不管幂等**：箱子键与幂等是物资箱的性质；怪物按种类掉、同一只种类会死很多次、
每次都要掉，所以「一只怪只结算一次」由调用方（死亡那一处）保证。当前波次只提供这条入口，**还没有人调它**——
死亡掉落的接线归后续波次（S3 / S5）。

**让位规则**（`SupplyCrateFocus.ShouldYield`）：`DialogueInteractionFocus.Current != null`、
`DialogueService.IsRunning`、`IWorldPauseService.IsPaused`、`IHudVisibility.IsHudHidden` 任一为真时
`Current = null` 且不响应交互键；焦点刚变化的那一帧本身也不响应交互（避免关对白的那一下顺手开箱）。

**存档分区**：`LootService` 不缓存 `LootSaveData` 实例，每次操作重新 `saves.Get<LootSaveData>()`
（`LootService.cs:57`）——读档 `Commit` 会整体替换分区实例，缓存旧引用会把进度写进一份没人读的对象
（同 `QuestService.Flush` 的注释）。

## 依赖方向

`Game.Loot → Game.Core`（Save / Config / Logging / Telemetry / UI / Boot）、
`Game.Loot → Game.Quest`（只用 `QuestService.IsReady` / `Report`）。
`Game.IsometricExploration → Game.Loot`（HUD 读焦点、提示、`LootService.Items`、`LootConfig` 文案），反向禁止；
`Game.Quest` / `Game.Dialogue` / `Game.Core` 不认识 `Game.Loot`。

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动或报错：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `LootInstaller`，排在 `QuestInstaller` 之后、`ExplorationInstaller` 之前；**Config** 拖 `Data/Loot/LootConfig.asset` | 没挂：解析不到 `LootService`；没拖：记 Error 并用默认值顶上 |
| 场景箱子 | 根物体挂 `SupplyCrate` + `SupplyCrateMarker` + `BoxCollider`（Trigger 不强制），子物体 `Closed` / `Opened`（两套外观）+ `Marker`（头顶标记，子物体 SpriteRenderer + `CameraBillboard`；贴图 `Art/Sprites/Loot/marker_crate.png`，颜色白不染色，缩放 0.45 与 NPC / 任务标记齐平；**不得复用对白的 `Marker_Focus.png`**，否则与任务目标标记撞脸）；可选再挂 `ExplorationPointOfInterest`（`kind = Crate`）供探索 HUD 万向标指引 | `crateKey` 空：`LootSceneBinder` 记 Warn，开箱不会被记录；无 `ExplorationPointOfInterest`：不影响开箱，只是万向标不指这只箱子 |
| 奖励 | `SupplyCrate.itemId` / `count` 对应 `Tables/Defines/item.xml` 的 tbitem 主键 | id 查不到：通知正文用 `"#" + id` 顶上，不阻断开箱（`LootService.ItemName`） |
| 玩家标记 | 玩家根挂 `DialogueInteractionActor`（复用对话模块） | 无标记：`SupplyCrateFocus` 永远没有焦点 |
| 入口 | 从 Boot → 标题「开始」进场景才有 `LootService` | 直接 Play 玩法场景：任务上报会跳过并记 Warn（`quest.IsReady == false`） |

示例接线：`Assets/Scenes/SampleScene.unity` 的 `Crates` 根节点下 `Crate_A`（营地，itemId 1005 破旧信笺 ×1，线索；B1 背包白盒由 1001 改来）、
`Crate_B`（瞭望点，itemId 1002 精钢长剑 ×1）、`Crate_C`（灰盒另一角，itemId 1004 治疗药水 ×2）；支线 `Tables/Data/quest/2002.json`
（Counter key `crate` × 3）用同一个 `CrateQuestKey` 上报。

探索 HUD 侧（交互提示所在面板）PC 优先：触屏控件（摇杆 / 三键 / 走跑按钮）默认仅 `IsTouchPrimary` 显示，
`ShowStickOnDesktop` 为开发开关；走跑 PC 走 `Gameplay/Run`。开箱交互键（Gameplay/Interact）与交互提示不受影响；
提示文案「E 打开物资箱」（`LootConfig.PromptText`）与对白交互提示「E 对话 · 名字」同口径，位置同为屏幕下方居中
`(0, 72)` 320×48，二者由 `SupplyCrateFocus` 让位规则互斥，不会同时出现。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Tests/EditMode/Loot/LootRulesTests.cs`（13 个 `[Test]` / `[TestCase]`） | 幂等记录 / 累加 / 非法参数 / 清空 / 扣减（够扣、扣到 0 删项、不够或非法不改数据） |
| EditMode | `.../LootServiceTests.cs`（11 个） | 开箱串起存档 / 任务 / 通知 / 事件、二次开箱无效、物品名查不到时的兜底、通知格式写坏不抛、`TryConsume` 扣 1 且不发事件不弹通知 / 没有该物品时返回 false |
| EditMode | `.../SupplyCrateFocusTests.cs`（3） | 最近选择 / 半径外无焦点 / 跳过已开与未激活 |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/Exploration/ExplorationShowcase.cs`（波 4 落地中） | 开箱 → 通知 → 支线计数 → 重置的端到端回放，覆盖 V4 / V5 |

跑 `/unity-test EditMode Loot`；端到端视觉验收跑 `/verify-module Exploration`（编辑器须打开）。

## 已知约束 / 未做

- 背包面板在 Inventory 模块：`InventoryPanelController` 读 `LootService.Items` + 物品表，开着时订阅 `CrateCollectedEvent` / `LootResetEvent` 刷新（见 `inventory-module-guide.md`）；本模块不认识面板。
- 没有落盘存档：分区只活在内存，进程重启即丢；游戏级读写盘归 roadmap E1。
- 奖励种类单一：只有「tbitem id × 数量」，没有稀有度、词条等扩展字段。

## 禁止事项

- 不要绕过 `LootRules.Collect` / `LootRules.Reset` / `LootRules.Consume` 直接改 `LootSaveData` 的字段（战斗侧扣道具也只走 `LootService.TryConsume`）。
- 不要长期持有 `LootSaveData` 引用：每次操作重新 `saves.Get<LootSaveData>()`。
- 不要在 `SupplyCrate` / `SupplyCrateMarker` 里注入服务：开合与沉浸状态都由外部（`LootService` / `LootSceneBinder`）驱动，组件自己不读输入、不认识容器。
- 不要在 `Game.Quest` / `Game.Dialogue` 里加物资箱名词；跨模块只读 `QuestService` 的公开方法。
