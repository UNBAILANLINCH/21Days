# 校准可信度与固定谱 Combat 实施记录（2026-10-05）

本页记录 2026-10-05 的校准可信度与固定谱 Combat 增量，保留当时的通过和失败结果。当前曲库、事务保存与暂停状态见 [2026-10-06 页面实施报告](../rhythm-ui-navigation-20261006/implementation-report.md) 和 [模块指南](../../ai-docs/docs/modules/rhythm/rhythm-module-guide.md)。

## 实际实现

- 校准策略 median-interval-permutation-v2：全部匹配样本的中位数秩区间、最大端点距离、固定1024次跨度置换。旧样本数、覆盖、过滤MAD和偏移有效性检查保留。Stable可靠；Suggested/Drift低可信且可试听/明确采用；无效结果不提供候选。非显著不宣称稳定，8拍不能将低可信主轮洗成可靠。
- JSON增加confidence/区间/概率，策略版本区分新旧。原补偿不参与估计；明确采用才替换。补拍不一致且主轮缺样时保持无效，不从Drift重新取得候选。
- 固定谱Combat策略使用song/chart/revision/ruleset/scoring全身份、整数通过比例。错曲拒绝开始，错谱拒绝消费且不消耗局；成功失败一次性分流，中断独立通知，不写自由局纪录。无伤害量或正式入口接线。

## 真实证据与边界

- 本机session36 runtime记录：33observed/32matched/32accepted，估计151.5287ms，MAD24.61315ms，区间138.0665～176.9006ms；原值140.26935在区间内。新策略为Drift/Low，可提供估计但不承诺比原值更好。不是对声卡、键盘、人的预判分别测量。
- 编译错误0；手动C# lint通过。Edit任务0c0e564c26d64a33af935aa726210d59：69/69、零跳过，含非法时钟停局清理、正常采样、真实样本、噪声群组和固定谱错身份/重复结果。
- 首轮Edit66/69的旧跨度分类断言修订；首轮Showcase2通过/1失败报告Logs/verify/rhythm/20261005-125820/report.md保留。通过的确认流程覆盖默认8+32、补拍、AB、Low试听中断/保留/明确采用、取消、保存及重入；诊断失败/取消通过。时间戳旧fixture实际26/32且估计95ms，新策略为Suggested/Low，旧Drift断言失败，不是修复事件单调性回归。
- 125820/07截图已查看：区间、低可信说明与六个按钮可见，无结果文字覆盖按钮。回放顶部长诊断trace裁切，仅为测试叠加层。视觉/手感最终由开发者确认。

以下是定向补验，不与历史 171/171 合并为全量重跑。独立真人有效性、串行相关下区间覆盖、设备物理延迟、Player与正式Boot接线仍未验收。

## 定向补验

### Profile 采用事务修复与后续验证

独立审查补验要求已落实到生产State/View：先写独立候选快照，成功才更新内存/清候选；失败保持旧内存、保留候选以重试/Keep；专属calibrationApplying锁重复采用、试听、补测、开始、换曲及滑块，不依赖会被StopRound重置的starting；Exit等待采用Task后执行原退出保存，Dispose标closing防止后台UI恢复。旧乐观SaveCalibrationAsync已移除，成功埋点不在IO失败捕获内。

新增CalibrationApply_ProfileFailureRetryKeepAndSlowExit_Work使用真实隔离Profile，注入两次可识别IOException，验证旧内存/档案、候选保留/重试、失败后Keep与重入、Gate慢写重复点击/失焦/退出顺序。候选明确预置，仅验证采用保存生命周期，不冒充真人/32拍采样。

事务补丁阶段 EditMode 50/50 通过；当时连接中断使新增 Showcase 尚未完成。此缺口已在 2026-10-06 曲库 PlayMode 13/13 中补验，包含真实 Profile 写失败、慢写、重试、保留候选和退出等待，见上方页面实施报告。离线模型检查不替代这些 Unity 用例。

- 任务541d1bc87d0b48749e59352d3ff40985：时间戳对照通过，旧26/32、估计95ms；修复32/32、offset80/MAD45/spread0/Suggested。报告130327仍FAIL，因为新增Combat测试漏yield而无实际按键；不删除该失败记录。
- 修正Input.Press协程执行后，任务25eebff53f7b4e4ea22f9f1cecf3239a：1/1、零跳过，8.697秒；Logs/verify/rhythm/20261005-130646/report.md PASS，检查点失败0/异常0。真实State/Input完成两键达标、低分失败、重复消费拒绝、失焦取消、Combat不写自由纪录。
- 本轮四项 Showcase 各有通过证据，非一次全绿或全仓重跑。生产代码复核 PASS，新增资产 `.meta` 存在。
