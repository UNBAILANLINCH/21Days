# 莱拉候选：映射审计、失败重放与盲标准备

2026-10-03 的诊断工具说明。命令离线读取采集记录，输出诊断与未标注包。
复用当前17轴候选与现行五类模型；不是正式语义校准或人工验收。
CLI输出限定于artifacts/laila_v2_candidate，避免诊断参数误覆盖原采集、部署模型或配置。

在 `ML/expression-recognition` 运行，使用已有 `.venv/Scripts/python.exe`，设置 `$env:PYTHONPATH='.'`。

```powershell
# 映射审计；输出至已有忽略范围artifacts，不改候选yaml
.venv/Scripts/python.exe analysis/laila_v2_candidate/capture_diagnostics.py audit

# 真实开发采集的离线重放；替换UUID为实际文件名
.venv/Scripts/python.exe analysis/laila_v2_candidate/capture_diagnostics.py diagnose --capture data/laila-captures-v2/dev/<UUID>.json --out artifacts/laila_v2_candidate/capture-diagnostics/<UUID>.json

# 仅在已有真实采集后生成一次性盲标包；输出存在时拒绝覆盖
.venv/Scripts/python.exe analysis/laila_v2_candidate/capture_diagnostics.py blind --out artifacts/laila_v2_candidate/blind-packet-001
```

## 输入与诊断证据

Unity菜单为 `21Days → Laila → 采集识别样本（未标注）`。Play时选择场景的识别对象，填中性近邻组名如g001、dev划分，采集当前脸与17轴快照。采集PNG不含预测UI。

诊断拒绝缺轴/缺形态、非有限数、越界、同轴正反同时激活、轴与权重不一致、绑定或FBX哈希不匹配，以及test开发诊断。记录原始17值、31权重、完整51维裁剪前映射/裁剪后映射/屏蔽后的网络输入、checkpoint logits、实际ONNX概率与energy、两条拒识规则是否触发和距离边界。模型/元数据/checkpoint/采集/图片均记录哈希。checkpoint重算与实际部署ONNX误差须小于1e-5，否则失败；不能把离线重放写成Unity实时测量。

截图里眉抬眼睁嘴张仅提供视觉线索，无法反推出精确权重。该已讨论失败属于dev，其近邻同组留在dev。没有原始JSON时不生成假失败配方；没有真实采集时blind命令报错。

历史中性smoke采集记录source为unity-player-capture，新菜单写unity-editor-play-capture；两者均保留原始字段。工具仅核查数值/资产契约，不能仅凭source字符串证明真实Player或采集环境，报告明确capture_environment_verified=false。该中性smoke不能冒充用户受惊失败，也不能冒充人工金标。

## 人工标注和分组

生成包中的 `annotators/` 供独立标注使用；`steward-manifest.json` 含group/split/hash，只给数据管理员。三个CSV保持标签空白，以原始七类 `neutral,happy,sad,surprise,fear,disgust,angry` 或 `ambiguous` 标注；clarity/note供记录清晰度与分歧，不自动推测标签。

管理员保留每人原始标注，按项目协议复核一致意见、混合/不明确和分歧，不丢掉难例。现脚本没有自动汇总标签或写human-labeled金标，避免仅凭空表或多数票冒充完成人工复核。正式金标沿用 `exprnet.golden_check` 契约；转换时保留原始七类、group、rig_hash，不先合并surprise/fear。

所有强度变化、镜像、近邻参数共用同一个group并整组划分。工具检查重复id和跨划分group；匿名文件不能消除重复/近邻泄漏，人工需复核组定义。锁定test不参与修映射、训练、选类别或定阈值。样本预算与正式协议尚未确定；报告独立组数，不将同配方多张脸计为独立证据。

## 映射审计的边界

`audit` 输出各有效方向 25/50/75/100% 的通道、被抹掉的半轴、共线通道及不可达维度。51D 映射中，内眉左右共享、三段 Down 归同侧通道，Mid 由内外眉近似；下眼睑 Down 和嘴角 In 未映射，jawOpen 不可达。共享系数是工程约定，局部共线不能证明整体惊讶／恐惧不可分。

本工具针对原 51D 采集契约，59D 候选与试玩状态见 [模块指南](../../../../ai-docs/docs/modules/lailafacerecognition/lailafacerecognition-module-guide.md)。几何或绑定版本变更需要明确区分旧样本；不得改旧快照 hash 强行混用。当前没有确定六类部署或新增采样计划。
