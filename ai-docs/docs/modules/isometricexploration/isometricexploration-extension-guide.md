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
   Pivot 必须是 **Bottom Center**（贴 `Chibi_Player.png` / `Chibi_Patrol.png` 的做法：96×160、PPU
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

## 验证

坐标映射与适配器接线放 EditMode 测试；玩家可见行为放 IsometricExploration Showcase。
场景资产只通过 Unity 编辑器或 Unity MCP 修改，不手改 YAML。
