---
type: extension-guide
module: session
layer: runtime
maturity: seed
---

# Session 扩展指南

> 要在 Session 基础上加东西时看这份。架构描述不重复，见 [`session-module-guide.md`](session-module-guide.md)。

## 加一个新的自动保存触发点

1. 找到那个「关键节点」对应的事件（MessagePipe 事件或 C# `event`）。
2. 在 `SaveTriggerBridge.Start()`（`SaveTriggerBridge.cs:63`）订阅它，`AddTo(bag)`（MessagePipe）或成对
   `+=` / `-=`（C# event，参照它订阅 `DialogueService.OnEnded` 的写法）。
3. 回调里只调 `session.RequestSave("原因字符串")`——**不要**直接调 `SaveNowAsync`：普通触发点要走闸门合并，
   避免同一帧内多次触发各自落盘一次。只有「离开玩法状态」「退出游戏」这两个例外场景（现场即将消失，等不了
   闸门）才直接调 `SaveNowAsync`，加新的这类例外前先确认现场真的会在这次调用后消失。
4. 原因字符串只用于埋点与日志排查，不影响行为，取一个能一眼看出触发源的短词（`quest_activated`、`crate`）。

不需要碰 `GameSession` 或 `SaveGateRules`——闸门条件（对白 / 面板 / 战斗终局 / 是否玩法状态）是固定的四条，
新触发点不改变「能不能存」，只改变「什么时候请求存」。

## 让新模块参与读档后重载

新模块有自己的存档分区（`ISaveData` 实现）时：

1. 在自己的 `IGameService.InitializeAsync` 里订阅 `Game.Session.SessionStartedEvent`（参照
   `QuestService.cs:123` 附近的写法：`sessionStarted.Subscribe(_ => ReloadFromSave()).AddTo(bag)`）。
2. 写一个 `ReloadFromSave()`（或类似命名）方法：**重新** `saves.Get<TYourSaveData>()`（不要用旧引用）→
   把读到的数据灌回运行时状态 → 按需重新发布「进度已刷新」一类的事件，让自己模块的 UI 整体刷新。
3. 不要在 `Session` 模块里加任何调用你这个方法的代码——Session 不枚举模块，`SessionStartedEvent` 是唯一约定。
4. `SessionStartedEvent.IsNewGame` 区分新游戏（分区是刚 `ResetAll()` 的默认值）与读档（分区是 `LoadAsync`
   载入的实际存档），需要区分这两种情况时用它判断，不要另外猜。

遭遇模块的重载走另一条路径（`ISessionStateSource.PrepareRestore`，`SessionStateAdapter.cs:95`）：因为
遭遇现场要在**进场景之前**准备好（不是进场景后再灌），不是普通「订阅事件后重载」的形状；新模块如果也有
「进场景前准备」的需要，参照这条而不是硬塞进 `SessionStartedEvent` 回调。

## 给槽位元数据加字段

1. 在 `SessionSaveData`（`SessionSaveData.cs:15`）加新的可读写属性，属性初始化器给默认值。
2. 只加字段（老存档没有的字段用默认值，见 `developer-guide.md` 9.3）：`Version` **不用动**。
3. 改语义（改名 / 换单位 / 值域变了）：`Version` 加一，在 `Migrate(int fromVersion)` 里按 `fromVersion` 逐级
   迁移（阶梯式 `if (fromVersion < N)`，不要用 `switch`）。
4. 新字段要在选槽面板展示，改 `SlotInfo`（新增字段 + 构造参数）与 `GameSession.ReadSlotInfoAsync`
   （`GameSession.cs:319` 附近，从 `candidate.Require<SessionSaveData>()` 里取）同步补上；`SaveSlotsView.Bind`
   按需要显示。
5. 更新字段的时机在 `GameSession.SaveNowAsync`（`GameSession.cs:239` 附近，`await saves.SaveAsync` 之前那几行）。

## 依赖方向约束

`Game.Session → Game.Core`（Save / Flow / UI / Events / Boot / Timing / Simulation / Telemetry）、
`Game.Session → Game.Quest / Game.Dialogue / Game.Monster / Game.Loot`（只经它们的公开 API 与事件）。
反过来任何玩法模块都不得引用 `Game.Session`——需要感知会话生命周期一律走 `SessionStartedEvent` /
`SaveCompletedEvent`，不要 `using Game.Session`。`Game.Core` 不认识 `Game.Session`。
