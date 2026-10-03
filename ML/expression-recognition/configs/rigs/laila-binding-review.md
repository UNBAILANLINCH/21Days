# laila_v1 绑定校准记录（历史版本）

> 2026-10-03 状态更新：下文只对应旧 `extended2`、22 形态和 12 维输入。该 Unity 模型已移除，当前模型为 `refined3 1`、31 形态；新版 17 维契约、重新校准与训练顺序见 [训练 PRP §16](../../../../PRP/laila-expression-recognition/spec.md#16-当前模型的训练执行顺序2026-10-03)。历史中的“当前”“正式”均指 2026-10-01，不是现版本；不沿用旧侧别、系数、覆盖率结论作为新版证据。

日期：2026-10-01。对象：当前 `laila` 场景启用的 `Eve`，1226 顶点、22 个 BlendShape。

本记录确认当前资产的几何方向和第一版工程映射，可用于五类实验训练；不代表表情语义、人类准确率或 Unity 网络接入已验收。正式绑定为同目录 `laila_rig.yaml`，不是上限分析的候选文件。

## 资产与绑定锚点

- 源资产：`Assets/_Project/Art/fbx/Head-topo-expression-extended2.fbx`。
- 源 FBX SHA256：`8973b990f0b69d9573611182fb7d0b33554c4ebae1afeec8d399142dabad976b`。
- `Rig.config_hash()`：`4feafe159e6fea4760bc8cac2b1dbccdcb99cec7d90b6a87b357935af02568ce`。
- Unity 2022.3.62f2；通过临时 stdio MCP 客户端实际读取编辑器。Codex 原生客户端仍初始化失败，二者不能混称同一连接。

## 实际执行与恢复

读取源网格逐顶点相对位移，并在 EditMode 用 `BakeMesh` 固化临时预览网格，通过当前 Main Camera 渲染。每次烘焙后立即恢复源 Renderer 权重，预览只短暂替换显示，不修改 FBX、控制区或材质。

每档 34 个状态：Basis、22 个单形态，以及双眉抬/压、双上眼皮闭合、双下眼皮上抬、上下眼皮联合闭合、双唇打开、双嘴角外拉/内收、嘴角上扬+下眼皮上抬、嘴角下压+抬眉、嘴角上扬+外拉。分别检查权重 50 和 100，共 68 次渲染。

本地证据在子项目的已忽略目录 `.cache/laila-calibration-20261001/`：`axes-50.png`、`axes-100.png`、`index.json`、`geometry.json`。截图按从左到右、从上到下排列，编号为 `index.json.samples` 的零起始索引；截图不进入部署资产。逐顶点数据和截图是当前本地验证产物，未提交；下表保留关键数值，源资产 hash 是重新校准的锚点。

操作前后：全部 22 权重为 0，原 Renderer 启用；已有节点的世界矩阵逐项相同；临时网格、物体、RenderTexture 与 Texture2D 已销毁；相机 targetTexture 与活动 RenderTexture 已恢复；场景 `isDirty=false`，未保存或重载场景、未进入 Play。

## 左右侧确认

Main Camera 在世界 Z=-10，朝 +Z，画面右方向为世界 +X。正面截图显示角色面对相机；源 `*_L_*` 位于 X<0 的画面左侧，`*_R_*` 位于 X>0 的画面右侧。因此源 L 是角色自身右侧，源 R 是角色自身左侧。

保留规格约定的 12 个输入 key/顺序，不能改 Unity 输入 key 来抵消侧别；绑定中的规范基 Left/Right 统一对调。与上限分析候选的同名侧假设不同，后续训练必须用这份绑定。

## 几何证据与映射

下表为受影响顶点的世界位移均值，单位毫米，取源形态满幅；方向不是按名称猜测。角色面部向世界 -Z，因此 ΔZ<0 为前凸。计数阈值为源局部位移长度 ≥1e-6，微小位移可能未计入。

| 源形态 | 顶点数 | ΔX | ΔY | ΔZ | 映射/限制 |
| --- | ---: | ---: | ---: | ---: | --- |
| Mouth_L_Up | 30 | -0.546 | 2.669 | -0.202 | mouthSmileRight |
| Mouth_L_Down | 32 | 0.396 | -4.268 | 0.000 | mouthFrownRight |
| Mouth_R_Up | 31 | 0.603 | 3.017 | 0.000 | mouthSmileLeft |
| Mouth_R_Down | 34 | -0.603 | -4.755 | 0.000 | mouthFrownLeft |
| Brow_L_Up | 10 | -0.018 | 7.718 | 0.000 | browOuterUpRight + 共享 browInnerUp |
| Brow_L_Down | 13 | 2.705 | -3.753 | 0.000 | browDownRight；不是纯降眉 |
| Brow_R_Up | 10 | 0.000 | 8.942 | 0.000 | browOuterUpLeft + 共享 browInnerUp |
| Brow_R_Down | 11 | -1.432 | -3.774 | 0.000 | browDownLeft；不是纯降眉 |
| Eye_L_UpperLid_Up | 22 | 0.257 | 4.963 | 0.000 | eyeWideRight |
| Eye_L_UpperLid_Down | 19 | -0.515 | -6.070 | 0.000 | eyeBlinkRight |
| Eye_L_LowerLid_Up | 15 | 0.121 | 1.422 | 0.000 | eyeSquintRight；不补 cheekSquint |
| Eye_L_LowerLid_Down | 10 | -0.493 | -1.479 | 0.000 | 没有独立规范基，neg={} |
| Eye_R_UpperLid_Up | 20 | 0.230 | 4.046 | 0.000 | eyeWideLeft |
| Eye_R_UpperLid_Down | 15 | 0.463 | -6.469 | 0.000 | eyeBlinkLeft |
| Eye_R_LowerLid_Up | 10 | -0.625 | 2.622 | 0.000 | eyeSquintLeft；不补 cheekSquint |
| Eye_R_LowerLid_Down | 8 | 0.000 | -1.794 | 0.000 | 没有独立规范基，neg={} |
| Mouth_UpperLip_Up.001 | 90 | 0.012 | 3.810 | 0.027 | 双侧 mouthUpperUp；不补 jawOpen |
| Mouth_LowerLip_Down.001 | 54 | 0.000 | -6.748 | 0.000 | 双侧 mouthLowerDown；不补 jawOpen |
| Mouth_L_Out | 12 | -3.183 | 0.000 | 0.000 | mouthStretchRight |
| Mouth_L_In | 29 | 2.837 | 0.218 | -1.637 | 内收+前凸已确认；完整噘嘴语义证据不足，neg={} |
| Mouth_R_Out | 12 | 3.183 | 0.000 | 0.000 | mouthStretchLeft |
| Mouth_R_In | 29 | -2.837 | 0.218 | -1.637 | 同上，neg={} |

内眉上抬有逐顶点证据：源 L 的 |X|≈10–12 mm 顶点上移 3.19–3.73 mm，外侧约 9.50 mm；源 R 对应内侧上移 5.96 mm，外侧约 10.22 mm。两个眉轴确实影响内眉，但不能独立控制内外眉。Down 还包含部分中线顶点上移约 2–3 mm，故不能称其为完整、纯净的 AU4。

## 系数的证据边界

单侧已确认动作的满幅归一化为 1；共享 `browInnerUp` 每眉占 0.5，双眉半幅得到 0.5、双眉满幅得到 1，不提前饱和。唇部单轴作用两侧，两侧各为 1。眼球旋转不进输入，视线规范维仍屏蔽。

这些数值是第一版工程约定，不是从标准 ARKit 参考网格拟合出的系数，也不是测得的 FACS 强度。几何位移与语义映射应区分：已确认升降、侧向位移和闭合；`eyeSquint` 等规范语义是依据动作的近似映射。左右形态幅度并不完全对称，暂不凭毫米比例直接改成情绪强度比例。后续独立开发集若显示系统性偏差，应调整系数并重训、重新标定和重新导出。

下眼皮下拉与嘴角内收虽然保留输入维度，当前规范向量不响应这些半轴。由此产生不同外观而网络输入等效的情况是明确的模型上限；若开发集证明这些差异改变人类标签，就需扩展映射或形态设计，不能只压低阈值。

## 检查与覆盖度

实际运行 `pytest tests/test_rig.py tests/test_canonical.py -q`：17 通过。新增检查验证源 L 嘴角对应角色 Right、源 R 对应 Left、共享内眉半幅/满幅，以及独立唇不冒充 jawOpen。

另独立执行 `tests/test_laila_contract.py` 中既有的 `assert_laila_contract` 与绑定名断言，通过 12 key/顺序、范围、零默认值、单极唇和全零向量检查。以 AST 提取这组既有断言，未加载该文件的 torch/导出依赖；这不等于完整运行该测试模块或训练导出用例。

实际运行 `exprnet.coverage --rig configs/rigs/laila_rig.yaml`：3500 个七类 FACS 合成样本，平均相对平方残差 0.3023，P90 0.7287。逐类：neutral 0.3863、happy 0.3194、sad 0.2768、surprise 0.1791、fear 0.2086、disgust 0.4264、angry 0.3198。完整报告在 `artifacts/coverage/laila_v1/coverage.md` 与同名 JSON。

没有驱动的常用基包括 cheekSquint、jawOpen、mouthPress、mouthRoll、mouthShrugLower、noseSneer；browInnerUp 的合成覆盖保留不足 70%，因为抬内眉必然联动外眉。这些缺口已解释，但并未解决；覆盖度不是分类准确率，也不能据此宣布五类验收通过。

下一步使用 `--label-set laila_5class --drop-infeasible-variants` 训练 ResMLP 实验基线，采集独立人类开发/测试集验证。当前环境仅安装 numpy/scipy/pyyaml/pytest，未运行神经网络训练、ONNX 导出或 Unity 推理测试。
