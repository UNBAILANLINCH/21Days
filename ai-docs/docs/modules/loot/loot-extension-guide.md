---
type: extension-guide
module: loot
layer: runtime
maturity: stable
---

# Loot 扩展指南

## 加一个新物资箱（场景内容，不用改代码）

1. 复制场景里已有的 `Crate_A` / `B` / `C` 层级（根 `SupplyCrate` + `SupplyCrateMarker` + `BoxCollider`，
   子 `Closed` / `Opened` 两套外观 + `Marker` 头顶标记），放到目标位置，走 Unity 编辑器 / MCP，不手改 YAML。
2. 改根物体上 `SupplyCrate` 的 `crateKey`（全场景唯一）、`itemId`（对应 `Tables/Defines/item.xml` 的 tbitem
   主键）、`count`。
3. 需要被万向标指引就再挂 `ExplorationPointOfInterest`（`kind = Crate`，`label` 填显示名）——这一步属于
   IsometricExploration 模块，见 `isometricexploration-extension-guide.md`。
4. 若要求推进某条任务的 Counter 目标，确认该任务表的 `key` 与 `LootConfig.CrateQuestKey` 一致（当前全局只有
   一个键 `"crate"`；要按箱子分别计数需要扩展 `LootConfig` 或改 `SupplyCrate` 携带自己的 questKey，见下条）。

## 加一种新的奖励口径（不再是单一 tbitem id × 数量）

当前 `SupplyCrate` 只声明一个 `itemId` + `count`。如果要支持「开箱给多件」或「随机奖励」：

1. 在 `SupplyCrate` 上把单个 `itemId`/`count` 换成一个奖励列表（`[SerializeField] private List<LootEntry> rewards`），
   `LootEntry` 是新的可序列化数据类，字段 `itemId`、`count`。
2. `LootRules.Collect` 的签名要跟着改成接收多组 `(itemId, count)`；纯规则测试同步扩容。
3. `LootService.TryCollect` 遍历奖励列表分别 `quest.Report` / 拼通知正文（多件奖励的通知格式需要在
   `LootConfig` 里另加一条格式串，不要复用 `RewardBodyFormat` 塞多行）。
4. 不要在 `SupplyCrate` 里直接引用具体道具类型（货币、装备）——奖励口径永远是「表驱动的 id + 数量」，
   具体是什么由 `tbitem` 决定，这是模块首次落地时定的边界（`SupplyCrate.cs` 文件头注释）。

## 让开箱推进不同的任务目标键

当前全局共用 `LootConfig.CrateQuestKey = "crate"`。如果要让不同箱子推进不同任务：

1. 给 `SupplyCrate` 加 `[SerializeField] private string questKey`（留空时回退到 `LootConfig.CrateQuestKey`，
   保持旧场景兼容）。
2. `LootService.TryCollect` 里把 `config.CrateQuestKey` 换成 `string.IsNullOrEmpty(crate.QuestKey) ? config.CrateQuestKey : crate.QuestKey`。
3. 不需要改 `LootRules` / 存档格式——`Counter` 目标本来就是按任意字符串键上报，`QuestService.Report` 已经支持。

## 给背包加一个消费者（如背包面板）

`LootService.Items` 是只读字典（tbitem id → 数量），新面板直接注入 `LootService` 读它即可，不需要新的读接口——
`CrateCollectedEvent` / `LootResetEvent` / `BackpackChangedEvent` 已经覆盖「什么时候变了」，面板订阅后重读 `Items` 刷新即可。

**但不要再给背包加一套独立的变更事件**：`BackpackChangedEvent` 是 2026-10-07 为「加物品（`GrantItems`）」补的，
`CrateCollectedEvent` 覆盖开箱、`LootResetEvent` 覆盖整体清空，三条合起来就是全量的写入面。
再加一条（例如「扣减事件」）前先想清楚：`TryConsume` 至今刻意不发事件（理由见 `LootService.cs:149-152`）。

## 加一次「非开箱、非掉落」的入包

走 `LootService.GrantItems(int itemId, int count)`（`LootService.cs:126`）。它不认箱子键、不做幂等、不推任务计数、
**不弹通知**（通知由发起方按自己的上下文拼），成功后发 `BackpackChangedEvent`。

- 参数是 `(int, int)` 而不是 `ItemStack`：`ItemStack` 是 `Game.Inventory` 的类型，**Loot 不许认识 Inventory**
  （依赖方向是 Inventory → Loot）。要发一批就调多次。
- **不要**为了「加物品」去调 `SettleMonsterDrop`（那是怪物语义，会发 `MonsterDroppedEvent` 并推一条怪物掉落通知），
  也不要直接改 `LootSaveData.Items`。

## 加一个「外部模块声明结算口、Loot 实现」的接法

照 `LootMonsterDropSink` 抄（`Game.Monster.IMonsterDropSink` → `Game.Loot.LootMonsterDropSink`）：

1. 接口**声明在调用方模块**（它才知道自己要什么时机、什么载荷）；
2. Loot 侧新建一个薄实现类，一行转发给 `LootService` 的公开方法，文件头写明「谁声明、谁实现、空操作安全」；
3. 在 `LootInstaller.Install` 里 `builder.Register<XxxSink>(...).AsSelf().As<IXxxSink>()`；
4. 装配由调用方模块的 installer 用构建回调 `TryResolve` 完成——**不接时行为与接线前逐字一致**。

**不要把这类接口挪进 `Game.Loot`**：那会让 Loot 认识调用方的具体类型，方向就反了。

## 依赖方向约束

新扩展只能 `Game.Loot → Game.Core` / `Game.Quest` / `Game.Interaction` / `Game.Monster`（只认后两者的接口，不认具体类型）；
不要引用 `Game.IsometricExploration` / `Game.Inventory`（方向反了，那两个模块才是读 Loot 的一方）、
不要引用 `Editor` / `Tests` 程序集。场景组件（`SupplyCrate` / `SupplyCrateMarker`）不注入服务，
扩展逻辑放入口点或门面服务里。

## 验证

规则改动配 EditMode 测试（`Tests/EditMode/Loot/`）；玩家可见行为（开箱、通知、重置）走
`Tests/Showcase/Exploration/ExplorationShowcase.cs`，背包侧的使用 / 合成走
`Tests/Showcase/Inventory/InventoryShowcase.cs`。场景资产只通过 Unity 编辑器或 Unity MCP 修改。
**注意 `LootService` 的构造签名改动会牵动所有构造点**：事件发布器收在 `LootEventPublishers` 里就是为此，
再加事件只改那个类与安装器，四处测试构造点不用动。
