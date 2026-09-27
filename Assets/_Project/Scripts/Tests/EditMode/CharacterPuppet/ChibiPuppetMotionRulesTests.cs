// 职责：锁定小人运动规则——静止、起步阈值、滞回停止、dt 非正、按剪辑地速标定的播放速率、跑走滞回、朝向死区与保持。
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
        private const float RunStart = 4f;
        private const float RunStop = 3.5f;
        private const float RateMin = 0.8f;
        private const float RateMax = 1.6f;

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
        public void PlaybackRate_AtClipGroundSpeed_IsOriginalSpeed()
        {
            // 走 3 单位/秒配走路剪辑地速 3、跑 5 单位/秒配奔跑剪辑地速 5：都按原速播放，不快放。
            Assert.That(ChibiPuppetMotionRules.PlaybackRate(3f, 3f, RateMin, RateMax), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(ChibiPuppetMotionRules.PlaybackRate(5f, 5f, RateMin, RateMax), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(ChibiPuppetMotionRules.PlaybackRate(3.6f, 3f, RateMin, RateMax), Is.EqualTo(1.2f).Within(1e-4f));
        }

        [Test]
        public void PlaybackRate_ClampsToRange()
        {
            // 潜行 1.5 / 走路地速 3 = 0.5 → 夹到下限；无 run 剪辑时跑 5 / 走路地速 3 ≈ 1.67 → 夹到上限。
            Assert.That(ChibiPuppetMotionRules.PlaybackRate(1.5f, 3f, RateMin, RateMax), Is.EqualTo(RateMin));
            Assert.That(ChibiPuppetMotionRules.PlaybackRate(5f, 3f, RateMin, RateMax), Is.EqualTo(RateMax));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void PlaybackRate_WhenClipSpeedNotPositive_FallsBackToOriginalSpeed(float clipSpeed)
        {
            Assert.That(ChibiPuppetMotionRules.PlaybackRate(5f, clipSpeed, RateMin, RateMax), Is.EqualTo(1f));
        }

        [Test]
        public void ResolveRunning_WhenWalking_SwitchesOnlyAtRunStart()
        {
            Assert.That(ChibiPuppetMotionRules.ResolveRunning(3f, RunStart, RunStop, false, true), Is.False);
            Assert.That(ChibiPuppetMotionRules.ResolveRunning(3.9f, RunStart, RunStop, false, true), Is.False);
            Assert.That(ChibiPuppetMotionRules.ResolveRunning(4f, RunStart, RunStop, false, true), Is.True);
            Assert.That(ChibiPuppetMotionRules.ResolveRunning(5f, RunStart, RunStop, false, true), Is.True);
        }

        [Test]
        public void ResolveRunning_WhenRunning_KeepsRunningAboveRunStop()
        {
            // 3.8：低于切跑阈值、高于切回阈值，跑动中保持奔跑（滞回）。
            Assert.That(ChibiPuppetMotionRules.ResolveRunning(3.8f, RunStart, RunStop, true, true), Is.True);
            Assert.That(ChibiPuppetMotionRules.ResolveRunning(3.5f, RunStart, RunStop, true, true), Is.False);
            Assert.That(ChibiPuppetMotionRules.ResolveRunning(3f, RunStart, RunStop, true, true), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResolveRunning_WithoutRunClip_NeverRuns(bool wasRunning)
        {
            Assert.That(ChibiPuppetMotionRules.ResolveRunning(5f, RunStart, RunStop, wasRunning, false), Is.False);
            Assert.That(ChibiPuppetMotionRules.ResolveRunning(100f, RunStart, RunStop, wasRunning, false), Is.False);
        }
    }
}
