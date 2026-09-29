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
| 缓存当前脸部网格的 BlendShape 名称与索引（扩展 FBX 为 22 个） | 不修改 Blender 源文件或重新生成 BlendShape |
| 12 个控制区：原有垂直拖拽、嘴角水平拖拽、独立上下唇与眼球旋转 | 不直接读取 `Input.mousePosition` 或 `Input.touches` |
| 给 Head-topo 提供局部壁画感材质 | 不添加全局 URP Renderer Feature，不影响其他场景 |
| 在 laila 场景中提供可回放的数值验证 | 不接入正式游戏流程、存档或跨场景状态 |

## 类与资产分工

| 类型 | 路径 | 职责 |
| --- | --- | --- |
| `FaceBlendShapeController` | `Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs` | 缓存 Renderer，提供单个权重、成对权重与 Reset API |
| `FaceDragHandle` | `Assets/_Project/Scripts/Runtime/Gameplay/FaceDragHandle.cs` | 接收 EventSystem 指针拖拽，写成对 / 单键权重，或旋转眼球 Pivot |
| `MuralFace` Shader | `Assets/_Project/Art/Shaders/MuralFace.shader` | 只负责 Head-topo 的分层明暗、边缘光和轻微颗粒 |
| `MuralFaceController` | `Assets/_Project/Scripts/Runtime/Gameplay/MuralFaceController.cs` | 在 Head-topo Inspector 调整阴影方向、阴影强度、边缘光和纸张颗粒 |
| `LailaFaceShowcase` | `Assets/_Project/Scripts/Tests/Showcase/LailaFace/LailaFaceShowcase.cs` | 回放 16 个形变、成对映射与重置 |
| `LailaFaceSetupMenu` | `Assets/_Project/Scripts/Editor/Tools/LailaFaceSetupMenu.cs` | 旧模型的 8 区接线工具；不用于当前扩展 FBX |

当前没有 ScriptableObject：BlendShape 名称是模型的固定资产接口；成对范围为 `-1..1`，唇部单键范围为 `0..100`，眼球角度在控制区 Inspector 配置。

## 控制映射

原有 8 个控制区保留 Up / Down 成对映射：

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

扩展交互复用嘴角区域，并新增 4 个区域，启用区域总数为 12，而不是每个 Key 各建一个区域：

| 控制区 | 对应目标 | 拖拽方式 |
| --- | --- | --- |
| 原 `Control_Mouth_L` | `Mouth_L_Out` / `Mouth_L_In`，保留原 Up / Down | 向屏幕左侧外拉，向右内收；可斜向同时改变两轴 |
| 原 `Control_Mouth_R` | `Mouth_R_Out` / `Mouth_R_In`，保留原 Up / Down | 向屏幕右侧外拉，向左内收 |
| `Control_UpperLip`（`UpperLip`） | `Mouth_UpperLip_Up.001` | 上拖张开，下拖收回到 0 |
| `Control_LowerLip`（`LowerLip`） | `Mouth_LowerLip_Down.001` | 下拖张开，上拖收回到 0 |
| `Control_Eye_L_Gaze`（`EyeLeftGaze`） | `Eye_L_Pivot` | 拖拽瞳孔附近控制视线；水平 ±20°、垂直 ±15° |
| `Control_Eye_R_Gaze`（`EyeRightGaze`） | `Eye_R_Pivot` | 同上，左右眼独立 |

唇部只需要现有两键，不伪造反向 Key；牙齿不联动。眼球与角膜一起随 Pivot 旋转，中心、局部位置及缩放不变，旋转方向以按下时的事件相机为准。控制区是脸部的子节点，不放到旋转 Pivot 下，避免拖动后拾取区移走。

Inspector 调整：`Full Range Screen Fraction` 越小越灵敏；嘴角勾选 `Enable Horizontal Drag`，左侧同时勾选 `Invert Horizontal`；眼球的 `Eye Pivot` 指向对应父节点，`Max Gaze Angles` 的 X/Y 分别为水平 / 垂直极限。新增 Collider 半径：唇部 0.004、眼球 0.006（当前世界缩放为 1）；有意小于现有眼皮区域，避免抢占邻近区域中心。若扩大半径，应重新检查射线首命中。

进入 Play 后在 Game 视图拖拽，不是用 Scene 移动工具。`FaceBlendShapeController.ResetFace` 重置全部 22 个形态，但不重置视线；眼球控制区组件的 `Reset Gaze` 上下文菜单可在运行态恢复初始朝向。旧枚举序号 0–7 未变，水平拖拽默认关闭，旧场景保留原行为。

## 旧模型的一键创建控制区

**当前扩展模型已接线，不需再执行此菜单。** 此工具按旧 `Head-topo` 名称和旧 FBX 选择目标，不会自动扩展成 12 区；以下只适用于原 16 键模型。

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

旧模型的接线示例（当前扩展模型以本节下方最新记录为准）：

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

主相机需要通过 `UniversalAdditionalCameraData.SetRenderer(1)` 选择当前项目的 3D `UniversalRenderer`，并具有 `PhysicsRaycaster`，场景需要 `EventSystem`；不要使用不存在的公开 `rendererIndex` 属性。每个 `FaceDragHandle` 的 `Face` 指向同一个 `FaceBlendShapeController`。调试阶段保留控制区 Renderer，确认位置后再关闭 Renderer，只保留 Collider。

Head-topo 的材质使用 `MuralFace` Shader；材质资产位于 `Assets/_Project/Art/Materials/Laila/M_LailaFace_Mural.mat`，同一个材质可复用到 FBX 的多个子网格槽位。`MuralFaceController` 用 `MaterialPropertyBlock` 覆盖阴影参数，不复制材质实例。当前只给该模型设置材质，不修改其他场景的材质或 URP Renderer 资产。

## 验证

| 类型 | 路径 | 覆盖 |
| --- | --- | --- |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/LailaFace/LailaFaceShowcase.cs` | 16 个名称、左嘴角 Up/Down、右上眼皮 Up/Down、Reset |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/LailaFace/FaceDragHandleTests.cs` | 双唇单键钳位与收回、嘴角双轴、旧行为、指针隔离、左右眼固定中心旋转与复位 |
| 场景 | `Assets/_Project/Scenes/laila.unity` | 扩展脸部与 12 个启用控制区 |

Showcase 直接加载 `laila.unity`，这是遵循本任务“测试场景在 laila、其他场景不改”的特例；没有额外创建 `Scenes/Verify/LailaFace.unity`。

当前 EditMode 用例不依赖具体场景或 FBX。Showcase 仍只覆盖原 16 个形态，不能代替扩展交互验收；拖拽手感、牙龈遮挡与材质外观仍需开发者肉眼确认。

### 新 FBX 导入检查与 EditMode 实测（2026-09-28）

本次使用临时命令行 MCP 客户端实际读取 `21Days` 编辑器；Codex 原生 MCP 客户端的初始化失败尚未解决，不能将两者混为同一连接状态。活动场景为 `laila`，未进入 Play 模式，也未运行 Showcase。全量 EditMode 测试通过：1028 通过、0 失败、0 跳过，用时约 41.78 秒，任务 ID 为 `eb2d10e1d5ba4bb4be14c7231f2e9176`。测试后 Console 中包含异常路径测试刻意产生的错误日志，不能据此认定测试失败；全量测试通过也不代表新模型的外观和交互已验收。

以下为当时编辑器实时状态，不保证与尚未保存的场景文件一致：

| 检查项 | 结果 |
| --- | --- |
| 旧 `Head-topo` | 已停用；其原有控制区未移动或重建 |
| 新模型根节点 `Head-topo-expression-extended2` | 额外挂有使用旧 `Head-topo.fbx` 网格的 `SkinnedMeshRenderer`；两个面部控制器的 `faceRenderer` 都指向该 Renderer，而非真实脸部子节点 |
| 真实脸部子节点 `Eve` | 使用新 FBX 网格，5 个材质槽仍为 FBX 内嵌材质，未应用 `M_LailaFace_Mural` |
| 正反朝向 | 相机位于 Z = -10、旋转为零；眼球父节点位于 Z ≈ +0.0702，支持当前画面是在观察头部背侧的判断 |
| 眼球层级 | 左右 `Eye_*_Pivot` 均保留，眼球和角膜为其子物体，局部位置为零；尚未进行 Unity 运行时转动验收 |

离线比较新旧 FBX 的脸部顶点、面索引及导出法线均相同，1068 个面的几何朝向与法线一致；没有证据支持将这次背面画面归因于“平滑：面”。新文件 `Assets/_Project/Art/fbx/Head-topo-expression-extended2.fbx` 导出了 22 个形态：原有 16 个、4 个嘴角横向形态，以及 `Mouth_UpperLip_Up.001` / `Mouth_LowerLip_Down.001`。不带后缀的上下唇四键未见于该 FBX，需核对导出资产，不能将 Blender 源文件的 Key 数量直接当作 Unity 导入结果。

`FaceBlendShapeController.BlendShapeCount` 是 `Awake` / `RebuildCache` 后的缓存数量；EditMode 下读取到 0 不能证明网格没有 BlendShape。本次只检查和测试、增量记录文档，没有修改场景、模型、材质或控制器代码。新模型的 Renderer 接线、正面显示、上下唇四键和新增交互仍未验收通过。

## 已知限制

### 牙齿旧化材质（2026-09-29，已保存）

同一几何的临时 Lit / Unlit 对比确认：当前场景缺少 3D 灯光，原牙齿 Lit 材质表现为暗色；换成 Unlit 后可见牙齿，但亮白色与古画脸部不协调。最终复用现有 `21Days/MuralFace` Shader，不新增灯光或 Shader：

- `Circle.002` / `Circle.003` 的槽 0 使用 `Assets/_Project/Art/Materials/Laila/M_LailaGingiva_Aged.mat`，基础色 RGBA 为 (0.31, 0.17, 0.14, 1)，压成暗肉褐色。
- 两者槽 1 使用 `Assets/_Project/Art/Materials/Laila/M_LailaTeeth_Aged.mat`，基础色为 (0.61, 0.54, 0.40, 1)，配合轻微洗染、颗粒与暗轮廓形成旧象牙色；不是烘焙的污渍贴图。

材质及 `.meta` 由 Unity 创建。保留用户本轮已调整的牙齿局部 Z（上牙 0.1152、下牙 0.1098），未修改位置、缩放、形态、控制区、脸部材质或源 FBX。张嘴预览将两唇权重临时设为 100，并烘焙临时网格供 Main Camera 渲染，避免 EditMode 蒙皮未及时刷新；检查后恢复原权重和 Renderer，销毁临时网格，再保存 `laila.unity`。预览为 `Temp/LailaFaceChecks/teeth-20260929-final-aged-open.png`，不作为运行时资产。

Console 读取到 0 条错误；本轮是材质外观检查，未运行自动化测试，也不代表所有表情组合的牙龈遮挡已验收。其他场景、共享 URP / Renderer、源 FBX 和 `MuralFace.shader` 前后 SHA256 一致。

### 当前扩展模型接线（2026-09-29，已修复）

已通过 MCP 检查 `laila` 的全部模型节点，而非仅按 `Eve` 名称寻找目标。启用的扩展模型来自 `Head-topo-expression-extended2.fbx`；其源脸部网格有 22 个形态、1226 个顶点和 5 个子网格。当前脸部 Renderer 已恢复使用该源网格，移除旧 `Head-topo.fbx` 的 16 形态网格覆盖；材质槽从 6 个调整为对应 5 个子网格，继续复用 `M_LailaFace_Mural`。

扩展模型下现有 8 个启用控制区的 `FaceDragHandle.face` 全部指向当前脸部的 `FaceBlendShapeController`；两个面部控制器的 `faceRenderer` 均指向当前 Renderer。旧 `Head-topo` 和其 8 个控制区仍停用，未删除或重建。眼球、角膜、牙齿均来自扩展 FBX，左右眼球颜色材质和透明角膜引用正确，未替换这些节点。

此次基础接线修复前后，扩展模型所有节点的局部位置、旋转和缩放，以及原控制区 Collider 的中心、半径和拖拽参数、其他 Renderer 的材质引用均未变化。未运行旧的一键创建菜单，没有修改共享 URP 设置或其他场景；新增交互随后完成，见下一节。

运行态验证通过 8/8：`EventSystem.RaycastAll` 在每个控制区中心的首个命中为该控制区；通过 `ExecuteEvents` 派发按下、上拖、下拖和释放事件，两侧分别达到约 50 权重、另一侧为 0，组件全部启用，控制器缓存 22 个形态。测试权重随后恢复；Console 未读到 `FaceDragHandle` 错误。`InputSystemUIInputModule` 的 Point / Click 已绑定 Mouse、Pen、Touchscreen。本检查验证了射线与指针事件处理链，不等同于真实鼠标设备输入或移动端实机触控验收。

本轮曾被另一个测试任务重载场景打断；等待其结束后才完成修复。并发测试期间不能复用此前读取的 instance ID，也不能在其他任务的 Play 模式下写资产。以下 2026-09-28 诊断属于修复前记录，不代表当前仍有旧网格或旧控制器引用错误。

### 扩展交互接入与验证（2026-09-29）

在现有 `FaceDragHandle` 上追加 4 种控制，不增加独立输入系统；嘴角水平开关只在扩展模型现有两区启用。新增 4 区均在扩展脸部子节点下，两个唇区按实际形变顶点平均位置定位，两个视线区按 Pivot 中心定位；碰撞区前移用于拾取，不移动眼球或牙齿。保存前比较 33 个已有节点变换、8 个旧碰撞体中心 / 半径、旧灵敏度 / 垂直反向参数，全部保持原值。

定向 EditMode：8 通过、0 失败、0 跳过，约 0.94 秒，任务 ID `d86c18f3e89447d5a335a6ead33c0c01`。运行态通过 12/12：各区域中心 `EventSystem.RaycastAll` 首个命中自身，再通过 `ExecuteEvents` 派发按下、拖拽和释放，检查原成对形态、嘴角双轴 / 极值互斥、唇部 50 / 100 / 回到 0，以及眼球四向 / 极值 / 重复拖拽复位。眼球父节点位置、缩放和子物体局部位置全程不变。检查后恢复权重和视线，退出 Play；Console 读取到 0 条错误。此次未运行全量测试，不将以前的全量通过数当作此次回归结果。

首轮脚本刷新没有导入新增用例，返回 0 个测试，不计入通过结果；强制刷新后取得上述 8 个真实结果，新测试及文件夹 `.meta` 由 Unity 生成。本轮验证是运行态射线与事件链，不等同于实际鼠标设备或移动端实机触控验收。小范围命中已检查，但未实现控制点随 BlendShape 形变移动，也未增加表情预设、左右眼联动或牙齿动画。

退出 Play 后场景处于待保存状态，确认全部权重为 0 后保存并重新加载：12 区引用、双眼 Pivot 和 22 个形态仍正确，`scene.isDirty = false`。本轮前后 `Boot.unity`、`SampleScene.unity`、`UniversalRP.asset`、`UniversalRenderer.asset` 的 SHA256 一致。修改文件 C# lint 通过；全仓 `gc_scan` 仍报告既有的 3 个 LailaFace 命名空间 / 目录不匹配及 Dynamic 字体数据体积问题，未在本轮修复这些无关项。

### 控制区触控失败检查（2026-09-28，只读诊断）

本次读取的是编辑器中未保存的 `laila`（`scene.isDirty = true`），未进 Play、未执行一键菜单、未保存或改动场景。当前状态与前面的阶段记录不同：

| 检查项 | 实测结果 |
| --- | --- |
| 控制区 | 旧 `Head-topo` 下有停用的 8 个；可见 `Eve` 下另有启用的 8 个 |
| 新控制区的 `FaceDragHandle.face` | 全部仍指向停用的旧 `Head-topo` 控制器，而非 `Eve` 上的控制器 |
| 当前可见 `Eve` | `FaceBlendShapeController.faceRenderer` 指向自身 Renderer，但 Renderer 的 `sharedMesh` 已指向旧 `Head-topo.fbx`，只有原有 16 个形态 |
| Console | 8 条各控制类型“没有找到对应的 BlendShape”错误；源码 `FaceDragHandle.Start` 在检查失败后禁用拖拽组件 |
| 输入接线 | `InputSystemUIInputModule` 与 `PhysicsRaycaster` 启用；`UI/Point`、`UI/Click` 均有鼠标、笔和触屏绑定 |
| 碰撞体射线 | 同步物理变换后，对每个控制区中心直接调用 `PhysicsRaycaster.Raycast`，8/8 命中自身，无需据此重建碰撞体或移动控制区 |

明确的修复点是将 **可见 `Eve` 下的 8 个拖拽区 `face` 引用**改为 `Eve` 的 `FaceBlendShapeController`；旧控制器因所在物体停用，不会正常初始化缓存，且 `FaceDragHandle.Awake` 只在 `face == null` 时查找父级，不会自动纠正一个非空但指错对象的引用。修正后需重新进入 Play 验证，不能只在已被 `Start` 禁用的运行态中拖引用就认定修复完成。

现有 `FaceDragHandle.FaceControl` 仅包含原有 8 种控制，嘴角 Up/Down 不是上下唇独立控制。用户已确认本版只保留上唇 Up、下唇 Down 两个开口形态，这是预期设计，不要求补齐四键或重新导出。接入时可增加两个控制区，各自控制一个形态的 0–100 权重，回到 0 即收回至 Basis；现有拖拽脚本强制要求 Up/Down 两个 Key，需扩展单形态映射，不能直接伪造不存在的反向 Key。

实际读取扩展 FBX 的 `Eve` 网格为 22 个形态，唇部名称为 `Mouth_UpperLip_Up.001` 与 `Mouth_LowerLip_Down.001`；后续应按真实名称接线。当前可见 Renderer 仍使用旧 16 形态网格，需要使用扩展网格才能控制这两键。此前将未导出另外两键判断为资产缺失，是沿用旧四向需求的错误；以用户确认的两键设计为准。本轮只定位问题，没有实施引用修复或新增控制区。

EditMode 中 `RaycasterManager` 注册数量为 0，因此本轮 `EventSystem.RaycastAll` 返回空不是运行时射线失效的证据；直接物理射线检查通过也不等于鼠标 / 实机触控端到端验收通过。

再次实时核对引用：可见 `Eve` 的 `FaceBlendShapeController.faceRenderer` 与 `MuralFaceController.faceRenderer` 均正确指向自身 Renderer，不应将它们改回旧 `Head-topo`；错误仍是新控制区的 `FaceDragHandle.face` 指向旧控制器。

眼球部分：`Eye_L_Pivot` / `Eye_R_Pivot` 均为启用状态，只有 Transform；眼球与角膜在各自 Pivot 下，局部位置为零，层级没有指错。本场景未找到 Animator，眼球及角膜子节点没有拖拽控制组件；项目脚本检索也未找到 `Gaze_*` / `Eye_*_Pivot` 的运行时控制实现。现有四个 `Control_Eye_*` 对应的是眼皮 BlendShape，不是眼球转动。因此眼球目前属于尚未实现输入到父节点旋转的接线，而非已有眼球控制器引用错误；后续应驱动 Pivot 的旋转，保持眼球中心与子节点位置不变。

### 角膜颜色与透明材质调整（2026-09-28）

左右角膜分别绑定 `Assets/_Project/Art/Materials/Laila/M_LailaCornea_L.mat` 和 `M_LailaCornea_R.mat`，从各自 FBX 内嵌材质复制；材质由 Unity 创建并生成 `.meta`，不修改源 FBX 或其他场景。初次仅改白色，后续已完成透明配置：`URP/Lit`、`Surface = Transparent`、Alpha 混合、基础色 RGBA (1,1,1,0)、Smoothness = 0.95、Metallic = 0；保留高光，关闭接收阴影。

透明状态通过已安装 URP 的 `LitShader.ValidateMaterial` 同步：队列 3000、ZWrite = 0、透明关键字启用、ShadowCaster / DepthOnly Pass 关闭。左右角膜 Renderer 也关闭投影与接收阴影，场景已保存。该阶段 Game 相机截图已确认内层黑色瞳孔可见，原蓝色遮挡消失，Console 当时读取到 0 条错误；内层眼球当时尚未绑定颜色贴图。后续贴图及眼周验收以以下记录为准。

### 双眼贴图与眼周明暗对比（2026-09-28，已保存）

| 接线 | 当前结果 |
| --- | --- |
| 颜色贴图 | `Assets/_Project/Art/Textures/Laila/eye_1_dif.png`，原图直接复制，SHA256 与原图一致；1024×1024、sRGB、忽略 Alpha |
| 眼球材质 | `Assets/_Project/Art/Materials/Laila/M_LailaEye.mat`，`URP/Unlit`，白色 `_BaseColor`，`_BaseMap` 指向上述贴图 |
| 左眼 `eye_1` | 材质槽 0 使用 `M_LailaEye`；槽 1 保留原有 `eye_retina` 黑色瞳孔 |
| 右眼 `eye_1.001` | 材质槽 0 共用 `M_LailaEye`；槽 1 保留原有 `eye_retina.001` |
| 角膜 | 继续使用左右独立的透明角膜材质，不用眼球颜色贴图覆盖角膜 |

源 `.blend` 中两眼 UV 逐项一致，右眼是 `eye_1` 的复制体，因此共用原始 UV 图集；文件名中的 `eye_1` / `eye_2` 不能直接解释为左 / 右眼。当前场景缺少 3D 灯光，眼球先使用 Unlit，保证虹膜颜色可见；角膜高光仍由独立透明材质处理，不添加全局灯光。该选择不代表眼球已完成受光或物理折射效果。

通过实际 Main Camera 渲染完成临时 A/B：角膜关闭、全部眼球关闭、脸部墨线关闭、脸部方向性阴影关闭，以及 SSAO 关闭。所有 Renderer、MaterialPropertyBlock、SSAO 开关与相机渲染目标均在 `finally` 恢复；临时状态没有保存。

- 当前 `Assets/Settings/UniversalRenderer.asset` 启用了 SSAO，`AfterOpaque = true`、Intensity = 0.6、Radius = 0.3。眼球加入深度后会改变眼周环境遮蔽，不能将新增黑眼圈全部归因于脸部 Shader。
- 关闭 SSAO 后，眼球开启 / 关闭两张 1920×1080 截图的差异仅限眼睛开口范围（像素包围盒左上 789,422、右下不含 1131,465）；眼周皮肤没有像素差异。开启 SSAO 时差异会扩展至眼周。这支持 SSAO 是新增眼周发黑来源之一。
- 上眼眶的方向性暗部在隐藏眼球后仍存在，临时将脸部 `_ShadowStrength` 设为 0 后显著消失；不应为了消除新增 AO 而直接删除全部脸部明暗。
- 本轮没有永久关闭共享 Renderer 的 SSAO，也没有改动脸部材质、源 FBX、控制区位置或其他场景。眼周 AO 的视觉问题已定位，但尚未永久修复；若要修复，需采用只作用于 laila 的渲染配置，不能直接关闭其他场景共用的效果。

贴图后再次运行全量 EditMode：1028 通过、0 失败、0 跳过，约 36.81 秒；任务 ID `007611c9ac034dbf86f310b1e6cf2fac`。未运行 PlayMode / 拖拽 Showcase，不将全量逻辑回归等同于新增交互验收。测试后 Console 读到 15 条错误级日志：14 条为异常路径测试日志，另 1 条为 Unity 编辑器预览图 `previewArtifactID` 断言，后者不属于预期测试日志；本轮未处理该编辑器问题，也未清空 Console。

截图为临时验收产物，位于 `Temp/LailaFaceChecks/`（`laila-eyes-textured.png`、`ab-*.png`），不作为运行时资产提交。材质、贴图及新建文件夹的 `.meta` 均由 Unity 生成。

### Blender 表达范围扩展（初版记录，非当前 Key 清单）

扩展源文件为 `PRP/Head-topo-expression-extended.blend`，脸部网格在 Blender 中名为 `Eve`。
在原有 16 个形态之外新增以下 6 个 Shape Key，共 22 个形态（另有 `Basis`）：

| Key | 用途 |
| --- | --- |
| `Mouth_Open` | 张嘴，局部顶点最大位移约 8 毫米 |
| `Mouth_Close` | 收拢嘴缝；初版不保证所有组合下唇缘完全贴合 |
| `Mouth_L_Out` / `Mouth_L_In` | 左嘴角外拉 / 内收 |
| `Mouth_R_Out` / `Mouth_R_In` | 右嘴角外拉 / 内收 |

原有 Key 与 Basis 在新增时经逐顶点比较保持不变。上下牙齿保持原位置，没有张嘴联动；
已检查正面张嘴与双侧外拉组合，但不同观察角度和原有表情的组合仍需肉眼验收。

眼球不使用 Shape Key，也不平移。`Eye_L_Pivot` / `Eye_R_Pivot` 分别控制一组眼球与角膜：
选中控制点，在对象自定义属性中调整 `Gaze_X` / `Gaze_Y`（范围 `-1..1`），
分别驱动水平约 ±20°、垂直约 ±15°旋转；零值为原始朝向。
眼球和角膜局部位置为零，控制点位于原始眼球中心，避免父级与子物体位置重复相加。

源文件内的 Text `ValidateLailaExpressions.py` 可在 Blender 文本编辑器中运行；
检查 Key 数量、顶点数量、坐标有限性、张嘴位移上限，以及视线极限的实际角度与中心位置不变。
保存时所有形态权重和视线属性归零。

**此段为 Blender 初版阶段记录；当前 Unity 使用 `Head-topo-expression-extended2.fbx`，不覆盖旧 `Head-topo.fbx`。**
初版运行时只有旧垂直映射；现在新增交互以本文顶部映射表及 2026-09-29 验证记录为准。
Blender 自定义属性驱动不会自动变成 Unity 运行时逻辑；后续接入应控制眼球父节点旋转，
保留现有控制区位置，不因源模型扩展而重建已调好的控制区。

#### 上下唇独立控制调整（2026-09-28，阶段记录）

本轮通过 Blender UI 在当前会话中将 `Mouth_Open` / `Mouth_Close` 改为局部唇部形变，
复用两个 Key 并补两个 Key；预期共 24 个形态（另有 `Basis`）：

| Key | 值为 1 时的最大局部位移 |
| --- | --- |
| `Mouth_UpperLip_Up` | 上唇上移约 6 毫米 |
| `Mouth_UpperLip_Down` | 上唇下移约 3 毫米 |
| `Mouth_LowerLip_Up` | 下唇上移约 3 毫米 |
| `Mouth_LowerLip_Down` | 下唇下移约 6 毫米 |

已在内存中逐顶点确认 Basis、原有 16 个 Key、4 个嘴角横向 Key 与牙齿数据未变，
并分别查看四个唇部 Key 的正面极值。组合张口、其他角度的牙龈遮挡仍未验收。
窗口输入保护连续触发后停止接管；截至中断时，**新版本尚未确认保存到上述 `.blend` 文件**。
此后用户手动调整了形态；以下最新资产记录为准，不应重新执行本段的生成逻辑。
内嵌 `ValidateLailaExpressions.py` 的旧 22 形态断言与 `Mouth_Open` / `Mouth_Close` 引用
已不适用于当前资产，不能将其作为当前版本已通过验收的依据。

眼球属性的 UI 路径为：选中 `Eye_L_Pivot` 或 `Eye_R_Pivot` → 对象属性 → 底部展开
“自定义属性” → 调整 `Gaze_X` / `Gaze_Y`；不是选中 `eye_cornea` 子物体。
本轮已验证左眼 `Gaze_X = 0.5` 时 Z 旋转为 10°，随后恢复视线零值。
Unity 场景、FBX 和已调好的控制区本轮均未修改。

#### 当前资产：手工版本与右嘴角镜像（2026-09-28，已保存）

用户手动调整后，以当前 `Mouth_L_Out` / `Mouth_L_In` 为准，新增对应的
`Mouth_R_Out` / `Mouth_R_In`。镜像的是相对 Basis 的顶点位移：X 方向取反，Y/Z 不变。
Basis 的左右匹配为一一对应，位置误差为零；右侧 Out 影响 10 个顶点，In 影响 23 个顶点。
新增前的全部 25 个 Key（含 Basis）经逐顶点比较保持不变，不重新制作用户的唇部形变。

当前文件共有 27 个 Key（含 Basis），其中保留用户已有的 `Mouth_UpperLip_Up.001`
和 `Mouth_LowerLip_Down.001`，未自动合并或删除。右侧两个 Key 已分别查看正面极值，
预览值归零后通过 Blender 保存至 `PRP/Head-topo-expression-extended.blend`。
新 Text `ValidateLailaMouthMirror.py` 已运行通过，可再次运行检查左右嘴角位移的精确镜像关系；
它不代替上下唇组合、牙龈遮挡或 Unity 导入验收。本次没有修改 Unity 场景或 FBX。

- 当前文档状态为 `seed`：原型代码与 laila 接线已落地；已有数值和运行态事件链验证，实际设备输入与视觉组合验收仍需确认。
- `MuralFace` 当前按“聊斋古画”处理：方向性分层阴影、冷墨轮廓、冷月边缘光、旧纸颗粒与极弱朱砂洗染；使用程序化颗粒，不提供墙面纹理输入。需要真实墙面纹理时再增加 `_GrainMap`，不要把纹理硬编码进 Shader。
- 当前 12 个启用控制区支持嘴角水平 / 垂直、双唇单键和独立双眼旋转；不包含镜像联动、形变跟随或表情预设。
