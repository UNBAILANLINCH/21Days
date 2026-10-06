// 职责：覆盖 GameFlow 的核心规则——先 Exit 再 Enter、切换中的请求排队、切换完成发事件、
//   Exit 前发 GameStateChangingEvent（顺序 Changing → Exit → Enter → Changed）；
//   以及加载黑幕（roadmap E4）的接线：涉及场景才落幕、盖严才 Exit、队列清空才揭幕、失败 / 取消也揭、黑幕出错不拖垮切换。
// 为什么新建：这三条是状态机最容易被后续改动破坏的约定，而且完全是纯逻辑，
// 用 VContainer 建个最小容器就能测，不需要场景也不需要帧循环。黑幕用假实现记调用顺序，场景状态用假资源服务
// （SceneHandle 包一个无效的 Addressables 句柄，Dispose 不碰 Addressables），同样不需要真场景。

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Telemetry;
using Game.Tests.EditMode.Telemetry;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
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
        private FakeCurtain curtain;
        private List<GameStateChangedEvent> published;
        private IDisposable subscription;

        [SetUp]
        public void SetUp()
        {
            var builder = new ContainerBuilder();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<GameStateChangedEvent>(options);
            builder.RegisterMessageBroker<GameStateChangingEvent>(options);
            builder.Register<TransitionLog>(Lifetime.Singleton);

            // GameFlow 波 2 起要埋点，容器里得有这几样它才建得出来。
            // 假时钟 + 收集型 sink：不依赖真实时间，也不会往 Console 里打东西影响 LogAssert。
            // 本文件不断言埋点内容（那是 TelemetryServiceTests 的事），只保证接了埋点之后转移逻辑不变。
            builder.RegisterInstance(TelemetryOptions.Default);
            builder.RegisterInstance(new FakeTelemetryClock()).As<ITelemetryClock>();
            // 注册成数组：服务吃的是「终点列表」（同一次格式化分发给每一个），同 GameLifetimeScope
            builder.RegisterInstance(new ITelemetrySink[] { new RecordingTelemetrySink() });
            builder.Register<TelemetryService>(Lifetime.Singleton).As<ITelemetryService>();

            // 加载黑幕：假实现往同一本流水账里记 Cover / Reveal，和状态的 Enter / Exit 排在一条时间线上比先后。
            builder.Register<FakeCurtain>(Lifetime.Singleton).AsSelf().As<ILoadingCurtain>();
            builder.RegisterInstance<IAssetService>(new FakeSceneAssets());

            builder.Register<GameFlow>(Lifetime.Singleton).As<IGameFlow>();
            builder.Register<StateA>(Lifetime.Singleton);
            builder.Register<StateB>(Lifetime.Singleton);
            builder.Register<GatedState>(Lifetime.Singleton);
            builder.Register<ThrowingState>(Lifetime.Singleton);
            builder.Register<SceneA>(Lifetime.Singleton);
            builder.Register<GatedScene>(Lifetime.Singleton);
            builder.Register<ThrowingScene>(Lifetime.Singleton);
            container = builder.Build();

            flow = container.Resolve<IGameFlow>();
            log = container.Resolve<TransitionLog>();
            curtain = container.Resolve<FakeCurtain>();
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
        public void GoToAsync_WhenSwitchingStates_PublishesChangingBeforeExitAndChangedAfterEnter()
        {
            // 两个事件的订阅者往同一本流水账里记，和状态的 Enter / Exit 排在一条时间线上比先后。
            var changing = new List<GameStateChangingEvent>();
            using (container.Resolve<ISubscriber<GameStateChangingEvent>>().Subscribe(e =>
                   {
                       changing.Add(e);
                       log.Add("Changing:" + (e.From == null ? "null" : e.From.Name) + "->" + e.To.Name);
                   }))
            using (container.Resolve<ISubscriber<GameStateChangedEvent>>().Subscribe(e =>
                       log.Add("Changed:" + (e.From == null ? "null" : e.From.Name) + "->" + e.To.Name)))
            {
                flow.GoToAsync<StateA>().Forget();
                flow.GoToAsync<StateB>().Forget();
            }

            Assert.That(log.Entries, Is.EqualTo(new[]
            {
                "Changing:null->StateA", "A.Enter", "Changed:null->StateA",
                "Changing:StateA->StateB", "A.Exit", "B.Enter", "Changed:StateA->StateB",
            }), "顺序必须是 Changing → 前一状态 Exit → 新状态 Enter → Changed；首次进入也发 Changing");
            Assert.That(changing.Count, Is.EqualTo(2));
            Assert.That(changing[0].From, Is.Null, "首次进入状态机时 Changing 的 From 为 null");
            Assert.That(changing[1].From, Is.EqualTo(typeof(StateA)));
            Assert.That(changing[1].To, Is.EqualTo(typeof(StateB)));
        }

        [Test]
        public void GoToAsync_WhenChangingPublished_CurrentIsStillPreviousState()
        {
            flow.GoToAsync<StateA>().Forget();

            // 订阅者在回调里同步抓现场时，前一状态还没开始 Exit、Current 仍是它——这是 Changing 存在的意义。
            Type currentAtChanging = null;
            using (container.Resolve<ISubscriber<GameStateChangingEvent>>()
                       .Subscribe(_ => currentAtChanging = flow.Current == null ? null : flow.Current.GetType()))
            {
                flow.GoToAsync<StateB>().Forget();
            }

            Assert.That(currentAtChanging, Is.EqualTo(typeof(StateA)));
        }

        [Test]
        public void GoToAsync_BeforeAnyTransition_CurrentIsNull()
        {
            Assert.That(flow.Current, Is.Null);
        }

        [Test]
        public void GoToAsync_WhenEnterAsyncThrows_KeepsPreviousCurrentAndPropagates()
        {
            flow.GoToAsync<StateA>().Forget();

            // GameFlow 切换失败时会 Log.Error 一条，测试运行器默认把 Error 当失败，先声明预期。
            LogAssert.Expect(LogType.Error, new Regex("切换到 ThrowingState 失败"));

            UniTask failing = flow.GoToAsync<ThrowingState>();
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => failing.GetAwaiter().GetResult(),
                "Enter 抛的异常要原样回传给 GoToAsync 的调用方");
            Assert.That(error.Message, Is.EqualTo(ThrowingState.Message));

            // Current 的语义是「最近一个**成功进入**的状态」。ThrowingState 没进去，
            // 所以它仍是 StateA——注意此刻 StateA 的 ExitAsync 已经执行过了，
            // 处在「前一状态已退出、目标未进入」的空档，恢复手段就是再切一次。
            Assert.That(flow.Current, Is.TypeOf<StateA>());
            Assert.That(published.Count, Is.EqualTo(1), "没进去的状态不该发 GameStateChangedEvent");

            flow.GoToAsync<StateB>().Forget();

            Assert.That(flow.Current, Is.TypeOf<StateB>(), "失败之后队列要能继续处理后面的请求");
            Assert.That(published.Count, Is.EqualTo(2));
            Assert.That(published[1].From, Is.EqualTo(typeof(StateA)));
            Assert.That(published[1].To, Is.EqualTo(typeof(StateB)));
        }

        // ───────────────────────── 加载黑幕（roadmap E4）─────────────────────────

        [Test]
        public void GoToAsync_WhenTargetIsSceneState_CoversBeforeExitAndRevealsAfterChanged()
        {
            flow.GoToAsync<StateA>().Forget();
            using (LogChangeEvents())
            {
                flow.GoToAsync<SceneA>().Forget();
            }

            Assert.That(log.Entries, Is.EqualTo(new[]
            {
                "A.Enter",
                "Changing:StateA->SceneA", "Cover", "A.Exit", "SceneA.Enter", "Changed:StateA->SceneA", "Reveal",
            }), "目标带场景：Changing → 落幕 → Exit → Enter → Changed → 揭幕");
            Assert.That(curtain.IsCovered, Is.False);
        }

        [Test]
        public void GoToAsync_WhenLeavingSceneState_CoversBeforeSceneExit()
        {
            flow.GoToAsync<SceneA>().Forget();
            flow.GoToAsync<StateB>().Forget();

            // 第二次的目标不带场景，但前一状态带场景——场景拆掉的那一下同样要盖住（场景→标题就是这条路）。
            Assert.That(log.Entries, Is.EqualTo(new[]
            {
                "Cover", "SceneA.Enter", "Reveal",
                "Cover", "SceneA.Exit", "B.Enter", "Reveal",
            }));
        }

        [Test]
        public void GoToAsync_WhenNeitherSideIsSceneState_DoesNotTouchCurtain()
        {
            flow.GoToAsync<StateA>().Forget();
            flow.GoToAsync<StateB>().Forget();

            Assert.That(curtain.CoverCalls, Is.Zero, "Boot→Title 这类两侧都不带场景的切换不落幕");
            Assert.That(curtain.RevealCalls, Is.Zero);
            Assert.That(log.Entries, Is.EqualTo(new[] { "A.Enter", "A.Exit", "B.Enter" }));
        }

        [Test]
        public void GoToAsync_WhenMoreRequestsQueued_StaysCoveredUntilQueueDrains()
        {
            var gated = container.Resolve<GatedScene>();
            flow.GoToAsync<StateA>().Forget();
            flow.GoToAsync<GatedScene>().Forget();
            flow.GoToAsync<StateB>().Forget();

            Assert.That(log.Entries, Is.EqualTo(new[] { "A.Enter", "Cover", "A.Exit", "GatedScene.Enter" }));

            gated.OpenGate();

            Assert.That(log.Entries, Is.EqualTo(new[]
            {
                "A.Enter", "Cover", "A.Exit", "GatedScene.Enter",
                "Cover", "GatedScene.Exit", "B.Enter", "Reveal",
            }), "中间那次切完队列里还有请求，不揭幕；最后一次切完队列空了才揭");
            Assert.That(curtain.RevealCalls, Is.EqualTo(1));
            Assert.That(curtain.IsCovered, Is.False);
        }

        [Test]
        public void GoToAsync_WhenSceneEnterThrows_StillReveals()
        {
            flow.GoToAsync<StateA>().Forget();
            LogAssert.Expect(LogType.Error, new Regex("切换到 ThrowingScene 失败"));

            UniTask failing = flow.GoToAsync<ThrowingScene>();

            Assert.Throws<InvalidOperationException>(() => failing.GetAwaiter().GetResult());
            Assert.That(log.Entries, Is.EqualTo(new[]
            {
                "A.Enter", "Cover", "A.Exit", "ThrowingScene.Enter", "ThrowingScene.Exit", "Reveal",
            }), "Enter 失败也要揭幕，绝不能留永久黑屏");
            Assert.That(curtain.IsCovered, Is.False);
        }

        [Test]
        public void GoToAsync_WhenCanceledWhileCovering_SkipsExitAndStillReveals()
        {
            flow.GoToAsync<StateA>().Forget();
            curtain.HoldCover = true;

            using (var cts = new CancellationTokenSource())
            {
                UniTask pending = flow.GoToAsync<SceneA>(cts.Token);
                Assert.That(log.Entries, Is.EqualTo(new[] { "A.Enter", "Cover" }), "没盖严之前不许 Exit");

                cts.Cancel();

                Assert.That(pending.Status, Is.EqualTo(UniTaskStatus.Canceled));
            }

            Assert.That(log.Entries, Is.EqualTo(new[] { "A.Enter", "Cover", "Reveal" }), "落幕途中被取消：不 Exit，照样揭幕");
            Assert.That(curtain.IsCovered, Is.False);
            Assert.That(flow.Current, Is.TypeOf<StateA>());
        }

        [Test]
        public void GoToAsync_WhenCurtainCoverThrows_WarnsAndStillTransitions()
        {
            flow.GoToAsync<StateA>().Forget();
            curtain.ThrowOnCover = true;
            LogAssert.Expect(LogType.Warning, new Regex("加载黑幕落幕失败"));

            UniTask task = flow.GoToAsync<SceneA>();

            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded), "黑幕自己出错不能拖垮切换");
            Assert.That(flow.Current, Is.TypeOf<SceneA>());
            Assert.That(log.Entries, Is.EqualTo(new[] { "A.Enter", "Cover", "A.Exit", "SceneA.Enter", "Reveal" }));
        }

        [Test]
        public void GoToAsync_WhenCurtainRevealThrows_WarnsAndKeepsQueueWorking()
        {
            curtain.ThrowOnReveal = true;
            LogAssert.Expect(LogType.Warning, new Regex("加载黑幕揭幕失败"));

            UniTask task = flow.GoToAsync<SceneA>();

            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(flow.Current, Is.TypeOf<SceneA>());

            curtain.ThrowOnReveal = false;
            flow.GoToAsync<StateB>().Forget();

            Assert.That(flow.Current, Is.TypeOf<StateB>(), "揭幕出错之后队列照常处理后面的请求");
            Assert.That(curtain.RevealCalls, Is.EqualTo(2));
        }

        [Test]
        public void GoToAsync_WhenQueueDrains_CompletesOnlyAfterReveal()
        {
            flow.GoToAsync<StateA>().Forget();
            curtain.HoldReveal = true;

            UniTask task = flow.GoToAsync<SceneA>();

            Assert.That(flow.Current, Is.TypeOf<SceneA>());
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending), "揭幕还没完，GoToAsync 不该完成");

            curtain.ReleaseReveal();

            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        [Test]
        public void GoToAsync_WhenAnotherRequestQueued_CompletesWithoutWaitingForReveal()
        {
            var gated = container.Resolve<GatedScene>();
            curtain.HoldReveal = true;

            UniTask first = flow.GoToAsync<GatedScene>();
            UniTask second = flow.GoToAsync<SceneA>();
            gated.OpenGate();

            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Succeeded), "后面还有请求：这一次不揭幕，切完就完成");
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Pending), "队列清空的那一次要等揭幕");

            curtain.ReleaseReveal();

            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(curtain.RevealCalls, Is.EqualTo(1));
        }

        [Test]
        public void GoToAsync_WithoutCurtain_SceneTransitionsStillWork()
        {
            // 黑幕参数为 null（EditMode 直接 new 的 GameFlow）：不落幕，其余行为不变。
            var bare = new GameFlow(
                container,
                container.Resolve<IPublisher<GameStateChangedEvent>>(),
                container.Resolve<IPublisher<GameStateChangingEvent>>(),
                null,
                null,
                null);

            bare.GoToAsync<SceneA>().Forget();
            bare.GoToAsync<StateB>().Forget();

            Assert.That(bare.Current, Is.TypeOf<StateB>());
            Assert.That(log.Entries, Is.EqualTo(new[] { "SceneA.Enter", "SceneA.Exit", "B.Enter" }));
            Assert.That(curtain.CoverCalls, Is.Zero);
        }

        /// <summary>把 Changing / Changed 两个事件记进同一本流水账，返回值 Dispose 即退订两者。</summary>
        private IDisposable LogChangeEvents()
        {
            IDisposable changing = container.Resolve<ISubscriber<GameStateChangingEvent>>()
                .Subscribe(e => log.Add("Changing:" + (e.From == null ? "null" : e.From.Name) + "->" + e.To.Name));
            IDisposable changed = container.Resolve<ISubscriber<GameStateChangedEvent>>()
                .Subscribe(e => log.Add("Changed:" + (e.From == null ? "null" : e.From.Name) + "->" + e.To.Name));
            return new PairDisposable(changing, changed);
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

        /// <summary>EnterAsync 直接抛异常的状态，用来覆盖「目标状态进不去」这条失败路径。</summary>
        public sealed class ThrowingState : GameState
        {
            /// <summary>抛出的异常消息，测试用它确认拿到的就是这一个异常。</summary>
            public const string Message = "ThrowingState 进不去";

            private readonly TransitionLog log;

            public ThrowingState(TransitionLog log) => this.log = log;

            public override UniTask EnterAsync(CancellationToken ct)
            {
                log.Add("Throwing.Enter");
                throw new InvalidOperationException(Message);
            }

            public override UniTask ExitAsync(CancellationToken ct)
            {
                log.Add("Throwing.Exit");
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

        /// <summary>
        /// 假加载黑幕：每次调用都往流水账里记一笔（已盖着时 GameFlow 照样会调 Cover，真实现会直接返回）。
        /// 可以让落幕 / 揭幕卡住（制造「还在淡入 / 淡出」的窗口）或出错。IsCovered 语义同真实现：落幕开始即 true。
        /// </summary>
        public sealed class FakeCurtain : ILoadingCurtain
        {
            private readonly TransitionLog log;
            private UniTaskCompletionSource revealGate;

            public FakeCurtain(TransitionLog log) => this.log = log;

            public bool IsCovered { get; private set; }

            public int CoverCalls { get; private set; }

            public int RevealCalls { get; private set; }

            /// <summary>为 true 时落幕一直不完成，只能被取消。</summary>
            public bool HoldCover { get; set; }

            /// <summary>为 true 时揭幕挂起，等 <see cref="ReleaseReveal"/>。</summary>
            public bool HoldReveal { get; set; }

            /// <summary>为 true 时落幕失败，且屏上没黑（模拟面板开不出来）。</summary>
            public bool ThrowOnCover { get; set; }

            /// <summary>为 true 时揭幕失败（真实现会先硬收掉再抛）。</summary>
            public bool ThrowOnReveal { get; set; }

            public UniTask CoverAsync(CancellationToken ct)
            {
                CoverCalls++;
                log.Add("Cover");
                if (ThrowOnCover)
                {
                    return UniTask.FromException(new InvalidOperationException("假黑幕：面板开不出来"));
                }

                IsCovered = true;
                return HoldCover
                    ? new UniTaskCompletionSource().Task.AttachExternalCancellation(ct)
                    : UniTask.CompletedTask;
            }

            public UniTask RevealAsync(CancellationToken ct)
            {
                RevealCalls++;
                log.Add("Reveal");
                IsCovered = false;
                if (ThrowOnReveal)
                {
                    return UniTask.FromException(new InvalidOperationException("假黑幕：揭幕出错"));
                }

                if (!HoldReveal)
                {
                    return UniTask.CompletedTask;
                }

                revealGate = new UniTaskCompletionSource();
                return revealGate.Task;
            }

            /// <summary>放行挂起的揭幕。</summary>
            public void ReleaseReveal() => revealGate.TrySetResult();
        }

        /// <summary>
        /// 只认 LoadSceneAsync 的假资源服务。返回的句柄包着无效的 Addressables 句柄：Scene 是 default，
        /// Dispose 不碰 Addressables——正好够场景状态走完加载 / 卸载生命周期。
        /// </summary>
        public sealed class FakeSceneAssets : IAssetService
        {
            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default)
                where T : UnityEngine.Object => throw new NotSupportedException(nameof(LoadAsync));

            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default)
                where T : UnityEngine.Object => throw new NotSupportedException(nameof(LoadAllAsync));

            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default)
                => throw new NotSupportedException(nameof(InstantiateAsync));

            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException(nameof(ReleaseInstance));

            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default)
                => UniTask.FromResult(new SceneHandle(key, default));
        }

        /// <summary>同步完成的带场景状态。</summary>
        public sealed class SceneA : SceneGameState
        {
            private readonly TransitionLog log;

            public SceneA(IAssetService assets, TransitionLog log) : base(assets) => this.log = log;

            protected override string SceneKey => "Test_SceneA";

            protected override UniTask OnSceneReadyAsync(CancellationToken ct)
            {
                log.Add("SceneA.Enter");
                return UniTask.CompletedTask;
            }

            protected override UniTask OnSceneUnloadingAsync(CancellationToken ct)
            {
                log.Add("SceneA.Exit");
                return UniTask.CompletedTask;
            }
        }

        /// <summary>场景就绪后卡在闸门上的带场景状态，用来制造「切换进行中、队列里还有请求」的窗口。</summary>
        public sealed class GatedScene : SceneGameState
        {
            private readonly TransitionLog log;
            private UniTaskCompletionSource gate;

            public GatedScene(IAssetService assets, TransitionLog log) : base(assets) => this.log = log;

            protected override string SceneKey => "Test_GatedScene";

            protected override UniTask OnSceneReadyAsync(CancellationToken ct)
            {
                log.Add("GatedScene.Enter");
                gate = new UniTaskCompletionSource();
                return gate.Task;
            }

            protected override UniTask OnSceneUnloadingAsync(CancellationToken ct)
            {
                log.Add("GatedScene.Exit");
                return UniTask.CompletedTask;
            }

            /// <summary>放行，让 EnterAsync 返回。</summary>
            public void OpenGate() => gate.TrySetResult();
        }

        /// <summary>场景加载完、绑定时抛异常的带场景状态（基类会先把场景卸掉再往外抛）。</summary>
        public sealed class ThrowingScene : SceneGameState
        {
            private readonly TransitionLog log;

            public ThrowingScene(IAssetService assets, TransitionLog log) : base(assets) => this.log = log;

            protected override string SceneKey => "Test_ThrowingScene";

            protected override UniTask OnSceneReadyAsync(CancellationToken ct)
            {
                log.Add("ThrowingScene.Enter");
                throw new InvalidOperationException("ThrowingScene 绑定失败");
            }

            protected override UniTask OnSceneUnloadingAsync(CancellationToken ct)
            {
                log.Add("ThrowingScene.Exit");
                return UniTask.CompletedTask;
            }
        }

        /// <summary>一次退订两个订阅。</summary>
        private sealed class PairDisposable : IDisposable
        {
            private readonly IDisposable first;
            private readonly IDisposable second;

            public PairDisposable(IDisposable first, IDisposable second)
            {
                this.first = first;
                this.second = second;
            }

            public void Dispose()
            {
                first.Dispose();
                second.Dispose();
            }
        }
    }
}
