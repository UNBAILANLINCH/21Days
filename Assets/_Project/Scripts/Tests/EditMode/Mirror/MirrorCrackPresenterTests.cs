// 职责：钉住镜碎页的按键保护时间判定（出现后一小段真实时间内不响应按键，防止被击中时连按直接跳过镜碎页）。
// 为什么新建：一个被测类一个测试类；入口点的状态流转已抽到 MirrorCrackTracker（见 MirrorCrackTrackerTests），这里只测余下的纯函数。
using Game.Mirror;
using NUnit.Framework;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorCrackPresenter.CanDismissShatter"/> 的 EditMode 测试。</summary>
    public sealed class MirrorCrackPresenterTests
    {
        [TestCase(0f, 0.6f, false)]
        [TestCase(0.59f, 0.6f, false)]
        [TestCase(0.6f, 0.6f, true)]
        [TestCase(5f, 0.6f, true)]
        [TestCase(0f, 0f, true)]
        public void CanDismissShatter_AfterDelay(float elapsed, float delay, bool expected)
        {
            Assert.That(MirrorCrackPresenter.CanDismissShatter(elapsed, delay), Is.EqualTo(expected));
        }
    }
}
