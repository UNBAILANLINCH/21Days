# PRP：探索层白盒示例（对标「安洁莉娜的旅行小记」）

> 状态：2026-09-26 第二版。第一版波 1 执行中与另一会话（roadmap W1 的 A1 / A2 / B2 / D4 / D5）撞车，已协调分工：**底层契约以对方为准，本 PRP 只做对方不做的部分**。对应 `docs/roadmap.md`：A5 摇杆正式 UI + 走跑按钮、A3+B1 的白盒版（物资箱 → 奖励 → 最小背包分区）、R6 万向标扩到多目标、R10 重置的白盒版（内存重置 + 重进场景）、SampleScene 白盒接线与回放。
> 参考：B 站 BV1cQ3m6PE4j（剧情录屏）、BV1mQ4R6gEfj（UI 归档）。只学探索交互与 HUD 形态，不学塔防养成。

## 1. 上下文快照（2026-09-26）

- 场景 `Assets/Scenes/SampleScene.unity`（Addressables 地址 `IsometricEncounter`）由 Boot → 标题「开始」→ `MonsterEncounterState` 进入；玩家位置由确定性遭遇规则推进：`LiveInputSource` 采样 → `InputCommand`（按钮位掩码）→ `EncounterStep` → `PlayerIntent` → `PlayerRules` → `EncounterSceneView` 投影到 XZ。直接 Play 场景时由 `StandaloneEncounterController` 驱动。
- 已有：Dialogue（NPC 头顶标记、范围交互、右下角对话按钮、气泡）、Quest（左上任务栏、面板、屏内头顶标记 / 屏外贴边箭头 `QuestGuidanceMath`）、CharacterPuppet。
- **对方会话（21days-13）正在落地、本 PRP 直接依赖的契约**（到其「编译零错误 + EditMode 全绿」稳定点后才可用）：
  - 走跑：`Gameplay/Run`（`<Keyboard>/leftCtrl`、`<Gamepad>/leftStickPress`）→ `InputCommand.ButtonRun` → `PlayerIntent.Run`（必填）→ `PlayerRules` 按**按下沿**切换 `PlayerModel.IsRunning`（潜行按住时临时压过）；进快照 / 存档 / 回放 v4；`PlayerConfig.RunSpeed`。`LiveInputSource.HeldButtons` 保留为软件按钮位。
  - 沉浸：`IHudVisibility.SetHudHidden(bool)` / `IsHudHidden`（`UIService` 实现）+ `HudVisibilityChangedEvent(bool Hidden)`；`UIView.VisibleWhenHudHidden`。沉浸按钮、`Gameplay/Immersive` 绑定、Dialogue / Quest 世界标记跟随隐藏都由对方做。
  - 探索 HUD 壳：`Runtime/IsometricExploration/` 下 `ExplorationInstaller`（挂 Boot）、`ExplorationHudView`（预制体 `Prefabs/UI/ExplorationHudView.prefab`，地址同名；左下沉浸按钮，右下预留空的 `RunSlot`）、`ExplorationHudPresenter`。
  - 通知：`INotificationService.Show(title, body, seconds)`（`NotificationView`，Top 层）。对方删除本会话留下的 `ToastView`。
  - `IUIService.SetLayerVisible` 保留为整层开关通用能力（本 PRP 不用）。
- 本会话已落地、归本 PRP 的：`QuestService.ResetProgress()` + `QuestServiceTests`、`Tables/Data/quest/2002.json`（支线「清点营地物资」，Counter key `crate` ×3）+ 重生成的 `quest_tbquest.bytes`、`MonsterEncounterState` 去掉触屏控件创建（`EncounterTouchControls.cs` 待 `git rm`）。
- 存档：分区机制齐备但没有落盘 / 读盘调用，进度只活在内存；游戏级存档会话归 roadmap E1，本 PRP 不做。
- 工作区他人未提交改动（字体资产等）不碰；提交按文件挑，不整份暂存。

## 2. 目标与范围

做成一个**可从 Boot 进入、可在 SampleScene 里完整走一遍的白盒**：摇杆 / 方向键走动，右下角走跑切换按钮，左下角沉浸模式（对方做），左上角任务便签（已有）+ 重置进度，NPC 带标志推进对话（已有），屏幕四周万向标指向所有屏外兴趣点，三只物资箱可打开得奖励并推进支线 2002，重置后物资箱与任务回到初始。

不做：泛化 `IInteractable` 重构 Dialogue（roadmap A3 正式版）、背包面板、落盘存档、多场景、动效。

## 3. 架构决策（契约，执行时不得偏离；要改先改这里）

### 3.1 新模块 `Game.Loot`（`Scripts/Runtime/Loot/`）

为什么新建：物资箱拾取 / 奖励 / 背包分区没有归属；Quest 管任务、Dialogue 管对白、IsometricExploration 管场景表现与探索 HUD 壳。依赖方向：Loot → Quest / Dialogue / Core；IsometricExploration → Loot（HUD 读焦点与提示）；反向禁止。

| 类 | 职责 / 契约 |
| --- | --- |
| `LootConfig`（SO，`Data/Loot/LootConfig.asset`） | `CrateInteractRadius`(1.5)、`CrateQuestKey`("crate")、`PromptText`("打开物资箱")、`RewardTitle`("获得物资")、`RewardBodyFormat`("{0} ×{1}"，{0}=物品名 {1}=数量)、`MarkerLift`(0.3)。（波 2 定稿：标题 / 正文拆开，对应 `INotificationService.Show(title, body)`。） |
| `LootInstaller : GameplayInstaller` | 注册 `LootService`(IGameService)、`LootSceneBinder`、`SupplyCrateFocus`；`InstallEvents` 注册 `CrateCollectedEvent`、`LootResetEvent`。挂 Boot 的 `GameBootstrap`，排在 `QuestInstaller` 之后、对方 `ExplorationInstaller` 之前。 |
| `LootService : IGameService, IDisposable` | `bool TryCollect(SupplyCrate)`：按 `Key` 幂等（已开返回 false）；`LootRules.Collect` 写 `LootSaveData`；`quest.Report(Counter, config.CrateQuestKey)`；`crate.SetOpened(true)`；`notifications.Show(标题, 正文)`（物品名查 `tbitem`，查不到用 id）；发 `CrateCollectedEvent(key, itemId, count)`。`void Reset()`：清空分区、发 `LootResetEvent`。`IsCollected(string key)`、`IReadOnlyDictionary<int,int> Items`（键 = tbitem id，与 `LootSaveData` 一致）。分区每次操作重新 `saves.Get<LootSaveData>()`，不缓存实例（读档 Commit 会替换分区）。 |
| `LootSaveData : ISaveData` | `Version 1`；`List<string> CollectedCrates`、`Dictionary<int,int> Items`（键 = tbitem id）。 |
| `LootRules`（纯 C#） | `Collect(data, key, itemId, count) → bool`（幂等 + 累加）、`Reset(data)`、`IsCollected`。EditMode 测试。 |
| `SupplyCrate : MonoBehaviour` | `[SerializeField] crateKey(string), itemId(int, tbitem 主键), count(int), closedVisual(GameObject), openedVisual(GameObject)`；`Key / ItemId / Count / IsOpened / Position / SetOpened(bool)`。奖励是表驱动引用（`tbitem` 的 int id，名字经 `IConfigService.Tables.TbItem.GetOrDefault(id)?.Name`），不写死货币。`LootSaveData.Items` 的键也用 int。 |
| `SupplyCrateMarker : MonoBehaviour` | 箱子头顶世界空间标记（子物体 SpriteRenderer + `CameraBillboard`），未开显示、已开隐藏（经 `SupplyCrate.OnOpenedChanged` 事件，OnEnable/OnDisable 成对订阅）；沉浸时隐藏——MonoBehaviour 不注入服务，由 `LootSceneBinder` 订阅 `HudVisibilityChangedEvent` 后逐个调 `SetHudHidden(bool)`。 |
| `LootSceneBinder : IStartable, IDisposable` | 照 `QuestSceneBinder`：`sceneLoaded` 扫描 `SupplyCrate`（含未激活），按 `LootSaveData` 恢复 `SetOpened`；暴露 `Crates`；`LootResetEvent` 时全部 `SetOpened(false)`。 |
| `SupplyCrateFocus : ITickable, IDisposable` | 玩家锚点取 `DialogueSceneBinder.Actor.Anchor`；最近且在半径内的未开箱子为 `Current`；`Gameplay/Confirm` 按下时 `TryCollect`（同 `DialogueInteractionFocus` 每帧读 `WasPressedThisFrame`，`input.Actions` 为空时容错；焦点刚变化的那一帧不响应确认）。让位：`DialogueInteractionFocus.Current != null`、`DialogueService.IsRunning`、世界暂停中、`IHudVisibility.IsHudHidden` 时 `Current = null` 且不响应。`event Action<SupplyCrate> OnFocusChanged`。 |

### 3.2 扩展对方的探索 HUD（`Runtime/IsometricExploration/`，稳定点后再动）

| 项 | 契约 |
| --- | --- |
| `ExplorationHudView`（对方的 View 类 + 预制体） | 只**追加**序列化字段与访问器，不改既有逻辑：`RunSlot` 内 `RunToggle`（`OnScreenButton` 绑 `<Gamepad>/leftStickPress` + `Image` + TMP 标签）（2026-09-26 用户决定 PC 优先：默认仅触屏显示，代码与预制体保留）；`Stick`（左下，`OnScreenStick` 绑 `<Gamepad>/leftStick`，movementRange 95）（2026-09-26 用户决定 PC 优先：默认仅触屏显示，代码与预制体保留）；`TouchButtons`（潜行 `<Gamepad>/leftShoulder`、伪装 `<Gamepad>/buttonNorth`、攻击 `<Gamepad>/buttonWest`，三个 `OnScreenButton`）（2026-09-26 用户决定 PC 优先：默认仅触屏显示，代码与预制体保留）；`ResetButton`（左上，任务栏下方）；`InteractPrompt`（TMP，屏幕下方居中）；`CompassRoot` + `CompassMarkerTemplate`（Image 箭头 + TMP 标签，运行时隐藏模板）。访问器：`SetRunLabel(string)`、`SetStickVisible(bool)`、`SetTouchButtonsVisible(bool)`、`SetPrompt(string)`、`ResetClicked` 事件、`CompassRoot`、`CompassMarkerTemplate`、`CanvasSize`。 |
| `ExplorationControlsPresenter : IStartable, ITickable, IDisposable`（新） | 等 `ui.Get<ExplorationHudView>()` 非空后绑定：走跑标签每帧读 `PlayerModel.IsRunning`（潜行时仍显示模式）；摇杆 / 触屏三键 / RunToggle 统一按 `IPlatformService.IsTouchPrimary || config.ShowStickOnDesktop` 显示（2026-09-26 用户决定 PC 优先：默认仅触屏显示，代码与预制体保留），PC 走跑走 `Gameplay/Run`（左 Ctrl / 手柄左摇杆按下）；`SupplyCrateFocus.OnFocusChanged` → 提示显隐；`ResetClicked` → `ui.OpenAsync<ExplorationConfirmView>()` → 确认后 `quest.ResetProgress()`、`loot.Reset()`、`flow.GoToAsync<MonsterEncounterState>()`。 |
| `ExplorationPointOfInterest : MonoBehaviour`（新） | `label`、`kind`(Npc / Crate / Location)；`IsVisible`：Crate 类型取同物体 `SupplyCrate.IsOpened == false`，其余恒 true。 |
| `ExplorationCompassRules`（纯 C#，新） | 输入：兴趣点视口坐标列表、画布尺寸、边距；输出：屏外者的贴边位置与角度（复用 `QuestGuidanceMath.Solve`），屏内者不画。EditMode 测试。 |
| `ExplorationCompassPresenter : IStartable, ITickable, IDisposable`（新） | `sceneLoaded` 扫描 `ExplorationPointOfInterest`；相机 / 玩家锚点取 `QuestSceneBinder.SceneCamera / PlayerAnchor`；每帧按规则摆标记（对象池，复用模板）；订阅 `HudVisibilityChangedEvent` 沉浸时整体隐藏（HUD 本身已隐藏，这里只是省每帧开销）。**2026-09-26 用户决定默认关闭**：新增 `Enabled` 读写属性（初值取 `IsometricExplorationConfig.ShowCompass`，默认 false），关闭时 `Tick` 跳过扫描与摆位——全部兴趣点都标识会显得屏幕乱，任务追踪指引已由 Quest 负责。 |
| `ExplorationConfirmView : UIView(Popup)`（新） | `message`、`confirm`、`cancel`；`OpenAsync(arg: string)`；预制体 `Prefabs/UI/ExplorationConfirmView.prefab`，地址同名。 |
| `ExplorationConfig`（若对方已建则追加字段；否则新建 SO） | `ShowStickOnDesktop`(true)、`CompassEdgeMargin`(48)、`RunLabel`("奔跑")、`WalkLabel`("散步")、`ResetMessage`。 |

> **波 3 定稿（偏离回写）**：① RunSlot 在对方预制体里不在 ControlsRoot 下（移动它会改对方的行），`RunToggle` 自带 CanvasGroup（View 字段 `runToggleGroup`），`SetControlsVisible` 同时切 ControlsRoot 与 RunToggle。② 万向标模板根物体不挂 Image，子物体依次为 `Label`（TMP，始终正立，朝画布中心方向偏移 44 px）和 `Arrow`（Knob Image 加子 TMP「▲」，只旋转箭头）。③ `ExplorationConfig` 没有新建，复用 `IsometricExplorationConfig` 并追加字段，另外多了 `CompassLabelMax` / `ResetConfirmText` / `ResetCancelText`，由 `ExplorationInstaller.config` 注册。④ 控件呈现器不用 `ui.Get` 轮询，改为 Boot 完成后 `OpenAsync<ExplorationHudView>()`（并发打开返回同一实例），构造参数多一个 `IHudVisibility`。⑤ `ExplorationConfirmView` 增加 `SetButtonLabels` 与 `UniTask<bool> WaitAsync(ct)`：被关掉或取消时返回 false，不抛异常。⑥ 埋点：`controls_bound`、`progress_reset`、`progress_reset_cancelled`、`progress_reset_failed`、`poi_bound`、`compass_template_missing`、`compass_open_failed`。⑦ 箱子材质 `Data/IsometricExploration/M_CrateClosed|M_CrateOpened.mat`。Crate_A 放在 (7.5, 5.8)，离村民 3.5 m：村民对白半径为 2，靠得太近会被对白焦点抢走。

### 3.3 场景与资产（全部走 MCP，不手改 YAML）

- `SampleScene.unity`：新增根节点 `Crates` 下 `Crate_A / B / C`（灰盒方块 0.8 m，`Closed` / `Opened` 两个子物体 + `Marker` 子物体，`SupplyCrate` + `SupplyCrateMarker` + `ExplorationPointOfInterest` + `BoxCollider`），分别放在营地、瞭望点、灰盒另一角；三个 NPC 与两个 `QuestLocation` 挂 `ExplorationPointOfInterest`。世界标记的沉浸隐藏走对方的事件订阅，**不改层**。
- `Boot.unity`：`GameBootstrap` 挂 `LootInstaller`，Config 拖 `LootConfig.asset`。
- Addressables UI 组：`ExplorationConfirmView`（地址 = 类名）。
- 不建独立验证场景：白盒示例本身就是 SampleScene，回放走真实 Boot 流程。

### 3.5 多层地图 / 碰撞 / 遮挡（波 9，2026-09-26）

用户反馈：①地图按「旅行小记」多层平台扩展；②遮挡物没有碰撞；③遭遇原型的 OnGUI 调试文字压在左上 HUD 上。

| 项 | 契约 / 取舍 |
| --- | --- |
| 多层地图 | `Environment_Graybox/MultiLevel`：`Ground_East`（地面扩到 x 33）、`Deck_Upper`（x 20..30、z 7..17，顶 7.89、底 7.49）、`Ramp_South`（x 22..25，z −1→7 升 3 m，坡角 20.6°）、`Stairs_West_Step_1..9`（甲板西沿 x 20 下到 x 12.8，z 14..17，每级 0.3）、`Bridge_West`（x 14.5..20、z 9..11.5，桥下净高 2.6）、栏杆 `Railings/*`。全部原生 Cube、Layer `Ground`。`QuestLocation_Lookout` 上甲板 (25, 7.89, 12)，`Crate_B` 上甲板 (28, 7.89, 15)。 |
| 遮挡碰撞 | **表现层白盒**：`EncounterCollision.Slide`（先 X 后 Z `Physics.CapsuleCast`，胶囊覆盖脚底 +0.35..+1.5，半径 0.3，skin 0.02）→ `EncounterSceneView.OnPlayerBlocked(Vector2)` → `EncounterStep.CorrectPlayerPosition`（仅 `IsActive`）回写 `PlayerModel.Position`。`obstacleMask` 默认 0（Disguise / Taming 验证场景不变），SampleScene 勾 `Ground`。**取舍**：障碍不进确定性内核——同机同场景可复现，跨机回放不保证；正式版把关卡障碍数据放进内核后删掉这条回写。怪物不解算。 |
| 瞬移阈值（偏离任务书，新增） | `obstacleTeleportDistance = 1.5`：单帧场景位移超过它视为瞬移（读档恢复、重置、回放 `PlayerRules.Reset` 挪位），不扫掠只贴地。否则从出生点瞬移到坡道脚下会被坡道侧面截停、并把逻辑位置错误回写。代价：卡顿导致单帧位移 > 1.5 m 时可穿过薄障碍（正常帧步行 ≤ 0.1 m）。 |
| 胶囊高度语义（澄清） | `bottomOffset / topOffset` 指胶囊**下沿 / 上沿**离脚底的高度，端点球心各往里收一个半径；若把 0.35 当球心，胶囊下沿只高出脚底 0.05，0.3 的台阶会被当成墙。 |
| 遮挡半透明 | `SceneOccluder`（`fadedMaterial`，切 `sharedMaterial` 引用）挂 `Bridge_West`、`Deck_Upper` 与 8 段栏杆（偏离任务书「栏杆不挂」：回放截图里桥面淡了，1 m 高的深色南栏杆仍把人整个压住）；`OccluderFadePresenter`（`ExplorationInstaller` 入口点）每帧相机 → 玩家胸口（+0.8）`RaycastNonAlloc`（8 个、只打 `Ground`），命中淡出、离开恢复，Collider → 组件查找按 Collider 缓存，`sceneUnloaded` 清缓存。材质 `Art/Materials/Graybox/M_Graybox_Faded.mat`（M_Wall 复制，URP Lit Transparent，Alpha 0.35，关 DepthOnly / ShadowCaster）。构图事实：视线俯角约 40°，离地 2.6 m 的桥挡住的是它北侧 2～3 m 的人，站桥正中下方相机看得见——回放改用 (17.25, 12.3)。 |
| 调试文字 | `EncounterSceneView.OnGUI` 两行状态 + 警戒条挪到右上「返回标题」按钮下方（x = 屏宽 − 16 − 480，y 64 / 92 / 124，右对齐）；按钮不动。 |

> **波 10 追加（2026-09-26，遮挡淡出通用化）**：用户反馈走西侧楼梯时前景 `Tower` 挡住半个画面不淡。① 探测改粗射线：`Physics.SphereCastNonAlloc`，半径 `IsometricExplorationConfig.OccluderProbeRadius`（默认 1.0），终点胸口前一个半径（纯规则 `OccluderFadePresenter.TryBuildProbe`），16 个结果；地面不挂组件，扫到只缓存成 null。② `SceneOccluder` 追加 `fadeSeconds`（0.15）：淡出立刻换半透明材质，`MaterialPropertyBlock` 把 `_BaseColor` 从「原色 α1」插到半透明材质色，恢复反向插完再换回；呈现器只推进过渡中的对象，终态清属性块。③ 追挂 `Tower`、`Ramp_South`、`Stairs_West_Step_1..9`、`Wall_Left`、`Wall_Back`（共 23 个）。④ 偏离：`ExplorationInstaller` 构造注册多传一个 `IsometricExplorationConfig`（不改就读不到新字段）；回放塔用例站位改 (13.8, 15.5)——(16, 15.5) 实测塔离视线约 1.5 m、视线从塔顶上方越过，不压人。⑤ 设计约束（写进 guide「关卡设计约束」）：相机在南、俯角 38°、FOV 28，高于 1.5 m 且在可行走区南侧的物体必须挂 `SceneOccluder`。

## 4. 验收标准（每条被 tasks.md 覆盖）

| # | 标准 | 判法 |
| --- | --- | --- |
| V1 | 方向键 / 摇杆走动；点右下角按钮或按 Run 键后同样输入位移更大，标签在「散步 / 奔跑」间切换；潜行时不加速 | Showcase 量位移 + 肉眼 |
| V2 | 桌面下触屏控件（摇杆 / 三键 / 走跑按钮）全部隐藏；`ShowStickOnDesktop` 打开时显示 | Showcase |
| V3 | 屏外兴趣点在屏幕边缘有万向标（NPC / 箱子 / 地点），进入屏内消失；沉浸时消失 | Showcase + 肉眼 + EditMode（CompassRules） |
| V4 | 靠近未开箱子出现「打开物资箱」提示，确认后箱子变开、标记消失、顶部通知「获得 X ×N」、支线 2002 计数 +1；再次确认无效；三只全开支线完成 | Showcase + EditMode（LootRules） |
| V5 | 重置：确认后任务回到初始（主线 1001 追踪、2002 计数 0）、箱子全部闭合、背包清空、场景重进 | Showcase |
| V6 | 既有回放 IsometricExploration / Quest / Dialogue 仍 PASS；EditMode 全绿 | `/verify-module`、`run_tests` |
| V7 | project-lint 零违规、`invariants.py` 零报错、code-reviewer 无 BLOCK；三件套登记 | 脚本 + 审查 |

## 5. 风险与回滚

- 与对方会话的交织：稳定点前不动 `.cs` / 资产；开工前先 `git status` + `ListAgents`；改对方的 `ExplorationHudView` 只追加不重写。
- 巡逻怪仍在场景中：不动巡逻点（IsometricExploration 回放依赖）；Showcase 步骤短、贴出生点。
- `OnScreenButton` 的走跑切换依赖按下沿：按住不放只切一次，符合对方规则；连点会来回切，属预期。
- 回滚：Loot 模块整目录、IsometricExploration 的追加文件、场景里的 `Crates` 节点与兴趣点组件，按文件 `git checkout` / 删除即可。
