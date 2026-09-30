# 捏脸表情识别网络（exprnet）

玩家在游戏里拖捏脸滑杆，网络判断这张脸「读起来是哪种表情」（默认 7 类基本情绪：中性、高兴、悲伤、惊讶、恐惧、厌恶、愤怒，类别可配置，也可用类别集合并 / 去掉部分类），并能判「认不出」。模型导出成 ONNX（opset 15），给 Unity Sentis 2.1.3 在客户端离线推理。

- 设计：[DESIGN.md](DESIGN.md)（需求与设计决定以它为准）
- 参考文献与数据集授权核对：[REFERENCES.md](REFERENCES.md)
- 本目录是独立的 Python 子项目，不含任何 Unity 侧工作。

## ⚠ 授权警告

| 训练数据 | 导出元数据 `commercial_use_allowed` | 能不能随包发布 |
| --- | --- | --- |
| 只用 FACS 原型合成数据 | `true` | 可以（数据是自己合成的） |
| 混入任何公开集（RAF-DB、FER2013、AffectNet、CK+、KDEF、JAFFE……） | `false` | **不行**，只能做内部原型 |

- 所有公开表情数据集都**仅限非商用研究**，多数还禁止再分发（详见 REFERENCES.md §7）。
- **FER2013 更特殊：它没有官方许可证**。图片是从网络搜索抓来的，版权来源不明，风险高于其余几个「持证、限非商用」的数据集；Kaggle 镜像页自标的 CC0 等只是上传者个人声明，不构成授权。
- 上线前必须换成自采数据（真人摆拍 + 签授权）或纯合成数据重训。
- 公开集原图、抽出来的特征（`data/`）、训练产物（`artifacts/`）都已被本目录的 `.gitignore` 忽略，**不要提交进仓库**。

## 目录

```
configs/
  labels.yaml          类别表 + 标签别名 + 各公开集官方编号顺序
  canonical.yaml       规范空间 51 维（ARKit 表情基）：区域、左右侧、左右配对、默认屏蔽的 eyeLook* 8 维
  facs.yaml            AU→表情基映射、各情绪的 AU 原型变体（逐条标出处：emfacs / ckplus_min / ckplus_fig1）、强度档、
                       干净稀疏样本比例（clean_fraction，默认 25%）、负样本
  rigs/sample_rig.yaml 示例绑定（20 根滑杆，仅跑通流程；live2d 字段是历史参照，Live2D 路线已废弃）
  label_sets/          类别集：laila_5class.yaml（laila 验收五类，规格 §15.1）
  train.yaml           训练超参
  sentis_ops.txt       Sentis 2.1 支持的 ONNX 算子白名单（附出处 URL 与抓取日期）
golden/sample_rig.json 金标捏脸集占位（与合成器同源，不能证明泛化）
analysis/laila_upper_bound/  laila 12 维输入的可分性上限分析（REPORT.md）与两份分析用候选绑定（不是正式绑定）
exprnet/               代码（见下）
tests/                 pytest，不联网、不需要数据集（test_laila_contract.py 是 laila 12 维契约测试）
pytest.ini             让 pytest 以本目录为根（缓存不落到仓库根）
requirements.txt       训练 / 导出 / 测试依赖
requirements-extract.txt  抽特征依赖（mediapipe、opencv）
```

`exprnet/` 模块：`common`（路径、配置、类别表、类别集）、`canonical`（规范空间）、`rig`（绑定前向、有界 NNLS 投影、配置哈希）、`facs`（合成器 + 负样本 + 按绑定判变体做不做得出来）、`datasets`（公开集读取、特征缓存 npz、基线归一化、划分、拼训练数据）、`extract`（MediaPipe → npz）、`augment`、`models`（regionformer / resmlp）、`losses`、`train`、`calibrate`、`evaluate`、`export`、`coverage`、`golden_check`（金标护栏）。

## 环境

Windows + Python 3.12 验证过。以下命令都**在本目录（`ML/expression-recognition/`）下**运行：

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt --extra-index-url https://download.pytorch.org/whl/cpu
# 只有要从图片抽特征时才需要：
.venv\Scripts\python -m pip install -r requirements-extract.txt
```

- torch 用 CPU 版就够：模型只有几万到十几万参数，合成数据训一次 CPU 上 3 分钟左右。
- 在仓库根目录运行也行，把本目录加进 `PYTHONPATH`：`PYTHONPATH=ML/expression-recognition ML/expression-recognition/.venv/Scripts/python -m exprnet.train ...`（Git Bash 写法）。
- 输出重定向到文件时中文乱码，可设 `PYTHONIOENCODING=utf-8`。

下文的 `python` 都指 `.venv\Scripts\python`。

## 命令

每个子命令都有中文 `--help`。

### 测试

```powershell
python -m pytest tests -q
# 在仓库根目录：ML/expression-recognition/.venv/Scripts/python -m pytest ML/expression-recognition/tests -q
```

不联网、不需要数据集。`tests/test_mediapipe_optional.py` 只有在装了 mediapipe、`.cache/face_landmarker.task` 已存在时才跑，否则自动跳过。`tests/test_laila_contract.py` 里检查正式绑定 `configs/rigs/laila_rig.yaml` 的那一条，在文件不存在时跳过（等阶段门 B 几何校准定稿），跳过理由会打印出来；pytest 加 `-rs` 可以看到。

### 训练 `python -m exprnet.train`

```powershell
python -m exprnet.train --name rf_synth                       # 默认：regionformer + 合成数据 + 示例绑定
python -m exprnet.train --name mlp_synth --model resmlp       # 基线
python -m exprnet.train --name rf_mix --npz data/features/rafdb.npz data/features/ckplus.npz   # 混入公开集
python -m exprnet.train --name rf_id --rig none --golden none # 单位绑定（输入就是 51 维）；sample_rig 金标对不上，不评金标
# laila 候选（分析用候选绑定，不是正式的 laila_v1）+ 五类类别集 + 按绑定剔除做不出来的变体：
python -m exprnet.train --name laila_cand_opt_5c_resmlp --model resmlp --golden none --rig analysis/laila_upper_bound/laila_candidate_optimistic.yaml --label-set laila_5class --drop-infeasible-variants
```

常用参数：`--epochs`、`--n-per-class`（合成数据每类样本数）、`--seed`、`--golden`、`--no-synthetic`、`--no-cache`。其余超参改 `configs/train.yaml`。

| 参数 | 作用（默认都关，不传时与原先完全一样） |
| --- | --- |
| `--label-set <名字或路径>` | 按类别集训练（DESIGN §5.1）：写名字时找 `configs/label_sets/<名字>.yaml`；合并类的合成样本由成员类各出一半，集外类不进正样本；`none` = 默认七类 |
| `--drop-infeasible-variants` | 按训练绑定剔除做不出来的合成变体（DESIGN §3.1）：核心 AU 投影后相对残差 ≥ 阈值的变体不进训练目标，带 choose 的逐个备选判断；剔除清单写进 ckpt 与 report.md。单位绑定下不剔除任何变体（日志会写明） |
| `--infeasible-threshold X` | 变体剔除的相对残差阈值，默认 0.9（`configs/train.yaml` 的 `infeasible_variants`） |
| `--allow-golden-skip` | 金标绑定名 / `rig_hash` 与训练绑定不符时降级为跳过（默认报错退出，且在训练开始前就报） |

训练结束自动做：温度缩放（验证集 LBFGS 拟合 T）→ energy 阈值 → 评估。产物在 `artifacts/runs/<名字>/`。训练开始前先查金标（绑定、值、标签、id，见下文「金标检查」），不合格直接报错退出，不白训。
试验性训练（候选绑定、临时类别集）的产物名要带 `cand` 之类的字样，不要用 `laila_v1` 这类正式名字，免得被当成正式模型。

energy 阈值默认按 **TNR95** 定（DESIGN §6）：另生成一批**标定用负样本**（怪脸，数量同留出集、种子不重叠），取它们 energy 的第 5 百分位，即 95% 的怪脸被拒；**留出负样本**不参与训练和定阈值，只用来报 AUROC、FPR95 和拒识率（单靠 energy 的与加上 min_confidence 的）。旧规则「分布内验证集 95% 通过」（ID95）同时算出来写进报告作对照——合成数据的分布内 energy 很集中，按 ID95 定的阈值会把稍有偏离的正常脸误拒。report.md 与导出元数据里都列出两种阈值下的验证集通过率、留出负样本拒识率与金标通过率。规则可在 `configs/train.yaml` 的 `calibration.threshold_rule` 切换。


| 文件 | 内容 |
| --- | --- |
| `ckpt.pt` | 权重、类别、类别集（`label_set`，默认 None）、变体剔除清单（`variant_filter`）、规范空间维度与屏蔽维、T、energy 阈值（含规则与两种规则的阈值）、训练绑定与哈希、数据来源 |
| `config.yaml`、`config_snapshot/` | 实际生效的配置与 labels / canonical / facs / 绑定 / 类别集的原样拷贝 |
| `metrics.json`、`report.md` | 验证集 acc、macro-F1、逐类 P/R/F1、混淆矩阵、最终判定口径（`val_final`）、ECE（校准前后）、OOD AUROC / FPR95、金标逐条结果与最终判定口径、训练目标剔除的变体、训练曲线 |
| `eval_data.npz` | 验证集与留出负样本，供 `evaluate` 重评 |

有绑定时，训练样本按 DESIGN §3.2 做绑定投影：每个样本解一次有界非负最小二乘，结果缓存在 `.cache/projection/`，每个 batch 以 70% 概率取投影版、30% 取原值。验证集指标默认是「投影后」的部署视角。

### 评估 `python -m exprnet.evaluate`

```powershell
python -m exprnet.evaluate --run artifacts/runs/rf_synth
python -m exprnet.evaluate --run artifacts/runs/rf_synth --npz data/features/ckplus.npz   # 额外评外部集
python -m exprnet.evaluate --run artifacts/runs/<名字> --golden golden/laila_dev.json --tag dev    # 写 metrics_dev.json / report_dev.md
python -m exprnet.evaluate --run artifacts/runs/<名字> --golden golden/laila_test.json --tag test  # 与开发集的评估互不覆盖
```

不给 `--tag` 时重写 `metrics.json` 与 `report.md`（行为同原先）；给了写 `metrics_<tag>.json` / `report_<tag>.md`，训练时的 `metrics.json` 不动（tag 只能用字母、数字、`_`、`-`）。外部 npz 有官方 test 划分时只评 test；类别集模型的外部集标签按类别集映射，集外类丢弃。`--golden none` 表示不评金标。

- **金标护栏**：金标的绑定名（或 `rig_hash`）与模型训练绑定不符时**报错退出**（返回 2，不写任何报告），不再静默跳过；只有加 `--allow-golden-skip` 才降级为跳过，并在报告的金标一节写明「已跳过」。滑杆值、标签、id 不合法一律报错，检查项见下文「金标检查」。
- **最终判定口径**（规格 §11.2）：验证集与金标都另报按阈值最终判定的指标：混淆矩阵带「认不出」列（金标另有 unknown 真值行），已知类判对率、macro-F1、逐类召回按最终判定算（被拒识计为漏判），已知类被拒识比例与 unknown 拒识率单独报。
- **类别集模型**：金标仍存七类标注，评估时按 `from` 映射（surprise、fear → surprise_fear）；集外类（disgust）的样本单独列出预测去向，不计入任何指标。
- **金标状态**：文件 `status` 为 `human-labeled` 时报告标「人工盲标」，否则标「占位，不能证明泛化」。

### 导出 `python -m exprnet.export`

```powershell
python -m exprnet.export --run artifacts/runs/rf_synth                         # 用训练时的绑定
python -m exprnet.export --run artifacts/runs/rf_synth --rig configs/rigs/my_rig.yaml --name rf_my_rig   # 换绑不用重训
python -m exprnet.export --run artifacts/runs/rf_synth --identity              # 单位绑定版（输入 51 维）
```

产物 `artifacts/export/<名字>.onnx` + 同名 `.json`。三项自检（任一不过就判失败，产物改名为 `*.failed.*`，进程返回 1）：

1. onnxruntime 与 PyTorch 在 1000 个随机输入上的最大绝对误差 < 1e-5；
2. 图里所有算子都在 `configs/sentis_ops.txt` 里，opset 为 15；
3. 文件 < 1 MB。

ONNX 契约（DESIGN §8）：输入 `sliders [batch, K]` float32（顺序见元数据 `sliders`）；输出 `probs [batch, C]`（已温度校准）、`energy [batch]`。游戏侧判定：`energy > energy_threshold` 或 `max(probs) < min_confidence` 判「认不出」，否则取 argmax。元数据还包括阈值规则与标定说明（`energy_threshold_rule`、`threshold_calibration`）、类别、规范空间维度顺序、屏蔽维、绑定配置哈希（改绑定必变）、训练数据来源、`commercial_use_allowed`、指标摘要与自检结果。

- `onnx.sha256`：最终写盘的 ONNX 文件字节的 SHA-256（规格 §7.4），防同名模型与 JSON 错配；导出日志里也打印。Python 侧可用 `exprnet.export.verify_pair(onnx, json)` 核对一对产物。
- 类别集模型：`labels` 就是类别集里的类（顺序即 probs 顺序），另有 `label_set` 块写类别集名、各类的 `from` 与集外类；默认七类不写这个块。
- 训练时打开了变体剔除的，另有 `training_variant_filter` 块（阈值、绑定与哈希、剔除清单）。

### 金标检查 `python -m exprnet.golden_check`

```powershell
python -m exprnet.golden_check --dev golden/laila_dev.json --test golden/laila_test.json
python -m exprnet.golden_check --dev golden/laila_dev.json --test golden/laila_test.json --rig configs/rigs/laila_rig.yaml
python -m exprnet.golden_check --dev golden/sample_rig.json --run artifacts/runs/mlp_synth   # 用训练产物的绑定与标注类
```

不给 `--rig` / `--run` 时，按金标文件的 `rig` 字段在 `configs/rigs/*.yaml` 里找同名绑定。逐个文件检查，再查跨集；全部通过返回 0，有问题返回 1，找不到绑定返回 2：

- `schema_version` 为 1，`entries` 非空；`rig` 等于绑定名，带 `rig_hash` 时等于绑定的完整哈希（`Rig.config_hash()`）；
- 每条 `id` 非空、不重复（跨集也不许重复）；`expect` 是标注类（`configs/labels.yaml` 的七类）、`unknown`，或 unknown 的别名 `ambiguous`（标注协议的「不明确 / 混合」，复核后按 unknown 算）；
- `sliders` 的 key 都在绑定里，值是有限数（拒绝 NaN / Infinity / 字符串 / 布尔）且在滑杆范围内；
- 带 `group` 字段时，同一组不许同时出现在开发集与测试集（规格 §6.2 按组划分）；报告里列出每个文件的状态、各标签条数和没写 group 的条数。

金标文件格式（沿用 `golden/sample_rig.json`，规格 §6.3）：

```json
{"schema_version": 1, "rig": "laila_v1", "rig_hash": "<可选，绑定的完整 64 位哈希>", "status": "human-labeled",
 "entries": [{"id": "laila_neutral_001", "expect": "neutral", "sliders": {}, "group": "<可选，近邻组>", "note": "…"}]}
```

省略的滑杆按默认值 0。`status` 只有写 `human-labeled` 才算人工盲标。

### 绑定覆盖度报告 `python -m exprnet.coverage`

```powershell
python -m exprnet.coverage --rig configs/rigs/sample_rig.yaml
python -m exprnet.coverage --rig configs/rigs/sample_rig.yaml --npz data/features/ckplus.npz
```

把样本投影到绑定可达子空间，报告：没有滑杆驱动的表情基、覆盖差的表情基、逐类残差（哪种表情捏不出来）、逐滑杆使用率与顶到边界的比例。产物在 `artifacts/coverage/<绑定名>/`。

### 抽特征 `python -m exprnet.extract`

```powershell
python -m exprnet.extract --source fer2013 --input data/raw/fer2013/fer2013.csv --out data/features/fer2013.npz --download-model
python -m exprnet.extract --source rafdb   --input data/raw/rafdb   --out data/features/rafdb.npz
python -m exprnet.extract --source ckplus  --input data/raw/ckplus  --out data/features/ckplus.npz
python -m exprnet.extract --source kdef    --input data/raw/kdef    --out data/features/kdef.npz
python -m exprnet.extract --source jaffe   --input data/raw/jaffe   --out data/features/jaffe.npz
python -m exprnet.extract --source affectnet --input data/raw/affectnet --out data/features/affectnet.npz
python -m exprnet.extract --source imagefolder --input data/raw/xxx --out data/features/xxx.npz --dataset-name xxx
```

- 用 MediaPipe Face Landmarker（Apache-2.0）输出的 blendshape，**按名字**对齐到规范空间 51 维（Tasks API 的 52 项里多出的是 `_neutral`，不是 `tongueOut`）。
- 模型文件 `face_landmarker.task` 放 `.cache/`，加 `--download-model` 自动下载。
- 每次抽取都打印并写出检出率（总体与逐类），同时写 `<out>.extract.json`。FER2013 默认先放大 4 倍（48→192）再检测，RAF-DB 对齐图默认放大 2 倍，可用 `--upscale` 改。
- 试跑加 `--limit 200`。

## 公开集：去哪下载、放在哪

全部放在 `data/raw/<名字>/`（已被忽略）。原型阶段优先用**摆拍**数据（CK+、KDEF、JAFFE）评估，它们比自然场景数据更接近「刻意捏出来的表情」。

| 数据集 | 获取 | 放置方式 | 抽取 `--source` |
| --- | --- | --- | --- |
| CK+ | 向 CMU 申请并签协议：<http://vasc.ri.cmu.edu/idb/html/face/facial_expression/> | `data/raw/ckplus/cohn-kanade-images/S005/001/*.png` 与 `data/raw/ckplus/Emotion/S005/001/*_emotion.txt` | `ckplus`（末帧 = 该情绪，首帧 = 中性，按受试者划分） |
| KDEF | 官网注册下载：<https://kdef.se/> | `data/raw/kdef/` 下任意层级，文件名保持原样（如 `AF01ANS.JPG`） | `kdef`（默认只取正面 S） |
| JAFFE | <https://www.kasrl.org/jaffe.html> | `data/raw/jaffe/` 下，文件名保持原样（如 `KA.AN1.39.tiff`） | `jaffe` |
| RAF-DB | 邮件联系作者获取：<http://www.whdeng.cn/RAF/model1.html> | `data/raw/rafdb/EmoLabel/list_patition_label.txt` 与 `data/raw/rafdb/Image/aligned/`（或 `Image/original/`） | `rafdb`（官方 train/test 划分） |
| AffectNet | 官网申请：<http://mohammadmahoor.com/affectnet/> | `data/raw/affectnet/Manually_Annotated_file_lists/{training,validation}.csv` 与 `Manually_Annotated_Images/` | `affectnet`（官方 validation 当 test） |
| FER2013 | Kaggle「Challenges in Representation Learning: Facial Expression Recognition Challenge」的 `fer2013.csv`（**无官方许可证，见上方警告**） | `data/raw/fer2013/fer2013.csv`；若拿到的是 `train/<标签>/`、`test/<标签>/` 图片目录版，用 `imagefolder` | `fer2013`（按 Usage 列划分） |

各数据集的官方标签编号顺序写在 `configs/labels.yaml`，已逐一核对；未启用的类别（如轻蔑、AffectNet 的 None/Uncertain/Non-Face）抽取时保留、训练时丢弃。

## 改绑定（策划定下真实滑杆之后）

1. 复制或修改 `configs/rigs/sample_rig.yaml`（写法见文件头注释；范围必须包含 0，默认值为 0）。
2. `python -m exprnet.coverage --rig <新绑定>` 看覆盖度，把「捏不出来」的表情反馈给捏脸设计。
3. `python -m exprnet.export --run <训练产物> --rig <新绑定> --name <名字>` 重新导出，元数据里的绑定哈希会随之改变。分类网络只认规范空间，**不用重训**；不过训练时的绑定投影增强用的是旧绑定，绑定变化大时建议用新绑定重训一次。
4. 为新绑定手写 / 采集金标集 `golden/<绑定名>.json`（`rig` 字段要等于绑定的 `name`，建议再写 `rig_hash`），用 `python -m exprnet.golden_check` 检查通过后再评估。旧绑定的金标拿去评新绑定的模型会报错退出，不会静默跳过。

## 已知限制

- 合成数据的训练集和验证集同源，金标也由同一套 FACS 先验手写，合成数据上的高分只能证明流程跑通，不能证明泛化。
- EMFACS 原型里的部分「其他原型 / 主要变体」来自二手转引（`facs.yaml` 里带 note 的条目），原书未逐字核对。
- AU11、AU23、AU25 在 ARKit 里没有对应的表情基，映射是近似的。
- 导出用的是 torch 的旧版 TorchScript 导出器（`dynamo=False`），torch 2.14 仍可用但已标为弃用。
- 还没在 Unity Sentis 真机上加载验证（DESIGN §10，本期不动 `Packages/manifest.json`）。
