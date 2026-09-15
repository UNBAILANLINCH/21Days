// 职责：钉住音量换算——总音量与通道相乘、越界夹取、线性转分贝的两个边界。
// 为什么新建：AudioVolumeMath 是纯函数，算错只能靠耳朵听「怎么这么小声」；
//   一个被测类一个测试类，不塞进别的 Tests 文件。

using Game.Core.Audio;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    /// <summary>AudioVolumeMath 的 EditMode 测试。</summary>
    public sealed class AudioVolumeMathTests
    {
        [Test]
        public void Effective_WhenBothInRange_MultipliesChannels()
        {
            Assert.That(AudioVolumeMath.Effective(0.5f, 0.5f), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(AudioVolumeMath.Effective(1f, 0.8f), Is.EqualTo(0.8f).Within(1e-5f));
        }

        [Test]
        public void Effective_WhenMasterIsZero_SilencesChannel()
        {
            Assert.That(AudioVolumeMath.Effective(0f, 1f), Is.EqualTo(0f));
        }

        [Test]
        public void Effective_WhenInputsOutOfRange_ClampsToUnitRange()
        {
            Assert.That(AudioVolumeMath.Effective(-1f, 0.5f), Is.EqualTo(0f), "存档里的负数音量不能带到 AudioSource 上");
            Assert.That(AudioVolumeMath.Effective(2f, 3f), Is.EqualTo(1f), "大于 1 的音量会爆音，必须夹住");
        }

        [Test]
        public void ToDecibels_AtBoundaries_MapsZeroToMinusEightyAndOneToZero()
        {
            Assert.That(AudioVolumeMath.ToDecibels(0f), Is.EqualTo(AudioVolumeMath.MinDecibels).Within(1e-4f));
            Assert.That(AudioVolumeMath.ToDecibels(1f), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void ToDecibels_AtHalfVolume_IsAboutMinusSixDecibels()
        {
            Assert.That(AudioVolumeMath.ToDecibels(0.5f), Is.EqualTo(-6.0206f).Within(1e-3f));
        }

        [Test]
        public void ToDecibels_WhenBelowMinLinear_StaysAtFloorInsteadOfNegativeInfinity()
        {
            float decibels = AudioVolumeMath.ToDecibels(0.00001f);

            Assert.That(decibels, Is.EqualTo(AudioVolumeMath.MinDecibels).Within(1e-4f));
            Assert.That(float.IsNegativeInfinity(decibels), Is.False, "log10(0) 是负无穷，赋给 Mixer 会污染整条链路");
        }
    }
}
