// 职责：IGameFlow 的唯一实现——串行切换 + 请求排队 + 发布 GameStateChangedEvent。
// 为什么新建：没有现成实现；也不能把排队逻辑塞进 GameState（状态不该知道有没有别的状态在排队）。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Logging;
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
        private readonly Queue<TransitionRequest> queue = new Queue<TransitionRequest>();

        private bool processing;

        public GameFlow(IObjectResolver resolver, IPublisher<GameStateChangedEvent> stateChangedPublisher)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.stateChangedPublisher = stateChangedPublisher
                ?? throw new ArgumentNullException(nameof(stateChangedPublisher));
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

            if (Current != null)
            {
                await Current.ExitAsync(ct);
            }

            // Enter 成功之后才换 Current。提前赋值的话，Enter 抛异常时 Current 会指着一个**从没进入过**的
            // 状态，下一次切换会去 Exit 它，Exit 里那些「Enter 时申请的资源」全是空的。
            // 代价是：切换失败后处于「前一状态已 Exit、目标未 Enter」的空档，此时 Current 仍指向前一状态——
            // 它的语义是「最近一个成功进入的状态」，不是「场上活着的状态」。异常照旧回传给 GoToAsync 的
            // 调用方，队列继续处理后面的请求；恢复手段是再 GoToAsync 到一个能进得去的状态。
            await next.EnterAsync(ct);
            Current = next;

            // 切换完成后发布一次，带上切换前后的状态类型；订阅者拿到时 Current 已是新状态。
            // 失败路径不发这个事件——没进去的状态不算「切换完成」。
            stateChangedPublisher.Publish(new GameStateChangedEvent(from, stateType));
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
