# 2026-10-07 手动分批提交清单

本清单依据当前 HEAD、暂存区、工作树及未跟踪文件整理。覆盖登记的 22 个模块的项目状态入口，以及本轮实际差异；没有宣称全仓代码逐行审计。提交和推送均未执行。

## 使用方式

在 PowerShell 中先执行：

```powershell
Set-Location -LiteralPath 'D:\GitHubDesktop\21Days'
git status --short
```

逐批执行下面的 add、查看差异、commit；任一步报错就停止该批。现有暂存区已混有其他文件，因此每条 commit 都限定相同路径，不能改成不带路径的 commit / `-a`，也不执行 `git add .`。新整理的文档包含未暂存改动，需重新 add 后才是最终版本。提交成功后再执行下一批。

03 含用途未确认的既有 GLTF 导入改动，先核对该批说明。04 依赖 02 / 03；08 在其余功能批完成后执行。09 为可选视觉参数，不与文档混提交。

## 审查结论与验证

| 范围 | 位置 / 行为 | 结论 |
| --- | --- | --- |
| Taming / Monster | 重复测试、当前存档版本与 legacy 迁移 | 重复成员已移除；此前本次对话定向 157/157 通过 |
| Laila / Recognition / Rhythm | 当前 EditMode | 232/232 完成、失败 0；job `fd852290a4db4cbbbf29fa6deb624f60`，程序集新于源码且包含新测试 |
| Rhythm 校准返回曲库 | 当前单条 Showcase | 1/1 通过、失败 / 跳过 0；job `009147b0c14e4024903e3d428c5782f3`，`Logs/verify/rhythm/20261007-153636/report.md` PASS、检查点失败 / 运行时异常 0 |
| C# 增量 | 修改及新增脚本 | 手动 project-lint 退出 0 |
| ML 研究工具 | 四组现有数据护栏测试 | 本轮 36/36 通过；首轮临时目录父路径不存在，改用子项目忽略缓存后复跑通过 |
| 模型 / 资产 | 四对 ONNX / metadata 与新增资产 | 四个 SHA256 配对一致；选入批次的新增 Unity 文件都有 meta |
| 文档 | 相对文件链接与代码行引用 | 相对文件链接无缺失；错误 Panel 引用已修正 |
| 差异格式 | 工作树 / 暂存区 | 本轮文档工作树 diff --check 通过；原暂存字体 / laila 场景有 Unity 序列化空字段尾空格提示，未手改 YAML |
| 静态不变量 | Gameplay 命名空间与动态字体 | 仍有三处既有目录 / 命名空间问题及 16,667KB 字体提示；不据扫描建议擅自搬目录或清字体 |
| 埋点 | Rhythm 增量及新识别实验 | Rhythm 校准 / 曲库路径复用现有事件；纯参数校验 / 高频采样不加日志。识别错误已有面板反馈，正式业务诊断接线未验收 |
| 学习记录 | 本轮纠错 | 本次无新结论；修正状态与引用，不再写重复坑册 |

Unity 编译 / 定向测试结果与历史文档中的全仓数字分开。物理鼠标、真人语义、完整 Player 交互与设备延迟仍未验收。

## 01 · fix(tests): 修复重复测试并对齐多目标存档版本

删除完全重复的身份保护用例，保留参数化覆盖；按存档 v2 构造当前用例并保留 v1 迁移检查。

```powershell
$batchPaths = @(
    'Assets/_Project/Scripts/Tests/EditMode/Taming/TamingRulesTests.cs'
    'Assets/_Project/Scripts/Tests/EditMode/Monster/EncounterSaveDataSceneKeyTests.cs'
    'Assets/_Project/Scripts/Runtime/Monster/EncounterSaveData.cs'
)
git add -- $batchPaths
git diff --cached --stat -- $batchPaths
git diff --cached -- $batchPaths
git commit -m 'fix(tests): 修复重复测试并对齐多目标存档版本' -- $batchPaths
```

## 02 · fix(lailaface): 允许研究试玩场景初始化脸部反馈

扩展既有场景范围，供后面的独立研究场景使用；不搬目录或修改公共输入。

```powershell
$batchPaths = @(
    'Assets/_Project/Scripts/Runtime/Gameplay/FacePointerFeedback.cs'
    'Assets/_Project/Scripts/Runtime/Gameplay/MuralFaceController.cs'
)
git add -- $batchPaths
git diff --cached --stat -- $batchPaths
git diff --cached -- $batchPaths
git commit -m 'fix(lailaface): 允许研究试玩场景初始化脸部反馈' -- $batchPaths
```

## 03 · chore(packages): 接入Sentis并保存模型导入依赖

先于 04 提交。Sentis 2.1.3 与实际程序集引用成对；lock 为现有 Unity 解析结果。此批同时包含既有 GLTFUtility 和其四个 Always Included Shader 引用：未找到项目源码调用 / glTF 资产，用途尚未确认。若不需要该导入包，先在 Package Manager 移除并让 Unity 重新解析、删除对应四个常驻引用，重新检查本批；不要仅回退 lock 或 GraphicsSettings 后保留另一半。

```powershell
$batchPaths = @(
    'Packages/manifest.json'
    'Packages/packages-lock.json'
    'Assets/_Project/Scripts/Runtime/Game.Runtime.asmdef'
    'Assets/_Project/Scripts/Editor/Game.Editor.asmdef'
    'ProjectSettings/GraphicsSettings.asset'
)
git add -- $batchPaths
git diff --cached --stat -- $batchPaths
git diff --cached -- $batchPaths
$commitBody = @'
- Packages 接入 Sentis 2.1.3 与 Unity 解析的依赖锁定结果
- Runtime / Editor 程序集引用实际 Sentis 与 JSON 依赖
- GLTFUtility 保留既有导入包和四个常驻 Shader 引用
'@
git commit -m 'chore(packages): 接入Sentis并保存模型导入依赖' -m $commitBody -- $batchPaths
```

## 04 · feat(lailafacerecognition): 接入离线识别与四版候选试玩

依赖 02 / 03。目录范围仅限四版 ONNX / JSON、共用展示配置及其 meta、运行时与定向测试。原 laila 保留 51D，独立场景默认定向修复 59D；反馈记录、模型切换、中性区和迟滞配套交付。研究模式禁止正式 Player 构建，独立语义与完整拒识未验收。

```powershell
$batchPaths = @(
    'Assets/_Project/Data/LailaFaceRecognition.meta'
    'Assets/_Project/Data/LailaFaceRecognition/'
    'Assets/_Project/Scripts/Runtime/LailaFaceRecognition.meta'
    'Assets/_Project/Scripts/Runtime/LailaFaceRecognition/'
    'Assets/_Project/Scripts/Tests/EditMode/LailaFaceRecognition.meta'
    'Assets/_Project/Scripts/Tests/EditMode/LailaFaceRecognition/'
    'Assets/_Project/Scripts/Editor/Tools/LailaRecognitionTools.cs'
    'Assets/_Project/Scripts/Editor/Tools/LailaRecognitionTools.cs.meta'
    'Assets/_Project/Scripts/Editor/Tools/LailaRecognitionCaptureWindow.cs'
    'Assets/_Project/Scripts/Editor/Tools/LailaRecognitionCaptureWindow.cs.meta'
    'Assets/_Project/Scripts/Editor/Tools/LailaRecognitionDeploymentCheck.cs'
    'Assets/_Project/Scripts/Editor/Tools/LailaRecognitionDeploymentCheck.cs.meta'
    'Assets/_Project/Scripts/Editor/Tools/LailaPlaytestTools.cs'
    'Assets/_Project/Scripts/Editor/Tools/LailaPlaytestTools.cs.meta'
    'Assets/_Project/Scenes/laila.unity'
    'Assets/_Project/Scenes/LailaRecognitionPlaytest.unity'
    'Assets/_Project/Scenes/LailaRecognitionPlaytest.unity.meta'
    '.claude/skills/generate-doc/modules.json'
    'ai-docs/docs/modules/lailafacerecognition/lailafacerecognition-module-guide.md'
    'ai-docs/docs/modules/lailaface/lailaface-module-guide.md'
    'ai-docs/docs/modules/lailaface/lailaface-external-api.md'
)
git add -- $batchPaths
git diff --cached --stat -- $batchPaths
git diff --cached -- $batchPaths
$commitBody = @'
- 识别模块新增严格17轴采样、CPU推理及生命周期释放
- 独立研究场景新增四版模型对照、稳定展示与主动反馈保存
- Editor 工具新增隔离试玩、未标注采集与模型配对检查
- 定向测试及模块注册、指南配套更新
- 留：独立语义、完整拒识与 Player 验收未完成
'@
git commit -m 'feat(lailafacerecognition): 接入离线识别与四版候选试玩' -m $commitBody -- $batchPaths
```

## 05 · feat(exprnet): 增加反馈修复与候选诊断工具

四组测试 36/36 通过，新增 / 修改 Python 语法检查通过。保留原意见、稳定 ID、近邻组、训练 / 校准 / 开发角色与旧模型来源。删除过期待办与未经确定的采样派工，工具说明保留复现能力。原图、标签 JSON、checkpoint 与完整 artifacts 继续遵守现有忽略策略。

```powershell
$batchPaths = @(
    'ML/expression-recognition/DESIGN.md'
    'ML/expression-recognition/README.md'
    'ML/expression-recognition/launch_pilot.cmd'
    'ML/expression-recognition/configs/rigs/laila-v2-candidate-review.md'
    'ML/expression-recognition/analysis/laila_v2_candidate/ANNOTATOR.md'
    'ML/expression-recognition/analysis/laila_v2_candidate/CAPTURE_DIAGNOSTICS.md'
    'ML/expression-recognition/analysis/laila_v2_candidate/PILOT_FEEDBACK_20261005.md'
    'ML/expression-recognition/analysis/laila_v2_candidate/ROUND_20261003.md'
    'ML/expression-recognition/analysis/laila_v2_candidate/annotation_adapter.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/annotator.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/annotator.html'
    'ML/expression-recognition/analysis/laila_v2_candidate/input_diagnostic.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/input_diagnostic.html'
    'ML/expression-recognition/analysis/laila_v2_candidate/export_playtest.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/eye_contact_analysis.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/feedback_repair.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/latest_review.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/lifecycle_eye_check.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/pilot_batches.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/rejection_diagnosis.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/retrain_candidate.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/run_comparison.py'
    'ML/expression-recognition/analysis/laila_v2_candidate/unity_round.py'
    'ML/expression-recognition/tests/test_feedback_repair.py'
    'ML/expression-recognition/tests/test_latest_review.py'
    'ML/expression-recognition/tests/test_pilot_batches.py'
    'ML/expression-recognition/tests/test_retrain_candidate.py'
    'PRP/laila-expression-recognition/spec.md'
)
git add -- $batchPaths
git diff --cached --stat -- $batchPaths
git diff --cached -- $batchPaths
$commitBody = @'
- 研究工具新增成对特征诊断、有界候选训练与反馈定向修复
- 标注入口保留稳定ID、近邻组、原意见与训练数据角色
- 四组数据护栏测试36项通过
- 规格和工具说明同步当前状态并删除过期派工
'@
git commit -m 'feat(exprnet): 增加反馈修复与候选诊断工具' -m $commitBody -- $batchPaths
```

## 06 · fix(rhythm): 修复校准返回曲库后的页面遮挡

采用 / 保留校准后回曲库，打开曲库关闭残留浮层；补取消 / 采用 / 结算后的实际按钮、输入和存档回归。历史事务阻塞记录改为已由后续 13/13 补验，不合并历史与当前结果。

```powershell
$batchPaths = @(
    'Assets/_Project/Scripts/Runtime/Rhythm/RhythmState.cs'
    'Assets/_Project/Scripts/Runtime/Rhythm/RhythmView.cs'
    'Assets/_Project/Scripts/Tests/Showcase/Rhythm/RhythmLibraryShowcase.cs'
    'ai-docs/docs/modules/rhythm/rhythm-module-guide.md'
    'PRP/rhythm-final-regression-fix-20261005/confidence-combat-implementation-report.md'
)
git add -- $batchPaths
git diff --cached --stat -- $batchPaths
git diff --cached -- $batchPaths
$commitBody = @'
- State 采用及保留校准后返回曲库
- View 打开曲库时关闭旧浮层并统一结算控件布局
- Showcase 校准返回和目标按钮命中回放1项通过
- 模块指南修正事务验证状态并保留历史失败证据
'@
git commit -m 'fix(rhythm): 修复校准返回曲库后的页面遮挡' -m $commitBody -- $batchPaths
```

## 07 · feat(font): 保留现有中文字形并补齐新增字符

保留合并基线 318 个字符及其字形 / atlas 数据，补 328 个缺失字符，合计 646。asset 与现有 meta GUID 配套；meta 无改动，无需再次 add。当前约 16.3MiB，静态扫描仍提示动态字体体积；按本次明确要求保留，不执行 Clear Dynamic Data。

```powershell
$batchPaths = @(
    'Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset'
)
git add -- $batchPaths
git diff --cached --stat -- $batchPaths
git diff --cached -- $batchPaths
$commitBody = @'
- 字体保留合并基线318个字符及其字形数据
- 字体补齐328个缺失字符，合计646个
- 留：保持动态字形数据，界面显示仍需视觉核对
'@
git commit -m 'feat(font): 保留现有中文字形并补齐新增字符' -m $commitBody -- $batchPaths
```

## 08 · docs(project): 同步模块进度并精简重复交接记录

最后提交。目录补齐注册表 22 个模块；按源码纠正 Identity / Stealth / World 接线、相机约束、背包类别和研究部署状态。HANDOVER 只保留共享进度 / 约束 / 证据入口，去掉本机同步、暂存、会话归还状态；详细训练角色保留在 ML 记录。精选初次接线证据配套加入，完整日志 / 进度流水不加入。

```powershell
$batchPaths = @(
    'HANDOVER.md'
    'ai-docs/docs/catalog.md'
    'docs/modules/README.md'
    'docs/roadmap.md'
    'PRP/README.md'
    '.claude/skills/generate-doc/SKILL.md'
    'PRP/commit-batches-2026-10-07.md'
    'PRP/laila-recognition-20261003/checkpoint.md'
    'PRP/laila-recognition-20261003/realtime-and-stack.md'
    'PRP/laila-recognition-20261003/result-ui.png'
    'PRP/laila-recognition-20261003/unity-verification.json'
    'PRP/laila-recognition-20261003/ui-lifecycle.json'
    'PRP/laila-recognition-20261003/realtime-current.png'
    'PRP/laila-recognition-20261003/realtime-unknown.png'
    'PRP/laila-recognition-20261003/realtime-verification.json'
    'PRP/laila-recognition-20261003/inspector-repair.json'
)
git add -- $batchPaths
git diff --cached --stat -- $batchPaths
git diff --cached -- $batchPaths
$commitBody = @'
- 项目目录按注册表补齐22个模块
- 交接与roadmap同步已有接线、回放结果和剩余验收
- Laila历史记录与未确定采样建议收敛到各自职责文档
- 手动提交清单列出功能批次、依赖及排除文件
'@
git commit -m 'docs(project): 同步模块进度并精简重复交接记录' -m $commitBody -- $batchPaths
```

## 09 · 可选：暗雾颜色

`DarkFog_Mo.asset` 只有颜色值变化，当前没有对应视觉验收证据。确认是需要保留的效果后单独提交；不归入识别、音游或文档批。

```powershell
$batchPaths = @('Assets/_Project/Data/IsometricExploration/DarkFog_Mo.asset')
git add -- $batchPaths
git diff --cached -- $batchPaths
git commit -m 'chore(exploration): 调整暗雾颜色' -- $batchPaths
```

## 单独排除的构建设置

下面四个 tracked 文件是独立测试构建相关改动：Scenes.asset 删除 RhythmDemoScene 登记，EditorBuildSettings 让 RhythmDemo 成为第一个启动场景，ProjectSettings 加测试包标识，UniversalRP 改 shader 预筛选。它们不属于上面模块提交，保留暂存也不会被限定路径的 commit 带入。

确认要撤销这四个差异时，手动执行以下精确恢复。只恢复 HEAD 的这四个文件，不碰字体、模型、场景内容或其他修改：

```powershell
$buildOnlyPaths = @(
    'Assets/AddressableAssetsData/AssetGroups/Scenes.asset'
    'Assets/Settings/UniversalRP.asset'
    'ProjectSettings/EditorBuildSettings.asset'
    'ProjectSettings/ProjectSettings.asset'
)
git diff HEAD -- $buildOnlyPaths
git restore --source=HEAD --staged --worktree -- $buildOnlyPaths
```

GraphicsSettings 的四个 Shader 来自 GLTFUtility，已与包依赖放在 03，不能当临时构建差异一并恢复。

## 不放进这些提交的文件

| 文件范围 | 处理 |
| --- | --- |
| `Assets/NewShaderVariants.shadervariants*`、`Assets/Resources.meta` | 临时构建资源 / 空目录记录，先保留，不提交 |
| `Assets/_Project/Art/fbx/TDImportCache*`、`ProjectSettings/ScriptableBuildPipeline.json` | 导入 / 构建缓存，不提交 |
| `PRP/font-merge-20261007/append-local-characters.json` | 一次性补字载荷，虽已暂存仍不纳入功能批 |
| `PRP/boot-authoring-review-20261005/` | 未实施方案，当前不当作已完成模块记录入库 |
| `PRP/rhythm-architecture-20261004/` | 历史图解大包；README 已标明快照和当前指南入口，整包不自动加入功能提交 |
| `PRP/rhythm-score-review-20261004/`、嵌套 `hold-edit/` | 曲谱候选 / 工具原型，独立评审后再决定；不递归 add 父目录 |
| `PRP/taming-multi-control/` | 未应用 patch / 离线审查快照，不替代生产代码与现有模块指南 |
| `PRP/rhythm-*/before/`、`Scripts/`、`tests/`、`*.cs.txt`、`*.before.*.txt`、辅助核验脚本 | 保留恢复 / 排查材料，不与现有 Runtime 源码重复提交 |
| `PRP/rhythm-song-progression-20261004/HANDOVER-fragment.md` | 交接片段已有共享入口，不再增加一份派工记录 |
| `ML/expression-recognition/analysis/laila_v2_candidate/RefreshEditorPoints-lifecycle.patch` | 生命周期改动已有生产源码，不提交第二份临时补丁 |
| `PRP/laila-recognition-20261003/realtime-progress*`、`inspector-final-verification.json` | 非精选过程 / 终态记录，当前保留但不自动入库 |
| Blender 导出教程、训练原图 / 标签 / 权重、完整日志 | 与本批模块信息无关或已被忽略，保留现有策略 |

上述“排除”表示不提交，不代表删除。未执行整仓回退、清缓存、删除原件或 stash 操作。

## 全部完成后

```powershell
git status --short
git log -9 --oneline
```

余下应主要是上表保留材料、未选择的视觉配置或未恢复的独立构建设置。逐项核对，不为了工作区显示干净而执行整仓清理。
