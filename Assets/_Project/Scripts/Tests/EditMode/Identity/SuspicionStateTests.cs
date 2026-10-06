// 职责：钉住怀疑度——累加封顶、两种口径（只增 / 可回落）都试一遍、负值拒绝、档位三档互斥。
// 为什么新建：「会不会自然回落」是原文没写的待定项（`02_身份暴露与怀疑.md:127` R26），
//   两种口径都在代码里，必须各有用例，否则开关一拨没人知道另一边还在不在。

using System;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using static Game.Tests.EditMode.Identity.IdentityTestContent;

namespace Game.Tests.EditMode.Identity
{
    public sealed class SuspicionStateTests
    {
        [Test]
        public void Add_AccumulatesAndClampsAtLimit()
        {
            var suspicion = new SuspicionState(10f, false, 0f);

            suspicion.Add(4f);
            suspicion.Add(4f);
            Assert.That(suspicion.Value, Is.EqualTo(8f).Within(0.001f));
            Assert.That(suspicion.IsAtLimit, Is.False);

            suspicion.Add(100f);
            Assert.That(suspicion.Value, Is.EqualTo(10f).Within(0.001f), "封顶不溢出");
            Assert.That(suspicion.IsAtLimit, Is.True);
        }

        [Test]
        public void IsAtLimit_NonPositiveLimit_DisablesJudgement()
        {
            var suspicion = new SuspicionState(0f, false, 0f);

            suspicion.Add(50f);

            Assert.That(suspicion.IsAtLimit, Is.False, "上限 ≤ 0 = 关闭判定");
        }

        /// <summary>只增口径：开关关掉时 <see cref="SuspicionState.Advance"/> 一点不动。</summary>
        [Test]
        public void Advance_WhenDecayDisabled_KeepsValue()
        {
            var suspicion = new SuspicionState(100f, false, 5f);
            suspicion.Add(30f);

            bool changed = suspicion.Advance(10f);

            Assert.That(changed, Is.False);
            Assert.That(suspicion.Value, Is.EqualTo(30f).Within(0.001f));
        }

        /// <summary>可回落口径：按速率下降，且不会落到负数。</summary>
        [Test]
        public void Advance_WhenDecayEnabled_FallsBackAndStopsAtZero()
        {
            var suspicion = new SuspicionState(100f, true, 5f);
            suspicion.Add(12f);

            Assert.That(suspicion.Advance(1f), Is.True);
            Assert.That(suspicion.Value, Is.EqualTo(7f).Within(0.001f));

            Assert.That(suspicion.Advance(10f), Is.True);
            Assert.That(suspicion.Value, Is.Zero);
            Assert.That(suspicion.Advance(1f), Is.False, "已经是 0，不再变化");
        }

        [Test]
        public void Add_NegativeAmount_Throws()
        {
            var suspicion = new SuspicionState(100f, true, 5f);

            Assert.Throws<ArgumentOutOfRangeException>(() => suspicion.Add(-1f),
                "回落只能走 Advance，否则等于绕过「可回落」开关");
        }

        [Test]
        public void Advance_NegativeDeltaTime_Throws()
        {
            var suspicion = new SuspicionState(100f, true, 5f);

            Assert.Throws<ArgumentOutOfRangeException>(() => suspicion.Advance(-0.1f));
        }

        /// <summary>档位互斥：同一时刻只有一个档为真，未达最低档时三档都不为真。</summary>
        [Test]
        public void Tier_CrossesThresholds_OneAtATime()
        {
            IdentitySettings settings = NewSettings();
            var suspicion = SuspicionState.FromSettings(settings);

            Assert.That(suspicion.Tier, Is.EqualTo(FactTier.None));
            suspicion.Add(settings.SuspicionLowThreshold);
            Assert.That(suspicion.Tier, Is.EqualTo(FactTier.Low));
            suspicion.Add(settings.SuspicionMidThreshold - settings.SuspicionLowThreshold);
            Assert.That(suspicion.Tier, Is.EqualTo(FactTier.Mid));
            suspicion.Add(settings.SuspicionHighThreshold - settings.SuspicionMidThreshold);
            Assert.That(suspicion.Tier, Is.EqualTo(FactTier.High));

            Assert.That(FactTierMath.TierOf(0f, 0f, 0f, 0f), Is.EqualTo(FactTier.None), "阈值全 0 = 三档都不启用");
        }

        /// <summary>埋点：档位每跨一次记一条（有门面时），同档内反复加不重复记。</summary>
        [Test]
        public void TierChanges_AreTrackedOncePerCrossing()
        {
            IdentitySettings settings = NewSettings();
            settings.SuspicionLimit = 1000f;
            var clock = new FakeTelemetryClock();
            var sink = new RecordingTelemetrySink();
            var service = new TelemetryService(TelemetryOptions.Default, clock, sink);
            try
            {
                var suspicion = new SuspicionState(settings, service.Scope("identity"));

                suspicion.Add(settings.SuspicionLowThreshold);
                suspicion.Add(1f);
                suspicion.Add(1f);
                suspicion.Add(settings.SuspicionHighThreshold);

                int tierLines = 0;
                foreach (string line in sink.Lines)
                {
                    if (line.Contains("identity/suspicion_tier"))
                    {
                        tierLines++;
                    }
                }

                Assert.That(tierLines, Is.EqualTo(2), "低档一次、高档一次；同档内的加值不再记");
                Assert.That(string.Join("\n", sink.Lines), Does.Contain("high"));
            }
            finally
            {
                service.Dispose();
            }
        }
    }
}
