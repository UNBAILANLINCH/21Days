---
type: module-guide
module: isometricexploration
layer: runtime
maturity: stable
---

# IsometricExploration 模块指南

## 目的

IsometricExploration 是 `SampleScene` 中的 2.5D / 3D 混合原型。
当前由确定性遭遇规则承载移动，2D Sprite 纸片保持与摄像机成像平面平行。早期 3D 物理控制器保留在工程中，但不参与当前场景推进。

当前解决四个问题：

- 角色通过 `EncounterStep` 在逻辑 XY 移动，再投影到场景 XZ；
- Sprite 纸片随摄像机倾角旋转，但逻辑碰撞体保持竖直；
- 摄像机保留初始构图偏移，并在角色停止时平滑收敛。
- 把 Player/Monster 的确定性 XY 逻辑坐标投影到等距场景 XZ 平面。

这仍是场景表现原型，不是正式探索系统。战斗状态与确定性回放复用 Player/Monster；
本模块不增加背刺处决、障碍物视线、物理碰撞判定、寻路或正式动画。

## 当前组成

| 类型 | 位置 | 职责 |
| --- | --- | --- |
| `CameraBillboard` | `Assets/_Project/Scripts/Runtime/IsometricExploration/CameraBillboard.cs:7` | 旋转纸片，并可校准竖直 `BoxCollider` 的前表面；本次升级中 `NameTag` 子节点也复用它保持朝向摄像机 |
| `SmoothCameraFollow` | `Assets/_Project/Scripts/Runtime/IsometricExploration/SmoothCameraFollow.cs:8` | 保持初始偏移并平滑跟随目标 |
| `IsometricExplorationConfig` | `Assets/_Project/Scripts/Runtime/IsometricExploration/IsometricExplorationConfig.cs:8` | 保存移动速度、排序兼容参数和相机缓动时间 |
| `IsometricPlayerController3D` | `Assets/_Project/Scripts/Tests/Showcase/IsometricExploration/IsometricPlayerController3D.cs:10` | 把 `Gameplay/Move` 输入应用到 3D `Rigidbody` |
| `StandaloneEncounterController` | `Assets/_Project/Scripts/Runtime/Monster/StandaloneEncounterController.cs:12` | 直接播放场景时，用现有遭遇规则读取 Gameplay 输入并推进角色、敌人与战斗 |

`IsometricPlayerController3D` 位于 Showcase 程序集，只用于当前原型。
不要把它当作正式玩家控制器，也不要让它写入正式玩法状态。
场景停用该物理控制器、重力和刚体推进；位置只由 `EncounterStep` 推进，
`EncounterSceneView` 负责把逻辑位置投影到场景。直接播放场景时由
`StandaloneEncounterController` 驱动；从 Boot 加载时它会自行停用，改由正式 `SimulationRunner` 驱动。

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

`Environment_Graybox`（灰盒环境几何体）与 `GlobalVolume`（后处理，见下文「表现层」）是与 `Encounter`
平级的场景根节点，不挂在 `Encounter` 下；`EncounterSceneView` 不引用它们，纯表现，不参与玩法判断：

```text
Environment_Graybox（场景根）
├─ Ground / Wall_Back / Wall_Left / Tower / Stairs
└─ Fence / Cone_1..3 / Bench_1..2

GlobalVolume（场景根，Global + ExplorationVolumeProfile）
```

`Environment_Graybox` 下全部物体统一放在新建 Layer `Ground`（slot 8，`ProjectSettings/TagManager.asset`），
`Encounter/EncounterSceneView.groundMask` 只勾这一层，供贴地射线专用；这一层只用于贴地探测，
不代表玩法碰撞层，新增环境物体记得同样放进 `Ground` 层，否则角色纸片走上去不会贴地。

角色（`player` / `enerme`）当前层级；根节点已改为**脚底**、缩放归一 `(1, 1, 1)`，
Y = 地面高度 `4.8884`（`Assets/Scenes/SampleScene.unity:4581`，`CapsuleCollider.center (0, 0.8, 0)`、
`height 1.6`、`radius 0.3`，即碰撞体从脚底往上量）：

```text
player（根节点，脚底，缩放 (1, 1, 1)，y = 4.8884）
├─ Rigidbody / CapsuleCollider(center 0,0.8,0 / height 1.6 / radius 0.3) / PlayerInput / IsometricPlayerController3D
├─ Visual
│  ├─ SpriteRenderer（Chibi_Player.png，96×160，Pivot BottomCenter，PPU 100，世界尺寸 0.96×1.6，材质 M_SpriteDepthClip）
│  └─ CameraBillboard
├─ BlobShadow（贴地阴影纸片，localPosition.y 0.02，世界直径 0.9）
├─ SelectRing（状态指示环纸片，localPosition.y 0.02，世界直径 1.1，已启用，赋给
│  EncounterSceneView.playerStateIndicator，状态色改染在这个环上而不是本体纸片）
└─ NameTag（World Space Canvas + CameraBillboard + Image + TMP「玩家」，localPosition.y 1.9，
   localScale 0.007，世界高 0.35）
```

`enerme` 同构，Visual 用 `Chibi_Patrol.png`；`SelectRing` 同样已启用并赋给
`monsterStateIndicator`；`NameTag` 文字为「巡逻者」；`enerme/Visual` 的 `localPosition.z` 仍为
`0.05`，避免两角色重合时与 `player/Visual` 发生 z-fighting。

根节点缩放已归一为 `(1, 1, 1)`，`BlobShadow` / `SelectRing` / `NameTag` 的局部尺寸此前需要按非等比
根缩放换算的问题已不存在；新角色若根节点仍做非等比缩放，才需要按对应轴换算。新增角色纸片的完整
步骤见 `isometricexploration-extension-guide.md`。

`Visual` 的纸片 `SpriteRenderer` 隐藏但保留（`enabled = false`，sprite 不删：仍是 `EncounterSceneView` 的
`EnsureSprite` 与 `flipX` 载体），小人预制体 `ChibiPuppet_Player` / `ChibiPuppet_Patrol` 挂在其下，
见 characterpuppet 模块（`ai-docs/docs/modules/characterpuppet/characterpuppet-module-guide.md`）。

`Visual` 是只负责显示的子节点。纸片倾斜只发生在这个节点上；`Rigidbody` 和 3D Collider 留在根节点。
这样视觉可以面向摄像机，物理体仍保持竖直，不会因为斜碰撞面产生攀爬效果。

**环境纸片（`PropRoot` + `SpriteRenderer` + `CameraBillboard` 层级）已停用、待美术替换**：原六个
「室内*」纸片物体仍在场景中但已停用（未删除，未来正式表现如仍需要 2D 占位可重新启用）；正式/灰盒
环境改用 `Environment_Graybox` 下的 3D 几何体，见下文「表现层」与扩展指南的「新增灰盒/正式环境模型的步骤」。

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

`SpriteImportProcessor`（`Assets/_Project/Scripts/Editor/Importers/SpriteImportProcessor.cs`）已改为
高清手绘预设（Bilinear / mipmap / Compressed / PPU 100），新角色/环境纸片素材导入时按这份预设走，
不要手改单张贴图的导入设置。

## 历史 3D 移动验证（当前遭遇停用）

角色根节点必须同时具备：

- `Rigidbody`；
- 3D `CapsuleCollider`；
- `PlayerInput`；
- `IsometricPlayerController3D`；
- `IsometricExplorationConfig` 引用。

地面和障碍必须使用 3D Collider。`Collider2D` 与 `Rigidbody` 不属于同一套物理系统，二者不会产生碰撞。

控制器在 `FixedUpdate` 中只覆盖 XZ 速度，保留 `Rigidbody.velocity.y`，因此重力和落地仍由 Unity 3D 物理处理。
`Awake` 会冻结刚体旋转，防止角色因碰撞侧翻。

`PlayerInput` 继续复用现有 `GameInput.inputactions` 的 `Gameplay/Move`，通过 `OnMove(InputValue)` 接收输入。
不要在此脚本里直接读取具体键盘按键。

上述物理控制器只服务历史物理验证。直接播放当前场景由 `PlayerInput → StandaloneEncounterController → EncounterStep` 推进；J/G 按下事件会缓存到下一个物理帧，避免短按丢失。正式遭遇输入由 `LiveInputSource → InputCommand → EncounterStep`
推进，Unity 物理只保留为环境表现，不参与位移、感知或命中判定。

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

当前 `SampleScene` 里 `Main Camera` 的具体接线：透视、FOV 28、旋转 `(38, 0, 0)`，Near 0.5 / Far 100；
`Start` 记录的 `offset` 落地为 `player 根 + (0, 11, -14)`；`UniversalAdditionalCameraData.rendererIndex`
= 1（对应表现层里的 `UniversalRenderer` / `UniversalRenderer_Mobile`，不是索引 0 的 `Renderer2D`），
Post Processing 开，Background 颜色等于雾色。改构图（FOV / 旋转 / 偏移）在编辑器里调 `Main Camera`
的 Transform 与 `SmoothCameraFollow.target`，脚本本身不用改。

## 配置资产

配置资产位于：

`Assets/_Project/Data/IsometricExploration/IsometricExplorationConfig.asset`

| 参数 | 当前值 | 用途 |
| --- | ---: | --- |
| `MoveSpeed` | 3 | 角色 XZ 平面移动速度 |
| `CameraSmoothTime` | 0.2 | 摄像机平滑跟随时间 |

`SortingScale` 是早期 2D 排序验证的兼容字段，当前 `SampleScene` 不读取它。
在旧排序验证代码退出工作区后，应连同该字段一起删除。

## 依赖方向

运行时代码只依赖 UnityEngine 和项目的 Runtime 程序集。
Showcase 控制器额外依赖 Unity Input System，因此 `Game.Tests.Showcase.asmdef` 必须引用 `Unity.InputSystem`。

数据流如下：

```text
Gameplay/Move
  → PlayerInput
  → IsometricPlayerController3D
  → Rigidbody.velocity（XZ）
  → Unity 3D Physics

Player Transform
  → SmoothCameraFollow
  → Main Camera position
  → CameraBillboard
  → Visual rotation / BoxCollider.center 校准

InputCommand
  → EncounterStep
  → PlayerModel / MonsterModel（XY）
  → EncounterSceneView（XY → XZ）
  → player / enerme Transform
```

## 已知限制

- `CameraBillboard` 直接复制摄像机完整旋转，不支持只绕单一轴 Billboard；
- Collider 校准以世界 Z 为目标轴，适用于当前固定构图，不是任意朝向通用解；
- Collider 仍为长方体，无法精确拟合不规则 Sprite 轮廓；
- 摄像机跟随只做位置缓动，没有边界、前视、死区或碰撞避让；
- 3D 控制器属于原型，不进入正式可重放玩法状态，遭遇时会被停用；
- 场景引用保存在 `EncounterSceneView`，重命名角色不会触发运行时名称查找；
- 玩法规则不读取 Rigidbody 或 Collider，角色会穿过环境碰撞体；
- 当前没有专门的 PlayMode 自动化测试，场景接线仍需在 Unity 中试玩确认；
- 贴地投影是表现层：`groundMask` 只影响 `EncounterSceneView` 里纸片的世界 Y，逻辑层没有高度、
  不做障碍或视线判定，玩法规则依旧不读取贴地结果；
- 两角色重合时 `NameTag` 会叠在一起，没有做避让或层级排序。

## 验证入口

- EditMode：`Assets/_Project/Scripts/Tests/EditMode/Monster/EncounterSceneViewTests.cs`
  （含 `EncounterProjection` 的 5 条 `ResolveGroundY` + 4 条 `ResolveFlipX` 用例）。
- 渲染分档守卫：`Assets/_Project/Scripts/Tests/EditMode/Rendering/RenderPipelineTiersTests.cs`
- Showcase：`Assets/_Project/Scripts/Tests/Showcase/IsometricExploration/IsometricExplorationShowcase.cs`，
  公共绑定逻辑抽到 `BindEncounter()`；除原有 `SneakApproach_ThenAttack_KillsMonster`，新增
  `WalkOntoStairs_RaisesBody`：逐级把玩家 `Reset` 到 `Stairs_Step_1..3`，检查
  `EncounterSceneView.PlayerScenePosition.y` 相对地面抬升 ≥0.55，再 `Reset` 回平地确认落回地面高度。
- 当前 Showcase 直接加载 `Assets/Scenes/SampleScene.unity`；固定验证场景待 Unity MCP 可用后保存到
  `Assets/_Project/Scenes/Verify/IsometricExploration.unity`。
- Showcase 报告落 `Logs/verify/isometricexploration/`（已 gitignore，不进版本库）；「探索场景 2.5D
  表现升级」本次验证结果 PASS。

## 修改时检查

- 改移动：确认唯一位置来源仍为遭遇规则，旧物理控制器保持停用；
- 改输入：继续使用 Input Action，不直接读取设备键；
- 改 Visual：确认 Rigidbody 与 Collider 没有被移动到倾斜节点；
- 改 Billboard：同时验证纸片朝向和 Collider 前表面对齐；
- 改 Collider：通过 Unity 编辑器修改并保存场景，不手改 `.unity` YAML；
- 改相机缓动：在角色持续移动和突然停止两种状态下检查构图；
- 改配置字段：同步配置资产和本指南；
- 改渲染分档 / 光影 / 后处理：高低档一起改（`UniversalRP*.asset` 与对应 `UniversalRenderer*.asset`、
  `QualitySettings.asset` 映射），跑 `RenderPipelineTiersTests`；
- 改角色纸片材质：确认仍用 `M_SpriteDepthClip`，不要回退到 URP 默认 Sprite 材质（会丢失遮挡与 SSAO）；
- 改灰盒/环境模型：新对象挂在 `Environment_Graybox` 下，不要改动 `PlayerSpawn` / `PatrolPoint0` /
  `PatrolPoint1` 的位置；新增的可站立物体要放进 `Ground` 层，否则贴地射线打不到，角色纸片会悬空；
- 改贴地参数（`groundMask` / `groundProbeHeight` / `groundProbeDepth` / `maxStepHeight`）：跑
  `EncounterSceneViewTests.cs` 里 `EncounterProjection` 的 5 条 `ResolveGroundY` + 4 条 `ResolveFlipX`
  用例与 Showcase 的 `WalkOntoStairs_RaisesBody`；
- 提交前：运行项目 lint、刷新 Unity 编译并读取 Console 错误。
