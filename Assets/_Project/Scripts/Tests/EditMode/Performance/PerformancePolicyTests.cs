// 职责：钉住 PerformancePolicy 的构造校验（长按秒数必须是大于 0 的有限数）与字段透传。
// 为什么新建：Performance 模块首次落地（PRP/performance-pipeline 波 1），一个被测类一个测试类。
using System;
using Game.Performance;
using NUnit.Framework;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformancePolicyTests
    {
        [Test]
        public void Constructor_ValidValues_KeepsAllFields()
        {
            var policy = new PerformancePolicy(false, 1.5f, true, false, true);

            Assert.That(policy.Skippable, Is.False);
            Assert.That(policy.SkipHoldSeconds, Is.EqualTo(1.5f));
            Assert.That(policy.PauseWorld, Is.True);
            Assert.That(policy.HideHud, Is.False);
            Assert.That(policy.Letterbox, Is.True);
            Assert.That(policy.IsValid, Is.True);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_InvalidHoldSeconds_Throws(float seconds)
        {
            Assert.Throws<ArgumentException>(() => new PerformancePolicy(true, seconds, true, true, true));
        }

        [Test]
        public void Default_IsNotValid()
        {
            PerformancePolicy policy = default;

            Assert.That(policy.IsValid, Is.False);
        }
    }
}
