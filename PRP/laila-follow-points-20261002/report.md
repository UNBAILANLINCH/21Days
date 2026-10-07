# Laila 嘴唇与眉毛抓取点跟随复测 — 2026-10-02

归档说明（2026-10-03）：这是阶段验证记录，当前状态以模块说明为准。文中 compare-Control_*、before-/after-Control_* 截图未在本地交付目录找到；本次归档实际存在的 JSON，不包含场景快照或 Library 上传记录。

本轮在用户后来明确授权的独立 21Days 实施会话完成。Unity 实例为 21Days@6860e04e，版本 2022.3.62f2。三个嘴唇点及六个眉毛点已保存到原 laila 场景；编辑模式的原生 Renderer 序列化权重改变和播放模式的实际 EventSystem 拖动均能跟随。没有提交或推送。

## 修改与原因

- `Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs`：已有控制器缓存中性烘焙顶点并用实际变形差更新位置；编辑模式在已有控制器的 EditorApplication.update 中刷新子抓取点；OnEnable 重建缓存，避免 ExecuteAlways 组件进入播放时缓存为空而禁用抓取点。
- `Assets/_Project/Scripts/Runtime/Gameplay/FaceDragHandle.cs`：已有组件添加可序列化的稳定顶点索引。跟随移动真实 Transform，Collider 世界中心、高亮环和 Gizmo 共用这个点；保持原碰撞半径和按下时固定的拖动基线。
- `Assets/_Project/Scenes/laila.unity`：开启原来未开启的下唇及六个眉毛跟随；九点对齐各自表面顶点，沿当前相机深度向前偏置 3.5 mm 写入既有锚点数据。Collider.center 为零，真实 Transform 与 Collider 中心一致。左右唇峰独立、原键名/绑定/碰撞半径/缩放保持。

根因包括六眉和下唇原 followSurface=false、只改 Collider.center 导致 Transform 不动、编辑模式没有实际刷新、在变形网格上重新寻找近点会跳点。反向复测还发现右眉中点被上眼睑控制区抢命中，最终表面对齐后已修复。

场景备份为 `laila-before.unity`，最终审阅副本为 `laila-after.unity`；保存前比较完整副本一致。原有其他工作树变更保留。FacePointerFeedback 本轮没有添加任何嘴唇/眉毛坐标或键名特判；没有新增控制窗口。

## 实际 Unity 验证

`edit-tests.json`：通过原生 SerializedObject 修改 Renderer 权重，随后独立工具调用观察自动更新，测试阶段手动 Refresh 次数为零。九点均随 100 权重移动且 Transform 与 Collider 一致。

`drag-stage.json`：九点分别通过真实事件命中及 PointerDown/Drag/Up 到 100，移动抓取点后重复完全相同的指针位置，权重没有反馈漂移；释放成功。

`regrab-tests.json`：九点在新的实际位置命中、捕获并从 100 再拖到 50，9/9 通过。

`down-individual-tests.json`：六眉逐个 Down100，Up0，跟随误差不超过 3.01e-8 世界单位，六点命中自身；6/6 通过。

`down-combined-tests.json`：六眉同时 Down100/Up0，等待实际 LateUpdate 后再核对烘焙网格与抓取点，误差不超过 3.34e-8 世界单位，六点命中自身；6/6 通过。旧 `down-tests.json` 是修复前失败记录，保留追溯，不能作为最终通过记录。

`../laila-lip-peaks-20261002/lip-tests.json` 本轮重新执行：左右上唇全部 0/50/100 九种组合均通过且其他形态键不被修改；两个唇峰周围各九个屏幕采样点命中 18/18；相机 ±20° 命中、背面不显示；默认无文字、显式编辑调试文字及关闭恢复均通过。

既有 LailaFace EditMode 回归 10/10 通过，job `b174659358be4f6e881164aed04f3fa2`，耗时 0.9498423 秒。两份修改 C# 手动项目 lint 和局部 git diff --check 通过。最终实际 Console 错误日志 0 条。全工作树 diff --check 仍有 Unity 原生序列化空字段尾随空格；没有为格式清理重写其他场景内容。文档后的 gc_scan 已执行，仍报告这两个控制器和 MuralFaceController 既有 Game.LailaFace 命名空间与 Gameplay 目录不一致三项；没有扩展本任务重命名模块。

## 原相机截图

九份 `compare-Control_*.png` 为实际场景主相机截图像素直接拼接，左边为九键全部 0，右边为九键全部 100 的组合；不是修复前与修复后的几何对比。每张对应鼠标悬停的一个真实抓取点。未改灯光、材质、相机以掩盖问题。原始 `before-/after-Control_*.png` 和 JSON 一并保留。已逐张检查最终图像。

| 控制点 | 主键 | 0→100 垂直位移 mm | 屏幕位移 px |
|---|---|---:|---:|
| UpperLip_L | Mouth_UpperLipL_Up | +7.8172 | +21.1064 |
| UpperLip_R | Mouth_UpperLipR_Up | +7.8172 | +21.1064 |
| LowerLip | Mouth_LowerLip_Down | -7.6919 | -20.7680 |
| Brow_L_Inner | Brow_L_Inner_Up | +3.1991 | +8.6375 |
| Brow_L_Mid | Brow_L_Mid_Up | +11.8074 | +31.8799 |
| Brow_L_Outer | Brow_L_Outer_Up | +12.6410 | +34.1307 |
| Brow_R_Inner | Brow_R_Inner_Up | +5.9716 | +16.1234 |
| Brow_R_Mid | Brow_R_Mid_Up | +12.6961 | +34.2795 |
| Brow_R_Outer | Brow_R_Outer_Up | +15.7263 | +42.4609 |

九点在前后两档均命中自身、高亮可见、高亮误差 0 px、Transform/Collider 中心误差 0。

## 验收边界与剩余问题

此轮通过的是抓取点跟随与输入规则，不代表整个嘴部几何已达到原始高质量模型验收要求。实际 100 组合截图仍明显露出形态不自然的牙齿/口腔，完整嘴部几何验收不能判通过。左右眉头的既有 Down 键在所附着表面顶点实际分别向上 +2.7491/+2.2839 mm，跟随正确但几何方向仍不自然；保留真实 100 幅度，没有暗缩幅度假通过。后续模型修复需另行验证这些几何问题。

验证使用 Unity EventSystem 实际命中与处理函数，不是操作系统物理鼠标录制；Inspector 测试通过原生序列化属性路径修改权重，没有声称亲手点击 Inspector。最后已退出播放，场景 dirty=false，31 个 BlendShape 全零，原摄像机位置/旋转恢复。

模型 FBX 本轮未改变，GUID `e56ea6a13114e354b8bc13f4c0f086dd`，SHA256 `86C77AF278764973B27C7C6F7F0B86C2D5434C878CB0B63A6402CC0D74C8D136`，31 键名称/数量保持，原生列表仍按 Mouth/Eye/Brow 部位排列，反馈仍是原创右手像素图标。

新参考图 `libfile_134c5920b70c8191986c1cbd9e62159f` 的官方 materialization helper 因 Windows 缺少 os.setxattr 失败，本轮没有声称已看到该图实际像素，没有绕过该流程读取其他项目/全局记忆。

自动审批拒绝了删除测试产生的七个零值 Prefab BlendShape 覆盖，理由是根据副本推断来源可能删除用户修改、授权不足。已保留索引 4/9/10/23/25/27/29 的零值覆盖，没有绕过审批；行为与模型默认零值一致。若需要移除，须另行获得对这七项具体清理的批准。
