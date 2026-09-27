---
type: external-api
module: lailaface
layer: runtime
maturity: seed
---

# LailaFace 对外接口

当前只允许 laila 测试场景与 Showcase 使用这些接口，正式玩法尚未接入。

## `FaceBlendShapeController`

路径：`Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs`

| API | 说明 |
| --- | --- |
| `bool HasShape(string shapeName)` | 判断 Head-topo 是否存在指定名称 |
| `bool TrySetWeight(string shapeName, float value)` | 写入 `0..100` 权重，缺少名称时返回 `false` |
| `void SetWeight(string shapeName, float value)` | 写入并在名称缺失时记录错误 |
| `float GetWeight(string shapeName)` | 读取权重；名称缺失时返回 `0` |
| `bool TrySetSignedPair(string upShape, string downShape, float value)` | 将 `-1..1` 映射为 Down / Up 权重 |
| `void SetSignedPair(string upShape, string downShape, float value)` | 成对写入并在名称缺失时记录错误 |
| `float GetSignedPair(string upShape, string downShape)` | 读取 Up 权重减 Down 权重 |
| `void ResetFace()` | 将 Renderer 的全部 BlendShape 权重归零 |

使用前置条件：组件已挂在 Head-topo 或其父节点，`faceRenderer` 已指向包含 16 个 BlendShape 的 `SkinnedMeshRenderer`，并且组件完成 `Awake` 缓存。

## `FaceDragHandle`

路径：`Assets/_Project/Scripts/Runtime/Gameplay/FaceDragHandle.cs`

`FaceControl` 枚举提供 8 个控制区类型。组件通过 `PointerEventData` 接收鼠标和触摸事件，使用同一套垂直拖拽规则，不需要调用方读取设备状态。

使用前置条件：控制物体有 Collider；主相机有 `PhysicsRaycaster`；场景存在 `EventSystem`；`face` 指向共享的 `FaceBlendShapeController`。

不要在其他模块里直接调用 `SkinnedMeshRenderer.SetBlendShapeWeight` 绕过控制器，否则会让 Reset 和成对权重约定失效。
