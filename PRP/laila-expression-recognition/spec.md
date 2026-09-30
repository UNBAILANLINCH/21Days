# laila 捏脸表情识别接入规格

版本：v0.2；日期：2026-09-30；状态：阶段门 A 已通过（审阅结论与契约改动见 §15），尚未执行。v0.1 为 2026-09-29 初稿。

本文是用户要求的技术 spec，不是实现完成报告，也不同时生成执行任务清单。文中的新文件、组件、命令产物和验收门槛均为拟实施内容；现有源码与接口事实单独标注。

## 1. 目标与边界

### 1.1 玩家可见结果

玩家在现有 laila 脸部控制区捏出表情，点击“识别当前表情”，获得：

- 表情名称：中性、高兴、悲伤、惊讶、恐惧、厌恶、愤怒之一。
- 该类的模型概率，并可展开全部类别概率。
- 超过拒识阈值时显示“认不出”，而不是硬选一个类别。
- 模型或输入故障时显示“识别不可用”，不能把故障伪装成“认不出”。

这里识别的是角色表情参数的语义，不是真人心理状态，不是身份识别，也不是摄像头识别。概率是模型输出，不等于已证明可靠的人类判断置信度。

### 1.2 本期范围

复用已有 Python 原型、已有 22 个 BlendShape、已有 12 个控制区；增加真实绑定、参数采集、模型训练与导出、Unity 离线推理和验证。

第一版采用按钮触发，不增加每帧推理或自动后台任务。验收通过后，若需要松手自动识别，再评审现有拖拽事件的接入，不能为此新建另一套输入系统。

### 1.3 不做

- 不改 Blender / FBX，不重建或挪动控制区，不联动牙齿。
- 不改眼球旋转、眼球 / 角膜 / 牙齿 / 脸部材质、相机和共享 URP 配置。
- 不做图片渲染分类、MediaPipe 运行时检测、网络请求或 Python 常驻服务。
- 不加入关卡判定、对白、存档或确定性回放；识别结果本期仅供原型展示。
- 不重写训练框架，不新增网络结构，不预建模型管理服务、推理接口工厂或远程更新系统。

## 2. 已确认的工程事实

| 项目 | 已检查的事实 | 依据 |
| --- | --- | --- |
| 原型提交 | `2b1d488`，2026-09-28，已包含在当前 main | Git 历史 |
| Python 实现 | 已有 RegionFormer、ResMLP、合成器、训练、评估、校准与 ONNX 导出 | `ML/expression-recognition/exprnet/` |
| 默认类别顺序 | neutral、happy、sad、surprise、fear、disgust、angry | `configs/labels.yaml` |
| 原型规范空间 | 51 维；默认屏蔽 eyeLook 视线维度 | 原型 README / DESIGN |
| 原型绑定 | sample_rig，20 个示例参数，不等于当前 laila | `configs/rigs/sample_rig.yaml` |
| 当前模型 | 扩展 FBX，22 个形态；上唇 / 下唇只各有一个开口键 | laila 模块指南与拖拽源码 |
| 参数读取 | 已有 HasShape、GetWeight、GetSignedPair、RebuildCache | `FaceBlendShapeController.cs` |
| 眼球 | FaceDragHandle 旋转 Pivot，不通过 BlendShape | `FaceDragHandle.cs` |
| Unity 推理接入 | 当前 manifest / Game.Runtime 未引用 Sentis，未找到识别接入代码 | 包与源码检查 |
| 本地部署产物 | 默认 artifacts 目录不存在；Assets 中未找到 ONNX | 文件检查；不排除产物被保存在其他目录 |

当前 12 个控制区不等于 12 个交互轴：8 个原控制区各有一个轴，左右嘴角各额外一个横轴，双唇各一个轴，双眼视线各两个轴，共 16 轴。排除 4 个视线轴后，分类输入为 12 维。22 是形态键数量，不是此次模型输入维数。

现有 LailaFace 脚本位于 Runtime/Gameplay，命名空间却为 Game.LailaFace，已有静态检查违规。本期不顺手迁移这些文件；新增代码使用与目录相符的命名空间，单独报告旧问题。

## 3. 数据流与最小架构

```text
现有拖拽 → 当前脸部 22 个权重
                  ↓ 公开读取接口，只读采样
             12 维参数快照
                  ↓ 按 metadata.sliders 的顺序打包
             ONNX 输入 sliders
                  ↓ 图内：范围裁剪 / 正负拆分 / 绑定 / 屏蔽 / 分类 / 校准
             probs + energy
                  ↓ 纯 C# 阈值判定
             结果文本与调试数值
```

Python 负责训练和把绑定固化进 ONNX；Unity 不再实现一份“12 维 → 51 维”的矩阵运算，不再次 softmax，也不再次除以温度。Unity 只负责采样、输入契约检查、运行模型和显示结果。

识别实现属于 Runtime 层，不进入 Game.Core。不依赖 Editor / Tests，不查询全场景中的私有组件字段。场景显式序列化引用当前可见脸部的 FaceBlendShapeController。

## 4. 12 维输入契约

### 4.1 固定 key、顺序与读取来源

定义 `P(A,B) = (GetWeight(A) - GetWeight(B)) / 100`，即现有 GetSignedPair；定义 `W(A) = GetWeight(A) / 100`。

| 索引 | key | 范围 | 来源 / 公式 |
| --- | --- | --- | --- |
| 0 | brow_L_y | [-1,1] | P(Brow_L_Up, Brow_L_Down) |
| 1 | brow_R_y | [-1,1] | P(Brow_R_Up, Brow_R_Down) |
| 2 | eye_L_upper_y | [-1,1] | P(Eye_L_UpperLid_Up, Eye_L_UpperLid_Down) |
| 3 | eye_L_lower_y | [-1,1] | P(Eye_L_LowerLid_Up, Eye_L_LowerLid_Down) |
| 4 | eye_R_upper_y | [-1,1] | P(Eye_R_UpperLid_Up, Eye_R_UpperLid_Down) |
| 5 | eye_R_lower_y | [-1,1] | P(Eye_R_LowerLid_Up, Eye_R_LowerLid_Down) |
| 6 | mouth_corner_L_y | [-1,1] | P(Mouth_L_Up, Mouth_L_Down) |
| 7 | mouth_corner_R_y | [-1,1] | P(Mouth_R_Up, Mouth_R_Down) |
| 8 | mouth_corner_L_x | [-1,1] | P(Mouth_L_Out, Mouth_L_In) |
| 9 | mouth_corner_R_x | [-1,1] | P(Mouth_R_Out, Mouth_R_In) |
| 10 | upper_lip | [0,1] | W(Mouth_UpperLip_Up.001) |
| 11 | lower_lip | [0,1] | W(Mouth_LowerLip_Down.001) |

全部默认值为 0。嘴角 X 正值表示各自向外拉，不是同一个屏幕方向；读取实际权重，不读取指针位移或 FaceDragHandle 的起始拖拽值。下唇正值表示下拉张开，不使用拖拽屏幕 Y 的负号作为模型输入。

### 4.2 不丢失错误的采样规则

现有 GetWeight 在名称不存在时返回 0，因此只调用 getter 不足以区分“中性”与“引用错误”。采样器必须先验证全部 22 个必需名称存在。

初始化在现有控制器 Awake 后进行，必要时通过公开 RebuildCache 建缓存。不得把 EditMode 缓存未初始化读到的 0 当作模型没有形态。

每次采样：

1. 验证脸部引用有效且是当前可见模型；所有必要名称齐全。
2. 检查每个原始权重有限，拒绝 NaN / Infinity。
3. 权重允许浮点容差：[-0.001,100.001] 内裁剪到 [0,100]；超出则返回 InvalidInput，不静默修正明显错误。
4. 每对互斥形态若两侧均大于 0.001，返回 PairConflict，不用两值相减掩盖复合形变。
5. 按表计算 12 个值，再按元数据 key 顺序写入复用的 float[12]。

成功采样不得改变权重、Renderer、Pivot、材质、碰撞体或用户位置参数。不支持当前拖拽系统之外“同一对 Up / Down 同时激活”的自定义表达；真有该需求时需升级输入契约，不能继续沿用有损的 12 维表示。

### 4.3 元数据顺序

本版导出顺序固定为表中顺序。运行时逐项比较元数据 key 与该顺序，不能依赖字典遍历、枚举值、Hierarchy 顺序或 FBX 的索引。

初始化检查：key 无重复、顺序恰为上述 12 个、范围 / default 一致、rig.name 为 `laila_v1`。未知 key、缺失 key、重排或新增维度均拒绝加载，不以 0 补齐。仅重排 JSON 而不重导出 ONNX 会让参数与图内矩阵错位，不能把这种操作当作合法修改；需要新顺序时升级契约并重新导出。

## 5. 真实绑定与几何校准

### 5.1 文件与公式

拟新增 `ML/expression-recognition/configs/rigs/laila_rig.yaml`，name 为 `laila_v1`。复用 rig.py 的正负半轴绑定和有界投影，不修改 sample_rig，也不伪造当前模型不存在的控制项。

现有实现：

```text
s = clip(sliders, min, max)
x = clip(relu(s) @ W_pos + relu(-s) @ W_neg, 0, 1)
```

上 / 下唇只有 pos，不写 neg。眼球不列入 sliders。左右规范表情基按角色解剖侧对齐；不能根据屏幕左右直接推断模型 L/R。

### 5.2 候选语义，不是已验证绑定

| 控制 | 候选规范表情基 | 必须确认的几何证据 |
| --- | --- | --- |
| 嘴角上 / 下 | mouthSmileLeft/Right、mouthFrownLeft/Right | 实际唇角相对 Basis 位移方向；不能仅按 Key 名称判为笑 / 悲伤 |
| 嘴角 Out | mouthStretchLeft/Right | 是否确为侧向展宽，而非整嘴平移 |
| 嘴角 In | 可能涉及 mouthPucker | 内收不必然等于噘嘴；没有唇前凸等证据时不启用此候选 |
| 眉上 / 下 | browOuterUpLeft/Right、browInnerUp、browDownLeft/Right | 整眉抬升与内眉抬升需区分，确认共享 browInnerUp 的叠加方式 |
| 上眼皮 Up / Down | eyeWideLeft/Right、eyeBlinkLeft/Right | 开口增大 / 缩小的实际方向与幅度 |
| 下眼皮 Up / Down | eyeBlink 或 eyeSquint、eyeWide | 方向可能与上眼皮相反；闭合与收紧不是同一语义 |
| 上唇 Up | mouthUpperUpLeft/Right | 是否上唇独立上提；不得直接冒充 jawOpen |
| 下唇 Down | mouthLowerDownLeft/Right | 是否下唇独立下拉；不因看起来张口就默认存在下颌运动 |

每个未验证系数先为 0，在绑定审阅完成前不得用这种临时绑定宣称识别可用。确有动作但规范基无法合理表达时记录映射限制，不凭空补 noseSneer、cheekSquint、lipPress 或 jawOpen。

### 5.3 校准操作与产物

在当前模型上逐项检查：Basis、单一形态 50 / 100、成对 ±1、左右同类组合、上下眼皮组合、双唇组合和嘴角双轴组合。保存正面截图及参数；用当前脸部屏幕朝向判动作，用模型 / 顶点信息核对角色侧。

每根参数记录：形态名称、正负方向、观察到的动作、选定规范基、满幅系数、无法表达的动作。两个控制驱动同一规范基时，先用合计不超过 1 的系数检查，避免一只眼刚动半程就饱和。具体系数由校准决定，本文不把 0.5 或 1.0 写成已确认值。

拟以 `ML/expression-recognition/configs/rigs/laila-binding-review.md` 保留上述证据、源 FBX SHA256 与最终 rig.config_hash。绑定定稿后，训练、校准、导出和评估必须用同一份 laila 绑定。

### 5.4 线性绑定的已知上限

现有 rig 的输出只能逐维非负相加再裁剪，不能表达“只有上下唇一起打开才激活 jawOpen”一类 AND / 乘积关系。第一版不私自添加派生 jawOpen 轴。若独立唇映射使张嘴表情系统性失败，先用盲标证据确认，再单独设计可测试的非线性映射，并保证 Python / ONNX 是同一公式；不能只在 Unity 临时补偿。

## 6. 参数采集与独立人类标注

### 6.1 采集工具

拟增加一个 Editor 采集窗口，明确绑定当前 FaceBlendShapeController，复用同一个公开采样器，不读取私有字段，不全场景 Find。人工点击“采集”，导出参数和当前 Game 相机截图；截图仅供人标，不作为网络输入。

原始产物放在已有忽略范围 `ML/expression-recognition/data/laila-captures/`。记录 sample id、12 个 key/value、原始 22 权重、rig hash、源 FBX hash、采集批次与近邻组 id；不记录本机绝对路径、用户名或设备身份。

采集不得永久修改相机、权重或视线。工具如使用临时 RenderTexture，必须 finally 恢复并释放。运行态采集优先；EditMode 截图需确认蒙皮已更新，不能把旧帧与新权重配成一个样本。

### 6.2 人工标注协议

本版建议至少 3 位标注者，独立查看截图，不看目标标签、模型预测或 FACS 配方。先选择七类之一或“不明确 / 混合”，再汇总；至少 2/3 一致才进入确定标签集合。“不明确 / 混合”经复核后作为 unknown，不能把所有左右不对称脸自动标成怪脸。

开发集用于修绑定和阈值，锁定测试集不参与这些调整。相同脸的轻微参数变化、左右镜像和同一配方的不同强度归为同一组；按组划分，不能近邻样本跨开发 / 测试集。

建议的原型最小规模：开发集每类 10 个一致标注样本 + 20 个 unknown；锁定测试集每类 20 个 + 40 个 unknown。即开发集 90 个、测试集 180 个，均为目标数量，不是已有数据。实际达不到某类数量时，该类验收未完成，不拿合成样本补充冒充盲标样本。

### 6.3 兼容现有 golden 格式

拟新增 `golden/laila_dev.json` 与 `golden/laila_test.json`。格式沿用现有 evaluate_golden：

```json
{
  "schema_version": 1,
  "rig": "laila_v1",
  "status": "human-labeled",
  "entries": [
    {
      "id": "laila_neutral_001",
      "expect": "neutral",
      "sliders": {},
      "note": "全零快照；标签必须由独立标注确认"
    }
  ]
}
```

省略的参数按绑定 default=0；其他样本记录完整 12 值。标注分歧、组 id、数据划分和 hash 可作为附加字段保留，但不能宣称现有评估器已经验证这些字段。

实施时扩展测试 / 评估入口检查 rig hash、有限值、参数范围、标签合法性、id 唯一及组不跨集。现有评估器仅检查 rig 名称和未知参数，不足以提供上述护栏。

## 7. 训练、阈值与导出

### 7.1 环境与默认选择

复用独立 Python 子项目；不在 Unity 中训练。先用 ResMLP 基线与 FACS 合成数据，保留现有 RegionFormer 作为后续同集对照，不因其名字更复杂就默认更好。

不自动下载公开图片集，不混入来源不明的数据。沿用原型的 commercial_use_allowed 护栏；该字段是流水线标记，不代替对来源和授权的审查。

以下 PowerShell 命令均在 `ML/expression-recognition/` 内执行，属于将来实施步骤，本次文档编写没有运行：

```powershell
# 仅在本地环境不存在时创建 / 安装；已有环境先核对依赖。
python -m venv .venv
.venv\Scripts\python.exe -m pip install -r requirements.txt --extra-index-url https://download.pytorch.org/whl/cpu

.venv\Scripts\python.exe -m pytest tests -q
.venv\Scripts\python.exe -m exprnet.coverage --rig configs/rigs/laila_rig.yaml

# 尚无真实标注集时先明确不评金标；不要误用 sample_rig 的占位集。
.venv\Scripts\python.exe -m exprnet.train --name laila_resmlp_v1 --model resmlp --rig configs/rigs/laila_rig.yaml --golden none --seed 42

# 开发集 / 测试集创建并通过数据护栏后，再分别评估。
.venv\Scripts\python.exe -m exprnet.evaluate --run artifacts/runs/laila_resmlp_v1 --golden golden/laila_dev.json
.venv\Scripts\python.exe -m exprnet.export --run artifacts/runs/laila_resmlp_v1 --name laila_resmlp_v1
```

如 python 不在 PATH，先用 uv python find 得到解释器。pytest 非零退出必须处理；依赖缺失或可选用例跳过要明确报告，不能算作全部通过。

现有 evaluate 会覆盖 metrics.json / report.md，正式测试前应保存开发评估报告，避免混淆两套结果。最终冻结后执行：

```powershell
.venv\Scripts\python.exe -m exprnet.evaluate --run artifacts/runs/laila_resmlp_v1 --golden golden/laila_test.json
```

再次导出只用于更新冻结后的评估摘要；现有 export 会覆盖同名产物。已经在 Unity 验收的版本不能原地覆盖，换唯一版本名并保留上一版供回滚。

### 7.2 拒识规则与校准

复用当前温度缩放和独立负样本标定。标定负样本与留出负样本种子不同，留出集不得参与阈值选择。开发集可用于判断默认阈值是否适合 laila；锁定测试集不可用于调参。

```text
若 energy > energy_threshold：unknown
否则若 max(probs) < min_confidence：unknown
否则：labels[argmax(probs)]
```

等于阈值不拒识；概率并列时按元数据顺序取第一个。unknown 是七类之外的决策状态，不是第八个 softmax 输出。min_confidence 初始沿用配置 0.4，仅是原型默认值，不是验收后必然适合的阈值。

绑定大改后不能只换 ONNX 第一层并沿用旧指标与阈值宣称通过。第一版要求 train_rig_hash 等于导出 rig.config_hash；不一致则重训 / 重新校准 / 重评，或明确退回实验状态。

### 7.3 覆盖度与可识别类别

保存 coverage 报告：不可达规范基、逐类投影残差、参数饱和比例。当前脸部没有皱鼻、独立眉角等完整控制，七类可分性尚无证据。

高残差或两类投影后近乎相同，先核查绑定与模型表达能力，不通过压低拒识阈值掩盖。若确实缺动作，提出缩小可验收类别或增加形态的单独方案；未经确认不改 FBX。本 spec 仍保留七类目标，不声称当前模型必然全部可识别。

### 7.4 ONNX 契约

| 项 | 名称 | 类型 / 形状 |
| --- | --- | --- |
| 输入 | sliders | float32，[batch,12]；Unity 本期 batch=1 |
| 输出 | probs | float32，[batch,7]；类别顺序取 metadata.labels |
| 输出 | energy | float32，[batch]；可为负数 |
| 格式 | ONNX | opset 15，沿用现有导出器 |
| 元数据 | 同名 JSON | schema_version=1，现有字段保留 |

必须通过现有导出三项自检：1000 个输入的 PyTorch / ONNX Runtime 最大绝对误差 < 1e-5、算子白名单与 opset 正确、文件小于 1 MiB。*.failed.onnx / *.failed.json 不得导入部署资产。

为防同名模型与 JSON 错配，拟在现有 export.py 元数据的 onnx 块增加 `sha256` 字段，摘要取最终 ONNX 字节；保持 schema_version=1 的其余字段，不重写导出器。新增导出测试验证摘要与文件一致。Unity Editor 部署校验读取源 ONNX 并比对；运行时使用通过该校验的 ModelAsset / TextAsset 对，不声称 ModelAsset 本身保留可直接计算的源文件字节。

不得手改导出 JSON 中的绑定矩阵、类别或温度。阈值变更需记录开发集证据并重新导出，不在 Inspector 另藏一套不匹配的阈值。

## 8. Unity 接入契约

### 8.1 包与程序集

拟沿用原型目标 `com.unity.sentis` 2.1.3，CPU 后端优先。安装前单独说明 manifest 变更原因并按项目设置护栏执行；若需手动操作则保留阻塞，不关闭 hooks 绕过。不得为了最新包而升级 Unity 或混用 Inference Engine 新版 API。

安装后核对包的实际程序集名，再给 Game.Runtime 添加正确引用；不要猜 GUID 或手改 packages-lock.json。当前 Game.Runtime 也没有显式 Newtonsoft 程序集引用，新组件解析 JSON 时需核对并添加实际引用；该包已存在，不重复安装。

官方 2.1 文档确认该系列支持 ONNX 模型运行，并已提示新版名称为 Inference Engine；本 spec 固定的是现有原型适配目标，不是最新版建议。是否能成功安装、导入和运行本项目导出图，必须实际验证。

### 8.2 拟新增最小实现

新目录为 `Assets/_Project/Scripts/Runtime/LailaFaceRecognition/`，命名空间 `Game.LailaFaceRecognition`，沿用 Game.Runtime，不新建独立 asmdef。

| 文件 / 类型 | 职责 | 为什么不能塞进现有拖拽脚本 |
| --- | --- | --- |
| LailaExpressionParameters.cs | 固定 key / 必需形态表、只读采样和输入校验 | 拖拽只负责写形变；采样还服务 Editor 与测试 |
| LailaExpressionRecognizer.cs | 元数据 DTO、纯阈值判定、模型生命周期、EvaluateCurrent、结果输出 | FaceBlendShapeController 不应承担推理依赖 |

第一版将小型 DTO / 判定辅助放在识别文件内，不为每个结果或错误码建一个文件，不新增单实现接口。只有出现独立复用需求才拆分。

拟公开契约：

```csharp
// 契约草案，不是现有 API；最终签名可在实现审查中收敛。
bool TryCapture(FaceBlendShapeController face, float[] destination, out string error);
void EvaluateCurrent();
ExpressionResult LastResult { get; }
```

TryCapture 的 buffer 长度必须为 12；失败时调用方不得消费半写入数组。ExpressionResult 包含状态、label key / 中文名、最高概率、七类概率快照、energy、拒识原因；不能暴露即将被下一次推理覆盖的内部数组。

状态至少区分 NotReady、Ready、Known、Unknown、Error。Ready 表示可运行，不表示识别质量已通过人类验收。错误码至少包括 MissingFace、MissingShape、PairConflict、InvalidInput、InvalidMetadata、ModelMismatch、InvalidOutput、InferenceFailed。

### 8.3 初始化与生命周期

在 Start 初始化，不依赖 MonoBehaviour Awake 相互先后顺序：

1. 校验 face、ModelAsset、元数据 TextAsset 引用。
2. 建立 / 检查形态缓存，解析 schema、sliders、labels、阈值、rig 与 checks。
3. 检查 labels 恰为约定七类且顺序相同、无重复；temperature 必须正数且有限，energy 阈值有限，min_confidence 在 [0,1]。
4. checks 三项全部为 true，train_rig_hash 与 rig hash 一致，并与场景组件保存的 approvedRigHash 一致；该值来自审阅过的绑定记录，不自动从待加载模型元数据抄取。本期部署还要求来源护栏通过。
5. 读取模型输入 / 输出名称、类型与维数，动态 batch 允许但特征维必须是 12、类别维必须是 7。
6. 创建 CPU Worker，做一次合法零输入冒烟并检查输出。缺引用不能返回 neutral 充当默认结果。

Worker 与输入 Tensor 复用；输出的 Peek 值如由 Worker 持有不得擅自 Dispose。需要 CPU 读取时按已安装版本 API 获取可读结果，拥有的克隆 / 临时 Tensor 用 using / finally 释放。禁用 / 销毁时统一释放且幂等。Start 完成前 OnEnable 不提前初始化；后续 OnEnable 在 Start 已完成的前提下重新调用幂等初始化，不能依赖只运行一次的 Start 重建 Worker。不要把旧 Worker 或旧结果跨场景保留。

EvaluateCurrent 在 Unity 主线程执行，不用 Task.Run 操作 Unity 对象。单次执行完成再允许下一次；失败清空有效结果并进入 Error，不展示上一张脸的标签。第一版不做自动重试或静默换后端。

输出检查：概率和 energy 都有限；probs 每项在容差 [-1e-4,1.0001] 内且总和与 1 相差不超过 1e-3；维数严格符合契约。明显错误不能靠再归一化掩盖。

### 8.4 触发与显示

只在 laila 增加局部调试 Canvas：识别按钮、状态文本、七类概率文本、拒识原因。复用现有 EventSystem，所有装饰文字关闭 Raycast Target，按钮区域避开脸部控制区，不能用全屏透明 Image 抢射线。

Button.onClick 显式连接 EvaluateCurrent；不添加原始按键 / Input.touches 读取，不改 GameInput。提供运行态 ContextMenu 入口便于无 UI 数值检查；Editor 不加载模型推理伪装成 Player 验收。

重置脸部后需重新点击识别。结果显示说明是“上次识别结果”，避免玩家继续拖动后把旧标签误认为实时结果。先不接“目标表情达标”逻辑，尤其不能把 top1 概率直接当关卡分数。

## 9. 场景接线与保护范围

在 laila 当前扩展模型旁创建 `LailaExpressionRecognition` 局部对象，挂识别组件。face 明确指向启用的扩展脸部控制器，不指向旧的停用 Head-topo，不额外挂一个旧网格 Renderer。

模型 / JSON 部署资产拟放 `Assets/_Project/Data/LailaFaceRecognition/`，例如 `laila_resmlp_v1.onnx` 与同名 JSON。从通过检查的 artifacts 复制，不能假定 ignored 训练目录会进入 Player。新资产和文件夹 .meta 由 Unity 生成，场景修改通过 Unity MCP。

实施前记录并实施后对比：

- 现有模型节点 Transform、控制区位置 / 半径 / 灵敏度、12 区 Face / Pivot 引用。
- 22 个权重在退出验证后恢复为进入前的值，不强行覆盖用户当前表情。
- 双眼 Pivot / 子物体位置与缩放、牙齿位置、材质引用、相机配置。
- 其他场景、共享 URP / Renderer 与源 FBX 的 SHA256。

不运行旧的一键创建控制区菜单；不加载其他任务正在测试的场景，不在并发 PlayMode 测试中写资产。场景本来有用户未保存改动时，先记录并保留，不通过重载场景丢弃。

## 10. 拟涉及文件与资产

下表是设计边界，不是已生成的任务清单。审阅 spec 后再拆执行任务。

| 路径 | 拟变更 | 必要性 |
| --- | --- | --- |
| ML/expression-recognition/configs/rigs/laila_rig.yaml | 新增 | 当前真实绑定 |
| ML/expression-recognition/configs/rigs/laila-binding-review.md | 新增 | 几何证据与绑定 hash |
| ML/expression-recognition/golden/laila_dev.json、laila_test.json | 采集后新增 | 非同源人类验收集，不先造假样本 |
| ML/expression-recognition/exprnet/export.py | 小改 | ONNX 字节 hash |
| ML/expression-recognition/exprnet/evaluate.py | 小改 | 人工样本的 hash / 值 / 标签护栏及非占位报告说明 |
| ML/expression-recognition/tests/test_export.py | 扩展 | 模型与 JSON 配对验证 |
| ML/expression-recognition/tests/test_laila_contract.py | 新增 | 12 维契约、真实绑定与人工样本护栏 |
| Assets/_Project/Scripts/Runtime/LailaFaceRecognition/ 下两文件 | 新增 | 采样 + 推理胶水 |
| Assets/_Project/Scripts/Editor/Tools/LailaExpressionCaptureWindow.cs | 新增 | 人工采集与部署配对校验；不做后台自动化 |
| Assets/_Project/Scripts/Runtime/Game.Runtime.asmdef | 小改 | 实际推理 / JSON 程序集引用 |
| Assets/_Project/Scripts/Tests/EditMode/LailaFaceRecognition/ | 新增测试 | 采样、元数据、阈值和失败分支 |
| Assets/_Project/Scripts/Tests/PlayMode/LailaFaceRecognition/ | 新增测试 | 生命周期与实际模型冒烟 |
| Assets/_Project/Data/LailaFaceRecognition/ | 验证后新增资产 | 版本化 ONNX / JSON |
| Assets/_Project/Scenes/laila.unity | 接线 | 组件和局部调试 UI |
| Packages/manifest.json | 经护栏批准后小改 | 安装推理包 |

模块文档在实现落地时按项目流程补齐；本次只新增本 spec，不把设计写成已实现模块接口。不会预建以上空目录。

训练原图、特征、环境和训练权重继续留在已有忽略范围。进入 Assets 的部署产物是发布资产，应记录 hash / 来源；提交资格须通过来源检查和项目提交审查，不能因为 artifacts 被忽略就让部署文件缺失。

## 11. 验证与验收矩阵

### 11.1 工程门槛：必须全部满足

| ID | 标准 | 验证方式 |
| --- | --- | --- |
| E01 | 12 个 key / 22 个必需形态正确，全零得到 12 个 0 | Python 契约测试 + C# 采样测试 |
| E02 | 成对 ±100 得 ±1，单唇 100 得 1，下唇不反号，横轴 Out 为正 | C# EditMode |
| E03 | 缺形态、两侧同时激活、NaN、Infinity、明显越界均失败，不伪造 neutral | EditMode 失败分支 |
| E04 | metadata 顺序与 12 维契约严格一致；重排、缺 key、重复、错范围、错 labels / hash 拒绝 | 元数据测试 |
| E05 | 阈值大于 / 小于 / 等于边界和概率并列结果与 Python 一致 | 纯判定测试 |
| E06 | 改眼球四向时 12 维输入完全相同，分类概率差异不超过 1e-6 | 同模型同输入对照 |
| E07 | PyTorch / ONNX Runtime 1000 输入误差 < 1e-5，opset / 白名单 / 大小全通过 | 现有 export 自检 |
| E08 | Unity CPU 与 ONNX Runtime：至少 100 组固定输入，probs / energy 最大绝对误差 ≤1e-4 | 导出数值 fixture + Unity 实际模型测试 |
| E09 | 稳定样本的 Unity / Python 标签和拒识一致 | 避开边界的固定 fixture；边界接近样本另报告，不隐藏 |
| E10 | 模型 / JSON 配对 SHA256 正确，故意错配拒绝部署 | Editor 校验 + 导出测试 |
| E11 | 启用 / 禁用或进入 / 退出 20 次，正确释放，无递增原生资源泄漏 | PlayMode / Profiler |
| E12 | 识别按钮正常；12 个现有控制区不被新 UI 抢射线 | EventSystem 首命中检查 + 人工拖动 |
| E13 | 本期保护范围未改变；测试临时权重 / 视线已恢复 | 变换 / 引用 / hash 前后比较 |
| E14 | 编辑器编译通过，定向测试真实执行，无本功能引入的 Console 错误 | Unity 编译、测试结果、Console，不清日志伪造零错误 |

E08 的 fixture 用稳定随机种子，覆盖全零、12 轴极值、组合与边界附近输入，并记录模型 hash。两条阈值任意一条距离小于数值误差时，标签不同可能是浮点边界问题，需独立记录并设计保守边界策略；不能仅用绝对误差通过来证明边界决策完全一致。

### 11.2 外观语义门槛：本 spec 提出的原型目标

以下不是已经测得的准确率，也不是保证能达到的结果。开发开始前审阅这组目标；达不到就保留实验状态并报告逐类结果。

锁定的 180 个独立人类样本上：

- 140 个已知类样本，最终判定准确率 ≥80%；被拒识也计为错误。
- 七类宏平均 F1 ≥0.80，每类召回率 ≥0.65；已知类被拒识比例 ≤10%。
- 40 个 unknown 样本，拒识率 ≥80%。
- 一致标注率单独报告；不能丢掉所有难例后只公布容易样本的高分。

报告必须包含混淆矩阵、逐类数量 / 召回、拒识率、开发 / 测试划分、绑定和模型 hash。逐类召回的分母包括该类全部已知样本，被拒识者计为漏判；precision 使用预测为该类的样本作为分母。现有 classification_metrics 已以全部真实支持数计算召回，但 confusion 不包含拒识列；人工验收报告应补 unknown 列和 unknown 真值行，并以最终阈值判定而非仅 argmax 计算已知类宏 F1，不能直接挪用合成验证集指标。

若确认某类当前几何不可表达，七类门槛不算通过。允许经审阅后改成较小类别集，但需修改类别配置、重训、重导出与重新冻结测试，不只在 UI 隐藏失败类别。

### 11.3 性能目标与平台口径

PC 原型：batch=1，20 次预热后至少 500 次测量，记录 CPU 后端、Unity / 包版本、测试机器与构建类型。建议采样 + 推理 + CPU 读取的 p95 ≤30 ms；这是待实测的交互预算，不引用设计稿的“微秒级”作为事实。

Editor 通过不等于 Windows Player 或 Android 通过。先做 Windows Player 冒烟与性能记录；Android 验收单列，未在实机运行时标为未验证。纯参数推理不会涉及摄像头权限。

## 12. 故障行为与回滚

| 情况 | 应有行为 |
| --- | --- |
| 模型 / 元数据缺失、契约不匹配 | Error，禁用识别按钮；拖拽仍可用 |
| 输入冲突 / 非有限值 | 显示具体输入错误，不运行网络 |
| 合法输入但分布外 | Unknown，保留概率与拒识原因 |
| 网络返回非法输出 / 抛异常 | 清除旧有效结果，Error，释放本次临时资源 |
| 某表情人类与模型不一致 | 保留失败截图与参数，先查映射 / 覆盖度，不直接降低全部阈值 |
| 推理时间超预算 | 先测 CPU 基线和读取开销，证据明确后再评估 GPU，不同时改模型与后端 |

回滚仅停用本期新增识别对象 / UI，恢复本期新增资产引用；原有捏脸与材质仍工作。回退到上一模型时必须成对换 ONNX 和 JSON，并重新校验 hash。不要以 git reset 或场景重载清除用户其他改动。

正式玩法若未来消费分类结果，需要另定输入命令、结果版本、回放和确定性策略；不同硬件上的神经网络浮点输出不能未经设计就成为确定性逻辑的唯一真值。

## 13. 阶段门与待确认项

| 阶段门 | 进入下一阶段的条件 |
| --- | --- |
| A：规格审阅 | 接受范围、12 维契约、按钮触发及拟验收指标 |
| B：绑定审阅 | 逐轴几何证据和候选系数定稿，coverage 无未解释的严重缺口 |
| C：工程跑通 | 测试 / 导出自检 / Unity 数值对照通过；明确标为实验版 |
| D：语义验收 | 独立人类测试集达到约定指标；失败类不能隐藏 |
| E：平台验收 | Windows Player 已测；其他平台独立标记 |

待确定的不是代码能否写出来，而是：眼皮收紧的规范语义、内收嘴角是否真的噘嘴、独立唇形变是否足以覆盖目标类别、共享眉基的系数，以及这组原型质量目标是否符合玩法需求。不得在实现中把这些未验证假设自动提升为事实。

## 14. 来源与实现前必读

- 仓库入口：`ai-docs/project-guide.md`、`.claude/rules/project-root.md`、`docs/architecture.md`。
- 当前捏脸接线：`ai-docs/docs/modules/lailaface/lailaface-module-guide.md`。
- 实际读取接口：`Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs`。
- 形态命名与范围：`Assets/_Project/Scripts/Runtime/Gameplay/FaceDragHandle.cs`。
- 原型契约：`ML/expression-recognition/README.md`、`DESIGN.md`。
- 绑定 / 导出 / 金标实现：`exprnet/rig.py`、`exprnet/export.py`、`exprnet/evaluate.py`。
- Unity 2.1 官方说明：[Sentis overview](https://docs.unity3d.com/Packages/com.unity.sentis@2.1/manual/index.html)、[Supported models](https://docs.unity3d.com/Packages/com.unity.sentis@2.1/manual/supported-models.html)。

实施前按项目路由追加读取 C#、Unity 资产、测试等规则；本 spec 不代替这些流程，也不代表已批准包安装、资产修改或 Git 提交。

## 15. 阶段门 A 审阅结论（2026-09-30）

**结论：通过。** 12 维输入契约（§4）、按钮触发（§1.2）、§11.2 的门槛数值都不变。唯一改动是验收类别集。
依据：[`ML/expression-recognition/analysis/laila_upper_bound/REPORT.md`](../../ML/expression-recognition/analysis/laila_upper_bound/REPORT.md)。按现有 22 个形态，恐惧（做不出 AU1+2+4 的眉形）和厌恶（做不出 AU9 皱鼻）即使在乐观上限下也只有 0.64 左右，大概率过不了阶段门 D。

### 15.1 验收类别集改为五类

| 顺序 | key | 中文 | 由哪些标注类组成 |
| --- | --- | --- | --- |
| 0 | neutral | 中性 | neutral |
| 1 | happy | 高兴 | happy |
| 2 | sad | 悲伤 | sad |
| 3 | surprise_fear | 惊讶/恐惧 | surprise、fear |
| 4 | angry | 愤怒 | angry |

- 厌恶（disgust）不在验收集。完整七类改为不承诺的延伸目标。升级条件有两个，满足其一即可：阶段门 B 证明上唇上提（`Mouth_UpperLip_Up.001`）可以作为厌恶线索，且开发集盲标证实；或者另立方案补内眉、皱鼻、抿唇形态（改 FBX，按 §7.3 单独审）。
- 改类别集按 §11.2 的要求走：改类别配置 → 重训 → 重导出 → 重新冻结测试，不在 UI 里隐藏。

### 15.2 受影响的条目

| 条目 | 原文 | 改为 |
| --- | --- | --- |
| §1.1 表情名称 | 七类之一 | 五类之一 |
| §7.4 ONNX 输出 | probs `[batch,7]` | probs `[batch,5]`；类别顺序以元数据 `labels` 为准，即上表顺序 |
| §8.3 第 3、5 步 | labels 恰为约定七类；类别维必须是 7 | labels 恰为上表五类且顺序相同；类别维必须是 5 |
| §6.2 标注协议 | — | **不变**：标注者仍在七类加「不明确 / 混合」里选。评估时按上表映射：surprise、fear → surprise_fear。disgust 是「集外类」，它的预测去向单独报告，不计入 §11.2 的任何指标。原始七类标注照原样保存，以后升级类别集时可以直接复用 |
| §6.2 / §11.2 样本量 | 已知类每类 20，共 140 | 采集目标不变（仍按七类各 20 采）。计入五类指标的已知类样本是 120 个，其中 surprise_fear 40 个；disgust 的 20 个单独报告 |
| §7.1 训练 | — | 新增两个开关：一是按类别集训练；二是按绑定剔除做不出来的变体（核心动作投影后几乎全丢的合成变体不进训练目标，剔除清单写进训练报告）。上限分析里，后者把愤怒召回从 0.81 提到 0.98 |

### 15.3 实施分工

- **网络侧**（`ML/expression-recognition/`，不需要 Unity 编辑器，不依赖几何校准）由主仓库会话实施：
  - 类别集配置与映射；
  - 按绑定剔除做不出来的变体；
  - 导出元数据加 ONNX 字节 sha256（§7.4）；
  - 评估器护栏（§6.3、§10），包括绑定名不符时报错，而不是像现在这样静默跳过金标；
  - 12 维契约测试（§10 的 `test_laila_contract.py`）。
- **不做**：`configs/rigs/laila_rig.yaml`（`laila_v1`）仍要等阶段门 B 的几何校准定稿。
- **Unity 侧**（§8–§9）不变，按 §15.2 的类别维改动实施。
