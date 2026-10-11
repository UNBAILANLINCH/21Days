// 职责：锁定小人运动规则——静止、起步阈值、滞回停止、dt 非正、按剪辑地速标定的播放速率、跑走滞回、朝向死区与保持；
//   以及门面表现的纯数学：转身补间（时长折算、插值、经过 0、中途改向不跳变）、朝向指定点左右判定、朝向保持、交互挤压回弹曲线。
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

        // ───────────────────────── 转身补间 ─────────────────────────

        private const float TurnSeconds = 0.12f;

        [Test]
        public void TurnDuration_FullFlip_TakesTurnSeconds_RegardlessOfInstanceScale()
        {
            Assert.That(ChibiPuppetMotionRules.TurnDuration(1f, -1f, 1f, TurnSeconds), Is.EqualTo(TurnSeconds).Within(1e-6f));
            Assert.That(ChibiPuppetMotionRules.TurnDuration(-2f, 2f, 2f, TurnSeconds), Is.EqualTo(TurnSeconds).Within(1e-6f));
        }

        [Test]
        public void TurnDuration_FromMidway_ScalesWithRemainingDistance()
        {
            // 翻到一半（缩放 0）改回原朝向：只剩半程。
            Assert.That(ChibiPuppetMotionRules.TurnDuration(0f, 1f, 1f, TurnSeconds), Is.EqualTo(TurnSeconds * 0.5f).Within(1e-6f));
            Assert.That(ChibiPuppetMotionRules.TurnDuration(0.5f, -1f, 1f, TurnSeconds), Is.EqualTo(TurnSeconds * 0.75f).Within(1e-6f));
        }

        [TestCase(1f, 1f, 1f, TurnSeconds)]
        [TestCase(1f, -1f, 1f, 0f)]
        [TestCase(1f, -1f, 0f, TurnSeconds)]
        public void TurnDuration_NoDistanceOrNoTimeOrNoScale_IsInstant(float from, float to, float magnitude, float seconds)
        {
            Assert.That(ChibiPuppetMotionRules.TurnDuration(from, to, magnitude, seconds), Is.EqualTo(0f));
        }

        [Test]
        public void TurnScaleX_HitsEndpoints_AndPassesZeroAtMidpoint()
        {
            Assert.That(ChibiPuppetMotionRules.TurnScaleX(1f, -1f, 0f), Is.EqualTo(1f));
            Assert.That(ChibiPuppetMotionRules.TurnScaleX(1f, -1f, 0.5f), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(ChibiPuppetMotionRules.TurnScaleX(1f, -1f, 1f), Is.EqualTo(-1f));
            // 进度越界夹到两端。
            Assert.That(ChibiPuppetMotionRules.TurnScaleX(1f, -1f, -0.5f), Is.EqualTo(1f));
            Assert.That(ChibiPuppetMotionRules.TurnScaleX(1f, -1f, 1.5f), Is.EqualTo(-1f));
        }

        [Test]
        public void TurnScaleX_KeepsInstanceScaleMagnitude()
        {
            Assert.That(ChibiPuppetMotionRules.TurnScaleX(2f, -2f, 0.5f), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(ChibiPuppetMotionRules.TurnScaleX(2f, -2f, 1f), Is.EqualTo(-2f));
        }

        [Test]
        public void TurnScaleX_IsMonotonicAndContinuous()
        {
            float previous = ChibiPuppetMotionRules.TurnScaleX(1f, -1f, 0f);
            for (int i = 1; i <= 100; i++)
            {
                float current = ChibiPuppetMotionRules.TurnScaleX(1f, -1f, i / 100f);
                Assert.That(current, Is.LessThanOrEqualTo(previous), $"第 {i} 步缩放回弹了");
                Assert.That(previous - current, Is.LessThan(0.04f), $"第 {i} 步跳变过大");
                previous = current;
            }
        }

        [Test]
        public void TurnScaleX_ReversedMidway_ContinuesFromCurrentScaleWithoutJump()
        {
            // 朝左翻到 30% 时改回朝右：新补间以当时的缩放为起点，进度 0 处与改向前一帧相同；剩余时长短于整面翻转。
            float midway = ChibiPuppetMotionRules.TurnScaleX(1f, -1f, 0.3f);
            Assert.That(midway, Is.LessThan(1f).And.GreaterThan(-1f));
            Assert.That(ChibiPuppetMotionRules.TurnScaleX(midway, 1f, 0f), Is.EqualTo(midway));
            Assert.That(ChibiPuppetMotionRules.TurnScaleX(midway, 1f, 1f), Is.EqualTo(1f));
            Assert.That(ChibiPuppetMotionRules.TurnDuration(midway, 1f, 1f, TurnSeconds), Is.LessThan(TurnSeconds));
        }

        // ───────────────────────── 朝向指定点 / 朝向保持 ─────────────────────────

        [Test]
        public void ResolveFacingTowards_TargetOnRight_FacesRight()
        {
            Assert.That(ChibiPuppetMotionRules.ResolveFacingTowards(2f, 0.01f, true), Is.False);
        }

        [Test]
        public void ResolveFacingTowards_TargetOnLeft_FacesLeft()
        {
            Assert.That(ChibiPuppetMotionRules.ResolveFacingTowards(-2f, 0.01f, false), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ResolveFacingTowards_TargetStraightAheadOrBehind_KeepsCurrentFacing(bool current)
        {
            // 目标几乎正对相机纵深（右方向投影落在死区内）：不乱翻，与位移判定的死区同一口径。
            Assert.That(ChibiPuppetMotionRules.ResolveFacingTowards(0.005f, 0.01f, current), Is.EqualTo(current));
            Assert.That(ChibiPuppetMotionRules.ResolveFacingTowards(-0.005f, 0.01f, current), Is.EqualTo(current));
        }

        [Test]
        public void KeepFacingHold_WhileStandingStill_Persists()
        {
            Assert.That(ChibiPuppetMotionRules.KeepFacingHold(true, false), Is.True);
        }

        [Test]
        public void KeepFacingHold_OnceMoving_Releases()
        {
            Assert.That(ChibiPuppetMotionRules.KeepFacingHold(true, true), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KeepFacingHold_WhenNotHeld_NeverArmsItself(bool moving)
        {
            Assert.That(ChibiPuppetMotionRules.KeepFacingHold(false, moving), Is.False);
        }

        [Test]
        public void KeepFacingHold_AcrossSampleWindows_HoldsUntilFirstMoveThenStaysReleased()
        {
            // 驱动层每个采样窗口调一次：站着的窗口保持，一移动就解除；之后再停下也不会自己恢复保持（要再 FaceTowards）。
            bool held = true;
            bool[] movingPerWindow = { false, false, false, true, false, false };
            bool[] expectedHeld = { true, true, true, false, false, false };
            for (int i = 0; i < movingPerWindow.Length; i++)
            {
                held = ChibiPuppetMotionRules.KeepFacingHold(held, movingPerWindow[i]);
                Assert.That(held, Is.EqualTo(expectedHeld[i]), $"第 {i} 个窗口");
            }
        }

        // ───────────────────────── 交互挤压回弹 ─────────────────────────

        private const float Squash = 0.1f;
        private const float Overshoot = 0.04f;

        [TestCase(0f)]
        [TestCase(1f)]
        [TestCase(-1f)]
        [TestCase(2f)]
        public void InteractPulse_AtEndsAndOutOfRange_IsAtRest(float progress)
        {
            ChibiPuppetMotionRules.InteractPulse(progress, Squash, Overshoot, out float scaleY, out float lean01);
            Assert.That(scaleY, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(lean01, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void InteractPulse_SquashesThenOvershootsThenSettles()
        {
            ChibiPuppetMotionRules.InteractPulse(0.3f, Squash, Overshoot, out float lowest, out float leanAtLowest);
            Assert.That(lowest, Is.EqualTo(1f - Squash).Within(1e-5f));
            Assert.That(leanAtLowest, Is.EqualTo(1f).Within(1e-5f));

            ChibiPuppetMotionRules.InteractPulse(0.65f, Squash, Overshoot, out float highest, out float leanAtHighest);
            Assert.That(highest, Is.EqualTo(1f + Overshoot).Within(1e-5f));
            Assert.That(leanAtHighest, Is.EqualTo(0f).Within(1e-5f));

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i <= 1000; i++)
            {
                ChibiPuppetMotionRules.InteractPulse(i / 1000f, Squash, Overshoot, out float scaleY, out float lean01);
                min = scaleY < min ? scaleY : min;
                max = scaleY > max ? scaleY : max;
                Assert.That(lean01, Is.InRange(0f, 1f));
            }

            Assert.That(min, Is.EqualTo(1f - Squash).Within(1e-4f), "最低点就是 1 − squash，不会压过头");
            Assert.That(max, Is.EqualTo(1f + Overshoot).Within(1e-4f), "最高点就是 1 + overshoot");
        }

        [Test]
        public void InteractPulse_IsContinuousAcrossPhases()
        {
            ChibiPuppetMotionRules.InteractPulse(0f, Squash, Overshoot, out float previousY, out float previousLean);
            for (int i = 1; i <= 1000; i++)
            {
                ChibiPuppetMotionRules.InteractPulse(i / 1000f, Squash, Overshoot, out float scaleY, out float lean01);
                Assert.That(System.Math.Abs(scaleY - previousY), Is.LessThan(0.002f), $"进度 {i / 1000f} 缩放跳变");
                Assert.That(System.Math.Abs(lean01 - previousLean), Is.LessThan(0.02f), $"进度 {i / 1000f} 前倾跳变");
                previousY = scaleY;
                previousLean = lean01;
            }
        }
    }
}
