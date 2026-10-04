// 职责：音游会话接线与资源清理。SampleState 只算购买价格，扩展它会混入另一种玩法。
using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Audio;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Core.Timing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VContainer.Unity;

namespace Game.Rhythm
{
    public sealed class RhythmState : GameState, ITickable, IDisposable
    {
        private readonly RhythmConfig config;
        private readonly IUIService ui;
        private readonly INotificationService notifications;
        private readonly IAudioService audio;
        private readonly ISaveService saves;
        private readonly IGameFlow flow;
        private readonly IInputService input;
        private readonly IWorldPauseService pause;
        private readonly ITelemetryScope telemetry;
        private RhythmView view;
        private AudioPlayback playback;
        private InputActionAsset actions;
        private RhythmCalibrationData calibration;
        private IDisposable pauseToken;
        private bool gameplayEnabled;
        private bool closing;
        private bool starting;
        private int session;
        private RhythmDiagnosticSession diagnostic;
        private double sampledRealtime;
        private double sampledDsp;
        private double nextBridgeSample;
        private Action<RhythmHitResult> judged;
        private RhythmClockGuard clockGuard;
        private RhythmCalibrationEstimator estimator;
        private AudioClip calibrationClip;
        private double calibrationDuration;
        private double calibrationBeatSeconds;
        private double inputBoundary;
        private double completedInputBoundary;
        private double roundDuration;
        private bool practiceRound;
        private bool missFeedbackShown;
        // 只有独立测试预设使用测试 key；正式 Player 和编辑器保留原档案契约。
        private string ProfileName => PlatformServiceBase.IsIsolatedTestBuild ? "rhythm-test-calibration" : "rhythm-calibration";
        public RhythmRules Rules { get; private set; }
        public RhythmDiagnosticData LastDiagnostic { get; private set; }
        public bool IsPlaying => playback != null;
        public double SongSeconds => playback == null ? 0d : playback.Position;
        public InputAction LaneAction(int lane) => actions.FindAction("Rhythm/Lane" + lane, true);

        public RhythmState(RhythmConfig config, IUIService ui, IAudioService audio, ISaveService saves,
            IGameFlow flow, IInputService input, IWorldPauseService pause, ITelemetryService telemetry, INotificationService notifications)
        {
            this.config = config; this.ui = ui; this.audio = audio; this.saves = saves;
            this.flow = flow; this.input = input; this.pause = pause;
            this.telemetry = telemetry.Scope("rhythm");
            this.notifications = notifications;
        }
        public override async UniTask EnterAsync(CancellationToken ct)
        {
            closing = false;
            try
            {
                if (config.Song == null || config.Input == null || config.ApproachSeconds <= 0 || config.CountdownSeconds < 0.1f ||
                    config.ClipStartSeconds < 0 || config.DurationSeconds <= 0 || config.ClipStartSeconds + config.DurationSeconds > config.Song.length)
                    throw new InvalidOperationException("音游配置或音频片段无效");
                calibration = await saves.ReadProfileAsync<RhythmCalibrationData>(ProfileName, ct);
                if (double.IsNaN(calibration.OffsetMs) || double.IsInfinity(calibration.OffsetMs)) calibration.OffsetMs = 0;
                calibration.OffsetMs = GameMath.Clamp((float)calibration.OffsetMs, -300, 300);
                if (!RhythmRules.Finite(calibration.VisualOffsetMs)) calibration.VisualOffsetMs = 0;
                calibration.VisualOffsetMs = GameMath.Clamp(calibration.VisualOffsetMs, -300, 300);
                Rules = config.CreateRules(calibration.OffsetMs, telemetry);
                config.Song.LoadAudioData();
                await UniTask.WaitUntil(() => config.Song.loadState != AudioDataLoadState.Loading, cancellationToken: ct);
                if (config.Song.loadState != AudioDataLoadState.Loaded) throw new InvalidOperationException("音频加载失败");
                actions = UnityEngine.Object.Instantiate(config.Input);
                for (int i = 0; i < 4; i++) { LaneAction(i).performed += OnLane; LaneAction(i).canceled += OnRelease; }
                judged = OnJudged;
                AudioSettings.OnAudioConfigurationChanged += AudioChanged;
                InputSystem.onDeviceChange += DeviceChanged;
                InputSystem.onAfterUpdate += InputBatchCompleted;
                gameplayEnabled = input.Actions.Gameplay.enabled; // lint-ok: 只保存动作图启用状态以便退出恢复，不采样按键；音游意图保留事件时间戳。
                input.DisableMap("Gameplay");
                pauseToken = pause.Acquire(this);
                using (telemetry.BeginSpan("view_ready")) view = await ui.OpenAsync<RhythmView>(config, ct);
                view.OnStartClicked += StartRound;
                view.OnPracticeClicked += StartPractice;
                view.OnBackClicked += Back;
                view.OnOffsetChanged += OffsetChanged;
                view.OnFocusLost += FocusLost;
                view.OnApplicationSuspended += ApplicationSuspended;
                view.OnDiagnosticExportClicked += ExportDiagnostic;
                view.OnCalibrationClicked += StartCalibration;
                view.OnVisualOffsetChanged += VisualOffsetChanged;
                view.SetOffset((float)calibration.OffsetMs);
                view.SetVisualOffset(calibration.VisualOffsetMs);
                view.Ready("选择虫儿飞单击试玩、短长按练习或参考拍校准");
                telemetry.Track("entered", ("notes", Rules.Count));
            }
            catch (Exception error)
            {
                telemetry.TrackError("enter_failed", error);
                await ExitAsync(CancellationToken.None);
                throw;
            }
        }
        public void StartRound()
        {
            if (closing || view == null || starting) return;
            StartRoundAsync(false).Forget();
        }
        public void StartPractice()
        {
            if (closing || view == null || starting) return;
            StartRoundAsync(true).Forget();
        }
        private async UniTask StartRoundAsync(bool practice)
        {
            if (!CanStart()) return;
            starting = true;
            StopRound(RhythmDiagnosticData.Reason.Restart);
            int preparingSession = session;
            view.Starting();
            try
            {
                await saves.WriteProfileAsync(ProfileName, new RhythmCalibrationData { OffsetMs = calibration.OffsetMs, VisualOffsetMs = calibration.VisualOffsetMs });
                if (closing || view == null || preparingSession != session) return;
                if (!CanStart()) return;
                practiceRound = practice;
                Rules = practice ? config.CreatePracticeRules(calibration.OffsetMs, telemetry) : config.CreateRules(calibration.OffsetMs, telemetry);
                roundDuration = practice ? config.PracticeDurationSeconds : config.DurationSeconds;
                AudioClip clip = config.Song;
                if (practice) { calibrationClip = CreateReferenceClip(roundDuration, 60d / config.CalibrationBpm); clip = calibrationClip; }
                playback = audio.PlayScheduledClip(clip, practice ? 0 : config.ClipStartSeconds, config.LeadInSeconds(calibration.VisualOffsetMs));
                playback.ScheduleEnd(roundDuration);
                ResetInputBoundary();
                BeginDiagnostic(practice, clip);
                actions.Enable();
                view.Begin(Rules, roundDuration, practice);
                view.Frame(playback.Position, Rules);
                telemetry.Track("started", ("offset_ms", calibration.OffsetMs));
            }
            catch (Exception error)
            {
                StopRound();
                if (view != null) view.Ready("保存或播放失败，请重试");
                telemetry.TrackError("start_failed", error);
            }
            finally { starting = false; }
        }
        private void OnLane(InputAction.CallbackContext context)
            => QueueInput(context, RhythmInputEdge.Press);
        private void OnRelease(InputAction.CallbackContext context)
            => QueueInput(context, RhythmInputEdge.Release);
        private void QueueInput(InputAction.CallbackContext context, RhythmInputEdge edge)
        {
            if (playback == null || closing) return;
            int lane = context.action.name[4] - '0';
            double seconds = playback.PositionAtInputTime(context.time);
            if (estimator != null)
            {
                if (edge == RhythmInputEdge.Press) estimator.Add(seconds);
                return;
            }
            var intent = new RhythmHitIntent(lane, seconds, edge, session);
            diagnostic.Enqueue(in intent, context.time, Time.realtimeSinceStartupAsDouble);
            if (edge == RhythmInputEdge.Press) view.Flash(lane, seconds);
        }
        private void OnJudged(RhythmHitResult result)
        {
            view.Hit(result, Rules);
            if (result.Grade == RhythmGrade.Miss) missFeedbackShown = true;
        }
        private void ResetInputBoundary()
        {
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int buffers);
            int sampleRate = AudioSettings.outputSampleRate;
            double bufferSeconds = (double)bufferLength * buffers / (sampleRate > 0 ? sampleRate : 1);
            double tolerance = bufferSeconds * 2 + playback.BridgeSampleSpanSeconds;
            clockGuard = new RhythmClockGuard(playback.DspStart, playback.InputStart,
                tolerance > 0.1 ? tolerance : 0.1);
            inputBoundary = playback.PositionAtInputTime(InputState.currentTime);
            completedInputBoundary = inputBoundary;
        }
        private void InputBatchCompleted()
        {
            if (playback == null) return;
            if (!InputModeSupported()) { Interrupt("输入模式已改变，本轮已停止，请恢复动态更新后重试", "input_mode_changed"); return; }
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            double boundary = playback.PositionAtInputTime(InputState.currentTime);
            if (boundary < inputBoundary) { Interrupt("输入时钟发生回退，本轮已停止，请重试", "input_clock_reversed"); return; }
            // 只封口上一批已完成的输入时间；当前批次的原始事件仍可排队，DSP 不作为输入水位。
            completedInputBoundary = inputBoundary;
            inputBoundary = boundary;
        }
        public void Tick()
        {
            if (playback == null || view == null || closing) return;
            if (!CanContinue()) return;
            double seconds = playback.Position;
            if (estimator != null)
            {
                view.Calibrating(seconds, estimator.Count, config.CalibrationWarmupBeats, calibrationBeatSeconds);
                if (completedInputBoundary >= calibrationDuration) FinishCalibration();
                return;
            }
            int oldMiss = Rules.Miss; int oldScore = Rules.Score; int oldHolds = Rules.CompletedHolds;
            missFeedbackShown = false;
            diagnostic.Drain(completedInputBoundary, inputBoundary, completedInputBoundary, sampledRealtime, sampledDsp, judged);
            if (diagnostic.LateInputs > 0)
            {
                StopRound(RhythmDiagnosticData.Reason.LateInput); view.Ready("输入送达过迟，本轮已停止，请重试"); telemetry.TrackWarn("late_input_delivery"); return;
            }
            if (Rules.Miss > oldMiss && !missFeedbackShown) view.Missed(Rules);
            else if (Rules.CompletedHolds > oldHolds) view.HoldCompleted(Rules);
            else if (Rules.Score != oldScore) view.Score(Rules);
            view.Frame(seconds, Rules);
            double deadline = Rules.LastEndSeconds + (config.GoodMs + (Rules.OffsetMs > 0 ? Rules.OffsetMs : 0)) / 1000;
            double finish = (roundDuration > deadline ? roundDuration : deadline) + config.FinishBufferSeconds;
            if (completedInputBoundary < finish) return;
            StopRound(RhythmDiagnosticData.Reason.Finished);
            view.Finish(Rules, practiceRound);
            telemetry.Track("finished", ("perfect", Rules.Perfect), ("good", Rules.Good), ("miss", Rules.Miss), ("score", Rules.Score));
        }
        private void StopRound(RhythmDiagnosticData.Reason reason = RhythmDiagnosticData.Reason.Stopped)
        {
            session++;
            if (diagnostic != null)
            {
                diagnostic.End(reason);
                LastDiagnostic = diagnostic.Snapshot();
                diagnostic = null;
            }
            AudioPlayback old = playback; playback = null; // Disable 会同步 canceled，先关闭判定入口。
            clockGuard = null;
            if (actions != null) actions.Disable();
            if (old != null) old.Dispose();
            if (view != null) view.SetDiagnosticAvailable(LastDiagnostic != null);
            estimator = null;
            if (calibrationClip != null) { UnityEngine.Object.Destroy(calibrationClip); calibrationClip = null; }
        }
        private void FocusLost()
        {
            Interrupt("窗口失焦，演奏已停止；点击开始重试", "focus_lost");
        }
        private void ApplicationSuspended() => Interrupt("应用已挂起，本轮已停止；恢复后点击开始重试", "application_paused");
        private void Interrupt(string message, string reason)
        {
            if (closing || (playback == null && !starting)) return;
            StopRound(DiagnosticReason(reason));
            if (view != null) view.Ready(message);
            telemetry.TrackWarn("interrupted", TelemetryProps.Of(("reason", reason)));
        }
        private void AudioChanged(bool changed) => Interrupt("音频配置已改变，本轮已停止；点击开始重试", "audio_configuration_changed");
        private void DeviceChanged(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
                Interrupt("输入设备已断开，本轮已停止；重新连接后重试", "input_device_lost");
        }
        private static bool InputModeSupported() => InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        private bool CanStart()
        {
            string reason = !InputModeSupported() ? "input_mode_unsupported" :
                AudioListener.pause ? "audio_paused" : view.IsApplicationPaused ? "application_paused" : null;
            if (reason == null) return true;
            StopRound();
            view.Ready(reason == "input_mode_unsupported" ? "当前输入模式不支持音游，请使用动态更新后重试" : "音频或应用仍在暂停，请恢复后重试");
            telemetry.TrackWarn("start_rejected", TelemetryProps.Of(("reason", reason)));
            return false;
        }
        private bool CanContinue()
        {
            if (!InputModeSupported()) { Interrupt("输入模式已改变，本轮已停止，请恢复动态更新后重试", "input_mode_changed"); return false; }
            if (AudioListener.pause) { Interrupt("音频已暂停，本轮已停止；恢复后点击开始重试", "audio_paused"); return false; }
            if (view.IsApplicationPaused) { ApplicationSuspended(); return false; }
            double before = Time.realtimeSinceStartupAsDouble;
            double dsp = AudioSettings.dspTime;
            double after = Time.realtimeSinceStartupAsDouble;
            sampledRealtime = (before + after) * 0.5;
            sampledDsp = dsp;
            if (diagnostic != null && sampledRealtime >= nextBridgeSample)
            {
                diagnostic.SampleBridge(sampledRealtime, dsp, after - before);
                nextBridgeSample = sampledRealtime + 0.25;
            }
            if (clockGuard.Check(sampledRealtime, dsp, after - before, out string reason)) return true;
            Interrupt("音频时钟已中断，本轮已停止；点击开始重新同步", reason);
            return false;
        }
        private static RhythmDiagnosticData.Reason DiagnosticReason(string reason)
        {
            switch (reason)
            {
                case "focus_lost": return RhythmDiagnosticData.Reason.FocusLost;
                case "audio_configuration_changed": return RhythmDiagnosticData.Reason.AudioChanged;
                case "input_device_lost": return RhythmDiagnosticData.Reason.DeviceChanged;
                case "clock_reversed": case "input_clock_reversed": return RhythmDiagnosticData.Reason.ClockRollback;
                case "audio_paused": return RhythmDiagnosticData.Reason.AudioPaused;
                case "application_paused": return RhythmDiagnosticData.Reason.ApplicationPaused;
                case "input_mode_changed": return RhythmDiagnosticData.Reason.InputModeChanged;
                case "clock_discontinuity": return RhythmDiagnosticData.Reason.ClockDiscontinuity;
                case "invalid_clock": return RhythmDiagnosticData.Reason.InvalidClock;
                default: return RhythmDiagnosticData.Reason.Stopped;
            }
        }
        private void BeginDiagnostic(bool practice, AudioClip clip)
        {
            var header = new RhythmDiagnosticData.Header(session, practice ? "rhythm-practice" : config.ChartId,
                practice ? clip.name : config.AudioKey, "chart-schema-" + config.SchemaVersion,
                Application.platform + "/Unity-" + Application.unityVersion, InputSystem.settings.updateMode.ToString(),
                practice ? 0 : config.ClipStartSeconds, playback.DspStart, playback.InputStart, playback.BridgeSampleSpanSeconds,
                Rules.ChartOffsetMs, Rules.OffsetMs, calibration.VisualOffsetMs, Rules.PerfectMs, Rules.GoodMs);
            diagnostic = new RhythmDiagnosticSession(header, Rules.CopyNotes(), telemetry: telemetry);
            Rules = diagnostic.Rules;
            nextBridgeSample = double.NegativeInfinity;
            view.SetDiagnosticAvailable(false);
        }
        private void ExportDiagnostic()
        {
            if (playback != null || starting || LastDiagnostic == null) return;
            try
            {
                string folder = Path.Combine(PlatformServiceBase.SaveRootOverride ?? Application.temporaryCachePath, "rhythm-diagnostics");
                Directory.CreateDirectory(folder);
                string file = "session-" + LastDiagnostic.SessionHeader.Session + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".rhd";
                using (var encoded = new MemoryStream())
                {
                    RhythmDiagnosticCodec.Write(encoded, LastDiagnostic);
                    encoded.Position = 0;
                    using (var destination = new FileStream(Path.Combine(folder, file), FileMode.CreateNew, FileAccess.Write))
                        encoded.CopyTo(destination);
                }
                var report = RhythmDiagnosticReplay.Run(LastDiagnostic);
                notifications.Show("本轮诊断已保存", "临时缓存 rhythm-diagnostics/" + file +
                    (report.IsComplete ? "；可用于判定回放。" : "；记录不完整，请勿当作完整验收。"));
                telemetry.Track("diagnostic_saved", ("session", LastDiagnostic.SessionHeader.Session), ("complete", report.IsComplete));
            }
            catch (Exception error)
            {
                telemetry.TrackError("diagnostic_save_failed", error);
                notifications.Show("诊断未保存", "请检查存储空间后重试，内存中的本轮记录仍保留。");
            }
        }
        private void OffsetChanged(float value) { calibration.OffsetMs = value; }
        private void VisualOffsetChanged(float value) { calibration.VisualOffsetMs = value; }
        public void StartCalibration()
        {
            if (closing || view == null || starting) return;
            if (!CanStart()) return;
            StopRound(); view.Starting();
            try
            {
                calibrationBeatSeconds = 60d / config.CalibrationBpm;
                int totalBeats = config.CalibrationWarmupBeats + config.CalibrationSampleBeats;
                calibrationDuration = (totalBeats + 1) * calibrationBeatSeconds;
                var targets = new double[config.CalibrationSampleBeats];
                for (int i = 0; i < targets.Length; i++) targets[i] = (config.CalibrationWarmupBeats + i + 1) * calibrationBeatSeconds;
                estimator = new RhythmCalibrationEstimator(targets, config.CalibrationMinimumSamples, config.CalibrationWindowMs, config.CalibrationMaxMadMs);
                calibrationClip = CreateReferenceClip(calibrationDuration, calibrationBeatSeconds);
                playback = audio.PlayScheduledClip(calibrationClip, 0, config.CountdownSeconds);
                playback.ScheduleEnd(calibrationDuration);
                ResetInputBoundary();
                actions.Enable();
                view.Calibrating(playback.Position, 0, config.CalibrationWarmupBeats, calibrationBeatSeconds);
                telemetry.Track("calibration_started");
            }
            catch (Exception error)
            {
                StopRound(); view.Ready("校准启动失败，请重试"); telemetry.TrackError("calibration_start_failed", error);
            }
        }
        private static AudioClip CreateReferenceClip(double duration, double beatSeconds)
        {
            const int sampleRate = 48000;
            var samples = new float[(int)(duration * sampleRate)];
            for (double beat = beatSeconds; beat < duration; beat += beatSeconds)
            {
                int start = (int)(beat * sampleRate);
                for (int i = 0; i < sampleRate / 30 && start + i < samples.Length; i++)
                    samples[start + i] = GameMath.Sin(i * 2 * 3.14159265f * 1000 / sampleRate) * (1 - i / (sampleRate / 30f)) * 0.35f;
            }
            var clip = AudioClip.Create("RhythmReference", samples.Length, 1, sampleRate, false);
            if (clip.SetData(samples, 0)) return clip;
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException("参考节拍生成失败");
        }
        private void FinishCalibration()
        {
            double offset; double mad; int accepted;
            bool valid = estimator.TryEstimate(out offset, out mad, out accepted);
            int count = estimator.Count;
            StopRound();
            if (!valid)
            {
                view.Ready($"校准不稳定或样本不足（{count} 次，MAD {mad:0} ms），已保留原补偿");
                telemetry.TrackWarn("calibration_rejected", TelemetryProps.Of(("samples", count)));
                return;
            }
            calibration.OffsetMs = offset;
            view.SetOffset((float)offset);
            view.Ready($"综合偏差 {offset:+0;-0;0} ms · 有效 {accepted} 次 · MAD {mad:0} ms，可手动微调");
            SaveCalibrationAsync().Forget();
        }
        private async UniTask SaveCalibrationAsync()
        {
            try
            {
                await saves.WriteProfileAsync(ProfileName, new RhythmCalibrationData { OffsetMs = calibration.OffsetMs, VisualOffsetMs = calibration.VisualOffsetMs });
                telemetry.Track("calibration_saved", ("offset_ms", calibration.OffsetMs));
            }
            catch (Exception error)
            {
                telemetry.TrackError("calibration_save_failed", error);
                notifications.Show("延迟补偿未保存", "当前参数仍可试玩，开始演奏或退出时会再次尝试保存。");
            }
        }
        private void Back()
        {
            if (closing) return;
            closing = true;
            StopRound();
            flow.GoToAsync<TitleState>().Forget();
        }
        public override async UniTask ExitAsync(CancellationToken ct)
        {
            closing = true;
            StopRound();
            try
            {
                if (view != null)
                {
                    view.OnStartClicked -= StartRound; view.OnBackClicked -= Back;
                    view.OnPracticeClicked -= StartPractice;
                    view.OnOffsetChanged -= OffsetChanged; view.OnFocusLost -= FocusLost;
                    view.OnApplicationSuspended -= ApplicationSuspended;
                    view.OnDiagnosticExportClicked -= ExportDiagnostic;
                    view.OnCalibrationClicked -= StartCalibration; view.OnVisualOffsetChanged -= VisualOffsetChanged;
                    await ui.CloseAsync(view, CancellationToken.None);
                }
            }
            finally { view = null; Release(); }
            if (calibration != null)
            {
                try
                {
                    await saves.WriteProfileAsync(ProfileName, calibration, ct);
                    telemetry.Track("calibration_saved", ("offset_ms", calibration.OffsetMs));
                }
                catch (Exception error)
                {
                    telemetry.TrackError("calibration_save_failed", error);
                    notifications.Show("延迟补偿未保存", "请检查存储空间，稍后重新调整并开始演奏。");
                }
            }
        }
        private void Release()
        {
            AudioSettings.OnAudioConfigurationChanged -= AudioChanged;
            InputSystem.onDeviceChange -= DeviceChanged;
            InputSystem.onAfterUpdate -= InputBatchCompleted;
            if (actions != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    var lane = actions.FindAction("Rhythm/Lane" + i);
                    if (lane == null) continue; // 配置缺动作时 Enter 会失败，清理仍须完成。
                    lane.performed -= OnLane; lane.canceled -= OnRelease;
                }
                UnityEngine.Object.Destroy(actions); actions = null;
            }
            if (pauseToken != null) { pauseToken.Dispose(); pauseToken = null; }
            if (gameplayEnabled) { input.EnableMap("Gameplay"); gameplayEnabled = false; }
        }
        public void Dispose() { StopRound(); Release(); }
    }
}
