// 职责：用假时钟覆盖 TimerService 的核心规则——到点触发、周期重复、取消、回调内再注册。
// 为什么新建：波 1 之前没有任何 Core 的测试文件；TimerService 的迭代安全逻辑是最容易回归的地方，
// 必须有一份不依赖帧循环的 EditMode 测试守着。

using System;
using Game.Core.Timing;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// TimerService 的 EditMode 测试。时间全由 FakeClock 推进，Tick 手动调用，
    /// 因此完全确定性——不会因为机器快慢而闪烁。
    /// </summary>
    public sealed class TimerServiceTests
    {
        private FakeClock clock;
        private TimerService timers;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock();
            timers = new TimerService(clock);
        }

        [TearDown]
        public void TearDown()
        {
            timers.Dispose();
        }

        [Test]
        public void Delay_BeforeDueTime_DoesNotInvokeCallback()
        {
            int calls = 0;
            timers.Delay(1f, () => calls++);

            clock.Advance(0.9f);
            timers.Tick();

            Assert.That(calls, Is.EqualTo(0));
        }

        [Test]
        public void Delay_WhenDueTimeReached_InvokesCallbackExactlyOnce()
        {
            int calls = 0;
            timers.Delay(1f, () => calls++);

            clock.Advance(1f);
            timers.Tick();
            Assert.That(calls, Is.EqualTo(1));

            clock.Advance(10f);
            timers.Tick();
            Assert.That(calls, Is.EqualTo(1), "Delay 只该触发一次");
            Assert.That(timers.ActiveCount, Is.EqualTo(0), "触发后槽位应已回收");
        }

        [Test]
        public void Interval_WhenPeriodsElapse_InvokesCallbackEveryPeriod()
        {
            int calls = 0;
            timers.Interval(1f, () => calls++);

            clock.Advance(1f);
            timers.Tick();
            Assert.That(calls, Is.EqualTo(1));

            clock.Advance(1f);
            timers.Tick();
            Assert.That(calls, Is.EqualTo(2));

            clock.Advance(0.5f);
            timers.Tick();
            Assert.That(calls, Is.EqualTo(2), "没到下一个周期不该触发");

            clock.Advance(0.5f);
            timers.Tick();
            Assert.That(calls, Is.EqualTo(3));
        }

        [Test]
        public void Dispose_OnHandle_CancelsPendingTimer()
        {
            int calls = 0;
            TimerHandle handle = timers.Delay(1f, () => calls++);
            Assert.That(handle.IsActive, Is.True);

            handle.Dispose();
            Assert.That(handle.IsActive, Is.False);

            clock.Advance(5f);
            timers.Tick();

            Assert.That(calls, Is.EqualTo(0));
        }

        [Test]
        public void Dispose_OnHandleInsideOwnCallback_StopsInterval()
        {
            int calls = 0;
            TimerHandle handle = default;
            handle = timers.Interval(1f, () =>
            {
                calls++;
                handle.Dispose();
            });

            clock.Advance(1f);
            timers.Tick();
            clock.Advance(5f);
            timers.Tick();

            Assert.That(calls, Is.EqualTo(1), "回调里自己取消后不该再触发");
        }

        [Test]
        public void Dispose_OnService_CancelsAllPendingTimers()
        {
            int calls = 0;
            timers.Delay(1f, () => calls++);
            timers.Interval(1f, () => calls++);

            timers.Dispose();
            clock.Advance(5f);
            timers.Tick();

            Assert.That(calls, Is.EqualTo(0));
            Assert.That(timers.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Delay_WhenCallbackRegistersAnotherTimer_NewTimerWaitsForNextTick()
        {
            int outerCalls = 0;
            int innerCalls = 0;
            timers.Delay(1f, () =>
            {
                outerCalls++;
                // 故意用 0 秒：没有「延迟变更」保护的话会在同一次 Tick 的遍历里被就地触发
                timers.Delay(0f, () => innerCalls++);
            });

            clock.Advance(1f);
            timers.Tick();
            Assert.That(outerCalls, Is.EqualTo(1));
            Assert.That(innerCalls, Is.EqualTo(0), "回调里新注册的定时器本帧不该参与遍历");

            timers.Tick();
            Assert.That(innerCalls, Is.EqualTo(1));
        }

        [Test]
        public void Delay_WhenUnscaledRequested_UsesUnscaledTime()
        {
            int scaledCalls = 0;
            int unscaledCalls = 0;
            timers.Delay(1f, () => scaledCalls++);
            timers.Delay(1f, () => unscaledCalls++, true);

            // 模拟 timeScale = 0：只有 unscaled 时间在走
            clock.AdvanceUnscaled(1f);
            timers.Tick();

            Assert.That(unscaledCalls, Is.EqualTo(1));
            Assert.That(scaledCalls, Is.EqualTo(0));
        }

        [Test]
        public void Interval_WithNonPositivePeriod_IsRejected()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("Interval"));

            int calls = 0;
            TimerHandle handle = timers.Interval(0f, () => calls++);

            clock.Advance(5f);
            timers.Tick();

            Assert.That(handle.IsActive, Is.False);
            Assert.That(calls, Is.EqualTo(0));
        }

        /// <summary>可手动推进的假时钟。scaled 与 unscaled 分开推，便于测 timeScale = 0 的场景。</summary>
        private sealed class FakeClock : IClock
        {
            public DateTime UtcNow { get; private set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public float GameTime { get; private set; }

            public float UnscaledTime { get; private set; }

            public float DeltaTime { get; private set; }

            public float UnscaledDeltaTime { get; private set; }

            /// <summary>scaled 与 unscaled 同步推进（等价于 timeScale = 1）。</summary>
            public void Advance(float seconds)
            {
                GameTime += seconds;
                DeltaTime = seconds;
                AdvanceUnscaled(seconds);
            }

            /// <summary>只推进 unscaled 时间（等价于 timeScale = 0）。</summary>
            public void AdvanceUnscaled(float seconds)
            {
                UnscaledTime += seconds;
                UnscaledDeltaTime = seconds;
                UtcNow = UtcNow.AddSeconds(seconds);
            }
        }
    }
}
