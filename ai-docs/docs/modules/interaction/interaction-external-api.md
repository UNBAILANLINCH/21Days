---
type: external-api
module: interaction
layer: runtime
maturity: stable
---

# Interaction 对外接口

> 内部结构与设计理由见 [`interaction-module-guide.md`](interaction-module-guide.md)；新加一种可交互物见
> [`interaction-extension-guide.md`](interaction-extension-guide.md)。命名空间 `Game.Interaction`，全部在根作用域。

## `IInteractable`（契约，`IInteractable.cs:13`）

| 成员 | 签名 | 约定 |
| --- | --- | --- |
| `Position` | `Vector3 Position { get; }` | 测距点（三维距离） |
| `InteractionRadius` | `float InteractionRadius { get; }` | 半径；**≤ 0 = 不限距离** |
| `CanInteract` | `bool CanInteract { get; }` | 现在能否交互，**不含距离**；组件未激活 / 未启用 / 已销毁由选择函数跳过，不必自己判 |
| `InteractionPriority` | `int InteractionPriority { get; }` | 只在距离精确相等时打平，大者胜；现有实现全是 0 |
| `Prompt` | `InteractionPrompt Prompt { get; }` | 成为焦点时提示 HUD 显示「动词 · 名字」 |
| `Interact` | `void Interact()` | 只由焦点系统在「本帧焦点没变」时调；也可被代码直接调（不经焦点、不受让位影响） |
| `OnFocusChanged` | `void OnFocusChanged(bool focused)` | 只由焦点系统调；已销毁的旧焦点不会收到 `false` |

## `InteractionPrompt`（`readonly struct`，`InteractionPrompt.cs:7`）

`new InteractionPrompt(string verb, string name)`。动词空白时 HUD 回退「交互」，名字空白时只显示动词
（拼法 `InteractPromptHudView.FormatLabel`，纯函数）。

## `IInteractionRegistry`（`IInteractionRegistry.cs:11`）

| 成员 | 签名 | 约定 |
| --- | --- | --- |
| `Candidates` | `IReadOnlyList<IInteractable>` | 已登记的候选（含暂时不可交互的），焦点每帧只读它 |
| `Actor` | `InteractionActor Actor { get; }` | 场景里的玩家标记；没有或所在场景已卸载时为 null（用 `== null` 判）。**要玩家锚点的模块一律从这里取** |
| `OnActorChanged` | `event Action<InteractionActor>` | 玩家标记变化（含变为 null）；只在场景加载 / 卸载时触发，**不补发历史**——订阅前先读一次 `Actor` |
| `Register` / `Unregister` | `void Register(IInteractable)` / `void Unregister(IInteractable)` | 重复登记、null、没登记过的注销都忽略；注销**按引用**比较（已销毁对象也能准确注销） |

现有取玩家锚点的调用方：`NarrativeService`（`NarrativeService.cs:94`）、`LootSceneBinder`、`QuestSceneBinder.PlayerAnchor`（`QuestSceneBinder.cs:51`）、
`DialogueSceneBinder`（给 NPC 注入测距角色）。`DialogueSceneBinder.Actor` 转发属性已于第二波删除。

## `IInteractionFocus`（只读，`IInteractionFocus.cs:9`）

| 成员 | 签名 | 约定 |
| --- | --- | --- |
| `Current` | `IInteractable Current { get; }` | 当前焦点；让位或没有候选时为 null |
| `OnFocusChanged` | `event Action<IInteractable>` | 只在变化时触发（含变为 null） |
| `OnInteracted` | `event Action<IInteractable>` | 焦点系统刚对 `Current` 调过 `Interact()`（按键或点提示）；代码直接调 `Interact()` 不触发 |

现有读方：`ExecutionInteractor`（F 键归属，经 `MonsterEncounterState.BindExecution` 的 `container.TryResolve` 可选注入，
`MonsterEncounterState.cs:167`）、`InteractionPuppetPresenter`（小人转向）、各模块回放。

## 具体类型（容器里 `AsSelf` 可取，一般不需要）

| 类型 | 可用的公开成员 | 用途 |
| --- | --- | --- |
| `InteractionFocus` | `static bool ShouldYield(bool hasActor, bool gameplayMapEnabled, bool hudHidden, bool worldPaused)`、`Advance(bool, bool)`、`RequestInteract()` | 纯判定与测试驱动；生产路径由 `Tick` / HUD 点击调 |
| `InteractionRegistry` | `SetActor(InteractionActor)`、`HandleSceneUnloaded()`、`InteractionRegistry(Func<InteractionActor>)` | 测试 / 运行时生成玩家时手动指定标记；卸载收尾公开只为测试可调 |
| `InteractionSelector` | `static IInteractable Select(Vector3, IReadOnlyList<IInteractable>)`、`static bool IsAvailable(IInteractable)` | 纯函数，无分配 |
| `InteractPromptHudView` | `FormatKeyText(string)`、`FormatLabel(...)`、`ResolveWidth(float, float, float)`、`IsShown`、`LabelText`、`KeyText` | 纯函数供测试；三个只读属性供回放断言 |
| `InteractionPuppetPresenter` | `Present(IInteractable)` | 由 `OnInteracted` 调；公开只为测试 |

## `InteractionActor`（场景组件，`InteractionActor.cs:12`）

挂在玩家根上的空标记，`Anchor => transform`。场景里只放一个；运行时生成的玩家要自己 `SetActor`。

## 调用约束

- **不要自己读 `Gameplay/Interact`**：全工程只在 `InteractionFocus` 读一次。要知道「玩家交互了什么」订阅 `OnInteracted`。
- **不要写焦点**：`Current` 只读；实现方的「是否焦点」状态只能在 `OnFocusChanged` 回调里写。
- **登记在场景事件上做**：`sceneLoaded` 时 `Register`，卸载 / `Dispose` 时 `Unregister`，不要每帧登记。
- **代码直接调 `Interact()` 绕过焦点**：不受沉浸 / 暂停 / Gameplay 图让位影响，也不触发 `OnInteracted`（因此不转向）。
- 处决按键与交互共用 F / South：焦点非空时 F 归交互（`ExecutionInteractor.HandleExecuteKey`），新加按键型玩法要沿用「屏幕提示什么，按键就做什么」。
