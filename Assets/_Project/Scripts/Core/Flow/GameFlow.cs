// 职责：IGameFlow 的唯一实现——串行切换 + 请求排队 + 发布 GameStateChangingEvent（Exit 前）/ GameStateChangedEvent（Enter 后）。
// 为什么新建：没有现成实现；也不能把排队逻辑塞进 GameState（状态不该知道有没有别的状态在排队）。

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

        private bool processing;

        /// <summary>
        /// 埋点两个参数允许为 null（EditMode 测试里直接 new 出来的 GameFlow 没有容器）：
        /// 拿不到就整条埋点链路变空操作，业务路径一个字节都不变。
        /// </summary>
        public GameFlow(
            IObjectResolver resolver,
            IPublisher<GameStateChangedEvent> stateChangedPublisher,
            IPublisher<GameStateChangingEvent> stateChangingPublisher,
            ITelemetryService telemetryService,
            ITelemetryClock clock)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.stateChangedPublisher = stateChangedPublisher
                ?? throw new ArgumentNullException(nameof(stateChangedPublisher));
            this.stateChangingPublisher = stateChangingPublisher
                ?? throw new ArgumentNullException(nameof(stateChangingPublisher));
            this.telemetryService = telemetryService;
            this.clock = clock;
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
                    try
                    {
                        await TransitionAsync(request.StateType, request.Token);
                        request.Completion.TrySetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        request.Completion.TrySetCanceled(request.Token);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"切换到 {request.StateType.Name} 失败：{e}");

                        // Current 的语义是「最近一个成功进入的状态」，切换失败时它正好就是这次转移的来源。
                        TrackTransition(
                            TelemetryKeys.FlowEvents.StateFailed,
                            Current == null ? string.Empty : Current.GetType().Name,
                            request.StateType.Name,
                            e);
                        request.Completion.TrySetException(e);
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
