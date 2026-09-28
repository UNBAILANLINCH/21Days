// 职责：演出管线的对外入口——按 id 实例化演出预制体并摆到世界位姿、持世界时停令牌、切输入图、开演出面板、藏 HUD、
//   让舞台相机接管画面（世界舞台），每帧把确认 / 长按跳过 / 自动 / 台词记录（LOG）喂给规则与面板，
//   结束后按「进来前的状态」逐项恢复、归还实例、记存档、广播事件。
// 为什么新建（复用 → 扩展 → 新建）：PerformanceRules 只管阶段语义、PerformanceStage 只管时间轴，二者都不该持有
//   时停 / 输入图 / UI / 相机 / 存档这类会话级资源；DialogueService 是对白专用且 Performance 不得依赖 Dialogue，只能新建。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using MessagePipe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Game.Performance
{
    /// <summary>
    /// 演出服务。其他模块注入 <see cref="IPerformanceService"/> 直接 <c>await PlayAsync(id)</c>；场景里走 <see cref="PerformanceTrigger"/>。
    /// <para>同一时刻只允许一段演出；演出期间表现层一律用 unscaled 时间。</para>
    /// <para>
    /// 策略 HideHud 为 true 时隐藏 HUD 层与弹窗层（对白框在弹窗层），结束后两层各自恢复进来前的显隐（对白里插播时 Hud 层保持隐藏）；演出面板自身在 Panel 层不受影响。
    /// </para>
    /// <para>
    /// 取消语义：<c>ct</c> 取消时规则置 Cancelled、照常收尾并广播 Ended(Cancelled)，然后抛 <see cref="OperationCanceledException"/>
    /// （UniTask 约定，同 DialogueService）；Cancelled / Failed 不记「已播」。
    /// </para>
    /// <para>
    /// 世界舞台：舞台相机当 Base 相机、深度 = 主相机 + 1、
    /// 剔除遮罩 = 主相机遮罩 + Performance 层，清屏 / 背景 / 后处理 / Volume 遮罩 / 渲染器从主相机拷贝（预制体里不用手选渲染器），
    /// 透视参数与位姿保留预制体里作者的值；主相机保持 enabled（<c>Camera.main</c> 不能变空），只把剔除遮罩置 0 省一遍场景渲染，
    /// 收尾（含跳过 / 取消 / 异常）恢复。UI 根画布是 Screen Space Overlay（UIService 建），不受相机深度影响。
    /// </para>
    /// <para>
    /// 台词记录（LOG）：History 键 / 面板「LOG」按钮打开 Core 通用记录面板 <see cref="TranscriptView"/>（Top 层，压在演出面板之上），
    /// 内容是本段演出已显示过的字幕，格式同对白历史。开着时时间轴暂停（停顿中本来就停着）、字幕不打字、不处理确认 / 自动 / 跳过键；
    /// 再按 History、按 Esc（UI/Cancel）或点记录面板「关闭」关掉，规则仍是 Playing 才恢复时间轴。
    /// 「自动」：Auto 键 / 面板「自动」按钮切换，停顿处字幕打完再等 <see cref="PerformancePolicy.AutoAdvanceSeconds"/> 按确认处理
    /// （语义同对白，手动确认不关自动）。鼠标按住面板「跳过」等同长按跳过键。
    /// </para>
    /// </summary>
    public sealed class PerformanceService : IPerformanceService, IGameService
    {
        /// <summary>
        /// 演出期间启用的动作图。复用 GameInput 的 Dialogue 图（Advance = 确认继续、Skip = 长按跳过）而不新开 Performance 图：
        /// GameInput.inputactions 正被另一会话改动，且两图的键位语义一致；常量写在本模块而不引用 DialogueService.InputMap，
        /// 避免 Performance → Dialogue 的反向依赖（prp 2.5）。将来要分图只改这里。
        /// </summary>
        private const string InputMap = "Dialogue";

        private const string RootName = "PerformanceRoot";
        private const string KeyboardPathPrefix = "<Keyboard>";
        private const string FallbackSkipKey = "跳过";
        private const float FallbackCameraDepthOffset = 10f;
        private const float WorldCameraDepthOffset = 1f;
        private const string PerformanceLayerName = "Performance";

        /// <summary>
        /// 相机当前渲染器索引。URP 14 没有公开 getter，只能反射读私有序列化字段 <c>m_RendererIndex</c>——
        /// URP 14.0.12 私有字段，升级 URP 时核对；取不到时 <see cref="ReadRendererIndex"/> 返回 -1，舞台相机保留预制体里的渲染器。
        /// </summary>
        private static readonly FieldInfo RendererIndexField =
            typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly PerformanceConfig config;
        private readonly PerformanceRules rules;
        private readonly IAssetService assets;
        private readonly IUIService ui;
        private readonly IInputService input;
        private readonly IWorldPauseService worldPause;
        private readonly ISaveService save;
        private readonly IPublisher<PerformanceStartedEvent> startedPublisher;
        private readonly IPublisher<PerformanceEndedEvent> endedPublisher;
        private readonly ITelemetryScope telemetry;
        private readonly Func<Camera> mainCameraProvider;
        private Transform root;
        private bool running;
        // 代码请求（Confirm / Skip）只置标志，由播放循环在下一帧开头消费，与读动作走同一条处理函数，避免两处逻辑分叉。
        private bool pendingConfirm;
        private bool pendingSkip;
        // 本段演出已显示过的字幕（台词记录的内容）：每次播放开始清空，面板每显示一句追加一条。
        private readonly List<TranscriptLine> transcriptLines = new List<TranscriptLine>();

        public PerformanceService(PerformanceConfig config, PerformanceRules rules, IAssetService assets, IUIService ui,
            IInputService input, IWorldPauseService worldPause, ISaveService save,
            IPublisher<PerformanceStartedEvent> startedPublisher, IPublisher<PerformanceEndedEvent> endedPublisher,
            ITelemetryScope telemetry, Func<Camera> mainCameraProvider = null)
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.worldPause = worldPause ?? throw new ArgumentNullException(nameof(worldPause));
            this.save = save ?? throw new ArgumentNullException(nameof(save));
            this.startedPublisher = startedPublisher ?? throw new ArgumentNullException(nameof(startedPublisher));
            this.endedPublisher = endedPublisher ?? throw new ArgumentNullException(nameof(endedPublisher));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            // 主相机来源默认 Camera.main；测试注入自己的相机，免得被编辑器里打开的场景干扰。
            this.mainCameraProvider = mainCameraProvider ?? (() => Camera.main);
        }

        public bool IsRunning => running;

        public string CurrentId { get; private set; }

        public UniTask InitializeAsync(CancellationToken ct) => UniTask.CompletedTask;

        public bool HasPlayed(string id) => save.Get<PerformanceSaveData>().HasPlayed(id);

        /// <summary>
        /// 代码确认：只在停顿（Holding）时置确认请求，下一帧继续时间轴；其余时候无事。
        /// 与玩家点击 / 按确认键不同：玩家输入在字幕逐字显示中会先整句补全，本方法不补全、也不因打字而被吞掉。
        /// 台词记录（LOG）开着时请求被丢弃（开着期间一切推进都冻结）；代码 <see cref="Skip"/> 不受 LOG 影响。
        /// </summary>
        public void Confirm()
        {
            if (running && rules.Phase == PerformancePhase.Holding) pendingConfirm = true;
        }

        public void Skip()
        {
            if (running && rules.IsActive) pendingSkip = true;
        }

        public UniTask<PerformanceResult> PlayAsync(string id, CancellationToken ct = default)
        {
            return PlayAsync(id, PerformancePlacement.None, ct);
        }

        public async UniTask<PerformanceResult> PlayAsync(string id, PerformancePlacement placement, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(id))
            {
                telemetry.TrackWarn("play_rejected", TelemetryProps.Of(("id", string.Empty), ("reason", "empty_id")));
                throw new ArgumentException("演出 id 不能为空", nameof(id));
            }
            telemetry.Track("play_requested", ("id", id));
            if (running)
            {
                telemetry.TrackWarn("play_rejected", TelemetryProps.Of(("id", id), ("reason", "busy"), ("current", CurrentId)));
                throw new InvalidOperationException($"演出 {CurrentId} 正在进行，不能再拉起 {id}");
            }
            ct.ThrowIfCancellationRequested();

            // 重入保护从这里开始：加载是异步的，加载窗口内再调同样要被挡住。
            running = true;
            CurrentId = id;
            // 清掉上一段残留的请求；规则 Start 之后、开面板窗口内发来的 Skip 保留到播放循环第一帧再消费（加载期间规则未开始，Skip 无效）。
            pendingConfirm = false;
            pendingSkip = false;
            GameObject instance = null;
            PerformanceOutcome outcome = PerformanceOutcome.Failed;
            try
            {
                instance = await LoadAsync(id, ct);
                PerformanceStage stage = instance.GetComponent<PerformanceStage>();
                if (stage == null)
                {
                    telemetry.TrackError("stage_missing", $"演出预制体 {id} 根上没有 PerformanceStage", TelemetryProps.Of(("id", id)));
                    throw new InvalidOperationException($"演出预制体 {id} 根上没有 PerformanceStage");
                }
                // 摆到调用方给的世界位姿（触发区锚点 / 说话 NPC）；不指定就保持预制体自身位姿。
                if (placement.HasValue) stage.transform.SetPositionAndRotation(placement.Position, placement.Rotation);
                PerformancePolicy policy = stage.BuildPolicy(config);
                rules.Start(id, policy);
                outcome = await RunAsync(id, stage, policy, ct);
                if (outcome == PerformanceOutcome.Completed || outcome == PerformanceOutcome.Skipped)
                {
                    save.Get<PerformanceSaveData>().MarkPlayed(id);
                }
                return new PerformanceResult(id, outcome, rules.ElapsedSeconds);
            }
            catch (OperationCanceledException)
            {
                outcome = PerformanceOutcome.Cancelled;
                throw;
            }
            finally
            {
                // 实例归还放最外层：加载成功后无论哪条路径（缺舞台、播放异常、取消）都要还。
                if (instance != null) assets.ReleaseInstance(instance);
                running = false;
                CurrentId = null;
                endedPublisher.Publish(new PerformanceEndedEvent(id, outcome));
            }
        }

        private async UniTask<GameObject> LoadAsync(string id, CancellationToken ct)
        {
            GameObject instance;
            try
            {
                instance = await assets.InstantiateAsync(id, EnsureRoot(), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                telemetry.TrackError("load_failed", e, TelemetryProps.Of(("id", id)));
                throw;
            }
            if (instance == null)
            {
                telemetry.TrackError("load_failed", "实例化结果为空", TelemetryProps.Of(("id", id)));
                throw new InvalidOperationException($"演出 {id} 实例化失败（结果为空）");
            }
            return instance;
        }

        // rules 已 Start；本方法持有并在 finally 里按「进来前的状态」归还全部会话级资源。
        private async UniTask<PerformanceOutcome> RunAsync(string id, PerformanceStage stage, PerformancePolicy policy,
            CancellationToken ct)
        {
            // Actions 为 null（InputService 尚未初始化、或 EditMode）时没有输入图可管，记录 / 切换 / 恢复 / 读键一并跳过。
            GameInput actions = input.Actions; // lint-ok: 只取动作集引用以记录图状态与读演出键位，演出表现层不进确定性模拟
            bool hasInput = actions != null;
            bool gameplayWasEnabled = hasInput && actions.Gameplay.enabled;
            bool inputMapWasEnabled = hasInput && actions.Dialogue.enabled;

            IDisposable pause = null;
            PerformanceView view = null;
            bool hudHidden = false;
            bool hudWasVisible = true;
            bool popupWasVisible = true;
            var camera = new StageCameraState();
            bool holdPending = false;
            bool finishedPending = false;
            Action onHold = () => holdPending = true;
            Action onFinished = () => finishedPending = true;
            // 台词记录（LOG）：logOpen 在 await 打开之前就置上（面板加载 / 淡入期间也按「开着」冻结），transcript 是打开后的面板。
            bool logOpen = false;
            TranscriptView transcript = null;
            // 面板点击与 Advance 键共用 HandlePlayerAdvance：打字中 = 整句补全；否则走 Confirm()（只在 Holding 时置确认请求，
            // 循环里经 HandleConfirm 继续）；非打字、非停顿期间点击无事，不触发跳过；LOG 开着时不处理。view 打开后才赋值。
            Action onTap = null;
            // 面板「自动」「LOG」按钮与记录面板「关闭」只置标志，由播放循环在下一帧与对应按键走同一条处理。
            bool autoClicked = false;
            bool logClicked = false;
            bool logDismissed = false;
            Action onAuto = () => autoClicked = true;
            Action onHistory = () => logClicked = true;
            Action onLogDismiss = () => logDismissed = true;
            Action<string, string> onSubtitle = (speaker, text) => transcriptLines.Add(new TranscriptLine(speaker, text));
            bool subscribed = false;
            transcriptLines.Clear();
            try
            {
                if (hasInput)
                {
                    input.DisableMap(InputService.GameplayMap);
                    input.EnableMap(InputMap);
                }
                if (policy.PauseWorld) pause = worldPause.Acquire(this);
                if (policy.HideHud)
                {
                    // 先记进来前的整层开关、再开面板：读回值只反映 SetLayerVisible 的记账，放在开面板之前是为了
                    // 记录的一定是「演出介入之前」的状态。对白里插播时 Hud 层已被 DialogueService 藏掉，收尾不能把它亮出来。
                    hudWasVisible = ui.IsLayerVisible(UILayer.Hud);
                    popupWasVisible = ui.IsLayerVisible(UILayer.Popup);
                }

                var args = new PerformanceViewArgs(policy, BuildSkipHint(actions), config.FadeSeconds,
                    config.HoldPromptText, config.SubtitleCharactersPerSecond,
                    config.SubtitlePunctuationPauseSeconds, config.SubtitlePunctuationChars,
                    hasInput ? KeyboardHint(actions.Dialogue.Auto) : string.Empty,
                    hasInput ? KeyboardHint(actions.Dialogue.History) : string.Empty);
                view = await ui.OpenAsync<PerformanceView>(args, ct);
                PerformanceView openedView = view;
                onTap = () =>
                {
                    if (!logOpen) HandlePlayerAdvance(openedView);
                };
                view.OnTap += onTap;
                view.OnAuto += onAuto;
                view.OnHistory += onHistory;
                view.OnSubtitleShown += onSubtitle;
                if (policy.HideHud)
                {
                    // 隐藏 HUD 层与弹窗层：对白框（DialogueView）在弹窗层，Panel 层的演出面板盖不住它，对白里插播时要一起藏。
                    ui.SetLayerVisible(UILayer.Hud, false);
                    ui.SetLayerVisible(UILayer.Popup, false);
                    hudHidden = true;
                }
                AttachCamera(id, stage, ref camera);
                stage.SetSubtitleSink(view);
                stage.OnHold += onHold;
                stage.OnFinished += onFinished;
                subscribed = true;

                startedPublisher.Publish(new PerformanceStartedEvent(id));
                stage.Play();

                while (rules.Phase != PerformancePhase.Finished)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    float dt = Time.unscaledDeltaTime;
                    rules.Tick(dt);
                    // 字幕逐字与规则同一个 unscaled dt；停顿期间也推进，停下时正在打的字继续打完；LOG 开着时不打字。
                    if (!logOpen) view.TickTyping(dt);

                    if (finishedPending)
                    {
                        finishedPending = false;
                        rules.Complete();
                        break;
                    }
                    // 代码跳过（回放 / 试播 / 触屏）：与长按满走同一条收尾。
                    if (pendingSkip)
                    {
                        pendingSkip = false;
                        if (rules.Skip())
                        {
                            HandleSkipped(stage, view);
                            break;
                        }
                    }

                    // 台词记录（LOG）：History 键 / 「LOG」按钮切换；开着时 Esc（UI/Cancel）与记录面板的「关闭」也能关。
                    bool toggleLog = logClicked || (hasInput && actions.Dialogue.History.WasPressedThisFrame()); // lint-ok: 演出表现层读动作，不进确定性模拟
                    bool closeLog = logDismissed || (logOpen && hasInput && actions.UI.Cancel.WasPressedThisFrame()); // lint-ok: 演出表现层读动作，不进确定性模拟
                    logClicked = false;
                    logDismissed = false;
                    if (logOpen && (toggleLog || closeLog))
                    {
                        // 关：先退订、再 await 关面板，最后规则仍是 Playing 才恢复时间轴（停顿中保持停着等确认）。
                        TranscriptView closing = transcript;
                        transcript = null;
                        if (closing != null) closing.OnDismiss -= onLogDismiss;
                        await CloseViewAsync(closing, id, "transcript_close_failed");
                        logOpen = false;
                        if (rules.Phase == PerformancePhase.Playing) stage.Resume();
                    }
                    else if (!logOpen && toggleLog)
                    {
                        // 开：先置「开着」、先停时间轴，再 await 开面板——加载 / 淡入期间时间轴不走、按键不处理。
                        logOpen = true;
                        if (rules.Phase == PerformancePhase.Playing) stage.Pause();
                        transcript = await OpenTranscriptAsync(id);
                        if (transcript != null)
                        {
                            transcript.OnDismiss += onLogDismiss;
                            transcript.Show(transcriptLines, false);
                        }
                        else
                        {
                            logOpen = false;
                            if (rules.Phase == PerformancePhase.Playing) stage.Resume();
                        }
                    }
                    if (logOpen)
                    {
                        // LOG 开着：不确认、不自动、不处理跳过键（进度清零）。新到的停顿照常记下——时间轴可能在暂停前刚走到标记，
                        // 不记的话关 LOG 时会按 Playing 恢复时间轴、把这个停顿跳过去。
                        pendingConfirm = false;
                        autoClicked = false;
                        if (holdPending)
                        {
                            holdPending = false;
                            if (rules.EnterHold()) view.SetHoldPromptVisible(true);
                        }
                        if (policy.Skippable)
                        {
                            rules.TickSkip(false, dt);
                            view.SetSkipProgress(rules.SkipProgress);
                        }
                        continue;
                    }

                    // 先处理本帧的确认、再处理新到的停顿：停顿出现的同一帧按下的键不算确认，免得玩家看不到 ▼ 就被带过去。
                    // Advance 键与面板点击同一条：打字中只补全，不同时继续（一次按键只消费一次）。
                    if (hasInput && actions.Dialogue.Advance.WasPressedThisFrame()) // lint-ok: 演出表现层读动作，不进确定性模拟
                        HandlePlayerAdvance(view);
                    bool confirmRequested = pendingConfirm;
                    pendingConfirm = false;
                    if (confirmRequested && rules.Phase == PerformancePhase.Holding) HandleConfirm(stage, view);
                    if (holdPending)
                    {
                        holdPending = false;
                        if (rules.EnterHold()) view.SetHoldPromptVisible(true);
                    }
                    // 自动：Auto 键 / 「自动」按钮切换（手动确认不关自动）；开着时停顿处字幕打完再等 AutoAdvanceSeconds，按确认处理。
                    bool toggleAuto = autoClicked || (hasInput && actions.Dialogue.Auto.WasPressedThisFrame()); // lint-ok: 演出表现层读动作，不进确定性模拟
                    autoClicked = false;
                    if (toggleAuto && rules.ToggleAuto()) view.SetAuto(rules.AutoPlay);
                    if (rules.TickAuto(dt, view.IsTyping)) HandleConfirm(stage, view);
                    if (policy.Skippable)
                    {
                        // 按住跳过键，或鼠标按住面板「跳过」，都算按着。
                        bool held = (hasInput && actions.Dialogue.Skip.IsPressed()) || view.SkipPointerHeld; // lint-ok: 演出表现层读动作，不进确定性模拟
                        bool fired = rules.TickSkip(held, dt);
                        view.SetSkipProgress(rules.SkipProgress);
                        if (fired) HandleSkipped(stage, view);
                    }
                }
                return rules.Outcome ?? PerformanceOutcome.Completed;
            }
            catch (OperationCanceledException)
            {
                rules.Cancel();
                throw;
            }
            catch (Exception e)
            {
                rules.Fail();
                telemetry.TrackError("play_failed", e, TelemetryProps.Of(("id", id)));
                throw;
            }
            finally
            {
                // 正常结束、取消、异常都走这里；顺序：先摘回调再停时间轴（Stop 会触发 stopped），再还相机、关台词记录与面板、恢复 HUD / 输入 / 时停。
                if (subscribed)
                {
                    stage.OnHold -= onHold;
                    stage.OnFinished -= onFinished;
                }
                // stage 是 UnityEngine.Object，判空只用 == null。
                if (stage != null)
                {
                    stage.SetSubtitleSink(null);
                    stage.Stop();
                }
                DetachCamera(ref camera);
                // LOG 还开着（播完 / 跳过 / 取消 / 异常）一并关掉：它在 Top 层，不关会压在回到探索的画面上。
                if (transcript != null)
                {
                    transcript.OnDismiss -= onLogDismiss;
                    await CloseViewAsync(transcript, id, "transcript_close_failed");
                    transcript = null;
                }
                logOpen = false;
                if (view != null)
                {
                    if (onTap != null) view.OnTap -= onTap;
                    view.OnAuto -= onAuto;
                    view.OnHistory -= onHistory;
                    view.OnSubtitleShown -= onSubtitle;
                    await CloseViewAsync(view, id, "view_close_failed");
                }
                pendingConfirm = false;
                pendingSkip = false;
                if (hudHidden)
                {
                    // 按进来前的显隐恢复：对白里插播时 Hud 层本来就藏着（DialogueService 藏的），原样保留。
                    ui.SetLayerVisible(UILayer.Hud, hudWasVisible);
                    ui.SetLayerVisible(UILayer.Popup, popupWasVisible);
                }
                if (hasInput)
                {
                    // 只恢复进来前的状态：对白里插播时 Dialogue 图本来就开着、Gameplay 本来就关着，都原样保留。
                    if (!inputMapWasEnabled) input.DisableMap(InputMap);
                    if (gameplayWasEnabled) input.EnableMap(InputService.GameplayMap);
                }
                pause?.Dispose();
            }
        }

        // 玩家输入（面板点击 / Advance 键）：字幕还在逐字显示 → 整句补全，本次输入就此消费；否则按确认处理（仅停顿时生效）。
        private void HandlePlayerAdvance(PerformanceView view)
        {
            if (view.IsTyping)
            {
                view.CompleteTyping();
                return;
            }
            Confirm();
        }

        // 玩家按确认与代码 Confirm 共用：规则回到 Playing 才继续时间轴、收起 ▼。
        private void HandleConfirm(PerformanceStage stage, PerformanceView view)
        {
            if (!rules.Confirm()) return;
            view.SetHoldPromptVisible(false);
            stage.Resume();
        }

        // 长按满与代码 Skip 共用：规则已置 Skipped + Finished，这里停时间轴、清进度环。
        private static void HandleSkipped(PerformanceStage stage, PerformanceView view)
        {
            view.SetSkipProgress(0f);
            stage.Stop();
        }

        // 关面板（演出面板 / 台词记录）：失败只记错误与埋点（failedEvent），不打断收尾。
        private async UniTask CloseViewAsync(UIView view, string id, string failedEvent)
        {
            // UIView 是 UnityEngine.Object，判空只用 == null。
            if (view == null) return;
            try
            {
                // 不跟随调用方的 ct：取消后面板也必须关掉。
                await ui.CloseAsync(view, CancellationToken.None);
            }
            catch (Exception e)
            {
                telemetry.TrackError(failedEvent, e, TelemetryProps.Of(("id", id)));
                Log.Error($"PerformanceService：关闭 {view.GetType().Name} 失败（{id}）：{e.Message}");
            }
        }

        // 开台词记录（Top 层，压在演出面板之上）。不跟随调用方的 ct：打开途中被取消会留下没人关的面板；打开只是一次淡入，
        // 开完后下一帧的 Yield 自然抛取消，由 finally 关掉。打开失败只记错误与埋点并返回 null，演出照常继续。
        private async UniTask<TranscriptView> OpenTranscriptAsync(string id)
        {
            try
            {
                return await ui.OpenAsync<TranscriptView>(ct: CancellationToken.None);
            }
            catch (Exception e)
            {
                telemetry.TrackError("transcript_open_failed", e, TelemetryProps.Of(("id", id)));
                Log.Error($"PerformanceService：打开台词记录失败（{id}）：{e.Message}");
                return null;
            }
        }

        private Transform EnsureRoot()
        {
            // 懒建：第一次播放时才建；DontDestroyOnLoad 让演出跨场景加载不被卸掉。Transform 判空只用 == null。
            if (root != null) return root;
            var go = new GameObject(RootName);
            // DontDestroyOnLoad 只能在 Play 模式调用（EditMode 下抛 InvalidOperationException，已实测）；编辑器测试 / 工具里跳过。
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            root = go.transform;
            return root;
        }

        private string BuildSkipHint(GameInput actions)
        {
            string key = actions == null ? string.Empty : KeyboardHint(actions.Dialogue.Skip);
            if (string.IsNullOrEmpty(key)) key = FallbackSkipKey;
            string format = config.SkipHintFormat;
            if (string.IsNullOrEmpty(format)) return key;
            try
            {
                return string.Format(format, key);
            }
            catch (FormatException)
            {
                Log.Warn($"PerformanceConfig.SkipHintFormat 格式串非法：「{format}」，已直接显示键位。");
                return key;
            }
        }

        // 取动作的第一条键盘绑定的显示文字（同 DialogueKeyboardInput.KeyboardHint；不引用它以免 Performance → Dialogue）。
        private static string KeyboardHint(InputAction action)
        {
            if (action == null) return string.Empty;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite) continue;
                string path = binding.effectivePath;
                if (path == null || !path.StartsWith(KeyboardPathPrefix, StringComparison.Ordinal)) continue;
                return action.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions);
            }
            return string.Empty;
        }

        /// <summary>舞台相机接管画面时改动的记录，收尾时据此原样改回。私有可变记录，只在本类的 Attach / Detach 之间传递，不是序列化暴露面。</summary>
        private struct StageCameraState
        {
            internal bool Attached;
            internal Camera Stage;
            internal UniversalAdditionalCameraData StageData;
            internal CameraRenderType StageRenderType;
            internal bool RendererChanged;
            internal int StageRendererIndex;
            internal float StageDepth;
            internal CameraClearFlags StageClearFlags;
            internal Color StageBackground;
            internal int StageCullingMask;
            internal LayerMask StageVolumeMask;
            internal bool StagePostProcessing;
            internal float[] StageLayerCullDistances;
            internal bool StageLayerCullSpherical;
            internal Camera Main;
            internal int MainCullingMask;
        }

        // 世界舞台：舞台相机当 Base 相机接管画面（见类注释）。主相机缺失 / 就是舞台相机时退路：舞台相机按作者设的参数独立渲染
        // （遮罩补上 Performance 层、深度抬高），记 Warn + 埋 world_camera_fallback。
        private void AttachCamera(string id, PerformanceStage stage, ref StageCameraState state)
        {
            Camera stageCamera = stage.StageCamera;
            if (stageCamera == null)
            {
                Log.Warn($"PerformanceService：演出 {id} 的舞台没接 stageCamera，舞台内容将不可见。", stage);
                return;
            }
            UniversalAdditionalCameraData stageData = stageCamera.GetUniversalAdditionalCameraData();
            state.Attached = true;
            state.Stage = stageCamera;
            state.StageData = stageData;
            state.StageRenderType = stageData.renderType;
            state.StageDepth = stageCamera.depth;
            state.StageClearFlags = stageCamera.clearFlags;
            state.StageBackground = stageCamera.backgroundColor;
            state.StageCullingMask = stageCamera.cullingMask;
            state.StageVolumeMask = stageData.volumeLayerMask;
            state.StagePostProcessing = stageData.renderPostProcessing;
            state.StageLayerCullDistances = stageCamera.layerCullDistances;
            state.StageLayerCullSpherical = stageCamera.layerCullSpherical;
            int performanceLayer = LayerMask.NameToLayer(PerformanceLayerName);
            int performanceBit = performanceLayer >= 0 ? 1 << performanceLayer : 0;
            stageData.renderType = CameraRenderType.Base;

            Camera main = mainCameraProvider();
            if (main == null || main == stageCamera)
            {
                string reason = main == null ? "no_main_camera" : "stage_is_main";
                stageCamera.depth = (main == null ? 0f : main.depth) + FallbackCameraDepthOffset;
                stageCamera.cullingMask |= performanceBit;
                Log.Warn($"PerformanceService：演出 {id} 找不到主相机（{reason}），舞台相机按预制体参数独立渲染。", stage);
                telemetry.TrackWarn("world_camera_fallback", TelemetryProps.Of(("id", id), ("reason", reason)));
                return;
            }

            stageCamera.depth = main.depth + WorldCameraDepthOffset;
            stageCamera.cullingMask = main.cullingMask | performanceBit;
            stageCamera.layerCullDistances = main.layerCullDistances;
            stageCamera.layerCullSpherical = main.layerCullSpherical;
            stageCamera.clearFlags = main.clearFlags;
            stageCamera.backgroundColor = main.backgroundColor;
            // 不用 GetUniversalAdditionalCameraData 取主相机：那个扩展在缺组件时会 AddComponent，改到主相机上。
            if (main.TryGetComponent(out UniversalAdditionalCameraData mainData))
            {
                stageData.volumeLayerMask = mainData.volumeLayerMask;
                stageData.renderPostProcessing = mainData.renderPostProcessing;
                int mainRendererIndex = ReadRendererIndex(mainData);
                if (mainRendererIndex >= 0)
                {
                    state.StageRendererIndex = ReadRendererIndex(stageData);
                    state.RendererChanged = true;
                    stageData.SetRenderer(mainRendererIndex);
                }
            }

            // 主相机保持 enabled（别的系统每帧读 Camera.main），只清空遮罩省一遍场景渲染；收尾恢复。
            state.Main = main;
            state.MainCullingMask = main.cullingMask;
            main.cullingMask = 0;
            telemetry.Track("world_stage_attached", ("id", id));
        }

        private static void DetachCamera(ref StageCameraState state)
        {
            // 先把主相机遮罩还回去：舞台相机随实例销毁后画面立刻由主相机接手。
            if (state.Main != null) state.Main.cullingMask = state.MainCullingMask;
            // 舞台相机随实例归还一起销毁，这里仍改回原值：实例若被复用 / 归还失败，也不留下被改过的相机。
            if (state.Attached && state.Stage != null)
            {
                state.Stage.depth = state.StageDepth;
                state.Stage.clearFlags = state.StageClearFlags;
                state.Stage.backgroundColor = state.StageBackground;
                state.Stage.cullingMask = state.StageCullingMask;
                state.Stage.layerCullDistances = state.StageLayerCullDistances;
                state.Stage.layerCullSpherical = state.StageLayerCullSpherical;
                if (state.StageData != null)
                {
                    state.StageData.renderType = state.StageRenderType;
                    if (state.RendererChanged) state.StageData.SetRenderer(state.StageRendererIndex);
                    state.StageData.volumeLayerMask = state.StageVolumeMask;
                    state.StageData.renderPostProcessing = state.StagePostProcessing;
                }
            }
            state = default;
        }

        /// <summary>反射读相机当前渲染器索引；字段取不到（URP 升级改名）返回 -1，调用方不改舞台相机的渲染器。</summary>
        private static int ReadRendererIndex(UniversalAdditionalCameraData data)
        {
            if (RendererIndexField == null || data == null) return -1;
            return RendererIndexField.GetValue(data) is int index ? index : -1;
        }
    }
}
