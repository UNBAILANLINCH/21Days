---
type: external-api
module: rhythm
layer: runtime
maturity: stable
---
# Rhythm 公开接口

调用前读 [模块指南](rhythm-module-guide.md)。所有玩法类型位于 `Game.Rhythm`。

| 接口 | 用途与约束 |
| --- | --- |
| `RhythmConfig.CreateRules(double offsetMs, ITelemetryScope telemetry)` | 建立新一轮规则；配置只读 |
| `new RhythmRules(double[] times, int[] lanes, double perfectMs, double goodMs, double offsetMs, ITelemetryScope telemetry)` | 校验并复制谱面；轨道 0～3，时刻有序，同轨窗口不能重叠；telemetry 可传 null |
| `RhythmRules.Hit(in RhythmHitIntent intent)` | 输入携带歌曲时刻；返回 RhythmHitResult，只解决最近一枚未决音符 |
| `RhythmRules.Advance(double songSeconds)` | 时间单调推进，返回本次 Miss 数量 |
| `new RhythmRules(RhythmNoteData[], perfectMs, goodMs, inputOffsetMs, chartOffsetMs, telemetry)` | 校验并稳定排序副本；毫秒转秒；Tap/Hold 同轨不冲突 |
| `RhythmInputQueue.Begin(session) / Enqueue(intent) / Drain(rules, now, callback) / Clear()` | 按时间戳先处理积压输入；now 是已完成输入批次的封口边界，不能直接传 DSP 当前时刻；迟到计数 LateInputs，调用方终止异常会话 |
| `NoteEnd(int) / NoteType(int) / NoteId(int) / IsHolding(int) / LastEndSeconds` | 长按、谱面和结算期限的只读查询 |
| `RhythmState.StartCalibration()` | 面板打开且不在异步准备时启动听声跟拍；新轮停止上一会话 |
| `RhythmState.CalibrationCandidate` | 当前未确认的只读结果；无结果时为 null，不等于已保存补偿 |
| `RhythmState.LastCalibrationDiagnosticPath` | 最近一次校准本地 JSON 保存路径；保存失败为 null，不代表已应用补偿；文件可被五轮保留策略回收 |
| `NoteTime(int) / NoteLane(int) / IsResolved(int)` | 只读查询谱面和解决状态 |
| `Count / OffsetMs / Perfect / Good / Miss / Score / Combo / MaxCombo` | 只读统计 |
| `RhythmState.StartRound()` | UI 会话已打开时开始或重试，正在准备时忽略重复点击 |
| `RhythmState.Rules / IsPlaying / SongSeconds / LaneAction(int)` | 会话与回放查询；LaneAction 只在 Enter 完成后可用 |
| `RhythmState.ConfigureCatalog(RhythmCatalogConfig)` | 仅首次 Enter 前配置曲库；Installer 在容器建立后调用，校验身份与前置关系 |
| `RhythmState.SelectSong(string) / ShowSongMenu()` | Enter 完成且面板打开后使用；锁定、未知或准备中的请求拒绝，返回选曲终止当前轮 |
| `SelectedSongId / IsSongMenu` | 当前曲目及选曲页状态 |
| `BestScore(string) / IsUnlocked(string) / IsCleared(string)` | Enter 后查询当前可比较完整纪录/永久曲目解锁/当前谱面通关；未知曲目返回零/false，legacy 不混入 BestScore |
| `RhythmSongData / RhythmCatalogConfig` | 只读曲库 SO 配置；通过比例为整数 1～100，前置必须存在且无环 |

有曲库时 `StartRound/StartCalibration` 和练习入口在选曲页拒绝启动；选择成功并加载音频后才进入准备页。成绩由 State 使用 ISaveService 保存，调用方不在 SO 中写玩家成绩。`RequiredScore` 向上取整；`RecordKey` 包含 songId/chartId/revision/rulesetId/scoringVersion。已获得曲目解锁跨修订保留；旧 BestScores 独立 legacy，不推断完整判定摘要。当前一曲一谱，difficulty 只是描述标签。

## 外部进入与结束

当前新增 `RhythmFixedChartCombatPolicy(songId, chartId, revision, rulesetId, scoringVersion, passScorePercent=60)`：固定全身份，比例1～100，匹配Completed Combat按目标分判成功。ExternalSession使用该具体策略时增加错曲开始/错谱版本消费守卫；其他自定义policy不自动获得此过滤。

当前校准结果增加 `Confidence`（Invalid/Low/Reliable）、`MedianLowerMs/MedianUpperMs`、`PrecisionRadiusMs`和`TemporalProbability`。Stable为Reliable，Suggested/Drift为Low，三者有限Offset均有HasCandidate；其他原因无候选。区间取全部matched，少于6拍无有限95%区间，依赖独立同分布假设；PrecisionRadius是候选到两端的最大距离。置换概率非显著不等于稳定。Supplement保留主轮中心/区间且不能将主轮Low升级Reliable。下面旧接口说明中“仅Stable/Suggested候选”是此前策略，以上更新优先。

| 接口 | 契约 |
| --- | --- |
| `RhythmState.ConfigureExternal(request, consumer)` | Enter 前配置可自动准备指定已解锁曲目；已 Enter 时先自行选择对应曲目。仅空闲时允许，拒绝外部 Practice；检查已驯服/曲库权限，真正开始任何外部模式都再查当前控制资格 |
| `RhythmState.ClearExternal()` | 空闲且不在异步准备时清除请求，之后回到普通自由局入口 |
| `LastRunResult / event OnRunFinished` | 只读上一单局与结束通知；Completed/Aborted/TechnicalError 均可能交付，校准没有演奏结果。订阅者负责退订，不能把收到事件等同战斗成功 |
| `RhythmPlayRequest(runId, contextId, songId, mode)` | 不可变身份；同一 consumer 实例生命周期内成功开始后 runId 不可复用，重试需新请求；不保证跨实例全局去重 |
| `IRhythmEntryPermission.Capture(contextId)` | 返回 `RhythmEntryAccess(IsContextValid, CanAccessLibrary, CanPerform)`；caller 查询真实角色/场景状态，不缓存失效上下文 |
| `RhythmExternalSession(permission, combatPolicy=null, onSuccess=null, onFailure=null, onAborted=null, onTechnicalError=null)` | 仅 Completed Combat 调成功策略与成功/失败回调；自由演奏/练习不会触发这些效果，中断独立通知 |
| `CanAccessLibrary(contextId) / TryBegin(request, out reason) / Active` | 曲库须上下文有效且已驯服；所有外部场景演奏额外要求 CanPerform，Combat 缺成功策略拒绝。活跃局不能替换，拒绝请求不占 runId |
| `Consume(result) / Invalidate()` | 身份/mode/song 错误或重复返回 false；匹配后先封口再读权限/回调，权限过期返回 false，适配器/策略/回调异常传播但不能重消费；Invalidate 拒绝迟到结果 |
| `IRhythmCombatPolicy.IsSuccess(result)` | 调用方定义成功条件；没有默认伤害量、失败惩罚或怪物写入口 |

`RhythmRunResult` 构造顺序：runId、contextId、mode、completion、endReason、songId、chartId、revision、rulesetId、scoringVersion、noteCount、perfect、good、miss、completedHolds、score、maxCombo、offsetMs=0。属性只读；Completed 要求全部音符结算，分数与判定摘要一致，Combat context 非空。`RhythmRecordData` 的 BestScoreRun/LastCompletedRun 保存完整同局；只接受 FreePlay Completed，不能拼接其他轮的最佳统计。

推荐 caller 配置请求/消费者、订阅结果、经 IGameFlow 进入；State 在音频/输入清理后交付并捕获监听异常。回调内不要立即重入当前 State 开始/退出，先排队到后续帧；完成后可 ClearExternal 或配置新 runId。普通自由局不能通过结束事件调用战斗回调。

现有驯服只读入口为 `TamingRules.IsTargetTamed(id) / CanControl(id) / CurrentControlId`，以稳定角色 ID 组合乐师资格；源码没有乐师职业类型，Boot/驯服控制接线尚未实施。未配置外部请求的 Demo 自由局不绑定角色控制；当前暂停令牌冻结世界 tick，未来战斗是否实时待定。曲库冻结快照EditMode171/171、五项新增曲库回放通过；八项整体7通过/1失败的历史报告保留，fixture分类缺陷已由[定向修复](../../../../PRP/rhythm-final-regression-fix-20261005/implementation-report.md)关闭。后续诊断与UI补验见[最新实施记录](../../../../PRP/rhythm-real-calibration-diagnostic-20261005/implementation-report.md)，不宣称最新全量重跑或真人校准通过。

`RhythmHitIntent(lane, songSeconds, edge=Press, session=0)` 为不可变意图；Release 无活动 Hold 时不计分。
结果公开 Note、Grade、RawErrorMs、ErrorMs；Grade 为 None、Perfect、Good、Miss。
None 表示未消费音符，不计分。
HoldStarted 表示头部已激活，尚未增加统计；整枚 Hold 只在自动到尾或失败时结算一次。
`RhythmCalibrationEstimator.TryEstimate(out offsetMs, out madMs, out accepted)` 保留历史质量门契约；当前玩家流程调用 `Analyze()`，返回不可变 `RhythmCalibrationResult`。`TryAdd(double, out rawErrorMs)` 记录输入/匹配/重复/窗外/非法计数，分析还提供 RawMadMs、FilteredMadMs、BlockSpreadMs、TemporalStable、Accepted 和分类 Reason；只有 Stable/Suggested 的 HasCandidate 为 true。`Supplement(primary, extra, minimum, maxMadMs)` 复核独立补测，阈值来自当前配置；调用方仍须显式确认才写入档案。

View 的 `OnCalibrationApply / OnCalibrationKeep / OnCalibrationSupplement / OnCalibrationPreview(bool candidate)` 仅发 UI 意图，State 管会话与保存。`ShowCalibrationResult(result, original, canSupplement, notice=null)` 可显示中断原因；`CalibrationPreviewFrame/Hit` 显示参考拍校正反馈，不能证明物理输出或直接保存候选。

`RhythmCalibrationResult.CanSupplement` 只描述时序覆盖有效且非Drift/Unstable；State额外检查一次上限与主轮有效数至少为minimum-8。View和State均拒绝无效补测。`TryAdd(double, out rawErrorMs, out beatIndex, out disposition)` 提供matched/duplicate/outside/invalid诊断细因；原两参数重载保持失败时rawErrorMs=0的契约。`RhythmCalibrationDiagnostic.Record/ToJson/TrySave` 仅供本模块接线/测试，不是玩家补偿档案；限额、保留和真假数据边界见模块指南。

框架音频扩展：`IAudioService.PlayScheduledClip(AudioClip clip, float startSeconds, float delaySeconds)` 返回 `AudioPlayback`。
句柄 Position 是 DSP 歌曲时刻；`PositionAtInputTime(double)` 映射输入事件时间；Dispose 停止并释放。
`AudioPlayback.StopAudio()` 仅停止声音，保留句柄时钟供尾窗结算，之后仍须 Dispose。
`AudioPlayback.ScheduleEnd(double songSeconds)` 在预约起点加歌曲时刻停止声音；有限且大于零，已释放句柄拒绝调用。`DspStart / InputStart / BridgeSampleSpanSeconds` 提供诊断快照，不代表物理输出时间。
`RhythmState.LastDiagnostic` 为上一结束会话的只读快照；诊断记录与 `RhythmDiagnosticSession / Replay / Codec` 契约见 [诊断接口](../../../../PRP/rhythm-followup/diagnostics-api.md)。`RhythmRules.PerfectMs / GoodMs / ChartOffsetMs / CopyNotes()` 提供原始谱面及窗口快照，不改变判定。
同一服务同时只允许一个调度片段，新播放会释放旧片段。
播放期间临时暂停原 BGM，释放后仅当 BGM 请求未变且原先在播放时恢复。
音频须先加载成功，delaySeconds 至少 0.1，起点在片段内，调用方必须释放句柄。

跨模块入口通过 `IGameFlow.GoToAsync<RhythmState>()`；须先注册配置与状态。
不要直接创建面板、改 SO、销毁服务持有的 UI 或绕过框架调用 AudioSource。
不要把固定 tick 时间当歌曲时间，也不要把补偿后的误差写回输入原始时间戳。
