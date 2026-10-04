---
type: module-guide
module: rhythm
layer: runtime
maturity: stable
---
# Rhythm 模块指南

## 范围与状态

PC 四轨下落音游独立试玩。用户要求新场景，入口为 `Assets/_Project/Scenes/RhythmDemo.unity`，不借用 SampleScene。当前场景用于隔离开发；正式 Boot 接线和 UI 预制体制作/迁移后置，尚未接入驯服控制玩法。
使用用户提供的《虫儿飞（伴奏）》MP3，已复制到 `Assets/_Project/Audio/Rhythm/ChongErFei.mp3`。
截取原音频 16～56 秒，至少 3 秒预滚。2026-10-04 按明确授权接入上传调试谱：58 枚（56 Tap + 2 Hold），旧 56Tap 资产保留在 `PRP/rhythm-chart-import-20261004/` 供回退；规则支持 Tap 与按住到尾的 Hold。
原谱来自音频能量起音候选，本轮保留上传的精确 ID、轨道、时间和时长，6 移动、1 删除、3 新增；空审核字段不推断声部意图，不自动加解码补偿，也不据此宣称音乐贴合度验收。来源、授权及验证见 [接入记录](../../../../PRP/rhythm-chart-import-20261004/README.md)。
PC 综合输入偏差支持听声跟拍估算与手动微调，视觉延迟独立手调。没有独立视觉采样向导、多设备身份档案、外部格式导入或复杂谱面编辑器。
2026-10-04 用户取消伪透视：四键 UGUI 轨道改为等宽、直线匀速下落，保留现有配色和可用古风/琴弦元素；不绑定七弦或某种乐器音色。Unity 编译与本轮 EditMode 74/74 已通过，真实视觉验收仍需用户确认，旧透视截图仅作历史。

## 试玩

2026-10-06 UI 导航增量：曲库保留滚动列表、选中高亮、当前成绩详情与直接开始按钮；选曲加载后仍显示曲库，外部指定曲目维持原准备入口。设置统一收纳自动参考拍、手动教学曲试听与视觉滑块，测试/诊断默认隐藏。演奏隐藏设置与旧重开入口，提供退出；结算提供重试和返回曲库。此次未制作新 prefab 或修改场景。

设置调整为未保存草稿，明确保存写独立快照成功后才更新内存；失败保原值并允许重试，取消恢复原值。手动入口使用曲库第一首教学曲音频与完整谱面，结果模式为 Practice，不计个人纪录或通关解锁；滑块只调整原始误差的早晚试听反馈，不实时改判定窗口。自动估计与原值/候选试听及质量分级沿用现有算法。

暂停页已提供继续、重试和退出。暂停冻结 AudioPlayback 双时钟位置、音频源及规则推进；继续重建起点，保留同局规则，丢弃积压和旧时间戳输入。长按在恢复首个 Dynamic 批次核对真实按键，已松开才释放结算。诊断格式只支持单一起点，暂停封为 AudioPaused 不完整记录，恢复演奏但不续写。倒计时重约剩余预滚动，正常播放 Pause/UnPause 并重约停音；原生音频、20 次暂停继续以及保持/释放 Hold 已在本轮 PlayMode 验证。自动参考拍提供独立取消入口。

正确运行中的 Unity 2022.3.62f2 / 21Days 已核对，使用项目既有 bridge 取得真实编辑器与页面。最终曲库 PlayMode 13/13（`Logs/verify/rhythm/20261006-104306/report.md`）通过，含满分 Practice 不计成绩/不解锁、事务失败/慢写、同局暂停、Hold 及结算旧控件隐藏。1227×710、1920×1080、1280×800 页面由 Unity 实际布局渲染，结算修后截图已检查；全演奏组及 EditMode 的最终结果见 [本轮记录](../../../../PRP/rhythm-ui-navigation-20261006/implementation-report.md)。真人手感与设备延迟有效性不由自动输入测试替代。

1. 打开 RhythmDemo 场景并 Play，启动后显示选曲页；初始只有虫儿飞开放，达标后解锁两首进阶曲。
2. 可点击跟拍校准：固定 8 拍适应和 32 拍采样，进度不随漏拍延长。结果区分无输入、未匹配、缺样、不稳定、漂移与建议，并显示分段差；比较原值/建议的参考拍反馈，明确点击应用才替换补偿。可保留继续或完整重测；只有时序覆盖有效且仍可恢复的主轮允许一次固定 8 拍补测，Drift 不提供无效补测。手调范围 -300～+300 ms。
3. 点击开始演奏，倒数后按 D、F、J、K 对应四条轨道。
4. 音符到达横线时按键，右侧显示等级、早晚、补偿后误差与原始误差。
5. 片段结束显示 Perfect、Good、Miss 与最高连击，可重试或返回曲库；演奏中可退出本轮。
6. 返回标题后点击开始可重新进入。窗口失焦、音频配置改变或输入设备断开会终止本轮，可重试。

当前面板另有“短 Tap/Hold 练习”：独立参考拍 9 秒，D/F 各一枚 Tap、J/K 各一枚两秒 Hold；到尾自动成功，早放整枚失败。歌曲、练习可互相切换或重开；自动校准标题明确标识独立节拍器，不能误当虫儿飞音乐。练习谱由 `CreatePracticeRules` 编译，不应用歌曲 chartOffsetMs。

## 内部结构

| 类型 | 职责 |
| --- | --- |
| RhythmInstaller | 只注册本模块配置、状态和独立入口 |
| RhythmDemoEntry | 首次自动进入，订阅标题开始事件，销毁时退订 |
| RhythmConfig | 音频、输入资产、片段与谱面，只读 SO |
| RhythmState | UI、音频、输入、存档及暂停服务接线 |
| RhythmRules | Tap/Hold 判定、分数、连击、漏按，不读取 Unity 时钟 |
| RhythmNoteData / RhythmBpmData | 版本 2 毫秒音符记录与可空的 BPM 制谱元数据 |
| RhythmInputQueue | Press/Release 稳定排序、重复边沿过滤与会话隔离 |
| RhythmCalibrationEstimator / RhythmCalibrationResult | 原始误差、离群剔除、时间分块质量及未确认候选；不管理存档 |
| RhythmTrackGraphic | 等宽四轨、恒定尺寸头部与长条的 UGUI 网格 |
| RhythmHitIntent / RhythmHitResult | 带歌曲时间戳的输入及判定结果 |
| RhythmView | 下落表现、交互、反馈及结算 |
| RhythmCalibrationData | Profile 档案中的输入补偿和视觉延迟，版本 2 |
| RhythmSongData / RhythmCatalogConfig | 稳定歌曲身份、当前唯一谱面和版本、曲库前置关系 |
| RhythmRunResult / RhythmRecordData | 不可变单局交付与同版本完整个人纪录；不拼接不同局的统计 |
| RhythmProgressData / Rules / Archive | v2 档案、版本成绩资格、迁移前原字节备份 |
| RhythmPlayRequest / RhythmExternalSession | 调用方运行/场景身份及一次结果消费；不直接施加伤害 |

规则层不引用 UnityEngine。状态通过框架服务开关 UI、播放音频、保存数据、暂停世界。
Input System performed/canceled 复制带时间戳的意图；onAfterUpdate 记录 Dynamic 输入批次的同域时刻，Tick 先按事件时刻推进和判定，再封口上一批已完成输入的边界。Fixed/Manual 在排程前明确拒绝，演奏中切换则终止，不修改全局输入配置。DSP 只驱动画面与预约停音，不作为输入水位；正常批次送达不会被 DSP 抢先超时。早于已封口水位的迟到输入仍终止本轮并诊断，不回滚；反馈最多等待下一输入批次，不修改原始误差。
本模块不接固定 tick 世界指令或 ReplayFormat，不改既有 InputCommand 布局。

## 时间与判定

歌曲时刻使用框架 AudioPlayback 的 DSP 起点差值，可在倒数阶段为负。
输入时刻使用 CallbackContext.time，映射到同一个播放起点。预约时夹取 16 组 realtime/DSP，选调用跨度最小样本；软件桥接没有物理精度保证。
下落位置由绝对歌曲时刻计算，掉帧时不会累积位移误差。
显示归一位置为 `(目标时刻 - 歌曲时刻 + VisualOffsetMs/1000) / ApproachSeconds`，0 对应判定线、1 对应轨道顶端；头部经过线后继续匀速，按住的 Hold 头部固定在线上，身体超出轨道的部分裁切。旧 FarWidthRatio 配置仍保留兼容读取/校验，本表现不再使用它，不需改谱面资产。
谱面对齐 chartOffsetMs 只移动目标；输入补偿 OffsetMs 只修正输入/超时/自动尾部的统一时间域；VisualOffsetMs 只影响画面。
原始误差为 `(输入时刻 - 对齐后的音符时刻) * 1000`。
补偿后误差为 `原始误差 - OffsetMs`，正补偿用于稳定晚按，负补偿用于稳定早按。
Perfect 窗口 ±65 ms，Good 窗口 ±140 ms，边界包含。
Perfect 得 1000 分，Good 得 500 分；漏按清空连击。Tap 一次结算；Hold 头部只确定等级，保持到补偿后终点自动按头部等级结算整枚，早放或漏头只记一次 Miss，不累计尾部额外分。
同轨寻找窗口内最近的未决音符，一枚音符只能命中一次。
窗口外或已解决音符的重复输入不计分；时间推进批量结算所有过期音符。
这是软件时钟一致性设计，尚未测量本机声卡、显示器和键盘的物理延迟。
毫秒数字是软件判定误差，不能据此宣称物理端到端精度达 1 ms。

## 生命周期

明确采用候选现在先写独立Profile快照，成功后才更新内存与清候选；失败保留原补偿和候选，可重试或保留。保存期间使用独立锁禁止并行采用/试听/开始/换曲与手调；退出等待采用写完成后保存同一当前值。此后续事务补丁已有定向Edit50/50；最后编译及新增真实Profile失败/慢写Showcase因bridge连接中断待补验，不能以此前诊断文件故障回放替代。详见最新实施记录。

Enter 校验配置、读补偿、加载音频、克隆输入资产、暂停世界并打开面板。
保存原 Gameplay 动作图启用状态，演奏期间禁用它，退出恢复。
开始时先停止上一轮，再保存补偿，重新建规则并调度音频。
保存等待期间锁定两个滑块；会话编号防止失焦/退出后的异步保存重新启动。停止先关闭入口和清队列，再 Disable，取消回调不算 Release。试听或补测中断后回到带中断原因的候选结果页，旧补偿保持；退出、换曲和开始演奏清除未确认候选。
开始失败回到准备状态，并记录错误。
退出先停止演奏、退订 UI，再关闭面板；finally 释放输入及暂停令牌。
校准保存失败记录错误并显示通知，不阻断返回标题。
Dispose 也释放音频、输入和暂停资源。

独立 AudioListener.pause、应用暂停、失焦与设备变化均采用终止后重开，不续播旧桥接。开始前及保存完成后再次检查暂停和输入模式；双时钟逆行、非有限值或超过音频缓冲相关容差的残差会终止。普通帧卡顿中两个时钟一起前进不误判。AudioPlayback.ScheduleEnd 在音频线程预约片段终点，句柄保留至输入尾窗结算。

## 会话诊断与试听

校准另用有界的本地 JSON 证据，不冒充演奏 RHD。完成、取消、中断时保留原始四轨 Press 事件时间、映射歌曲时间、目标拍/误差、拒绝原因、离群标记、各块数量/中位数/范围、整体跨度、桥接起点/跨度与事件捕获双时钟、配置策略版本和结果原因；适应期忽略输入也标记。只接收模块现有输入，不监听其他键、不采文本或设备身份、不外传。
文件为 `Application.temporaryCachePath/rhythm-diagnostics/calibration-v1-*.json`，测试改用 SaveRootOverride；header.source 区分真实 runtime 与 isolated-test。每轮最多256条/128KB，仅保留格式专属最近5轮，容量满标记不完整，不影响真实估计。保存成功/失败在结果说明中显示，保存失败原补偿保持；不写 Profile，不自动应用候选。此前真人失败没有原始采样，不能用新增记录追溯历史或把 fixture 数字当真人原因。判定阈值仍不变，真人误拒问题须用下一次真实记录判断。

RhythmDiagnosticSession 复用原 Rules/InputQueue 判定，每轮记录有限容量的输入、完整批次边界、桥接样本及判定。自动 Miss/尾部没有输入误差，使用空值。结束后“保存本轮诊断”显式写入 `Application.temporaryCachePath/rhythm-diagnostics/*.rhd`；测试使用框架 SaveRootOverride 隔离根目录。不自动写盘，不写玩家 Profile；唯一文件名加 CreateNew 避免覆盖。容量满后游戏继续而记录封存，导出明确标记不完整。
EndReason 保存暂停、模式变更、时钟跳变/非法值、迟到等细因；原枚举值和二进制布局不变，新读取器兼容旧文件，旧读取器拒绝未知原因。实际按钮事件导出已验：同轮/新轮四份文件回读一致，旧文件/哨兵保留，目录错误通知及恢复重试通过；当前 UI 无取消对话框。
RhythmDiagnosticCodec 严格检查版本、限额、有限数、顺序和回放一致性；RhythmDiagnosticReplay 使用记录边界复现结果并报告软件桥接残差 median/P95/max。它不重现声卡物理输出，不能证明端到端延迟。接口详见 [诊断契约](../../../../PRP/rhythm-followup/diagnostics-api.md)。

人工试听定位入口见 [工具说明](../../../../PRP/rhythm-audio-review-20261003/locator/README.md)：项目根运行 `node PRP/rhythm-audio-review-20261003/locator/server.mjs`，打开 `http://127.0.0.1:8766`。支持按 ID 定位、波形、短循环、原谱/候选 A/B、click、时间编辑及 JSON/CSV 导出回载。候选要求明确声部和人工依据，来源 hash/版本/冲突严格校验；不覆盖正式 SO，不自动应用 11ms。浏览器解码时间仍需人工与 Unity PCM 对照。

## 资产接线

配置：`Assets/_Project/Data/Rhythm/ChongErFei.asset`。schemaVersion=1 只读旧秒数组适配；版本 2 的 notes 保存 id/lane/timeMs/type/durationMs，旧数组清空。BPM 段只供未来制谱网格，不驱动秒时间判定。
`21Days/音游/迁移选中谱面到毫秒记录 v2` 校验后用 Undo/SerializedObject 迁移并只保存选中资产；禁止运行时改 SO。
输入：`Assets/_Project/Data/Rhythm/RhythmInput.asset`，Rhythm/Lane0～Lane3。
面板：`Assets/_Project/Prefabs/UI/RhythmView.prefab`，Addressables 地址 `RhythmView`。
场景 Addressables 地址 `RhythmDemoScene`。
场景自带框架 Bootstrap、Camera 与 Directional Light，仅挂 RhythmInstaller。
继承现有框架配置，不修改正式 Boot 的模块注册。
音频使用 PCM 解压加载，为开始调度准备采样数据。
编辑器工具 `21Days/音游/创建四轨试玩场景` 负责创建资产，已有场景时拒绝覆盖。
只保存本次创建的资产及相关 Addressables 分组，不全局保存其他脏资产。

## 保存与埋点

当前校准策略 `median-interval-permutation-v2`：有效样本数、时段覆盖、±300ms与过滤MAD有效性检查保留。全部匹配样本用于中位数95%秩区间，依赖独立同分布假设；候选到区间两端的最大距离超过20ms降为低可信。分块差超过20ms时执行固定种子1024次置换，概率≤0.05标记Drift，否则Suggested；两者都提供低可信估计。非显著不证明没有趋势。只有跨度、区间精度和原始MAD都达标才Reliable。低可信可试听/保留，只有明确“自行采用估计并保存”才替换原值；补拍不能升级主轮低可信，保留主轮中心和区间。以下历史阈值与验收记录按其日期保留，不代表当前策略。真人误拒率和串行相关下区间覆盖尚未验证。

`RhythmFixedChartCombatPolicy` 固定 song/chart/revision/ruleset/scoring 五项身份及通过百分比。使用此具体策略的ExternalSession错曲拒绝开始，错谱/版本零回调且不消耗活动局，匹配Completed按目标分结算；取消/技术错误独立通知。自定义IRhythmCombatPolicy不自动获得这套身份过滤。没有伤害实现或正式Boot接线。

编辑器 Profile key `rhythm-calibration`，当前测试 Player key `rhythm-test-calibration`，版本 2，保存 OffsetMs 和 VisualOffsetMs；旧档视觉值为零。开始、明确应用建议及退出时写入。该分支目前用于独立测试 Player；正式集成时须明确正式档案策略，通用 settings 不由此 key 隔离。
校准参考音在本轮内生成并预约；估计不扣旧补偿。默认至少 24 个剔除后样本，每个时间块至少覆盖一半目标，块间中位数跨度不超过 20ms；过滤后 MAD 超过配置上限两倍或偏移超 ±300ms 拒绝。原始 MAD 超配置上限（默认 30ms）但时序一致时可给待确认建议。补测须与主轮独立一致，不能修复时间覆盖缺口或漂移，主轮估计不被补测平均改写。阈值是工程策略，尚非真人有效性保证；音频、输入和人的预判偏差无法分别辨识。
歌曲进度使用独立 `rhythm-progress` Profile（测试 Player 为 `rhythm-test-progress`），与校准档案分开。v2 保存完整 FreePlay Completed 单局，比较键包含 songId、chartId、revision、rulesetId、scoringVersion；练习、校准、Combat、中断和技术错误不获得个人纪录资格。最高分对应整份真实单局；同分保留先前局，不从其他轮拼接最大连击或判定统计。已获得曲目解锁单独保留，不因纯谱面修订撤销。异步写入按队列保存独立快照，退出等待写入完成；失败保留内存成绩并通知。没有线上分数榜。
模块 scope `rhythm`：entered、started、hit、finished、interrupted、calibration_saved。
错误：enter_failed、start_failed、calibration_save_failed；加载面板用 view_ready span。
参数守卫不额外埋点；Enter catch 覆盖加载与配置失败。
StartRound 与 OnLane 的成功日志在 StartRoundAsync 与 Rules.Hit，避免重复。
Dispose 清理不埋；下落/漏按的每帧路径不埋，结束聚合结果。

## 验证入口

EditMode：`Game.Tests.EditMode.Rhythm.RhythmRulesTests`，判定边界、重复输入、补偿正负、掉帧及时间戳一致性。
已有覆盖 Hold 自动尾/早放/漏头、输入积压/会话隔离、批次封口前送达的早放、版本记录排序/冲突和校准离群/样本质量。本轮 74/74 通过：等宽网格、Tap/Hold 头尾/判定线、线性滚动、视觉偏移与多区域尺寸，另有诊断编解码/回放和时钟守卫。30/60/144 FPS 数学回归不替代设备实测。
Showcase：`Game.Tests.Showcase.Rhythm.RhythmShowcase`，独立场景四轨真实输入、完整当前谱面的 Tap/Hold 交错头尾（本轮 58 枚、2 Hold）、重试、漏按结算、保存及标题重入；短谱运行时副本经真实 State/UI/音频/输入验证 Hold 正负补偿、早放/重开和校准接受/拒绝、独立视觉值，不改正式谱面。
回放依赖基类的临时 SaveRootOverride 隔离目录，开始前断言平台存档根目录确为该目录，收尾删除并恢复覆盖；补偿保存检查不会写玩家真实档案。
回放报告在 `Logs/verify/rhythm/`；它验证流程和界面，不替代人工试听。
2026-10-04 本轮 Showcase 3/3 通过，报告 `Logs/verify/rhythm/20261004-104628/report.md`：检查点失败 0、运行时异常 0。新增真实场景覆盖 350ms 主线程跨预约停音终点、二十次重试、诊断往返、音频暂停、应用暂停消息模拟和输入模式拒绝/中途变更；非 OS 挂起、真实设备切换或物理输出验收。测试节奏恢复至原倍率 1.0，结束时编辑器 idle、非 Play、Console error 0。
导出与停止细因收尾：最新 EditMode 88/88、定向 Showcase 2/2；`Logs/verify/rhythm/20261004-110234/report.md` 为 PASS，故意目录错误的一条诊断错误已按框架预期错误机制单独过滤，其余异常 0。两张保存成功/失败截图已核对文字出现，视觉排版仍须用户确认。
上传谱接入验证（2026-10-04）：EditMode 完成 88 个用例、失败 0；Showcase 4/4（无跳过），报告 `Logs/verify/rhythm/20261004-191705/report.md` 检查点失败 0、运行时异常 0。实际正式 SO 的 58 枚全部完成、2 Hold 成功、Miss=0，界面数量与操作说明按当前谱面显示。目录错误仍为明确预期错误；不是物理输出或人工音乐贴合度验收。
视觉与手感最终验收仍由开发者试玩决定。

## 选曲与歌曲进度（2026-10-04）

`RhythmInstaller.catalog` 接入 `RhythmSongCatalog.asset`，旧单曲 config 保留兼容；没有曲库时仍走原单曲练习流程。曲库包含虫儿飞、hybeboy 吉他伴奏和 Attention 伴奏，后两曲都以前者通关为前置。三曲初始通过比例均为 60%，可逐曲调整；虫儿飞目标 34800 分。

`RhythmSongData` 保存身份、标题、描述标签、当前谱面、revision、rulesetId、scoringVersion、前置与整数通过百分比；`RhythmCatalogConfig` 校验唯一 ID 和无环前置关系。一曲一谱，现有 difficulty 字段只是显示标签，没有多难度选择或额外谱。`RhythmProgressData/Rules` 管理存档快照、目标分及解锁；`RhythmSelectionRules` 管理异步选曲代次和一次性结算资格。State 复用原音频排程、输入批次与 StopRound/ReleaseActions，换曲先终止旧轮、加载新音频，成功后重建规则与输入动作；返回曲库取消准备，过期异步准备不得重新启动旧会话。

选曲页运行时复用 UIView 创建 UGUI 垂直 ScrollRect、曲目行与详情，替代固定三按钮；不是本轮制作的新正式 prefab。行显示通过门槛、当前可比较最佳分和通关/锁定状态；详情显示最佳完整局的判定、最高连击、成功 Hold、最近完成及独立 legacy 分数。返回曲库保留已选曲目详情及滚动位置。锁定按钮不可用，直接触发按钮事件也由 State 拒绝。返回选曲中断当前演奏，返回标题保存并释放资源；重试清零本轮分数，不清除历史最高分。曲名与 Tap/Hold 数量随当前配置显示。

## 单局结果、版本与旧档保护（当前增量，待统一验收）

歌曲 ID 表示内容，ChartId 表示当前谱面；schema v2 毫秒音符格式保持。修改谱面提升 revision，改变判定契约或评分算法分别提升 rulesetId/scoringVersion。当前最高分只读完全匹配的完整纪录，不能把未知旧评分版本的 legacy 高分混入比较。

`RhythmRunResult` 记录 runId/contextId、模式、Completed/Aborted/TechnicalError、EndReason、曲谱/规则/评分版本、音符总数、Perfect/Good/Miss、成功 Hold、分数、最高连击和输入补偿。Completed 必须全谱结算；不是仅凭播放停止推断完成。State 结束先封口运行、清音频/输入，再交付外部消费者与结束事件，避免重复结束及监听者异常阻碍资源回收。

`RhythmRecordData` 保存 BestScoreRun、LastCompletedRun、Cleared。`ProcessedRunIds` 拒绝重复落同局纪录；Combat 结果与自由演奏进度分流。当前没有独立最佳连击榜、完整历史局列表或跨版本分数归一化。

旧 v1 的 BestScores/ClearedCharts 原样保留为 legacy；无法反推 Perfect/Good/Hold 时不构造历史单局。迁移补齐 v2 容器并按历史通关推导永久 UnlockedSongs，重复迁移幂等。已获得解锁跨 revision 保留，当前谱面是否通关与曲目可进入分开显示。

真实 JsonSaveService 档案读取前，`RhythmProgressArchive` 校验结构与支持版本，并为 v1 原字节创建带 SHA256 的 `.migration-v1-<hash>.bak`；相同 bytes 不重复备份，不依赖会被后续写入替换的框架滚动 `.bak`。未知未来版本、非法 envelope 或无效完整局拒绝进入并保留原档。内存假存档没有文件备份；测试必须用 SaveRootOverride 隔离真实玩家档案。

## 外部演奏契约与迁移边界（当前增量，待统一验收）

调用方提供不可变请求、只读入口权限和场景 context。已驯服资格开放曲库；所有外部场景演奏（包含外部 FreePlay 与 Combat）开始/消费均要求当前控制指定乐师且上下文有效。未 ConfigureExternal 的独立 Demo 自由局不依赖 Boot 角色状态。音游不定义乐师职业，也不调用驯服或控制写 API。实际可读的既有接口是 TamingRules 的 IsTargetTamed(id)、CanControl(id)、CurrentControlId；本轮仅核查契约，未接入正式场景。

外部 session 拒绝错误 run/context/song/mode、过期权限及重复结果；同一 consumer 实例生命周期内成功开始的 runId 不能复用，重试需调用方配置新请求，不宣称跨实例全局恰好一次。身份匹配后先封口，再查询权限/调用策略/回调；异常不能重消费。仅 Completed Combat 交给可注入成功策略，并走成功/失败回调；Aborted/TechnicalError 独立通知，不冒充战斗失败。FreePlay 不触发战斗效果。

正式 Boot 路由、世界恢复与返回位置、乐师职业身份、伤害量和失败惩罚仍由后续玩法设计决定。当前沿用暂停令牌冻结世界 tick；正式战斗演奏期间是否继续世界时间尚待决定。本轮不做世界事务框架、不写怪物生命、不制作正式 UI prefab；隔离适配器和 20 曲运行时测试曲库只验证契约/滚动能力，不新增正式曲目。

曲库增量已完成冻结快照EditMode171/171及五项曲库Showcase；历史八项7通过/1失败的校准fixture问题已定向修复，见下方2026-10-05证据。不将历史节点或定向补验合并为最新全量结果。

两首新曲采用已核实上传 bytes 的 MP3：吉他伴奏取 24.495–101.295 秒（76.800 秒），164 枚（157 Tap、7 Hold）；Attention 取 25.490–98.494 秒（约 73.004 秒），229 枚（214 Tap、15 Hold）。2026-10-06 只读核对两份文本与首次提交 `503d601` 及当前 HEAD 完全一致，本轮未改谱或音频。统计必须只读取 `notes`，不能混入另列的四枚 `practiceNotes`；本轮曾误报 168/233，已纠正。自动起音包络相关与相位搜索候选分别为 100、105.2 BPM，chartOffsetMs=0。这是固定网格测试谱，尚未人工确认完整乐句、Hold 音乐语义及浏览器/Unity 解码对齐。分析与可重跑校验见 [记录](../../../../PRP/rhythm-song-progression-20261004/README.md)。

实际验收：Unity 编译成功，EditMode 120/120；原四项 Showcase 通过。第一轮新增长流程因默认 180 秒超时中止，入门低分/达标解锁、吉他 164 Perfect/7 Hold/Miss=0 已完成；之后只补跑 Attention 与存档重读 1/1（81.66 秒），229 Perfect/15 Hold/Miss=0，重试/换曲清零、真实档案写盘及重入三曲进度恢复通过，文字不溢出断言和截图核对通过。报告 `Logs/verify/rhythm/20261004-205648/report.md`；前置 fixture 明确预置入门和吉他成绩，不把补验说成再演奏前两曲。长流程现有明确 360 秒超时，本轮未重跑。慢写/失败注入和 OS 重启恢复未验收。收尾恢复干净 LailaRecognitionPlaytest、非 Play、无运行测试、倍率 1。

## 确认式校准验收

最新分级可信度与固定谱Combat增量：编译/error0、手动lint通过、定向Edit69/69；四项受影响Showcase分别通过，最后Combat1/1见`Logs/verify/rhythm/20261005-130646/report.md`（异常0）。校准确认/取消通过位于125820批次，时间戳对照通过位于130327；两批整体FAIL继续保留，不合并冒充一次全绿。Low估计试听/保留/明确采用与重入、真实两键Combat达标/低分/取消及纪录分流已验。Unity归还RhythmDemo idle、非Play/编译测试、倍率1、error0。[实施与边界](../../../../PRP/rhythm-final-regression-fix-20261005/confidence-combat-implementation-report.md)。

2026-10-05 诊断与不可恢复补测修复实际验收：EditMode44/44，页内提示微调后State/Diagnostic33/33；Showcase2/2见`Logs/verify/rhythm/20261005-110306/report.md`，最终保存失败/取消UI补验1/1见`Logs/verify/rhythm/20261005-110957/report.md`，检查点失败与运行时异常均0。真实32拍fixture、一次8拍补测、Drift禁补测、JSON逐拍回读及失败/取消保留旧值均已验证。阈值未修改，历史真人失败无原始数据，不能推断原因或宣称真人通过。详情见`PRP/rhythm-real-calibration-diagnostic-20261005/implementation-report.md`。

确认式校准验证（2026-10-04）：最终运行时代码EditMode131/131，手动C# lint通过。旧Hold/校准生命周期和跨轨组合在`Logs/verify/rhythm/20261004-224845/report.md`通过；该批新校准因整数滑块显示断言失败，保留整体FAIL。第一次定向复验的协程发键抖动使预设稳定样本误为Drift；未放宽产品门，改用明确事件时间戳经真实InputSystem/Action接线。最终定向1/1（95.47秒），报告`Logs/verify/rhythm/20261004-230245/report.md`为PASS、检查点失败0、运行时异常0，覆盖默认8+32、一次补测、建议/取消/确认保存、试听失焦提示、漂移和重入恢复。此测试不验收真实设备桥接或真人有效性。三首正式谱与项目设置hash保持；类型/接口及历史失败见[实现记录](../../../../PRP/rhythm-calibration-review-20261004/implementation-report.md)。

## 谱面边界

2026-10-05 两项回归收尾：Runtime先校验时钟再记录诊断，非法样本保留明确Reason并停局清理，不捕获吞掉所有异常。校准回放press/release同用单调预定时刻，实际接收记录保留每拍timestamp/beat及invalid等计数；真实InputSystem受控卡顿对照为旧26/32 Drift、修复32/32 Suggested（offset80、MAD45、分块差0），质量门未变。定向EditMode35/35；首轮六项4通过/2失败保留，两个校准用例修正隔离后补验2/2，报告 `Logs/verify/rhythm/20261005-031538/report.md` PASS（检查点失败0、异常0）。当前六项各有通过证据，未重跑全部171项或全仓。实际已交还干净LailaRecognitionPlaytest、非Play/编译/测试、timeScale/倍率1、Console error0；边界和首轮时钟中断见[交付记录](../../../../PRP/rhythm-final-regression-fix-20261005/implementation-report.md)。

2026-10-05 历史增量快照EditMode171/171，C# lint通过；定向八项Showcase **7通过/1失败**。五项新曲库回放、旧Hold生命周期和跨轨组合通过；确认式校准Suggested预设收到Drift，当时原因待定位，整体FAIL报告 `Logs/verify/rhythm/20261005-014637/report.md` 保留。该待定位项由上方本轮修复关闭，不把历史PASS或补验改写为原批次全绿。详情见[实施记录](../../../../PRP/rhythm-library-integration-20261005/implementation-report.md)。

毫秒记录是版本 2 的权威数据，运行秒数仅为编译副本。时刻允许零，倒数使用负歌曲时间；不要求首音符晚于倒数。提前量至少覆盖 approach 和视觉偏移。
ID 唯一，时间有限非负，Tap 时长零、Hold 时长正；同轨头窗不重叠、Hold 占用不能和下一枚头窗交叉。对齐后终点不能超出片段。
片段结束停止声音，但保留 DSP 句柄至最后终点/头窗（含正输入补偿）关闭并加缓冲后结算，避免截掉尾部。
# 2026-10-06 UI 定向验收补充

最终 Attention 演奏、重试、切歌和 v2 落盘恢复单项通过（job 78702fa84f254645afef170906f92cfd，Logs/verify/rhythm/20261006-113506/report.md）：229 命中、15 Hold、零 Miss。页面、中断、加载选择与四轨的定向检查通过；未重复已通过的暂停/Profile 全组。Showcase 输入调度按 PositionAtInputTime(realtime) 等待，与生产输入时钟一致；不改变谱面、判定窗口或原断言。历史四个 Hold Miss 无逐音符记录，旧 DSP 对照也通过，不能追溯断言具体原因。完整过程、失败证据及截图见 PRP/rhythm-ui-navigation-20261006/implementation-report.md。
