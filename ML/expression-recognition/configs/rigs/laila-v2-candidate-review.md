# 当前17轴候选记录

2026-10-03 建档，2026-10-07 同步状态：唯一 `LailaRecognitionPlaytest.unity` 默认使用定向修复 59D，原 51D 五类候选保留为同场景对照，并保留上一版反馈 59D / 旧 r779 / 原 51D 对照。正式人工语义与拒识验收未完成；本页不派生下一轮训练。

唯一执行入口为 [训练规格 §17](../../../../PRP/laila-expression-recognition/spec.md#17-当前工程候选接手入口2026-10-04)→§16。专业基础继续保留 DESIGN、REFERENCES 和旧上限报告；旧 `laila_v1` 不覆盖。

- 当前 FBX SHA256：`86c77af278764973b27c7c6f7f0b86c2d5434c878cb0b63a6402cc0d74c8d136`。
- 输入按 §16.2 的17轴顺序。候选配置为 `laila_rig_v2_candidate.yaml`，名字明确带 candidate。
- 原始几何证据：`data/laila-calibration-20261003/geometry.json`、`axes-50.png`、`axes-100.png`。这些是临时静态烘焙网格在原相机材质下的检查，不等于GPU运行态组合/人工语义标注。
- 左眉Inner/Mid/Outer Up平均Y位移约4.15/5.62/5.61mm，右侧约5.27/6.04/5.15mm；Down含横向分量，不能称作纯AU4。平均位移不能作为AU强度测量。
- 画面L→角色Right继续作为候选约定；正式冻结前逐轴核对当前几何侧别。
- Inner正向偏内眉、Outer偏外眉、Mid为混合近似。共享内眉系数合计1只为防提前饱和，不是测得的FACS强度。
- 51维规范空间没有独立眉中基；三段Down归同侧browDown，仍丢失空间形状差异。需要用人工失败样本判断是否扩展规范表示，不伪称17轴全部无损。
- 嘴角In及下眼皮Down暂不赋予未证实语义；没有补noseSneer、jawOpen、cheekSquint或mouthPress。
- 单侧上唇分别驱动对应mouthUpperUp；当前左右分区已修复，但系数1仍是工程归一化约定。

候选纯合成ResMLP：`artifacts/runs/laila_cand_v2_s42_20261003`。只跑种子42，默认预算，五类+不可达变体剔除；该合成基线阶段没有人工训练／开发／锁测；随后r696单人dev微调另见spec§17，不作为独立语义验收。准确率不代表玩家脸部判断。

对照脚本：`analysis/laila_v2_candidate/run_comparison.py`。同一资产/候选映射，将眉三段锁为同侧同方向、上下唇左右锁为相同值，得到12轴。它不是旧FBX对新版FBX，也不是独立人类对照。配置、hash、两份完整metrics与135输入fixture在 `artifacts/laila_v2_candidate/`；相同种子/预算/类别/变体过滤规则，投影后的输入不同，单种子差异不能说明17维一定更准。

正式冻结仍需：逐轴/组合几何复核、代表玩家表达的独立盲标、按近邻组拆分开发/锁定测试、真实分布拒识校准。保持原始七类标签，暂以五类验收；恐惧/惊讶合并掩盖的混淆应单独诊断。
