// 职责：钉住遮挡探测的纯几何规则 OccluderFadePresenter.TryBuildProbe（方向归一、距离扣掉半径、贴脸不探测）。
// 为什么新建：现有两份测试各测一个被测类（万向标规则、HUD 呈现器），遮挡呈现器是另一个被测类，按「<被测类>Tests」单独成文件。
using Game.IsometricExploration;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.IsometricExploration
{
    public sealed class OccluderFadePresenterTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void TryBuildProbe_SubtractsRadiusFromDistance()
        {
            bool ok = OccluderFadePresenter.TryBuildProbe(new Vector3(0f, 10f, 0f), Vector3.zero, 1f,
                out Vector3 direction, out float distance);

            Assert.That(ok, Is.True);
            Assert.That(distance, Is.EqualTo(9f).Within(Tolerance), "球停在目标前一个半径处，不扫到目标脚下地面");
            Assert.That(Vector3.Distance(direction, Vector3.down), Is.LessThan(Tolerance), "方向为单位向量，指向目标");
        }

        [Test]
        public void TryBuildProbe_ZeroRadius_UsesFullDistance()
        {
            bool ok = OccluderFadePresenter.TryBuildProbe(Vector3.zero, new Vector3(3f, 0f, 4f), 0f,
                out Vector3 direction, out float distance);

            Assert.That(ok, Is.True);
            Assert.That(distance, Is.EqualTo(5f).Within(Tolerance), "半径 0 退化成细射线");
            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void TryBuildProbe_NegativeRadius_TreatedAsZero()
        {
            OccluderFadePresenter.TryBuildProbe(Vector3.zero, new Vector3(0f, 0f, 2f), -1f, out _, out float distance);

            Assert.That(distance, Is.EqualTo(2f).Within(Tolerance), "负半径不能把探测距离拉长");
        }

        [Test]
        public void TryBuildProbe_WhenTargetInsideRadius_ReturnsFalse()
        {
            bool ok = OccluderFadePresenter.TryBuildProbe(Vector3.zero, new Vector3(0f, 0f, 0.5f), 1f,
                out Vector3 direction, out float distance);

            Assert.That(ok, Is.False, "相机贴着目标时不探测");
            Assert.That(distance, Is.EqualTo(0f));
            Assert.That(direction, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void TryBuildProbe_WhenSamePoint_ReturnsFalse()
        {
            bool ok = OccluderFadePresenter.TryBuildProbe(Vector3.one, Vector3.one, 0f, out _, out _);

            Assert.That(ok, Is.False);
        }
    }
}
