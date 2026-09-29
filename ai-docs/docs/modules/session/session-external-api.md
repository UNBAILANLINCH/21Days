---
type: external-api
module: session
layer: runtime
maturity: seed
---

# Session 对外接口

> 别的模块要调 Session、或要在 Session 之上触发保存 / 感知读档时看这份。内部实现看
> [`session-module-guide.md`](session-module-guide.md)。

## 事件（`Game.Session`，订阅用）

```csharp
public readonly struct SessionStartedEvent { int Slot; bool IsNewGame; }
public readonly struct SaveCompletedEvent { int Slot; string Reason; bool Success; }
```

- `SessionStartedEvent`：一局开始（新游戏或读档），**内存分区已就位、切进玩法状态之前**发布。各分区所有者
  在回调里同步重载即可（示例：`QuestService.ReloadFromSave()`，见 `QuestService.cs:135`）。回调必须同步完成，
  不要 `await` 或切状态。
- `SaveCompletedEvent`：一次落盘结束（成功或失败），`GameSession.SaveNowAsync` 里 `ISaveService.SaveAsync`
  返回后发布。用来等一次保存真正完成（例如回放里的「触发保存并等待」）。

两者都在根作用域注册 broker（`SessionInstaller.InstallEvents`），跨模块订阅按普通 MessagePipe 用法，
`IDisposable` 挂到自己的 `DisposableBag`。

## `GameSession`（`IGameService` + `ITickable`，根作用域单例，`AsSelf` 可按具体类型注入）

| 成员 | 约束 |
| --- | --- |
| `int CurrentSlot` | 0 = 还没开局（标题页）；非 0 才会响应保存触发 |
| `int LatestSlot` | 保存时间最新的可用槽；0 = 没有可用存档 |
| `bool HasPendingSave` | 是否有尚未落盘的自动保存请求 |
| `UniTask<bool> NewGameAsync(int slot, ct)` | 槽号越界（不在 `1..SessionConfig.SlotCount`）直接返回 false，不抛 |
| `UniTask<bool> ContinueAsync(int slot, ct)` | 候选校验失败（损坏 / 高版本 / 缺 SessionSaveData / 无效叙事状态）不改内存与槽号；成功 Commit 同一候选。对白进行中拒绝新游戏/读档 |
| `UniTask<IReadOnlyList<SlotInfo>> ReadSlotInfosAsync(ct)` | 只读候选，不碰内存分区，可随时调 |
| `UniTask DeleteSlotAsync(int slot, ct)` | 删的是当前槽则 `CurrentSlot` 归 0 |
| `void RequestSave(string reason)` | 合并式：还没开局时忽略；落盘发生在下一次闸门打开时（`Tick`），不是同步的 |
| `UniTask<bool> SaveNowAsync(string reason, ct)` | 立即保存入口；不在玩法状态时返回false，对白运行或Narrative不稳定时也返回false并保留待保存请求。退出/离场同样受此保护，不能覆盖旧槽；其余自动保存节流条件不在此检查 |

SaveNowAsync 在对白运行或 NarrativeStable=false 时返回 false、保留待保存请求；退出/离场也不覆盖旧槽。
ISessionStateSource 新增 NarrativeStable 与 ValidateCandidate(SaveSnapshot)，后者必须只校验、不修改当前分区。

**线程 / 时序**：`SaveNowAsync` 的捕获段（`state.CaptureEncounter` + 更新 `SessionSaveData`）在第一个
`await` 之前**同步**完成，所以在 `GameStateChangingEvent` 回调里调用时，捕获发生在前一状态 `ExitAsync`
之前；落盘本身（`saves.SaveAsync`）是异步的。

## 只读值类型

```csharp
public readonly struct SlotInfo { int Slot; SlotState State; DateTime SavedAtUtc; float PlaytimeSeconds; string ProgressText; }
public enum SlotState { Empty, Available, Unavailable }
```

`SlotInfo` 按值传递，不持有分区引用；`State != Available` 时其余字段为默认值。

## 禁止事项

- 不要绕过 `GameSession` 直接调 `ISaveService.SaveAsync(slot)` 存槽位——那样绕开了稳定边界闸门与槽位元数据
  更新（`SessionSaveData` 不会跟着刷新）。
- 不要缓存 `SlotInfo` 列表跨帧使用；`ReadSlotInfosAsync` 很轻（只读候选），要展示就每次现读。
- 不要在 `SessionStartedEvent` 之外的时机假设分区已重载完成；`GameSession` 发布该事件时才保证内存分区就位。
