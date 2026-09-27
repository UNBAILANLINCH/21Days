---
type: extension-guide
module: isometricexploration
layer: runtime
maturity: stable
---

# IsometricExploration 扩展指南

## 更换玩家或敌人纸片

1. 保留 `player` 与 `enerme` / `enemy` 根对象名，或同步修改适配器查找契约。
2. 让可见子节点拥有启用的 `SpriteRenderer` 和 `CameraBillboard`。
3. Rigidbody、Collider 留在根节点，纸片倾斜只发生在 Visual。
4. 运行 IsometricExploration Showcase，检查潜行色、敌对色与死亡色。

## 调整巡逻范围

当前适配器以敌人初始位置为第一个点，沿世界 X 正方向四单位创建第二个点。
若需要策划布点，优先在场景增加显式 Marker，再让适配器读取；不要把巡逻点反推自碰撞体或画面位置。

## 新增一个角色纸片的标准步骤

角色根节点语义是**脚底**：根 `Transform` 的位置即站立点，缩放保持 `(1, 1, 1)`（当前 `player` /
`enerme` 均已归一，不要再引入非等比缩放）；`CapsuleCollider.center` 相应上移半个身高
（参考 `(0, 0.8, 0)`，`height 1.6`）。按这个语义，子节点的局部尺寸/位置就是世界尺寸/位置，不需要
换算。

1. 材质：SpriteRenderer 用 `Assets/_Project/Art/Materials/Character/M_SpriteDepthClip.mat`
   （着色器 `SpriteDepthClip.shader`），不要用 URP 默认 Sprite-Lit/Unlit-Default——那套不写深度、
   没有 ShadowCaster/DepthNormals Pass，纸片不会被灰盒环境遮挡、不参与 SSAO。新纸片素材的导入
   Pivot 必须是 **Bottom Center**（早期纸片 `Chibi_Player.png` / `Chibi_Patrol.png` 的做法，二图已于 2026-09-28 删除：96×160、PPU
   100、Mesh Type FullRect），这样 `Visual` 挂在根节点原点时纸片底边正好落在脚底，不需要额外偏移。
2. 贴地阴影：加 `BlobShadow` 子节点，SpriteRenderer 用 `Art/Sprites/Fx/Fx_BlobShadow.png`
   （`Sprite-Unlit-Default` 材质），`localPosition.y` 参考 `0.02`（避免与地面 z-fighting），世界直径
   参考 0.9。
3. 状态指示环：加 `SelectRing` 子节点，用 `Fx_SelectRing.png`，`localPosition.y` 参考 `0.02`，世界
   直径参考 1.1，色 `(1, .85, .3)`；启用后赋给 `EncounterSceneView.playerStateIndicator` /
   `monsterStateIndicator`，状态色（潜行/伪装/警戒/敌对/死亡等）会染在这个环上而不是本体纸片——
   现在两个角色的 `SelectRing` 都常驻启用当状态指示，不再是「仅选中时才显示」的语义，若要额外做
   选中反馈需要另建节点，不要复用这个字段。
4. 名牌：加 `NameTag` 子节点，World Space Canvas + 复用 `CameraBillboard`（保证文字朝向摄像机）+
   Image 底板 + TMP 文本，`localPosition.y` 参考 `1.9`、`localScale` 参考 `0.007`，世界高度约 0.35。
5. 根节点缩放：保持 `(1, 1, 1)`。若确有理由必须做非等比缩放，`BlobShadow`/`SelectRing`/`NameTag`
   这类按世界尺寸设计的子节点，局部尺寸要换算为「期望世界尺寸 / 对应轴的根缩放」，不能直接填世界
   尺寸数值——这是历史坑，正常情况不要引入它。
6. 前后叠放：多个纸片角色可能站到同一格时，给 `Visual`（或整个角色）一个小的 `localPosition.z`
   偏移（当前 `enerme/Visual` 用 `0.05`）避免与另一角色的纸片 z-fighting。
7. 若角色需要在环境上贴地行走，确认可站立的环境物体已放进 `Ground` 层（见「新增灰盒/正式环境模型
   的步骤」），否则 `EncounterSceneView` 的贴地射线打不到，角色会保持原高度悬空/陷地。
8. 跑 IsometricExploration Showcase，确认新角色能被灰盒环境正确遮挡，阴影/状态指示环/名牌显示
   正常，走上台阶等可站立物体时身体会贴地抬升。

## 新增灰盒/正式环境模型的步骤

1. 新几何体挂在场景根节点 `Environment_Graybox` 下，不要挂到 `Encounter` 下——`Encounter` 只放
   `EncounterSceneView` 引用的玩法对象。
2. 材质用标准 URP/Lit（不透明），不要用 `SpriteDepthClip`——那是给纸片角色用的 alpha-clip 着色器，
   不适合实心几何体。
3. Collider 按需要保留（挡人用 BoxCollider/MeshCollider 等），不挂 Rigidbody——当前灰盒环境都是
   静态碰撞体；角色需要能在这个物体上贴地站立/行走的，把它放进 Layer `Ground`（slot 8），否则
   `EncounterSceneView.groundMask` 的贴地射线打不到，角色纸片会保持原高度悬空。
4. 不要移动 `PlayerSpawn` / `PatrolPoint0` / `PatrolPoint1`；改了地面高度要反过来检查这些出生点/
   巡逻点是否还落在新地面上，需要时调点位，不要抬高整个地面迁就旧点位。
5. 正式美术替换灰盒时，先确认新模型的世界包围盒（尤其地面高度）与原灰盒一致，再整体替换，避免
   出生点/巡逻点悬空或陷地；单帧允许的抬升上限是 `maxStepHeight`（默认 0.32），新台阶/家具每级
   落差超过这个值时，角色会被视为「墙顶/家具」而不贴地，需要拆成更小的级差或调整该字段。
6. 跑 IsometricExploration Showcase 做一遍视觉回归（含 `WalkOntoStairs_RaisesBody` 用例）；
   灰盒/正式环境的最终验收仍交人工过一遍。

## 在探索 HUD 上加按钮（如走 / 跑）

1. 按钮预制体或子物体挂到 `ExplorationHudView.RunSlot` 下，或新开一个槽位（在预制体里经 MCP / 编辑器改，不手改 YAML）；
2. 需要新的 View 字段就加 `[SerializeField] private`（必填字段在 `Validate()` 里点名，可选字段全部容错为空操作，
   照 `stick` / `touchButtons` / `resetButton` 的写法）；事件照 `OnImmersiveToggle` / `OnResetClicked` 的写法抛出，
   逻辑放已有呈现器（`ExplorationHudPresenter` 管沉浸、`ExplorationControlsPresenter` 管走跑 / 摇杆 / 提示 / 重置）
   或新建一个呈现器，View 不注入服务；
3. 沉浸中默认行为不一致：整个 `ExplorationHudView` 留在画面上（`VisibleWhenHudHidden = true`），但
   `ControlsRoot` 下的按钮会随 `SetControlsVisible(false)` 一起隐藏；`RunToggle` 因为不在 `ControlsRoot` 下，
   用独立的 `runToggleGroup` 单独隐藏——新按钮想要「沉浸时隐藏」就挂进 `ControlsRoot`，想要「沉浸时也显示」
   就挂在外面并自己接一个 `CanvasGroup`，同样交给驱动它的呈现器在 `HudVisibilityChangedEvent` 里切。
4. 新增会挡视线的世界空间提示：读 `IHudVisibility.IsHudHidden` 或订阅 `HudVisibilityChangedEvent` 自行隐藏，不要每帧 Find。

## 加一种新的兴趣点种类（万向标指引）

1. `PoiKind` 加一个新枚举值；`ExplorationPointOfInterest.IsVisible` 按新种类加分支（默认 `true`，只有需要
   「达成条件后不再指引」的种类才像 `Crate` 那样读同物体的其它组件状态）。
2. 场景里给目标物体挂 `ExplorationPointOfInterest`，填 `label` 与新 `kind`；不需要改
   `ExplorationCompassPresenter`——它只认 `ExplorationPointOfInterest` 这一个类型，不关心具体 `Kind`。
3. 新种类若要引用别的模块的运行时状态（如 `Crate` 读 `Game.Loot.SupplyCrate`），依赖方向必须是
   `Game.IsometricExploration → 目标模块`，不要反过来让目标模块认识 `PoiKind`。

## 在探索 HUD 上加一个新控件（万向标之外，如小地图、状态条）

1. 新控件的显示逻辑写进 `ExplorationHudView` 的可空字段 + `SetXxx` 方法，不注入服务；
2. 驱动逻辑评估放进哪个呈现器：只依赖玩家 / 物资箱 / 任务状态且需要每帧刷新，跟 `ExplorationControlsPresenter`
   同类，可以直接加进去；需要扫描场景物体（如 `ExplorationCompassPresenter` 扫 `ExplorationPointOfInterest`），
   新建一个入口点，理由与两个呈现器的文件头注释一致——职责说不通就别硬塞进已有类；
3. 新入口点在 `ExplorationInstaller.Install` 里按构造函数参数 `RegisterEntryPoint`，排在它依赖的模块
   （Loot / Quest / Player）注册器之后。

## 验证

坐标映射与适配器接线放 EditMode 测试；玩家可见行为放 IsometricExploration Showcase。
场景资产只通过 Unity 编辑器或 Unity MCP 修改，不手改 YAML。
