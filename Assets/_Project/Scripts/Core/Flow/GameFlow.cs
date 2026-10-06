// 职责：IGameFlow 的唯一实现——串行切换 + 请求排队 + 发布 GameStateChangingEvent（Exit 前）/ GameStateChangedEvent（Enter 后）
//   + 切换涉及场景时落 / 揭加载黑幕（ILoadingCurtain，roadmap E4）。
// 为什么新建：没有现成实现；也不能把排队逻辑塞进 GameState（状态不该知道有没有别的状态在排队）。
//   加载黑幕是扩展：它要跨两个状态、跨多次排队切换保持黑屏，只有这里同时知道「前后两侧是不是场景」和「队列空没空」。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Logging;
using Game.Core.Telemetry;
using MessagePipe;
using VContainer;

namespace Game.Core.Flow
{
    /// <summary>
    /// 状态流实现。
    /// 排队而不是打断：切换过程中来的请求进队列，等当前这次切完再按顺序处理——
    /// 打断会让某个状态的 Exit 半途而废，留下开了一半的 UI 和没释放的资源。
    /// </summary>
    public sealed class GameFlow : IGameFlow
    {
        private readonly IObjectResolver resolver;
        private readonly IPublisher<GameStateChangedEvent> stateChangedPublisher;
        private readonly IPublisher<GameStateChangingEvent> stateChangingPublisher;
        private readonly Queue<TransitionRequest> queue = new Queue<TransitionRequest>();

        /// <summary>埋点服务本体。只用来问 <see cref="ITelemetryService.SessionStarted"/>，埋点本身走 <see cref="telemetry"/>。</summary>
        private readonly ITelemetryService telemetryService;

        private readonly ITelemetryScope telemetry;
        private readonly ITelemetryClock clock;

        /// <summary>加载黑幕；为 null 时不落幕（EditMode 测试直接 new 的 GameFlow）。</summary>
        private readonly ILoadingCurtain curtain;

        private bool processing;

        /// <summary>这一轮（队列从有请求到清空）落过幕没有；落过就在队列清空时揭幕。</summary>
        private bool curtainRaised;

        /// <summary>这一轮第一次落幕的时刻与来源状态，揭幕时埋一条黑屏总时长。</summary>
        private long curtainStartMs;

        private string curtainFrom;

        /// <summary>
        /// 埋点两个参数与 <paramref name="curtain"/> 允许为 null（EditMode 测试里直接 new 出来的 GameFlow 没有容器）：
        /// 拿不到埋点就整条埋点链路变空操作；没有黑幕就不落幕，其余业务路径一个字节都不变。
        /// </summary>
        public GameFlow(
            IObjectResolver resolver,
            IPublisher<GameStateChangedEvent> stateChangedPublisher,
            IPublisher<GameStateChangingEvent> stateChangingPublisher,
            ITelemetryService telemetryService,
            ITelemetryClock clock,
            ILoadingCurtain curtain)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.stateChangedPublisher = stateChangedPublisher
                ?? throw new ArgumentNullException(nameof(stateChangedPublisher));
            this.stateChangingPublisher = stateChangingPublisher
                ?? throw new ArgumentNullException(nameof(stateChangingPublisher));
            this.telemetryService = telemetryService;
            this.clock = clock;
            this.curtain = curtain;
            telemetry = telemetryService == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetryService.Scope(TelemetryKeys.Flow);
        }

        public GameState Current { get; private set; }

        public UniTask GoToAsync<TState>(CancellationToken ct = default) where TState : GameState
        {
            var request = new TransitionRequest(typeof(TState), ct);
            queue.Enqueue(request);
            if (!processing)
            {
                ProcessQueueAsync().Forget();
            }

            return request.Completion.Task;
        }

        private async UniTaskVoid ProcessQueueAsync()
        {
            processing = true;
            try
            {
                while (queue.Count > 0)
                {
                    TransitionRequest request = queue.Dequeue();
                    Exception failure = null;
                    bool canceled = false;
                    try
                    {
                        await TransitionAsync(request.StateType, request.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        canceled = true;
                    }
                    catch (Exception e)
                    {
                        failure = e;
                        Log.Error($"切换到 {request.StateType.Name} 失败：{e}");

                        // Current 的语义是「最近一个成功进入的状态」，切换失败时它正好就是这次转移的来源。
                        TrackTransition(
                            TelemetryKeys.FlowEvents.StateFailed,
                            Current == null ? string.Empty : Current.GetType().Name,
                            request.StateType.Name,
                            e);
                    }

                    // 队列里还有后续请求就保持黑屏，连续切换全程不露出中间态；队列清空才揭幕。
                    // 成功、失败、取消都走到这里，不会留永久黑屏。揭完幕才让这一次的 GoToAsync 完成。
                    if (queue.Count == 0)
                    {
                        await RevealCurtainAsync(request.StateType.Name);
                    }

                    if (canceled)
                    {
                        request.Completion.TrySetCanceled(request.Token);
                    }
                    else if (failure != null)
                    {
                        request.Completion.TrySetException(failure);
                    }
                    else
                    {
                        request.Completion.TrySetResult();
                    }
                }
            }
            finally
            {
                processing = false;
            }
        }

        private async UniTask TransitionAsync(Type stateType, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Type from = Current == null ? null : Current.GetType();
            var next = (GameState)resolver.Resolve(stateType);

            // 契约（docs/telemetry.md 2.1）：from 恒指转移的**来源**状态、to 恒指**目标**状态，
            // 不随事件名换意思——state_exit 是「从 from 离开、要去 to」，state_enter 是「从 from 来、进了 to」。
            // 首次进入没有来源，那一侧留空字符串。
            string fromName = from == null ? string.Empty : from.Name;
            string toName = stateType.Name;

            // 前一状态 Exit 之前先发「即将切换」：订阅者在回调里同步抓现场（如存档捕获），回调返回后才开始 Exit，
            // 那时前一状态的场景与对象都还在。首次进入没有前一状态也照发（From 为 null），订阅者自己按 From 过滤。
            // 发在 Resolve 之后：目标状态解析不出来时整次切换直接失败，不该先喊一声「要切了」。
            stateChangingPublisher.Publish(new GameStateChangingEvent(from, stateType));

            // 涉及场景（前一状态带场景，或目标状态带场景）时先落幕，盖严了才 Exit：场景拆掉与空屏都在黑幕后面。
            // 落在 Changing 之后：订阅者同步抓现场时画面还没变。两侧都不带场景（Boot→Title、首次进标题）不碰黑幕。
            if (Current is SceneGameState || typeof(SceneGameState).IsAssignableFrom(stateType))
            {
                await CoverCurtainAsync(fromName, ct);
            }

            if (Current != null)
            {
                long exitStartMs = NowMs;
                await Current.ExitAsync(ct);

                // 埋在 await 之后：Exit 抛异常时这条不写，那次失败由队列处理里的 state_failed 负责。
                TrackTransition(TelemetryKeys.FlowEvents.StateExit, fromName, toName, NowMs - exitStartMs);
            }

            long enterStartMs = NowMs;

            // Enter 成功之后才换 Current。提前赋值的话，Enter 抛异常时 Current 会指着一个**从没进入过**的
            // 状态，下一次切换会去 Exit 它，Exit 里那些「Enter 时申请的资源」全是空的。
            // 代价是：切换失败后处于「前一状态已 Exit、目标未 Enter」的空档，此时 Current 仍指向前一状态——
            // 它的语义是「最近一个成功进入的状态」，不是「场上活着的状态」。异常照旧回传给 GoToAsync 的
            // 调用方，队列继续处理后面的请求；恢复手段是再 GoToAsync 到一个能进得去的状态。
            await next.EnterAsync(ct);
            Current = next;
            TrackTransition(TelemetryKeys.FlowEvents.StateEnter, fromName, toName, NowMs - enterStartMs);

            // 切换完成后发布一次，带上切换前后的状态类型；订阅者拿到时 Current 已是新状态。
            // 失败路径不发这个事件——没进去的状态不算「切换完成」。
            stateChangedPublisher.Publish(new GameStateChangedEvent(from, stateType));
        }

        /// <summary>
        /// 落幕。这一轮第一次落幕时记下起点（揭幕时埋黑屏总时长）。已盖着时黑幕自己直接返回。
        /// 本次请求被取消就照取消往外抛（交给队列收尾、队列清空时照常揭幕）；黑幕自己出错不拖垮切换，记 Warn 照常切。
        /// </summary>
        private async UniTask CoverCurtainAsync(string fromName, CancellationToken ct)
        {
            if (curtain == null)
            {
                return;
            }

            // 先记账再调：落幕半途抛异常也要在队列清空时调一次揭幕，把半透明的黑幕收掉。
            if (!curtainRaised)
            {
                curtainRaised = true;
                curtainStartMs = NowMs;
                curtainFrom = fromName;
            }

            try
            {
                await curtain.CoverAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Warn("加载黑幕落幕失败，本次切换不遮挡、照常进行"
                         + $"（检查 Prefabs/UI/LoadingView.prefab 与 Addressables 地址 LoadingView）：{e}");
            }
        }

        /// <summary>
        /// 揭幕（这一轮落过幕才揭）。不带请求的取消令牌：调用方取消了也必须揭开。
        /// 黑幕自己出错只记 Warn——它的实现保证出错时已硬收掉，这里不再往外抛，免得拖垮队列。
        /// </summary>
        private async UniTask RevealCurtainAsync(string toName)
        {
            if (!curtainRaised)
            {
                return;
            }

            curtainRaised = false;

            // 落幕时面板没开出来的那一轮屏上从没黑过，不埋黑屏时长。
            bool wasCovered = curtain.IsCovered;
            try
            {
                await curtain.RevealAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                Log.Warn($"加载黑幕揭幕失败（已硬收掉）：{e}");
            }

            if (wasCovered)
            {
                // from 是这一轮第一次切换的来源，to 是最后一次切换的目标；ms 是从开始落幕到揭幕完成的黑屏总时长。
                TrackTransition(TelemetryKeys.FlowEvents.CurtainRevealed, curtainFrom, toName, NowMs - curtainStartMs);
            }

            curtainFrom = null;
        }

        /// <summary>埋点层自己的时钟。拿不到时恒为 0（ms 记成 0），不影响任何业务路径。</summary>
        private long NowMs => clock == null ? 0L : clock.MillisecondsNow;

        /// <summary>
        /// 现在能不能埋。**会话头写出来之前一律不埋**：GameBootstrap 的第一次切换（进 BootState）
        /// 发生在 TelemetryService.InitializeAsync 之前，那时候写出去的事件会被分析脚本按
        /// session_start 切段时算进**上一段会话**，比丢掉还糟。代价是 BootState 那条 state_enter
        /// 记不到，换来的是后面每一条都落在正确的会话里。
        /// </summary>
        private bool CanTrack => telemetryService != null && telemetryService.SessionStarted;

        /// <summary>埋一条成功的转移。三个属性照契约固定用 from / to / ms。</summary>
        private void TrackTransition(string evt, string from, string to, long elapsedMs)
        {
            if (!CanTrack)
            {
                return;
            }

            telemetry.Track(
                evt,
                (TelemetryKeys.Props.From, from),
                (TelemetryKeys.Props.To, to),
                (TelemetryKeys.Props.Ms, elapsedMs));
        }

        /// <summary>埋一条失败的转移（E 级，带 err / st）。没有 ms：失败可能发生在转移的任何一步。</summary>
        private void TrackTransition(string evt, string from, string to, Exception error)
        {
            if (!CanTrack)
            {
                return;
            }

            telemetry.TrackError(
                evt,
                error,
                TelemetryProps.Of(
                    (TelemetryKeys.Props.From, from),
                    (TelemetryKeys.Props.To, to)));
        }

        /// <summary>一次排队中的切换请求：目标状态 + 取消令牌 + 让调用方 await 的完成源。</summary>
        private sealed class TransitionRequest
        {
            public TransitionRequest(Type stateType, CancellationToken token)
            {
                StateType = stateType;
                Token = token;
                Completion = new UniTaskCompletionSource();
            }

            public Type StateType { get; }

            public CancellationToken Token { get; }

            public UniTaskCompletionSource Completion { get; }
        }
    }
}
