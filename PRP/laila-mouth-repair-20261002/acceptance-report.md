# Laila 嘴部形态键修复自检报告

日期：2026-10-02。状态：模型修复和下述自检完成，等待用户视觉验收；不能据此声称所有连续组合都绝无交叠。

## 修改与回滚

修复 11 个嘴部键的邻接形变连续性，消除原上唇单侧键跨中线拉扯和局部尖刺。左右上唇采用互补平滑分区，嘴角位移向周围组织连续衰减。保留唇部深度和厚度；没有修改光照或材质来隐藏问题。嘴角最大上下位移 5 mm、内外位移 4 mm；上唇单侧最大约 8.13 mm，下唇最大 8 mm（模型局部单位按米解释）。没有改控制器幅度或钳制逻辑。

实际应用到现有 Unity 资产：`Assets/_Project/Art/fbx/Head-topo-expression-extended-brow-regions-refined3 1.fbx`。其 GUID `e56ea6a13114e354b8bc13f4c0f086dd` 和原 `.meta` 保持不变。31 个 blendshape 的名称、顺序和控制器绑定保持不变；20 个非嘴部键、Basis、11 个导出对象的坐标、拓扑、变换、UV 和材质名称未改。Blender 头部仍是 1091 顶点；Unity 导入后的拆分/焊接顶点由 1694 变为 1658，不能把这两个数解释为源拓扑被删减。

交付源模型：`mouth-repaired.blend`；导出副本：`mouth-repaired.fbx`；可重建脚本：`../laila-expression-recognition/repair_mouth_shape_keys.py`。最终 FBX SHA256：`86C77AF278764973B27C7C6F7F0B86C2D5434C878CB0B63A6402CC0D74C8D136`。

原件保存在 `source-backup/original.blend` 和 `source-backup/original.fbx`。原始工作用 blend 未覆盖。原件 SHA256 分别为 `36C4EF7C52A018FC5DB5BD0FED4314AEAA420ACFD3888A55B150A0F9F8082846`、`39C542D9E58E33D68EFAD16F7742F68D83593C1E00FC63E407899F90F037CFD8`。回滚模型时可将备份 FBX 恢复到上述同名资产并重新导入，保留既有 `.meta`；不会恢复其他用户改动。

## Unity 视觉检查

使用现有 laila 场景的材质、灯光和主摄像机；在 Play 模式通过真实 `FaceDragHandle` 的 PointerDown/Drag/PointerUp 事件驱动。`before/` 和 `after-runtime/` 各有 50 张截图及 `matrix.json`，包括 Basis、每键四档和五个组合。连续图组使用实际控制器结果 BakeMesh 后临时渲染，复制原材质属性与变换，结束立即恢复；另以真正的 SkinnedMeshRenderer 拍摄 `actual-gpu/` 补充核对，未用代理渲染替代全部 GPU 验证。

下列每键均检查 25、50、75、100；0 为共同 Basis。检查结论：随权重渐变，100 时运动可辨识，未观察到原有尖刺、撕裂、翻面或外轮廓穿透。结果属于截图视觉自检，尚未获得用户对自然程度的验收。

| 名称 | after-runtime 图号 | 视觉结果 |
|---|---|---|
| Mouth_L_Up | 001–004 | 左嘴角平滑上抬 |
| Mouth_L_Down | 005–008 | 左嘴角平滑下移 |
| Mouth_R_Up | 009–012 | 右嘴角平滑上抬 |
| Mouth_R_Down | 013–016 | 右嘴角平滑下移 |
| Mouth_LowerLip_Down | 017–020 | 下唇连续下移、开口增大 |
| Mouth_L_Out | 021–024 | 左侧外展，周边连续 |
| Mouth_L_In | 025–028 | 左侧内收，未见尖刺 |
| Mouth_R_Out | 029–032 | 右侧外展，周边连续 |
| Mouth_R_In | 033–036 | 右侧内收，未见尖刺 |
| Mouth_UpperLipR_Up | 037–040 | 右上唇抬起，中心过渡连续 |
| Mouth_UpperLipL_Up | 041–044 | 左上唇抬起，中心过渡连续 |

组合中未列出的键为 0，列出的键全为 100：

| 图号 | 组合 | 结果 |
|---|---|---|
| 045 | UpperLipL_Up + UpperLipR_Up + LowerLip_Down | 双上唇及下唇最大张开，唇缘连续 |
| 046 | 045 + L_Out + R_Out | 最大张开并外展，未见外侧撕裂 |
| 047 | 045 + L_In + R_In | 最大张开并内收，未见外轮廓折刺 |
| 048 | L_Up + R_Down + L_Out + R_In + UpperLipL_Up + LowerLip_Down | 左右、上下非对称组合，过渡连续 |
| 049 | L_Down + R_Up + L_In + R_Out + UpperLipR_Up + LowerLip_Down | 相反非对称组合，过渡连续 |

上述表中省略的名称前缀均为 `Mouth_`。完整名称和权重以各 `matrix.json` 为准。`before-after.png` 将 040、044、045、048、049 的嘴部放在左右两列对照，左为修复前，右为修复后。`singles-part1.png`、`singles-part2.png` 可浏览全部单键四档。

`actual-gpu/upper-left-100.png`、`upper-right-100.png`、`all-lips-100.png`、`asymmetric-100.png` 为真实 GPU 正面渲染；其中 all-lips 对应 045，asymmetric 对应 048。最大张开另检查左右约 20°、上下约 15°的正交视角（文件名 `*-ortho.png`）。所查角度没有观察到外部牙龈穿出、异常蓝色孔洞或口唇轮廓撕裂。极值张开能明显露齿；牙列保持原静态形状，仍有模型原有的风格化外观。

## 数值与控制器验证（区别于逐图视觉检查）

- Blender：648 个合法极值组合，无相对 Basis 法线反转，无三角面积比低于 0.15；最小面积比约 0.31414。另对固定随机种子的 4096 个合法连续权重组合采样，同样无失败，最小面积比约 0.42676。见 `geometry-check.json`。
- 非邻接三角交叉检查：11 个单键最大值及 648 极值组合未检出面部自身交叉。检测跳过共享顶点、共面和边界接触，不能视为无限精度碰撞证明。见 `candidate3-intersections.json`（最终模型对应的数据）。
- Unity Play：648 个组合经真实拖拽组件事件执行，实际 11 键权重与请求一致（容差 0.02）；记录在 `after-runtime/controller-matrix.json`。这 648 项是控制器/权重检查，未逐项人工看图。
- 控制器的同侧 Up/Down、Out/In 已显式检查互斥，反方向归零；两个轴可同时生效，左右独立。唇控制为独立 0–100。超范围 150 被钳到 100，负权重 -20 被钳到 0。没有为通过测试暗缩最大值。
- 五个嘴部手柄在真实 EventSystem RaycastAll 中均首先命中自身，组件启用；见 `after-runtime/raycast.json`。事件派发不等同于实体鼠标/移动端完整交互测试。
- 收尾读取 Unity Console 的 error、warning（最近 100 条，详细模式）：返回 0 条；未清空 Console。未运行项目全量 EditMode/PlayMode 测试套件。

## 口腔交叠与验收限制

原 Basis 的内部口腔衬面已与上下牙齿/牙龈静态网格交叉（检测到下组 676、上组 370 个三角对；这是面片对数量，不是可见瑕疵数量）。修复保留 Basis，因此不宣称整套口腔无交叠。运动会改变内部接触，但在本次检查的正面和四个侧向视角，未观察到这些内侧交叠穿出外唇或改变外轮廓。面向前方的外侧区域筛查也没有发现单键或组合新增交叉；该方向筛查不是所有视角的保证。仓库统计见 [内部相交摘要](candidate3-teeth-summary.json)；完整明细按下节说明保留本地，不作为仓库必需输入。

唇部深度与厚度视觉上保留，但没有严格体积守恒测量；未检查所有连续权重、所有观察角度、其他角色、其他场景或移动设备。极值自然程度和露齿幅度仍应由用户据实际 GPU 图验收。若要求口腔衬面与牙齿/牙龈在任何内部区域都绝不接触，需要额外修复 Basis/口腔或制作组合校正键，本轮没有擅自改变这些基础结构。

历史提交整理（2026-10-03）：完整明细当时与统计摘要一并归档。2026-10-04 按用户批准的最小清理，核验本地备份后只将 `PRP/laila-mouth-repair-20261002/candidate3-teeth.json` 移出后续 Git 跟踪并精确忽略；原文件不删除，历史不改写，本轮不提交或推送。它是生成的逐三角对几何诊断，不是训练标签、模型权重或运行时输入。

本地原文件和备份均为 39,404,704 字节、2,482,793 行，SHA-256 为 `4aba8acde6f7ee5326fe6d0aca122c42e9ae32ffb2b06751b1adefda1422ae14`。备份位于 `ML/expression-recognition/artifacts/laila_v2_candidate/candidate3-teeth-backup-20261004/candidate3-teeth.json`，同目录 `backup-manifest.json` 记录大小和哈希；该目录在既有 artifacts 忽略范围。历史 Git blob 为 LF、36,921,912 字节；本地为 CRLF，大小差异来自换行。统计摘要保留 Basis、11 个单键和 648 组合的计数/范围，不改变原诊断结论。

本地再查（在项目根执行）：

```powershell
Get-Item -LiteralPath 'PRP/laila-mouth-repair-20261002/candidate3-teeth.json' | Select-Object Length
Get-FileHash -Algorithm SHA256 -LiteralPath 'PRP/laila-mouth-repair-20261002/candidate3-teeth.json'
Get-FileHash -Algorithm SHA256 -LiteralPath 'ML/expression-recognition/artifacts/laila_v2_candidate/candidate3-teeth-backup-20261004/candidate3-teeth.json'
git check-ignore -v -- 'PRP/laila-mouth-repair-20261002/candidate3-teeth.json'
```

需要逐三角对精查时读取本地 JSON 的 baseline/single/combinations；缺少本地副本时先核对备份摘要或从既有历史取出，不能由小统计摘要重建全部明细。此次不处理其他模型、labels/history、权重或大文件。当前可提交的视觉证据为 before/ 与 after-runtime/ 的拼图、矩阵，以及 actual-gpu/ 的 all-lips-100 正面和四张正交侧向图；上文所述其余单张截图未在本地目录找到，不计入交付。制作脚本保留算法用途，不保证删除历史输入后可复现原制作。

## 编辑器与工作树状态

Unity 已退出 Play，嘴部权重归零，主摄像机恢复原位置与旋转。用户原有未保存的 laila 场景仍保持 dirty，未保存、重载或覆盖场景。70 个场景节点的激活状态、局部变换及手柄绑定前后快照相同。原有 3659 项文件快照中只检测到本次目标 FBX 内容改变；原有其他未提交改动保留。临时 Unity 预览资产及其 `.meta` 已通过 AssetDatabase 删除。

Blender 原工作窗口未操作，使用独立后台进程处理备份并保存交付模型。没有改 Unity 版本，没有提交或推送。Codex hooks 的信任状态未能确认；本次没有修改项目 C#。本报告为自检交付，不代表用户最终验收。

收尾项目检查：`gc_scan.py` 返回失败，报告三个既有 C# 文件（FaceBlendShapeController、FaceDragHandle、MuralFaceController）的 `Game.LailaFace` 命名空间与 Gameplay 目录不匹配。全工作树 `git diff --check` 返回失败，内容为既有 `laila.unity` diff 中的尾空格。本次未修改这些源文件或场景，未为消除检查提示扩大范围。仅清理本次生成的候选 blend/FBX、候选截图、冗余几何诊断和两张空白的失败侧视截图；保留原件、最终模型、正式证据和交叉检测原始数据。

Library：按要求使用官方 Library 技能的上传助手尝试保存对照图、两张实际 Unity 图和本报告；本机受支持的 hosted-app 工具发现返回 `Library prepare_uploads is not available`，上传未完成，没有可返回的原生附件 ID。上述文件仍在本地交付目录，未生成或猜测下载链接。
