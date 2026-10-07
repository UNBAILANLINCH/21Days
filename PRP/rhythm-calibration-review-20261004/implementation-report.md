# 确认式校准实现与验证（2026-10-04）

已实现固定8+32主轮、分类结果、明确应用、保留继续、原值/建议参考拍反馈和一次主动8拍补测。三个正式谱面、输入到DSP桥接、批次边界和三个偏移语义保持。跨轨组合复用现有Tap/Hold，只增加验证，没有新增组合类型或练习UI。

## 当前实现

- `RhythmCalibrationEstimator`：`TryAdd(double, out double)` 统计 Observed/Duplicate/Outside/Invalid，保留原目标拍索引；`Analyze()` 返回不可变结果。四时间块（短补测两块）分别要求至少一半目标覆盖，块中位数跨度≤20ms；有效样本至少配置minimum，离群截止max(20,3×rawMAD)。偏移超±300ms或过滤MAD>配置上限两倍拒绝；rawMAD>配置上限但时序稳定给Suggested，仍须确认。旧`TryEstimate`保留历史契约，玩家流程不再用它自动保存。
- 新`RhythmCalibrationReason`：NoInput、Unmatched、Insufficient、Unstable、Drift、Suggested、Stable。
- 新`RhythmCalibrationResult`只读属性：Reason、OffsetMs、RawMadMs、FilteredMadMs、BlockSpreadMs、Observed、Matched、Accepted、Duplicate、Outside、Invalid、TemporalStable、HasCandidate。构造器接收这些统计。静态`Supplement(primary, extra, minimum, maxMadMs)`要求主轮已覆盖且无漂移/不稳定、主轮≥minimum−8、合计≥minimum、独立补测有候选且偏移差≤20ms；分类使用配置MAD上限，保留主轮偏移不平均洗平漂移。
- `RhythmState`新增`CalibrationCandidate`只读查询；私有pendingCalibration、supplementUsed、calibrationSupplement、calibrationPreview、calibrationPreviewOffset、calibrationWarmup、calibrationTargets。`StartCalibrationRun`构造独立轮次；`FinishCalibration`分析结果；`ShowCalibrationSuggestion(notice=null)`显示候选；`SupplementCalibration`限制一次；`PreviewCalibration(bool)`切换原值/候选反馈；`ApplyCalibration`明确写入并保存；`KeepCalibration`保留；`ClearCalibrationSuggestion`在退出/换曲/开始歌曲时清候选。试听/补测中断后回结果页显示原因。
- `RhythmView`新增`OnCalibrationApply/Keep/Supplement`事件和`OnCalibrationPreview(bool)`事件；`ShowCalibrationResult(result, original, canSupplement, notice=null)`、`HideCalibrationResult()`、`CalibrationPreviewFrame(seconds,duration,offset)`、`CalibrationPreviewHit(correctedError)`。`Calibrating`新增可选targetCount，固定拍数与匹配次数分别显示。试听倒计时缓存约10Hz，输入反馈dirty刷新；试听使用同样的参考声，变化的是校正后输入反馈。

供架构图同步的私有字段补充：Estimator增加`beatIndices: List<int>`，公开Observed/Duplicate/Outside/Invalid只读计数。View增加`calibrationProgress:int`、`calibrationPreviewFeedback:string`、`calibrationPreviewRemaining:int`、`calibrationPreviewShownOffset:double`、`calibrationPreviewDirty:bool`、`calibrationResult:GameObject`、`calibrationResultText:TMP_Text`、`calibrationApply/calibrationCandidate/calibrationSupplementButton:Button`；`EnsureCalibrationResult()`构造结果层，`CalibrationChoice(...)`复用按钮。结果层在运行时构造，没有新增prefab/场景接线。

本轮代码路径为Runtime/Rhythm下RhythmCalibrationEstimator.cs、RhythmCalibrationResult.cs（新）、RhythmState.cs、RhythmView.cs；Tests/EditMode/Rhythm/RhythmCalibrationFlowTests.cs（新）、Tests/Showcase/Rhythm/RhythmShowcase.cs。另同步Rhythm模块文档三件套、此PRP与HANDOVER。Installer/曲库等工作区变化属于此前选曲实现，不混为本次校准新增。

## 验证状态

Unity当前编译已通过；最终EditMode作业`4d751074e9784512847ab6e57f7cf5ca`实际131/131通过、失败/跳过0，含非30ms配置补测分类。六个相关C#文件手动project-lint退出0；hooks信任状态未确认，不能声称hooks自动执行。

首轮真实Showcase作业`6b2bccc3847749958b715db70d7c8ae2`完成3项：旧Hold/校准生命周期和跨轨组合通过；新校准仅最终重入界面断言失败（存档85.8339462ms，整数滑块显示86ms），运行时异常0。保留失败报告`Logs/verify/rhythm/20261004-224845/report.md`。档案精确读盘断言不变，界面改核对Mathf.Round后的显示值，定向复验见下文。

第一次定向复验在Suggested分类失败，实际得到Drift，报告`Logs/verify/rhythm/20261004-225558/report.md`、运行时异常0。原驱动在协程等待后按当前时刻发键，帧调度误差混入预设32个样本；校准分类fixture改为实际虚拟键盘StateEvent携带`InputStart + 目标歌曲时刻`，仍经输入动作回调与当前State映射采样，释放在真实当前时刻。不扩大20ms质量门；旧生命周期及组合测试仍使用原实时驱动。该确定性接线测试不能冒充真人跟拍、真实键盘时间戳或物理延迟验收。

最终定向作业`a19125076b04402dba0c2a04cc408dd6`实际1/1通过、失败/跳过0，95.4668773秒；[报告](../../Logs/verify/rhythm/20261004-230245/report.md)检查点失败0、运行时异常0。最终三项目标用例均已有通过证据，未把首轮整体失败改写为3/3通过。候选页、试听反馈及重入恢复截图已核对文字与布局。

测试档案使用框架隔离SaveRoot，检查默认主轮无输入固定结束、单次补测上限、稳定但波动候选、原值/建议切换、试听失焦提示、取消保留、明确保存与重入恢复、漂移拒绝；跨轨真实输入覆盖双Tap、双Hold、Hold+其他轨Tap。同轨冲突仍拒绝。

## 边界

阈值是工程质量门，不是IID置信区间或真人效果保证。整拍相位别名、人的预判和声卡/键盘延迟不能独立辨识；物理输出/设备切换/OS挂起仍需实机验证。未新增设备身份档案，未改谱面offset，也未自动套用历史11ms解码差值。

三首正式谱面SHA256与本轮开始一致；EditorSettings、ProjectSettings和TimeManager的SHA256保持。`verify-installed.mjs`实际PASS：新音频bytes、164/229枚音符、旧入门备份hash、场景曲库接线与新脚本meta配对。限定本轮路径diff检查通过；全工作区diff检查受并行Laila场景和动态字体YAML空白影响，不清理或回滚他人改动。

Unity已实际交还干净RhythmDemo（3根节点）、idle、非Play、无运行测试、倍率1、Console error=[]，无InitTestScene残留。最终文档扫描仍有4处既有/共享资产问题：Gameplay的3处Game.LailaFace目录命名空间不匹配，以及Play后动态NotoSans字体35005KB；未修改并行模块或清理用户字体缓存。模块只读复审PASS，无BLOCK/WARN。没有commit/push/build/上传。
