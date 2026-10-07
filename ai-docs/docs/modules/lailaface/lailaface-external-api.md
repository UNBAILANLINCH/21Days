---
type: external-api
module: lailaface
layer: runtime
maturity: seed
---

# LailaFace 对外接口

当前用于 laila、独立研究试玩及 Showcase，正式玩法尚未接入。

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

使用前置条件：`faceRenderer` 显式指向当前启用脸部的 `SkinnedMeshRenderer`；当前laila为31形态，启用/Awake建立缓存。识别采样先HasShape检查，不把GetWeight缺键返回的0当作中性。

## `FaceDragHandle`

路径：`Assets/_Project/Scripts/Runtime/Gameplay/FaceDragHandle.cs`

`FaceControl` 保留旧类型序号，并支持分段眉、单键唇部与视线；可选形态名称覆盖用于当前 31 形态模型。组件通过 `PointerEventData` 接收指针事件，嘴角可双轴拖拽，视线旋转 Pivot，不要求调用方读取设备状态。

使用前置条件：控制物体有 Collider；主相机有 `PhysicsRaycaster`；场景存在 `EventSystem`；`face` 指向共享的 `FaceBlendShapeController`。

不要在其他模块里直接调用 `SkinnedMeshRenderer.SetBlendShapeWeight` 绕过控制器，否则会让 Reset 和成对权重约定失效。
