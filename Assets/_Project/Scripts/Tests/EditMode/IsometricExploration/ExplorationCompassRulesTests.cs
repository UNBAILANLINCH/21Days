// 职责：钉住万向标摆位规则（屏内不画、屏外贴边、身后翻转、四角钳制、同边错开）与控件区的纯判定。
// 为什么新建：ExplorationHudPresenterTests 只测沉浸规则，万向标是另一个被测类（ExplorationCompassRules），按「<被测类>Tests」单独成文件。
using System.Collections.Generic;
using Game.IsometricExploration;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.IsometricExploration
{
    public sealed class ExplorationCompassRulesTests
    {
        private static readonly Vector2 Canvas = new Vector2(1920f, 1080f);
        private const float Margin = 48f;
        private const float Tolerance = 0.01f;

        [Test]
        public void TryPlace_WhenOnScreen_ReturnsFalse()
        {
            bool placed = ExplorationCompassRules.TryPlace(new Vector3(0.3f, 0.7f, 5f), Canvas, Margin, out _, out _);

            Assert.That(placed, Is.False, "屏内兴趣点由场景自身可见，万向标不画");
        }

        [Test]
        public void TryPlace_WhenRightOffScreen_ClampsToRightEdge()
        {
            bool placed = ExplorationCompassRules.TryPlace(new Vector3(1.5f, 0.5f, 5f), Canvas, Margin,
                out Vector2 position, out float angle);

            Assert.That(placed, Is.True);
            Assert.That(position.x, Is.EqualTo(Canvas.x / 2f - Margin).Within(Tolerance));
            Assert.That(position.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(angle, Is.EqualTo(-90f).Within(Tolerance), "箭头朝右 = 顺时针 90°");
        }

        [Test]
        public void TryPlace_WhenBehindCamera_FlipsToOppositeSide()
        {
            // 身后且视口 x 偏右：翻转后应贴左边。
            bool placed = ExplorationCompassRules.TryPlace(new Vector3(0.8f, 0.5f, -3f), Canvas, Margin,
                out Vector2 position, out float angle);

            Assert.That(placed, Is.True, "身后的兴趣点即便视口 xy 落在 [0,1] 内也要画");
            Assert.That(position.x, Is.EqualTo(-(Canvas.x / 2f - Margin)).Within(Tolerance));
            Assert.That(angle, Is.EqualTo(90f).Within(Tolerance));
        }

        [Test]
        public void TryPlace_WhenFarCorner_ClampsInsideMarginRect()
        {
            bool placed = ExplorationCompassRules.TryPlace(new Vector3(-10f, 12f, 5f), Canvas, Margin,
                out Vector2 position, out _);

            Assert.That(placed, Is.True);
            Assert.That(Mathf.Abs(position.x), Is.LessThanOrEqualTo(Canvas.x / 2f - Margin + Tolerance));
            Assert.That(Mathf.Abs(position.y), Is.LessThanOrEqualTo(Canvas.y / 2f - Margin + Tolerance));
            Assert.That(position.x, Is.LessThan(0f), "左上方目标贴在左侧");
            Assert.That(position.y, Is.GreaterThan(0f), "左上方目标贴在上侧");
        }

        [Test]
        public void TryPlace_WhenOnCameraPlane_TreatedAsOffScreen()
        {
            bool placed = ExplorationCompassRules.TryPlace(new Vector3(0.5f, 0.5f, 0f), Canvas, Margin, out _, out _);

            Assert.That(placed, Is.True, "z = 0 不算屏内");
        }

        [Test]
        public void TrimLabel_WhenLongerThanMax_Truncates()
        {
            Assert.That(ExplorationCompassPresenter.TrimLabel("营地东侧瞭望塔顶", 6), Is.EqualTo("营地东侧瞭望"));
            Assert.That(ExplorationCompassPresenter.TrimLabel("营地", 6), Is.EqualTo("营地"));
            Assert.That(ExplorationCompassPresenter.TrimLabel("营地", 0), Is.EqualTo(string.Empty));
            Assert.That(ExplorationCompassPresenter.TrimLabel(null, 6), Is.EqualTo(string.Empty));
        }

        [Test]
        public void ShouldShowStick_OnDesktop_FollowsConfig()
        {
            Assert.That(ExplorationControlsPresenter.ShouldShowStick(true, false), Is.True, "触屏恒显示摇杆");
            Assert.That(ExplorationControlsPresenter.ShouldShowStick(false, true), Is.True);
            Assert.That(ExplorationControlsPresenter.ShouldShowStick(false, false), Is.False);
        }

        [Test]
        public void SelectRunLabel_FollowsRunningMode()
        {
            Assert.That(ExplorationControlsPresenter.SelectRunLabel(true, "奔跑", "散步"), Is.EqualTo("奔跑"));
            Assert.That(ExplorationControlsPresenter.SelectRunLabel(false, "奔跑", "散步"), Is.EqualTo("散步"));
        }

        [Test]
        public void SpreadAlongEdges_WhenSameEdgeOverlaps_PushesApartByMinSpacing()
        {
            const float MinSpacing = 56f;
            float rightEdgeX = Canvas.x / 2f - Margin;
            var placements = new List<ExplorationCompassRules.CompassPlacement>
            {
                new ExplorationCompassRules.CompassPlacement(new Vector2(rightEdgeX, 0f), -90f),
                new ExplorationCompassRules.CompassPlacement(new Vector2(rightEdgeX, 0f), -90f),
            };

            ExplorationCompassRules.SpreadAlongEdges(placements, Canvas, Margin, MinSpacing);

            float gap = Mathf.Abs(placements[1].AnchoredPosition.y - placements[0].AnchoredPosition.y);
            Assert.That(gap, Is.GreaterThanOrEqualTo(MinSpacing - Tolerance), "同边重叠标记要被推开到至少 minSpacing");
            Assert.That(placements[0].AnchoredPosition.x, Is.EqualTo(rightEdgeX).Within(Tolerance), "贴边坐标不因错开而改变");
            Assert.That(placements[1].AnchoredPosition.x, Is.EqualTo(rightEdgeX).Within(Tolerance));
        }

        [Test]
        public void SpreadAlongEdges_WhenDifferentEdges_LeavesPositionsUnchanged()
        {
            const float MinSpacing = 56f;
            Vector2 leftPos = new Vector2(-(Canvas.x / 2f - Margin), 0f);
            Vector2 topPos = new Vector2(0f, Canvas.y / 2f - Margin);
            var placements = new List<ExplorationCompassRules.CompassPlacement>
            {
                new ExplorationCompassRules.CompassPlacement(leftPos, 90f),
                new ExplorationCompassRules.CompassPlacement(topPos, 0f),
            };

            ExplorationCompassRules.SpreadAlongEdges(placements, Canvas, Margin, MinSpacing);

            Assert.That(placements[0].AnchoredPosition.x, Is.EqualTo(leftPos.x).Within(Tolerance), "不同边互不影响，左边标记原样保留");
            Assert.That(placements[0].AnchoredPosition.y, Is.EqualTo(leftPos.y).Within(Tolerance));
            Assert.That(placements[1].AnchoredPosition.x, Is.EqualTo(topPos.x).Within(Tolerance), "不同边互不影响，上边标记原样保留");
            Assert.That(placements[1].AnchoredPosition.y, Is.EqualTo(topPos.y).Within(Tolerance));
        }

        [Test]
        public void SpreadAlongEdges_WhenPushedIntoCorner_ClampsWithoutGoingOutOfBounds()
        {
            const float MinSpacing = 56f;
            float rightEdgeX = Canvas.x / 2f - Margin;
            float halfHeight = Canvas.y / 2f - Margin; // 492
            // 三个标记贴右边，聚在右上角附近，正向推挤会把最后一个推出画布，必须整体回退再夹紧。
            var placements = new List<ExplorationCompassRules.CompassPlacement>
            {
                new ExplorationCompassRules.CompassPlacement(new Vector2(rightEdgeX, 470f), -90f),
                new ExplorationCompassRules.CompassPlacement(new Vector2(rightEdgeX, 475f), -90f),
                new ExplorationCompassRules.CompassPlacement(new Vector2(rightEdgeX, 480f), -90f),
            };

            ExplorationCompassRules.SpreadAlongEdges(placements, Canvas, Margin, MinSpacing);

            foreach (ExplorationCompassRules.CompassPlacement placement in placements)
            {
                Assert.That(placement.AnchoredPosition.y, Is.LessThanOrEqualTo(halfHeight + Tolerance), "钳制在边内，不越界");
                Assert.That(placement.AnchoredPosition.y, Is.GreaterThanOrEqualTo(-halfHeight - Tolerance));
                Assert.That(placement.AnchoredPosition.x, Is.EqualTo(rightEdgeX).Within(Tolerance), "只改沿边坐标，不改贴边坐标");
            }

            Assert.That(placements[2].AnchoredPosition.y, Is.EqualTo(halfHeight).Within(Tolerance), "推到边角，末端夹在边界上");
            Assert.That(placements[1].AnchoredPosition.y - placements[0].AnchoredPosition.y,
                Is.EqualTo(MinSpacing).Within(Tolerance), "挤不下时整体前移，相邻间距仍压到 minSpacing");
            Assert.That(placements[2].AnchoredPosition.y - placements[1].AnchoredPosition.y,
                Is.EqualTo(MinSpacing).Within(Tolerance));
        }
    }
}
