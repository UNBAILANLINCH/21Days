using System;
using Game.LailaFaceRecognition;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.LailaFaceRecognition
{
    // 职责：验证展示规则边界与时序，旧拒识测试继续验证历史策略，不改弱断言。
    public sealed class LailaExpressionFeedbackTests
    {
        private LailaExpressionFeedbackConfig config;
        private LailaExpressionFeedback feedback;
        private readonly float[] nearTieHappy = { .19f, .22f, .21f, .19f, .19f };
        private readonly float[] nearTieSad = { .19f, .21f, .22f, .19f, .19f };
        [SetUp] public void SetUp() { config = ScriptableObject.CreateInstance<LailaExpressionFeedbackConfig>(); feedback = new LailaExpressionFeedback(config); }
        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(config); }
        [Test] public void Neutral_ZeroAndHysteresis_DoNotUseConfidence()
        {
            var axes = new float[17];
            feedback.Observe(axes, nearTieSad, 0); Assert.That(feedback.DisplayIndex, Is.Zero); Assert.That(feedback.NeutralOverride, Is.True);
            axes[0] = config.Tolerance(0) * 1.25f; feedback.Observe(axes, nearTieSad, .1);
            Assert.That(feedback.NeutralOverride, Is.True);
            axes[0] = config.Tolerance(0) * 1.6f; feedback.Observe(axes, nearTieSad, .2);
            Assert.That(feedback.NeutralOverride, Is.False); feedback.Advance(.5); Assert.That(feedback.DisplayIndex, Is.EqualTo(2));
            axes[0] = config.Tolerance(0) * 1.25f; feedback.Observe(axes, nearTieSad, .6); Assert.That(feedback.NeutralOverride, Is.False);
            axes[0] = config.Tolerance(0); feedback.Observe(axes, nearTieSad, .7); Assert.That(feedback.DisplayIndex, Is.Zero);
        }
        [Test] public void SignificantSingleAxis_AndOppositeSides_AreNotDilutedOrCancelled()
        {
            var axes = new float[17]; axes[0] = .3f; axes[3] = -.3f;
            feedback.Observe(axes, nearTieSad, 0);
            Assert.That(feedback.NeutralOverride, Is.False); Assert.That(feedback.DisplayIndex, Is.EqualTo(2));
        }
        [Test] public void CloseScores_JitterDoesNotSwitch_ButStationaryCandidateSettles()
        {
            var axes = new float[17]; axes[0] = .3f;
            feedback.Observe(axes, nearTieHappy, 0);
            axes[0] += .001f; feedback.Observe(axes, nearTieSad, .1); Assert.That(feedback.DisplayIndex, Is.EqualTo(1));
            feedback.Observe(axes, nearTieHappy, .15); feedback.Observe(axes, nearTieSad, .2);
            Assert.That(feedback.Advance(.3), Is.False); Assert.That(feedback.Advance(.4), Is.True);
            Assert.That(feedback.DisplayIndex, Is.EqualTo(2));
        }
        [Test] public void StrongInputOrClearDominance_SwitchesImmediately_AndResetClearsTiming()
        {
            var axes = new float[17]; axes[0] = .3f; feedback.Observe(axes, nearTieHappy, 0);
            axes[0] = .6f; feedback.Observe(axes, nearTieSad, .01); Assert.That(feedback.DisplayIndex, Is.EqualTo(2));
            feedback.Observe(axes, new[] { .02f, .9f, .03f, .03f, .02f }, .02); Assert.That(feedback.DisplayIndex, Is.EqualTo(1));
            feedback.Reset(); Assert.That(feedback.DisplayIndex, Is.EqualTo(-1)); Assert.That(feedback.Advance(0), Is.False);
            feedback.Observe(new float[17], nearTieHappy, 0); Assert.That(feedback.DisplayIndex, Is.Zero);
        }
        [Test] public void LowScoresAndHistoricalRejection_DoNotChangeFiveClassCompetition()
        {
            Assert.That(LailaExpressionRecognizer.Decide(nearTieSad, 10, -2, .4f), Is.EqualTo(-1));
            Assert.That(LailaExpressionRecognizer.HighestClass(nearTieSad, 10), Is.EqualTo(2));
            var axes = new float[17]; axes[0] = .3f; feedback.Observe(axes, nearTieSad, 0);
            Assert.That(feedback.DisplayIndex, Is.EqualTo(2)); Assert.That(feedback.NeutralOverride, Is.False);
            feedback.Observe(axes, new[] { .6f, .1f, .1f, .1f, .1f }, .1); Assert.That(feedback.DisplayIndex, Is.Zero); Assert.That(feedback.NeutralOverride, Is.False);
        }
        [Test] public void InvalidOutputsAndAxes_AreErrors()
        {
            Assert.Throws<InvalidOperationException>(() => feedback.Observe(new float[16], nearTieHappy, 0));
            var axes = new float[17]; axes[16] = float.NaN;
            Assert.Throws<InvalidOperationException>(() => feedback.Observe(axes, nearTieHappy, 0));
            Assert.Throws<InvalidOperationException>(() => LailaExpressionRecognizer.HighestClass(new float[5], 0));
            Assert.Throws<InvalidOperationException>(() => LailaExpressionRecognizer.HighestClass(nearTieHappy, float.PositiveInfinity));
            Assert.Throws<InvalidOperationException>(() => LailaExpressionRecognizer.HighestClass(new[] {float.NaN,0f,0f,0f,1f},0));
        }
    }
}
