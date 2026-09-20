---
type: module-guide
module: isometricexploration
layer: runtime
maturity: seed
---

# IsometricExploration 模块指南

## 目的

IsometricExploration 是 `SampleScene` 中的 2.5D / 3D 混合原型。
它用 3D 物理承载移动和碰撞，同时让 2D Sprite 纸片保持与摄像机成像平面平行。

当前解决三个问题：

- 角色通过 `Rigidbody` 和 3D `CapsuleCollider` 在 XZ 地面移动；
- Sprite 纸片随摄像机倾角旋转，但逻辑碰撞体保持竖直；
- 摄像机保留初始构图偏移，并在角色停止时平滑收敛。

这仍是场景原型，不是正式探索系统。模块暂不处理动画状态机、导航、存档、战斗状态或确定性回放。

## 当前组成

| 类型 | 位置 | 职责 |
| --- | --- | --- |
| `CameraBillboard` | `Assets/_Project/Scripts/Runtime/IsometricExploration/CameraBillboard.cs:8` | 旋转纸片，并可校准竖直 `BoxCollider` 的前表面 |
| `SmoothCameraFollow` | `Assets/_Project/Scripts/Runtime/IsometricExploration/SmoothCameraFollow.cs:8` | 保持初始偏移并平滑跟随目标 |
| `IsometricExplorationConfig` | `Assets/_Project/Scripts/Runtime/IsometricExploration/IsometricExplorationConfig.cs:8` | 保存移动速度、排序兼容参数和相机缓动时间 |
| `IsometricPlayerController3D` | `Assets/_Project/Scripts/Tests/Showcase/IsometricExploration/IsometricPlayerController3D.cs:10` | 把 `Gameplay/Move` 输入应用到 3D `Rigidbody` |

`IsometricPlayerController3D` 位于 Showcase 程序集，只用于当前原型。
不要把它当作正式玩家控制器，也不要让它写入正式玩法状态。

## 场景结构

当前接线保存在 `Assets/Scenes/SampleScene.unity`。

角色建议保持以下层级：

```text
Player
├─ Rigidbody
├─ CapsuleCollider
├─ PlayerInput
├─ IsometricPlayerController3D
└─ Visual
   ├─ SpriteRenderer
   └─ CameraBillboard
```

环境纸片建议保持以下层级：

```text
PropRoot
├─ BoxCollider
└─ Visual
   ├─ SpriteRenderer
   └─ CameraBillboard
```

`Visual` 是只负责显示的子节点。纸片倾斜只发生在这个节点上；`Rigidbody` 和 3D Collider 留在根节点。
这样视觉可以面向摄像机，物理体仍保持竖直，不会因为斜碰撞面产生攀爬效果。

## 3D 移动与碰撞

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

`CameraSmoothTime` 越小，跟随越紧；越大，停下后的缓动越明显。
当前默认值为 `0.2` 秒。

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
```

## 已知限制

- `CameraBillboard` 直接复制摄像机完整旋转，不支持只绕单一轴 Billboard；
- Collider 校准以世界 Z 为目标轴，适用于当前固定构图，不是任意朝向通用解；
- Collider 仍为长方体，无法精确拟合不规则 Sprite 轮廓；
- 摄像机跟随只做位置缓动，没有边界、前视、死区或碰撞避让；
- 3D 控制器属于 Showcase，不进入正式可重放玩法状态；
- 当前没有专门的 PlayMode 自动化测试，场景接线仍需在 Unity 中试玩确认。

## 修改时检查

- 改移动：确认 XZ 速度更新仍保留 Y 速度和重力；
- 改输入：继续使用 Input Action，不直接读取设备键；
- 改 Visual：确认 Rigidbody 与 Collider 没有被移动到倾斜节点；
- 改 Billboard：同时验证纸片朝向和 Collider 前表面对齐；
- 改 Collider：通过 Unity 编辑器修改并保存场景，不手改 `.unity` YAML；
- 改相机缓动：在角色持续移动和突然停止两种状态下检查构图；
- 改配置字段：同步配置资产和本指南；
- 提交前：运行项目 lint、刷新 Unity 编译并读取 Console 错误。
