// 职责：演出管线的对外入口——按 id 实例化演出预制体、持世界时停令牌、切输入图、开演出面板、藏 HUD、
//   把舞台相机叠到主相机上，每帧把确认 / 长按跳过喂给规则，结束后按「进来前的状态」逐项恢复、归还实例、记存档、广播事件。
// 为什么新建（复用 → 扩展 → 新建）：PerformanceRules 只管阶段语义、PerformanceStage 只管时间轴，二者都不该持有
//   时停 / 输入图 / UI / 相机 / 存档这类会话级资源；DialogueService 是对白专用且 Performance 不得依赖 Dialogue，只能新建。
using System;
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
    /// 舞台相机的渲染器在运行时对齐主相机（URP 只允许同类渲染器叠加），演出预制体里不用手选渲染器。
    /// </para>
    /// <para>
    /// 世界模式（<see cref="PerformanceStageMode.World"/>）不叠相机栈：舞台相机当 Base 相机、深度 = 主相机 + 1、
    /// 剔除遮罩 = 主相机遮罩 + Performance 层，清屏 / 背景 / 后处理 / Volume 遮罩 / 渲染器从主相机拷贝，透视参数与位姿保留预制体里作者的值；
    /// 主相机保持 enabled（<c>Camera.main</c> 不能变空），只把剔除遮罩置 0 省一遍场景渲染，收尾（含跳过 / 取消 / 异常）恢复。
    /// UI 根画布是 Screen Space Overlay（UIService 建），不受相机深度影响。
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
        /// URP 14.0.12 私有字段，升级 URP 时核对；取不到时 <see cref="ReadRendererIndex"/> 返回 -1，改走渲染器类型比较。
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
                ApplyPlacement(id, stage, placement);
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

        // 世界模式：把实例摆到调用方给的世界位姿；叠加模式的舞台与世界位置无关，摆放值忽略并告警（调用方多半接错了舞台）。
        private void ApplyPlacement(string id, PerformanceStage stage, PerformancePlacement placement)
        {
            if (!placement.HasValue) return;
            if (stage.Mode == PerformanceStageMode.World)
            {
                stage.transform.SetPositionAndRotation(placement.Position, placement.Rotation);
                return;
            }
            Log.Warn($"PerformanceService：演出 {id} 是叠加模式，忽略传入的摆放位姿。", stage);
            telemetry.TrackWarn("placement_ignored", TelemetryProps.Of(("id", id), ("reason", "placement_ignored_overlay")));
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
            var camera = new CameraStackState();
            bool holdPending = false;
            bool finishedPending = false;
            Action onHold = () => holdPending = true;
            Action onFinished = () => finishedPending = true;
            // 面板点击与 Advance 键共用 HandlePlayerAdvance：打字中 = 整句补全；否则走 Confirm()（只在 Holding 时置确认请求，
            // 循环里经 HandleConfirm 继续）；非打字、非停顿期间点击无事，不触发跳过。view 打开后才赋值。
            Action onTap = null;
            bool subscribed = false;
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

                var args = new PerformanceViewArgs(policy, BuildSkipHint(actions), config.LetterboxHeight,
                    config.FadeSeconds, config.HoldPromptText, config.SubtitleCharactersPerSecond,
                    config.SubtitlePunctuationPauseSeconds, config.SubtitlePunctuationChars);
                view = await ui.OpenAsync<PerformanceView>(args, ct);
                PerformanceView openedView = view;
                onTap = () => HandlePlayerAdvance(openedView);
                view.OnTap += onTap;
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
                    // 字幕逐字与规则同一个 unscaled dt；停顿期间也推进，停下时正在打的字继续打完。
                    view.TickTyping(dt);

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
                    if (policy.Skippable)
                    {
                        bool held = hasInput && actions.Dialogue.Skip.IsPressed(); // lint-ok: 演出表现层读动作，不进确定性模拟
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
                // 正常结束、取消、异常都走这里；顺序：先摘回调再停时间轴（Stop 会触发 stopped），再拆相机、关面板、恢复 HUD / 输入 / 时停。
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
                if (view != null)
                {
                    if (onTap != null) view.OnTap -= onTap;
                    await CloseViewAsync(view, id);
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

        private async UniTask CloseViewAsync(PerformanceView view, string id)
        {
            try
            {
                // 收尾不跟随调用方的 ct：取消后面板也必须关掉。
                await ui.CloseAsync(view, CancellationToken.None);
            }
            catch (Exception e)
            {
                telemetry.TrackError("view_close_failed", e, TelemetryProps.Of(("id", id)));
                Log.Error($"PerformanceService：关闭演出面板失败（{id}）：{e.Message}");
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

        /// <summary>相机叠加改动的记录，收尾时据此原样改回。私有可变记录，只在本类的 Attach / Detach 之间传递，不是序列化暴露面。</summary>
        private struct CameraStackState
        {
            internal Camera Stage;
            internal UniversalAdditionalCameraData StageData;
            internal CameraRenderType StageRenderType;
            internal bool RendererChanged;
            internal int StageRendererIndex;
            internal UniversalAdditionalCameraData MainData;
            internal bool Stacked;
            internal bool Fallback;
            internal float StageDepth;
            internal CameraClearFlags StageClearFlags;
            internal Color StageBackground;
            // 世界模式专用：舞台相机被改的其余字段与主相机的剔除遮罩，收尾原样改回。
            internal bool World;
            internal int StageCullingMask;
            internal LayerMask StageVolumeMask;
            internal bool StagePostProcessing;
            internal float[] StageLayerCullDistances;
            internal bool StageLayerCullSpherical;
            internal Camera Main;
            internal int MainCullingMask;
        }

        // 舞台相机（Overlay）叠进 Camera.main 的 URP 相机栈；叠加前先把舞台相机的渲染器对齐主相机（URP 规定渲染器类型不同的
        // 相机不能叠加，否则每帧告警并整段跳过渲染），预制体里不用手选渲染器。主相机缺 URP 数据、本身不是 Base、渲染器不支持叠加、
        // 或对齐后渲染器类型仍不一致时退路：舞台相机改 Base、深度高于主相机、纯黑底，记 Warn + 埋 camera_stack_unavailable（prp 2.3）。
        private void AttachCamera(string id, PerformanceStage stage, ref CameraStackState state)
        {
            Camera stageCamera = stage.StageCamera;
            if (stageCamera == null)
            {
                Log.Warn($"PerformanceService：演出 {id} 的舞台没接 stageCamera，舞台内容将不可见。", stage);
                return;
            }
            UniversalAdditionalCameraData stageData = stageCamera.GetUniversalAdditionalCameraData();
            state.Stage = stageCamera;
            state.StageData = stageData;
            state.StageRenderType = stageData.renderType;
            state.StageDepth = stageCamera.depth;
            state.StageClearFlags = stageCamera.clearFlags;
            state.StageBackground = stageCamera.backgroundColor;

            if (stage.Mode == PerformanceStageMode.World)
            {
                AttachWorldCamera(id, stage, stageCamera, stageData, ref state);
                return;
            }

            Camera main = mainCameraProvider();
            // 不用 GetUniversalAdditionalCameraData 取主相机：那个扩展在缺组件时会 AddComponent，「没有 URP 数据」就判不出来了。
            UniversalAdditionalCameraData mainData = null;
            bool canStack = main != null && main != stageCamera
                && main.TryGetComponent(out mainData)
                && mainData.renderType == CameraRenderType.Base;
            bool rendererMismatch = false;
            if (canStack)
            {
                // 对齐渲染器：读得到主相机索引就让舞台相机用同一个；读不到（反射失败 / 索引 < 0 即「用默认」）不改，只靠下面的类型比较兜底。
                int mainRendererIndex = ReadRendererIndex(mainData);
                if (mainRendererIndex >= 0)
                {
                    state.StageRendererIndex = ReadRendererIndex(stageData);
                    state.RendererChanged = true;
                    stageData.SetRenderer(mainRendererIndex);
                }
                rendererMismatch = !SameRendererType(mainData, stageData);
            }
            if (canStack && !rendererMismatch)
            {
                stageData.renderType = CameraRenderType.Overlay;
                // cameraStack 在渲染器不支持叠加时返回 null（URP 自己会记一条 Warning）。
                var stack = mainData.cameraStack;
                if (stack != null)
                {
                    if (!stack.Contains(stageCamera)) stack.Add(stageCamera);
                    state.MainData = mainData;
                    state.Stacked = true;
                    // 叠加后再核一次：类型仍不一致时 URP 会每帧跳过整段渲染，撤出相机栈走退路。
                    if (SameRendererType(mainData, stageData)) return;
                    stack.Remove(stageCamera);
                    state.MainData = null;
                    state.Stacked = false;
                    rendererMismatch = true;
                }
            }

            stageData.renderType = CameraRenderType.Base;
            stageCamera.depth = (main == null ? 0f : main.depth) + FallbackCameraDepthOffset;
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = Color.black;
            state.Fallback = true;
            string reason = main == null ? "no_main_camera"
                : main == stageCamera ? "stage_is_main"
                : mainData == null ? "no_urp_data"
                : mainData.renderType != CameraRenderType.Base ? "main_not_base"
                : rendererMismatch ? "renderer_mismatch"
                : "stack_unsupported";
            Log.Warn($"PerformanceService：演出 {id} 无法叠加到主相机（{reason}），舞台相机改为 Base 独立渲染。", stage);
            telemetry.TrackWarn("camera_stack_unavailable", TelemetryProps.Of(("id", id), ("reason", reason)));
        }

        // 世界模式：舞台相机当 Base 相机接管画面（见类注释）。主相机缺失 / 就是舞台相机时退路：舞台相机按作者设的参数独立渲染
        // （遮罩补上 Performance 层、深度抬高），记 Warn + 埋 world_camera_fallback。
        private void AttachWorldCamera(string id, PerformanceStage stage, Camera stageCamera,
            UniversalAdditionalCameraData stageData, ref CameraStackState state)
        {
            state.World = true;
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
                Log.Warn($"PerformanceService：世界模式演出 {id} 找不到主相机（{reason}），舞台相机按预制体参数独立渲染。", stage);
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

        private static void DetachCamera(ref CameraStackState state)
        {
            // 世界模式先把主相机遮罩还回去：舞台相机随实例销毁后画面立刻由主相机接手。
            if (state.World && state.Main != null) state.Main.cullingMask = state.MainCullingMask;
            if (state.Stacked && state.MainData != null && state.Stage != null)
            {
                var stack = state.MainData.cameraStack;
                if (stack != null) stack.Remove(state.Stage);
            }
            // 舞台相机随实例归还一起销毁，这里仍改回原值：实例若被复用 / 归还失败，也不留下被改过的相机。
            if (state.Stage != null)
            {
                if (state.StageData != null)
                {
                    state.StageData.renderType = state.StageRenderType;
                    if (state.RendererChanged) state.StageData.SetRenderer(state.StageRendererIndex);
                }
                if (state.Fallback || state.World)
                {
                    state.Stage.depth = state.StageDepth;
                    state.Stage.clearFlags = state.StageClearFlags;
                    state.Stage.backgroundColor = state.StageBackground;
                }
                if (state.World)
                {
                    state.Stage.cullingMask = state.StageCullingMask;
                    state.Stage.layerCullDistances = state.StageLayerCullDistances;
                    state.Stage.layerCullSpherical = state.StageLayerCullSpherical;
                    if (state.StageData != null)
                    {
                        state.StageData.volumeLayerMask = state.StageVolumeMask;
                        state.StageData.renderPostProcessing = state.StagePostProcessing;
                    }
                }
            }
            state = default;
        }

        /// <summary>反射读相机当前渲染器索引；字段取不到（URP 升级改名）返回 -1，调用方退回类型比较。</summary>
        private static int ReadRendererIndex(UniversalAdditionalCameraData data)
        {
            if (RendererIndexField == null || data == null) return -1;
            return RendererIndexField.GetValue(data) is int index ? index : -1;
        }

        /// <summary>两台相机实际生效的渲染器是否同一类型（Renderer2D / UniversalRenderer）；任一取不到按不一致处理。</summary>
        private static bool SameRendererType(UniversalAdditionalCameraData main, UniversalAdditionalCameraData stage)
        {
            ScriptableRenderer mainRenderer = main.scriptableRenderer;
            ScriptableRenderer stageRenderer = stage.scriptableRenderer;
            return mainRenderer != null && stageRenderer != null && mainRenderer.GetType() == stageRenderer.GetType();
        }
    }
}
