---
type: external-api
module: narrative
layer: runtime
maturity: seed
---

# Narrative 外部接口

> 纯 C# 规则库，没有根作用域注册，**目前没有生产调用方**——下表是签名与调用约束，不代表已经在游戏里跑。
> 内部结构见 [`narrative-module-guide.md`](narrative-module-guide.md)。Dialogue 模块当前只用本文档的
> `EncounterContext` 与 `NarrativeCondition` 两节，其余类型暂无消费者。

## `Game.Narrative.EncounterContext`（`sealed class`，值语义快照）

```csharp
public EncounterContext(string targetId, string targetKind, bool playerAlive, bool sneaking,
    bool disguised, bool targetAlive, bool hostile, bool detected, IEnumerable<string> storyFlags = null);
public enum Fact { PlayerAlive, PlayerSneaking, PlayerDisguised, TargetAlive, TargetHostile, TargetDetected, StoryFlag }
public bool Read(Fact fact, string key);
```

- `targetId` 不可为空/空白，否则构造抛 `ArgumentException`。
- `Read(Fact.StoryFlag, key)`：`key` 为空/空白抛 `ArgumentException`；未知 `Fact` 抛 `ArgumentOutOfRangeException`。
- 无副作用、无内部可变状态；每次快照重新 `new` 一个实例，不要缓存后长期复用。

## `Game.Narrative.NarrativeCondition`（可变 DTO，构造后需 `Validate()`）

```csharp
public EncounterContext.Fact Fact { get; set; }
public string Key { get; set; }
public bool Expected { get; set; } = true;
public void Validate();
public static bool Matches(NarrativeCondition[][] groups, EncounterContext context);
```

- `Matches`：外层数组 **OR**，内层数组 **AND**；`groups == null` 或 `context == null` 抛 `ArgumentNullException`；
  `groups.Length == 0` 视为「无条件」返回 `true`；组内元素为空数组或 `null` 元素抛 `ArgumentException`。
- **不短路校验**：所有条件都会调 `Validate()`，非法条件不会被前面已匹配的 `true` 掩盖。
- 调用方每次求值都应传入当前实际状态的 `EncounterContext`，不要用旧快照判新一轮。

## `Game.Narrative.NarrativeContent`（`sealed class`，构造即校验，不可变）

```csharp
public NarrativeContent(string id, string entry, IEnumerable<Stage> source);
public string Id { get; }
public string Entry { get; }
public IEnumerable<Stage> Stages { get; }
public Stage Get(string id);   // 找不到抛 ArgumentException
public enum StageKind { Condition, Dialogue, WaitAction, Battle, End }
public sealed class Stage { /* Id、Kind、PayloadId、AllowEncounter、IssueRequest、RequiredParts、Conditions、Exits(Dictionary<string,string>)、Outcome */ }
```

- 构造时强校验（失败即抛 `ArgumentException`）：`id`/`entry` 非空、阶段 ID 不重复、跳转目标存在、
  `Condition` 必须有 `True`/`False` 出口、`RequiredParts` 非空须有 `Success` 出口、`AllowEncounter` 只能在 `WaitAction`、连续 `Condition` 不能成环。
- 构造后是只读值对象，可安全在多处共享引用。

## `Game.Narrative.NarrativeRules`（`sealed class`，持有可变状态，非线程安全）

```csharp
public NarrativeRules(IEnumerable<NarrativeContent> contents, ITelemetryScope telemetry);  // telemetry 可为 null
public long Generation { get; }
public NarrativeSaveData.Frame Current { get; }
public NarrativeContent.Stage Stage { get; }
public bool CanEnterEncounter { get; }
public string Outcome { get; }
public IEnumerable<string> StoryFlags { get; }

public void Start(string storyId, string targetId);
public bool EnterEncounter(string storyId, string entry, string targetId, string consumptionKey);
public bool IsConsumed(string key);
public bool ReadEdge(string key);
public void SetEdge(string key, bool value);
public long EdgeCounter(string key);
public void ConsumeEdge(string key);
public void SetFlag(string flag);
public bool MarkRequestIssued(long activation);
public void ResolveAutomatic(EncounterContext context);
public bool Apply(in NarrativeIntent intent);
public NarrativeSaveData Capture();
public void Restore(NarrativeSaveData saved);
```

调用约束：

- `Start`：`Generation` 自增，清空 `Parent`/`Outcome`，进入该剧情 `Entry` 阶段；`storyId` 未知抛 `ArgumentException`。
- `EnterEncounter`：仅当 `CanEnterEncounter`（无父阶段且当前无阶段或当前阶段 `AllowEncounter`）且 `consumptionKey` 未消费过才生效，`false` 表示未进入（不抛）；`targetId`/`consumptionKey` 为空抛 `ArgumentException`。
- `ResolveAutomatic`：只推进连续 `Condition` 与遇到的第一个 `End`；`End` 有父阶段则回续（父阶段配了对应出口才继续 `Move`，否则原地停等），无父阶段则写 `Outcome` 并清空 `Current`。超 128 步 `Condition` 链抛 `InvalidOperationException`。
- `Apply`：`in` 参数；`Generation`/`ActivationId`/`TargetId`/`RequestId` 全匹配才可能推进，否则返回 `false`（**不抛**，调用方据此判过期）。`Part` 非空须属于 `RequiredParts` 且结果 `"Success"`，全部完成才真正 `Move`；`Result` 不在 `Exits` 里返回 `false` 并埋 `result_rejected`（Warn）。
- `Capture`/`Restore`：`Capture()` 返回深拷贝；`Restore` 重新校验 `Current`/`Parent` 合法性，非法抛 `ArgumentException`，成功后 `Generation` 自增。
- 均非线程安全；一个实例对应一条独立剧情会话。

## `Game.Narrative.EncounterRules`（`sealed class`，构造即校验规则集）

```csharp
public EncounterRules(IEnumerable<Rule> source, NarrativeRules narrative);
public enum RepeatPolicy { Once, Reenter, RisingCondition }
public sealed class Rule { /* Id, TriggerKind, TargetKind, Priority, StoryId, EntryStageId, Repeat, Conditions */ }
public sealed class Candidate { /* TriggerId, TriggerKind, EntryEpoch, Context */ }
public bool TryActivate(IEnumerable<Candidate> candidates);
```

- 构造时校验每条 `Rule`（`Id` 唯一、`TriggerKind`/`StoryId`/`EntryStageId` 非空、`Repeat` 合法枚举值），并用占位 `EncounterContext` 试跑一次 `Conditions`，非法抛 `ArgumentException`。
- `TryActivate`：**一次提交一批当前有效候选**，内部不缓存过期事件；只处理 `PlayerAlive && TargetAlive` 的候选；同目标同优先级多条匹配抛 `InvalidOperationException`（内容设计错误）；`true` 表示已 `EnterEncounter`，`false` 表示无匹配或 `CanEnterEncounter` 为假。
- 调用方需在「安全边界」（如一帧固定点）调用一次，不要逐候选多次调——仲裁依赖一次性传入完整批次。

## `Game.Narrative.NarrativeIntent`（`readonly struct`）

```csharp
public NarrativeIntent(long generation, long activationId, string targetId, string requestId, string result, string part = null);
```

调用方从 `NarrativeRules.Generation` / `Current.ActivationId` / `Current.TargetId` / `Current.ActionRequestId` 取值构造，
传给 `NarrativeRules.Apply`；不要用缓存的旧值构造（会被 `Apply` 拒绝，这是设计意图而非 bug）。

## `Game.Narrative.NarrativeSaveData`（`sealed class : ISaveData`，`Version => 1`）

字段：`NextActivationId`、`Current`/`Parent`（`Frame`）、`Outcome`、`ConsumedTriggers`、`ConditionEdges`、`EdgeCounters`、`StoryFlags`（详见 module-guide 类分工表）；`Migrate(int)` 当前空实现。

由 `NarrativeRules.Capture()` 产出、`Restore()` 消费；可经 `ISaveService.Get<NarrativeSaveData>()` 取到空白实例，
但**没有任何存读档流程会往里写数据或从里面读数据**（见 module-guide「未接线清单」T6/E1）。不要手工构造 `Frame` 后直接赋值给 `NarrativeRules`——没有公开入口这样做，只能通过 `Restore`。

## 禁止事项

- 不要缓存 `EncounterContext` 长期复用；每次条件求值都应该是当时状态的新快照。
- 不要绕过 `NarrativeRules.Apply`/`EnterEncounter` 直接操作 `NarrativeSaveData` 字段。
- 不要把 `EncounterRules.TryActivate` 拆成逐候选多次调用；仲裁语义依赖一次性传入完整批次。
- 不要假设这些类已经被任何 Installer 注册到 VContainer 容器——目前需要调用方自己 `new`。
