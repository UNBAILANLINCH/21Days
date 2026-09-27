// 职责：钉住 PerformancePlacement 的「未指定 / 指定」语义与从锚点取位姿。
// 为什么新建（复用 → 扩展 → 新建）：PerformancePlacement 是新值类型，现有测试类各测各的被测类，按「一个被测类一个测试类」新建。
using Game.Performance;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformancePlacementTests
    {
        [Test]
        public void None_AndDefault_HaveNoValue()
        {
            Assert.That(PerformancePlacement.None.HasValue, Is.False);
            Assert.That(default(PerformancePlacement).HasValue, Is.False);
        }

        [Test]
        public void Constructor_AtOrigin_StillHasValue()
        {
            var placement = new PerformancePlacement(Vector3.zero, Quaternion.identity);

            Assert.That(placement.HasValue, Is.True, "摆到原点与不摆放必须能区分");
            Assert.That(placement.Position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void FromTransform_Null_ReturnsNone()
        {
            Assert.That(PerformancePlacement.FromTransform(null).HasValue, Is.False);
        }

        [Test]
        public void FromTransform_UsesWorldPose()
        {
            var parent = new GameObject("placement_parent");
            try
            {
                parent.transform.position = new Vector3(10f, 0f, 0f);
                var child = new GameObject("placement_anchor");
                child.transform.SetParent(parent.transform, false);
                child.transform.localPosition = new Vector3(1f, 2f, 3f);
                child.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

                PerformancePlacement placement = PerformancePlacement.FromTransform(child.transform);

                Assert.That(placement.HasValue, Is.True);
                Assert.That(Vector3.Distance(placement.Position, new Vector3(11f, 2f, 3f)), Is.LessThan(1e-4f));
                Assert.That(Quaternion.Angle(placement.Rotation, Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
