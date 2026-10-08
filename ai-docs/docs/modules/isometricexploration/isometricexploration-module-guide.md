---
type: module-guide
module: isometricexploration
layer: runtime
maturity: stable
---

# IsometricExploration 模块指南

> 探索 HUD 不显示交互提示：物资箱、NPC、传送点的提示统一由 `Game.Interaction.InteractPromptHudView` 显示（统一交互，
> [`PRP/interaction/prp.md`](../../../../PRP/interaction/prp.md) D8；原 `InteractPrompt` 一行、`ExplorationHudView.SetPrompt` 与订阅物资箱焦点那段已于 2026-10-08 删除）。

## 目的

IsometricExploration 是 `SampleScene` 中的 2.5D / 3D 混合原型。
当前由确定性遭遇规则承载移动，2D Sprite 纸片保持与摄像机成像平面平行。早期 3D 物理控制器保留在工程中，但不参与当前场景推进。

当前解决四个问题：

- 角色通过 `EncounterStep` 在逻辑 XY 移动，再投影到场景 XZ；
- Sprite 纸片随摄像机倾角旋转，但逻辑碰撞体保持竖直；
- 摄像机保留初始构图偏移，并在角色停止时平滑收敛。
- 把 Player/Monster 的确定性 XY 逻辑坐标投影到等距场景 XZ 平面。

这仍是场景表现原型，不是正式探索系统。战斗状态与确定性回放复用 Player/Monster；
本模块不增加背刺处决、障碍物视线、寻路或正式动画；玩家遮挡碰撞只有波 9 的表现层白盒实现（见「已知限制」）。

## 当前组成

| 类型 | 位置 | 职责 |
| --- | --- | --- |
| `CameraBillboard` | `Assets/_Project/Scripts/Runtime/IsometricExploration/CameraBillboard.cs:7` | 旋转纸片，并可校准竖直 `BoxCollider` 的前表面；本次升级中 `NameTag` 子节点也复用它保持朝向摄像机 |
| `SmoothCameraFollow` | `Assets/_Project/Scripts/Runtime/IsometricExploration/SmoothCameraFollow.cs:14` | 保持初始偏移并平滑跟随目标；设了镜头边界时按 `CameraConstraintPolicy` 约束（A6） |
| `IsometricExplorationConfig` | `Assets/_Project/Scripts/Runtime/IsometricExploration/IsometricExplorationConfig.cs:8` | 保存移动速度、排序兼容参数和相机缓动时间 |
| `ExplorationInstaller` | `Assets/_Project/Scripts/Runtime/IsometricExploration/ExplorationInstaller.cs:20` | 本模块的 GameplayInstaller：注册 `ExplorationHudPresenter` / `ExplorationControlsPresenter` / `ExplorationCompassPresenter` 三个入口点与 `IsometricExplorationConfig` |
| `ExplorationHudView` | `Assets/_Project/Scripts/Runtime/IsometricExploration/ExplorationHudView.cs:21` | 探索常驻 Hud：右上角「沉浸」切换按钮 + 走跑 / 摇杆 / 触屏三键 / 重置 / 交互提示 / 万向标（全部可空容错），`VisibleWhenHudHidden = true` |
| `ExplorationHudPresenter` | `Assets/_Project/Scripts/Runtime/IsometricExploration/ExplorationHudPresenter.cs:23` | 启动后打开探索 HUD，驱动沉浸模式的进入 / 退出与埋点 `immersive_changed` |
| `ExplorationControlsPresenter` | `Assets/_Project/Scripts/Runtime/IsometricExploration/ExplorationControlsPresenter.cs:31` | 入口点：摇杆 / 触屏三键按平台显隐、走跑标签跟随 `PlayerModel.IsRunning`、物资箱焦点提示、沉浸时整体隐藏控件区、驱动「重置进度」确认流程 |
| `ExplorationCompassPresenter` | `Assets/_Project/Scripts/Runtime/IsometricExploration/ExplorationCompassPresenter.cs:29` | 入口点：场景加载时登记 `ExplorationPointOfInterest`，每帧把屏外兴趣点摆到画布边缘（对象池复用模板），沉浸时整体跳过 |
| `ExplorationCompassRules` | `Assets/_Project/Scripts/Runtime/IsometricExploration/ExplorationCompassRules.cs:16` | 纯函数：视口坐标 → 屏内不画 / 屏外贴边位置与箭头角度，复用 `QuestGuidanceMath.Solve` |
| `ExplorationPointOfInterest` / `PoiKind` | `ExplorationPointOfInterest.cs:16` / `PoiKind.cs:6` | 场景组件：万向标指引目标（Npc / Crate / Location），`Crate` 类型在同物体 `SupplyCrate` 打开后不再可见 |
| `ExplorationConfirmView` | `Assets/_Project/Scripts/Runtime/IsometricExploration/ExplorationConfirmView.cs:20` | Popup 层通用确认弹窗（首个用途「重置进度」），`WaitAsync` 交回确认 / 取消，被动关闭按取消处理不抛异常 |
| `SceneOccluder` | `Assets/_Project/Scripts/Runtime/IsometricExploration/SceneOccluder.cs` | 场景组件（波 9）：挡住相机视线时把 `sharedMaterial` 换成半透明材质，离开换回 |
| `OccluderFadePresenter` | `Assets/_Project/Scripts/Runtime/IsometricExploration/OccluderFadePresenter.cs` | 入口点（波 9）：每帧相机→玩家胸口射线，命中的 `SceneOccluder` 淡出、离开恢复 |
| `StandaloneEncounterController` | `Assets/_Project/Scripts/Runtime/Monster/StandaloneEncounterController.cs` | 直接播放场景时，用现有遭遇规则读取 Gameplay 输入并推进角色、敌人与战斗（2026-10-07 起还接场景里的 `ExecutionInteractor`，但那条路没有种类表，物种门槛恒拒） |

位置只由 `EncounterStep` 推进，不存在写入 3D `Rigidbody` 的物理控制器
（历史原型 `IsometricPlayerController3D` 已于 2026-09-28 删除，回放改走 Boot 真实流程，见下文「历史记录：3D 物理移动验证」）。
`EncounterSceneView` 负责把逻辑位置投影到场景：每个渲染帧先在上一 tick 与当前 tick 之间插值
（`Lerp(PreviousPosition, Position, SimulationRunner.Accumulator / FixedDeltaTime)`，细则见 Monster 模块指南「两逻辑 tick 之间的渲染插值」），
所以角色 Transform 每帧连续移动，相机 `SmoothCameraFollow` 追的也是连续目标，渲染帧率 ≠ 60 Hz 时不再一帧动一帧不动。直接播放场景时由
`StandaloneEncounterController` 驱动（它读场景内 `PlayerInput` 推进同一套 `EncounterStep`，不经过任何 3D 物理控制器）；
从 Boot 加载时它会自行停用，改由正式 `SimulationRunner` 驱动。

## 场景结构

当前接线保存在 `Assets/Scenes/SampleScene.unity`。

运行时通过 Addressables 地址 `IsometricEncounter` 加载该场景。场景根节点 `Encounter` 上的
`EncounterSceneView` 显式引用 `player`、`enerme`、两个 `Visual/SpriteRenderer`、出生点和巡逻点。
若 Sprite 资产引用失效，`EncounterSceneView` 会回退为白色纸片并继续显示状态色。

```text
Encounter
├─ EncounterSceneView
├─ StandaloneEncounterController
├─ PlayerSpawn
├─ PatrolPoint0
└─ PatrolPoint1
```

`Crates`（场景根节点，与 `Encounter` 平级）下 `Crate_A` / `Crate_B` / `Crate_C` 三只物资箱（归 Loot 模块，
见 `ai-docs/docs/modules/loot/loot-module-guide.md`）；每只箱子根物体额外挂 `ExplorationPointOfInterest`
（`kind = Crate`）供本模块的万向标指引：

```text
Crates
├─ Crate_A（Obstacle 层；SupplyCrate + SupplyCrateMarker + ExplorationPointOfInterest(Crate) + BoxCollider 0.8×0.6×0.8，挡人）
│  ├─ Closed / Opened（两套外观）
│  └─ Marker（头顶标记：SpriteRenderer + CameraBillboard）
├─ Crate_B（同构）
└─ Crate_C（同构）
```

场景里 3 个 NPC 与 2 个 `QuestLocation` 同样挂了 `ExplorationPointOfInterest`（`kind = Npc` / `Location`），
共 8 个兴趣点由 `ExplorationCompassPresenter` 统一登记；`Location` / `Npc` 恒可见，`Crate` 类型箱子打开后
不再指引（`ExplorationPointOfInterest.IsVisible`）。

`Environment_Graybox`（灰盒环境几何体）与 `GlobalVolume`（后处理，见下文「表现层」）是与 `Encounter`
平级的场景根节点，不挂在 `Encounter` 下；`EncounterSceneView` 不引用它们，纯表现，不参与玩法判断：

```text
Environment_Graybox（场景根）
├─ Ground / Wall_Back(SceneOccluder) / Wall_Left(SceneOccluder) / Tower(SceneOccluder) / Stairs
├─ Fence / Cone_1..3 / Bench_1..2
└─ MultiLevel（波 9：多层平台，对标「旅行小记」的上下层）
   ├─ Ground_East / Deck_Upper(SceneOccluder) / Ramp_South(SceneOccluder) / Bridge_West(SceneOccluder)
   ├─ Stairs_West（Stairs_West_Step_1..9，均挂 SceneOccluder）
   └─ Railings（Rail_Bridge_S/N、Rail_Deck_N/E/S1/S2/W1/W2，均挂 SceneOccluder）

GlobalVolume（场景根，Global + ExplorationVolumeProfile）
```

`MultiLevel` 各件（全是原生 Cube，Layer `Ground`，材质复用 `Art/Materials/Graybox/`；地面顶面 y = 4.89）：

| 物体 | 中心 / 缩放 | 覆盖范围与用途 |
| --- | --- | --- |
| `Ground_East` | (26, 4.79, 3.4) / (14, 0.2, 30) | 地面向东扩到 x 33（x 19..33、z −11.6..18.4） |
| `Deck_Upper` | (25, 7.69, 12) / (10, 0.4, 10) | 上层甲板 x 20..30、z 7..17，顶面 7.89、底面 7.49，人能从下面走过 |
| `Ramp_South` | (23.5, 6.2496, 3.0527) / (3, 0.3, 8.544)，绕 X −20.556° | 坡道 x 22..25，顶面从 (z −1, y 4.89) 升到 (z 7, y 7.89)，接甲板南沿 |
| `Stairs_West_Step_1..9` | 第 i 级中心 x = 20 − 0.8i + 0.4、顶面 y = 7.89 − 0.3i，缩放 (0.8, 0.3, 3)，z 15.5 | 甲板西沿 x 20 下到地面 x 12.8，z 14..17，每级 0.3 |
| `Bridge_West` | (17.25, 7.69, 10.25) / (5.5, 0.4, 2.5) | 桥 x 14.5..20、z 9..11.5，顶面 7.89，西端顶到 Tower 东面，桥下净高 2.6 |
| `Rail_Bridge_S/N` | y 8.39，z 9.04 / 11.46，缩放 (5.5, 1, 0.08) | 桥两侧栏杆 |
| `Rail_Deck_*` | y 8.39，厚 0.08、高 1 | 甲板四沿栏杆；南沿留坡道口（x 22..25），西沿留桥口（z 9..11.5）与楼梯口（z 14..17） |

同波搬家：`QuestLocation_Lookout` → (25, 7.89, 12)（甲板中央，「观察神秘生物」的瞭望点上楼）；
`Crate_B` → (28, 7.89, 15)（甲板上）。

`Environment_Graybox` 下全部物体统一放在新建 Layer `Ground`（slot 8，`ProjectSettings/TagManager.asset`），
`Encounter/EncounterSceneView.groundMask` 只勾这一层，供贴地射线专用；这一层只用于贴地探测，
不代表玩法碰撞层，新增环境物体记得同样放进 `Ground` 层，否则角色纸片走上去不会贴地。
波 9 起 `EncounterSceneView.obstacleMask` 勾 `Ground`，2026-09-30 起再勾 `Obstacle`（第 10 层，合计 1280）：这两层里
「高于脚底 0.35、低于脚底 1.5」的部分会挡住玩家（`Ground`：墙、围栏、长椅、路障、栏杆、塔；`Obstacle`：物资箱根节点）；
0.3 的台阶、坡面、桥底与甲板底不挡。`Obstacle` 专放「挡人、但不是地面也不是遮挡物的道具」，不参与贴地与遮挡淡出。
NPC 在 Default 层，**不挡人**，玩家可以穿过，前后关系靠下文「表现层」的纸片深度偏移。接线步骤见 monster-module-guide「修改时检查」。
新加的可站立平台若底面离地低于 1.5 m，人会被它从侧面挡住——想让人从下面走过就把底面放到 1.5 m 以上。

## 遮挡半透明（波 9 建，波 10 改粗射线 + 过渡）

`OccluderFadePresenter`（`Runtime/IsometricExploration/OccluderFadePresenter.cs`，`ExplorationInstaller` 注册的入口点，
构造注入 `QuestSceneBinder` 与 `IsometricExplorationConfig`）每帧从 `QuestSceneBinder.SceneCamera` 向
`PlayerAnchor + (0, 0.8, 0)`（胸口）扫一根**粗射线** `Physics.SphereCastNonAlloc`：半径取配置 `OccluderProbeRadius`
（默认 1.0 m），距离 = 相机到胸口 − 半径（纯规则 `OccluderFadePresenter.TryBuildProbe`，EditMode 有测），球停在胸口前一个半径处，
不把脚下地面扫进来；只扫 `Ground` 层，预分配 16 个结果；Collider → `SceneOccluder` 查找结果（含「没挂」）按 Collider 缓存，场景卸载清缓存。
扫到挂了 `SceneOccluder` 的物体就 `SetFaded(true)`，上一帧淡出、本帧没扫到的恢复。地面（`Ground` / `Ground_East`）不挂，
扫到只缓存成 null，不报错不误淡。纯表现，不回写玩法状态。
波 9 的细射线只在物体正压住胸口时才淡出，前景的 `Tower`（顶高 9.39）在玩家走西侧楼梯时从视线旁擦过、挡住半个画面却不淡——
粗射线就是为这种「挡住人周围」的情况。

`SceneOccluder`（`Runtime/IsometricExploration/SceneOccluder.cs`）挂在要淡出的几何体上，字段 `fadedMaterial`
（灰盒拖 `Art/Materials/Graybox/M_Graybox_Faded.mat`：URP Lit 透明、Alpha 0.35、不投影不写深度）与 `fadeSeconds`（默认 0.15）；
过渡：变淡时立刻换上半透明材质，用 `MaterialPropertyBlock` 把 `_BaseColor` 从「原材质颜色、alpha 1」插值到半透明材质自身颜色；
恢复时反向插值后换回原材质；到终态就清掉属性块。插值由呈现器只对「正在过渡」的对象调 `Advance(unscaledDeltaTime)`，
静止时零开销。`fadeSeconds = 0` 或材质没有 `_BaseColor` 时直接切换。不改材质资产内容；禁用时立刻恢复原样。
`IsFaded` 是目标状态（恢复过渡中已为 false，`sharedMaterial` 要等过渡结束才换回）。

当前挂 `SceneOccluder` 的物体（共 23 个）：`Bridge_West`、`Deck_Upper`、`Railings` 下 8 段栏杆（波 9），
`Tower`、`Ramp_South`、`Stairs_West_Step_1..9`、`Wall_Left`、`Wall_Back`（波 10）。不挂：`Ground`、`Ground_East`、
围栏、长椅、锥桶（矮，不挡）。

构图提示：相机→胸口的视线俯角约 40°，离地 2.6 m 的桥 / 甲板挡住的是它**北侧**约 2～3 m 的人，
站在桥正中下方反而看得见（回放用 (17.25, 12.3) 验证桥淡出）。

角色（`player` / `enerme`）当前层级；根节点已改为**脚底**、缩放归一 `(1, 1, 1)`，
Y = 地面高度 `4.8884`（`Assets/Scenes/SampleScene.unity:4581`，`CapsuleCollider.center (0, 0.8, 0)`、
`height 1.6`、`radius 0.3`，即碰撞体从脚底往上量）：

```text
player（根节点，脚底，缩放 (1, 1, 1)，y = 4.8884）
├─ Rigidbody / CapsuleCollider(center 0,0.8,0 / height 1.6 / radius 0.3) / PlayerInput
├─ Visual
│  ├─ SpriteRenderer（停用，sprite 为空，只当 flipX 朝向源；材质 M_SpriteDepthClip）
│  └─ CameraBillboard
├─ BlobShadow（贴地阴影纸片，localPosition.y 0.02，世界直径 0.9）
├─ SelectRing（状态指示环纸片，localPosition.y 0.02，世界直径 1.1，已启用，赋给
│  EncounterSceneView.playerStateIndicator，状态色改染在这个环上而不是本体纸片）
└─ NameTag（World Space Canvas + CameraBillboard + Image + TMP「玩家」，localPosition.y 1.9，
   localScale 0.007，世界高 0.35）
```

`enerme` 同构（Visual 纸片同样停用、sprite 为空）；`SelectRing` 同样已启用并赋给
`monsterStateIndicator`；`NameTag` 文字为「巡逻者」；`enerme/Visual` 的 `localPosition.z` 仍为
`0.05`，避免两角色重合时与 `player/Visual` 发生 z-fighting（早期做法；玩家与任何角色打平时的前后现在由玩家材质的
深度偏移统一保证，见「表现层」）。

根节点缩放已归一为 `(1, 1, 1)`，`BlobShadow` / `SelectRing` / `NameTag` 的局部尺寸此前需要按非等比
根缩放换算的问题已不存在；新角色若根节点仍做非等比缩放，才需要按对应轴换算。新增角色纸片的完整
步骤见 `isometricexploration-extension-guide.md`。

`Visual` 的纸片 `SpriteRenderer` 隐藏但保留（`enabled = false`，sprite 已置空，运行时由 `EncounterSceneView.EnsureSprite`
补 1×1 占位图；组件仍是 `flipX` 载体），序列帧小人预制体 `Chibi_amiya`（玩家）/ `Chibi_chen`（巡逻者）挂在其下，
见 characterpuppet 模块（`ai-docs/docs/modules/characterpuppet/characterpuppet-module-guide.md`）。

`Visual` 是只负责显示的子节点。纸片倾斜只发生在这个节点上；`Rigidbody` 和 3D Collider 留在根节点。
这样视觉可以面向摄像机，物理体仍保持竖直，不会因为斜碰撞面产生攀爬效果。

**环境纸片（`PropRoot` + `SpriteRenderer` + `CameraBillboard` 层级）已停用、待美术替换**：原六个
「室内*」纸片物体仍在场景中但已停用（未删除，未来正式表现如仍需要 2D 占位可重新启用）；正式/灰盒
环境改用 `Environment_Graybox` 下的 3D 几何体，见下文「表现层」与扩展指南的「新增灰盒/正式环境模型的步骤」。

## 探索 HUD 与沉浸模式

参考《明日方舟》旅行小记左下角按钮：一键隐藏全部 HUD 与世界空间交互标记，只留沉浸按钮本身。

- **Core 能力**：`IHudVisibility`（`Core/UI/IHudVisibility.cs`，由 `UIService` 实现）+ 事件 `HudVisibilityChangedEvent`。
  沉浸时 UIService 把 `VisibleWhenHudHidden == false` 的 Hud 面板 CanvasGroup 置 alpha 0 / 不可交互 / 不挡射线，不关面板。
- **入口**：`ExplorationHudPresenter` 在 `BootCompletedEvent` 后 `OpenAsync<ExplorationHudView>()` 并常驻；
  切换来源：按钮、`Gameplay/Immersive` 动作（键鼠 H / 手柄右摇杆按下）；退出来源：沉浸中 `Gameplay/Cancel` 且没有对白
  （`ShouldExitOnCancel` 纯函数）、`DialogueService.OnStarted`。动作只在启动完成时订阅一次。
- **世界空间物件**：Dialogue 由 `DialogueSceneBinder` 订阅事件后给已登记的 `DialogueInteractable.SetHiddenByHud`，
  标记 / 名字 / 台词气泡据此隐藏，焦点在沉浸时为空；Quest 的 `QuestHudPresenter` 在沉浸时隐藏 `QuestTargetMarker`。
  玩家 / 巡逻者的 `NameTag`（角色头顶名字）目前**不**随沉浸隐藏。
- **埋点**：模块名 `exploration`，`immersive_changed`（`hidden`、`source` = button / key / cancel / dialogue），
  `hud_open_failed`（Error）。
- **接线要求**：`ExplorationInstaller` 挂到 `Assets/_Project/Scenes/Boot.unity` 的 `GameBootstrap` 上，排在 `QuestInstaller` 之后；
  预制体 `Assets/_Project/Prefabs/UI/ExplorationHudView.prefab` 在 Addressables `UI` 组，地址 `ExplorationHudView`。

```text
ExplorationHudView（RectTransform 铺满，CanvasGroup，ExplorationHudView）
├─ ImmersiveButton（右上角锚点；Image + Button + CanvasGroup，不在 ControlsRoot 下，沉浸中仍显示）
│  └─ Label（TextMeshProUGUI「沉浸」/「退出沉浸」）
├─ ControlsRoot（CanvasGroup = controlsGroup，沉浸时整体隐藏）
│  ├─ Stick（左下，OnScreenStick 绑 `<Gamepad>/leftStick`）── Knob
│  ├─ TouchButtons（潜行 SneakButton / 伪装 DisguiseButton / 攻击 AttackButton，各自 OnScreenButton）
│  ├─ ResetButton（右上，沉浸按钮下方）
│  （原 InteractPrompt 一行已随统一交互删除，箱子提示改走 `Game.Interaction.InteractPromptHudView`，PRP/interaction D8）
├─ RunSlot（右下角锚点，独立 CanvasGroup = runToggleGroup，不在 ControlsRoot 下）
│  └─ RunToggle（OnScreenButton 绑 `<Gamepad>/leftStickPress`）── Label（「散步」/「奔跑」）
└─ CompassRoot（铺满，不挡射线）
   └─ CompassMarkerTemplate（运行时隐藏；子 Arrow + 子 Label，由呈现器 Instantiate 复制）
```

## 探索控件与万向标（波 3 追加）

- **控件区**（`ExplorationControlsPresenter`）：`BootCompletedEvent` 后与 `ExplorationHudPresenter`
  并发 `OpenAsync<ExplorationHudView>()`（`IUIService` 保证返回同一实例）。
  **PC 优先（波 6，2026-09-26）**：触屏控件（摇杆 / 三键 / 走跑按钮）默认仅 `IPlatformService.IsTouchPrimary` 显示，
  三者统一取 `IsTouchPrimary || config.ShowStickOnDesktop`（`ShowStickOnDesktop` 是开发开关，默认关），
  走跑按钮经 `SetRunToggleVisible` 切 `RunToggle` 物体；PC 走跑走 `Gameplay/Run`（左 Ctrl / 手柄左摇杆按下）。
  预制体与代码保留作移动端移植的底子。走跑标签每帧读 `PlayerModel.IsRunning`（潜行时不改标签，只显示模式），只在变化时才写 TMP；
  沉浸时 `HudVisibilityChangedEvent` 驱动 `hud.SetControlsVisible(false)`，同时切 `ControlsRoot` 与
  `RunToggle` 两个 CanvasGroup。
- **重置进度**（波 12 挪到右上角，2026-09-26）：`ImmersiveButton` 与 `ResetButton` 均改锚点/pivot
  为右上角 (1,1)：`ImmersiveButton` `anchoredPosition (-48, -160)`、`sizeDelta 220×72`；`ResetButton`
  `anchoredPosition (-48, -240)`、`sizeDelta 220×56`，紧贴 `ImmersiveButton` 下方（占 x −268..−48、
  y −160..−296）。挪到右上角是为了不再压左上角任务栏 `QuestHudView`（波 8 的左下角方案会与
  `EncounterSceneView` 的调试按钮/文字叠在一起，波 12 改为两边各占一角）；同时与
  `DialogueView.prefab` 的 `Controls`（右上锚点 (−40,−40)、392×36，倍速/自动/跳过三键）和
  `PortraitRight`（右上锚点 (−40,0)、200×200，pivot (1,0.5)）留出间隔：对话三键占 y −40..−76，
  右立绘到 y −100，与本组的 y −160..−296 之间有 60 像素以上的空隙，不会互相遮挡。点 `ResetButton` → `OpenAsync<ExplorationConfirmView>(config.ResetMessage)` →
  `WaitAsync` 等选择 → 关闭弹窗 → 确认则 `QuestService.ResetProgress()` → `LootService.Reset()` →
  `IGameFlow.GoToAsync<MonsterEncounterState>()`（退出当前遭遇状态卸载场景，重新加载后玩家回出生点，
  箱子随 `LootResetEvent` 合上）；取消 / 弹窗被关掉都按「取消」处理，不抛异常。埋点
  `progress_reset` / `progress_reset_cancelled` / `progress_reset_failed`（Error）。
- **万向标默认关闭（2026-09-26 用户决定）**：`ExplorationCompassPresenter.Enabled`（`bool` 读写属性，
  初值取 `IsometricExplorationConfig.ShowCompass`，默认 `false`）——全部兴趣点都标识会显得屏幕乱，任务
  追踪的指引已由 Quest 模块负责（追踪目标画头顶标记 / 引导）。`Tick` 在 `!Enabled` 时直接跳过扫描与摆位
  这些重活，只在关闭当帧收起已显示的标记。调试或将来做「附近兴趣点提示」时把 `ShowCompass` 勾上，或运行时
  `Resolve<ExplorationCompassPresenter>().Enabled = true`（回放 `Compass_ShowsOffscreenPoiAndHidesWhenImmersive`
  就是这样打开验证摆位规则的）。
- **万向标**（`ExplorationCompassPresenter` + `ExplorationCompassRules`）：`sceneLoaded` 扫描全部
  `ExplorationPointOfInterest`（含未激活），标签在登记时按 `IsometricExplorationConfig.CompassLabelMax`
  截好；开启后每帧对可见兴趣点 `Camera.WorldToViewportPoint` → `ExplorationCompassRules.TryPlace`（屏内返回
  `false` 不画，屏外 / 身后贴边并给出箭头角度，复用 `QuestGuidanceMath.Solve`）；标记走对象池，只在
  需要更多槽位时从 `CompassMarkerTemplate` 复制，不逐帧销毁重建。`CompassRoot` / `CompassMarkerTemplate`
  未接线时报一次 Error 并埋 `compass_template_missing`，之后每帧静默跳过。沉浸时整体隐藏（`HideFrom(0)`）。
  同边标记按 56 单位最小间距错开（`ExplorationCompassRules.SpreadAlongEdges`，常量
  `ExplorationCompassPresenter.CompassMinSpacing`，待并入 `IsometricExplorationConfig`）：`Tick` 先把
  全部摆位收进预分配的 `framePlacements`，跑完 `SpreadAlongEdges` 再落到标记池，避免屏幕右缘等多个
  标签互相压住；同边按沿边坐标排序做最小间距推挤，推到边角时整体前移并钳制在画布内，不越界。
- **埋点补充**：`controls_bound`（`touch`）、`poi_bound`（`count`，场景加载时一次）、
  `compass_open_failed` / `controls_open_failed`（Error）。
- **接线要求补充**：`ExplorationInstaller` 的 `Config` 拖 `Data/IsometricExploration/IsometricExplorationConfig.asset`；
  控件区 / 万向标依赖 `Game.Loot.LootService`（`LootInstaller`）、
  `Game.Quest.QuestSceneBinder` / `QuestService`（`QuestInstaller`）、`Game.Player.PlayerModel`
  （`PlayerInstaller`），`ExplorationInstaller` 必须排在这些注册器之后；Addressables `UI` 组另增地址
  `ExplorationConfirmView`（预制体 `Assets/_Project/Prefabs/UI/ExplorationConfirmView.prefab`）。

## 表现层（渲染分档 / 光影 / 后处理）

场景表现从「2D Sprite 纸片替代环境」升级为「3D 灰盒环境 + 深度裁剪纸片角色」，渲染管线按平台分两档：

| 档位 | Pipeline Asset | Renderer List | 差异 |
| --- | --- | --- | --- |
| 高档（Standalone 默认 High） | `Assets/Settings/UniversalRP.asset` | `[Renderer2D(0), UniversalRenderer(1)]`，`UniversalRenderer.asset` 带 SSAO Feature | 软阴影开、Shadow Distance 40、Cascade 2、MSAA 2x、Depth Texture 开 |
| 手游档（Android 默认 Low） | `Assets/Settings/UniversalRP_Mobile.asset` | `[Renderer2D(0), UniversalRenderer_Mobile(1)]`，无 SSAO | 软阴影关、Shadow Distance 25、Cascade 1、无 MSAA |

Quality 六档见 `ProjectSettings/QualitySettings.asset`：Very Low / Low / Medium 用手游档 Pipeline Asset，
High / Very High / Ultra 用高档；平台默认 Android = Low、Standalone = High。改分档参数只改这两份
Pipeline/Renderer 资产或 Quality 映射，不要在玩法代码里按平台分支调渲染参数（呼应
`project-root.md` 的平台隔离约束）。守卫测试：
`Assets/_Project/Scripts/Tests/EditMode/Rendering/RenderPipelineTiersTests.cs`，改完两份 Renderer List
或 Quality 映射后必须跑它。

后处理用 `Assets/Settings/ExplorationVolumeProfile.asset`（Tonemapping Neutral、Color Adjustments、
Vignette，故意不加 Bloom），由场景根节点 `GlobalVolume`（Global）引用。调后处理效果改这份
Profile，不要新建 Volume。

光照：删除了原 `Global Light 2D` 与旧地面占位 `Cube`，改用新建的 `Directional Light`
（旋转 `(50, -35, 0)`、色 `(1, .96, .88)`、强度 1.15、Soft 阴影 0.75）；`RenderSettings` 配 Linear 雾
（距离 18–42，色 `(.80,.76,.68)`）与 Flat 环境光 `(.62,.60,.58)`。改光照效果改这盏灯和
`RenderSettings`，不要再挂 2D 灯。

角色纸片要能被灰盒环境遮挡、参与 SSAO，用专门着色器
`Assets/_Project/Art/Shaders/SpriteDepthClip.shader`（`UniversalForward` / `ShadowCaster` /
`DepthOnly` / `DepthNormals` 四个 Pass 都做 alpha clip，`ZWrite On`），材质
`Assets/_Project/Art/Materials/Character/M_SpriteDepthClip.mat`。SpriteRenderer 默认不投实时阴影，
`ShadowCaster` Pass 已备好，需要投影时在 Renderer 上开 Cast Shadows。新纸片素材要能被环境遮挡，
必须走这份材质，不要用 URP 自带 Sprite-Lit/Unlit-Default（不写深度、没有 ShadowCaster/DepthNormals
Pass）；`BlobShadow`/`SelectRing` 这类贴地特效纸片用普通 `Sprite-Unlit-Default` 材质即可，
不需要深度裁剪。

### 纸片深度偏移（玩家压在同排角色前面，2026-09-30）

- **为什么需要**：纸片经 `CameraBillboard` 转成与相机成像平面平行，站在同一排（同 z、同地面高度）的两个角色观察深度
  **完全相同**（实测出生点那一排 z 3.4 的 NPC 小人都是 16.5870），深度缓冲分不出前后，重叠处两张图的像素交错显示（z-fighting）。
  NPC 不挡人（见上文「场景结构」的 obstacleMask），玩家随时可能与 NPC 叠在一起，所以要在渲染上分出前后，而不是靠碰撞把人隔开。
- **做法**：`SpriteDepthClip` 着色器的材质属性 `_DepthBias`（世界单位，放在 `UnityPerMaterial`，SRP Batcher 兼容）：
  顶点阶段把裁剪空间深度换成「沿观察方向往相机挪 `_DepthBias`」那一点的深度，xy 与 w 不动，画面位置不变；
  `UniversalForward` / `DepthOnly` / `DepthNormals` 三个 Pass 用同一个 `ApplyDepthBias`，深度预通道与前向通道一致；
  `ShadowCaster` 不加（阴影图从光源方向渲染，偏移只会挪影子）。仍是 Alpha Test + ZWrite，**不许改成透明队列**（会丢掉与灰盒的遮挡）。
- **谁用**：只有玩家。`Art/Materials/Character/M_SpriteDepthClip_Player.mat`（同一着色器，`_DepthBias = 0.02`）挂在 SampleScene
  `player/Visual/Chibi_amiya/Sprite`（预制体实例上的材质覆写）与 `player/Visual` 的隐藏纸片上；NPC、巡逻者与 `Chibi_*` 预制体本身都用
  基础材质 `M_SpriteDepthClip`（偏移 0）。效果：与任何角色深度打平或差不到 0.02 时玩家稳定画在前面；深度差大于约 0.05 时仍按真实深度遮挡。
- **取值 0.02 的依据**（算式在着色器文件头）：相机 FOV 20、俯角 38°、偏移 (0, 11.81, −14)、Near 0.5，画面内角色观察深度 15.6～24.8 m；
  保守按 24 位深度，25 m 处一个刻度约 25² / 0.5 × 2⁻²⁴ ≈ 7.5e-5 m，0.02 约为 270 个刻度（打平时稳），又小于 0.05（不颠倒真实前后）。
  改相机远近 / 近裁剪面、或把角色放到更远处时按同一算式复核。
- **守卫**：EditMode `Tests/EditMode/CharacterPuppet/SpriteDepthBiasWiringTests.cs`——两份材质只差 `_DepthBias`（玩家在 (0, 0.05) 内、基础为 0、
  渲染队列一致），SampleScene 玩家根下用该着色器的渲染器全是玩家材质、其余角色全是 0。`FramePuppetGenerator` 重跑会把预制体材质写回基础材质，
  场景里的覆写一般保得住；给玩家换角色预制体时覆写会丢，要重新把小人 `Sprite` 的材质换成 `M_SpriteDepthClip_Player`，这条测试会报出来。
- 头顶名字 `NameTag`（World Space Canvas）不走这套材质，重叠时的样子见「已知限制」。

`SpriteImportProcessor`（`Assets/_Project/Scripts/Editor/Importers/SpriteImportProcessor.cs`）已改为
高清手绘预设（Bilinear / mipmap / Compressed / PPU 100），新角色/环境纸片素材导入时按这份预设走，
不要手改单张贴图的导入设置。

## 历史记录：3D 物理移动验证（已删除）

早期原型曾在角色根节点挂一套 `Rigidbody` + 3D `CapsuleCollider` + `PlayerInput` + `IsometricPlayerController3D`
（`FixedUpdate` 里把 `Gameplay/Move` 映射成 XZ 速度、保留 `Rigidbody.velocity.y` 交给 Unity 3D 物理管落地），
只用来验证「3D 物理体能不能带着纸片走」这件事，从不参与正式位移、感知或命中判定。
`IsometricPlayerController3D.cs`（原 `Assets/_Project/Scripts/Tests/Showcase/IsometricExploration/`）已于
2026-09-28 删除；要追溯实现看 git 历史。

角色根节点现在仍保留 `Rigidbody` / 3D `CapsuleCollider` / `PlayerInput`（`obstacleMask` 碰撞判定与
`StandaloneEncounterController` 读键要用），但没有任何组件驱动 `Rigidbody` 位移——直接播放场景由
`PlayerInput → StandaloneEncounterController → EncounterStep` 推进；正式遭遇输入由
`LiveInputSource → InputCommand → EncounterStep` 推进；Showcase 回放走 Boot 真实流程，虚拟手柄/键盘经
`ShowcaseInputDriver` 驱动同一条 `LiveInputSource → InputCommand → EncounterStep` 路径。Unity 3D 物理只保留为
环境表现（重力、地面碰撞体），不参与位移、感知或命中判定。

## 纸片朝向

`CameraBillboard` 在 `LateUpdate` 中把所在 Transform 的旋转复制为目标摄像机旋转。
若 `targetCamera` 未赋值，组件在 `Start` 中回退到 `Camera.main`。

组件应挂在 `Visual`，而不是同时带有 `Rigidbody` 的根节点。
否则根节点及碰撞体会跟着倾斜，角色接触斜面后可能被物理系统推高或表现为攀爬。

`visualRenderer` 未赋值时会从同一节点获取 `Renderer`。
显式拖入引用更清楚，也能避免以后调整层级后拿错 Renderer。

## 竖直碰撞体校准

环境纸片需要阻挡角色时，可把根节点的 `BoxCollider` 赋给 `CameraBillboard.verticalCollider`。

校准规则位于 `CameraBillboard.LateUpdate`：

1. 从 `Renderer.localBounds` 取得纸片底边中心；
2. 转换到底边中心的世界坐标；
3. 计算 `BoxCollider` 正负 Z 两个表面；
4. 选择更靠近摄像机的表面作为前表面；
5. 只移动 `BoxCollider.center`，使前表面的世界 Z 与纸片底边的世界 Z 一致。

组件不会修改 Collider 的 Transform 旋转、Y 位置、高度或形状。
因此碰撞体仍是竖直长方体，而不是随 Sprite 倾斜的平行四边形。

如果纸片陷入碰撞体，优先检查：

- `visualRenderer` 是否指向实际显示的 SpriteRenderer；
- `verticalCollider` 是否指向根节点的 BoxCollider；
- Sprite 的底边是否确实代表接地边；
- Collider 的 Z 尺寸是否合理。

## 摄像机跟随

`SmoothCameraFollow` 挂在 `Main Camera` 上。
必须给 `target` 指定角色根节点，并给 `config` 指定当前配置资产。

组件在 `Start` 记录：

```text
offset = camera.position - target.position
```

此后在 `LateUpdate` 使用 `Vector3.SmoothDamp` 追踪 `target.position + offset`。
它只改变位置，不改变摄像机旋转和投影参数。
`Target` 可读取当前目标；`SetTarget(Transform)` 切换目标并重置缓动速度，保留初始构图偏移，供独立驯服验证使用。
`CameraSmoothTime` 越小，跟随越紧；越大，停下后的缓动越明显。
当前默认值为 `0.2` 秒。

`SmoothCameraFollow` 标了 `[DefaultExecutionOrder(50)]`：必须排在 `EncounterSceneView`（默认 0，LateUpdate 里把逻辑位置
插值写成本帧的角色 Transform 位置）之后、`ChibiPuppetMotion`（100）之前，保证本组件跟随的是本帧刚投影好的位置。

当前工作区 `SampleScene` 里 `Main Camera` 的具体接线：透视、FOV 20（原基线 28，327304f 为拉远视距改到 20）、旋转 `(38, 0, 0)`，Near 0.5 / Far 100；
按保存的相机与玩家位置，`Start` 记录的 `offset` 为 `(0, 11.8116, -14)`；`UniversalAdditionalCameraData.rendererIndex`
= 1（对应表现层里的 `UniversalRenderer` / `UniversalRenderer_Mobile`，不是索引 0 的 `Renderer2D`），
Post Processing 开，Background 颜色等于雾色。改构图（FOV / 旋转 / 偏移）在编辑器里调 `Main Camera`
的 Transform 与 `SmoothCameraFollow.target`，脚本本身不用改。

### 临时视角对比（仅 Unity 编辑器）

播放 `SampleScene`，Game 画面右下角可直接选择「原视角 / 30° / 25° / 20°」，当前项高亮。
入口在 `Assets/_Project/Scripts/Runtime/IsometricExploration/SmoothCameraFollow.cs:51`，整体位于 `UNITY_EDITOR` 条件内，不进入构建。
「原视角」保存第一次选择时的旋转与初始跟随偏移，不是硬编码 38°；每次选择均从这份基准计算，
围绕目标脚底上方 0.8 m 转动，重置缓动速度并继续跟随当前目标。FOV 不变，退出 Play 不保存选择。

`Camera_Reference_30deg` 是场景里停用、Untagged 的参考相机；GUI 始终操作主相机，不需要启用它。
`Data/IsometricExploration/CameraAngle_Original38.preset` 与 `CameraAngle_Reference30.preset` 是原生 Transform 预设，
可在运行前应用到 Main Camera；仅包含位姿，不包含 FOV。25° / 20° 由 GUI 计算，没有对应预设资产。
新室内场景目前不会自动显示按钮（入口限定场景名 `SampleScene`）；迁移步骤见扩展指南。
这些角度是构图试验档位，不是参考图片反推的真实参数；最终选择要同时检查通道、前景遮挡与上下层可读性。
确认构图后移除临时 GUI；正式镜头仍通过场景参数配置。

## 配置资产

### mo 暗雾原型（2026-10-01）

`mo` 的 `DarkFogPrototype` 下放占位纸片与近、中、远方块；原有 Cube 保留。
`DarkFog_Player/DarkFog_ClearRegion` 挂 `DarkFogRegion`，引用主相机、
`Data/IsometricExploration/DarkFog_Mo.asset` 与 `Art/Shaders/DarkFog.shader`。
组件通过 URP 相机回调登记一次深度雾 Pass，不修改共用 Renderer 资产；仅作用于指定相机与当前场景的 Scene 预览。
纸片复用 `M_SpriteDepthClip_Player`（Alpha Test、写深度），透明队列物件暂不参与雾，不能据此宣称透明材质已兼容。

减雾区是局部单位球经 Transform 变换形成的椭球：位置为中心，XYZ 缩放为外半径，
配置 `Feather` 决定内圈与外圈之间的平滑过渡。选中组件后 Scene 中青色内圈、蓝色外圈与半径手柄可编辑；
移动 `DarkFog_Player` 可预览附近物件的显现，原型没有接入玩家输入。配置包含颜色、密度、最大采样距离、
4–32 步采样（默认 16）、过渡比例与缓慢变化的程序噪声。减雾内圈跳过采样，浓雾累计到光学深度 8 后提前退出。
表面额外乘减雾权重，保障远置相机到主角之间的雾不会遮黑主角；这是玩法显现近似，不是物理散射。
单区域、全分辨率、无体积灯光或体积阴影；目标设备 GPU 耗时尚未测定。

主相机复用 `CameraDistanceCulling`，引用独立 `DarkFog_MoCulling.asset`，Obstacle 渲染层试验距离 25 m。
雾不修改剔除数组、不遍历物件、不控制 AI、碰撞或物件活动状态；相机剔除仍只减少绘制。
调减雾范围时必须使可见区内的物件仍在相机剔除距离内，二者分别按玩家局部空间和相机深度计算，不能直接共用半径。
已有物件可把 Renderer 放独立视觉层，保持碰撞根层不变；本场景的方块只是验证探针。

验证：`DarkFogRegionTests` 的内外边界与过渡 EditMode 测试通过；实际相机像素对比中远方绿色探针在关闭雾与剔除时可见，
开启剔除后变成背景，关闭剔除但开启雾后变暗，Collider 保留。截图位于 `Logs/verify/dark-fog/`。

### 相机距离剔除

`CameraDistanceCulling`（`Assets/_Project/Scripts/Runtime/IsometricExploration/CameraDistanceCulling.cs`）
挂在 SampleScene 的 Main Camera，引用下述配置资产。使用 Unity 原生 `Camera.layerCullDistances`，
没有逐帧遍历物体，不关 Renderer、GameObject 或 Collider，也不卸载资产。
配置 `CameraLayerCulling` 默认为空，具体距离待定；启用和显式刷新时应用，禁用时恢复启用前的相机距离。
编辑器实测曾出现原生距离数组为 32 项、组件缓存却为 0 项的情况；不能只用 null 判断缓存有效。
`ApplySettings` 遇到非 32 项缓存会重读相机，`OnDisable` 只恢复完整的 32 项缓存，避免 Unity setter 抛异常。
未配置层保持原值，重复层后项覆盖前项；0、负数、NaN、无穷统一使用 0（该相机的 Far Clip）。
保留相机原有 `layerCullSpherical` 模式；默认平面剔除沿相机深度，不等于以玩家为中心的半径。
Performance 世界舞台接管时继承主相机的分层距离与模式，结束恢复作者值；找不到主相机的退路保持舞台作者值。

验证：`Tests/EditMode/IsometricExploration/CameraLayerCullSettingsTests.cs` 覆盖数值边界；
`Tests/PlayMode/CameraDistanceCullingTests.cs` 覆盖重新应用、禁用/重启恢复及碰撞体保留，以及 0 / 31 / 33 项无效缓存；
`Tests/PlayMode/SmoothCameraFollowTests.cs` 覆盖四档反复切换、胸口相对相机位置保持及目标移动后恢复原构图；
`IsometricExplorationShowcase.CameraDistanceCulling_HidesDistantVisualAndRestoresIt` 在 SampleScene
临时生成并清理青色方块，用实际画面采样和截图验证消失/重现。测试距离不写入正式配置。

配置资产位于：

`Assets/_Project/Data/IsometricExploration/IsometricExplorationConfig.asset`

| 参数 | 当前值 | 用途 |
| --- | ---: | --- |
| `CameraSmoothTime` | 0.2 | 摄像机平滑跟随时间 |
| `ShowStickOnDesktop` | false | 开发开关：桌面平台也显示触屏控件（摇杆 / 三键 / 走跑按钮）；正式只在触屏平台显示 |
| `CompassEdgeMargin` | 48 | 万向标贴屏幕边时的内缩像素 |
| `CompassLabelMax` | 6 | 万向标标签最多显示几个字，超出截断；0 = 不显示标签 |
| `RunLabel` / `WalkLabel` | "奔跑" / "散步" | 走跑按钮标签文案 |
| `ResetMessage` / `ResetConfirmText` / `ResetCancelText` | 见资产 | 重置确认弹窗正文与两个按钮文案 |

移动速度归 `PlayerConfig`；原 `MoveSpeed` / `SortingScale` 两个无人读取的字段已在波 6 删除
（当时的 Showcase 原型控制器 `IsometricPlayerController3D` 改用本地常量 3；该类已于 2026-09-28 整个删除）。

## 依赖方向

运行时代码只依赖 UnityEngine 和项目的 Runtime 程序集。
`Game.Tests.Showcase.asmdef` 引用 `Unity.InputSystem`：框架的虚拟输入驱动
（`ShowcaseInputDriver`，见 `Assets/_Project/Scripts/Tests/Showcase/Framework/`）要建虚拟 `Gamepad` / `Keyboard`
并往 Input System 事件队列写状态事件，回放借它像玩家一样操作场景里的真实角色。

数据流如下：

```text
Player Transform
  → SmoothCameraFollow
  → Main Camera position
  → CameraBillboard
  → Visual rotation / BoxCollider.center 校准

InputCommand
  → EncounterStep
  → PlayerModel / MonsterModel（XY，含上一 tick 位置 PreviousPosition）
  → EncounterSceneView（按 SimulationRunner.Accumulator 插值 → XY → XZ 贴地 / 障碍滑动）
  → player / enerme Transform

PlayerModel.IsRunning
  → ExplorationControlsPresenter.Tick
  → ExplorationHudView.SetRunLabel

ExplorationPointOfInterest（场景，含 SupplyCrate 只读 IsOpened）
  → ExplorationCompassPresenter.Tick
  → Camera.WorldToViewportPoint → ExplorationCompassRules.TryPlace（复用 QuestGuidanceMath.Solve）
  → CompassRoot 下的标记实例

ResetButton
  → ExplorationConfirmView.WaitAsync
  → QuestService.ResetProgress() + LootService.Reset()
  → IGameFlow.GoToAsync<MonsterEncounterState>()
```

`ExplorationControlsPresenter → Game.Loot / Game.Quest / Game.Player / Game.Monster`（读
`LootService`、`QuestService`、`PlayerModel`、`MonsterEncounterState`
类型）；`ExplorationCompassPresenter → Game.Quest`（`QuestSceneBinder.SceneCamera`）、
`ExplorationPointOfInterest → Game.Loot`（只读 `SupplyCrate.IsOpened`）。均为单向读取，反向禁止。

## 已知限制

- `CameraBillboard` 直接复制摄像机完整旋转，不支持只绕单一轴 Billboard；
- Collider 校准以世界 Z 为目标轴，适用于当前固定构图，不是任意朝向通用解；
- Collider 仍为长方体，无法精确拟合不规则 Sprite 轮廓；
- 摄像机跟随只做位置缓动，没有边界、前视、死区或碰撞避让；
- 3D 控制器属于原型，不进入正式可重放玩法状态，遭遇时会被停用；
- 场景引用保存在 `EncounterSceneView`，重命名角色不会触发运行时名称查找；
- 遮挡碰撞是**白盒实现**（波 9）：`EncounterSceneView` 在表现层用 `EncounterCollision.Slide`（先 X 后 Z 胶囊扫掠，
  贴墙滑动）解算玩家纸片位移，被挡时经 `OnPlayerBlocked` → `EncounterStep.CorrectPlayerPosition` 回写逻辑位置。
  取舍：障碍不在确定性内核里，同机同场景可复现，跨机 / 跨平台回放不保证逐位一致；正式版要把关卡障碍数据放进内核。
  怪物不解算碰撞（巡逻路线本身避开障碍）；单帧位移超过 `obstacleTeleportDistance`（1.5 m）视为瞬移，不解算只贴地；
  碰撞按本帧插值后的位置解算，回写逻辑位置时只改被挡的轴（沿墙那一轴保留逻辑值，贴墙滑动不减速）；
- 距离剔除有 PlayMode 生命周期测试；整体探索场景接线仍需 Showcase 与 Unity 试玩确认；
- 贴地投影是表现层：`groundMask` 只影响 `EncounterSceneView` 里纸片的世界 Y，逻辑层没有高度、
  不做视线判定，玩法规则依旧不读取贴地结果（怪物感知不会被桥 / 墙挡住）；
- 两角色重合时 `NameTag` 会叠在一起，没有做避让或层级排序。

### 关卡设计约束（遮挡）

- 相机在玩家**南侧**、俯角约 38°、FOV 20（长焦）：近处的高物体在屏幕上占比很大。凡是**高于 1.5 m 且位于可行走区域南侧**
  的物体，都会在某些站位挡住玩家，**必须挂 `SceneOccluder`**（`fadedMaterial` 拖 `M_Graybox_Faded.mat`，放在 `Ground` 层，
  带 Collider——没有 Collider 扫不到）；北侧的墙挂上也无害（玩家贴墙北侧走时视线会穿过它）。
- 美术替换时把高物体（塔、树、建筑）尽量放到可行走区域**北侧或边缘**，少让它们站在玩家与相机之间。
- 淡出只看「相机 → 胸口」这一根 1 m 粗的扫掠：离视线超过半径的物体即使在画面里挡住别的东西（NPC、箱子）也不淡；
  需要更早淡出就调大 `OccluderProbeRadius`。

## 验证入口

- EditMode（沉浸）：`Assets/_Project/Scripts/Tests/EditMode/IsometricExploration/ExplorationHudPresenterTests.cs`；
  UIService 的沉浸行为见 `Tests/EditMode/Core/UIServiceTests.cs` 的 `SetHudHidden_*` 用例。
- EditMode（控件与万向标）：`.../ExplorationCompassRulesTests.cs`（11 条：`TryPlace` 屏内 / 贴边 / 身后翻转 /
  远角内缩 / 相机平面兜底、`TrimLabel` 截断、`ShouldShowStick` 平台判定、`SelectRunLabel` 走跑标签选择、
  `SpreadAlongEdges` 同边推开 / 不同边互不影响 / 推到边角钳制不越界）。
- Showcase（波 4）：`Assets/_Project/Scripts/Tests/Showcase/Exploration/ExplorationShowcase.cs`（波 4 落地中，
  覆盖 V1–V5：走跑切换（`Gameplay/Run` 脉冲）、桌面下触屏控件全部隐藏、万向标进出屏、开箱提示与通知、重置后任务与箱子回到初始）。
- EditMode：`Assets/_Project/Scripts/Tests/EditMode/Monster/EncounterSceneViewTests.cs`
  （含 `EncounterProjection` 的 5 条 `ResolveGroundY` + 4 条 `ResolveFlipX` 用例）。
- 渲染分档守卫：`Assets/_Project/Scripts/Tests/EditMode/Rendering/RenderPipelineTiersTests.cs`
- Showcase（2026-09-28 重写，走 Boot 真实流程）：`Assets/_Project/Scripts/Tests/Showcase/IsometricExploration/IsometricExplorationShowcase.cs`
  纸片场景适配用例 `WalkUpStairs_BodyRisesStepByStepThenLandsBack`：标题「开始」进 SampleScene
  （`EnterDemoWorld`），切跑绕开村口演出触发区走到楼梯口，虚拟摇杆真走上楼梯（不再 `Reset` 瞬移），
  检查 `EncounterSceneView.PlayerScenePosition.y` 逐级只升不降、抬升量与台阶顶高度对得上，再走回平地确认落回地面高度。
  原有的潜行接近 / 攻击击杀用例已挪到 Player / Monster 两份回放（每个行为只演一次），本文件不再覆盖。
  同时保留 `CameraDistanceCulling_HidesDistantVisualAndRestoresIt`，同样先经 `EnterDemoWorld` 进场，再验证相机距离剔除。
- Showcase 通过 `ShowcaseOptions.DemoScenePath` 推出的场景名加载 SampleScene（走 Boot 真实流程时由标题「开始」触发同一次加载），
  这就是定案做法，不再建固定验证场景。
- Showcase（波 9）：`ExplorationShowcase` 追加 `Collision_FenceBlocksPlayer`（摇杆顶围栏，z 不越过 −0.7）、
  `MultiLevel_RampLeadsToDeck`（沿坡道走上甲板，高度单调不降、终点 y ≥ 7.8）、
  `Occluder_FadesBridgeWhenPlayerBeneath`（人在桥后侧时桥变 `M_Graybox_Faded`，离开恢复），共 7 条。
- Showcase 报告落 `Logs/verify/isometricexploration/`（已 gitignore，不进版本库）；「探索场景 2.5D
  表现升级」本次验证结果 PASS。

## 修改时检查

- 改移动：确认唯一位置来源仍为遭遇规则；不要新增任何写 `Rigidbody` 速度的移动控制器（历史原型已删除，见「历史记录」）；
- 改输入：继续使用 Input Action，不直接读取设备键；
- 改 Visual：确认 Rigidbody 与 Collider 没有被移动到倾斜节点；
- 改 Billboard：同时验证纸片朝向和 Collider 前表面对齐；
- 改 Collider：通过 Unity 编辑器修改并保存场景，不手改 `.unity` YAML；
- 改相机缓动：在角色持续移动和突然停止两种状态下检查构图；角色位置已在视图里按 tick 余量插值，
  相机不要再自己做一层 tick 对齐；排查抖动先确认 `EncounterSceneView.Bind` 收到了 alpha 源（正式流程由 `MonsterEncounterState` 传）；
- 改配置字段：同步配置资产和本指南；
- 改渲染分档 / 光影 / 后处理：高低档一起改（`UniversalRP*.asset` 与对应 `UniversalRenderer*.asset`、
  `QualitySettings.asset` 映射），跑 `RenderPipelineTiersTests`；
- 改角色纸片材质：确认仍用 `M_SpriteDepthClip`（玩家用 `M_SpriteDepthClip_Player`），不要回退到 URP 默认 Sprite 材质（会丢失遮挡与 SSAO）；
  改 `SpriteDepthClip.shader` 的深度计算时三个相机视角 Pass 一起改，跑 `SpriteDepthBiasWiringTests` 并在 Play 里让玩家与同排 NPC 重叠看一眼；
- 改灰盒/环境模型：新对象挂在 `Environment_Graybox` 下，不要改动 `PlayerSpawn` / `PatrolPoint0` /
  `PatrolPoint1` 的位置；新增的可站立物体要放进 `Ground` 层，否则贴地射线打不到，角色纸片会悬空；
- 改贴地参数（`groundMask` / `groundProbeHeight` / `groundProbeDepth` / `maxStepHeight`）：跑
  `EncounterSceneViewTests.cs` 里 `EncounterProjection` 的 5 条 `ResolveGroundY` + 4 条 `ResolveFlipX`
  用例与 Showcase 的 `WalkOntoStairs_RaisesBody`；
- 改万向标摆位规则：改 `ExplorationCompassRules`（纯函数）并跑 `ExplorationCompassRulesTests`，不要在
  `ExplorationCompassPresenter.Tick` 里另写一份判断；
- 改控件区显隐 / 重置流程：`ExplorationControlsPresenter` 只读 `Game.Loot` / `Game.Quest` / `Game.Player`
  的公开成员，不要反向让那些模块认识探索 HUD；
- 加新的兴趣点或 HUD 控件：见 `isometricexploration-extension-guide.md`；
- 提交前：运行项目 lint、刷新 Unity 编译并读取 Console 错误。
