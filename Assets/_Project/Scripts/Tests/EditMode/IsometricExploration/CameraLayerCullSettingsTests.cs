// 职责：剔除距离的数值边界；现有测试未覆盖该配置，单独验证无需场景的规则。
using Game.IsometricExploration;
using NUnit.Framework;

namespace Game.Tests.EditMode.IsometricExploration
{
    public sealed class CameraLayerCullSettingsTests
    {
        [TestCase(0f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(float.NegativeInfinity, 0f)]
        [TestCase(40f, 40f)]
        public void Distance_InvalidValues_UseFarClip(float input, float expected)
        {
            var settings = new CameraLayerCullSettings(1 << 31, input);
            Assert.That(settings.Distance, Is.EqualTo(expected));
            Assert.That(settings.Layers, Is.EqualTo(1 << 31));
        }
    }
}
