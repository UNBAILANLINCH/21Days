// 职责：钉住安全区换算——全屏得满锚点、刘海屏得正确比例、异常屏幕尺寸不产生 NaN。
// 为什么新建：SafeAreaMath 是纯函数，真机上算错只能靠肉眼看「界面被顶到刘海下面」，
//   必须有 EditMode 用例挡住；一个被测类一个测试类。

using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Core
{
    /// <summary>SafeAreaMath 的 EditMode 测试。</summary>
    public sealed class SafeAreaMathTests
    {
        [Test]
        public void ToAnchors_WhenSafeAreaCoversWholeScreen_ReturnsFullAnchors()
        {
            SafeAreaMath.ToAnchors(new Rect(0f, 0f, 1920f, 1080f), 1920, 1080,
                out Vector2 min, out Vector2 max);

            Assert.That(min, Is.EqualTo(Vector2.zero));
            Assert.That(max, Is.EqualTo(Vector2.one));
        }

        [Test]
        public void ToAnchors_WhenScreenHasNotch_ReturnsInsetAnchors()
        {
            // 竖屏手机：顶部 132px 刘海、底部 66px 手势条，左右各留 30px 圆角。
            SafeAreaMath.ToAnchors(new Rect(30f, 66f, 1080f - 60f, 2340f - 198f), 1080, 2340,
                out Vector2 min, out Vector2 max);

            Assert.That(min.x, Is.EqualTo(30f / 1080f).Within(1e-5f));
            Assert.That(min.y, Is.EqualTo(66f / 2340f).Within(1e-5f));
            Assert.That(max.x, Is.EqualTo(1050f / 1080f).Within(1e-5f));
            Assert.That(max.y, Is.EqualTo(2208f / 2340f).Within(1e-5f));
        }

        [Test]
        public void ToAnchors_WhenSafeAreaOverflowsScreen_ClampsToUnitRange()
        {
            SafeAreaMath.ToAnchors(new Rect(-50f, -50f, 5000f, 5000f), 1920, 1080,
                out Vector2 min, out Vector2 max);

            Assert.That(min, Is.EqualTo(Vector2.zero));
            Assert.That(max, Is.EqualTo(Vector2.one));
        }

        [Test]
        public void ToAnchors_WhenScreenSizeIsZero_ReturnsFullAnchorsInsteadOfNaN()
        {
            SafeAreaMath.ToAnchors(new Rect(0f, 0f, 100f, 100f), 0, 0,
                out Vector2 min, out Vector2 max);

            Assert.That(min, Is.EqualTo(Vector2.zero));
            Assert.That(max, Is.EqualTo(Vector2.one));
            Assert.That(float.IsNaN(min.x), Is.False, "除零得到的 NaN 赋给 anchor 会让整个界面消失");
        }
    }
}
