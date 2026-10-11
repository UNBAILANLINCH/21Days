# PRP：统一交互（roadmap A3）

> 2026-10-08 由主窗口写。依据是三份只读摸底（交互现状、B4、D6），摸底时基于当时的 HEAD。
> 门三简化：本文先交用户审架构，审完直接按第 6 节的波次派单，不另出 tasks.md。
> 执行中如有修订记在第 8 节。与源码不符时以源码为准。

## 1. 上下文快照

「走近 → 出提示 → 按键触发」现在有四套，各写各的：

| 模块 | 范围检测 | 选焦点 | 读交互键 | 提示 UI |
| --- | --- | --- | --- | --- |
| Dialogue | `DialogueInteractionFocus` 每帧三维距离 | 最近 | 自己读 `Gameplay/Interact` | `DialogueInteractHudView`，动词写死「对话」；头顶标记 |
| Loot | `SupplyCrateFocus` 每帧三维距离 + `LootConfig` 半径 | 最近，且只要有对白焦点就让位 | 自己读 | 探索 HUD `SetPrompt`；`SupplyCrateMarker` |
| World | `WorldSceneDriver` 每帧 XZ 距离 | 无焦点，第一个命中的直接触发 | 自己读 | 无 |
| Stealth | `ExecutionInteractor` 按键时才算 | 背后 120° 最近 | 自开一份 InputActionAsset 读 `Execute`（F/South，与 Interact 同键） | 只有白盒文字 |

另外：
- Narrative 的 BOSS「挑战」借 Dialogue 的焦点（`SetInteractionHandover`），提示仍显示「对话」。
- Performance / Narrative 的进入范围触发、Quest 目标标记、探索万向标兴趣点都不是按键交互，**不在本 PRP 范围**。

问题：
1. 一次 E 可能同时触发对白、开箱和按键型传送点；一次 F 可能同时触发交互和处决。
2. Loot 只要对白焦点存在就让位，与距离无关。`interactRadius ≤ 0` 的 NPC 会永久压住所有箱子。
3. 选焦点的规则写了三遍，距离算法各不相同。

## 2. 目标 / 非目标

**目标**
- 任一时刻全局只有一个交互焦点，屏幕上只有一条提示。一次按键只触发一件事。
- `Gameplay/Interact` 全工程只在一处读取。
- Dialogue、Loot、按键型传送点改为同一契约的实现。新加可交互物时只需实现契约并登记。
- 提示动词由对象自己给，BOSS 显示「挑战」。
- 交互时玩家小人转向目标（接 D6）。

**非目标**
- 不并入进入范围即触发的东西：演出触发、剧情触发、`EnterRange` 型传送点。
- 不碰 Quest 标记与探索万向标。
- 处决不进统一焦点，只做按键归属的裁决（D6）。
- 不改键位绑定，不做提示的美术。
- 各模块的存档分区不合并：Loot 的已开、Narrative 的已消耗、Performance 的 once 各存各的。

## 3. 关键决策

| # | 决策 | 理由 |
| --- | --- | --- |
| D1 | 新模块 `Runtime/Interaction/`，命名空间 `Game.Interaction`。依赖 Core 和 CharacterPuppet 的公开门面。Dialogue、Loot、World、Stealth 依赖它 | 提示 HUD 和小人转向都是玩法表现，Core 要求零玩法，所以不放 Core；也不能放 Dialogue，否则 Loot、World 要反向依赖对白 |
| D2 | **最近者得焦点**。`InteractionPriority`（默认 0）只在距离相等时打平，现有对象全是 0。**废掉「箱子永远让位对白」** | 统一后只有一条提示，原来让位的理由（两条提示同位重叠）不存在了；还顺带修掉无限半径 NPC 压住箱子的问题。将来真要某类优先，改这个值就行 |
| D3 | 让位条件统一为：没有玩家锚点、`Gameplay` 动作图未启用、沉浸模式、世界暂停。满足任一条时焦点为空，按键不响应 | 对白、演出、任务面板、镜碎页都会关 Gameplay 图，这是「玩家此刻不能操作世界」最通用的信号。Interaction 不必认识对白服务。Dialogue 原来不看世界暂停，这次补齐 |
| D4 | 焦点在本帧变化时，本帧不响应按键。原来只有 Loot 这样做，推广到全部 | 防止「关掉对白那一下顺手开箱」 |
| D5 | 契约 `IInteractable`：位置、半径（≤ 0 表示不限距离，沿用 Dialogue 的语义）、`CanInteract`（不含距离）、优先级、`InteractionPrompt`（动词 + 名字）、`Interact()`、焦点变化回调（驱动各自的头顶标记） | 只用最小集合覆盖现有三类。头顶标记的可见性规则各模块不同（气泡让位、任务标记接管），留在各自模块 |
| D6 | **F 键归属：交互焦点非空时，这一次 F/South 归交互，处决让位** | 屏幕上提示什么，按键就做什么；处决没有提示 UI。用户不同意可以改成反向，改动只在 `ExecutionInteractor` 一处 |
| D7 | `DialogueInteractHudView` 迁入 Interaction 并改名为 `InteractPromptHudView`；`DialogueInteractionActor` 迁入并改名为 `InteractionActor`。都用 `git mv` 连同 `.meta` 一起移，保住 GUID，场景与预制体引用不断。Addressables 地址改成新类名 | 两者本来就不是对白私有的。Loot 和 Narrative 已经在借用 Actor |
| D8 | Loot 的提示改走统一 HUD，探索 HUD 的 `SetPrompt` 接线删除。`SupplyCrateFocus` 删除，`SupplyCrate` 实现契约，`LootSceneBinder` 负责登记 | 消除第二套提示 |
| D9 | 按键型 `PortalAnchor` 实现契约：动词「前往」，名字用目的地的显示名，没有就只显示动词。`WorldSceneDriver` 不再读交互键，只负责登记和注销按键型传送点。`PortalAnchor` 仍然不读输入、不查物理、不做转场 | 保住 world-module-guide 的契约，EditMode 里 `AddComponent` 照样能测 |
| D10 | `DialogueInteractable` 加 `verb` 字段，默认「对话」。SampleScene 里的 `Npc_SampleBoss` 设为「挑战」 | battle-module-guide 记的待办 |
| D11 | 第二波：`InteractionFocus.OnInteracted(target)` 触发时，玩家小人 `FaceTowards(target)` 并播一次程序化交互动作；目标身上有 `ChibiPuppet` 时也转向玩家。由 Interaction 里的一个表现层组件订阅 | 依赖 D6 先交付的 `ChibiPuppet` 接口。CharacterPuppet 不反向认识 Interaction |

## 4. 架构

```text
Game.Interaction（Runtime/Interaction/）
  IInteractable                 契约（D5）
  InteractionPrompt             readonly struct：Verb、Name
  InteractionActor              玩家锚点（由 DialogueInteractionActor 迁入，保 GUID）
  IInteractionRegistry / InteractionRegistry   登记与注销候选；找出已加载场景里的 InteractionActor
  InteractionSelector           静态纯函数 Select(origin, candidates)：无分配，可单测
  InteractionFocus              ITickable：让位判定 → Select → 焦点变化（回调与埋点）→ 读 Interact（全工程唯一一处）→ 本帧焦点没变才调 Current.Interact()
  IInteractionFocus             对外只读：Current、OnFocusChanged、OnInteracted
  InteractPromptHudView         由 DialogueInteractHudView 迁入，保 GUID；显示「键位 动词 · 名字」
  InteractionInstaller          GameplayInstaller，挂到 Boot 的 GameBootstrap 上
  （第二波）InteractionPuppetPresenter   D11

实现方：
  Dialogue  DialogueInteractable : IInteractable（加 verb）；DialogueSceneBinder 把绑定的对象登记进注册表；DialogueInteractionFocus 删除
  Loot      SupplyCrate : IInteractable；LootSceneBinder 登记；SupplyCrateFocus 删除
  World     PortalAnchor : IInteractable（只有按键型才登记）；WorldSceneDriver 改为登记 / 注销
  Stealth   ExecutionInteractor 按键时先看 IInteractionFocus.Current（D6）
```

## 5. 必须保留的现有行为

以下是回归底线，均出自摸底报告，附原出处：

1. 对白进行中没有焦点，关掉对白的那一下不会再次触发（Dialogue 原来靠 `service.IsRunning`，现在靠 D3 的 Gameplay 图信号，并且 `DialogueInteractable.CanInteract` 的占用检查保留作兜底）。
2. 沉浸模式：没有焦点，交互键和 HUD 点击无效，头顶标记隐藏；但代码直接调用 `Interact()` 不受影响（dialogue-module-guide :21）。
3. 头顶标记规则不变：
   - 焦点优先于可交互。
   - 气泡显示时，标记和名字都隐藏。
   - `MarkerOverridden` 时只隐藏图标，名字仍跟随焦点（:197）。
   - `TryGetIconAnchor` 照旧对外公开。
4. handover 的语义不变：`NarrativeTrigger` 接管「按下后做什么」，焦点、提示、标记仍走 `DialogueInteractable`；点击路径不重复响应（NarrativeTrigger.cs:84-87）。
5. HUD 等 `BootCompletedEvent` 之后再打开，常驻，显隐只切 root；打不开只记 Error，交互键照常可用。
6. 键位徽章读第一条 `<Keyboard>` 绑定的显示串，没有则回退为「E」。
7. 读 `Gameplay/Interact` 的地方带 `// lint-ok` 注释，不进 `InputCommand`。绝不复用 `Confirm`（它是确定性输入位）。
8. Loot 开箱幂等：`Collect` 返回 false 就不开；开了要刷新标记、上报任务计数、发通知。
9. 选择函数保持静态、纯函数、无分配；`Focused` 这类状态只有焦点系统能写。
10. `PortalAnchor` 不读输入、不查物理、不做转场；一次按键只处理一个传送点。

## 6. 波次与验证

| 波次 | 内容 | 档位 | EditMode | 回放（逐模块列出，附理由） |
| --- | --- | --- | --- | --- |
| 第一波 | Interaction 模块本体（D1–D5、D7）；Dialogue 迁移与 verb（D10）；Loot 迁移（D8） | opus | 新的 Interaction 测试（选择、让位、本帧防误触、优先级打平）；`DialogueInteractableTests`；Loot 迁移后的测试 | Dialogue：焦点、HUD、按键链路迁走了。Exploration：箱子的焦点与提示改走统一 HUD，回放里有断言。Quest：回放里和 NPC 对话走统一焦点。Battle：BOSS 经 handover 进入战斗，动词改为「挑战」 |
| 第二波 | World 传送点（D9）；Stealth 归属（D6）；小人转向（D11）；文档 | opus | `PortalAnchorTests`、`ExecutionInteractorTests`、桥接组件测试 | World：按键型传送点改走焦点。Stealth：F 键归属规则变了。Dialogue：交互时玩家转向 NPC，截图看效果 |

D11 依赖 roadmap D6 先交付的 `ChibiPuppet.FaceTowards` / `PlayInteractPulse`。执行顺序是 B4 → D6 → A3 第一波 → A3 第二波，因为它们共用同一个 Unity 编辑器，只能串行。

文档（第二波收尾）：
- 用 `/generate-doc interaction` 生成三件套。
- 更新 dialogue、loot、world、stealth、battle 各模块 guide 里关于交互的段落。
- roadmap A3 / D6 行。
- `docs/modules/` 的策划说明里，凡写「E 打开物资箱」「对话提示」的地方核对一遍。

## 7. 风险与回滚

- **改名丢引用**：必须 `git mv` 连同 `.meta` 一起移，并且类名与文件名同时改。改完在 SampleScene 和预制体里抽查组件是否 Missing。Addressables 地址同步更新。
- **D3 信号有漏网**：可能存在 Gameplay 图开着、但本不该有焦点的时刻。靠第一波的回放兜住；发现了就在 D3 补一条让位条件，不要回退到依赖对白服务。
- **D2 行为变化**：箱子离得近时会抢过 NPC 的焦点。这是有意为之。如果回放里有断言依赖旧行为，改断言，不改规则。
- **回滚**：每波单独成提交，按波 revert 即可。

## 8. 执行修订

第一波（2026-10-08，opus 执行）：

1. **D3 核实**：对白（`DialogueService.cs:140`）、演出（`PerformanceService.cs:283`）、任务面板（`QuestPanelController.cs:90`）、镜碎页（`MirrorCrackPresenter.cs:185`）都会关 Gameplay 图，无例外。
   小窗口：任务面板 / 背包面板在 `await ui.OpenAsync` 之后才关图，打开动画那几帧焦点仍在；旧实现同样存在（旧焦点根本不看面板），不算回归，不加让位条件。
   `input.Actions == null`（输入未初始化）按「Gameplay 图未启用」处理，即让位。
2. **D5 两个 CanInteract**：`DialogueInteractable` 公开的 `CanInteract` 仍含距离（头顶标记在用，规则不变）；契约 `IInteractable.CanInteract` 显式实现、不含距离。
   统一焦点按 `interactRadius` 从 `InteractionActor` 测距；Inspector 的 `actor` 覆盖只影响公开 `CanInteract` / `InRange` 与直接调 `Interact()` 的守卫（SampleScene 里老者 / 旅人的 `actor` 就是玩家，等价）。
3. **D8 箱子参数**：`SupplyCrate` 不注入服务，由 `LootSceneBinder` 经 `BindInteraction(radius, prompt, collect)` 下发半径、提示与开箱回调（`LootService.TryCollect`）。
   `LootConfig.promptText`（「E 打开物资箱」整句）拆成 `promptVerb`「打开」+ `promptName`「物资箱」，键位徽章交给 HUD。半径 ≤ 0 的语义随 D5 变成「不限距离」（旧实现是「永不成焦点」），当前配置 1.5 不受影响。
4. **玩家锚点**：Loot、Narrative 改从 `IInteractionRegistry.Actor` 取；`DialogueSceneBinder.Actor` 保留为转发属性，只为 Quest 的 `QuestSceneBinder` 少改一处（Quest 不在本波范围）。
   登记表的 Start 与 `DialogueSceneBinder` 的先后不固定，后者订阅 `OnActorChanged` 补测距角色；`InteractionInstaller` 仍排在 `DialogueInstaller` 之前。
5. **埋点**：`focus_changed` 迁到模块 `interaction`，保留 `target`，`id`（对话树）去掉、改记 `verb`；新增 `interacted` / `interact_ignored`（焦点本帧刚变）/ `hud_open_failed`。迁移记录写在 `docs/telemetry.md` 第 2.2 节末。
6. **探索 HUD**：`SetPrompt` 删后无人使用，连同 `ExplorationHudView.interactPrompt` 字段与预制体子物体 `ControlsRoot/InteractPrompt` 一起删掉（只删不改别的节点）。
7. **选择打平**：「距离相等」按 `sqrMagnitude` 精确相等判；优先级也相等时取登记表里靠前的（旧对白焦点同为先到先得）。

第二波（2026-10-08，opus 执行）：

1. **D9 目的地名**：查表结论——`TbPortal` 没有显示名字段，`TbScene` 有 `display_name`（「泾阳」「镜中妖界长安·坊市」）。提示名字取**目标场景**的 `display_name`，由 `WorldSceneDriver` 登记时经 `PortalAnchor.SetDestinationName` 下发（组件不引用表生成物）；查不到只显示动词，不为此新增表字段。动词做成 `PortalAnchor.interactVerb` 字段（默认「前往」），与 D10 的 `DialogueInteractable.verb` 同一做法。
2. **D9「成为焦点 = 进入范围」**：按键型出口不再由 Driver 轮询范围，`IInteractable.OnFocusChanged(true/false)` 转成 `NotifyEntered/NotifyExited`，按键时焦点调 `Interact()` → `TryInteract()`；契约 `CanInteract` 不含距离（只看按键型 / 有目标 / 没触发过）。范围从 XZ 变成焦点的三维距离：灰盒里出口与玩家根同高（y 0），等价。`requirePlayerInRange = false` 映射为半径 ≤ 0（不限距离）。`WorldSceneDriver` 去掉 `IInputService` 依赖，`WorldInstallerTests` 相应改为注册登记表、去掉输入桩。`Runtime` 里 `Gameplay/Interact` 只剩 `InteractionFocus.cs:225`（`LootConfig` 一处 Tooltip 文字也改掉了，免得 grep 误报）。
3. **世界场景补玩家标记**：`HumanJingyang` / `YaoFangshi` 的 `Player` 根原来没有 `InteractionActor`，按键型出口在世界里会永远让位；经编辑器 API 各加一个（场景 diff 只多一个组件 + `interactVerb` 序列化值）。
4. **D6 埋点形式**：没有新增事件，在已有拒绝原因里加值 `ExecutionReject.YieldedToInteraction`（原因码 `interaction_focus`，同 `NoTarget` 是交互层专用值），埋点仍是 `stealth_execute_rejected`，另带 `focus` = 焦点对象名。按键路径抽成公开的 `ExecutionInteractor.HandleExecuteKey`（`Update` 调它，测试直接调）；`Inspect` 同口径，面板与按键给同一原因。焦点经 `MonsterEncounterState.BindExecution` 的 `container.TryResolve` **可选**取（同 `StealthKernel`：纯 Monster 的测试作用域没有 InteractionInstaller），`StandaloneEncounterController` 不传，不做裁决。
5. **D11 与战斗**：选「让 `BattleActor` 用立即翻面覆盖」，且现状已满足，**`BattleActor` 一行没改**：战斗场景的两只小人是另一份实例（BOSS 按 `StagePrefab` 现实例化，玩家是 `BattleArena` 里摆好的），`BattleActor.BindVisual` 关掉 `ChibiPuppetMotion`（朝向保持唯一的读方），`ResetPose` 再 `SetFacing(faceLeft, true)`。Battle 回放断言了「不是同一实例、无保持、朝向等于 `BattleActor.FaceLeft`」。表现组件不判「玩家站定」：对白类交互立刻时停、时停分支不解除保持；不时停的交互（开箱）边走边按会在下一个采样窗口被移动解除。
6. **WARN 1 修法**：`InteractionRegistry.HandleSceneUnloaded` 在玩家标记成伪空时重扫已加载场景，找到就直接换成新标记（只发一次 `OnActorChanged`），找不到才置空。扫描函数可由构造注入（EditMode 下 SceneManager 扫到的是编辑器开着的场景），因此 `InteractionInstaller` 改为工厂注册（两个构造按类型注册时 VContainer 会挑参数多的那个）。回放实测：World 两次换图、Exploration 重置的玩家标记序列都是「空 → 新场景玩家」（旧场景先卸），反向时序这次没复现，由 `InteractionRegistryTests.SceneUnloaded_OldActorDestroyed_RescansAndFindsTheNewSceneActor` 钉住。
7. **WARN 2 测试落点**：登记表 11 条在 `InteractionRegistryTests`；「焦点对象被销毁不回调」测的是 `InteractionFocus` 的行为，放在 `InteractionFocusTests`（复用它的假 HUD / 暂停 / 输入）。
8. **收尾**：`QuestSceneBinder` 仍要 `DialogueSceneBinder`（TalkTo 目标从 `Bound` 找 NPC），只把玩家锚点换成 `IInteractionRegistry.Actor`；`DialogueSceneBinder.Actor` 已删。提示宽度在代码里按文字算（`ResolveWidth` 纯函数 + `FitWidth`，`minWidth` / `maxWidth` 序列化字段默认 320 / 640，预制体不改）。**坑**：`Root` 在预制体里默认未激活，第一次 `Show` 时先量宽后激活，文字组件没初始化，量出来退回最小宽度（World 回放截到「前往 · 镜中妖界长安…」）；改成先激活再量。
9. **回放范围事故**：第一次跑 World 时 `group_names=["World"]` 按子串匹配，带出了两条名字含 World 的演出回放（`PlayById_ShowsSubtitlesAndRestoresWorld` PASS、`VillageEntrance_WorldStageTalkAndRestore` 在「自动」推进处超时，与交互无关），并把 World 本身弄脏；之后一律用锚定正则 `^Game\.Tests\.Showcase\.<模块>\.`，World 重跑 PASS。world guide 的跑法同步改了。
10. **Stealth 回放 FAIL 为既有漂移**：两条绕背用例在潜行接近途中被打到镜碎（第二只巡逻怪「巡逻者甲」，`3396ca8` 合入 `MonsterRules.CreateForActor` 之后），发生在按 F 之前，与交互无关，同 Mirror 的 4 条既有失败；本波新增的接线断言（组件拿到的是容器里那份焦点）通过。「交互焦点与处决目标同时在场」的局面这张图里没有，没有硬造，由 `ExecutionInteractorTests` 覆盖。
