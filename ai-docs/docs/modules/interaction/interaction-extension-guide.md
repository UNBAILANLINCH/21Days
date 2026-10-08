---
type: extension-guide
module: interaction
layer: runtime
maturity: stable
---

# Interaction 扩展指南

> 先读 [`interaction-module-guide.md`](interaction-module-guide.md)（焦点规则、让位、F 键归属）；签名见
> [`interaction-external-api.md`](interaction-external-api.md)。

## 扩展点

| 想做的事 | 扩展点 | 不要从哪扩 |
| --- | --- | --- |
| 新加一种「走近 → 出提示 → 按键触发」的东西 | 实现 `IInteractable` + 在本模块的场景登记器里 `Register` | 不要在 `InteractionFocus` 里加类型判断；不要自己读交互键、自己开提示 |
| 某类对象在等距时优先 | 让它的 `InteractionPriority` 返回更大的值 | 不要恢复「某类永远让位某类」的特判（D2 已废） |
| 新的让位条件（某个时刻不该有焦点） | 先找通用信号（关 Gameplay 图 / 世界暂停 / 沉浸），实在没有才在 `InteractionFocus.ShouldYield` 加一个参数 | 不要让 Interaction 认识对白 / 任务等具体服务 |
| 交互那一刻的表现（转向、动作、音效） | 订阅 `IInteractionFocus.OnInteracted`，参照 `InteractionPuppetPresenter` | 不要在实现方的 `Interact()` 里驱动玩家小人 |
| 提示样式 | `Prefabs/UI/InteractPromptHudView.prefab`；宽度上下限是视图上的 `minWidth` / `maxWidth` | 不要给实现方加自己的提示 UI |

## 新加一种可交互物（标准步骤）

以「可拾取的道具」为例，模块 `Game.Foo`：

1. **组件实现契约**：`FooPickup : MonoBehaviour, IInteractable`。
   - `Position => transform.position`；`InteractionRadius` 取配置（≤ 0 表示不限距离，一般别这么配）。
   - `CanInteract` 只写「此刻能不能交互」（如「还没被捡」），**不写距离**，也不必判激活 / 销毁。
   - `Prompt => new InteractionPrompt("拾取", 显示名)`：动词与名字放配置或 Inspector 字段，不写死在代码里多处。
   - `Interact()` 只做本模块的事（调服务 / 回调）；幂等（重复调用不重复生效）由服务保证。
   - `OnFocusChanged(bool)` 只记状态、驱动自己的头顶标记，不做别的。
   - 组件不读输入、不注入服务：需要服务就由登记器下发回调（参照 `SupplyCrate.BindInteraction`、`PortalAnchor.SetDestinationName`）。
2. **登记**：本模块的场景登记器（`IStartable`，`sceneLoaded` 扫场景）注入 `IInteractionRegistry`，扫到组件就下发参数并 `Register`；
   `sceneUnloaded` 清已销毁项、`Dispose` 时逐个 `Unregister`。参照 `LootSceneBinder.cs:128`、`DialogueSceneBinder.cs:107`；
   组件由别的驱动按版本号换人时参照 `WorldSceneDriver.SyncPortalSubscriptions`（`WorldSceneDriver.cs:204`）。
3. **注册器**：本模块的 `GameplayInstaller` 按类型注入 `IInteractionRegistry` 即可（`InteractionInstaller` 已注册它，解析在容器建好之后，与注册器顺序无关）。
4. **场景**：玩家根上要有 `InteractionActor`（每张玩法场景一个）；新场景忘了挂，整张图都没有焦点。
5. **测试**：EditMode 里 `AddComponent` 出组件，验 `CanInteract` / `Prompt` / `Interact` 幂等 / `OnFocusChanged`，
   再用 `InteractionSelector.Select` 验半径内外（参照 `PortalAnchorTests` 的「统一交互」一节、`SupplyCrateTests`）。
6. **回放**：走到它跟前 → 断言 `IInteractionFocus.Current` 是它、`InteractPromptHudView.LabelText` 是「动词 · 名字」→ 按 `Gameplay/Interact` → 断言效果。
7. **文档**：本模块 guide 写「实现了 `IInteractable`、谁登记」；本文件不用改，除非加了新的扩展点。

## 给已有对象换动词

- 对白 NPC：`DialogueInteractable` 的 Inspector 字段 `verb`（默认「对话」，`Npc_SampleBoss` 是「挑战」）。
- 物资箱：`LootConfig` 的 `promptVerb` / `promptName`。
- 传送点：`PortalAnchor` 的 `interactVerb`（默认「前往」）；目的地名来自 `TbScene.display_name`，表里没有就只显示动词。

## 约束

- 依赖方向：实现方 → Interaction，Interaction 不认识实现方；CharacterPuppet 不认识 Interaction。
- 处决不进统一焦点：要新增「按同一个键做另一件事」的玩法，按 D6 的口径在那一处让位给交互，并埋一条可区分的拒绝原因。
- 进入范围即触发的东西不要登记进来（它们没有提示、不需要按键）。
