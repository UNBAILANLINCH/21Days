// 职责：QuestGuidanceMath 纯函数的 EditMode 回归测试（屏内悬浮 / 屏外贴边 + 箭头、直线距离取整）。
// 为什么新建：框架里没有「世界目标 → 屏幕指示（屏内悬浮 / 屏外贴边 + 箭头）」的现成组件，任务系统首次落地（PRP/quest-system 3.8）。
using Game.Quest;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Quest
{
    public class QuestGuidanceMathTests
    {
        private const float CanvasWidth = 1920f;
        private const float CanvasHeight = 1080f;
        private const float EdgeMargin = 48f;
        private const float HoverOffset = 80f;

        private static readonly Vector2 CanvasSize = new Vector2(CanvasWidth, CanvasHeight);

        [Test]
        public void Solve_CenterOnScreen_HoversAboveTarget()
        {
            QuestGuidance guidance = QuestGuidanceMath.Solve(
                new Vector3(0.5f, 0.5f, 10f), CanvasSize, EdgeMargin, HoverOffset);

            Assert.That(guidance.OnScreen, Is.True);
            Assert.That(guidance.AnchoredPosition.x, Is.EqualTo(0f).Within(0.5f));
            Assert.That(guidance.AnchoredPosition.y, Is.EqualTo(80f).Within(0.5f));
            Assert.That(guidance.ShowArrow, Is.False);
        }

        [Test]
        public void Solve_TopLeftCorner_IsStillOnScreen()
        {
            QuestGuidance guidance = QuestGuidanceMath.Solve(
                new Vector3(0f, 1f, 5f), CanvasSize, EdgeMargin, HoverOffset);

            Assert.That(guidance.OnScreen, Is.True);
            Assert.That(guidance.AnchoredPosition.x, Is.EqualTo(-960f).Within(0.5f));
            Assert.That(guidance.AnchoredPosition.y, Is.EqualTo(620f).Within(0.5f));
        }

        [Test]
        public void Solve_OffScreenRight_ClampsToRightEdge()
        {
            QuestGuidance guidance = QuestGuidanceMath.Solve(
                new Vector3(1.5f, 0.5f, 5f), CanvasSize, EdgeMargin, HoverOffset);

            Assert.That(guidance.OnScreen, Is.False);
            Assert.That(guidance.AnchoredPosition.x, Is.EqualTo(912f).Within(0.5f));
            Assert.That(guidance.AnchoredPosition.y, Is.EqualTo(0f).Within(0.5f));
            Assert.That(guidance.ArrowAngleDeg, Is.EqualTo(-90f).Within(0.5f));
            Assert.That(guidance.ShowArrow, Is.True);
        }

        [Test]
        public void Solve_OffScreenTop_ClampsToTopEdgeArrowUp()
        {
            QuestGuidance guidance = QuestGuidanceMath.Solve(
                new Vector3(0.5f, 2f, 5f), CanvasSize, EdgeMargin, HoverOffset);

            Assert.That(guidance.AnchoredPosition.x, Is.EqualTo(0f).Within(0.5f));
            Assert.That(guidance.AnchoredPosition.y, Is.EqualTo(492f).Within(0.5f));
            Assert.That(guidance.ArrowAngleDeg, Is.EqualTo(0f).Within(0.5f));
        }

        [Test]
        public void Solve_OffScreenBottomLeft_ClampsToNearestEdge()
        {
            // dir = ((-1 - 0.5) * 1920, (-1 - 0.5) * 1080) = (-2880, -1620)；
            // hx = 912，hy = 492；t = min(912/2880, 492/1620) = 492/1620 ≈ 0.303704（y 分量更早触边）。
            // 位置 = dir * t ≈ (-874.67, -492)；角度 = Atan2(2880, -1620) ≈ 119.36°。
            // 注：PRP 任务书里给出的参考值（dir (-1440,-1620)、位置 (-437.3,-492)、角度 138.4）
            // 是按公式 dir.x = (x - 0.5) * (w/2) 算的（x 分量少乘了一半的画布宽度）；
            // 本实现严格按任务书写明的公式 dir = ((x - 0.5) * w, (y - 0.5) * h) 计算，用上面的修正值断言。
            QuestGuidance guidance = QuestGuidanceMath.Solve(
                new Vector3(-1f, -1f, 5f), CanvasSize, EdgeMargin, HoverOffset);

            Assert.That(guidance.OnScreen, Is.False);
            Assert.That(guidance.AnchoredPosition.x, Is.EqualTo(-874.67f).Within(0.5f));
            Assert.That(guidance.AnchoredPosition.y, Is.EqualTo(-492f).Within(0.5f));
            Assert.That(guidance.ArrowAngleDeg, Is.EqualTo(119.36f).Within(0.5f));
            Assert.That(guidance.ShowArrow, Is.True);
        }

        [Test]
        public void Solve_BehindCamera_FlipsAndTreatsAsOffScreen()
        {
            // z < 0：(0.25, 0.25) 关于 (0.5, 0.5) 翻转为 (0.75, 0.75)，视为屏外。
            // dir = (0.25 * 1920, 0.25 * 1080) = (480, 270)；t = min(912/480, 492/270) ≈ 1.822。
            // 位置 = dir * t ≈ (874.7, 492)。
            QuestGuidance guidance = QuestGuidanceMath.Solve(
                new Vector3(0.25f, 0.25f, -3f), CanvasSize, EdgeMargin, HoverOffset);

            Assert.That(guidance.OnScreen, Is.False);
            Assert.That(guidance.AnchoredPosition.x, Is.EqualTo(874.67f).Within(0.5f));
            Assert.That(guidance.AnchoredPosition.y, Is.EqualTo(492f).Within(0.5f));
            Assert.That(guidance.ShowArrow, Is.True);
        }

        [Test]
        public void Solve_ZeroMargin_ClampsToCanvasEdge()
        {
            QuestGuidance guidance = QuestGuidanceMath.Solve(
                new Vector3(1.5f, 0.5f, 5f), CanvasSize, 0f, HoverOffset);

            Assert.That(guidance.AnchoredPosition.x, Is.EqualTo(960f).Within(0.5f));
            Assert.That(guidance.AnchoredPosition.y, Is.EqualTo(0f).Within(0.5f));
        }

        [Test]
        public void DistanceMeters_ThreeFourFive_ReturnsFive()
        {
            int distance = QuestGuidanceMath.DistanceMeters(Vector3.zero, new Vector3(3f, 4f, 0f));

            Assert.That(distance, Is.EqualTo(5));
        }

        [Test]
        public void DistanceMeters_RoundsHalfUp()
        {
            Assert.That(QuestGuidanceMath.DistanceMeters(Vector3.zero, new Vector3(2.4f, 0f, 0f)), Is.EqualTo(2));
            Assert.That(QuestGuidanceMath.DistanceMeters(Vector3.zero, new Vector3(2.5f, 0f, 0f)), Is.EqualTo(3));
            Assert.That(QuestGuidanceMath.DistanceMeters(Vector3.zero, new Vector3(2.6f, 0f, 0f)), Is.EqualTo(3));
        }
    }
}
