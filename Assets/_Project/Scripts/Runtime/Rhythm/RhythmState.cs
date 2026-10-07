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
        private RhythmConfig config;
        private RhythmCatalogConfig catalog;
        private RhythmSongData selectedSong;
        private RhythmProgressData progress;
        private bool inSongMenu;
        private readonly RhythmSelectionRules selection = new RhythmSelectionRules();
        private long playingTicket;
        private System.Threading.Tasks.Task progressWrite = System.Threading.Tasks.Task.CompletedTask;
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
        private bool calibrationApplying;
        private System.Threading.Tasks.Task calibrationApplyWrite = System.Threading.Tasks.Task.CompletedTask;
        private int session;
        private RhythmDiagnosticSession diagnostic;
        private double sampledRealtime;
        private double sampledDsp;
        private double nextBridgeSample;
        private Action<RhythmHitResult> judged;
        private RhythmClockGuard clockGuard;
        private RhythmCalibrationEstimator estimator;
        private RhythmCalibrationDiagnostic calibrationDiagnostic;
        private string calibrationDiagnosticNotice;
        public string LastCalibrationDiagnosticPath { get; private set; }
        private AudioClip calibrationClip;
        private double calibrationDuration;
        private double calibrationBeatSeconds;
        private RhythmCalibrationResult pendingCalibration;
        private bool supplementUsed;
        private bool calibrationSupplement;
        private bool calibrationPreview;
        private double calibrationPreviewOffset;
        private int calibrationWarmup;
        private int calibrationTargets;
        private double inputBoundary;
        private double completedInputBoundary;
        private double roundDuration;
        private bool practiceRound;
        private bool manualCalibration;
        private double resumeInputFloor = double.NegativeInfinity;
        private bool reconcileResumeHolds;
        private bool missFeedbackShown;
        private RhythmPlayRequest externalRequest;
        private RhythmExternalSession externalSession;
        private RhythmPlayRequest activeRequest;
        private RhythmRunResult pendingRunResult;
        private RhythmExternalSession pendingResultConsumer;
        public RhythmRunResult LastRunResult { get; private set; }
        public event Action<RhythmRunResult> OnRunFinished;
        public void ClearExternal()
        {
            if (IsPlaying || starting || calibrationApplying) throw new InvalidOperationException("请先结束当前演奏再清除外部请求");
            externalRequest = null; externalSession = null;
        }
        public void ConfigureExternal(RhythmPlayRequest request, RhythmExternalSession consumer)
        {
            if (request == null || consumer == null) throw new ArgumentNullException(nameof(request));
            if (IsPlaying || starting || calibrationApplying) throw new InvalidOperationException("请先结束当前演奏再配置外部请求");
            if (request.Mode == RhythmPlayMode.Practice) throw new ArgumentException("外部入口不启动练习");
            if (!consumer.CanAccessLibrary(request.ContextId)) throw new InvalidOperationException("曲库入口尚未解锁或上下文已过期");
            externalRequest = request; externalSession = consumer;
        }
        // 只有独立测试预设使用测试 key；正式 Player 和编辑器保留原档案契约。
        private string ProfileName => PlatformServiceBase.IsIsolatedTestBuild ? "rhythm-test-calibration" : "rhythm-calibration";
        private string ProgressProfileName => PlatformServiceBase.IsIsolatedTestBuild ? "rhythm-test-progress" : "rhythm-progress";
        public RhythmRules Rules { get; private set; }
        public string SelectedSongId => selectedSong == null ? config.ChartId : selectedSong.Id;
        public bool IsSongMenu => inSongMenu;
        public int BestScore(string id)
        {
            var song = catalog == null ? null : catalog.Find(id);
            return song != null && progress != null ? RhythmProgressRules.GetBestScore(progress, song) : 0;
        }
        public bool IsUnlocked(string id) => catalog != null && progress != null && catalog.IsUnlocked(catalog.Find(id), progress);
        public bool IsCleared(string id)
        {
            var song = catalog == null ? null : catalog.Find(id);
            return song != null && progress != null && (progress.ClearedCharts.Contains(song.ProgressKey) || RhythmProgressRules.ReadCurrentRecord(progress, song)?.Cleared == true);
        }
        public void ConfigureCatalog(RhythmCatalogConfig value)
        {
            if (view != null || IsPlaying || starting) throw new InvalidOperationException("会话打开后不能替换曲库");
            if (value == null) throw new ArgumentNullException(nameof(value));
            value.Validate(); catalog = value; selectedSong = value.Song(0); config = selectedSong.Chart;
        }
        public RhythmDiagnosticData LastDiagnostic { get; private set; }
        public RhythmCalibrationResult CalibrationCandidate => pendingCalibration;
        public bool IsPlaying => playback != null;
        public bool IsPaused => playback != null && playback.IsPaused;
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
            closing = false; inSongMenu = false;
            try
            {
                if (config.Song == null || config.Input == null || config.ApproachSeconds <= 0 || config.CountdownSeconds < 0.1f ||
                    config.ClipStartSeconds < 0 || config.DurationSeconds <= 0 || config.ClipStartSeconds + config.DurationSeconds > config.Song.length)
                    throw new InvalidOperationException("音游配置或音频片段无效");
                if (catalog != null)
                {
                    await progressWrite;
                    progress = null;
                    RhythmProgressArchive.EnsureSupportedAndBackup((saves as JsonSaveService)?.SaveRoot, ProgressProfileName, new RhythmProgressData().Version);
                    progress = await saves.ReadProfileAsync<RhythmProgressData>(ProgressProfileName, ct);
                    RhythmProgressRules.Normalize(progress);
                    if (catalog.MigrateProgress(progress)) progressWrite = SaveProgressAsync(progressWrite, progress.Copy());
                }
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
                view.OnLibraryStartClicked += StartFromLibrary;
                view.OnManualCalibrationClicked += StartManualCalibration;
                view.OnSettingsSaved += SaveSettings;
                view.OnSettingsCancelled += CancelSettings;
                view.OnPauseClicked += PauseRound;
                view.OnResumeClicked += ResumeRound;
                view.OnPracticeClicked += StartPractice;
                view.OnBackClicked += Back;
                view.OnOffsetChanged += OffsetChanged;
                view.OnFocusLost += FocusLost;
                view.OnApplicationSuspended += ApplicationSuspended;
                view.OnDiagnosticExportClicked += ExportDiagnostic;
                view.OnCalibrationClicked += StartCalibration;
                view.OnVisualOffsetChanged += VisualOffsetChanged;
                view.OnCalibrationApply += ApplyCalibration;
                view.OnCalibrationKeep += KeepCalibration;
                view.OnCalibrationSupplement += SupplementCalibration;
                view.OnCalibrationPreview += PreviewCalibration;
                view.SetOffset((float)calibration.OffsetMs);
                view.SetVisualOffset(calibration.VisualOffsetMs);
                view.Ready("选择歌曲、短长按练习或参考拍校准");
                if (catalog != null)
                {
                    view.OnSongSelected += SelectSong;
                    view.OnSongMenuClicked += ShowSongMenu;
                    view.ConfigureCatalog(catalog);
                    view.ConfigureSong(config, selectedSong.Title, selectedSong.Difficulty);
                    ShowSongMenu();
                    if (externalRequest != null)
                    {
                        var requested = catalog.Find(externalRequest.SongId);
                        if (requested == null) throw new ArgumentException("外部请求曲目不存在");
                        if (!catalog.IsUnlocked(requested, progress)) throw new InvalidOperationException("外部请求曲目尚未解锁");
                        await SelectSongAsync(requested);
                    }
                }
                telemetry.Track("entered", ("notes", Rules.Count));
            }
            catch (Exception error)
            {
                telemetry.TrackError("enter_failed", error);
                await ExitAsync(CancellationToken.None);
                throw;
            }
        }
        public void ShowSongMenu()
        {
            if (catalog == null || closing || view == null || calibrationApplying) return;
            if (manualCalibration)
            {
                manualCalibration = false;
                view.CancelSettingsPreview();
                view.SetOffset((float)calibration.OffsetMs); view.SetVisualOffset(calibration.VisualOffsetMs);
            }
            ClearCalibrationSuggestion();
            int stoppedSession = session + 1;
            StopRound();
            if (closing || view == null || stoppedSession != session) return;
            selection.LeaveSelection(); inSongMenu = true;
            starting = false;
            if (closing || view == null) return;
            view.Ready("选曲后点击开始；通过入门关解锁进阶测试曲");
            view.ShowSongMenu(catalog, progress);
            telemetry.Track("song_menu");
        }
        public void SelectSong(string id)
        {
            if (catalog == null || closing || view == null || starting || calibrationApplying) return;
            var song = catalog.Find(id);
            if (song == null || !catalog.IsUnlocked(song, progress))
            {
                telemetry.TrackWarn("song_locked");
                return;
            }
            if (externalRequest != null && song.Id != externalRequest.SongId)
            {
                view.Ready("外部会话已指定曲目，请结束后重新请求"); return;
            }
            SelectSongAsync(song).Forget();
        }
        private async UniTask SelectSongAsync(RhythmSongData song)
        {
            ClearCalibrationSuggestion();
            int preparingSession = session + 1;
            StopRound(RhythmDiagnosticData.Reason.Restart);
            if (closing || view == null || preparingSession != session) return;
            starting = true;
            inSongMenu = true; view.PreparingSelection();
            if (!selection.TrySelect(song.ProgressKey, true, out long ticket)) { starting = false; return; }
            try
            {
                var next = song.Chart;
                if (next.Song == null || next.Input == null || next.ApproachSeconds <= 0 || next.CountdownSeconds < 0.1f ||
                    next.ClipStartSeconds < 0 || next.DurationSeconds <= 0 || next.ClipStartSeconds + next.DurationSeconds > next.Song.length)
                    throw new InvalidOperationException("所选曲目的音频片段无效");
                var nextRules = next.CreateRules(calibration.OffsetMs, telemetry);
                next.Song.LoadAudioData();
                await UniTask.WaitUntil(() => next.Song.loadState != AudioDataLoadState.Loading || closing || preparingSession != session);
                if (closing || view == null || preparingSession != session) return;
                if (next.Song.loadState != AudioDataLoadState.Loaded) throw new InvalidOperationException("所选曲目音频加载失败");
                if (!selection.CompleteSelection(ticket)) return;
                ReleaseActions();
                config = next; selectedSong = song; Rules = nextRules; LastDiagnostic = null;
                actions = UnityEngine.Object.Instantiate(config.Input);
                for (int i = 0; i < 4; i++) { LaneAction(i).performed += OnLane; LaneAction(i).canceled += OnRelease; }
                view.ConfigureSong(config, song.Title, song.Difficulty);
                inSongMenu = false;
                view.SetDiagnosticAvailable(false);
                view.Ready($"通关需达到 {RhythmProgressRules.RequiredScore(Rules.Count, song.PassScoreRatio)} 分（满分的 {song.PassScoreRatio:P0}）");
                view.ShowCurrentRecord(song, progress);
                if (externalRequest == null) view.ShowSongMenu(catalog, progress);
                else view.HideSongMenu();
                telemetry.Track("song_selected", ("song", song.Id));
            }
            catch (Exception error)
            {
                selection.LeaveSelection();
                if (!closing && view != null) { view.ShowSongMenu(catalog, progress); inSongMenu = true; }
                telemetry.TrackError("song_select_failed", error);
                notifications.Show("选曲失败", "请检查音频与谱面资源，再重新选择。");
            }
            finally { if (preparingSession == session) starting = false; }
        }
        public void StartRound()
        {
            if (closing || view == null || starting || calibrationApplying || inSongMenu) return;
            StartRoundAsync(false).Forget();
        }
        private void StartFromLibrary()
        {
            if (closing || starting || calibrationApplying || view == null) return;
            StartFromLibraryAsync().Forget();
        }
        private async UniTask StartFromLibraryAsync()
        {
            if (inSongMenu)
            {
                await SelectSongAsync(selectedSong);
                if (closing || view == null || inSongMenu) return;
            }
            view.HideSongMenu();
            StartRound();
        }
        private void StartManualCalibration()
        {
            if (closing || starting || calibrationApplying || externalRequest != null || view == null) return;
            StartManualCalibrationAsync().Forget();
        }
        private async UniTask StartManualCalibrationAsync()
        {
            // 复用教学曲与 Practice 资格，不能产生自由演奏成绩或通关解锁。
            if (catalog != null)
            {
                await SelectSongAsync(catalog.Song(0));
                if (closing || view == null || inSongMenu) return;
            }
            manualCalibration = true;
            view.HideSongMenu();
            await StartRoundAsync(true);
            if (!closing && view != null) view.ShowManualCalibration();
        }
        private void SaveSettings(float offset, float visual)
        {
            if (closing || starting || calibrationApplying || view == null || !RhythmRules.Finite(offset) ||
                !RhythmRules.Finite(visual) || Game.Core.Simulation.GameMath.Abs(offset) > 300 || Game.Core.Simulation.GameMath.Abs(visual) > 300) return;
            calibrationApplyWrite = SaveSettingsAsync(offset, visual).AsTask();
        }
        private async UniTask SaveSettingsAsync(float offset, float visual)
        {
            calibrationApplying = true;
            view.SetCalibrationSaving(true);
            StopRound();
            var saved = new RhythmCalibrationData { OffsetMs = offset, VisualOffsetMs = visual };
            bool success = false;
            try
            {
                await saves.WriteProfileAsync(ProfileName, saved);
                calibration = saved;
                success = true;
                telemetry.Track("calibration_saved", ("offset_ms", offset));
            }
            catch (Exception error)
            {
                telemetry.TrackError("calibration_save_failed", error);
                notifications.Show("设置未保存", "原值保持；可重试保存或取消。");
            }
            finally
            {
                calibrationApplying = false;
                if (view != null && !closing) view.SetCalibrationSaving(false);
            }
            if (!success || closing || view == null) return;
            manualCalibration = false;
            view.SettingsSaved();
            view.SetOffset(offset); view.SetVisualOffset(visual);
            if (catalog != null) ShowSongMenu(); else view.Ready("设置已保存");
        }
        private void CancelSettings()
        {
            if (closing || calibrationApplying || view == null) return;
            if (manualCalibration) StopRound();
            manualCalibration = false;
            view.SetOffset((float)calibration.OffsetMs); view.SetVisualOffset(calibration.VisualOffsetMs);
            if (catalog != null) ShowSongMenu(); else view.Ready("已保留原设置");
        }
        public void PauseRound()
        {
            if (closing || starting || calibrationApplying || manualCalibration || playback == null || playback.IsPaused || estimator != null) return;
            Tick();
            if (playback == null || closing) return;
            playback.Pause();
            diagnostic.Suspend();
            view.ShowPause(true);
            telemetry.Track("paused");
        }
        public void ResumeRound()
        {
            if (closing || starting || calibrationApplying || playback == null || !playback.IsPaused) return;
            if (!InputModeSupported() || AudioListener.pause || view.IsApplicationPaused)
            { Interrupt("恢复条件不可用，本轮停止，请重试", "resume_unavailable"); return; }
            try
            {
                double floor = completedInputBoundary;
                resumeInputFloor = Time.realtimeSinceStartupAsDouble;
                playback.Resume(resumeInputFloor);
                diagnostic.Resume();
                ResetInputBoundary();
                if (inputBoundary < floor) inputBoundary = floor;
                completedInputBoundary = inputBoundary;
                reconcileResumeHolds = true;
                view.ShowPause(false);
                telemetry.Track("resumed");
            }
            catch (Exception error)
            {
                CompleteRun(RhythmRunCompletion.TechnicalError, "resume_failed");
                Interrupt("恢复音频失败，本轮停止，请重试", "resume_failed");
                telemetry.TrackError("resume_failed", error);
            }
        }
        public void StartPractice()
        {
            if (closing || view == null || starting || calibrationApplying) return;
            if (inSongMenu) { StartPracticeFromLibraryAsync().Forget(); return; }
            StartRoundAsync(true).Forget();
        }
        private async UniTask StartPracticeFromLibraryAsync()
        {
            await SelectSongAsync(selectedSong);
            if (closing || view == null || inSongMenu) return;
            await StartRoundAsync(true);
        }
        private async UniTask StartRoundAsync(bool practice)
        {
            if (externalRequest != null && practice) { view.Ready("外部会话不使用个人练习入口"); return; }
            if (!CanStart()) return;
            ClearCalibrationSuggestion();
            int preparingSession = session + 1;
            StopRound(RhythmDiagnosticData.Reason.Restart);
            if (closing || view == null || preparingSession != session) return;
            starting = true;
            view.Starting();
            try
            {
                if (!manualCalibration)
                    await saves.WriteProfileAsync(ProfileName, new RhythmCalibrationData { OffsetMs = calibration.OffsetMs, VisualOffsetMs = calibration.VisualOffsetMs });
                if (closing || view == null || preparingSession != session) return;
                if (!CanStart()) return;
                if (selectedSong != null && !selection.TryStart(practice, out playingTicket)) return;
                practiceRound = practice;
                Rules = practice && !manualCalibration ? config.CreatePracticeRules(calibration.OffsetMs, telemetry) : config.CreateRules(calibration.OffsetMs, telemetry);
                if (externalRequest != null)
                {
                    if (externalRequest.SongId != SelectedSongId || !externalSession.TryBegin(externalRequest, out string denied))
                    { view.Ready("外部演奏请求不可用，请重新发起"); return; }
                    activeRequest = externalRequest;
                }
                else activeRequest = new RhythmPlayRequest(Guid.NewGuid().ToString("N"), "rhythm-demo", SelectedSongId,
                    practice ? RhythmPlayMode.Practice : RhythmPlayMode.FreePlay);
                roundDuration = practice && !manualCalibration ? config.PracticeDurationSeconds : config.DurationSeconds;
                AudioClip clip = config.Song;
                if (practice && !manualCalibration) { calibrationClip = CreateReferenceClip(roundDuration, 60d / config.CalibrationBpm); clip = calibrationClip; }
                playback = audio.PlayScheduledClip(clip, practice && !manualCalibration ? 0 : config.ClipStartSeconds, config.LeadInSeconds(calibration.VisualOffsetMs));
                playback.ScheduleEnd(roundDuration);
                ResetInputBoundary();
                BeginDiagnostic(practice, clip);
                actions.Enable();
                view.Begin(Rules, roundDuration, practice && !manualCalibration);
                if (manualCalibration) view.ShowManualCalibration();
                view.Frame(playback.Position, Rules);
                telemetry.Track("started", ("offset_ms", calibration.OffsetMs));
            }
            catch (Exception error)
            {
                if (preparingSession != session) { telemetry.TrackError("start_failed", error); return; }
                int stoppedSession = session + 1;
                CompleteRun(RhythmRunCompletion.TechnicalError, "start_failed");
                StopRound();
                if (view != null && !closing && stoppedSession == session) view.Ready("保存或播放失败，请重试");
                telemetry.TrackError("start_failed", error);
            }
            finally { if (preparingSession == session) starting = false; }
        }
        private void OnLane(InputAction.CallbackContext context)
            => QueueInput(context, RhythmInputEdge.Press);
        private void OnRelease(InputAction.CallbackContext context)
            => QueueInput(context, RhythmInputEdge.Release);
        private void QueueInput(InputAction.CallbackContext context, RhythmInputEdge edge)
        {
            if (playback == null || closing || playback.IsPaused || context.time < resumeInputFloor) return;
            int lane = context.action.name[4] - '0';
            double seconds = playback.PositionAtInputTime(context.time);
            if (reconcileResumeHolds && seconds < completedInputBoundary) return;
            if (estimator != null)
            {
                if (edge == RhythmInputEdge.Press)
                {
                    bool sampling = seconds >= calibrationWarmup * calibrationBeatSeconds;
                    int beat = -1; double error = 0; string disposition = RhythmRules.Finite(seconds) ? "warmup" : "invalid_before_sampling";
                    bool accepted = sampling && estimator.TryAdd(seconds, out error, out beat, out disposition);
                    double before = Time.realtimeSinceStartupAsDouble;
                    double dsp = AudioSettings.dspTime;
                    double after = Time.realtimeSinceStartupAsDouble;
                    calibrationDiagnostic?.Record(context.time, seconds, beat, error, disposition, before, dsp, after);
                    if (accepted && calibrationPreview) view.CalibrationPreviewHit(error - calibrationPreviewOffset);
                }
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
            if (playback == null || playback.IsPaused) return;
            if (!InputModeSupported()) { Interrupt("输入模式已改变，本轮已停止，请恢复动态更新后重试", "input_mode_changed"); return; }
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            double boundary = playback.PositionAtInputTime(InputState.currentTime);
            if (reconcileResumeHolds)
            {
                if (boundary < completedInputBoundary) return;
                // 只在继续后的首个完整输入批次检查握持，暂停中不产生判定。
                for (int i = 0; i < Rules.Count; i++)
                    if (Rules.IsHolding(i) && !LaneAction(Rules.NoteLane(i)).IsPressed()) // lint-ok: 暂停恢复的单次握持同步，不作每帧玩法采样。
                    {
                        var release = new RhythmHitIntent(Rules.NoteLane(i), boundary, RhythmInputEdge.Release, session);
                        diagnostic.Enqueue(in release, InputState.currentTime, Time.realtimeSinceStartupAsDouble);
                    }
                reconcileResumeHolds = false;
            }
            if (boundary < inputBoundary) { Interrupt("输入时钟发生回退，本轮已停止，请重试", "input_clock_reversed"); return; }
            // 只封口上一批已完成的输入时间；当前批次的原始事件仍可排队，DSP 不作为输入水位。
            completedInputBoundary = inputBoundary;
            inputBoundary = boundary;
        }
        public void Tick()
        {
            if (playback == null || view == null || closing) return;
            if (playback.IsPaused)
            {
                if (!InputModeSupported() || AudioListener.pause || view.IsApplicationPaused)
                    Interrupt("暂停期间运行条件已改变，本轮停止，请重试", "pause_invalidated");
                return;
            }
            if (!CanContinue()) return;
            double seconds = playback.Position;
            if (estimator != null)
            {
                if (calibrationPreview) view.CalibrationPreviewFrame(seconds, calibrationDuration, calibrationPreviewOffset);
                else view.Calibrating(seconds, estimator.Count, calibrationWarmup, calibrationBeatSeconds, calibrationTargets);
                if (completedInputBoundary >= calibrationDuration) FinishCalibration();
                return;
            }
            int oldMiss = Rules.Miss; int oldScore = Rules.Score; int oldHolds = Rules.CompletedHolds;
            missFeedbackShown = false;
            diagnostic.Drain(completedInputBoundary, inputBoundary, completedInputBoundary, sampledRealtime, sampledDsp, judged);
            if (diagnostic.LateInputs > 0)
            {
                StopRound(RhythmDiagnosticData.Reason.LateInput); if (view != null && !closing) view.Ready("输入送达过迟，本轮已停止，请重试"); telemetry.TrackWarn("late_input_delivery"); return;
            }
            if (Rules.Miss > oldMiss && !missFeedbackShown) view.Missed(Rules);
            else if (Rules.CompletedHolds > oldHolds) view.HoldCompleted(Rules);
            else if (Rules.Score != oldScore) view.Score(Rules);
            view.Frame(seconds, Rules);
            double deadline = Rules.LastEndSeconds + (config.GoodMs + (Rules.OffsetMs > 0 ? Rules.OffsetMs : 0)) / 1000;
            double finish = (roundDuration > deadline ? roundDuration : deadline) + config.FinishBufferSeconds;
            if (completedInputBoundary < finish) return;
            bool recordSong = false;
            if (selectedSong != null) selection.TryFinish(playingTicket, selectedSong.ProgressKey, Rules.Finished, out recordSong);
            var result = CompleteRun(RhythmRunCompletion.Completed, "finished");
            StopRound(RhythmDiagnosticData.Reason.Finished);
            if (manualCalibration) view.Ready("试听结束，保存试用值或取消保留原值");
            else view.Finish(Rules, practiceRound);
            if (!practiceRound && recordSong && selectedSong != null && progress != null && result?.Mode == RhythmPlayMode.FreePlay)
            {
                bool recorded = result != null && RhythmProgressRules.RecordRun(progress, selectedSong, result);
                catalog.MigrateProgress(progress);
                bool passed = IsCleared(selectedSong.Id);
                view.SetSongResult(passed, RhythmProgressRules.RequiredScore(Rules.Count, selectedSong.PassScoreRatio),
                    BestScore(selectedSong.Id));
                if (recorded) progressWrite = SaveProgressAsync(progressWrite, progress.Copy());
            }
            telemetry.Track("finished", ("perfect", Rules.Perfect), ("good", Rules.Good), ("miss", Rules.Miss), ("score", Rules.Score));
            PublishPendingResult();
        }
        private async System.Threading.Tasks.Task SaveProgressAsync(System.Threading.Tasks.Task previous, RhythmProgressData snapshot)
        {
            try
            {
                await previous;
                await saves.WriteProfileAsync(ProgressProfileName, snapshot);
                telemetry.Track("progress_saved");
            }
            catch (Exception error)
            {
                telemetry.TrackError("progress_save_failed", error);
                if (!closing) notifications.Show("成绩未保存", "本次进度仍在内存中，退出时会再次尝试保存。");
            }
        }
        private void StopRound(RhythmDiagnosticData.Reason reason = RhythmDiagnosticData.Reason.Stopped)
        {
            SaveCalibrationDiagnostic(reason.ToString());
            CompleteRun(reason == RhythmDiagnosticData.Reason.InvalidClock || reason == RhythmDiagnosticData.Reason.ClockDiscontinuity ||
                reason == RhythmDiagnosticData.Reason.ClockRollback || reason == RhythmDiagnosticData.Reason.LateInput ?
                RhythmRunCompletion.TechnicalError : RhythmRunCompletion.Aborted, reason.ToString());
            session++;
            resumeInputFloor = double.NegativeInfinity; reconcileResumeHolds = false;
            if (view != null) view.ShowPause(false);
            starting = false;
            selection.Invalidate();
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
            calibrationPreview = false; calibrationSupplement = false;
            if (calibrationClip != null) { UnityEngine.Object.Destroy(calibrationClip); calibrationClip = null; }
            if (pendingRunResult != null && pendingRunResult.Completion != RhythmRunCompletion.Completed) PublishPendingResult();
        }
        private RhythmRunResult CompleteRun(RhythmRunCompletion completion, string reason)
        {
            if (activeRequest == null || Rules == null) return null;
            var request = activeRequest; activeRequest = null; // 回调前封口，重入/重试不能重复消费。
            var song = selectedSong;
            var result = new RhythmRunResult(request.RunId, request.ContextId, request.Mode, completion, reason,
                request.SongId, config.ChartId, song == null ? "standalone" : song.Revision,
                song == null ? "four-lane-tap-hold" : song.RulesetId, song == null ? "1" : song.ScoringVersion,
                Rules.Count, Rules.Perfect, Rules.Good, Rules.Miss, Rules.CompletedHolds, Rules.Score, Rules.MaxCombo, Rules.OffsetMs);
            LastRunResult = result;
            pendingRunResult = result;
            pendingResultConsumer = externalRequest == request ? externalSession : null;
            return result;
        }
        private void PublishPendingResult()
        {
            var result = pendingRunResult;
            var consumer = pendingResultConsumer;
            pendingRunResult = null; pendingResultConsumer = null;
            if (result == null) return;
            if (consumer != null)
            {
                try { consumer.Consume(result); }
                catch (Exception error) { telemetry.TrackError("external_result_failed", error); }
            }
            var listeners = OnRunFinished;
            if (listeners != null)
                foreach (Action<RhythmRunResult> listener in listeners.GetInvocationList())
                    try { listener(result); } catch (Exception error) { telemetry.TrackError("result_listener_failed", error); }
        }
        private void FocusLost()
        {
            Interrupt("窗口失焦，演奏已停止；点击开始重试", "focus_lost");
        }
        private void ApplicationSuspended() => Interrupt("应用已挂起，本轮已停止；恢复后点击开始重试", "application_paused");
        private void Interrupt(string message, string reason)
        {
            if (closing || (playback == null && !starting)) return;
            bool showSuggestion = pendingCalibration != null && (calibrationPreview || calibrationSupplement);
            bool wasCalibrating = estimator != null;
            int stoppedSession = session + 1;
            StopRound(DiagnosticReason(reason));
            if (view != null && !closing && stoppedSession == session)
            {
                if (showSuggestion) ShowCalibrationSuggestion(message);
                else if (!wasCalibrating && !manualCalibration && catalog != null && externalRequest == null)
                    view.Interrupted(message);
                else view.Ready(message + (wasCalibrating && LastCalibrationDiagnosticPath == null && calibrationDiagnosticNotice != null ?
                    "\n" + calibrationDiagnosticNotice : ""));
            }
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
            if (view != null && !closing) view.Ready(reason == "input_mode_unsupported" ? "当前输入模式不支持音游，请使用动态更新后重试" : "音频或应用仍在暂停，请恢复后重试");
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
            return ObserveClockSample(before, dsp, after);
        }
        // 先校验再记录；非法样本不得进入诊断，也必须走既有中断清理。
        private bool ObserveClockSample(double before, double dsp, double after)
        {
            sampledRealtime = (before + after) * 0.5;
            sampledDsp = dsp;
            if (!clockGuard.Check(sampledRealtime, dsp, after - before, out string reason))
            {
                Interrupt("音频时钟已中断，本轮已停止；点击开始重新同步", reason);
                return false;
            }
            if (diagnostic != null && sampledRealtime >= nextBridgeSample)
            {
                diagnostic.SampleBridge(sampledRealtime, dsp, after - before);
                nextBridgeSample = sampledRealtime + 0.25;
            }
            return true;
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
                practice && !manualCalibration ? 0 : config.ClipStartSeconds, playback.DspStart, playback.InputStart, playback.BridgeSampleSpanSeconds,
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
        private void OffsetChanged(float value) { if (!calibrationApplying) calibration.OffsetMs = value; }
        private void VisualOffsetChanged(float value) { if (!calibrationApplying) calibration.VisualOffsetMs = value; }
        public void StartCalibration()
        {
            if (closing || view == null || starting || calibrationApplying) return;
            if (inSongMenu) { inSongMenu = false; view.HideSongMenu(); }
            if (!CanStart()) return;
            pendingCalibration = null; supplementUsed = false;
            view.HideCalibrationResult();
            StartCalibrationRun(config.CalibrationWarmupBeats, config.CalibrationSampleBeats, config.CalibrationMinimumSamples, false, false, 0);
        }
        private void StartCalibrationRun(int warmup, int count, int minimum, bool supplement, bool preview, double previewOffset)
        {
            int preparingSession = session + 1;
            StopRound();
            if (closing || view == null || preparingSession != session || inSongMenu) return;
            view.Starting();
            try
            {
                calibrationBeatSeconds = 60d / config.CalibrationBpm;
                calibrationWarmup = warmup; calibrationTargets = count;
                calibrationSupplement = supplement; calibrationPreview = preview; calibrationPreviewOffset = previewOffset;
                int totalBeats = warmup + count;
                calibrationDuration = (totalBeats + 1) * calibrationBeatSeconds;
                var targets = new double[count];
                for (int i = 0; i < targets.Length; i++) targets[i] = (warmup + i + 1) * calibrationBeatSeconds;
                estimator = new RhythmCalibrationEstimator(targets, minimum, config.CalibrationWindowMs, config.CalibrationMaxMadMs);
                calibrationClip = CreateReferenceClip(calibrationDuration, calibrationBeatSeconds);
                playback = audio.PlayScheduledClip(calibrationClip, 0, config.CountdownSeconds);
                playback.ScheduleEnd(calibrationDuration);
                calibrationDiagnostic = new RhythmCalibrationDiagnostic(targets, new {
                    formatVersion = 1, source = string.IsNullOrEmpty(PlatformServiceBase.SaveRootOverride) ? "runtime" : "isolated-test", strategy = RhythmCalibrationEstimator.Strategy, session, mode = preview ? "preview" : supplement ? "supplement" : "primary",
                    bpm = config.CalibrationBpm, warmup, count, minimum, windowMs = config.CalibrationWindowMs,
                    maxMadMs = config.CalibrationMaxMadMs, blockSpreadLimitMs = 20, originalOffsetMs = calibration.OffsetMs,
                    originalVisualOffsetMs = calibration.VisualOffsetMs,
                    dspStart = playback.DspStart, inputStart = playback.InputStart, bridgeSpanSeconds = playback.BridgeSampleSpanSeconds,
                    inputUpdateMode = InputSystem.settings.updateMode.ToString()
                });
                ResetInputBoundary();
                actions.Enable();
                if (preview) view.CalibrationPreviewFrame(playback.Position, calibrationDuration, previewOffset);
                else view.Calibrating(playback.Position, 0, warmup, calibrationBeatSeconds, count);
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
            bool preview = calibrationPreview; bool supplement = calibrationSupplement;
            var result = estimator.Analyze();
            SaveCalibrationDiagnostic("Completed");
            StopRound();
            if (preview)
            {
                ShowCalibrationSuggestion();
                return;
            }
            pendingCalibration = supplement ? RhythmCalibrationResult.Supplement(pendingCalibration, result, config.CalibrationMinimumSamples, config.CalibrationMaxMadMs) : result;
            ShowCalibrationSuggestion();
            telemetry.Track("calibration_analyzed", ("reason", pendingCalibration.Reason.ToString()), ("matched", pendingCalibration.Matched));
        }
        private void SaveCalibrationDiagnostic(string endReason)
        {
            var recording = calibrationDiagnostic; calibrationDiagnostic = null;
            if (recording == null || estimator == null) return;
            var result = estimator.Analyze();
            bool saved = recording.TrySave(Path.Combine(PlatformServiceBase.SaveRootOverride ?? Application.temporaryCachePath, "rhythm-diagnostics"),
                result, endReason, out string path, out _);
            calibrationDiagnosticNotice = saved ? "本机校准诊断已记录（最近5轮）" : "本机校准诊断未保存；原补偿不变";
            LastCalibrationDiagnosticPath = path;
        }
        private void ShowCalibrationSuggestion(string notice = null)
        {
            if (pendingCalibration == null || view == null || closing) return;
            view.Ready("校准已结束，原补偿未改变；可试听确认或保留继续");
            if (calibrationDiagnosticNotice != null)
                notice = (notice == null ? "" : notice + "\n") + calibrationDiagnosticNotice;
            view.ShowCalibrationResult(pendingCalibration, calibration.OffsetMs, CanSupplementCalibration(), notice);
        }
        private bool CanSupplementCalibration() => pendingCalibration != null && !supplementUsed &&
            pendingCalibration.CanSupplement && pendingCalibration.Accepted >= config.CalibrationMinimumSamples - 8;
        private void SupplementCalibration()
        {
            if (closing || starting || calibrationApplying || !CanSupplementCalibration() || view == null || !CanStart()) return;
            supplementUsed = true; view.HideCalibrationResult();
            StartCalibrationRun(0, 8, 6, true, false, 0);
        }
        private void PreviewCalibration(bool candidate)
        {
            if (closing || starting || calibrationApplying || pendingCalibration == null || view == null || !CanStart() || candidate && !pendingCalibration.HasCandidate) return;
            StartCalibrationRun(0, 8, 6, false, true, candidate ? pendingCalibration.OffsetMs : calibration.OffsetMs);
        }
        private void ApplyCalibration()
        {
            if (closing || starting || calibrationApplying || pendingCalibration == null || !pendingCalibration.HasCandidate || view == null) return;
            calibrationApplyWrite = ApplyCalibrationAsync().AsTask();
        }
        private async UniTask ApplyCalibrationAsync()
        {
            var saved = new RhythmCalibrationData { OffsetMs = pendingCalibration.OffsetMs, VisualOffsetMs = calibration.VisualOffsetMs };
            StopRound();
            calibrationApplying = true;
            ShowCalibrationSuggestion("正在保存估计，原补偿暂未改变；请等待完成");
            view.SetCalibrationSaving(true);
            bool success = false;
            try
            {
                await saves.WriteProfileAsync(ProfileName, saved);
                calibration.OffsetMs = saved.OffsetMs;
                calibration.VisualOffsetMs = saved.VisualOffsetMs;
                pendingCalibration = null;
                success = true;
            }
            catch (Exception error)
            {
                telemetry.TrackError("calibration_save_failed", error);
                notifications.Show("校准估计未保存", "原补偿保持，可重试采用或保留原值。");
            }
            finally
            {
                calibrationApplying = false;
                if (view != null && !closing) view.SetCalibrationSaving(false);
            }
            if (view == null || closing) return;
            if (success)
            {
                view.SetOffset((float)saved.OffsetMs);
                view.HideCalibrationResult();
                if (catalog != null && externalRequest == null) ShowSongMenu();
                else view.Ready($"已采用并保存输入补偿 {saved.OffsetMs:+0;-0;0} ms，视觉值保持");
            }
            else ShowCalibrationSuggestion("采用未保存，原补偿保持；可重试采用或保留原值");
            if (success) telemetry.Track("calibration_saved", ("offset_ms", saved.OffsetMs));
        }
        private void KeepCalibration()
        {
            if (closing || view == null || calibrationApplying) return;
            StopRound(); ClearCalibrationSuggestion();
            if (catalog != null && externalRequest == null) ShowSongMenu();
            else view.Ready("已保留原补偿；可继续演奏或手动调节");
        }
        private void ClearCalibrationSuggestion()
        {
            pendingCalibration = null;
            if (view != null) view.HideCalibrationResult();
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
            await calibrationApplyWrite;
            ClearCalibrationSuggestion();
            StopRound();
            try
            {
                if (view != null)
                {
                    view.OnStartClicked -= StartRound; view.OnBackClicked -= Back;
                    view.OnLibraryStartClicked -= StartFromLibrary;
                    view.OnManualCalibrationClicked -= StartManualCalibration;
                    view.OnSettingsSaved -= SaveSettings;
                    view.OnSettingsCancelled -= CancelSettings;
                    view.OnPauseClicked -= PauseRound; view.OnResumeClicked -= ResumeRound;
                    view.OnSongSelected -= SelectSong; view.OnSongMenuClicked -= ShowSongMenu;
                    view.OnPracticeClicked -= StartPractice;
                    view.OnOffsetChanged -= OffsetChanged; view.OnFocusLost -= FocusLost;
                    view.OnApplicationSuspended -= ApplicationSuspended;
                    view.OnDiagnosticExportClicked -= ExportDiagnostic;
                    view.OnCalibrationClicked -= StartCalibration; view.OnVisualOffsetChanged -= VisualOffsetChanged;
                    view.OnCalibrationApply -= ApplyCalibration; view.OnCalibrationKeep -= KeepCalibration;
                    view.OnCalibrationSupplement -= SupplementCalibration; view.OnCalibrationPreview -= PreviewCalibration;
                    await ui.CloseAsync(view, CancellationToken.None);
                }
            }
            finally { view = null; Release(); }
            if (progress != null)
            {
                progressWrite = SaveProgressAsync(progressWrite, progress.Copy());
                await progressWrite;
            }
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
            ReleaseActions();
            if (pauseToken != null) { pauseToken.Dispose(); pauseToken = null; }
            if (gameplayEnabled) { input.EnableMap("Gameplay"); gameplayEnabled = false; }
        }
        private void ReleaseActions()
        {
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
        }
        public void Dispose() { closing = true; ClearCalibrationSuggestion(); StopRound(); externalRequest = null; externalSession = null; Release(); }
    }
}
