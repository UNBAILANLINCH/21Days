---
type: module-guide
module: lailaface
layer: runtime
maturity: seed
---

# LailaFace 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Gameplay/` 中与 Head-topo 面部控制相关的代码前读这份。
> 这是复刻莱拉脸部玩法的原型模块，唯一场景为 `Assets/_Project/Scenes/LailaRecognitionPlaytest.unity`。
当前状态（2026-10-07）：启用 `Head-topo-expression-extended-brow-regions-refined3 1.fbx`，31 形态、17 区。
唯一当前执行入口是 [训练 PRP §17](../../../../PRP/laila-expression-recognition/spec.md#17-当前工程候选接手入口2026-10-04)→§16；资产逐项去留见 [文件清单](../../../../PRP/laila-expression-recognition/asset-disposition.md)。本指南的旧12区、22形态和早期源模型小节均为历史阶段，不作为当前接线命令。

## 职责边界

| 做 | 不做 |
| --- | --- |
| 缓存当前脸部网格的 31 个 BlendShape 名称与索引 | 不修改 Blender 源文件或重新生成 BlendShape |
| 17 个控制区：6 段眉毛、4 眼皮、2 嘴角、3 唇区、2 视线 | 不直接读取 `Input.mousePosition` 或 `Input.touches` |
| 给 Head-topo 提供局部壁画感材质 | 不添加全局 URP Renderer Feature，不影响其他场景 |
| 在 laila 场景中提供可回放的数值验证 | 不接入正式游戏流程、存档或跨场景状态 |

## 类与资产分工

| 类型 | 路径 | 职责 |
| --- | --- | --- |
| `FaceBlendShapeController` | `Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs` | 缓存 Renderer，提供单个权重、成对权重与 Reset API |
| `FaceDragHandle` | `Assets/_Project/Scripts/Runtime/Gameplay/FaceDragHandle.cs` | 接收 EventSystem 指针拖拽，写成对 / 单键权重，或旋转眼球 Pivot |
| `FacePointerFeedback` | `Assets/_Project/Scripts/Runtime/Gameplay/FacePointerFeedback.cs` | 真实拾取与遮挡检查、抓点提示、三态右手光标及取消捕获 |
| `LailaBlendShapeOrder` | `Assets/_Project/Scripts/Editor/Tools/LailaBlendShapeOrder.cs` | 当前正式 FBX 导入时按部位排序，保留名称及帧数据 |
| `MuralFace` Shader | `Assets/_Project/Art/Shaders/MuralFace.shader` | 只负责 Head-topo 的分层明暗、边缘光和轻微颗粒 |
| `MuralFaceController` | `Assets/_Project/Scripts/Runtime/Gameplay/MuralFaceController.cs` | 在 Head-topo Inspector 调整阴影方向、阴影强度、边缘光和纸张颗粒 |
| `LailaFaceShowcase` | `Assets/_Project/Scripts/Tests/Showcase/LailaFace/LailaFaceShowcase.cs` | 回放 16 个形变、成对映射与重置 |

当前没有 ScriptableObject：BlendShape 名称是模型的固定资产接口；成对范围为 `-1..1`，唇部单键范围为 `0..100`，眼球角度在控制区 Inspector 配置。

## 控制映射

以下为旧 12 区兼容映射；当前分段模型以“分段眉毛模型接线”及训练 PRP §16 为准，不将整眉键或 `.001` 唇键用于当前模型。

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

进入 Play 后在 Game 视图拖拽，不是用 Scene 移动工具。`FaceBlendShapeController.ResetFace` 重置当前网格全部形态，但不重置视线；眼球控制区组件的 `Reset Gaze` 上下文菜单可在运行态恢复初始朝向。旧枚举序号 0–7 未变，水平拖拽默认关闭，旧场景保留原行为。

## 依赖方向

```text
Game.LailaFace（Runtime / 表现）
  └─ UnityEngine + UnityEngine.EventSystems

Game.Tests.Showcase.LailaFace
  └─ Game.Runtime + Game.Tests.Showcase Framework
```

控制器与拖拽区只写表现状态，不参与逻辑模拟；拖拽事件由 Unity EventSystem 派发，运行时没有平台分支。

## 当前场景接线

`LailaRecognitionPlaytest.unity` 已保存当前 31 形态模型、17 个控制区、识别结果与四版模型对照。菜单只打开试玩并在退出后恢复此前场景，不自动生成、升级或保存资产。旧 8 区搭建菜单、识别 UI 安装器及复制 / 升级代码已移除；后续修改走现有场景与组件。

主相机需要通过 `UniversalAdditionalCameraData.SetRenderer(1)` 选择当前项目的 3D `UniversalRenderer`，并具有 `PhysicsRaycaster`，场景需要 `EventSystem`；不要使用不存在的公开 `rendererIndex` 属性。每个 `FaceDragHandle` 的 `Face` 指向同一个 `FaceBlendShapeController`。调试阶段保留控制区 Renderer，确认位置后再关闭 Renderer，只保留 Collider。

Head-topo 的材质使用 `MuralFace` Shader；材质资产位于 `Assets/_Project/Art/Materials/Laila/M_LailaFace_Mural.mat`，同一个材质可复用到 FBX 的多个子网格槽位。`MuralFaceController` 用 `MaterialPropertyBlock` 覆盖阴影参数，不复制材质实例。当前只给该模型设置材质，不修改其他场景的材质或 URP Renderer 资产。

## 验证

| 类型 | 路径 | 覆盖 |
| --- | --- | --- |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/LailaFace/LailaFaceShowcase.cs` | 当前 31 形态采样、17 控制区、抓点反馈、嘴角 / 眼皮、Reset 与四版模型推理 |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/LailaFace/FaceDragHandleTests.cs` | 双唇单键钳位与收回、嘴角双轴、旧行为、指针隔离、左右眼固定中心旋转与复位 |
| 场景 | `Assets/_Project/Scenes/LailaRecognitionPlaytest.unity` | 当前 31 形态脸部与 17 个启用控制区 |

Showcase 直接加载唯一的 `LailaRecognitionPlaytest.unity`，通过识别器的 Face 引用验证活动模型，不再检查停用的 16 形态备份。

2026-10-07 场景收敛后，定向 Showcase 1/1 通过、失败 / 跳过 0，报告 `Logs/verify/lailaface/20261007-175309/report.md` PASS；覆盖31形态严格采样、17控制区、抓点反馈、嘴角 / 眼皮、重置及四版模型实际推理。此结果不等于物理鼠标或人工语义验收。

当前 EditMode 用例不依赖具体场景或 FBX。Showcase 的数值与模型切换检查不能代替实际鼠标交互验收；拖拽手感、牙龈遮挡与材质外观仍需开发者肉眼确认。

## 材质与几何约束

- 牙龈与牙齿复用 `MuralFace`，分别为 `M_LailaGingiva_Aged.mat`、`M_LailaTeeth_Aged.mat`；不增加全局灯光。
- 双眼使用 `M_LailaEye.mat` 与 `eye_1_dif.png`，保留各自黑色瞳孔槽；透明角膜使用左右独立材质。
- 当前脸部材质使用手绘 `T_LailaFace_BrowsLips_HandPainted.png`；材质属性经 `MaterialPropertyBlock` 覆盖。
- 眼周黑暗曾在相机 A/B 中确认包含 SSAO 的影响；局部视觉问题不应通过关闭共享 Renderer 效果处理。
- Blender Key 数量不能代替 Unity 实际导入的 BlendShape 数量；缓存需经 Awake / RebuildCache 初始化。
- 修改源形态前检查非空形变，再检查镜像、Basis 与其他 Key 未变；只检查镜像等式会让两侧空形态误报成功。
- 旧 16 / 22 形态及未确认保存的 Blender 操作不作为当前接线依据。源资产去留与几何验收见顶部文件清单及下方嘴部修复报告。
- 原型状态为 `seed`：自动数值和事件链检查已做，实际设备输入、牙龈遮挡和视觉组合仍需验收。

### 分段眉毛模型接线（2026-10-02，取代上面的旧资产记录）

当前启用模型为 `Head-topo-expression-extended-brow-regions-refined3 1.fbx`，
脸部 `Eve` 实际导入 31 个 BlendShape；以 Unity 实测名称为准，不按 Blender Key 数量推算。
`FaceBlendShapeController` 与 `MuralFaceController` 的 Renderer 已指向当前 `Eve`，
全部控制区的 Face 引用及两个眼球 Pivot 引用已恢复。旧模型留作停用备份。

`FaceDragHandle` 增加可选 `upShapeOverride` / `downShapeOverride`，留空沿用旧模型映射。
两侧原整眉控制区改为 Mid，新增 Inner / Outer；上唇改为左右两区，
分别映射 `Mouth_UpperLipL_Up` / `Mouth_UpperLipR_Up`，下唇映射 `Mouth_LowerLip_Down`。
合计 17 区：嘴角 2、眉毛 6、眼皮 4、上唇 2、下唇 1、视线 2。
左右上唇现为互补平滑分区；几何验收与内部口腔交叠限制见下方嘴部修复报告。

按用户要求，已有控制布局整体向世界 Z 正方向平移 5.56，未按形变重新逐点定位；
已有位置从操作前四位小数快照恢复，坐标精度约 0.0001 世界单位。
原拾取中心、半径和尺寸恢复；新增控制点单独布置。
Scene 开启 Gizmos 后可见青色控制点与拾取范围，Game 不绘制标记。

定向 EditMode 9/9 通过（任务 `61b80c9085eb415587a6f1ff67de00fb`）；
运行态 17/17 控制区中心射线首命中自身，形态模拟拖拽达到 50 权重、成对反向权重为零，
视线引用有效并可复位。检查后恢复形态权重并退出 Play，Console 零错误。
这是事件链检查，不等同于真实鼠标或移动端验收。

手绘图片已另存 `Assets/_Project/Art/Textures/Laila/T_LailaFace_BrowsLips_HandPainted.png`，
脸部材质使用此图；当前 FBX 的眼球、角膜、牙龈和牙齿映射已恢复。
双眼与角膜的最终世界缩放匹配旧模型，局部位置与旋转保持原样。

#### 后续交付状态（2026-10-03）

当前正式 FBX 已应用嘴部修复；最终可编辑源文件为
`PRP/laila-mouth-repair-20261002/mouth-repaired.blend`，制作脚本为
`PRP/laila-expression-recognition/repair_mouth_shape_keys.py`。
此版本左右上唇改为互补平滑分区，取代上文旧版跨中线叠加的限制记录。
具体几何验收范围与内部口腔交叠限制见同目录 `acceptance-report.md`。

控制点后续已支持网格表面跟随及眼球刚体跟随；运行态提供实际射线驱动的抓点提示、
三态右手光标与取消捕获。模型导入后按嘴、眼、眉排列 BlendShape，保留名称与每帧几何。
当前停用备份是最早的 `Head-topo.fbx`（16 形态），仍被正式场景引用，应保留。
无正式引用的 `Head-topo-expression-extended2.fbx` 和 `Head-topo-expression-extended-brow-regions-refined3.fbx` 已移除；
PRP 历史场景快照仍引用这些旧版本，不是正式运行依赖，不能保证删除后的快照完整恢复。
2026-10-03 定向 EditMode：10/10，任务 `c1ba2de7a77549778f71cd296c56fd2e`；六个 C# lint 通过，Console 零错误。
埋点扫描为原有失败日志与表现查询，不加高频埋点；`gc_scan` 仍有三个既有命名空间问题，Unity 序列化 diff 有尾空格。

### 表情识别实验接线（2026-10-03）

当前唯一试玩场景有局部 `LailaExpressionRecognition` 和右上角结果面板，使用真实Sentis2.1.3 CPU候选。严格17轴只读采样，四视线轴排除；输入变化即时清除旧结果，自动识别最多10次/秒，静止不重复；模型/输入错误与拒识分开显示，分数不是人工准确率，没有调参面板。人工标注采集仍为独立手动流程；采样与分类职责见识别模块指南。原专业设计与旧上限分析保留；正式语义/人工验收待完成。详见[识别模块指南](../lailafacerecognition/lailafacerecognition-module-guide.md)和训练PRP§17。

### 编辑态预览生命周期（2026-10-03）

编辑器更新委托可能在场景卸载后的当前调用列表中仍持有组件。`RefreshEditorPoints`必须先检查Unity的`this == null`再访问属性；`OnDestroy`退订`EditorApplication.update`作为销毁清理，保留原`OnDisable`退订。此路径只更新表现抓点，不新增高频业务日志。

本轮`FaceDragHandleTests`12/12，任务`a5730221344f48b78cd2b9b3b6ed77b8`，包括捕获回调后销毁和单独销毁退订两项；前文10项是此前阶段，不是本轮新增12项。检查后RhythmDemo、非Play、未编译、dirty=false、预览live/dead委托均0，Console读取错误0；不代表任意场景退出永不出现原生Editor异常。

左上眼睑单键高权重的局部眼角自交尚未修复，几何验收不能写通过；定位、权重和数值证据见当前执行入口spec§17。没有为消除指标而修改模型、幅度、材质或光照。
