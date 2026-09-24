// 职责：钉住世界暂停服务的引用计数、timeScale 保存与恢复、令牌幂等、服务整体释放。
// 为什么新建：WorldPauseService 是新服务，没有现成测试可扩展；它改的是全局 Time.timeScale，
//   错了会让整局卡死或暂停失效，必须有 EditMode 测试守着。

using System;
using Game.Core.Simulation;
using Game.Core.Timing;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="WorldPauseService"/> 的 EditMode 测试。推进器用默认配置直接 new，不依赖场景与资产。
    /// timeScale 是全局状态：SetUp 记下、TearDown 还原，并把基线设成 0.5，
    /// 这样能区分「恢复原值」与「简单置 1」。
    /// </summary>
    public sealed class WorldPauseServiceTests
    {
        private const float BaselineTimeScale = 0.5f;

        private float originalTimeScale;
        private SimulationConfig config;
        private SimulationRunner runner;
        private WorldPauseService service;

        [SetUp]
        public void SetUp()
        {
            originalTimeScale = Time.timeScale;
            Time.timeScale = BaselineTimeScale;

            config = ScriptableObject.CreateInstance<SimulationConfig>();
            runner = new SimulationRunner(config, new FakeClock(), new FakeInputSource(), new RandomService(1UL), null);
            service = new WorldPauseService(runner);
        }

        [TearDown]
        public void TearDown()
        {
            service.Dispose();
            UnityEngine.Object.DestroyImmediate(config);
            Time.timeScale = originalTimeScale;
        }

        [Test]
        public void Acquire_WhenFirstOwner_PausesTimeScaleAndLogicTick()
        {
            service.Acquire(new object());

            Assert.That(service.IsPaused, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(runner.IsPaused, Is.True);
        }

        [Test]
        public void Dispose_WhenLastToken_RestoresOriginalTimeScale()
        {
            IDisposable token = service.Acquire(new object());

            token.Dispose();

            Assert.That(service.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(BaselineTimeScale), "应恢复进入暂停前的值，而不是置 1");
            Assert.That(runner.IsPaused, Is.False);
        }

        [Test]
        public void Dispose_WhenOtherOwnerStillHolds_StaysPaused()
        {
            IDisposable first = service.Acquire(new object());
            IDisposable second = service.Acquire(new object());

            first.Dispose();

            Assert.That(service.IsPaused, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(runner.IsPaused, Is.True);

            second.Dispose();

            Assert.That(service.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(BaselineTimeScale));
            Assert.That(runner.IsPaused, Is.False);
        }

        [Test]
        public void Acquire_WhenSameOwnerTwice_ReturnsSameTokenAndReleasesOnSingleDispose()
        {
            object owner = new object();
            IDisposable first = service.Acquire(owner);
            IDisposable second = service.Acquire(owner);

            Assert.That(second, Is.SameAs(first));

            first.Dispose();

            Assert.That(service.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(BaselineTimeScale));
        }

        [Test]
        public void TokenDispose_WhenCalledTwice_HasNoSideEffect()
        {
            IDisposable stale = service.Acquire(new object());
            IDisposable other = service.Acquire(new object());

            stale.Dispose();
            stale.Dispose();

            Assert.That(service.IsPaused, Is.True, "重复 Dispose 不应顺带释放别的持有者");
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            other.Dispose();
            Assert.That(Time.timeScale, Is.EqualTo(BaselineTimeScale));
        }

        [Test]
        public void Acquire_WhenOwnerIsNull_Throws()
        {
            Assert.That(() => service.Acquire(null), Throws.TypeOf<ArgumentNullException>());
            Assert.That(service.IsPaused, Is.False);
        }

        [Test]
        public void ServiceDispose_WhenHoldersRemain_ReleasesAllAndRestores()
        {
            IDisposable first = service.Acquire(new object());
            service.Acquire(new object());

            service.Dispose();

            Assert.That(service.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(BaselineTimeScale));
            Assert.That(runner.IsPaused, Is.False);

            // 服务释放后旧令牌再 Dispose 不应有任何副作用
            first.Dispose();
            Assert.That(Time.timeScale, Is.EqualTo(BaselineTimeScale));
        }

        /// <summary>推进器构造要一个渲染帧时钟；本测试不推进 tick，给常量即可。</summary>
        private sealed class FakeClock : IClock
        {
            public DateTime UtcNow => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public float GameTime => 0f;

            public float DeltaTime => 0f;

            public float UnscaledTime => 0f;

            public float UnscaledDeltaTime => 0f;
        }

        /// <summary>推进器构造要一个输入源；本测试不推进 tick，永远给默认命令。</summary>
        private sealed class FakeInputSource : IInputSource
        {
            public InputCommand Current => default;

            public void Sample(long tick)
            {
            }
        }
    }
}
