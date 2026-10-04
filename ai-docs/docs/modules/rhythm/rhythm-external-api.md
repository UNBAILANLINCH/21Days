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
| `NoteTime(int) / NoteLane(int) / IsResolved(int)` | 只读查询谱面和解决状态 |
| `Count / OffsetMs / Perfect / Good / Miss / Score / Combo / MaxCombo` | 只读统计 |
| `RhythmState.StartRound()` | UI 会话已打开时开始或重试，正在准备时忽略重复点击 |
| `RhythmState.Rules / IsPlaying / SongSeconds / LaneAction(int)` | 会话与回放查询；LaneAction 只在 Enter 完成后可用 |

`RhythmHitIntent(lane, songSeconds, edge=Press, session=0)` 为不可变意图；Release 无活动 Hold 时不计分。
结果公开 Note、Grade、RawErrorMs、ErrorMs；Grade 为 None、Perfect、Good、Miss。
None 表示未消费音符，不计分。
HoldStarted 表示头部已激活，尚未增加统计；整枚 Hold 只在自动到尾或失败时结算一次。
`RhythmCalibrationEstimator.TryEstimate(out offsetMs, out madMs, out accepted)` 返回软件综合偏差候选；false 不应覆盖保存值。

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
