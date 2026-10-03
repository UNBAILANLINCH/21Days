# 莱拉文件去留清单

日期：2026-10-03。下面“可删”指后续清理建议，不表示已删除；本轮已删除两个无正式引用的 Unity 模型及其 meta。按用户决定，先提交正式成果、制作工具及验收记录；修复前原件、阶段 blend 与场景快照不加入，第一笔提交完成后再单独清理。删除原件后无法保证按历史脚本复现原制作输入。

清理完成（2026-10-03）：成果已提交为 `26459be`。随后删除 32 个 PRP 过程文件（约 63.29 MiB）：9 个早期模型/备份、重复最终 FBX、2 个修复前原件、18 个场景快照及配套 meta、失败的 Inspector 灰图、Library 操作记录和旧导入 meta。已提交的最终源模型、正式资产、制作脚本、报告、JSON 和有效截图全部保留；它们仍可用于后续制作或验收复查，不因已进入 Git 就删除工作副本。

## 当前正式提交：保留

提交范围包含正式资产、代码、训练准备文档和下述归档成果。文件计数包含配套 meta 与验证数据，不代表独立模型版本数量。

| 文件（路径相对仓库） | 处理 | 后续用途 |
| --- | --- | --- |
| Assets/_Project/Art/fbx/Head-topo-expression-extended-brow-regions-refined3 1.fbx + .meta | 保留、提交 | 当前 Unity 正式 31 形态头模；校准 hash 的基准 |
| Assets/_Project/Scenes/laila.unity | 保留、提交 | 17 区接线、眼球引用、控制点跟随设置 |
| Assets/_Project/Art/Textures/Laila/T_LailaFace_BrowsLips_HandPainted.png + .meta | 保留、提交 | 手绘眉毛和唇色；以后继续上色 |
| Assets/_Project/Art/Materials/Laila/M_LailaFace_Mural.mat | 保留、提交 | 手绘图片与脸部材质绑定 |
| Assets/_Project/Art/Shaders/MuralFace.shader | 保留、提交 | 实际采样 PNG，不保留会丢掉眉毛/唇色效果 |
| Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs | 保留、提交 | 形态读写与控制点表面跟随；训练采样可复用公开读取接口 |
| Assets/_Project/Scripts/Runtime/Gameplay/FaceDragHandle.cs | 保留、提交 | 分段眉毛、独立唇区、嘴角、视线、取消拖动 |
| Assets/_Project/Scripts/Runtime/Gameplay/FacePointerFeedback.cs + .meta | 保留、提交 | 抓点提示、右手光标和生命周期清理 |
| Assets/_Project/Scripts/Runtime/Gameplay/MuralFaceController.cs | 保留、提交 | 当前阴影过渡和运行时反馈启用 |
| Assets/_Project/Scripts/Editor/Tools/LailaBlendShapeOrder.cs + .meta | 保留、提交 | 原生 Inspector 按部位排序；训练按名称读取，不依赖这个索引顺序 |
| Assets/_Project/Scripts/Tests/EditMode/LailaFace/FaceDragHandleTests.cs | 保留、提交 | 10 项控制交互回归，不代替新 17 维训练契约测试 |
| PRP/laila-mouth-repair-20261002/mouth-repaired.blend | 保留、提交 | 唯一正式交付的可编辑最终源模型，不可当缓存删除 |
| PRP/laila-expression-recognition/repair_mouth_shape_keys.py | 保留、提交 | 嘴部制作工具；重新运行需要原件，不能对最终结果随意重复应用 |
| PRP/laila-mouth-repair-20261002/acceptance-report.md | 保留、提交 | 修复范围、几何检查和口腔限制；不是训练语义校准报告 |
| PRP/laila-mouth-repair-20261002/before-after.png | 保留、提交 | 修复前后摘要，不是神经网络输入 |
| PRP/laila-mouth-repair-20261002/after-runtime/singles-part1.png、singles-part2.png | 保留、提交 | 单键四档的精选视觉证据 |
| PRP/laila-all-points-20261002/all-17-overview.png | 保留、提交 | 当前控制点布局总览 |
| PRP/laila-interaction-20261002/light-before-after.png | 保留、提交 | 材质/光照改变的对照证据 |
| PRP/laila-interaction-20261002/right-hand-0.png、right-hand-1.png、right-hand-2.png | 保留、可随提交 | 三态光标外观样张；运行时由代码生成，删图片不影响功能，但保留方便对照 |
| ai-docs/docs/modules/lailaface/lailaface-module-guide.md | 更新后提交 | 当前模型、源码与场景接线入口 |

## 模型与源文件

| 文件 | 处理 | 原因 / 将来用途 |
| --- | --- | --- |
| Assets/_Project/Art/fbx/Head-topo-expression-extended2.fbx + .meta | 已删除，提交删除 | 正式资产无 GUID 引用；不是当前最早停用备份 |
| Assets/_Project/Art/fbx/Head-topo-expression-extended-brow-regions-refined3.fbx + .meta | 已通过 Unity 删除，从新增暂存项移除 | 不带 1 的中间版本无正式引用 |
| Assets/_Project/Art/fbx/Head-topo.fbx + .meta | 必须保留（既有文件） | 当前停用 Head-topo 的 16 形态备份仍引用它 |
| PRP/laila-mouth-repair-20261002/source-backup/original.blend、original.fbx | 已删除，原为未跟踪文件 | 用户不保留此阶段回滚；对应脚本输入不再归档 |
| PRP/laila-mouth-repair-20261002/mouth-repaired.fbx | 已删除重复副本 | 删除前核对 SHA256 与正式 Assets FBX 一致 |
| PRP/Head-topo-expression-extended-brow-regions-refined3.fbx、Head-topo-expression-extended2.fbx | 已删除 | 已淘汰导出版本 |
| PRP/Head-topo-expression-extended-brow-regions-refined-painted.blend | 已删除 | 早期程序化上色阶段；保留制作脚本和最终手绘 PNG |
| PRP/Head-topo-expression-extended-brow-regions.blend、*-refined.blend、*-refined2.blend | 已删除 | 分段眉毛与幅度调整过程，最终源模型已提交 |
| PRP/Head-topo-expression-extended.blend | 已删除，需提交删除记录 | 早期制作历史可从旧 Git 提交找回；本次未提交的旧模型修改随清理移除 |
| PRP/Head-topo-expression-extended.blend1、*-refined.blend1 | 已删除 | 用户不再保留早期制作回滚 |
| Assets/_Project/Art/fbx/TDImportCache/Head-topo.tdc 及配套 meta | 可删缓存，不提交 | 导入过程产物，不是模型源文件 |

## 验收与训练文件

| 文件 | 处理 | 后续用途 |
| --- | --- | --- |
| PRP/laila-mouth-repair-20261002/geometry-check.json、candidate3-intersections.json | 保留、补入提交 | 支撑报告中的形变连续性与面部自身相交检查 |
| 同目录 candidate3-teeth-summary.json | 保留、提交 | 内部牙齿三角对统计及原文件 hash，便于核对报告 |
| 同目录 candidate3-teeth.json | 保留、提交 | 约 39 MB、248 万行逐三角对原始记录；供精确复查，摘要方便阅读 |
| 同目录 after-runtime/controller-matrix.json、matrix.json、raycast.json | 保留、补入提交 | 实际权重组合、截图索引和拾取检查数据 |
| 同目录 actual-gpu/、before/、after-runtime/extremes-contact.png | 保留、提交实际存在的 PNG/JSON | 原始 GPU 与修复前视觉证据；以后定位回归有用，不是训练集；不是修复前模型原件 |
| PRP/laila-all-points-20261002、laila-controller-config-20261002、laila-follow-points-20261002 下的 *.unity + .meta | 已删除，共 18 个文件 | 阶段快照不再保留；正式场景及验证记录已提交 |
| 上述目录及 laila-interaction-20261002、laila-lip-peaks-20261002 的剩余 JSON | 保留、提交验证记录；不提交 library-deliverables.json | 特定阶段参数/事件验证，含修复前失败记录；可复查历史，不能冒充当前训练标注；Library 操作记录留本地 |
| 上述目录 report.md、implementation-report.md、exception-followup.md | 标明历史后提交 | 已说明被替换的实现及缺失截图；Console 异常收尾证据一并保留 |
| PRP/laila-interaction-20261002/model-import-original.meta | 已删除 | 原导入设置快照；正式 FBX 的 meta 保留 |
| PRP/laila-expression-recognition/refine_shape_keys.py、paint_face.py | 保留、提交工具 | 早期眉毛分区/程序化上色算法；需要对应阶段输入，不自动重建当前最终版，不能重复应用到最终源模型 |
| PRP/Blender材质完整导出教程.txt | 保留本地、可另整理提交 | 通用制作参考，不是当前训练入口 |
| PRP/laila-expression-recognition/spec.md | 更新、提交 | 当前 17 维训练顺序、采集与导出条件 |
| ML/expression-recognition/README.md | 更新、提交 | Python 流水线入口，指向最新 PRP |
| ML/expression-recognition/configs/rigs/laila_rig.yaml、laila-binding-review.md | 标历史后保留、提交 | 复现旧 12 维实验；新版不能直接训练它们 |
| ML/expression-recognition/tests/test_rig.py 的旧绑定用例 | 保留、随历史配置提交 | 验证旧 v1 归一化/侧别，不证明 v2 几何成立 |
| ML/expression-recognition/tests/test_laila_contract.py | 保留既有测试 | 仍为旧 12 维契约；新版增加独立 17 维测试，不破坏旧实验复现 |
| 将来的 laila_rig_v2.yaml、v2 绑定记录与测试 | 校准后生成并提交 | 下次训练需要的版本化配置；目前尚不存在 |
| ML 下 .venv/、.cache/、data/、artifacts/ | 留在既有忽略范围 | 环境、原图、缓存、训练权重；不提交也不为本轮清理删除 |

全局渲染设置、GLTFUtility 依赖、TimeManager 的 0.25 倍速、Mo 雾颜色和 music 文档另行处理，不混入莱拉清单。
