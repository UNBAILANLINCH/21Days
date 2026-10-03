# laila 光影、分组与右手反馈自检报告

归档说明（2026-10-03）：本报告描述历史实现。独立分组窗口后来移除，改为原生 BlendShapes 排序；抓点跟随也已迁至 FaceDragHandle。actual-*-geometry.png 未在本地交付目录找到，当前版本以模块说明和 controller-config 报告为准。

日期：2026-10-02。仅处理 21Days / laila；未提交、推送、改 Unity 版本或保存场景。用户直接贴出的手部参考图已看到；采用原创右手像素骨骼，手掌朝向观察者、拇指在画面左侧，并非复制原图。分组按用户要求直接按部位设计。

## 实现与根因

- 光影：目标 FBX 原先为 Import normals + Calculate blendshape normals。在 UpperLipL 测试中，位置不动却明显改变法线的点有 769 个，导致嘴部拖动波及眼睛、额头阴影。经 Unity ModelImporter 改为 Calculate/Calculate 后降至 2 个相邻边界点。已保留原导入 meta 备份 `model-import-original.meta`。仅 laila 的 MuralFaceController 加入 0.28 的阴影柔化下限；原 shadowSoftness=0.155、strength=0.434、threshold=0.787，颜色和力度未改。当前 shader 的阴影主要来自自身法线与固定方向运算，不是实际场景 Light 的投射阴影。
- 分组：菜单 `21Days/LailaFace/按部位调节`，嘴 / 眼 / 眉，左右 / 中央；保留实际 31 个 key 的名字、索引和绑定。范围 0–100；同部位 Up/Down、Out/In 正值明确清零反向 key。单向唇 key 不虚构反向 key。通过 Undo 修改，不自动保存场景。
- 提示：用真实 EventSystem 第一射线结果选择当前抓点；UI 优先；用实际变形 BakeMesh 三角形判断面部遮挡。仅将原型中埋在表面内的两侧眉头拾取球运行时移到前表面，其余用户调整过的深度保留。提示本身不参与射线。拖动保留当前抓点；退出视口、取消、失焦、禁用、离开场景会清除捕获和提示。
- 右手：普通、可抓、抓取三态，32×40 像素；抓取为暖色折指。ScreenSpaceCamera UI 光标带明确指尖热点，只隐藏并恢复原 Cursor.visible，不改锁定状态或硬件光标纹理。仅 laila 自动安装，运行时根节点不继承模型的 100 倍缩放；退出 Play 后自动消失。

## 已测结果

- 最后编译后的 LailaFace 全部相关 EditMode 测试：10/10，通过，失败 0、跳过 0，0.951 秒。任务 ID `122ce443f6a74f52b36c6c551f507d06`。其中包含取消后旧拖动不得写权重的回归测试。
- 实际场景 17/17 抓点正确命中、提示可见。拖动跨到别的抓点保持原点、取消清除、空白区清除、UI 遮挡、面部背后遮挡、禁用恢复全部通过：`feedback-tests.json`。
- 跨帧 UI 遮挡通过：GraphicRaycaster 的 Block 在 PhysicsRaycaster 抓点之前，提示为空；`ui-cross-frame-tests.json`。先前同帧失败由测试用 Overlay UI 尚未绘制、Graphic.depth=-1 导致。已将同帧测试用 UI 放到 Camera Canvas 并实际渲染后再射线验证，最终同帧结果也通过；没有跳过失败或将生产逻辑缩小来规避。
- 切换到临时空场景、失去焦点都恢复原光标并清除提示；重新回到 laila：`lifecycle-tests.json`。提示已清除但抓取仍存在的情况也可正确取消：`cancel-regression.json`。
- 分组核验全部 31 key：`group-layout.json`。实际 EditorWindow.SendEvent 鼠标事件操作 Mouth_L_Up/Down、Eye_L_UpperLid_Up、Brow_L_Inner_Up，均达到 74.7；切 Mouth_L_Down 后 Up=0：`group-slider-tests.json`。这是实际窗口事件实操，不是直接调用 SetBlendShapeWeight 代替滑条。
- 真实控制器 PointerDown/Drag/PointerUp，跨帧等待皮肤更新后以真实 Main Camera、材质、场景光影拍摄，未替换 Renderer、未关闭阴影：baseline、maximum、mixed。每组完整 31 权重见 `controller-*.json`，三态及提示结果见 `capture-*.json`。
- maximum：左右嘴角 Up、左右上唇 Up、下唇 Down、左右上眼睑 Up、左右眉头 Up 均为约 100（浮点值 99.9999847），反方向为 0。mixed：左嘴角 Up / 右 Down、左上唇 100 / 右 0、下唇 100、左上眼睑 Up / 右 Down、左眉头 Up / 右 Down 均约 100。
- 光影 0/50/100 A/B 见 `light-before-after.png`：相同摄像机和材质的 BakeMesh 静态代理诊断，用于立即取得变形后的法线；不是最终实际控制器截图。最终控制器截图为 `actual-*-geometry.png`，两种证据明确区分。
- 五个涉及的 C# 文件手动 lint 通过；修改范围 diff --check 通过；新增脚本均有 Unity 生成的 meta。

## 实际修改文件与资产完整性

- `Assets/_Project/Scripts/Runtime/Gameplay/FaceDragHandle.cs`：只增补形态名、抓取状态、取消入口；已有用户修改保留。
- `Assets/_Project/Scripts/Runtime/Gameplay/MuralFaceController.cs`：场景局部柔化下限与反馈自动安装；已有用户修改保留。
- `Assets/_Project/Scripts/Runtime/Gameplay/FacePointerFeedback.cs` 及 Unity 生成的 meta：新增运行时抓点提示和原创右手。
- `Assets/_Project/Scripts/Editor/Tools/LailaFaceControlWindow.cs` 及 meta：新增分组窗口。
- `Assets/_Project/Scripts/Tests/EditMode/LailaFace/FaceDragHandleTests.cs`：新增取消回归测试，保留既有测试与用户修改。
- `Assets/_Project/Art/fbx/Head-topo-expression-extended-brow-regions-refined3 1.fbx.meta`：通过 ModelImporter 调整法线导入。
- FBX 本体延续上一轮嘴部修复，当前任务未再修改几何；SHA256 `86C77AF278764973B27C7C6F7F0B86C2D5434C878CB0B63A6402CC0D74C8D136`，GUID `e56ea6a13114e354b8bc13f4c0f086dd`。Blender 源文件和其他资产不覆盖。

## 验收边界

- 最终视觉效果由用户决定；没有实体鼠标手动连续操作或打包后的设备测试。已通过真实 Unity 射线、控制器、窗口事件及 GPU 画面验证。
- 原模型的低多边形轮廓及内部牙齿/口腔构造保留。最大张嘴会露出原有牙齿；本轮不宣称内部牙齿与口腔完全无相交，不用改变光影掩盖它。此前几何修复范围和限制见 `../laila-mouth-repair-20261002/acceptance-report.md`。
- Hooks 信任未能确认，所以手动执行 lint；gc_scan 实际返回失败（exit 1），仍为原有三个 Game.LailaFace / Gameplay 命名空间与目录不一致问题，不跨范围修复。
- 复连时已安全停止遗留 Play；当前编辑场景本来已为 dirty=false，70 节点与原快照一致。最终只读检查：Play=false、compiling=false、dirty=false、31 个权重全为 0，摄像机位置 (0,0,-10)、旋转 (0,0,0)，临时对象为空，Console error/warning 为 0。保留复连后的状态，不强行重建原先 dirty=true 状态，没有保存场景。
