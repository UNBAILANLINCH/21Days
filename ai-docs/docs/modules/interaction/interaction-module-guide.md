---
type: module-guide
module: interaction
layer: runtime
maturity: stable
---

# Interaction 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Interaction/` 之前读这份。设计见 [`PRP/interaction/prp.md`](../../../../PRP/interaction/prp.md)
> （第 3 节决策 D1–D11，第 8 节两波执行修订）；与源码不符时以源码为准。
> 别的模块要读焦点 / 拿玩家锚点 → [`interaction-external-api.md`](interaction-external-api.md)；
> 新加一种可交互物 → [`interaction-extension-guide.md`](interaction-extension-guide.md)。

## 职责边界

「走近 → 出提示 → 按键触发」全工程只有这一套（roadmap A3，2026-10-08 两波落地）。

| 做 | 不做 |
| --- | --- |
| 统一选焦点：全局任一时刻至多一个焦点，屏幕上只有一条提示，一次按键只触发一件事 | 认识任何具体的可交互类型（对白、箱子、传送点都只是 `IInteractable` 的实现方） |
| 全工程唯一读 `Gameplay/Interact`（E / F / 手柄 South）；底部提示 HUD「[E] 动词 · 名字」，点提示与按键同一判定 | 进入范围即触发的东西：演出触发、剧情触发、`EnterRange` 型传送点（各自模块轮询） |
| 玩家锚点（`InteractionActor`）：扫已加载场景找到它，场景卸载后重扫 | 各模块的头顶标记（显隐规则各不相同，留在实现方，契约只给焦点变化回调） |
| 交互那一刻的小人表现：玩家小人转向目标 + 交互动作，目标小人转向玩家（D11） | 处决：不进统一焦点，只在 `ExecutionInteractor` 里按焦点做按键归属裁决（D6） |
| 让位：没有玩家锚点 / Gameplay 图关着 / 沉浸 / 世界暂停时没有焦点 | 键位绑定、提示美术、各模块存档（Loot 已开、Narrative 已消耗各存各的） |

## 运行时类分工

| 类型 | 文件 | 职责 |
| --- | --- | --- |
| `IInteractable` | `IInteractable.cs:13` | 契约（D5）：位置、半径（≤ 0 不限距离）、`CanInteract`（**不含距离**）、优先级、`Prompt`、`Interact()`、`OnFocusChanged(bool)` |
| `InteractionPrompt` | `InteractionPrompt.cs:7` | `readonly struct`：动词 + 名字，取值无分配 |
| `InteractionActor` | `InteractionActor.cs:12` | 玩家根上的空标记（测距原点），由 `DialogueInteractionActor` 迁入、保 GUID |
| `IInteractionRegistry` / `InteractionRegistry` | `InteractionRegistry.cs:23` | 候选登记 / 注销（按引用比较，`:71`、`:77`）；启动时扫已加载场景找玩家标记，每次 `sceneLoaded` 扫新场景；`HandleSceneUnloaded`（`:96`）清已销毁候选，玩家标记成伪空时重扫 |
| `InteractionSelector` | `InteractionSelector.cs:17` | 静态纯函数 `Select`：可用 + `CanInteract` + 半径内，三维距离最近；等距按优先级大者胜，再相等取登记靠前的；无分配 |
| `IInteractionFocus` / `InteractionFocus` | `InteractionFocus.cs:27` | 入口点（`ITickable`）：让位判定（`ShouldYield`，`:70`）→ 选焦点 → 焦点变化回调 + HUD + 埋点（`SetFocus`，`:143`）→ 读交互键 → 本帧焦点没变才 `Interact()` 并抛 `OnInteracted`（`TryInteract`，`:162`–`:174`） |
| `InteractPromptHudView` | `InteractPromptHudView.cs:25` | `UIView`（Hud 层）：底部居中胶囊「[键位] 动词 · 名字」，常驻打开、显隐只切 `root`；宽度随文字在 320–640 之间伸缩（`ResolveWidth`，`:106`；`FitWidth`，`:113`）；整条是按钮，抛 `OnInteract` |
| `InteractionPuppetPresenter` | `InteractionPuppetPresenter.cs:31` | 入口点：订阅 `OnInteracted`，玩家小人 `FaceTowards(目标)` + `PlayInteractPulse`，目标层级里的小人 `FaceTowards(玩家)`（`Present`，`:64`） |
| `InteractionInstaller` | `InteractionInstaller.cs:25` | `GameplayInstaller`：注册以上三个入口点（`:33`、`:35`、`:44`），挂 Boot 的 `GameBootstrap`，排在 `DialogueInstaller` 之前 |

实现方（登记方）一览：

| 模块 | 实现类 | 谁登记 | 动词 / 名字 |
| --- | --- | --- | --- |
| Dialogue | `DialogueInteractable`（`DialogueInteractable.cs:27`） | `DialogueSceneBinder`（`DialogueSceneBinder.cs:107`） | `verb` 字段（默认「对话」，BOSS「挑战」）· `displayName` |
| Loot | `SupplyCrate`（`SupplyCrate.cs:19`） | `LootSceneBinder`（`LootSceneBinder.cs:128`） | `LootConfig.promptVerb`「打开」· `promptName`「物资箱」 |
| World | `PortalAnchor`，只登记按键型（`PortalAnchor.cs:43`） | `WorldSceneDriver`（`WorldSceneDriver.cs:228`） | `interactVerb`（默认「前往」）· 目的地 `TbScene.display_name` |

## 一帧的数据流

```text
InteractionFocus.Tick（每帧，InteractionFocus.cs:83）
  ├─ 取 input.Actions：Gameplay 图开没开、Interact 本帧按没按（全工程唯一读处，带 lint-ok）
  └─ Advance(gameplayEnabled, pressed)
       ├─ ShouldYield(有无 Actor, Gameplay 图, 沉浸, 世界暂停) → 让位则 next = null
       ├─ 否则 next = InteractionSelector.Select(Actor.Anchor.position, registry.Candidates)
       ├─ next 与 Current 引用不同 → SetFocus：旧.OnFocusChanged(false)（旧的已销毁则跳过）→ 新.OnFocusChanged(true)
       │     → HUD Show(Prompt)/Hide → 埋 interaction/focus_changed → OnFocusChanged 事件
       └─ pressed → TryInteract("key")：本帧焦点刚变 → 埋 interact_ignored 不响应（D4）；
             否则 埋 interacted → Current.Interact() → OnInteracted 事件
                                                         └─ InteractionPuppetPresenter.Present（转向 + 交互动作）
HUD 点击 → RequestInteract：同一让位判定 + TryInteract("hud")
```

## 关键决策（源码读不出来的部分）

- **最近者得焦点（D2）**：废掉了「箱子永远让位对白」。统一后只有一条提示，原来让位的理由（两条提示同位重叠）没了；
  顺带修掉 `interactRadius ≤ 0` 的 NPC 永久压住所有箱子。将来真要某类优先，改 `InteractionPriority`（只在距离精确相等时打平）。
- **让位只看通用信号（D3）**：对白、演出、任务面板、镜碎页都会关 Gameplay 图，焦点不必认识对白服务。
  `input.Actions == null`（输入未初始化）按「Gameplay 图未启用」处理。任务面板 / 背包面板打开动画那几帧焦点仍在，旧实现同样存在，不算回归。
- **本帧焦点刚变不响应按键（D4）**：防止「关掉对白的那一下顺手开箱」。
- **F 键归交互（D6）**：`Interact` 绑 E / F / South，`Execute` 绑 F / South。屏幕提示什么，按键就做什么——
  焦点非空时 `ExecutionInteractor.HandleExecuteKey` 让位（`ExecutionInteractor.cs:199`），只记一条拒绝 `reason = interaction_focus`。
  要反过来让处决优先，只改那一处。
- **按键型传送点「成为焦点 = 进入范围」（D9）**：`PortalAnchor` 契约不读输入、不查物理，范围本来就由外部喂；
  统一后按键型的「外部」就是焦点系统——`OnFocusChanged(true/false)` 转成 `NotifyEntered/NotifyExited`（`PortalAnchor.cs:213`），
  按键时焦点调 `Interact()` → `TryInteract()`（`:207`）。进入范围型仍由 `WorldSceneDriver.UpdatePortals` 按 XZ 距离轮询（`WorldSceneDriver.cs:170`）。
- **玩家标记卸载后重扫（第二波 WARN 1）**：流程层卸旧场景用 Addressables 异步卸载、不等，紧接着加载新场景；
  新场景的 `sceneLoaded` 可能先于旧场景的 `sceneUnloaded`——那时旧标记还活着，`FindActor` 跳过新标记；旧标记随后卸载成伪空，
  置空后若不重扫就再也没人找，交互键整个失灵。现在 `HandleSceneUnloaded` 找到新标记就直接换（只发一次 `OnActorChanged`），找不到才置空。
- **登记表用工厂注册**：`InteractionRegistry` 另有一个给测试注入扫描函数的构造（EditMode 下 SceneManager 扫到的是编辑器里开着的场景），
  按类型注册时 VContainer 会挑参数最多的构造而解析失败（`InteractionInstaller.cs:33`）。
- **小人表现放在 Interaction 而不是 CharacterPuppet（D11）**：小人只是「看位移演动画」的皮，「什么时候转向谁」由外部决定；
  只调门面 `FaceTowards` / `PlayInteractPulse`，不写 Animator 参数，单驱动者约束不变。小人按事件现找（交互是低频事件，不在每帧路径上）。
- **提示宽度自适应**：内容宽 = 布局组左右内边距 + 键位徽章等其它子物体首选宽 + 间距 + 文字首选宽 + 2 px 余量，夹在
  `minWidth`（320）与 `maxWidth`（640，参考分辨率下）之间，超出才由文字的省略号吃掉。只在 `Show`（焦点变化）时算。

## 依赖方向

```text
Game.Interaction ──► Game.Core（Input / UI / Timing / Events / Telemetry / Boot / Logging / Simulation.GameMath）
                 └─► Game.CharacterPuppet（只用 ChibiPuppet 门面，InteractionPuppetPresenter 一处）
Game.Dialogue / Game.Loot / Game.World / Game.Narrative / Game.Quest ──► Game.Interaction（实现契约、登记、取玩家锚点）
Game.Stealth（ExecutionInteractor）、Game.Monster（MonsterEncounterState.BindExecution 可选取焦点）──► Game.Interaction
```

Interaction 不认识任何实现方；CharacterPuppet 不反向认识 Interaction。全在 `Game.Runtime` 一个 asmdef 里，方向靠约定守。

## 小人表现的边界（D11）

- 玩家小人从 `InteractionActor` 所在层级里找（`GetComponentInChildren`）；目标小人先找目标组件的自身与子物体，再找父链。
  找不到就跳过，不报错：灰盒世界场景的玩家只有方块，物资箱与传送点没有小人。
- 朝向保持由小人自己在「真正移动」时解除：玩家走开就转回纸片朝向；NPC 不走动，会一直面向上次交互时玩家的位置，直到场景重载。
- 只转表现层：隐藏纸片的 `flipX`、`PlayerModel` / `MonsterModel` 的 `Facing` 不变，潜行 / 处决判定按逻辑朝向。
- **不带进战斗**：BOSS 被交互后转向玩家、随即进战斗，但战斗场景 `BattleArena` 里的两只小人是另一份实例（BOSS 按 `StagePrefab`
  现实例化，玩家是战斗场景里摆好的），`BattleActor` 还会关掉它们的 `ChibiPuppetMotion`（朝向保持唯一的读方）并用
  `SetFacing(faceLeft, true)` 立即翻面布台（`BattleActor.cs:134`）。所以既不需要在战斗接管时解除保持，也不需要改 `BattleActor`。
- 交互时玩家正在走：下一个采样窗口就会解除保持并转回纸片朝向；对白类交互会立刻时停，时停分支只写待机、不解除保持。

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `InteractionInstaller`（排在 `DialogueInstaller` 之前；顺序只影响少一次补通知） | 解析不到 `IInteractionRegistry`，Dialogue / Loot / World / Narrative / Quest 全部建不起来 |
| 玩家标记 | 每张玩法场景的玩家根挂一个 `InteractionActor`：SampleScene 的 `player`；世界灰盒 `HumanJingyang` / `YaoFangshi` 的 `Player`（第二波补挂） | 没有焦点、没有提示、交互键无效（让位条件第一条） |
| HUD 地址 | Addressables（UI 组）`InteractPromptHudView` → `Prefabs/UI/InteractPromptHudView.prefab`，**地址等于类名** | 记 Error + 埋 `hud_open_failed`，交互键与点击 NPC 照常可用 |
| HUD 预制体 | `root` / `button` → `Root`（底部居中 `(0, 72)`，高 48，`HorizontalLayoutGroup`，宽度运行时按文字算）；`keyLabel` → `KeyBadge/KeyText`；`label` → `Label`（弹性宽、单行省略号）；`icon` 可空 | 打开时 `Validate` 逐个点名抛出 |
| 实现方登记 | 场景登记器在 `sceneLoaded` 时 `Register`，卸载 / `Dispose` 时 `Unregister`；运行时 `Instantiate` 出来的对象要自己登记 | 不登记就永远不是焦点 |

## 埋点（模块名 `interaction`）

| 事件 | 属性 | 位置 |
| --- | --- | --- |
| `focus_changed` | `target`（物体名）、`verb` | `InteractionFocus.cs:158`，只在变化时 |
| `interacted` | `target`、`verb`、`via`（key / hud） | `InteractionFocus.cs:172` |
| `interact_ignored` | `target`、`via`、`reason`=focus_changed_this_frame | `InteractionFocus.cs:169` |
| `hud_open_failed`（Error） | 异常 | `InteractionFocus.cs:200` |
| `puppets_turned` | `target`、`player`（找到玩家小人没）、`npc`（找到目标小人没） | `InteractionPuppetPresenter.cs:88` |

F 键让位记在模块 `stealth`：`stealth_execute_rejected`，`reason = interaction_focus`、`focus` = 焦点对象名（`ExecutionResolver.cs:186`）。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Tests/EditMode/Interaction/InteractionSelectorTests.cs` | 最近、半径外、半径恰好、≤ 0 不限距离、跳过不可交互 / 未激活 / 已销毁 / null、优先级只打平、等距取靠前 |
| EditMode | `.../InteractionFocusTests.cs` | 四条让位各自清焦点且按键无效、本帧焦点刚变按键 / 点击不响应、换焦点那一帧谁都不触发、`OnInteracted` 一次、焦点对象被销毁（伪空）时清焦点但不回调它 |
| EditMode | `.../InteractionRegistryTests.cs` | 重复登记忽略、注销按引用（两个已销毁对象不删错）、`SetActor` 事件含置空与同值不触发、标记先到 / 订阅方先到、卸载后重扫找回新标记 / 找不到置空 / 标记活着不重扫、卸载清已销毁候选 |
| EditMode | `.../InteractionPuppetPresenterTests.cs` | 双方互相转向并保持、父链上的小人、目标没小人只转玩家、纯 C# 目标、玩家没小人时 NPC 照转、没有玩家标记谁都不转、`Dispose` 退订 |
| EditMode | `.../InteractPromptHudViewTests.cs` | 键位回退「E」、「动词 · 名字」拼接、宽度夹取 |
| EditMode | `Tests/EditMode/World/PortalAnchorTests.cs`、`Tests/EditMode/Stealth/ExecutionInteractorTests.cs` | 传送点作为可交互物、F 键让位 |
| Showcase | Dialogue（焦点 → 点提示 → 玩家与长者对视）、Exploration（按 E 开箱时玩家交互动作；重置进度后玩家标记重找、交互照常）、World（「前往 · 目的地」、换图后玩家标记是新场景的）、Battle（「挑战」提示不截断、BOSS 转向玩家、战斗小人不带保持）、Stealth（按 F 时焦点为空、焦点已接线） | 见各模块 guide |

跑法：`/unity-test EditMode`，组过滤 `^Game\.Tests\.EditMode\.Interaction\.`；回放按模块逐个跑（`run_tests` 的 `group_names` 用锚定正则，
例如 `^Game\.Tests\.Showcase\.World\.`——只写 `World` 会匹配到名字里带 World 的演出回放）。

## 已知约束

- **只显示键盘键位**：徽章取 `Interact` 第一条 `<Keyboard>` 绑定的显示串（`InteractionFocus.cs:231`），打开时赋一次；手柄为主时不切换。
- **面板打开动画那几帧焦点仍在**：任务 / 背包面板在 `await ui.OpenAsync` 之后才关 Gameplay 图（PRP §8 第一波第 1 条）。
- **NPC 被交互后一直面向那个方向**：NPC 不走动，朝向保持不会自己解除；要「说完话转回去」得在小人门面加解除入口，本模块不做。
- **按键型传送点的范围是三维距离**：世界灰盒里出口与玩家根同高（y 0），与旧的 XZ 判定等价；出口摆高了要相应放大 `triggerRadius`。
- 处决没有提示 UI，所以 F 让位只能从埋点与白盒面板（`Inspect` 报 `YieldedToInteraction`）看出来。

## 禁止事项

- 不要在别处再读 `Gameplay/Interact`：`Runtime` 里只许 `InteractionFocus.cs:225` 一处（grep 可查）。不要复用 `Confirm`（确定性输入位）。
- 不要在实现方里自己选焦点、自己读键、自己开提示：实现契约 + 登记，其余交给焦点系统。
- 不要在实现方外部写 `Focused` 一类状态：只有焦点系统经 `OnFocusChanged` 回调能写。
- 不要让焦点系统每帧 `Find`：候选只从 `IInteractionRegistry.Candidates` 读。
- 不要在 `InteractionPuppetPresenter` 里写 Animator 参数或改小人位置：只调 `ChibiPuppet` 公开门面。
