// 职责：覆盖 GameFlow 的三条核心规则——先 Exit 再 Enter、切换中的请求排队、切换完成发事件。
// 为什么新建：这三条是状态机最容易被后续改动破坏的约定，而且完全是纯逻辑，
// 用 VContainer 建个最小容器就能测，不需要场景也不需要帧循环。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using MessagePipe;
using NUnit.Framework;
using VContainer;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// GameFlow 的 EditMode 测试。测试用的状态全部同步完成（GatedState 除外），
    /// 所以调用 GoToAsync 后断言可以立刻做，不需要等帧。
    /// </summary>
    public sealed class GameFlowTests
    {
        private IObjectResolver container;
        private IGameFlow flow;
        private TransitionLog log;
        private List<GameStateChangedEvent> published;
        private IDisposable subscription;

        [SetUp]
        public void SetUp()
        {
            var builder = new ContainerBuilder();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<GameStateChangedEvent>(options);
            builder.Register<TransitionLog>(Lifetime.Singleton);
            builder.Register<GameFlow>(Lifetime.Singleton).As<IGameFlow>();
            builder.Register<StateA>(Lifetime.Singleton);
            builder.Register<StateB>(Lifetime.Singleton);
            builder.Register<GatedState>(Lifetime.Singleton);
            container = builder.Build();

            flow = container.Resolve<IGameFlow>();
            log = container.Resolve<TransitionLog>();
            published = new List<GameStateChangedEvent>();
            subscription = container.Resolve<ISubscriber<GameStateChangedEvent>>()
                .Subscribe(e => published.Add(e));
        }

        [TearDown]
        public void TearDown()
        {
            subscription.Dispose();
            container.Dispose();
        }

        [Test]
        public void GoToAsync_WhenSwitchingStates_ExitsCurrentBeforeEnteringNext()
        {
            flow.GoToAsync<StateA>().Forget();
            flow.GoToAsync<StateB>().Forget();

            Assert.That(log.Entries, Is.EqualTo(new[] { "A.Enter", "A.Exit", "B.Enter" }));
            Assert.That(flow.Current, Is.TypeOf<StateB>());
        }

        [Test]
        public void GoToAsync_WhenCalledDuringTransition_QueuesUntilCurrentFinishes()
        {
            var gated = container.Resolve<GatedState>();

            flow.GoToAsync<GatedState>().Forget();
            flow.GoToAsync<StateB>().Forget();

            Assert.That(log.Entries, Is.EqualTo(new[] { "Gated.Enter" }),
                "前一次切换还没完成，后一次请求必须排队而不是插进来");

            gated.OpenGate();

            Assert.That(log.Entries, Is.EqualTo(new[] { "Gated.Enter", "Gated.Exit", "B.Enter" }));
            Assert.That(flow.Current, Is.TypeOf<StateB>());
        }

        [Test]
        public void GoToAsync_WhenTransitionCompletes_PublishesStateChangedEvent()
        {
            flow.GoToAsync<StateA>().Forget();
            flow.GoToAsync<StateB>().Forget();

            Assert.That(published.Count, Is.EqualTo(2));
            Assert.That(published[0].From, Is.Null, "首次进入状态机时 From 为 null");
            Assert.That(published[0].To, Is.EqualTo(typeof(StateA)));
            Assert.That(published[1].From, Is.EqualTo(typeof(StateA)));
            Assert.That(published[1].To, Is.EqualTo(typeof(StateB)));
        }

        [Test]
        public void GoToAsync_BeforeAnyTransition_CurrentIsNull()
        {
            Assert.That(flow.Current, Is.Null);
        }

        /// <summary>记录状态进出顺序的公共黑板，由容器注入给各个测试状态。</summary>
        public sealed class TransitionLog
        {
            private readonly List<string> entries = new List<string>();

            public IReadOnlyList<string> Entries => entries;

            public void Add(string entry) => entries.Add(entry);
        }

        /// <summary>同步完成的状态 A。</summary>
        public sealed class StateA : GameState
        {
            private readonly TransitionLog log;

            public StateA(TransitionLog log) => this.log = log;

            public override UniTask EnterAsync(CancellationToken ct)
            {
                log.Add("A.Enter");
                return UniTask.CompletedTask;
            }

            public override UniTask ExitAsync(CancellationToken ct)
            {
                log.Add("A.Exit");
                return UniTask.CompletedTask;
            }
        }

        /// <summary>同步完成的状态 B。</summary>
        public sealed class StateB : GameState
        {
            private readonly TransitionLog log;

            public StateB(TransitionLog log) => this.log = log;

            public override UniTask EnterAsync(CancellationToken ct)
            {
                log.Add("B.Enter");
                return UniTask.CompletedTask;
            }

            public override UniTask ExitAsync(CancellationToken ct)
            {
                log.Add("B.Exit");
                return UniTask.CompletedTask;
            }
        }

        /// <summary>EnterAsync 卡在闸门上的状态，用来制造「切换进行中」的窗口。</summary>
        public sealed class GatedState : GameState
        {
            private readonly TransitionLog log;
            private UniTaskCompletionSource gate;

            public GatedState(TransitionLog log) => this.log = log;

            public override UniTask EnterAsync(CancellationToken ct)
            {
                log.Add("Gated.Enter");
                gate = new UniTaskCompletionSource();
                return gate.Task;
            }

            public override UniTask ExitAsync(CancellationToken ct)
            {
                log.Add("Gated.Exit");
                return UniTask.CompletedTask;
            }

            /// <summary>放行，让 EnterAsync 返回。</summary>
            public void OpenGate() => gate.TrySetResult();
        }
    }
}
