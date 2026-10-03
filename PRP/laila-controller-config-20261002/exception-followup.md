# Console 异常收尾核实

本记录修正先前“已有编辑器异常”的表述：仅凭当时一条 Console 消息不足以确认既有或无关。

## 原 Inspector 异常

读取 Unity Editor.log 中两处匹配（行 17952、355601），完整堆栈相同：

```
SerializedObjectNotCreatableException: Object at index 0 is null
UnityEditor.Editor.CreateSerializedObject
UnityEditor.Editor.GetSerializedObjectInternal
UnityEditor.Editor.get_serializedObject
UnityEditor.RendererEditorBase.OnEnable
UnityEditor.SkinnedMeshRendererEditor.OnEnable
```

匹配前为 `ModeService.Initialize / LoadModes`，之后为 PhysX 初始化与 `Mono: successfully reloaded assembly`。堆栈没有项目脚本或独立窗口；说明发生于原生 Renderer Inspector 重载，但不能据此证明本轮删除窗口、重新导入或选择对象完全无关。日志该段没有时间戳，不能补造发生时间。本轮检查日期为 2026-10-02。

有界检查：Play 和退出 Play 后各切换 Eve、原生 SkinnedMeshRenderer、Control_UpperLip_L，创建并释放它们的实际 Editor，读取 serializedObject；原生 `UnityEditor.SkinnedMeshRendererEditor` 的 `m_BlendShapeWeights` 数量均为 31。未复现该异常。证据 `inspector-bounded.json`。未重复删除已删除窗口，未为了复现重建独立面板、重新导入资产或保存场景。

结论：该 Inspector 异常目前未复现、精确原因未确定，不称作“已证实既有问题”。原生 Inspector 截图通道此前返回灰图，未反复重试或提交灰图。

## 新发现并修复的本轮回归

扩大 Console 读取发现 `UnassignedReferenceException: The variable surfaceMesh of FaceBlendShapeController has not been assigned`，完整项目堆栈为：

```
UnityEngine.SkinnedMeshRenderer.BakeMesh
Game.LailaFace.FaceBlendShapeController.TryGetSurfacePoint (FaceBlendShapeController.cs:135)
Game.LailaFace.FaceDragHandle.RefreshGrabPoint (FaceDragHandle.cs:159)
Game.LailaFace.FaceDragHandle.LateUpdate (FaceDragHandle.cs:153)
```

原因：新加入的表面缓存使用 C# `??=`，无法识别 Unity 原生 Mesh 已销毁、托管引用仍在的情况。修改为 `surfaceMesh == null`，重建时同时重置帧缓存。这是本轮引入的代码问题，不归为既有异常。

实测 `surface-recovery.json`：运行态主动销毁缓存，再由现有控制器 RefreshGrabPoint 重建；新 Mesh instance 不同、1650 顶点，跟随开启、offset 仍为 0.0035。配置三项 `authority-tests.json` 再次全部通过，手动中心/半径不被反馈覆写，偏移实际移动 0.00200001872。C# lint 成功。

修复后 Play 与退出 Play、原生 Inspector 检查后的 Console `error` 查询均为 0 条；没有调用 clear 删除日志。源错误仍保留在 Editor.log。退出 Play 后未保存或重导入任何场景/模型，保留用户现有改动。修改仅为 FaceBlendShapeController 的 Unity 判空与缓存重置，证据和本报告另存 PRP。
