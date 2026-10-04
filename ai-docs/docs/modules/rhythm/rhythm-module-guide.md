---
type: module-guide
module: rhythm
layer: runtime
maturity: stable
---
# Rhythm 模块指南

## 范围与状态

PC 四轨下落音游独立试玩。用户要求新场景，入口为 `Assets/_Project/Scenes/RhythmDemo.unity`，不借用 SampleScene。
使用用户提供的《虫儿飞（伴奏）》MP3，已复制到 `Assets/_Project/Audio/Rhythm/ChongErFei.mp3`。
截取原音频 16～56 秒，至少 3 秒预滚，旧 56 个单键音符保留；规则支持 Tap 与按住到尾的 Hold。
谱面来自音频能量起音候选，轨道按固定模式分配；尚未经过人工音乐贴合度验收。
PC 综合输入偏差支持听声跟拍估算与手动微调，视觉延迟独立手调。没有独立视觉采样向导、多设备身份档案、外部格式导入或复杂谱面编辑器。
2026-10-04 用户取消伪透视：四键 UGUI 轨道改为等宽、直线匀速下落，保留现有配色和可用古风/琴弦元素；不绑定七弦或某种乐器音色。新表现代码已调整，Unity 编译/测试与真实画面验收待完成，旧透视截图仅作历史。

## 试玩

1. 打开 RhythmDemo 场景并 Play，启动后自动显示音游。
2. 可点击跟拍自动校准：先听 8 拍适应，再采集 32 拍；至少 24 次稳定样本才替换补偿。手调范围 -300～+300 ms。
3. 点击开始演奏，倒数后按 D、F、J、K 对应四条轨道。
4. 音符到达横线时按键，右侧显示等级、早晚、补偿后误差与原始误差。
5. 随时重新开始；片段结束显示 Perfect、Good、Miss 与最高连击。
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
| RhythmCalibrationEstimator | 原始误差中位数、MAD、离群剔除及质量门 |
| RhythmTrackGraphic | 等宽四轨、恒定尺寸头部与长条的 UGUI 网格 |
| RhythmHitIntent / RhythmHitResult | 带歌曲时间戳的输入及判定结果 |
| RhythmView | 下落表现、交互、反馈及结算 |
| RhythmCalibrationData | Profile 档案中的输入补偿和视觉延迟，版本 2 |

规则层不引用 UnityEngine。状态通过框架服务开关 UI、播放音频、保存数据、暂停世界。
Input System performed/canceled 复制带时间戳的意图；onAfterUpdate 记录 Dynamic/Fixed 输入批次的同域时刻，Tick 先按事件时刻推进和判定，再封口上一批已完成输入的边界。DSP 只驱动画面与停止声音，不作为输入水位；正常批次送达不会被 DSP 抢先超时。早于已封口水位的迟到输入仍终止本轮并诊断，不回滚；反馈最多等待下一输入批次，不修改原始误差。
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

Enter 校验配置、读补偿、加载音频、克隆输入资产、暂停世界并打开面板。
保存原 Gameplay 动作图启用状态，演奏期间禁用它，退出恢复。
开始时先停止上一轮，再保存补偿，重新建规则并调度音频。
保存等待期间锁定两个滑块；会话编号防止失焦/退出后的异步保存重新启动。停止先关闭入口和清队列，再 Disable，取消回调不算 Release。
开始失败回到准备状态，并记录错误。
退出先停止演奏、退订 UI，再关闭面板；finally 释放输入及暂停令牌。
校准保存失败记录错误并显示通知，不阻断返回标题。
Dispose 也释放音频、输入和暂停资源。

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

编辑器 Profile key `rhythm-calibration`，当前测试 Player key `rhythm-test-calibration`，版本 2，保存 OffsetMs 和 VisualOffsetMs；旧档视觉值为零。开始、有效自动校准及退出时写入。该分支目前用于独立测试 Player；正式集成时须明确正式档案策略，通用 settings 不由此 key 隔离。
校准参考音在本轮内生成并预约；估计不扣旧补偿。MAD >30ms、有效数不足或偏差超范围时保留原值；音频、输入和人的预判偏差无法分别辨识。
无分数榜、成绩档案或歌曲解锁存档。
模块 scope `rhythm`：entered、started、hit、finished、interrupted、calibration_saved。
错误：enter_failed、start_failed、calibration_save_failed；加载面板用 view_ready span。
参数守卫不额外埋点；Enter catch 覆盖加载与配置失败。
StartRound 与 OnLane 的成功日志在 StartRoundAsync 与 Rules.Hit，避免重复。
Dispose 清理不埋；下落/漏按的每帧路径不埋，结束聚合结果。

## 验证入口

EditMode：`Game.Tests.EditMode.Rhythm.RhythmRulesTests`，判定边界、重复输入、补偿正负、掉帧及时间戳一致性。
已有覆盖 Hold 自动尾/早放/漏头、输入积压/会话隔离、批次封口前送达的早放、版本记录排序/冲突和校准离群/样本质量。新表现回归改为等宽网格、Tap/Hold 头尾/判定线、线性滚动、视觉偏移与多区域尺寸，测试源码已调整、本轮尚未运行；30/60/144 FPS 数学回归不替代设备实测。
Showcase：`Game.Tests.Showcase.Rhythm.RhythmShowcase`，独立场景四轨真实输入、完整 56Tap、重试、漏按结算、保存及标题重入；短谱运行时副本经真实 State/UI/音频/输入验证 Hold 正负补偿、早放/重开和校准接受/拒绝、独立视觉值，不改正式谱面。
回放依赖基类的临时 SaveRootOverride 隔离目录，开始前断言平台存档根目录确为该目录，收尾删除并恢复覆盖；补偿保存检查不会写玩家真实档案。
回放报告在 `Logs/verify/rhythm/`；它验证流程和界面，不替代人工试听。
视觉与手感最终验收仍由开发者试玩决定。

## 制作约束

毫秒记录是版本 2 的权威数据，运行秒数仅为编译副本。时刻允许零，倒数使用负歌曲时间；不要求首音符晚于倒数。提前量至少覆盖 approach 和视觉偏移。
ID 唯一，时间有限非负，Tap 时长零、Hold 时长正；同轨头窗不重叠、Hold 占用不能和下一枚头窗交叉。对齐后终点不能超出片段。
片段结束停止声音，但保留 DSP 句柄至最后终点/头窗（含正输入补偿）关闭并加缓冲后结算，避免截掉尾部。
