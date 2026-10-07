---
type: module-guide
module: lailafacerecognition
layer: runtime
maturity: seed
---

# 莱拉表情识别实验

2026-10-06更新，独立Editor试玩默认当日定向修复59D的五类稳定反馈，可切上一版反馈59D／旧r779／原51D；原51D作为同场景的旧版对照保留。单轮训练与Unity定向验证见下方收尾段。人工语义／Player未验收，集外拒识不是当前反馈验收要求。唯一当前执行入口：[训练PRP§17](../../../../PRP/laila-expression-recognition/spec.md#17-当前工程候选接手入口2026-10-04)→§16；本指南维护Unity职责，不另设训练流程。17控制轴是ONNX外部输入，51／59维是各模式的图内空间，输出仍5类。拆6类未执行；旧12轴/v1仅历史复现。

## 功能目的与范围

识别当前角色脸部参数“看起来是哪种表情”，用于原型展示和开发诊断；不推断真人心理状态，不做人脸身份识别。截图供人工标注，分类器输入仍为形态参数，不是渲染图片。

| 功能 | 当前状态 |
| --- | --- |
| 17轴采样、五类推理、稳定反馈／历史拒识诊断 | 已接Unity Editor实验场景；不接关卡成功条件 |
| 当前相机截图与匿名参数采集 | 已实现Editor手动菜单 |
| 首轮开发辅助标注器 | 已实现本机浏览器UI、保存续标与导出 |
| 输入诊断参考图 | 原Laila控制器／Sampler已核对6参考＋31单方向；离线Unity BakeMesh截图精确匹配raw17，不是实时预览或人工语义金标 |
| 单人raw17标签适配／候选训练 | 成对51D／59D适配及seed42／80轮研究微调已完成；通用加载器未改，多人合并未完成 |
| 正式v2语义冻结、独立测试、Player | 未验收；不能以合成高分代替 |

本模块不修改脸部几何、视线、灯光或材质，不给关卡判定、存档或确定性回放提供情绪真值。眼角自交修复已延期，标注工作不解除几何限制。

## 职责

Runtime/LailaFaceRecognition：Sampler按17固定key严格采样，研究试玩赋权复用同一形态表且完整校验后才写；Recognizer负责元数据校验、Sentis CPU推理及输出／拒识，SetResearchCandidate显式更换研究引用并重新初始化，失败清旧输出；Panel显示等待／结果／错误。Playtest提供典型脸／三档／复位／四版模型切换及原下睑／旧怒眉对照，不采集或标注。仅通过现有Face控制器读写权重，不改几何。

Editor/Tools：RecognitionTools采集未标注样本；CaptureWindow显式选择近邻组/划分；PlaytestTools在无Play／无脏场景时打开唯一研究试玩，退出恢复此前场景，不创建或升级资产；DeploymentCheck在Play/构建核对真实源ONNX与JSON及批准hash，正式Player拒绝研究模式。

## 接线

唯一推荐菜单 `21Days/Laila/候选试玩（含旧版对照）` 单独加载 `LailaRecognitionPlaytest.unity` 并进入Play。默认研究引用 `Data/LailaFaceRecognition/Research/StrengthRepair20261006/laila_research59_strength_20261006`；按钮循环定向修复59D→上一版反馈59D→旧r779 59D→原51D→定向修复59D，四对引用及批准hash显式序列化且Editor核源字节。研究模式要求59D、未校准、单人开发标记；切换释放旧Worker重建，不改脸或门槛。Playtest保留旧眉／原下睑配方，默认加强眉并下睑0；设计意图不是标签。菜单拒绝已有Play、脏或未保存场景，只打开既有场景，不升级或保存资产；SessionState跨Play域重载保留此前场景配置，退出或启动失败后恢复并清键退订。Play前隔离其他scene避免同时运行其Bootstrap。Runtime原根对象暂停／恢复逻辑仍供手动附加研究场景兼容，不能代替菜单的初始化隔离。具体操作、实际测试及边界由ANNOTATOR维护。

只在laila场景创建 `LailaExpressionRecognition`，显式Face指向启用31形态Eve，引用 `Data/LailaFaceRecognition/laila_cand_v2_s42_20261003.onnx`、同名JSON及hash。没有修改其他场景。官方Sentis2.1.3支持当前Unity版本，程序集引用Unity.Sentis；不升级Unity。

Start后加载模型，Disable/Destroy释放Worker/Input，再Enable幂等重建。CPU PeekOutput由Worker持有；完成作业后直接读取，实时路径复用输入/概率/采样暂存数组与委托，不克隆输出。元数据严格校验17轴顺序/范围/default、五类顺序、导出自检与train/rig hash；Editor再核对源字节，不声称ModelAsset保留源ONNX字节。

## 输入和结果

17轴按PRP§16.2：六眉、四眼皮、四嘴角轴、左右上唇、下唇；四视线轴排除。31必需名称齐全、权重有限合法、同对不冲突后才写完整快照。缺键不能当0；同对同时激活不能通过相减隐藏。

反馈59D主标签只显示中性／高兴／悲伤／惊恐／愤怒：合法近零输入可由中性区覆盖，其余保持完整五类最高分竞争并经展示迟滞；低分不阻挡反馈、不自动转中性。原始五类概率不改、neutral不删。旧r779／51D及折叠详情仍保留历史energy／min_confidence拒识（相等不拒识、并列类别首项），不当当前反馈或关卡验收线。非法模型／输入／输出为错误，立即清旧展示和分数；结果不进关卡／回放，分数不代表人类分布校准。

## UI和采集

独立试玩追加LailaPlaytestFeedback与LailaPlaytestFeedbackStore：主动选类／不确定／难以表达和可选备注，同步BakeMesh／相机冻结点击瞬间PNG、raw17、31权重及同一输入预测；独占新UUID目录、JSON最后原子发布，不改玩家权重、不进旧标注／训练／锁测。隐藏预测用CanvasGroup，保持Recognizer启用；有限10张试点提示与标签分开，保存后揭示、显式下一张从中性重置。未保存试点在组件停用／重启时保持隐藏，但保留此前主动揭晓的暴露记录。显示名惊恐仅展示映射，内部surprise_fear不变。实际完整UI事件保存／重复／10封顶／失败重试／重入通过，14份fixture哈希不变；新Store八例＋原组EditMode共31项完成、失败记录0，Console错误0。fixture不是人工意见，物理鼠标／Player／独立语义验收未完成。保存路径、证据和新怒眉三档仍中性的限制由ANNOTATOR试玩段维护。

右上角面板CanvasScaler按高度匹配1920×1080，背景/文字不抢射线，只有重新识别/详情按钮可点击。LateUpdate读取17轴；实际变化立即清除旧标签/分数并显示等待，自动推理最多10次/秒，等待期间合并为最新输入。静止不重复推理，不因分组视线轴变化重算。输入冲突显示错误，恢复合法输入自动重算；模型错误释放资源，重新Initialize/启用后恢复。关闭Panel会停用识别组件并释放Worker/Input；重新打开重建、重新采样。手动重新识别可以立即刷新，不代替人工采集。

菜单 `21Days/Laila/采集识别样本（未标注）` 在运行态使用。显式对象、同配方共用近邻组、train/dev/test/unassigned划分；同组跨集拒绝。文件在忽略的`ML/expression-recognition/data/laila-captures-v2/<split>/`，记录17轴、31原权重、实际FBX/rig hash、相机截图。临时渲染目标finally恢复，截图不包含预测面板。文件不自动写标签；人工盲标、组迁移、锁定测试冻结尚需人员/流程。

## 验证和限制

| 范围 | 已有证据 | 不覆盖 |
| --- | --- | --- |
| 定向候选 2026-10-06 | 205 条 Sentis / ONNX 对照、21 档实际 UI、四版切换、异常清理及迟滞 | 独立人工语义、Player |
| 抓点回归 2026-10-06 | 原场景与独立试玩均有 17/17 hover，指针事件中的拖动跟随及释放恢复 | 物理鼠标连续拖动、跨帧合成捕获 |
| 初版实时推理 | [realtime-verification.json](../../../../PRP/laila-recognition-20261003/realtime-verification.json)：限频、静止不重复、冲突恢复及启停释放 | 正式部署、人工准确率 |
| 自动测试 2026-10-07 | 标注与反馈修复四份 Python 测试 36/36；见提交审查清单 | Unity、人工语义验收 |

定向候选和抓点证据分别在 `ML/expression-recognition/artifacts/laila_v2_candidate/strength-repair-unity-20261006-v1/` 与 `highlight-diagnosis-20261006-v1/`。
证据按冻结模型、日期和范围解释；历史失败与重试留在对应产物，不合并成一次全量通过。
这些 artifacts 为忽略的研究产物；需要复核时由持有者提供，干净检出不保证包含。

## 首轮开发辅助标注

Python子项目提供[双击启动入口](../../../../ML/expression-recognition/launch_annotator.cmd)及[操作说明](../../../../ML/expression-recognition/analysis/laila_v2_candidate/ANNOTATOR.md)。使用已验证139张匿名dev图、两个近邻组，七类加ambiguous，自动保存、续标和CSV/JSON导出；不连接Unity、不训练或改部署。记录位于忽略的artifacts目录，单人意见标记为开发标注，后续须独立多人审核及组划分，不能自动变成人工金标。

## 跨层数据流与依赖

```text
FaceBlendShapeController实际31权重
  → Sampler严格17轴快照
  → Recognizer / Sentis CPU：ONNX内映射51D或59D、分类5类
  → Panel：当前结果、等待、拒识或错误

Editor手动采集 → 匿名PNG＋17轴/31权重JSON
  → dev盲标包 → 本机辅助标注器 → 单人开发记录CSV/JSON
  → raw17＋单人标签成对候选适配 → 已完成51D／59D有界研究微调
  → 独立研究试玩／开发回归 → 待独立人工语义验收、校准与锁测
```

| 层 | 依赖及边界 |
| --- | --- |
| Runtime | `Game.Runtime`，使用已有`Game.LailaFace`公开读接口；依赖Sentis、Newtonsoft.Json、TMP及Unity UI |
| Editor | `Game.Editor`采集／部署校验；不得被Runtime引用 |
| Python训练 | `ML/expression-recognition/exprnet/`，独立子项目，不随Unity运行 |
| Python标注 | 盲标使用标准库HTTP；独立输入诊断延迟使用已有numpy／onnxruntime／exprnet，加载固定离线Unity参考包，不连接实时Unity或云服务 |

Unity没有另一份17→51矩阵；正负拆分、范围裁剪、规范映射、eyeLook屏蔽、分类和温度处理都在ONNX图内。运行时读取已校准概率和energy，不再softmax或重复除温度。

### 输入与输出契约

| 契约 | 实际实现 |
| --- | --- |
| 输入 | `sliders [1,17]`，前14轴[-1,1]、后3轴[0,1]，默认全0，顺序见spec§16.2 |
| 原始形态 | 14对双极键＋3个单极键，共31；四个眼球视线轴不输入识别 |
| 分类输入 | 图内51D或59D特征空间；与17个外部控制轴不是同一概念 |
| 输出 | `probs [1,5]`、`energy [1]`；类别顺序严格验证 |
| 互斥 | 同对正／反权重大于0.001即拒绝，不用相减隐藏冲突 |
| 数据容差 | 原权重[-0.001,100.001]内裁剪到[0,100]，非有限／明显越界报错 |
| 失败快照 | 全部检查成功才写destination，晚失败不会留下半个有效快照 |

当前下眼睑Down及嘴角In四个负方向没有51维映射；左右内眉Up共享，三段眉Down归同侧通道。真实几何差别可被映射压成相同输入。重训相同51维链路不能恢复被抹掉的区别，不能凭分段眉自由度宣称六类可分。

59D在51维后追加四个有符号眉轴和四个遗漏负方向，保留当前17轴信息；单人dev研究微调与独立Editor研究导出已完成，正式51D部署未替换，也不是实测AU标定。需要保留raw17的人工标签数据，不能从旧51维缓存唯一还原追加信息。候选训练及诊断记录由spec§17与ANNOTATOR说明维护。

### 拒识、错误及生命周期

历史拒识阈值来自模型JSON：energy `−1.5637034177780151`、最低概率`0.4`。大于energy阈值或低于概率门槛拒识；等号允许通过，概率并列按类别顺序。字节和规则不改，但反馈59D只在折叠详情记录该诊断，不用它阻挡主标签；旧r779／51D仍按历史策略展示，并标清模式。

| 页面状态 | 应如何理解 |
| --- | --- |
| 表情已变化／识别中 | 旧结果失效，等待当前输入推理；不表示新脸中性 |
| 当前识别／实验模型 | 模型判断，不代表人工准确率或已验收语义 |
| 不确定／认不出 | 合法输入未通过现行拒识门槛，不是第六个softmax类别 |
| 输入无效 | 缺键、引用失效、权重异常或互斥冲突，没有有效识别结果 |
| 识别不可用 | 模型、元数据、推理或输出故障，不能伪装成拒识 |

Start／重新启用初始化；Disable／Destroy释放输入张量及Worker。Panel启用订阅OnChanged，停用退订并停用Recognizer。推理失败释放资源；修复配置后重新Initialize／启用恢复，不继续显示旧概率。

## 采集与标注记录分工

采集必须处于Play、有显式识别对象和主相机；菜单窗口选择近邻组和划分。相同配方强度、镜像及近邻脸整组放一个split，已有group跨split会拒绝。锁测不进入开发诊断或用于修改模型／阈值。

| 记录 | 必需内容及用途 |
| --- | --- |
| 原始采集JSON | schema_version=1、匿名UUID、rig/name/hash、FBX hash、group/split、完整17轴和31权重；status保持unlabeled |
| 同名PNG | 当前相机真实渲染，人工观察用，不输入分类网络 |
| 管理员manifest | 原始JSON／PNG哈希、分组、版本、稳定顺序，负责关联匿名图与参数 |
| 标注器快照 | 代号、dataset_version、order、revision、cursor、标签／清晰度／备注、UTC时间及修改历史 |
| CSV／JSON导出 | 开发意见交接；JSON保留完整历史，CSV适合表格查看 |

原始unlabeled不覆盖用户意见，标注器也不改原始快照。139张只是两个开发近邻组，新增眼睑扫轴点不自动进入固定139图包；不能把139张随机切开冒充独立测试。

用户首轮标注与至少三名独立盲标者的语义验收是不同证据。多人原始七类标签应保留；surprise/fear只在五类评估映射时合并，disgust集外去向单报。ambiguous须经协议复核后解释为unknown，不依据模型分数补标签。

## 训练复现与真实效果证据

当前s42合成复现产物在`ML/expression-recognition/artifacts/runs/laila_cand_v2_repro_s42_20261003/`；核对结果为checkpoint参数和评估数组与旧候选完全相同，不是效果提升。

| 数据角色 | 数量与限制 |
| --- | --- |
| 合成正训练／验证 | 8482／1518，来自同一套FACS原型随机切分，不是按独立真实配方组锁测 |
| 合成负训练 | 7000，seed143，拒识训练 |
| 温度校准 | 复用1518条验证正样本；已用于选最佳epoch，不是独立校准或锁测 |
| 拒识阈值校准 | 2000条合成负样本，seed345，仅定门槛 |
| 负样本留出 | 2000，seed244，不训练或定门槛；仍与训练负样本同一生成体系 |
| 真实独立训练／测试 | 尚未形成完成多人复核和适配的数据集 |

合成验证argmax为1499/1518（98.7484%）；含拒识最终正确为1466/1518（96.5744%），拒识43条（2.8327%）；留出合成负样本拒识94.3%。这些不能写作当前玩家表情准确率。

标注器阶段25项相关pytest通过（5项标注器＋20项采集诊断），实际浏览器截图／快捷键／下载证据见操作说明。源码阅读和文档lint不替代这些测试，也不代表本次文档同步又执行了一轮Unity验收。

用户首轮意见已读核查，revision562快照为113张有标签／26张空标签／0跳过，其中24张空标签有“疑惑／无辜”等超出选项的备注；不得视为全部未标或自动补中性。五类范围91张，现ONNX argmax一致63、原阈值最终一致43、拒识36。完整分布、备份、版本、逐图结果及备注语义缺口由ANNOTATOR.md的首轮核查段维护；这是仅两个近邻组上的单人开发诊断，不是独立测试或真实泛化率。

## 拒识与语义证据边界

反馈模型保留五类原始概率，主标签按中性区与迟滞显示；历史 energy / 置信度门槛只用于详情诊断。
原 13 张 disgust 均来自已用于开发的近邻组，且已被多次查看，没有独立多人集外共识，不能作为锁测。
该反馈候选接受其中 11 张；统一 energy / 最高分门槛若保住当前 110 条正确五类结果，至少 9 张仍会被接受。
这个解析边界只适用于该冻结模型与统一规则，不证明其他模型或逐类规则不可分。

独立语义验收的采样量、人数与判据尚未确定，不按研究设想派工。
当前剩余验收是独立人工语义、组合姿态、跨帧稳定性与实际设备；不据此追加训练、扩类或关卡判定。
拒识分析与原始意见角色见 ANNOTATOR 的诊断入口；正式验收样本量与判据须另行确定。

### 2026-10-06五类反馈实现与定向验证

用户授权的当前范围是实时展示，不是关卡是否通关。主标签仅五类，拖动更新期间保留已有展示而不反复清为“识别中”；初次未就绪或真实输入／模型异常仍是等待／明确错误。旧版切换保持历史诊断并重置时序，不跨模型借用旧标签。Feedback负责纯展示状态，Recognizer负责严格验证／推理／原始预测，Config为独立SO，Panel复用现有详情折叠；反馈保存另记display_class／display_policy／neutral_override／display_reason，原class_key和历史accepted_class_key仍分别保留。

`Research/FeedbackRepair20261005/StableFeedback.asset`使用可调**初始参数**：六眉／四嘴角容差0.03，四眼皮／三唇0.02；幅度取`max(abs(raw17[i])/tolerance[i])`，进入中性区≤1、已在区内到≥1.5才离开，单轴显著动作和左右反向不相消。迟滞0.18秒；最高与次高差≥0.25或单轴输入变化≥0.12可立即切换，复位近零立即中性；静止时按已冻结候选的经过时间推进，不靠重复推理凑次数。容差不是可见形变或语义校准，各轴等权重不等可见幅度；完整五类概率不删、不重归一化，也不把低置信度当中性。

实际验证证据在`ML/expression-recognition/artifacts/laila_v2_candidate/stable-feedback-20261006-v1/`：25项定向EditMode实际完成、失败记录0（含6项新展示规则测试）；真实模型零／中性区往返、31单方向、左右相反、全部15档参考完成，12张非中性参考未被中性区吞掉。既有上唇0.25开发样本最高分愤怒36.52%、energy−1.291且旧规则拒绝，现在主界面仍显示愤怒，不转中性；不据此宣称原disgust标签错误或语义准确。缺键／NaN／直接输入维度及非有限／错误模型hash清理、三版切换与恢复通过。9项跨帧UI通过，实际0.26→0.27上唇近分从愤怒稳定到惊恐，静止无需重复推理；复位按钮显示中性，详情分列预测／展示／原因／旧拒识，主标签不含“偏”或“认不出”。记录时间见各JSON的UTC／Unix毫秒，不沿用旧轮时间。

首次新增脚本导入曾报找不到两个新类型，完整资产刷新后编译错误0；首次临时验证脚本JSON匿名对象序列化失败，证据保留，修正后重跑完成。测试脚本注入及指针事件不等于物理鼠标、人工语义或Player验收；未训练、改模型／温度／历史阈值、扩类或接关卡。

## 当前候选与接手边界

| 候选 | 用途与数据角色 |
| --- | --- |
| 原 51D | 同一试玩场景中的旧版对照，合成候选与历史拒识 |
| r779 59D | 单人开发意见研究候选；旧版对照 |
| 反馈修复 59D | 原始五类概率加独立展示反馈；历史拒识未校准 |
| 定向修复 59D，2026-10-06 | 当前独立试玩默认；补原下睑 / 旧眉三个愤怒目标，固定单轮训练 |

定向候选 SHA256 为 `11f828902e4cd26eb6488a1f744a9bad71d9a06ab76108a50dca547b43bf3b3b`，
资产在 `Research/StrengthRepair20261006/`；上一版反馈的 `StableFeedback.asset` 为共用展示参数。
三套愤怒各三档已在实际 UI 核对；新增三点为训练拟合，同原型其他档位仅为暴露开发回归。
原 111 张五类记录含训练 / 单类校准，独立测试仍为 0；不以 110/111 宣称泛化率。
原始意见、history 与旧模型保持；具体训练设置、角色审计与失败证据从 spec §17 / ANNOTATOR 进入。

指针提示由 `MuralFaceController.Start` 自动创建，`FacePointerFeedback.Awake` 只允许 `LailaRecognitionPlaytest.unity`。
编辑态没有序列化该组件是正常设计；不要为此补绑定或重建控制点。
微小内眉拖动越过中性区后仍可能原始预测中性，单轴语义没有因此完成验收。
研究候选只用于 Editor；构建校验会拒绝含启用研究识别器的 Player 场景。

| 接手项 | 当前边界 |
| --- | --- |
| 开发标注 | 保留原始标签、备注与分组；不擅改为人工金标 |
| 多人复核 | 尚未形成独立数据集；自动 fixture 不能代替 |
| 数据适配 | raw17 成对适配 51D / 59D 已完成；通用 NPZ 加载器仍为 51D |
| 候选比较 | 已有有界训练及对照；不自动开下一轮或拆六类 |
| 校准 / 锁测 | 冻结模型与标签后另定独立方案，开发数据不能充当锁测 |
| 正式接入 | 关卡、Player、正式模型与真实输入验收均未完成 |

`exprnet.train --golden` 只附加评估，不是训练数据导入；标注 CSV 不能直接更名为 NPZ。
眼角自交几何问题仍延期，模型分数不能替代几何与光影验收。

## 源码依据与文档职责

| 实现位置（仓库相对path:line） | 本指南核对的职责 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/LailaFaceRecognition/LailaExpressionSampler.cs:40` | 严格只读采样；`:52`为复用暂存区入口 |
| `Assets/_Project/Scripts/Runtime/LailaFaceRecognition/LailaExpressionRecognizer.cs:38` | 100ms限频；`Initialize` 初始化、`Decide` 历史拒识、`PublishFeedback` 展示 |
| `Assets/_Project/Scripts/Runtime/LailaFaceRecognition/LailaExpressionPanel.cs:45` | 停用退订／释放链路 |
| `Assets/_Project/Scripts/Editor/Tools/LailaRecognitionTools.cs` | 原始采集与相机恢复 |
| `ML/expression-recognition/analysis/laila_v2_candidate/annotator.py:46` | 默认包及版本校验；`:85`保存；`:215`本机HTTP |
| `ML/expression-recognition/exprnet/datasets.py:376` | 合成源；`:388`是51D NPZ源 |
| `ML/expression-recognition/exprnet/train.py:221` | 在验证正样本上拟合温度 |

本指南描述架构与边界；标签键位、保存字段和故障操作由ANNOTATOR.md维护，Python通用命令由README维护。专业FACS依据、旧12轴上限及原始设计继续保存在DESIGN、REFERENCES和历史分析中，不能用当前工程状态改写为已完成人工验证。

输入诊断独立于盲标：六种参考和单轴输入只为人工确认脸型与方向，不写标签/history或训练集；分类最高概率与拒识原因分开，阈值试验不落盘。参考截图通过控制器赋权、Sampler回读及真实网格BakeMesh后相机渲染，临时场景／网格关闭恢复，不保存资产；精确输入匹配才显示截图，无对应值隐藏旧图。正常启动以Python包方式执行，旧服务保存退出再启动才能加载新接口／参考包；MCP恢复后的数值及视觉证据见ANNOTATOR最新段，不等于Play／Player验收。
