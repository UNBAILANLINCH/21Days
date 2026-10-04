# P0-A 纯时间诊断与回放接入

独立 A 任务拥有新增文件：`RhythmDiagnosticData.cs`、`RhythmDiagnosticSession.cs`、`RhythmDiagnosticReplay.cs`、`RhythmDiagnosticCodec.cs`（均位于 `Assets/_Project/Scripts/Runtime/Rhythm/`），以及 `Assets/_Project/Scripts/Tests/EditMode/Rhythm/RhythmDiagnosticTests.cs` 和本文。State/View/Audio/配置/HANDOVER 未修改。

## 最小接入

```csharp
var header = new RhythmDiagnosticData.Header(
    session, chartId, audioId, contentVersion, environment, inputUpdateMode,
    clipStartSeconds, scheduledDsp, scheduledRealtime, bridgeWidthSeconds,
    chartOffsetMs, inputOffsetMs, visualOffsetMs, perfectMs, goodMs);
var diagnostics = new RhythmDiagnosticSession(header, notes, telemetry: telemetry);
rules = diagnostics.Rules;

// 只在游戏 Lane0..Lane3 的 performed/canceled 回调调用；不要监听全局键盘或文本。
var intent = new RhythmHitIntent(lane, mappedSongSeconds, edge, session);
diagnostics.Enqueue(in intent, context.time, capturedRealtime);

// 与原 Queue.Drain 使用同一个释放水位；当前 DSP 仅作证据，不推进判定。
diagnostics.Drain(previousBatchSongBoundary, currentBatchSongBoundary,
    releasedSongWatermark, sampledRealtime, sampledDsp, OnHitResult);

// 可选：稀疏采样同一 realtime/DSP 对，采样宽度由调用者提供。
diagnostics.SampleBridge(sampledRealtime, sampledDsp, sampleWidthSeconds);
diagnostics.End(RhythmDiagnosticData.Reason.FocusLost);
var data = diagnostics.Snapshot();
var verification = RhythmDiagnosticReplay.Run(data);
RhythmDiagnosticCodec.Write(callerOwnedStream, data);
var restored = RhythmDiagnosticCodec.Read(callerOwnedSeekableStream);
```

不要同时再调用原队列或对 `Rules` 直接 `Hit/Advance`，否则会重复判定或漏录。原队列应被本 facade 替换；原 UI 回调传给 Drain。`Rules` 只供读取，仍是原 `RhythmRules` 实例。Header 的 notes 必须与实际规则一致：练习谱面使用 chart offset 0；正式谱面应用原配置 chart offset。`ClipStartSeconds` 是音频原片段起点，音符与 Intent 都是片段相对秒。桥接原点必须是同一预计播放起点在 DSP/realtime 两个时钟上的坐标；原点尚未取得时不要用猜测值创建记录。

`End` 支持 Finished/Restart/FocusLost/AudioChanged/DeviceChanged/ClockRollback/Stopped，以及 AudioPaused/ApplicationPaused/InputModeChanged/ClockDiscontinuity/InvalidClock/LateInput；幂等并清队列。新一轮创建新实例和新 session，不重用旧实例。边界回退应在集成方先 End(ClockRollback)，Drain 遇到非法时间会抛异常且不修改记录。显式终止不会自动把未结算音符记 Miss。

集成补充：沿用现有 Command.EndReason 字段，新增枚举追加在旧 RecordingLimit 后，原数值和二进制布局保持；新读取器兼容旧文件，旧读取器遇到不认识的追加原因明确拒绝。没有用环境字符串或数值时间字段偷塞原因。

## 数据与证据边界

格式 `RHD1`：little endian，formatVersion=1，rulesVersion=1，严格读取，不接受未知版本、非法枚举、非有限数、超长字符串/数组、截断或尾随字节；导出与导入均验证重放判定一致性。记录 Header 的谱面/音频/内容版本标识、环境与真实 updateMode、三类 offset、窗口、片段起点、预定双时钟原点、采样宽度，以及完整谱面快照。Commands 保存稳定接收序号、原始事件时间、捕获时间、lane/edge/session、实际映射 songSeconds、批次前后边界/释放水位、稀疏桥接样本、终止原因。Judgements 保存输入关联序号、目标 ID/头尾时间、等级/原因、原始/补偿误差、得分变化与 combo。

排序遵循现有 Queue：按映射 songSeconds 排序，同时间保持接收顺序；迟到不钳制。重复 Press/Release、旧 session、晚于释放水位返回的旧事件分别标记 DuplicateEdge/StaleSession/LateInput。自动 Miss/尾结算没有原始输入，两个误差为 null（不是 0）；自动输出关联序号为 0。Hold 头仅记录等级，尾成功或提前松开只结算一枚。

容量默认 65536 个命令、最多 4096 个音符；每个标识最多 1024 字符，导出上限 32MiB。到达命令上限封存前缀并增加唯一 End(RecordingLimit)，后续游戏判定继续运行，`IsRecording=false`；封存记录只证明前缀，Report.IsComplete=false，不代表全场成绩。Snapshot 显式复制，实时路径不做字符串日志或磁盘导出。调用者决定何时采样、是否启用记录以及导出路径；没有任何隐式文件/Profile 写入。

Replay 按记录命令与批次水位调用真实 Queue/Rules，不使用当前 DSP；Matches 比对完整判定输出，重复执行可复现。Report 的 PendingInputs 是终止时尚未处理的输入数量；它們被终止清理，保留该数量用于审计。逻辑回放不产生或证明物理声学延迟：Report.ProvesPhysicalLatency 永远 false。桥接残差 `(dsp - scheduledDsp) - (realtime - scheduledRealtime)` 单位毫秒，SummarizeBridge 输出 median/P95 absolute/max absolute 与采样宽度；没有样本返回 null。采样间隔、DSP buffer、设备、帧率和硬件闭环证据由集成方填入环境标识并实际采集，不能把输入捕获延迟或残差当作声学测量。

## 验证边界

测试位于 `Game.Tests.EditMode.Rhythm.RhythmDiagnosticTests`。本任务不启动/操作 Unity Editor，不生成 `.meta`，由集成方授权刷新后补齐。纯 C# 本地编译/测试结果在任务最终报告中给出；这不替代 Unity Test Runner 或 Player/物理闭环验证。

2026-10-04 实测：.NET 8 Roslyn 以 C# 9 编译本任务源码、真实 RhythmRules/InputQueue/Note/Intent/Result，引用现有 Game.Core、UnityEngine.CoreModule 和 Unity NUnit 托管程序集；临时反射入口逐个执行 NUnit 用例，18/18 PASS，退出码 0。覆盖正常 Tap/Hold/自动 Miss、无误差自动尾、批次积压/原始时间、迟到/旧 session、同时间稳定顺序/重复边沿、-100/0/+100ms offset、终止/重开、容量前缀、导出读回/重复一致性、版本/截断/尾随字节拒绝、非法时间水位、篡改判定与缺失命令拒绝，以及 30/60/144 FPS + 约 200ms 卡顿对照原队列。五个新增 C# 文件 project-lint 最终通过；本任务范围 diff whitespace 检查通过。

全仓 gc_scan 实际退出码 1：已有 Gameplay/Game.LailaFace 命名空间不一致与 Dynamic 字体污染，以及新增 RhythmDiagnosticTests.cs 缺 `.meta`。四个 Runtime `.meta` 已由并行编辑器流程生成，本任务没有写它们；测试 `.meta` 仍由集成方刷新补齐。未执行 Unity Test Runner、场景接入、Player 或物理声学验证。临时编译器产物和验证入口在 `$env:TEMP/21days-rhythm-diagnostics/`，没有写入正式 Profile 或项目生成物目录。

后续集成验收（同日）：Unity 已生成测试 `.meta` 并完成真实编译；最新 Rhythm EditMode 88/88，定向 Showcase 2/2。`Logs/verify/rhythm/20261004-110234/report.md` 记录实际 Button.onClick 导出四份独立文件、磁盘 Codec/Replay 回读、旧文件/哨兵不覆盖，以及目录错误通知、内存保留和恢复重试。新增 EndReason 已由 codec/replay、运行时映射和真实暂停/模式场景验证。测试目录由框架隔离/清理，不保留为正式数据；仍没有 Player 或物理声学验收。
