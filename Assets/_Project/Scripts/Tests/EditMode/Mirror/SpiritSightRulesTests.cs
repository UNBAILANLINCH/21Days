// 职责：钉住 SpiritSightRules——雨 / 夜 / 昏暗任一即生效、只有妖可见、半径边界、区域矩形判定（PRD V7 规则侧）。
// 为什么新建：一个被测类一个测试类；纯规则不经容器与场景。
using Game.Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="SpiritSightRules"/> 的 EditMode 测试。</summary>
    public sealed class SpiritSightRulesTests
    {
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, true, true)]
        public void IsActive_AnyCondition_IsTrue(bool rain, bool night, bool dim)
        {
            Assert.That(SpiritSightRules.IsActive(rain, night, dim), Is.True);
        }

        [Test]
        public void IsActive_NoCondition_IsFalse()
        {
            Assert.That(SpiritSightRules.IsActive(false, false, false), Is.False);
        }

        [Test]
        public void Visible_OnlyYao()
        {
            Vector2 near = new Vector2(1f, 1f);

            Assert.That(SpiritSightRules.Visible(MirrorSubjectKind.Yao, near, Vector2.zero, 8f), Is.True);
            Assert.That(SpiritSightRules.Visible(MirrorSubjectKind.Human, near, Vector2.zero, 8f), Is.False, "普通人不出影子");
            Assert.That(SpiritSightRules.Visible(MirrorSubjectKind.Object, near, Vector2.zero, 8f), Is.False);
        }

        [Test]
        public void Visible_RadiusBoundary_Inclusive()
        {
            Assert.That(SpiritSightRules.Visible(MirrorSubjectKind.Yao, new Vector2(8f, 0f), Vector2.zero, 8f), Is.True);
            Assert.That(SpiritSightRules.Visible(MirrorSubjectKind.Yao, new Vector2(8.01f, 0f), Vector2.zero, 8f), Is.False);
        }

        [Test]
        public void Visible_NonPositiveRadius_NothingVisible()
        {
            Assert.That(SpiritSightRules.Visible(MirrorSubjectKind.Yao, Vector2.zero, Vector2.zero, 0f), Is.False);
            Assert.That(SpiritSightRules.Visible(MirrorSubjectKind.Yao, Vector2.zero, Vector2.zero, -1f), Is.False);
        }

        [Test]
        public void Visible_UsesRelativeDistance()
        {
            var player = new Vector2(10f, -4f);

            Assert.That(SpiritSightRules.Visible(MirrorSubjectKind.Yao, new Vector2(13f, 0f), player, 5f), Is.True);
            Assert.That(SpiritSightRules.Visible(MirrorSubjectKind.Yao, new Vector2(0f, 0f), player, 5f), Is.False);
        }

        [Test]
        public void InZone_InsideAndBoundary_IsTrue()
        {
            var a = new Vector2(-2f, -1f);
            var b = new Vector2(3f, 4f);

            Assert.That(SpiritSightRules.InZone(new Vector2(0f, 0f), a, b), Is.True);
            Assert.That(SpiritSightRules.InZone(new Vector2(3f, 4f), a, b), Is.True, "边界算在区内");
        }

        [Test]
        public void InZone_Outside_IsFalse()
        {
            var a = new Vector2(-2f, -1f);
            var b = new Vector2(3f, 4f);

            Assert.That(SpiritSightRules.InZone(new Vector2(3.01f, 0f), a, b), Is.False);
            Assert.That(SpiritSightRules.InZone(new Vector2(0f, -1.01f), a, b), Is.False);
        }

        [Test]
        public void InZone_CornersInAnyOrder_SameResult()
        {
            var point = new Vector2(1f, 1f);

            Assert.That(SpiritSightRules.InZone(point, new Vector2(3f, 4f), new Vector2(-2f, -1f)), Is.True);
            Assert.That(SpiritSightRules.InZone(point, new Vector2(-2f, 4f), new Vector2(3f, -1f)), Is.True);
        }
    }
}
