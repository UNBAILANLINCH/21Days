// 职责：钉住固定步长推进器的立身之本——帧率无关、Driven 模式不自走、追帧有上限且余量整段丢弃、
//   每个 tick 各采样一次、步骤看到的是固定步长而不是渲染帧时长。
// 为什么新建：Core/Simulation/ 下 15 个文件一条测试都没有，而这套东西是「输入录制 + 逻辑重放」的地基——
//   它错了不会编译失败也不会报错，只会在某次重放里悄悄分叉。
//   为什么不放进 Tests/EditMode/Core/：那个目录按单个服务分文件，确定性内核是一整层
//   （推进 + 随机 + 输入命令），照 Tests/EditMode/Telemetry/ 的先例单开一个目录。

using System;
using System.Collections.Generic;
using Game.Core.Simulation;
using Game.Core.Timing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Simulation
{
    /// <summary>
    /// <see cref="SimulationRunner"/> 的 EditMode 测试。渲染帧时长由假时钟给、推进由测试手动调，
    /// 因此完全确定性：不依赖场景、不依赖资产路径、不依赖机器快慢。
    /// </summary>
    public sealed class SimulationRunnerTests
    {
        /// <summary>
        /// 本文件统一取 64 Hz 而不是默认的 60 Hz：步长 1/64 = 0.015625，在 float 里是**精确值**，
        /// 「N 个 tick 的时长」也就能精确表达，判据不会被浮点末位误差搅动。
        /// </summary>
        private const int TickRate = 64;

        /// <summary>除了专测追帧上限的用例，其余用例把上限开到足够大，免得它意外参与判定。</summary>
        private const int UnlimitedCatchUp = 256;

        /// <summary>CreateInstance 出来的配置资产不属于任何场景，得自己销毁，否则 EditMode 下会泄漏。</summary>
        private readonly List<SimulationConfig> createdConfigs = new List<SimulationConfig>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < createdConfigs.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(createdConfigs[i]);
            }

            createdConfigs.Clear();
        }

        [Test]
        public void Tick_WhenFrameRateDiffers_ProducesTheSameTickSequence()
        {
            // 两种喂法总时长都是 1.1 秒：10 FPS 喂 11 帧 × 0.1 秒，200 FPS 喂 220 帧 × 0.005 秒。
            //
            // 判据为什么挑 1.1 秒 / 64 Hz：1.1 × 64 = 70.4 个 tick，离两侧 tick 边界各有 0.4 个 tick
            // （约 6 毫秒）的余量，而两种喂法累加出来的浮点误差只有 1e-7 秒量级，够不着边界。
            // 反例值得写下来：若取 60 Hz，1.1 秒恰好等于 66 个**整** tick，两种喂法一个落在边界左边、
            // 一个落在右边，结果会差整整一个 tick——那测的是浮点舍入，不是帧率无关性。
            // 换句话说，边界上的总时长本身就没有唯一正确答案，不该拿来当判据。
            RecordingStep slowFrames = FeedUniformFrames(11, 0.1f);
            RecordingStep fastFrames = FeedUniformFrames(220, 0.005f);

            // 先钉死绝对值，免得「两边都推了 0 个 tick」也能让下一条断言通过
            Assert.That(slowFrames.Ticks.Count, Is.EqualTo(70), "1.1 秒在 64 Hz 下应推进 70 个 tick");
            Assert.That(fastFrames.Ticks, Is.EqualTo(slowFrames.Ticks), "同样的总时长，两种帧率必须推出同一串 tick");
        }

        [Test]
        public void AdvanceOneTick_Always_PassesFixedDeltaTimeNotFrameDeltaTime()
        {
            FakeFrameClock clock = new FakeFrameClock();
            RecordingInputSource input = new RecordingInputSource();
            SimulationRunner runner = CreateRunner(clock, input);
            RecordingStep step = new RecordingStep();
            runner.AddStep(step);

            // 三帧长短悬殊，但每个 tick 拿到的 DeltaTime 都必须是同一个固定步长
            clock.SetFrameSeconds(0.1f);
            runner.Tick();
            clock.SetFrameSeconds(0.4f);
            runner.Tick();
            clock.SetFrameSeconds(0.016f);
            runner.Tick();

            Assert.That(step.DeltaTimes.Count, Is.GreaterThan(0), "这几帧总该推出点 tick 来");
            for (int i = 0; i < step.DeltaTimes.Count; i++)
            {
                Assert.That(
                    step.DeltaTimes[i],
                    Is.EqualTo(1f / TickRate),
                    $"第 {i} 个 tick 拿到的不是固定步长——逻辑一旦读到渲染帧时长，重放必分叉");
            }
        }

        [Test]
        public void Tick_InDrivenMode_AdvancesNothingUntilAdvanceOneTickIsCalled()
        {
            FakeFrameClock clock = new FakeFrameClock();
            RecordingInputSource input = new RecordingInputSource();
            SimulationRunner runner = CreateRunner(clock, input);
            RecordingStep step = new RecordingStep();
            runner.AddStep(step);

            runner.SetMode(SimulationRunner.Mode.Driven);

            // 一帧喂 0.5 秒（= 32 个 tick 的量）连喂 10 帧：同样的喂法在 Live 下早该推出 320 个 tick
            clock.SetFrameSeconds(0.5f);
            for (int i = 0; i < 10; i++)
            {
                runner.Tick();
            }

            Assert.That(step.Ticks.Count, Is.EqualTo(0), "Driven 模式下 Tick 不许自己推进，节奏由播放器说了算");
            Assert.That(runner.Clock.Tick, Is.EqualTo(0L), "一个 tick 都没推，逻辑时钟就不该动");
            Assert.That(input.SampledTicks.Count, Is.EqualTo(0), "没推进就不该采样");

            runner.AdvanceOneTick();
            runner.AdvanceOneTick();

            Assert.That(step.Ticks, Is.EqualTo(new long[] { 0L, 1L }), "显式驱动几次就该推几个 tick");
            Assert.That(runner.Clock.Tick, Is.EqualTo(2L));
        }

        [Test]
        public void Tick_WhenFrameTimeExceedsCatchUpLimit_AdvancesAtMostTheLimitAndDropsTheRest()
        {
            const int MaxCatchUpTicks = 5;

            FakeFrameClock clock = new FakeFrameClock();
            RecordingInputSource input = new RecordingInputSource();
            SimulationRunner runner = CreateRunner(clock, input, MaxCatchUpTicks);
            RecordingStep step = new RecordingStep();
            runner.AddStep(step);

            // 一帧喂 10 秒 = 640 个 tick 的量，上限只有 5 个
            clock.SetFrameSeconds(10f);
            runner.Tick();

            Assert.That(step.Ticks.Count, Is.EqualTo(MaxCatchUpTicks), "一帧推进数不许超过追帧上限");
            Assert.That(
                runner.Accumulator,
                Is.LessThan(runner.Clock.FixedDeltaTime),
                "超出上限的时间必须整段丢掉，不能留在 accumulator 里等下一帧补课");

            // 下一帧只喂 3 个 tick 的量（3/64 秒，在 float 里精确）：
            // 上一帧的余量若没丢干净，这里会再次顶到上限推 5 个，而不是老老实实推 3 个。
            clock.SetFrameSeconds(3f / TickRate);
            runner.Tick();

            Assert.That(
                step.Ticks.Count,
                Is.EqualTo(MaxCatchUpTicks + 3),
                "丢弃之后下一帧只该推它自己那 3 个 tick，不该「补课」式地继续爆推");
        }

        [Test]
        public void Tick_WhenAdvancing_SamplesInputSourceOncePerTickInTheSameOrder()
        {
            FakeFrameClock clock = new FakeFrameClock();
            RecordingInputSource input = new RecordingInputSource();
            SimulationRunner runner = CreateRunner(clock, input);
            RecordingStep step = new RecordingStep();
            runner.AddStep(step);

            // 三帧各推 2 / 5 / 1 个 tick，一共 8 个：帧长故意不等，免得「一帧一采样」也能蒙混过关
            float[] frameSeconds = { 2f / TickRate, 5f / TickRate, 1f / TickRate };
            for (int i = 0; i < frameSeconds.Length; i++)
            {
                clock.SetFrameSeconds(frameSeconds[i]);
                runner.Tick();
            }

            Assert.That(step.Ticks, Is.EqualTo(new long[] { 0L, 1L, 2L, 3L, 4L, 5L, 6L, 7L }), "8 个 tick 应连号推进");
            Assert.That(
                input.SampledTicks,
                Is.EqualTo(step.Ticks),
                "Sample 收到的 tick 序列必须与实际推进的 tick 一一对应、顺序一致");
        }

        [Test]
        public void Tick_WhenOneFrameCoversManyTicks_SamplesOncePerTickNotOncePerFrame()
        {
            FakeFrameClock clock = new FakeFrameClock();
            RecordingInputSource input = new RecordingInputSource();
            SimulationRunner runner = CreateRunner(clock, input);
            RecordingStep step = new RecordingStep();
            runner.AddStep(step);

            // 一帧喂 4 个 tick 的量，只调一次 Tick()
            clock.SetFrameSeconds(4f / TickRate);
            runner.Tick();

            Assert.That(step.Ticks.Count, Is.EqualTo(4), "一帧该补 4 个 tick");
            Assert.That(
                input.SampledTicks,
                Is.EqualTo(new long[] { 0L, 1L, 2L, 3L }),
                "一帧补 4 个 tick 就该采样 4 次。这是刻意的设计，别「优化」成一帧只采一条——"
                + "录制按 tick 逐条记、重放按 tick 逐条喂，少记一条后面所有输入整体错位一格");
        }

        [Test]
        public void AdvanceOneTick_WhenStepRuns_SeesTheCommandSampledForThatSameTick()
        {
            FakeFrameClock clock = new FakeFrameClock();
            RecordingInputSource input = new RecordingInputSource();
            SimulationRunner runner = CreateRunner(clock, input);
            RecordingStep step = new RecordingStep();
            runner.AddStep(step);

            for (int i = 0; i < 4; i++)
            {
                runner.AdvanceOneTick();
            }

            // RecordingInputSource 每次 Sample 把 Buttons 设成 tick + 1，所以第 n 个 tick 该看到 n+1。
            // 推进器若写成「先读 Current 再 Sample」，第 0 个 tick 会读到空命令（0），整串往后错一位——
            // 这正是回放类 bug 里最难用肉眼看出来的那一种：画面照样在动，只是每个操作都晚一格生效。
            Assert.That(
                step.Buttons,
                Is.EqualTo(new uint[] { 1u, 2u, 3u, 4u }),
                "步骤读到的必须是本 tick 刚采到的那条命令，不能是上一 tick 的");
        }

        /// <summary>
        /// 用等长帧连喂 <paramref name="frameCount"/> 帧，返回记录下 tick 序列的步骤。
        /// 每次调用都新建一整套（时钟 / 输入源 / 推进器），两组喂法之间不共享任何状态。
        /// </summary>
        private RecordingStep FeedUniformFrames(int frameCount, float frameSeconds)
        {
            FakeFrameClock clock = new FakeFrameClock();
            RecordingInputSource input = new RecordingInputSource();
            SimulationRunner runner = CreateRunner(clock, input);
            RecordingStep step = new RecordingStep();
            runner.AddStep(step);

            clock.SetFrameSeconds(frameSeconds);
            for (int i = 0; i < frameCount; i++)
            {
                runner.Tick();
            }

            return step;
        }

        /// <summary>
        /// 建一个不依赖任何资产路径的推进器。埋点传 null（构造函数文档写明允许），
        /// 随机源给一个固定种子的真实实现——这几条用例都不看随机数，只是构造函数要求非空。
        /// </summary>
        private SimulationRunner CreateRunner(
            FakeFrameClock clock,
            IInputSource inputSource,
            int maxCatchUpTicks = UnlimitedCatchUp)
        {
            return new SimulationRunner(
                CreateConfig(TickRate, maxCatchUpTicks),
                clock,
                inputSource,
                new RandomService(1UL),
                null);
        }

        /// <summary>
        /// 造一份内存里的 <see cref="SimulationConfig"/>。两个字段都是 <c>[SerializeField] private</c>、
        /// 没有公开写入口，所以走 <see cref="SerializedObject"/> 改（测试程序集是 Editor-only，用得了）。
        /// </summary>
        private SimulationConfig CreateConfig(int tickRate, int maxCatchUpTicks)
        {
            SimulationConfig config = ScriptableObject.CreateInstance<SimulationConfig>();
            createdConfigs.Add(config);

            SerializedObject serialized = new SerializedObject(config);
            serialized.FindProperty("tickRate").intValue = tickRate;
            serialized.FindProperty("maxCatchUpTicks").intValue = maxCatchUpTicks;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(config.TickRate, Is.EqualTo(tickRate), "配置没改成功，后面的判据全不成立");
            Assert.That(config.MaxCatchUpTicks, Is.EqualTo(maxCatchUpTicks), "配置没改成功，后面的判据全不成立");
            return config;
        }

        /// <summary>
        /// 假渲染帧时钟。推进器只读 <see cref="DeltaTime"/>，其余成员给出自洽的值即可。
        /// </summary>
        private sealed class FakeFrameClock : IClock
        {
            public DateTime UtcNow => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public float GameTime { get; private set; }

            public float UnscaledTime => GameTime;

            public float DeltaTime { get; private set; }

            public float UnscaledDeltaTime => DeltaTime;

            /// <summary>把「上一帧时长」设成 <paramref name="seconds"/>，之后每次 Tick 都按这个长度算。</summary>
            public void SetFrameSeconds(float seconds)
            {
                DeltaTime = seconds;
                GameTime += seconds;
            }
        }

        /// <summary>
        /// 记录采样 tick 的假输入源。每次采样按 tick 造一条**独一无二**的命令
        /// （Buttons = tick + 1），这样「步骤读到的是不是本 tick 那条」就能被断言区分开。
        /// </summary>
        private sealed class RecordingInputSource : IInputSource
        {
            private readonly List<long> sampledTicks = new List<long>();

            private InputCommand current;

            /// <summary>按调用顺序记下 <see cref="Sample"/> 收到的每个 tick 号。</summary>
            public IReadOnlyList<long> SampledTicks => sampledTicks;

            public InputCommand Current => current;

            public void Sample(long tick)
            {
                sampledTicks.Add(tick);
                current = new InputCommand(
                    new Vector2(tick, -tick),
                    Vector2.zero,
                    unchecked((uint)(tick + 1)),
                    Vector2.zero,
                    0);
            }
        }

        /// <summary>把每个 tick 的上下文关键项抄进列表，供断言逐项比对。</summary>
        private sealed class RecordingStep : ISimulationStep
        {
            private readonly List<long> ticks = new List<long>();
            private readonly List<uint> buttons = new List<uint>();
            private readonly List<float> deltaTimes = new List<float>();

            /// <summary>按执行顺序排列的 tick 序号。</summary>
            public IReadOnlyList<long> Ticks => ticks;

            /// <summary>与 <see cref="Ticks"/> 一一对应的输入按钮位。</summary>
            public IReadOnlyList<uint> Buttons => buttons;

            /// <summary>与 <see cref="Ticks"/> 一一对应的步长。</summary>
            public IReadOnlyList<float> DeltaTimes => deltaTimes;

            public void Step(in SimulationContext context)
            {
                ticks.Add(context.Tick);
                buttons.Add(context.Input.Buttons);
                deltaTimes.Add(context.DeltaTime);
            }
        }
    }
}
