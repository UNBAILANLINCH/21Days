// 职责：钉住 PerformancePolicy 的构造校验（长按秒数必须是大于 0 的有限数；「自动」间隔必须是不小于 0 的有限数）与字段透传，
//   以及 PerformanceConfig.BuildPolicy 把「自动」间隔带进策略、资产非法值按默认兜底；连点补全两参数默认值与对白一致、非法值兜底。
// 为什么新建：Performance 模块首次落地（PRP/performance-pipeline 波 1），一个被测类一个测试类。
using System;
using Game.Dialogue;
using Game.Performance;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformancePolicyTests
    {
        [Test]
        public void Constructor_ValidValues_KeepsAllFields()
        {
            var policy = new PerformancePolicy(false, 1.5f, true, false);

            Assert.That(policy.Skippable, Is.False);
            Assert.That(policy.SkipHoldSeconds, Is.EqualTo(1.5f));
            Assert.That(policy.PauseWorld, Is.True);
            Assert.That(policy.HideHud, Is.False);
            Assert.That(policy.IsValid, Is.True);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_InvalidHoldSeconds_Throws(float seconds)
        {
            Assert.Throws<ArgumentException>(() => new PerformancePolicy(true, seconds, true, true));
        }

        [Test]
        public void Default_IsNotValid()
        {
            PerformancePolicy policy = default;

            Assert.That(policy.IsValid, Is.False);
        }

        [Test]
        public void Constructor_WithoutAutoSeconds_UsesDefault()
        {
            var policy = new PerformancePolicy(true, 1f, true, true);

            Assert.That(policy.AutoAdvanceSeconds, Is.EqualTo(PerformancePolicy.DefaultAutoAdvanceSeconds));
            Assert.That(PerformancePolicy.DefaultAutoAdvanceSeconds, Is.EqualTo(1.5f), "与对白的自动间隔默认值一致");
        }

        [TestCase(0f)]
        [TestCase(2.5f)]
        public void Constructor_ValidAutoSeconds_KeepsValue(float seconds)
        {
            var policy = new PerformancePolicy(true, 1f, true, true, seconds);

            Assert.That(policy.AutoAdvanceSeconds, Is.EqualTo(seconds));
        }

        [TestCase(-0.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_InvalidAutoSeconds_Throws(float seconds)
        {
            Assert.Throws<ArgumentException>(() => new PerformancePolicy(true, 1f, true, true, seconds));
        }

        [Test]
        public void ConfigBuildPolicy_CarriesAutoSeconds_AndFallsBackOnInvalidAsset()
        {
            var config = ScriptableObject.CreateInstance<PerformanceConfig>();
            try
            {
                Assert.That(config.BuildPolicy(true, true, true).AutoAdvanceSeconds, Is.EqualTo(1.5f), "配置默认 1.5 秒");

                SetAutoSeconds(config, 0.4f);
                Assert.That(config.BuildPolicy(true, true, true).AutoAdvanceSeconds, Is.EqualTo(0.4f).Within(1e-5f));

                SetAutoSeconds(config, -1f);
                Assert.That(config.AutoAdvanceSeconds, Is.EqualTo(PerformancePolicy.DefaultAutoAdvanceSeconds), "资产里的负数按默认兜底");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ConfigTapReveal_DefaultsMatchDialogue_AndFallsBackOnInvalidAsset()
        {
            var config = ScriptableObject.CreateInstance<PerformanceConfig>();
            var dialogue = ScriptableObject.CreateInstance<DialogueConfig>();
            try
            {
                Assert.That(config.RevealTapCount, Is.EqualTo(dialogue.RevealTapCount), "连点次数默认值与对白一致");
                Assert.That(config.TapWindowSeconds, Is.EqualTo(dialogue.TapWindowSeconds), "连点窗口默认值与对白一致");
                Assert.That(config.RevealTapCount, Is.EqualTo(PerformanceConfig.DefaultRevealTapCount));
                Assert.That(config.TapWindowSeconds, Is.EqualTo(PerformanceConfig.DefaultTapWindowSeconds));

                using (var so = new SerializedObject(config))
                {
                    so.FindProperty("revealTapCount").intValue = 0;
                    so.FindProperty("tapWindowSeconds").floatValue = -1f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                Assert.That(config.RevealTapCount, Is.EqualTo(PerformanceConfig.DefaultRevealTapCount), "资产里小于 1 的次数按默认兜底");
                Assert.That(config.TapWindowSeconds, Is.EqualTo(PerformanceConfig.DefaultTapWindowSeconds), "资产里非正的窗口按默认兜底");

                using (var so = new SerializedObject(config))
                {
                    so.FindProperty("revealTapCount").intValue = 1;
                    so.FindProperty("tapWindowSeconds").floatValue = 0.25f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                Assert.That(config.RevealTapCount, Is.EqualTo(1), "合法值原样透传");
                Assert.That(config.TapWindowSeconds, Is.EqualTo(0.25f));
            }
            finally
            {
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(dialogue);
            }
        }

        private static void SetAutoSeconds(PerformanceConfig config, float seconds)
        {
            using (var so = new SerializedObject(config))
            {
                so.FindProperty("autoAdvanceSeconds").floatValue = seconds;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
