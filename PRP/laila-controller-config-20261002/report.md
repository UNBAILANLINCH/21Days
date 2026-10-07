# 原生 Key 排列、控制器配置与右手反馈修正

归档说明（2026-10-03）：这是阶段验证记录，当前状态以模块说明为准。文中引用的 both-L.png、both-R.png 未在本地交付目录找到；场景快照不纳入本次提交，不能据此声称这些文件已归档。

本轮在独立 21Days 会话完成控制器配置迁移，并将骨节手修改为实心像素右手。参考用户手势，但轮廓、比例和握持状态重新绘制。掌面朝观察者、拇指在左侧；具有指向、可抓取、握持三态。实际 Unity 相机截图为 `../laila-lip-peaks-20261002/both-L.png`、`both-R.png`，精灵为 `../laila-interaction-20261002/right-hand-0.png` 至 `right-hand-2.png`。

## 调整入口与实现

- 选中 `Eve`，在原生 **SkinnedMeshRenderer > BlendShapes** 调整形态。列表连续排列为嘴部 11、眼部 8、眉部 12；左右和子区域相邻。没有新增独立面板，`LailaFaceControlWindow.cs` 及 meta 已删除。
- 选中现有 `Control_UpperLip_L/R`，在 **FaceDragHandle** 调整 `Follow Surface`、`Surface Anchor`、`Surface Offset`、`Up Shape Override` 和拖拽灵敏度；拾取半径由同物体 **SphereCollider > Radius** 决定。关闭跟随后可手调 Collider Center。
- `FacePointerFeedback` 只读取既有控制器/碰撞体和实际射线结果，移除按嘴部 Key 名称决定固定坐标、半径和眉头运行时覆写。跟随由现有 `FaceDragHandle` 执行，`FaceBlendShapeController` 提供真实 BakeMesh 表面位置。
- `LailaBlendShapeOrder.cs` 仅处理指定 FBX 的导入列表，不改变 Key 名称和每帧数据。项目运行逻辑按名字查索引，导入 Renderer 权重也按名字恢复。

## 实测证据

`mesh-before.json` / `mesh-after.json`：31 个名字对应的形变帧权重、顶点/法线/切线增量哈希逐项一致，顶点数保持 1650；FBX SHA256 为 `86C77AF278764973B27C7C6F7F0B86C2D5434C878CB0B63A6402CC0D74C8D136`，GUID 保持 `e56ea6a13114e354b8bc13f4c0f086dd`。未重新制作嘴部几何、修改光照或压低最大权重。

`authority-tests.json` 三项通过：关闭跟随后手动中心保持；半径缩为 70% 后反馈/跟随不覆写；Surface Offset 从 0.0035 改到 0.0055，实际世界位移 0.00200001872，配置保持新值。最初断言用了计算表达式的严格浮点相等，输出实际值后改为浮点容差；并非发现或掩盖配置覆写。

当前 Play 复测 `../laila-lip-peaks-20261002/lip-tests.json`：左右上唇分别取 0/50/100 的九种组合全部通过，真实控制器权重误差小于 0.001；其他 Key 保持零。两唇各九个附近采样首命中正确。左右 ±20 度可见，背面不可见。默认无文字，显式 Editor 调试开关生效。

`../laila-interaction-20261002/feedback-tests.json`：17/17 控制区命中、拖拽跨点保持、取消、窗口外、UI 阻挡、背面遮挡、禁用恢复光标全部通过；`lifecycle-tests.json` 记录场景切换与失焦恢复均通过。

最终版本定向 EditMode 10/10 通过，0 失败/跳过，1.0590501 秒，job `293f1c39fd2448978e4b63965237c082`。四个 C# 文件手动 lint 成功，diff 空白检查通过。gc_scan 仍报告既有三个 Game.LailaFace 命名空间与 Gameplay 目录不匹配，未扩大修改范围。最终独立读取为 Play=false、compiling=false、dirty=false、31 权重全零，相机位置和旋转已恢复。

## 保存范围与文件

迁移前完整场景副本 `laila-before.unity`，迁移后 `laila-after.unity`。逐行比较仅新增序列化跟随字段、左右上唇及两侧眉头四个 Collider 的授权迁移参数；原有用户布局/场景内容保留。最终保存前再次生成副本并验证全文与已审查副本一致才保存原场景。退出 Play，原场景已保存、dirty=false。

修改文件：`FacePointerFeedback.cs`、`FaceDragHandle.cs`、`FaceBlendShapeController.cs`、新增 `Editor/Tools/LailaBlendShapeOrder.cs` 与 Unity 生成 meta、`Scenes/laila.unity`；删除独立窗口及 meta。本报告之外现有工作树改动不认领。不提交、不推送。

## 验收限制

实际查看了用户原生 Inspector 参考截图。Unity GUIView 的原生 Inspector 抓取返回灰图，重绘后仍无有效像素，因此不将 `inspector-before.png` 当作修改后证据；原生排序由当前 Renderer 名称索引与实际 Inspector 元数据确认，属性界面截图仍缺失。

测试为真实 Unity Play 射线/事件链和场景相机渲染，未代替用户真实鼠标手感验收。旧有口腔/牙齿资产限制仍适用；本轮不宣称全部嘴部几何组合重新验收。Console 异常的进一步核实见 [exception-followup.md](exception-followup.md)：原生 Inspector 异常有完整重载堆栈，目前未复现，不能证明既有；另发现并修复本轮表面缓存对 Unity 已销毁对象使用 `??=` 的回归，修复后重建/配置验证通过，最终 Console 错误查询零条。Hooks 信任状态未确认，手动执行 C# lint；gc_scan 仍为三个既有命名空间/目录问题。
