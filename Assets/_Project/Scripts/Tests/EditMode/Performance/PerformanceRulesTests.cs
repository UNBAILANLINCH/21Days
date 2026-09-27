// 职责：钉住 PerformanceRules 的阶段机——阶段迁移、只在 Holding 能确认、长按累计与松手归零、不可跳过、重入、结果归类、计时。
// 为什么新建：Performance 模块首次落地（PRP/performance-pipeline 波 1），一个被测类一个测试类。
using System;
using Game.Core.Telemetry;
using Game.Performance;
using NUnit.Framework;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformanceRulesTests
    {
        private const string Id = "perf_test";
        private PerformanceRules rules;

        [SetUp]
        public void SetUp() => rules = new PerformanceRules(NullTelemetryScope.Instance);

        private static PerformancePolicy Policy(bool skippable = true, float holdSeconds = 1f) =>
            new PerformancePolicy(skippable, holdSeconds, true, true, true);

        [Test]
        public void New_IsIdleWithoutOutcome()
        {
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Idle));
            Assert.That(rules.Outcome, Is.Null);
            Assert.That(rules.Id, Is.Null);
        }

        [Test]
        public void Start_FromIdle_EntersPlaying()
        {
            rules.Start(Id, Policy());

            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Playing));
            Assert.That(rules.Id, Is.EqualTo(Id));
            Assert.That(rules.Outcome, Is.Null);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Start_EmptyId_Throws(string id)
        {
            Assert.Throws<ArgumentException>(() => rules.Start(id, Policy()));
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Idle));
        }

        [Test]
        public void Start_DefaultPolicy_Throws()
        {
            Assert.Throws<ArgumentException>(() => rules.Start(Id, default));
        }

        [Test]
        public void Start_WhilePlaying_ThrowsInvalidOperation()
        {
            rules.Start(Id, Policy());

            Assert.Throws<InvalidOperationException>(() => rules.Start("perf_other", Policy()));
            Assert.That(rules.Id, Is.EqualTo(Id));
        }

        [Test]
        public void Start_WhileHolding_ThrowsInvalidOperation()
        {
            rules.Start(Id, Policy());
            rules.EnterHold();

            Assert.Throws<InvalidOperationException>(() => rules.Start("perf_other", Policy()));
        }

        [Test]
        public void Start_AfterFinished_ResetsCounters()
        {
            rules.Start(Id, Policy());
            rules.Tick(2f);
            rules.EnterHold();
            rules.Complete();

            rules.Start("perf_next", Policy());

            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Playing));
            Assert.That(rules.Outcome, Is.Null);
            Assert.That(rules.HoldCount, Is.EqualTo(0));
            Assert.That(rules.ElapsedSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void EnterHold_FromPlaying_EntersHoldingAndCounts()
        {
            rules.Start(Id, Policy());

            Assert.That(rules.EnterHold(), Is.True);
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Holding));
            Assert.That(rules.HoldCount, Is.EqualTo(1));
        }

        [Test]
        public void EnterHold_WhenNotPlaying_ReturnsFalse()
        {
            Assert.That(rules.EnterHold(), Is.False);
            rules.Start(Id, Policy());
            rules.EnterHold();
            Assert.That(rules.EnterHold(), Is.False);
            Assert.That(rules.HoldCount, Is.EqualTo(1));
        }

        [Test]
        public void Confirm_WhilePlaying_ReturnsFalse()
        {
            rules.Start(Id, Policy());

            Assert.That(rules.Confirm(), Is.False);
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Playing));
        }

        [Test]
        public void Confirm_WhileHolding_ReturnsToPlaying()
        {
            rules.Start(Id, Policy());
            rules.EnterHold();

            Assert.That(rules.Confirm(), Is.True);
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Playing));
        }

        [Test]
        public void Confirm_WhenIdleOrFinished_ReturnsFalse()
        {
            Assert.That(rules.Confirm(), Is.False);
            rules.Start(Id, Policy());
            rules.Complete();
            Assert.That(rules.Confirm(), Is.False);
        }

        [Test]
        public void Hold_Twice_CountsBoth()
        {
            rules.Start(Id, Policy());
            rules.EnterHold();
            rules.Confirm();
            rules.EnterHold();

            Assert.That(rules.HoldCount, Is.EqualTo(2));
        }

        [Test]
        public void Complete_FromPlaying_FinishesCompleted()
        {
            rules.Start(Id, Policy());

            Assert.That(rules.Complete(), Is.True);
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Finished));
            Assert.That(rules.Outcome, Is.EqualTo(PerformanceOutcome.Completed));
        }

        [Test]
        public void Complete_AfterFinished_ReturnsFalseAndKeepsOutcome()
        {
            rules.Start(Id, Policy());
            rules.Skip();

            Assert.That(rules.Complete(), Is.False);
            Assert.That(rules.Outcome, Is.EqualTo(PerformanceOutcome.Skipped));
        }

        [Test]
        public void Complete_WhenIdle_ReturnsFalse()
        {
            Assert.That(rules.Complete(), Is.False);
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Idle));
        }

        [Test]
        public void Cancel_WhileHolding_FinishesCancelled()
        {
            rules.Start(Id, Policy());
            rules.EnterHold();

            Assert.That(rules.Cancel(), Is.True);
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Finished));
            Assert.That(rules.Outcome, Is.EqualTo(PerformanceOutcome.Cancelled));
        }

        [Test]
        public void Fail_WhilePlaying_FinishesFailed()
        {
            rules.Start(Id, Policy());

            Assert.That(rules.Fail(), Is.True);
            Assert.That(rules.Outcome, Is.EqualTo(PerformanceOutcome.Failed));
        }

        [Test]
        public void CancelAndFail_WhenIdle_ReturnFalse()
        {
            Assert.That(rules.Cancel(), Is.False);
            Assert.That(rules.Fail(), Is.False);
            Assert.That(rules.Outcome, Is.Null);
        }

        [Test]
        public void Skip_NotSkippablePolicy_StillSkips()
        {
            // 策略的 Skippable 只约束玩家长按；代码直接 Skip 仍然生效。
            rules.Start(Id, Policy(skippable: false));

            Assert.That(rules.Skip(), Is.True);
            Assert.That(rules.Outcome, Is.EqualTo(PerformanceOutcome.Skipped));
        }

        [Test]
        public void TickSkip_HeldUntilThreshold_FinishesSkipped()
        {
            rules.Start(Id, Policy(holdSeconds: 1f));

            Assert.That(rules.TickSkip(true, 0.4f), Is.False);
            Assert.That(rules.TickSkip(true, 0.4f), Is.False);
            Assert.That(rules.SkipProgress, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(rules.TickSkip(true, 0.4f), Is.True);

            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Finished));
            Assert.That(rules.Outcome, Is.EqualTo(PerformanceOutcome.Skipped));
        }

        [Test]
        public void TickSkip_AfterTriggered_ReturnsFalse()
        {
            rules.Start(Id, Policy(holdSeconds: 0.5f));
            rules.TickSkip(true, 1f);

            Assert.That(rules.TickSkip(true, 1f), Is.False);
            Assert.That(rules.SkipProgress, Is.EqualTo(0f));
        }

        [Test]
        public void TickSkip_Released_ResetsProgress()
        {
            rules.Start(Id, Policy(holdSeconds: 1f));
            rules.TickSkip(true, 0.9f);

            Assert.That(rules.TickSkip(false, 0.1f), Is.False);
            Assert.That(rules.SkipProgress, Is.EqualTo(0f));
            // 松手后重新按，要重新累计满 1 秒。
            Assert.That(rules.TickSkip(true, 0.5f), Is.False);
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Playing));
        }

        [Test]
        public void TickSkip_NotSkippable_NeverFires()
        {
            rules.Start(Id, Policy(skippable: false, holdSeconds: 0.1f));

            for (int i = 0; i < 10; i++) Assert.That(rules.TickSkip(true, 1f), Is.False);
            Assert.That(rules.SkipProgress, Is.EqualTo(0f));
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Playing));
        }

        [Test]
        public void TickSkip_WhileHolding_CanSkip()
        {
            rules.Start(Id, Policy(holdSeconds: 0.5f));
            rules.EnterHold();

            Assert.That(rules.TickSkip(true, 0.6f), Is.True);
            Assert.That(rules.Outcome, Is.EqualTo(PerformanceOutcome.Skipped));
        }

        [Test]
        public void TickSkip_WhenIdle_ReturnsFalse()
        {
            Assert.That(rules.TickSkip(true, 5f), Is.False);
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Idle));
        }

        [Test]
        public void SkipProgress_IsRatioOfHoldSeconds()
        {
            rules.Start(Id, Policy(holdSeconds: 2f));
            rules.TickSkip(true, 1f);

            Assert.That(rules.SkipProgress, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Tick_AccumulatesOnlyWhileActive()
        {
            rules.Tick(1f);
            Assert.That(rules.ElapsedSeconds, Is.EqualTo(0f));

            rules.Start(Id, Policy());
            rules.Tick(0.5f);
            rules.EnterHold();
            rules.Tick(0.25f);
            rules.Tick(-1f);
            rules.Complete();
            rules.Tick(3f);

            Assert.That(rules.ElapsedSeconds, Is.EqualTo(0.75f).Within(1e-4f));
        }

        [Test]
        public void TickSkip_DoesNotAccumulateElapsed()
        {
            rules.Start(Id, Policy(holdSeconds: 5f));
            rules.TickSkip(true, 1f);

            Assert.That(rules.ElapsedSeconds, Is.EqualTo(0f));
        }

        [TestCase(PerformanceOutcome.Completed, "completed")]
        [TestCase(PerformanceOutcome.Skipped, "skipped")]
        [TestCase(PerformanceOutcome.Cancelled, "cancelled")]
        [TestCase(PerformanceOutcome.Failed, "failed")]
        public void OutcomeName_IsStableLowercase(PerformanceOutcome outcome, string expected)
        {
            Assert.That(PerformanceRules.OutcomeName(outcome), Is.EqualTo(expected));
        }
    }
}
