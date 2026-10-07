// 职责：验证减雾内外边界与过渡；已有相机测试不覆盖空间雾密度判定。
using Game.IsometricExploration;
using NUnit.Framework;

namespace Game.Tests.EditMode.IsometricExploration
{
    public sealed class DarkFogRegionTests
    {
        [Test]
        public void DensityMask_InnerOuterAndFeather_StayMonotonic()
        {
            Assert.That(DarkFogRegion.DensityMask(0f, 0.3f), Is.Zero);
            Assert.That(DarkFogRegion.DensityMask(0.7f, 0.3f), Is.Zero);
            Assert.That(DarkFogRegion.DensityMask(0.85f, 0.3f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(DarkFogRegion.DensityMask(1f, 0.3f), Is.EqualTo(1f));
            Assert.That(DarkFogRegion.DensityMask(2f, 0.3f), Is.EqualTo(1f));
            Assert.That(DarkFogRegion.DensityMask(1f, 0f), Is.EqualTo(1f));
        }
    }
}
