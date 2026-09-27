---
type: module-guide
module: lailaface
layer: runtime
maturity: seed
---

# LailaFace 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Gameplay/` 中与 Head-topo 面部控制相关的代码前读这份。
> 这是复刻莱拉脸部玩法的原型模块，当前只服务 `Assets/_Project/Scenes/laila.unity`。

## 职责边界

| 做 | 不做 |
| --- | --- |
| 缓存 Head-topo 的 16 个 BlendShape 名称与索引 | 不修改 Blender 源文件或重新生成 BlendShape |
| 把 8 个控制区的垂直拖拽映射成 Up / Down 成对权重 | 不直接读取 `Input.mousePosition` 或 `Input.touches` |
| 给 Head-topo 提供局部壁画感材质 | 不添加全局 URP Renderer Feature，不影响其他场景 |
| 在 laila 场景中提供可回放的数值验证 | 不接入正式游戏流程、存档或跨场景状态 |

## 类与资产分工

| 类型 | 路径 | 职责 |
| --- | --- | --- |
| `FaceBlendShapeController` | `Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs` | 缓存 Renderer，提供单个权重、成对权重与 Reset API |
| `FaceDragHandle` | `Assets/_Project/Scripts/Runtime/Gameplay/FaceDragHandle.cs` | 接收 EventSystem 指针拖拽，将一个控制区映射到一个成对控制 |
| `MuralFace` Shader | `Assets/_Project/Art/Shaders/MuralFace.shader` | 只负责 Head-topo 的分层明暗、边缘光和轻微颗粒 |
| `MuralFaceController` | `Assets/_Project/Scripts/Runtime/Gameplay/MuralFaceController.cs` | 在 Head-topo Inspector 调整阴影方向、阴影强度、边缘光和纸张颗粒 |
| `LailaFaceShowcase` | `Assets/_Project/Scripts/Tests/Showcase/LailaFace/LailaFaceShowcase.cs` | 回放 16 个形变、成对映射与重置 |
| `LailaFaceSetupMenu` | `Assets/_Project/Scripts/Editor/Tools/LailaFaceSetupMenu.cs` | 在 laila 场景一键创建或复用 8 个控制区及输入接收组件 |

当前没有 ScriptableObject：BlendShape 名称是 Head-topo 的固定资产接口，控制范围固定为 `-1..1`，不需要额外配置资产。

## 控制映射

一个控制区对应一个 Up / Down 成对值：

| 控制区 | Up | Down |
| --- | --- | --- |
| `MouthLeft` | `Mouth_L_Up` | `Mouth_L_Down` |
| `MouthRight` | `Mouth_R_Up` | `Mouth_R_Down` |
| `BrowLeft` | `Brow_L_Up` | `Brow_L_Down` |
| `BrowRight` | `Brow_R_Up` | `Brow_R_Down` |
| `EyeLeftUpperLid` | `Eye_L_UpperLid_Up` | `Eye_L_UpperLid_Down` |
| `EyeLeftLowerLid` | `Eye_L_LowerLid_Up` | `Eye_L_LowerLid_Down` |
| `EyeRightUpperLid` | `Eye_R_UpperLid_Up` | `Eye_R_UpperLid_Down` |
| `EyeRightLowerLid` | `Eye_R_LowerLid_Up` | `Eye_R_LowerLid_Down` |

控制值约定：`-1` 为 Down 100，`0` 为基础状态，`+1` 为 Up 100。拖拽向上增加值，向下减少值；达到一侧时另一侧自动归零。

## 一键创建控制区

打开 `Assets/_Project/Scenes/laila.unity` 后，在 Unity 菜单执行：

```text
21Days / LailaFace / 一键创建 Head-topo 控制区
```

如果控制区已经调好，只想更新外观，执行：

```text
21Days / LailaFace / 仅应用聊斋材质
```

该菜单只替换 Head-topo 的材质槽，不移动或重建控制区。

工具只接受当前活动场景为 `laila.unity`；如果场景里没有 `Head-topo`，会先从 `Assets/_Project/Art/fbx/Head-topo.fbx` 实例化。之后在 `Head-topo` 下创建或复用以下 8 个子物体：

```text
Control_Mouth_L
Control_Mouth_R
Control_Brow_L
Control_Brow_R
Control_Eye_L_Upper
Control_Eye_L_Lower
Control_Eye_R_Upper
Control_Eye_R_Lower
```

每个控制区包含 `SphereCollider` 与 `FaceDragHandle`，并自动设置对应的 `FaceControl`。工具同时把 laila 主相机切到当前 `UniversalRP.asset` 中的 3D `UniversalRenderer`，确保 `SkinnedMeshRenderer` 能进入 Game 视图；并确保场景相机有 `PhysicsRaycaster`、场景有 `EventSystem + InputSystemUIInputModule`，绑定 `GameInput.inputactions` 的 UI 指针动作，创建或复用 `Assets/_Project/Art/Materials/Laila/M_LailaFace_Mural.mat`、添加 `MuralFaceController` 并只赋给 Head-topo，最后保存 `laila.unity`。控制区位置按当前相机和 Head-topo 的包围盒估算，首次生成后应在 Scene 视图微调碰撞体位置与半径；再次执行工具会复用已有控制区，不重置已调整的位置、碰撞体半径和拖拽参数。

## 依赖方向

```text
Game.LailaFace（Runtime / 表现）
  └─ UnityEngine + UnityEngine.EventSystems

Game.Tests.Showcase.LailaFace
  └─ Game.Runtime + Game.Tests.Showcase Framework
```

控制器与拖拽区只写表现状态，不参与逻辑模拟；拖拽事件由 Unity EventSystem 派发，运行时没有平台分支。

## laila 场景接线

未执行一键菜单前，场景应接线为：

```text
Head-topo（Head-topo.fbx 实例）
├─ SkinnedMeshRenderer
├─ FaceBlendShapeController（Face Renderer 指向自身 SkinnedMeshRenderer）
└─ 8 个控制区子物体
   ├─ SphereCollider + FaceDragHandle（MouthLeft）
   ├─ SphereCollider + FaceDragHandle（MouthRight）
   ├─ SphereCollider + FaceDragHandle（BrowLeft）
   ├─ SphereCollider + FaceDragHandle（BrowRight）
   ├─ SphereCollider + FaceDragHandle（EyeLeftUpperLid）
   ├─ SphereCollider + FaceDragHandle（EyeLeftLowerLid）
   ├─ SphereCollider + FaceDragHandle（EyeRightUpperLid）
   └─ SphereCollider + FaceDragHandle（EyeRightLowerLid）
```

主相机需要 `UniversalAdditionalCameraData.rendererIndex = 1`（当前项目的 3D `UniversalRenderer`）和 `PhysicsRaycaster`，场景需要 `EventSystem`。每个 `FaceDragHandle` 的 `Face` 指向同一个 `FaceBlendShapeController`。调试阶段保留控制区 Renderer，确认位置后再关闭 Renderer，只保留 Collider。

Head-topo 的材质使用 `MuralFace` Shader；材质资产位于 `Assets/_Project/Art/Materials/Laila/M_LailaFace_Mural.mat`，同一个材质可复用到 FBX 的多个子网格槽位。`MuralFaceController` 用 `MaterialPropertyBlock` 覆盖阴影参数，不复制材质实例。当前只给该模型设置材质，不修改其他场景的材质或 URP Renderer 资产。

## 验证

| 类型 | 路径 | 覆盖 |
| --- | --- | --- |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/LailaFace/LailaFaceShowcase.cs` | 16 个名称、左嘴角 Up/Down、右上眼皮 Up/Down、Reset |
| 场景 | `Assets/_Project/Scenes/laila.unity` | Head-topo 与 8 个控制区的肉眼表现 |

Showcase 直接加载 `laila.unity`，这是遵循本任务“测试场景在 laila、其他场景不改”的特例；没有额外创建 `Scenes/Verify/LailaFace.unity`。

执行一键菜单并确认场景保存后运行 `/verify-module LailaFace`，先看数值检查，再由开发者确认拖拽区域和壁画材质外观。

## 已知限制

- 当前文档状态为 `seed`：代码、自动化接线工具与 Shader 已落地，但 Unity 场景实际执行菜单、材质资产生成和回放验收仍需确认。
- `MuralFace` 当前按“聊斋古画”处理：方向性分层阴影、冷墨轮廓、冷月边缘光、旧纸颗粒与极弱朱砂洗染；使用程序化颗粒，不提供墙面纹理输入。需要真实墙面纹理时再增加 `_GrainMap`，不要把纹理硬编码进 Shader。
- 8 个控制区是垂直单轴控制，不包含水平拖拽、镜像联动或表情预设。
