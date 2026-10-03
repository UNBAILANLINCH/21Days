# 双唇峰独立抓点与正式游玩无文字

归档说明（2026-10-03）：本报告描述历史实现。表面跟随后移至 FaceDragHandle，反馈不再覆写坐标/半径；当前实现见模块说明和 controller-config 报告。文末四张本地截图及附加截图未在交付目录找到，Library ID 不等同于本次提交的图片附件。

2026-10-02，仅 21Days / laila，本轮不保存场景、不提交推送。

## 实际原因与修改

原来已有两个 FaceDragHandle：Control_UpperLip_L → Mouth_UpperLipL_Up、Control_UpperLip_R → Mouth_UpperLipR_Up，共用一个 FaceBlendShapeController 管理器。左右绑定正确，不是缺两个控制器。

原左点的世界中心为 (0,-0.0468,-0.1282)，在中线，实际拾取半径约 9.022mm；右点为 (0.012,-0.0522277,-0.1646433)，半径 4.5mm。两点位置、深度、范围不对称。反馈又只给当前 hover 点显示 UpperLip_L / GRAB 等文字，造成“只有一个上左”的观感。

只修改 `Assets/_Project/Scripts/Runtime/Gameplay/FacePointerFeedback.cs`：

- 用实际表面两侧唇峰定位原有两个抓点，两个独立拾取球统一为 3.5mm 世界半径，不复制控制系统。
- 在 Play 中使拾取球跟随真实 BakeMesh 顶点和表面法线；不依赖易随导入变化的固定顶点索引。原编辑场景的节点配置不覆盖。
- 基础姿态下拾取中心约为 (-0.0117526,-0.0398818,-0.1330089) 与 (0.0117526,-0.0398818,-0.1330089)，1920×1080 的屏幕坐标为 (928.268,432.319) 与 (991.732,432.319)，两侧分开且不重叠。
- 所有点位文字默认关闭，关闭时 Text.enabled=false 且内容为空；保留 hover 圈、抓取高亮和骨手。FacePointerFeedback Inspector 的 Show Debug Labels 可在编辑器显式开启；非 UNITY_EDITOR 的发布编译路径始终返回关闭。

没有改 Blender/FBX 几何、31 key、绑定、法线导入、柔化参数、GUID。FBX SHA256 仍为 `86C77AF278764973B27C7C6F7F0B86C2D5434C878CB0B63A6402CC0D74C8D136`。

## 形变与操作验证

- 左右各自九宫格（中心及 ±4px）前方命中 9/9。
- 真实 PointerDown/Drag/PointerUp，左右 0/50/100 共九种组合全部符合各自权重，其他 29 key 不变。仅左拖：L≈100、R=0；仅右拖：L=0、R≈100；双峰：L/R≈100。浮点拖动值 99.9999847，截图姿态用超出满程的真实拖动钳位到 100，未暗缩幅度。
- 正式无文字、显式 Editor Debug 显示、关闭 Debug 后无文字均通过，Debug 开关不改变命中或权重。
- 摄像机左右旋转 20°，两点分别正确命中；背面 180° 两点都不穿透面部拾取、无提示。
- 现有上唇 key 已有独立主侧形变，局部衔接保留：左 key 在左唇峰抬起 6.3711mm、右唇峰过渡移动 1.4461mm；右 key 完全对称。对侧权重仍为 0，约 22.7% 的几何衔接不是误写对侧 key；没有把唇部切成不连续的两块。记录见 `geometry-regions.json`。
- 左单独、右单独及双峰组合均跨工具调用等待真实皮肤帧更新后，用真实 Main Camera、原材质和光影 GPU 拍摄；未使用静态代理替代这些最终图。
- 原有 17/17 抓点、拖动保持、取消、空白清除、UI 遮挡、面部遮挡、禁用恢复回归通过。场景切换和失焦恢复通过。原记录位于 `../laila-interaction-20261002/feedback-tests.json` 和 `lifecycle-tests.json`，本轮已重新执行。
- 相关 10 项 EditMode 回归任务 `b5f3ab28ea2646949c7cfb8f70d42394` 状态 succeeded，completed=10，失败列表为空。工具本次未返回详细汇总 result，不编造时长。
- 本轮 C# lint 通过；Unity Console error/warning 0。

## 用户状态与限制

最终 Play=false、dirty=true。70 个节点任意变换、碰撞中心/半径、活动状态及全部权重，与拍摄前最新完整快照一致（`edit-state-before.json` / `edit-state-after.json`）。早先瞬时读数 Mouth_L_Out=15.5，但完整快照时已为 0，按最新快照保留 0，不将旧预览值覆盖回去。测试后仅恢复原 dirty 标志，没有保存场景；运行时对象随 Play 退出清除。

用户本轮 Library 参考图 `libfile_4bf56517790881919014388ec61ce708` 只读返回 asset pointer 和文字，download_file 未授权解析成功，未取得像素；没有拿文字假装看图或绕过授权。实际定位和截图依据当前 Unity 画面。

验收由用户决定；未做实体鼠标连续操作或发布包设备测试，发布无文字路径有编译条件保证但未构建发布包。唇峰锚点针对当前 31-key 原型模型。原有内部牙齿/口腔限制不在本轮扩大修复。

## 已上传 Library 图像

| 图像 | library_file_id |
|---|---|
| 修正前中线左点与文字 | libfile_a0a4a506c7d481919d922fb7d0d51042 |
| 左峰100、右0，无文字 | libfile_8663e419146c819197d342da907207f9 |
| 右峰100、左0，无文字 | libfile_c5f0c6188b3c8191a7611da0b4904178 |
| 双峰100，无文字 | libfile_1cef14b9f6cc8191b9dcf031a8920ee7 |

对应本地文件：`before-L.png`、`left-only-L.png`、`right-only-R.png`、`both-L.png`；各侧附加截图也保留在本目录。
