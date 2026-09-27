---
type: extension-guide
module: lailaface
layer: runtime
maturity: seed
---

# LailaFace 扩展指南

## 增加一个新的面部控制

1. 在 `FaceDragHandle.FaceControl` 增加枚举值。
2. 在 `ResolveShapeNames()` 中补充唯一的 Up / Down 名称映射。
3. 确认 FBX 的 `Import BlendShapes` 已开启，并且名称与映射完全一致。
4. 在 `laila.unity` 增加一个 Collider 控制区，绑定同一个 `FaceBlendShapeController`。
5. 在 `LailaFaceShowcase` 增加一条数值检查。
6. 同步 `lailaface-module-guide.md` 的控制映射表。

不要把 Blender 的 Shape Key 创建逻辑放进 Unity Runtime，也不要为了一个固定名称新增 ScriptableObject 配置。

## 增加材质表现

优先扩展 `Assets/_Project/Art/Shaders/MuralFace.shader` 的材质属性，并只修改 Head-topo 使用的材质资产。不要把壁画效果做成全局 Renderer Feature，除非需求明确要求其他场景也受影响。

Shader 只负责视觉结果，不读取玩法状态、不写 BlendShape、不改变场景对象层级。增加属性时同步材质接线说明与本模块 guide 的已知限制。

## 不应从哪里扩展

- 不在 `FaceDragHandle` 中复制 Renderer 缓存或 BlendShape 名称查找。
- 不在每帧 `Find`、`GetComponent` 或输出日志。
- 不在本模块引入平台条件编译、原始鼠标 API 或触摸 API。
- 不修改 Boot、Sample、CharacterPuppet 或其他场景来完成 laila 原型。
