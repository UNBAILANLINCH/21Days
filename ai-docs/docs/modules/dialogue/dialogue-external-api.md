---
type: external-api
module: dialogue
layer: runtime
maturity: stable
---

# Dialogue 外部接口

> 别的模块要拉起对白、监听对白、提供条件时查这份。内部结构见 [`dialogue-module-guide.md`](dialogue-module-guide.md)。

## `Game.Dialogue.DialogueService`（根作用域单例，构造注入即可）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `PlayAsync` | `UniTask<DialogueResult> PlayAsync(int dialogueId, CancellationToken ct = default)` | 播放一段对白直到结束（`DialogueService.cs:67`） |
| `IsRunning` | `bool IsRunning { get; }` | 含打开面板、展示、收尾整个过程 |
| `OnStarted` | `event Action<DialogueStartedEvent>` | 已暂停世界、已关 Gameplay 图之后 |
| `OnChoiceSelected` | `event Action<DialogueChoiceSelectedEvent>` | 玩家选定一个选项（规则已接受） |
| `OnEnded` | `event Action<DialogueEndedEvent>` | 成功 / 取消 / 失败都触发；非成功时 `Outcome` 为空；已恢复输入、已释放暂停 |

`PlayAsync` 的异常：

| 异常 | 何时 |
| --- | --- |
| `InvalidOperationException` | 已有对白在进行（**进行中重复调用会抛**，先看 `IsRunning`） |
| `ArgumentException` | 表里没有该 id；或 `DialogueConfig` 非法（首次播放时暴露） |
| `OperationCanceledException` | `ct` 取消，或对白被外部中断；规则已回到 `Closed` |

## 返回值与事件载荷（`readonly struct`）

| 类型 | 字段 |
| --- | --- |
| `DialogueResult` | `int DialogueId`、`string Outcome`（End 节点或选项的出口码，如 `Accepted`）、`bool Skipped` |
| `DialogueStartedEvent` | `int DialogueId` |
| `DialogueChoiceSelectedEvent` | `int DialogueId`、`string NodeId`（选项所在节点）、`string ChoiceId` |
| `DialogueEndedEvent` | 同 `DialogueResult` |

事件是 C# `event`，不是 MessagePipe；订阅方自己负责退订。

## `Game.Dialogue.DialogueInteractable`（场景组件）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Interact` | `void Interact()` | 已有对白 / 超出半径 / 有树未绑定 / 无树无台词时记 Warn 并忽略；无树有台词 → 抛 `OnBubbleRequested`；有树 → 后台拉起（`DialogueInteractable.cs:116`） |
| `Bind` | `void Bind(DialogueService service)` | 场景里摆好的由 `DialogueSceneBinder` 自动调；**运行时实例化的要自己调** |
| `OnCompleted` | `event Action<DialogueResult>` | 仅正常结束触发；取消 / 失败不触发 |
| `OnBubbleRequested` | `event Action<string>` | 无树时每次交互抛下一句台词（按序循环）；不暂停世界、不切输入图 |
| `InRange` | `bool InRange { get; }` | 测距角色（Inspector `actor`，否则场景 `DialogueInteractionActor`）为空或半径 ≤ 0 恒 true；否则三维距离 |
| `CanInteract` | `bool CanInteract { get; }` | 在范围内、无对白进行，且「有树已绑定」或「无树有台词」 |
| `Focused` | `bool Focused { get; internal set; }` | 是否当前交互焦点；**只读**，仅焦点系统写 |
| `DisplayName` / `DialogueId` / `IsBound` / `HasTree` / `HasBubble` | 只读属性 | `DialogueId == 0` 即 `HasTree == false` |

`Interact()` 不抛异常、不返回结果；要结果就订阅 `OnCompleted`，或直接调 `DialogueService.PlayAsync`。
物体销毁会取消进行中的对白（用的是 `destroyCancellationToken`）。

## `Game.Dialogue.DialogueInteractionFocus`（根作用域入口点，可构造注入）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Current` | `DialogueInteractable Current { get; }` | 离玩家最近且可交互的那个；无玩家标记 / 对白进行中 / 无候选时为 null |
| `OnFocusChanged` | `event Action<DialogueInteractable>` | 焦点变化时（含变 null）触发一次，不每帧 |
| `SelectNearest` | `static DialogueInteractable SelectNearest(Vector3, IReadOnlyList<DialogueInteractable>)` | 纯选择函数，无分配（`DialogueInteractionFocus.cs:80`） |

前提：场景玩家根挂 `DialogueInteractionActor`；候选只含场景里摆好的物体。要自己的交互提示就订阅 `OnFocusChanged`，别再逐帧测距。

## `Game.Core.Boot.FallbackCamera`（Core，挂 Boot 的 `Main Camera`）

场景加载 / 卸载时有别的启用相机就禁用自己，否则恢复（`FallbackCamera.cs:54`）。前提：**玩法场景加载时就带着启用的相机**（之后才启用的不触发让位）；别手动开关它。

## `Game.Dialogue.IDialogueConditionSource`（Narrative 替换点）

```csharp
EncounterContext Snapshot(string targetId);   // targetId 形如 "dialogue:1001"
```

- 选项可用性**定时刷新**（unscaled 每 0.25 s，进入节点与提交后强制）时与**提交选择**时各取一次，实现必须廉价、无副作用、不抛。
- 当前注册的是占位 `DefaultDialogueConditionSource`（正向事实全真、无剧情标记）。
  Narrative 接线后在 `DialogueInstaller` 替换注册（`DialogueInstaller.cs:51`），见 extension-guide。

## `Game.Dialogue.DialogueConfig`（ScriptableObject）

资产 `Assets/_Project/Data/Dialogue/DialogueConfig.asset`，由 `DialogueInstaller` 注册；只读属性含义见 extension-guide「改表现参数」。

## 调用时机与前置条件

- 需要 Boot 根作用域已建好（`DialogueInstaller` 已注册）且 `IConfigService` 已初始化——Title 之后任意时刻都满足。
  直接 Play 玩法场景没有这些，只剩无树气泡能用。
- 调用方不需要、也不应该自己暂停世界或关输入：`PlayAsync` 期间 `Time.timeScale = 0`、Gameplay 动作图关闭，
  结束后恢复到进来前的状态。暂停期间还要动的东西用 unscaled 时间。输入服务未初始化（`input.Actions == null`，
  如 EditMode 测试）时跳过输入图的记录 / 禁用 / 恢复，不抛。
- 条件选项的真假取决于 `IDialogueConditionSource`；占位实现下依赖剧情标记的选项永远不可用。

## 禁止事项

- **不要自己开 `DialogueView` / `DialogueHistoryView`**，也不要直接调 `DialogueController` / `DialogueRules.Start`——
  会绕过暂停、输入图与重入保护。一律走 `PlayAsync` 或 `Interact`。跳过确认弹窗、交互 HUD 同理，分归 Controller 与焦点系统。
- **不要各自改 `Time.timeScale`**：要让世界停下走 `IWorldPauseService.Acquire(this)`，否则会和对白的暂停互相覆盖。
- 不要在 `OnEnded` 回调里同步再调 `PlayAsync` 以外的方式续播；要连播就 `await` 上一段 `PlayAsync` 返回后再调下一段。
- 不要运行时改 `DialogueConfig` 字段（SO 改动在编辑器里会写回资产）。
