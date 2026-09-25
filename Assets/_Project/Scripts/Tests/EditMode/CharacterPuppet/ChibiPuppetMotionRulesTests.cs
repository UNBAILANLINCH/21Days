// 职责：锁定拼接小人运动规则——静止、起步阈值、滞回停止、dt 非正、走路速率夹取、朝向死区与保持。
// 新建原因：CharacterPuppet 是新模块，按「被测类 + Tests」单独成文件。
using Game.CharacterPuppet;
using NUnit.Framework;

namespace Game.Tests.EditMode.CharacterPuppet
{
    public sealed class ChibiPuppetMotionRulesTests
    {
        private const float Start = 0.15f;
        private const float Stop = 0.05f;
        private const float Dt = 0.02f;

        [Test]
        public void Evaluate_WhenNoDisplacement_StaysIdleWithZeroSpeed()
        {
            float speed;
            Assert.That(ChibiPuppetMotionRules.Evaluate(0f, Dt, Start, Stop, false, out speed), Is.False);
            Assert.That(speed, Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_WhenIdle_StartsOnlyAtStartThreshold()
        {
            float speed;
            // 0.1 单位/秒：高于停步阈值、低于起步阈值，静止时不起步。
            Assert.That(ChibiPuppetMotionRules.Evaluate(0.1f * Dt, Dt, Start, Stop, false, out speed), Is.False);
            Assert.That(speed, Is.EqualTo(0.1f).Within(1e-4f));
            // 1.5 单位/秒：起步。
            Assert.That(ChibiPuppetMotionRules.Evaluate(1.5f * Dt, Dt, Start, Stop, false, out speed), Is.True);
            Assert.That(speed, Is.EqualTo(1.5f).Within(1e-4f));
        }

        [Test]
        public void Evaluate_WhenMoving_KeepsWalkingAboveStopThreshold()
        {
            float speed;
            // 0.1 单位/秒：低于起步但高于停步，走动中保持走路（滞回）。
            Assert.That(ChibiPuppetMotionRules.Evaluate(0.1f * Dt, Dt, Start, Stop, true, out speed), Is.True);
        }

        [Test]
        public void Evaluate_WhenMoving_StopsAtOrBelowStopThreshold()
        {
            float speed;
            Assert.That(ChibiPuppetMotionRules.Evaluate(0.04f * Dt, Dt, Start, Stop, true, out speed), Is.False);
            Assert.That(ChibiPuppetMotionRules.Evaluate(0f, Dt, Start, Stop, true, out speed), Is.False);
        }

        [TestCase(0f)]
        [TestCase(-0.016f)]
        public void Evaluate_WhenDeltaTimeNotPositive_TreatedAsIdle(float dt)
        {
            float speed;
            Assert.That(ChibiPuppetMotionRules.Evaluate(1f, dt, Start, Stop, true, out speed), Is.False);
            Assert.That(speed, Is.EqualTo(0f));
        }

        [Test]
        public void ResolveFacing_InsideDeadZone_KeepsPreviousFacing()
        {
            Assert.That(ChibiPuppetMotionRules.ResolveFacing(0.005f, 0.01f, true), Is.True);
            Assert.That(ChibiPuppetMotionRules.ResolveFacing(-0.005f, 0.01f, false), Is.False);
            Assert.That(ChibiPuppetMotionRules.ResolveFacing(0.01f, 0.01f, true), Is.True);
        }

        [Test]
        public void ResolveFacing_OutsideDeadZone_FollowsSign()
        {
            Assert.That(ChibiPuppetMotionRules.ResolveFacing(-0.5f, 0.01f, false), Is.True);
            Assert.That(ChibiPuppetMotionRules.ResolveFacing(0.5f, 0.01f, true), Is.False);
        }

        [Test]
        public void WalkPlaybackRate_ClampsToRange()
        {
            Assert.That(ChibiPuppetMotionRules.WalkPlaybackRate(0.2f, 0.8f, 0.8f, 1.6f), Is.EqualTo(0.8f));
            Assert.That(ChibiPuppetMotionRules.WalkPlaybackRate(1.5f, 0.8f, 0.8f, 1.6f), Is.EqualTo(1.2f).Within(1e-4f));
            Assert.That(ChibiPuppetMotionRules.WalkPlaybackRate(5f, 0.8f, 0.8f, 1.6f), Is.EqualTo(1.6f));
        }
    }
}
