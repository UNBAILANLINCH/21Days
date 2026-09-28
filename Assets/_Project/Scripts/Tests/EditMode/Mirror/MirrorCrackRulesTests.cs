// 职责：钉住 MirrorCrackRules——击中裂痕 0..3 与夹取、作用距离 / 可见范围随裂痕缩减并以 0 为底、
//   剧情裂痕只缩范围不致碎（PRD V8 V9 规则侧）。
// 为什么新建：一个被测类一个测试类；纯规则不经容器。
using Game.Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorCrackRules"/> 的 EditMode 测试。</summary>
    public sealed class MirrorCrackRulesTests
    {
        [TestCase(3, 3, 0)]
        [TestCase(3, 2, 1)]
        [TestCase(3, 1, 2)]
        [TestCase(3, 0, 3)]
        public void HitCracks_ByHealth_CountsDamageTaken(int maxHealth, int health, int expected)
        {
            Assert.That(MirrorCrackRules.HitCracks(maxHealth, health), Is.EqualTo(expected));
        }

        [Test]
        public void HitCracks_OutOfRange_ClampsToZeroAndThree()
        {
            Assert.That(MirrorCrackRules.HitCracks(3, -2), Is.EqualTo(3), "生命扣成负数也只算三裂");
            Assert.That(MirrorCrackRules.HitCracks(3, 5), Is.EqualTo(0), "生命高于上限不出现负裂痕");
            Assert.That(MirrorCrackRules.HitCracks(6, 0), Is.EqualTo(MirrorCrackRules.MaxHitCracks));
        }

        [Test]
        public void IsShattered_OnlyAtThreeHitCracks()
        {
            Assert.That(MirrorCrackRules.IsShattered(0), Is.False);
            Assert.That(MirrorCrackRules.IsShattered(2), Is.False);
            Assert.That(MirrorCrackRules.IsShattered(3), Is.True);
        }

        [Test]
        public void EffectiveRange_ShrinksPerCrack()
        {
            Assert.That(MirrorCrackRules.EffectiveRange(5f, 1.2f, 0, 0), Is.EqualTo(5f).Within(1e-4f));
            Assert.That(MirrorCrackRules.EffectiveRange(5f, 1.2f, 1, 0), Is.EqualTo(3.8f).Within(1e-4f));
            Assert.That(MirrorCrackRules.EffectiveRange(5f, 1.2f, 2, 0), Is.EqualTo(2.6f).Within(1e-4f));
        }

        [Test]
        public void EffectiveRange_NeverBelowZero()
        {
            Assert.That(MirrorCrackRules.EffectiveRange(5f, 1.2f, 3, 5), Is.EqualTo(0f));
        }

        [Test]
        public void EffectiveRange_NegativeCracks_TreatedAsZero()
        {
            Assert.That(MirrorCrackRules.EffectiveRange(5f, 1.2f, -1, -4), Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void StoryCracks_ShrinkRangeAndVisionButDoNotShatter()
        {
            float range = MirrorCrackRules.EffectiveRange(5f, 1.2f, 0, 3);
            float vision = MirrorCrackRules.VisionRadius(1f, 0.18f, 0, 3);
            int hitCracks = MirrorCrackRules.HitCracks(3, 3);

            Assert.That(range, Is.LessThan(5f));
            Assert.That(vision, Is.LessThan(1f));
            Assert.That(MirrorCrackRules.IsShattered(hitCracks), Is.False, "剧情裂痕不计入三裂");
        }

        [Test]
        public void VisionRadius_ShrinksPerCrackAndFloorsAtZero()
        {
            Assert.That(MirrorCrackRules.VisionRadius(1f, 0.18f, 1, 1), Is.EqualTo(0.64f).Within(1e-4f));
            Assert.That(MirrorCrackRules.VisionRadius(1f, 0.18f, 3, 10), Is.EqualTo(0f));
        }

        [Test]
        public void ConfigOverloads_MatchValueOverloads()
        {
            MirrorConfig config = ScriptableObject.CreateInstance<MirrorConfig>();
            try
            {
                Assert.That(MirrorCrackRules.EffectiveRange(config, 1, 1),
                    Is.EqualTo(MirrorCrackRules.EffectiveRange(config.BaseRange, config.RangeLossPerCrack, 1, 1)));
                Assert.That(MirrorCrackRules.VisionRadius(config, 2, 0),
                    Is.EqualTo(MirrorCrackRules.VisionRadius(config.VisionBaseRadius, config.VisionLossPerCrack, 2, 0)));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ConfigOverloads_NullConfig_FallBackWithoutThrowing()
        {
            Assert.That(MirrorCrackRules.EffectiveRange(null, 0, 0), Is.EqualTo(0f));
            Assert.That(MirrorCrackRules.VisionRadius(null, 0, 0), Is.EqualTo(1f));
        }
    }
}
