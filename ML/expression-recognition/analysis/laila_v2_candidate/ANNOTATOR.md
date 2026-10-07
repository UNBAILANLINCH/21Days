# 首轮开发辅助标注器

当前状态（2026-10-07）：唯一推荐Unity入口为 `21Days → Laila → 候选试玩（含旧版对照）`，默认使用2026-10-06定向修复59D，可依次切上一版反馈59D、旧r779 59D、原51D、定向修复59D。唯一 `LailaRecognitionPlaytest.unity` 承担候选与配方对照，原51D在同场景切换；重复旧场景和一次性搭建代码已移除。授权单轮训练、205条Sentis及21档实际UI通过，三套愤怒9档均原始／展示愤怒；三个新增目标仅训练拟合，同原型其他档位不算独立测试。历史训练未覆盖原模型、标签和门槛；单轴内眉显著拖动仍模型中性，独立语义、物理鼠标、完整拒识及Player仍缺，不要求先补26图。本页历史训练与诊断记录保留；详见[本轮Unity证据](../../../../ai-docs/docs/modules/lailafacerecognition/lailafacerecognition-module-guide.md#2026-10-06定向候选unity接线与验证)。


功能状态：本地标注、保存、续标、导出及raw17＋单人标签成对51D／59D适配已验证；r696与r779研究微调分别存档，通用训练加载器未改。多人标签合并和独立语义验收未完成；历史r696指标不套用到最新r779，最新逐类结果见本文末尾。当前工程契约从[spec§17→§16](../../../../PRP/laila-expression-recognition/spec.md#17-当前工程候选接手入口2026-10-04)进入；Unity职责见[识别模块指南](../../../../ai-docs/docs/modules/lailafacerecognition/lailafacerecognition-module-guide.md)，本页负责标注操作和记录交接契约。

## 用途与启动

双击子项目根目录的 [launch_annotator.cmd](../../launch_annotator.cmd)，浏览器自动打开本机页面。无需安装依赖、打开Unity或运行训练。填写自己的标注者代号，点“开始／续标”；再次启动会记住最近保存的代号，也可填写另一代号建立独立记录。

默认入口固定使用已验证的139张匿名dev图、两个近邻组，保持原盲标随机顺序。启动时核查三份空CSV、图片和manifest一一对应，以及原始dev JSON分组和哈希；默认包拒绝test、缺图、重复ID或版本变化。独立试点使用下节新增入口，保留test角色但不开放test预测。盲标页面不会展示模型预测、配方或key名称；显式打开诊断后不能再将意见称作盲标。单人意见不是独立金标。

### Unity直接拖脸试玩（2026-10-04）

菜单 `21Days → Laila → 候选试玩（含旧版对照）` 单独加载研究场景并进入 Play。左侧有五类 / 轻中强 / 复位 / 原下睑与旧怒眉对照；模型按钮按定向修复 59D→上一版反馈 59D→r779 59D→原 51D 循环。默认资产为 `Research/StrengthRepair20261006/laila_research59_strength_20261006.onnx` 与同名 JSON，hash `11f828902e4cd26eb6488a1f744a9bad71d9a06ab76108a50dca547b43bf3b3b`。

菜单拒绝已有 Play、脏或未保存场景；退出恢复原加载 / 活动场景配置，不自动保存用户场景。研究模式禁止正式 Player 构建。详情区分别显示原始分类、稳定展示、概率 / energy 与旧拒识参照；配方名称和模型概率均不作为人工标签。当前模型、资源释放与 205 输入 / 21 档 UI 证据集中见 [模块指南](../../../../ai-docs/docs/modules/lailafacerecognition/lailafacerecognition-module-guide.md#2026-10-06定向候选unity接线与验证)。

现场反馈先主动选五类 / 不确定 / 难以表达，备注可空，再保存当前脸。记录同步冻结 raw17、31 权重、实际模型版本 / 输出及 PNG；保存在 `artifacts/laila_v2_candidate/playtest-feedback/batch-时间-随机ID/记录UUID/`。重进 Play 新建批次；重复保存、失败重试与渲染状态恢复已有检查。十张模式先隐藏预测、主动选择并保存后揭示，完成即停止；目标提示不代填标签，开发反馈不自动训练。真实十张与后续授权修复的来源、角色和结果见 [反馈记录](PILOT_FEEDBACK_20261005.md)。

独立语义、完整拒识、物理鼠标与 Player 尚未验收。显著单轴内眉仍可能模型中性，眼角几何限制保留；不由现有试玩派生新采样或训练任务。

### 新批次与新59D查看

双击 [launch_pilot.cmd](../../launch_pilot.cmd)，它使用独立服务和记录目录，不必关闭原139张标注窗口。没有新采集时，仅开放原参考的只读输入诊断，禁止在此写旧139张标签。点“打开输入诊断”，模型下拉明确选“新59D候选”，即可查看实际候选分类；默认仍是现部署51D。研究拒识沿用旧温度／门槛并标明“未校准”，不会自动采用单类ID95探针，不写正式阈值。

采集完成后的操作：

1. 在现有Unity菜单`21Days/Laila/采集识别样本（未标注）`采集。校准图选dev、组名用`试点-校准-1`等；测试图选test、组名用`试点-测试-1`等。相同基础姿态的中心／变化两图保持同组；不同角色用不同独立姿态，不从旧139张换名复制。
2. 已有符合下述数据契约的采集后，双击`launch_pilot.cmd`。如果试点窗口已打开，只在该窗口“保存并退出服务”后重开；原139张服务保持运行。无需填写UUID或修改配置。
3. 填自己的代号，使用原七类＋不明确选项和备注盲标，等待自动保存；重开用同一代号续标。锁定测试页隐藏预测入口，诊断样本目录与sample接口排除test。诊断不能识别手动输入究竟来自哪张图，因此不要另行手输测试参数查看预测。

新入口只接收`data/laila-captures-v2/dev`和`test`中组名以`试点-`或`pilot-`开头的真实JSON＋PNG。原UUID、group、split和快照／图像hash保留，复制成不可变版本包`artifacts/laila_v2_candidate/pilot-batches/<版本hash>/`；标签在该包`annotations/`，不同包或代号不自动合并。后来添加采集会形成新版本，已有标注留在旧版本，不自动迁移；建议先采齐一轮再标注。

试点JSON保留`sample_roles`、`training_eligible=false`和`purpose=single-annotator-pilot-role-preserved-not-training`，CSV每行追加group／data_role并保留dev/test。适配及有界重训入口明确拒绝整个试点记录，避免锁测误入训练；并不意味着已完成独立多人金标转换。组跨角色、相同raw17跨角色、缺图／快照／hash变化、重复UUID或角色篡改均拒绝。

浏览器新59D固定加载已核验的`targeted-r779-s42-20261004-v2/new59_tau0.pt`，该浏览器路径不训练或执行Unity。支持归档校准快照、37张离线参考和手动17轴输入；浏览器没有Unity实时渲染，直接拖脸使用前节独立Editor试玩。最高概率分类与未校准研究拒识分别显示，JSON下载保留实际模型hash和`features59_model_used=true`。27条疑惑意见保留原文，未新增输出类。

启动脚本优先使用已有`.venv/Scripts/python.exe`，缺失时调用`uv python find`查找已安装解释器。盲标页只用Python标准库；可选诊断页延迟加载已有numpy、onnxruntime及exprnet依赖，缺少时显示错误，不影响盲标，不自行安装。实际开发验证使用Python3.13.5和本机Chromium浏览器；默认浏览器未关联时，复制启动窗口显示的本机URL打开，不自行安装依赖或上传图片。

同一代号绑定同一份记录，前后空白会被去除；建议使用稳定代号，避免填写姓名等个人信息。同一人换代号会建立另一份记录，不会自动合并。文件名是代号摘要，文件正文仍保存代号；它不是个人信息匿名化保证。

## 标签与快捷键

标注保留原始七类，当前模型的五类输出不限制标注选项。惊讶和恐惧此时分别标，不先合并。

| 按键 | 中文标签 | 保存值 |
| --- | --- | --- |
| 1 | 中性 | `neutral` |
| 2 | 高兴 | `happy` |
| 3 | 悲伤 | `sad` |
| 4 | 惊讶 | `surprise` |
| 5 | 恐惧 | `fear` |
| 6 | 厌恶 | `disgust` |
| 7 | 愤怒 | `angry` |
| 8 | 不明确／混合 | `ambiguous` |

1. 按1–8或点击按钮：中性、高兴、悲伤、惊讶、恐惧、厌恶、愤怒、不明确／混合。类别选择立即保存，不自动翻页。
2. 清晰度和备注可留空；编辑后450毫秒自动保存。页面显示“已自动保存”后再离开。输入备注时快捷键不作用于标签。
3. 左右方向键翻页；S跳过。跳过和未标保持空标签，“清除标签”可重标；修改历史保留。遇到不确定表情可标“不明确／混合”，不要默认填中性。
4. “导出CSV”供表格查看；“导出JSON”保留原文备注、版本、顺序、UTC时间和完整修改历史，供后续合并适配。CSV将可能被表格当公式的备注加前导单引号，原文在JSON中保留。
5. “保存并退出服务”结束进程；也可在启动窗口按Ctrl+C。只关浏览器不会关闭服务，再次双击会打开原服务，不创建第二份写入进程。

清晰度保存值为留空、`clear`或`uncertain`，不代表模型置信度；备注最多4000字。进度分别计已标、跳过和未标。“下一张未标”也会找到跳过样本，先向后找，再从头查找。到首尾时上一张／下一张停在边界。

## 数据位置与版本

下表路径均相对于`ML/expression-recognition/`，不是仓库根目录。原图和记录都在已有忽略范围，导出下载由浏览器保存到其下载目录／用户选择的位置。

| 内容 | 实际路径 |
| --- | --- |
| 本轮原始配对快照 | `data/laila-captures-v2/dev/<UUID>.json`和同名PNG |
| 固定139张起步包 | `artifacts/laila_v2_candidate/unity-round-20261003/blind-packet/` |
| 页面使用的匿名图 | 起步包下`annotators/images/<UUID>.png` |
| 原始空标注表 | 起步包下`annotators/annotator_1.csv`至`annotator_3.csv`；界面不会填写这些表 |
| 管理员分组／哈希清单 | 起步包下`steward-manifest.json`；不给标注者，HTTP入口也不提供该文件 |
| 用户自动保存 | `artifacts/laila_v2_candidate/dev-annotations/<代号摘要>.json`及`.bak` |
| 服务／最近代号状态 | 同目录`server.lock`、`endpoint.json`、`last-annotator.json` |
| 浏览器下载文件名 | `laila-dev-annotations.csv`／`laila-dev-annotations.json`，具体下载位置由浏览器决定 |
| 隔离的测试记录 | `artifacts/laila_v2_candidate/annotator-ui-check-20261003/test-records/`；不能混作用户意见 |

139张固定包仅两个近邻组；当前采集目录的后来新增点不自动进入该包。原始采集JSON仍保持`status=unlabeled`，用户标签单独保存，二者不相互覆盖。已标数量以界面和用户记录为准，原始JSON的unlabeled不表示用户尚未标注。

稳定UUID和顺序来自管理员manifest及三份原始空CSV。`dataset_version`是完整manifest entries的规范化SHA256，包含原始快照／图像哈希与分组信息。默认起步包还核对原始dev JSON的id、group、split、rig_hash和fbx_sha256；运行中每次读取PNG再次核对图片哈希。

默认包和标签选项保持原契约；新批次由上节`launch_pilot.cmd`自动准备和验证独立版本，不修改旧包或旧哈希以强行续标。

## 保存与导出契约

记录自动保存到 `artifacts/laila_v2_candidate/dev-annotations/`，文件名为代号摘要，内容保留代号。`.json`为当前快照、`.bak`为上一份有效保存；损坏时保留`.corrupt-*`并恢复备份，页面提示复核最后一次修改。首次保存前没有历史备份。不要删除整个记录目录；导出JSON可另作备份。

| JSON字段 | 含义 |
| --- | --- |
| `schema_version`、`purpose`、`split` | 版本1；`single-annotator-development-not-golden`；固定dev |
| `dataset_version`、`order` | 数据版本和完整稳定ID顺序；不因跳过或改标签重排 |
| `annotator`、`created_at`、`updated_at` | 代号及UTC ISO时间；首次未保存状态可能尚无updated_at |
| `revision`、`cursor` | 保存修订号及当前图片零基索引，用于并发检查和续标 |
| `annotations` | 按UUID保存label、clarity、note、status、updated_at；未操作样本可能没有条目 |
| `history` | 实际改动的id、previous、current和UTC时间；翻页不会伪造一次重标 |

`status`为`labeled`时必须有合法标签；`unlabeled`和`skipped`必须为空标签。未操作图片从`order`还原，不能把缺失annotation补成neutral。跳过保留清晰度／备注；清除标签把状态改回unlabeled，旧标签仍在history中。

CSV为UTF-8 BOM，每个ID一行，字段是`id,label,clarity,note,status,annotator,split,dataset_version,order_index,updated_at`。JSON含全部状态和修改历史，是无损交接依据；CSV没有修改历史。后续适配必须通过UUID和dataset_version关联管理员manifest与原始17轴快照，而不是依赖图片文件排序或CSV行号推断参数。

磁盘保存先写同目录临时文件，flush和fsync后原子替换；已有有效快照先更新上一版备份。备份只覆盖上一版，不是完整灾难恢复保证：磁盘失效或当前与备份同时损坏仍需另存的导出JSON。未确认保存成功时，不能把页面上已选标签当作已落盘。

## 故障与隐私边界

服务仅监听127.0.0.1，随机端口和会话令牌，不上传数据。不同浏览器标签页同时写入会因版本冲突停止，需复制未保存备注后刷新，避免静默覆盖。图像加载或保存失败会显示错误，不会自动填写标签。训练、部署、正式金标转换和多人一致性审核均不在该工具内。

| 情况 | 已实现行为／操作 |
| --- | --- |
| 重复双击 | 文件锁阻止第二份写入服务，使用endpoint重新打开原页面；残留锁文件不等于锁仍被占用 |
| 两页并发改同一代号 | revision不符则拒绝覆盖；复制未保存备注后刷新复核，不自动合并 |
| 丢图、改图或包版本变化 | 启动校验报错／页面停止标注；恢复正确原始包，不自动补图、补中性或跨版迁移 |
| 保存JSON损坏 | 备份也须通过版本检查；有效则保留损坏原件并恢复上一版，否则报错保留现场 |
| 关闭页面但没退出 | 服务继续运行；再次双击回到同一服务。需要结束时用页面退出或启动窗口Ctrl+C |
| 磁盘无写权限／写入失败 | 保存错误并暂停；先复制未保存备注，解决权限／空间后刷新，检查实际保存状态 |
| 浏览器拦截下载 | 自动保存仍在本地；检查浏览器下载提示。导出点击前会先等待保存，失败不伪造成功 |
| 无解释器／打不开默认浏览器 | 启动窗口保留错误，或手动打开窗口给出的本机URL；不默默下载环境 |

HTTP要求正确本机Host和随机令牌，跨站写入受Origin检查；不是远程多人服务。不要分享endpoint中的访问令牌。浏览器可能保存最近代号和下载文件，备注本身也可能含个人信息；数据留本地不等于无需管理本机访问权限。

## 本轮验证与下一步

验证证据：`artifacts/laila_v2_candidate/annotator-ui-check-20261003/ui-check.json`及`ui.png`。测试标注只写在该目录的`test-records/`，不混入用户记录。

标注器开发阶段运行`tests/test_dev_annotator.py`和`tests/test_capture_diagnostics.py`，合计25通过，其中标注器5项、复用诊断20项。覆盖保存／续标、revision冲突、修改历史、空值／跳过、八个标签、CSV/JSON、备份恢复、原子写中断、中文目录、缺图、test拒绝、版本不符及重复启动锁。浏览器实测记录为Chrome/154.0.8037.93，验证数字及方向快捷键、输入框忽略快捷键、中文备注自动保存、刷新续标、导出按钮下载、匿名图加载、令牌和manifest不可访问。

这是此前实现阶段的测试记录，本页文档同步不重复启动UI或操作Unity。未由此完成手动双击习惯、长期断电恢复、独立多人一致性、真实分类性能或Player验收。

数据交接保留原始 JSON、稳定 ID、版本、空值 / 跳过、备注与分组。单人意见不自动转为 `human-labeled`；现工具没有多人合并脚本。适配包按下文关联 raw17，不能从有损 51D 反推附加信息，`--golden` 仅用于评估。

实现依据：`annotator.py:46`（Dataset）、`:85`（Store）、`:192`（InstanceLock）、`:215`（本机HTTP）、`annotator.html`（交互）、`launch_annotator.cmd`（解释器选择）；标签训练边界见`../../exprnet/datasets.py:376`和`:388`。

### 首轮用户意见核查快照（2026-10-03）

按用户“已标注完”后的授权读取唯一标注者记录，快照为revision562、UTC更新于`2026-10-03T23:21:19.695147+00:00`。这不是自动宣布139张分类完成：实际113张有合法标签，26张标签为空，跳过0；其中24张空标签有“疑惑”“无辜／茫然”等备注，另外2张无备注。这些备注揭示当前七类选项的语义覆盖问题，不能自动替换成中性或ambiguous。

| 用户原始标签 | 数量 |
| --- | ---: |
| neutral／中性 | 50 |
| happy／高兴 | 7 |
| sad／悲伤 | 10 |
| surprise／惊讶 | 12 |
| fear／恐惧 | 7 |
| disgust／厌恶 | 9 |
| angry／愤怒 | 5 |
| ambiguous／不明确混合 | 13 |
| 空标签 | 26 |

清晰度139张全部留空，不能由备注或模型概率推断低清晰度；31张有备注，232项修改历史保留。139图／三份起步表／原始17轴及31权重／图片和FBX/rig哈希／dev分组均核对，仍只有两个近邻组。未发现重复ID、非法标签或图片缺失；137个annotation条目不等于137张已选分类，未操作与已清除标签都以空值排除。

快照和上一版备份独立保存在`artifacts/laila_v2_candidate/user-dev-review-20261003-r562/annotation-snapshot.json`及`annotation-previous.bak`。原记录和history未改。该目录的`diagnosis.json`保存逐图用户意见、原始参数、现部署ONNX概率／energy与最终判定，`sample-results.csv`是逐图开发结果；不是自动金标或新训练数据格式。

只在五类比较视图合并surprise/fear后，91张属于已知五类范围：argmax与用户意见一致63/91（69.23%），原阈值最终一致43/91（47.25%），拒识36/91（39.56%），其中20张argmax一致却被拒识。非中性已知41张仅1张最终一致，整体一致率受50张中性影响，不能只报总分。

| 用户类别 | 数量 | argmax一致 | 含拒识最终一致 | 拒识 |
| --- | ---: | ---: | ---: | ---: |
| 中性 | 50 | 50 | 42 | 8 |
| 高兴 | 7 | 4 | 0 | 7 |
| 悲伤 | 10 | 2 | 0 | 4 |
| 惊讶／恐惧（仅比较合并） | 19 | 7 | 1 | 16 |
| 愤怒 | 5 | 0 | 0 | 1 |

disgust9张是当前模型集外，单报去向：拒识6、接受为中性2／愤怒1，不计入五类一致率。ambiguous13张只作为用户不明确参考，拒识7／接受中性6，不能叫独立unknown真值或FPR。26空标签完全排除，不因模型argmax为中性就补标签。阈值和部署模型均未变化；分析为离线ONNX重放，不是新的Unity实测。

备注与空值按原意见保留，不自动改为中性、unknown 或新类别；该轮记录仅用于开发问题定位。

补充记录`supplement.json`列出空标签页码及映射碰撞：26张空标签中，无备注的是界面第18／96张；其余24条备注为“疑惑”21、“无辜／茫然”1、“疑惑／无辜”2。139点有143对不同raw17被压为相同51维输入，但此快照没有已知五类标签冲突的碰撞对；信息保留候选尚不能承诺分类收益。原备注不改写，是否在七类任务中按ambiguous处理或另研究新目标类别须先明确。

### 补标后的r696与本地适配（2026-10-03 UTC）

实际保存为r696，更新时间`2026-10-03T23:40:52.457005+00:00`。唯一用户记录与旧r562的139个稳定ID、顺序、dataset_version均一致；仅第16张从空值补为surprise（备注仍“疑惑”），第18／96张补为neutral。原记录、原始采集、旧r562诊断及用户index未改。

| 原始类别 | r696数量 |
| --- | ---: |
| neutral | 52 |
| happy | 7 |
| sad | 10 |
| surprise | 13 |
| fear | 7 |
| disgust | 9 |
| angry | 5 |
| ambiguous | 13 |
| 空值 | 23 |

116张有分类，23张空值均有备注，无空值无备注；清晰度仍全空，31张有备注，236项历史。备注原文分布：“疑惑”22、“无辜/茫然”1、“或是疑惑？”2、“带点不屑”1、“疑惑/无辜”2、“6+7？”1、“6+7”1、“疑惑？”1。未把这些备注改写为ambiguous、中性或新输出类。

`annotation_adapter.py`显式选择一份记录，不自动发现或合并多人文件。按UUID／图片与快照SHA256／绑定与FBX hash关联；核查完整17轴、31权重及一致性、重复／非法标签／JSON字段、修改历史链与当前意见、dev包版本和近邻组跨split泄漏。所有行保持原dev，没有随机划分train/test。

成对包包含`manifest.json`、`features.npz`、逐图`diagnosis.json`、原字节`annotation-original.json`、上一版备份、rig/canonical配置快照及139份原字节`capture-originals/<ID>.json`。manifest给出源文件和产物hash、revision、history索引、原标签／空值／状态／备注／清晰度及来源。NPZ以同一ID顺序保存raw17、weights31、x51、x59、名称、原标签及五类候选mask；59D前51维与51D逐元素一致，追加有符号眉轴0/1/3/4和遗漏负方向7/9/12/13，当前139点还原raw17最大误差`4.76837e-8`。59D仅信息保留候选，没有分类收益结论。

含字面“疑惑”的备注建立`confused_candidate`复核tag，并保留逐图原文依据；“无辜/茫然”单独保留，未被等同疑惑。tag不是类别、监督标签或OOD真值。disgust、ambiguous、空值默认不入五类监督；surprise/fear仅在五类视图合并，原标签不变。五类候选94张：neutral52、happy7、sad10、surprise_fear20、angry5，仍仅两个近邻组，`training_ready=false`。

实际现部署ONNX重放，阈值未改：argmax一致65/94（69.15%）、含拒识最终一致45/94（47.87%）、拒识36/94（38.30%）。按类argmax／最终一致／拒识分别为中性52/44/8、高兴4/0/7、悲伤2/0/4、惊讶恐惧7/1/16、愤怒0/0/1；非中性42张最终仅1张一致。disgust9张去向拒识6／中性2／愤怒1；ambiguous13张拒识7／中性6；空值23张拒识3／中性20，三组不计入五类一致率。

27张`confused_candidate`全部argmax中性，最终中性23／拒识4，仅报模型输出分布，不算错。其中精确“疑惑”22张中性19／拒识3，“或是疑惑？”2张中性1／拒识1，“疑惑/无辜”2张中性2，“疑惑？”1张中性1；独立的“无辜/茫然”1张中性1。逐图概率、energy和判定在diagnosis中。

产物位于`artifacts/laila_v2_candidate/annotation-adapter-r696-v1/`；同一实现重算旧r562到`annotation-adapter-r562-v3/`，复现91候选、argmax63／最终43／拒识36，没有覆盖原r562目录或此前未跟踪的v1/v2产物。Windows工作区实际拒绝目录rename，使用独占建目录、逐文件复制校验hash、manifest最后发布；`.partial-*`保留为中间证据，不当成功产物。完成包以manifest及其全部文件hash校验为准。

复现命令（在Python子项目目录，`--out`必须换用尚不存在的目录）：

```powershell
.venv\Scripts\python.exe -m analysis.laila_v2_candidate.annotation_adapter --annotations artifacts/laila_v2_candidate/dev-annotations/8254c329a92850f6d539dd37.json --previous artifacts/laila_v2_candidate/user-dev-review-20261003-r562/annotation-snapshot.json --out artifacts/laila_v2_candidate/annotation-adapter-r696-new --replay-model ../../Assets/_Project/Data/LailaFaceRecognition/laila_cand_v2_s42_20261003.onnx
```

本轮适配器＋标注器＋采集护栏＋17轴候选测试56通过（含历史链、跨版本、重复／非法标签、缺图／快照／hash、组泄漏、空值政策、59D随机及边界还原、Windows目录rename拒绝）。未训练、未调阈值、未改ONNX／Unity／眼角；不是新Unity或Player验收。

后续采集协议与预算尚未确定；现有空值、备注与原始七类标签保留，不为凑训练数量代填。

### 一次有界候选训练（2026-10-04）

版本存档已核验：`f7b4646`保存12个标注／适配／测试文件，`c4387b4`为spec存档（原`e0decae`仅改写提交说明）。既有标签备份三个文件字节hash均匹配，该次存档使用r696快照；后续记录已到r779，未重算或重训。当前ONNX hash仍为`7e63c6fc1f2b4da857fbe4a3ba00885bf43f39b6483e3b572d65c0ad6b93396f`，energy阈值仍−1.5637034177780151，min_confidence仍0.4。

`retrain_candidate.py`复用现有ResMLP、ExprLoss和成对适配器。通用`exprnet.train`会重切样本且只支持51D，不能直接用于本轮整组划分／59D／旧权重微调，故使用范围限定的研究脚本，不改主训练框架。51D从原checkpoint开始；59D复制全部原权重，输入层追加8列初始为0，起始预测与旧51D相同。五类不变，disgust／ambiguous／空值均不训练，没有备注伪标签、合成样本、数据增强或Outlier Exposure。4张含疑惑备注且有明确五类标签的样本实际进入全部4份候选训练：第16张surprise（`879d75c1bd2547cbad1b18210a0e2735`）、第20张neutral（`34dd7013f89b4ac29efbace05eac0f0d`）、第26张neutral（`0f35a239c482424296dd55201716f436`）、第73张sad（`64f270a804c04b119d51d6f4ba38fa78`）。它们均属sweep组，按原标签监督并保留备注；其余疑惑tag不产生补标签。

单次实验固定seed42，CPU2线程，确定性运算，AdamW学习率3e−4、weight_decay0.05、batch64、label_smoothing0.1、logit_adjust_tau1。每个候选固定80轮取末轮，不用留出组选epoch、不做超参搜索。两个维度分别完成整组留出与全dev拟合，共4份研究checkpoint；不是四次调参寻优。

近邻组`round20261003-sweep`包含88张五类标签；`round20261003-pairs`仅6张惊讶／恐惧（surprise4、fear2）。整组实验仅以88张训练，6张留出，不同组没有相同raw17；6张距训练样本最近raw17欧氏距离约0.609–1.532。所有组本来都是开发材料，先前已经看过旧模型结果，因此留出不是独立锁测，更不能代表其他四类。全dev拟合训练全部94张，其结果是训练数据回代。

候选保留旧温度与energy／confidence规则，仅作固定参数诊断；模型logits已改变，绝不能把这称为新候选完成拒识校准，或将旧绝对energy阈值直接部署到新模型。

| 相同94张五类dev输入 | argmax一致 | 固定旧规则最终一致 | 拒识 | 已知标签错误接受 |
| --- | ---: | ---: | ---: | ---: |
| 原部署 | 65 | 45 | 36 | 13 |
| 51D全dev拟合 | 90 | 43 | 51 | 0 |
| 59D全dev拟合 | 93 | 55 | 39 | 0 |
| 51D大组训练（本表混含88训练＋6留出） | 90 | 42 | 52 | 0 |
| 59D大组训练（本表混含88训练＋6留出） | 91 | 42 | 50 | 2 |

真正整组留出的6张：原模型argmax6/6、最终0/6、拒识6；51D及59D大组训练候选均argmax6/6、最终6/6、拒识0。全dev拟合后这6张是训练数据，不能再报为留出指标。此处不计算五类测试macro-F1，因为留出只有一个合并输出类。

| 原标签五类视图 | 数量 | 旧argmax／最终／拒识 | 51D全dev拟合argmax／最终／拒识 | 59D全dev拟合argmax／最终／拒识 |
| --- | ---: | --- | --- | --- |
| neutral | 52 | 52／44／8 | 48／3／49 | 51／14／38 |
| happy | 7 | 4／0／7 | 7／7／0 | 7／7／0 |
| sad | 10 | 2／0／4 | 10／8／2 | 10／9／1 |
| surprise_fear | 20 | 7／1／16 | 20／20／0 | 20／20／0 |
| angry | 5 | 0／0／1 | 5／5／0 | 5／5／0 |

逐类混淆矩阵和139张所有原标签对应输出在`comparison.json`。59D全dev拟合的argmax仅一张neutral被判sad；但固定旧规则下38/52张neutral拒识。disgust的集外接受由旧3/9增至两个全dev候选7/9（59D为sad1、surprise_fear2、angry4），不能只看已知类错误接受降低。ambiguous13张只报输出，旧接受6／拒识7，51D接受5／拒识8，59D接受7／拒识6；空值23张旧接受20／拒识3，51D接受12／拒识11，59D接受14／拒识9，不算已知分类错误或OOD真值。

27张含“疑惑”的备注仍未改标签：原模型最终中性23／拒识4；59D全dev候选最终中性3、sad6、surprise_fear6、angry2、拒识10，仅报输出分布，不计错。没有新“疑惑”输出类。

该r696轮次结论：人工dev微调可明显拟合现有非中性意见，59D全dev拟合比51D多3张argmax一致；这是同一开发集上一个种子的结果，不证明特征普遍胜出。由于中性拒识明显恶化、disgust集外接受增加、无覆盖五类的独立评估和新拒识校准，未替换部署。此后已完成文末r779针对性研究，旧候选和证据继续保留。

产物目录`artifacts/laila_v2_candidate/retrain-r696-s42-20261004-v1/`含4份`.pt`、完整成对输入、源manifest、config、脚本快照、comparison及15份文件hash。`baseline-backup/`保存原字节ONNX／metadata／checkpoint／rig／canonical／标签JSON，实际从备份加载推理：概率最大误差2.38419e−7、energy1.19209e−6、checkpoint logits误差0；原文件训练后hash再次一致，具备可核验恢复来源。59D研究checkpoint不是现有51D加载器／ONNX导出／Unity metadata兼容版本，51D候选也未导出或重新校准，不接正式场景。

实际命令（Python子项目目录；输出必须是尚不存在的新目录）：

```powershell
.venv\Scripts\python.exe -u -m analysis.laila_v2_candidate.retrain_candidate --annotations artifacts/laila_v2_candidate/dev-annotations/8254c329a92850f6d539dd37.json --out artifacts/laila_v2_candidate/retrain-r696-s42-20261004-v1 --epochs 80 --seed 42
```

有界训练约6.57秒；相关pytest61通过（原56＋新5），新增测试核查零扩展59D初始等价、整组／相同raw17泄漏、缺类与预算拒绝、原模型不变及同seed重复、错误接受与不明确输出分开。4份候选checkpoint逐一重载得到完全相同输出，全部产物hash独立重读核验。没有运行Unity、构建、网络上传、push或新的commit，既有指南暂存hash未变。

### 输入诊断与经典参考（2026-10-04）

后续真实截图接入：本会话通过临时STDIO MCP实际读取`21Days@6860e04e`空闲／非Play状态，临时附加原laila场景，使用现有Face控制器和严格Sampler完成6参考＋31单方向回读，Sentis按现部署模型推理。第一版同帧SkinnedMesh渲染未及时反映权重，视觉检查发现图片相同，v1保留但不接UI；v2使用Unity对当前权重BakeMesh，在相同变换／材质／相机下用临时MeshRenderer渲染，finally销毁临时网格、恢复权重、关闭附加场景并回到原空白场景。未进Play或保存场景，前后编辑器仍空闲且无脏场景。

通过核验的`artifacts/laila_v2_candidate/reference-inputs-20261004-v2/`含37张真实Unity网格截图、jobs、manifest、保护hash及verification。17轴与目标、31权重逐项一致；六参考图hash不同，遗漏负方向7/9/12/13与neutral图像不同而51D相同、59D不同。全部37点Sentis／ONNX最大误差概率`2.3887834e-7`、energy`1.1970007e-6`；59D还原raw17最大误差`4.7683716e-8`。原标签／模型／rig／FBX／laila场景等受保护文件hash均未变。参考图不是语义金标，惊讶／恐惧等名称仍待用户视觉确认，既有口腔／眼角几何限制未修复。

诊断页选择经典参考即显示精确匹配图，原始截图列表额外提供31个单方向核线图；图片区移到滑杆表前，方便先看脸。图片／manifest版本和hash、37点完整性、17／31一致性、预设变化／非法键／重复／非有限权重均受护栏约束。修改到无精确对应输入时隐藏旧图，不伪装实时渲染。139张包及用户标签/history不扩展，截图不自动加入训练数据。新增截图包测试后全套70通过，隔离浏览器逐一确认六图、归零恢复、单轴图、原始139图、分类与拒识及下载；真实启动入口自检保持通过。

参考图是离线烘焙结果；服务升级需先保存退出旧页面再重启，不能仅刷新浏览器宣称已加载新实现。

启动入口修复：`launch_annotator.cmd`的venv与uv两条分支均从自身目录以`-m analysis.laila_v2_candidate.annotator`启动。之前直接执行脚本路径使Python搜索路径落在子目录，诊断延迟导入报`No module named 'analysis'`；此前包根目录下的页面测试未覆盖这个入口，不能作为双击可用的证明。可运行`launch_annotator.cmd --check-diagnostic`只读核验实际入口、依赖、六参考、17轴、139快照及ONNX推理，不开服务或写标签；已从仓库根目录及临时目录验证，两者均成功，未添加PYTHONPATH。

旧服务已在运行时，仍会使用旧Python搜索路径，必须先在旧标注页“保存并退出服务”，再双击重启；不要只刷新浏览器或重复双击。此次检查发现用户记录已续改到r779／266条history，保护条件阻止自动重启，没有停止旧服务或覆盖更新。该记录与r696的差异不自动加入旧适配或训练结果。修复后全套65测试通过（新增真实Windows启动入口回归）；隔离浏览器验证全部六参考、归零恢复、17轴、分类／拒识、精确归档图及下载，证据为`artifacts/laila_v2_candidate/input-diagnostic-launch-fix-20261004/`。隔离浏览器测试不是旧服务已重启的证明，正常服务完整浏览器验证仍待旧页保存退出后执行。本轮用户记录hash前后相同，部署ONNX仍为原hash。

使用同一个启动脚本，首页点“打开输入诊断”。若已有旧服务运行，先在旧标注页保存并退出，再启动以加载新接口。诊断页与盲标页分开；诊断不读取或修改人工标签，不能用看过预测后的意见冒充盲标。

1. 选择中性、高兴、悲伤、愤怒、惊讶或恐惧的设计参考；这些raw17配方等待实际脸型确认，不自动作为训练标签。支持归零、恢复参考及单轴模式。
2. 查看17轴顺序／范围、31形态正负权重（值×100）、正负规范映射。可载入139张中的原始Unity快照；仅raw17逐元素相同时显示配套旧截图，滑杆改变后无匹配则隐藏图片。没有动态渲染，不拿旧图代替新姿态，也不证明实时接线已经正确。
3. 分类区始终单列最高概率类和五类分数；拒识区单列正式结果、energy／confidence未通过原因及超出量。临时阈值只影响试验结果，不保存至模型、配置、标签或history。下载的`laila-input-diagnostic.json`标明非训练标签，保留当前输入、特征、输出、试验阈值及模型hash。

默认链路为31形态权重→17轴→ONNX内部51D→五类。51D是现有规范特征坐标，不是51个可操作控制；不可达通道仍占维度。59D前51维相同，追加4个有符号眉轴及下眼皮Down／嘴角In的4个负方向。此诊断历史阶段仅显示59D特征；当前显式候选推理见前文“新批次与新59D查看”。L/R沿用形态命名，部分规范通道对侧映射需在实际脸型逐轴确认，不凭名称判断接反。

正式决定先验证5维概率有限、0–1、总和误差≤0.001及energy有限，异常为识别错误；合法输出在`energy > -1.5637034177780151`或`max(probs) < 0.4`时拒识，两边界相等通过。温度0.487122944970898，energy阈值来自2000张合成负样本、seed345的第5百分位（TNR95），不是139张真实脸型标注校准。运行时还先检查模型／metadata／hash／维度，以及31形态存在、权重范围−0.001至100.001、同轴正负权重不能都大于0.001；失败不算正常拒识。输入变化使旧结果失效，推理最多10Hz。

优化顺序：先确认真实输入方向和脸型，再定位argmax不一致，最后检查分类正确却被拒识；只放宽阈值会同时增加错误接受，不能修复分类。候选logits变化后旧energy阈值不能视作已完成校准。此功能没有重训、部署59D、修改正式阈值或提出新采集任务。

实现为`input_diagnostic.py/html`，由现有HTTP服务延迟加载。新增3项测试＋原61项共64通过，覆盖输入越界／非有限／布尔值、概率与拒识边界、四个丢失方向、59D前51维一致、原标签不变、快照精确匹配、HTTP令牌及隔离记录。默认pytest临时目录权限不足，使用新专用临时目录完成复跑。该64项测试阶段Unity MCP尚未可用，未操作编辑器；随后临时STDIO连接完成本节开头37图核查。任意姿态实时渲染与Player验收仍待完成。

浏览器实测Chromium151.0.7922.34通过参考选择、17滑杆、单轴清零、原始截图精确载入、修改后隐藏旧图、临时阈值／恢复及JSON下载；隔离测试记录目录无标签写入。修复输入改变时仍显示上一轮“推理完成”状态的问题。证据位于`artifacts/laila_v2_candidate/input-diagnostic-ui-20261004/`，含`ui-check.json`、`ui.png`和非训练标签的`diagnostic-download.json`。文档gc仍报原有Gameplay命名空间3处与Dynamic字体资产问题，本任务不修改这些文件。

### 最新r779五类评估与针对性训练（2026-10-04）

r779原字节SHA256为`5c524a26287801e7ca37937ad761351312815137fd0fc6d86548ea9f634f54c9`，139个稳定ID／版本未变，266项history完整保存。原始类别：neutral53、happy7、sad16、surprise22、fear7、angry6、disgust13、ambiguous14、空值1；五类111。相对r696新增17张五类意见，仍是原来两个近邻来源组，不能改称独立测试。原标签、备注与原始采集未改；27条疑惑tag中现在有20条明确五类标签，按原标签监督，不统一改中性、unknown或新类别。

冻结包为`artifacts/laila_v2_candidate/annotation-adapter-r779-20261004-v1/`，保留原字节标注、139原始快照、raw17／31权重／51D／59D、版本差异、history与145个配套文件hash。最新复评为`latest-review-r779-20261004-v1/`；逐样本列各候选训练成员、训练时标签／当前变化、训练组重叠、最近raw17距离与同姿态标志。r696整组候选重叠88张、全dev候选94张；新增标签不能消除其来源组已用于开发的风险。

| r779上的模型（全部为开发诊断） | argmax一致/111 | 旧规则最终一致 | 拒识 | 正确却拒 | 已知错误接受 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 旧部署51D | 66 | 46 | 39 | 20 | 26 |
| r696 51D整组训练 | 95 | 46 | 61 | 49 | 4 |
| r696 51D全dev拟合 | 96 | 47 | 59 | 49 | 5 |
| r696 59D整组训练 | 97 | 48 | 56 | 49 | 7 |
| r696 59D全dev拟合 | 100 | 62 | 45 | 38 | 4 |
| 新51D，tau0 | 104 | 104 | 0 | 0 | 7 |
| 新59D，tau0 | 110 | 110 | 0 | 0 | 1 |
| 同数据新59D，tau1对照 | 106 | 76 | 33 | 30 | 2 |

新实验固定seed42、末轮80、原ResMLP权重、AdamW lr3e−4／weight_decay0.05／batch64／label_smoothing0.1，不选epoch或扫参，无增强／合成／OE。sweep105张训练，pairs6张仅惊讶／恐惧作校准，独立test为空；四样本敏感性训练101张。只有两个组，无法造出三个互斥角色；不把sweep内部强度／镜像当新独立组。已完成三份候选及一个同数据先验对照，校准组不用于挑维度、参数或epoch。

| 类别 | 数量 | 旧argmax一致 | 旧最终一致 | 旧正确却拒 | 旧错误接受 | 新51D argmax | 新59D argmax | 新59D单类校准探针最终一致 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| neutral | 53 | 53 | 45 | 8 | 0 | 49 | 53 | 21 |
| happy | 7 | 4 | 0 | 4 | 0 | 7 | 7 | 1 |
| sad | 16 | 2 | 0 | 2 | 12 | 15 | 16 | 12 |
| surprise_fear | 29 | 7 | 1 | 6 | 10 | 28 | 29 | 17 |
| angry | 6 | 0 | 0 | 0 | 4 | 5 | 5 | 2 |

111张包含105训练＋6校准，上表不是独立准确率。新59D唯一分类不一致是第93张`a44945e0783d4b5e8fa39c76d813c66c`：原标签angry、备注“疑惑”，判surprise_fear；保留原意见，不据此改标。同数据tau1对照中neutral分类50／拒33，tau0为53／拒0，但angry从6变5、disgust接受从9增12；这是一个种子下的明确取舍，不证明tau0全局更好。

最新五类标签有3对51D相同特征异标签：第5／99张neutral／sad、第50／98张sad／neutral、第53／62张sad／surprise；raw17与59D没有这些冲突。51D丢信息使这些对不能同时分类正确，继续同维度训练不能恢复区别。标准化质心基线只用训练组算尺度／中心，分类88/111（neutral46、happy7、sad10、surprise_fear22、angry3）；概率与energy未获语义校准，不能直接部署。现有ResMLP确有开发拟合价值，特征保留也有实际标签冲突证据。

原r696的四个明确标签＋疑惑备注样本在新51D／59D中均按原标签分类一致；仅训练时排除四者的59D敏感性为109/111，第20张neutral转为surprise_fear，其余三张分类不变。这些仍出自同开发组，不称独立留出，也不改原标签。当前另16条明确五类疑惑备注继续按各自原标签处理。

拒识单独验证：新59D沿用旧温度／门槛最终110/111，但disgust接受12/13、ambiguous接受14/14、空值接受1/1；后两组不当OOD真值。固定ID95探针仅用pairs6的energy第95百分位，旧温度与confidence0.4保持，阈值不取训练或test。其结果最终53/111、拒58、正确却拒57、错误接受0；disgust仍接受4/13。校准仅5/6通过，6张的分位数也不能支持可靠95%通过率；缺四类与人工unknown，`full_rejection_calibrated=false`。所有候选不接Unity；不凭高训练分放宽部署门槛。

主要产物：
- `artifacts/laila_v2_candidate/targeted-r779-s42-20261004-v2/`：三份checkpoint、split-plan、逐样本与两套拒识混淆、四样本敏感性、局部校准参数、运行命令、源代码快照／依赖版本／源hash；15个产物hash独立核验。
- `artifacts/laila_v2_candidate/prior-control-r779-s42-20261004-v1/`：同数据tau1的单一对照checkpoint与相同校准分析，全部hash独立核验。
- v1/v2相同预算的所有模型指标及逐图输出完全一致，checkpoint实际重载输出逐元素一致；旧r562／r696及原部署保留。

复现先使用冻结r779包，输出换尚不存在的新目录；不要用当前可续改记录冒充历史快照：

```powershell
.venv\Scripts\python.exe -m analysis.laila_v2_candidate.latest_review --bundle artifacts/laila_v2_candidate/annotation-adapter-r779-20261004-v1 --out artifacts/laila_v2_candidate/targeted-r779-reproduce-new --targeted
.venv\Scripts\python.exe -m analysis.laila_v2_candidate.latest_review --bundle artifacts/laila_v2_candidate/annotation-adapter-r779-20261004-v1 --out artifacts/laila_v2_candidate/prior-control-r779-reproduce-new --prior-control
```

本阶段研究已完成；独立多人评估与五类拒识校准仍缺。新增数据的数量和组织方式须由实际验证目标确定，此处不保留未执行的 26 图派工。

本轮相关75项pytest通过，含5项新审计护栏：训练成员／近邻、正确却拒与错误接受、校准只依赖指定组、相同姿态／未知标签校准拒绝、质心只使用训练统计和有损特征冲突。没有运行新Unity／Player或导出候选；原标注与部署hash保持，既有index未操作，无commit／push／上传。

试点入口开发阶段有 83 项相关 pytest 与隔离浏览器验证，覆盖中文备注保存 / 续标、锁测预测隐藏 / 目录排除、实际 59D 输出与未校准提示、JSON 下载以及切回 51D。测试标签只写隔离 fixture；这不代表真实采集或正式独立验收已完成。
