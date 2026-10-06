// 职责：钉住 2.5D 探索镜头的纯数学约束——死区内不动、越界被钳制、边界比视口小、
//   死区与边界打架时不抖，以及「y 轴不参与约束」。
// 为什么新建：CameraConstraintRules 是 roadmap A6「相机边界与死区」的规则层，纯数学、可穷举；
//   这些边界情况在场景里靠肉眼看不出来，必须用测试把口径钉死。
using Game.IsometricExploration;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="CameraConstraintRules"/> 的 EditMode 测试。</summary>
    public sealed class CameraConstraintRulesTests
    {
        /// <summary>边界：中心 (0,0)、40 x 20（x 从 -20 到 20，z 从 -10 到 10）。</summary>
        private static CameraConstraintRules Bounded(Vector2 deadZone, Vector2 viewport = default) =>
            new CameraConstraintRules(Vector2.zero, new Vector2(40f, 20f), deadZone, viewport);

        // ---------------------------------------------------------------- 死区

        [Test]
        public void Resolve_TargetInsideDeadZone_CameraDoesNotMove()
        {
            // 死区的全部意义：目标在里面动，镜头一步都不挪。
            CameraConstraintRules rules = Bounded(new Vector2(4f, 4f));
            var camera = new Vector3(0f, 8f, 0f);

            Assert.That(rules.InsideDeadZone(new Vector2(0f, 0f), new Vector3(1.9f, 0f, -1.9f)), Is.True);
            Assert.That(rules.Resolve(camera, new Vector3(1.9f, 0f, -1.9f)), Is.EqualTo(camera));
            Assert.That(rules.Resolve(camera, new Vector3(0f, 0f, 0f)), Is.EqualTo(camera));
        }

        [Test]
        public void Resolve_TargetExactlyOnDeadZoneEdge_CameraDoesNotMove()
        {
            // 负对照的另一半：死区边界算「里面」（含边界），否则目标停在边界上时镜头会一帧一帧地跳。
            CameraConstraintRules rules = Bounded(new Vector2(4f, 4f));
            var camera = new Vector3(0f, 8f, 0f);

            Assert.That(rules.Resolve(camera, new Vector3(2f, 0f, -2f)), Is.EqualTo(camera));
        }

        [Test]
        public void Resolve_ZeroDeadZone_CameraFollowsExactly()
        {
            // 死区为 0 时退化成「严丝合缝跟随」。
            CameraConstraintRules rules = Bounded(Vector2.zero);
            var camera = new Vector3(0f, 8f, 0f);

            Assert.That(rules.Resolve(camera, new Vector3(3f, 0f, -2f)), Is.EqualTo(new Vector3(3f, 8f, -2f)));
        }

        // ---------------------------------------------------------------- 越界被钳制

        [Test]
        public void Resolve_TargetBeyondDeadZone_PushesCameraToTheEdge()
        {
            // 目标越出死区：镜头挪到「刚好让目标落在最近的死区边上」，不是把目标摆到正中。
            CameraConstraintRules rules = Bounded(new Vector2(4f, 4f));
            var camera = new Vector3(0f, 8f, 0f);

            Vector3 moved = rules.Resolve(camera, new Vector3(10f, 0f, -1f));

            Assert.That(moved.x, Is.EqualTo(10f - 2f), "x 越界，推到「目标停在死区边上」");
            Assert.That(moved.z, Is.EqualTo(-1f), "z 在死区内，镜头跟到目标的 z（该轴上与目标重合）");
            Assert.That(moved.y, Is.EqualTo(8f), "y 轴不参与约束");
        }

        [Test]
        public void Resolve_OnlyOneAxisBeyondDeadZone_DoesNotDriftTheOther()
        {
            // 负对照：一个轴越界不该让另一个轴漂移。目标在 z 上不动，镜头的 z 也只跟到目标，不额外推。
            CameraConstraintRules rules = Bounded(new Vector2(4f, 4f));
            var camera = new Vector3(0f, 8f, -1f);

            Vector3 moved = rules.Resolve(camera, new Vector3(10f, 0f, -1f));

            Assert.That(moved.x, Is.EqualTo(8f));
            Assert.That(moved.z, Is.EqualTo(-1f), "z 本来就在死区里，镜头 z 原地不动");
        }

        [Test]
        public void Resolve_TargetPushedPastBounds_IsClampedToBounds()
        {
            // 目标继续往外走：镜头被边界钳住，停在边界上（x = 20），不再跟着跑。
            CameraConstraintRules rules = Bounded(new Vector2(4f, 4f));
            var camera = new Vector3(19f, 8f, 0f);

            Vector3 moved = rules.Resolve(camera, new Vector3(40f, 0f, 0f));

            Assert.That(moved.x, Is.EqualTo(20f), "被钳在边界 x = 20");
            Assert.That(moved.z, Is.EqualTo(0f));
        }

        [Test]
        public void ClampToBounds_ClampsAllFourSides()
        {
            CameraConstraintRules rules = Bounded(new Vector2(4f, 4f));

            Assert.That(rules.ClampToBounds(new Vector2(100f, 100f)), Is.EqualTo(new Vector2(20f, 10f)));
            Assert.That(rules.ClampToBounds(new Vector2(-100f, -100f)), Is.EqualTo(new Vector2(-20f, -10f)));
            Assert.That(rules.ClampToBounds(new Vector2(3f, -4f)), Is.EqualTo(new Vector2(3f, -4f)), "界内原样返回");
        }

        // ---------------------------------------------------------------- 边界与死区打架

        [Test]
        public void Resolve_ClampPushesTargetBackIntoDeadZone_KeepsCameraStill()
        {
            // 边界与死区打架时（典型：场景边角）不抖：钳制之后目标又落回死区里，就退回原位置。
            CameraConstraintRules rules = Bounded(new Vector2(4f, 4f));
            var camera = new Vector3(20f, 8f, 0f);

            Vector3 moved = rules.Resolve(camera, new Vector3(24f, 0f, 0f));

            Assert.That(moved, Is.EqualTo(camera), "目标在死区外但钳完又回到死区里，宁可不挪");
        }

        [Test]
        public void Resolve_AtBounds_CameraStopsEvenIfTargetKeepsGoing()
        {
            // 连续推进：目标一路往右，镜头到边界后就不动了（不抖、不越界）。
            CameraConstraintRules rules = Bounded(new Vector2(4f, 4f));
            var camera = new Vector3(18f, 8f, 0f);

            for (float x = 20f; x <= 30f; x += 1f)
            {
                camera = rules.Resolve(camera, new Vector3(x, 0f, 0f));
                Assert.That(camera.x, Is.LessThanOrEqualTo(20f), $"x = {x} 时镜头越过了边界");
            }

            Assert.That(camera.x, Is.EqualTo(20f));
        }

        // ---------------------------------------------------------------- 视口修正

        [Test]
        public void ClampToBounds_WithViewport_ShrinksTheMovableRange()
        {
            // 边界说的是「画面能看到的世界范围」，所以镜头中心的可动范围要收缩半个视野。
            CameraConstraintRules rules = Bounded(new Vector2(4f, 4f), new Vector2(10f, 6f));

            Assert.That(rules.ClampToBounds(new Vector2(100f, 100f)), Is.EqualTo(new Vector2(15f, 7f)),
                "20 - 10/2 = 15，10 - 6/2 = 7");
            Assert.That(rules.CenterBounds, Is.EqualTo(new Rect(-20f, -10f, 40f, 20f)),
                "CenterBounds 给的是不含视口修正的原始边界");
        }

        [Test]
        public void ClampToBounds_WhenBoundsSmallerThanViewport_centersTheCamera()
        {
            // 边界比视野还小时可动范围退化成一点，镜头摆在边界正中——几何上无解，但结果必须确定。
            var rules = new CameraConstraintRules(Vector2.zero, new Vector2(4f, 4f), new Vector2(2f, 2f), new Vector2(20f, 20f));

            Assert.That(rules.ClampToBounds(new Vector2(100f, -100f)), Is.EqualTo(Vector2.zero));
            Assert.That(rules.ClampToBounds(Vector2.zero), Is.EqualTo(Vector2.zero));

            // 目标走到天边也一样：镜头停在正中，不会算出 NaN 或翻转的区间。
            Vector3 moved = rules.Resolve(new Vector3(0f, 5f, 0f), new Vector3(500f, 0f, 500f));
            Assert.That(moved.x, Is.EqualTo(0f));
            Assert.That(moved.z, Is.EqualTo(0f));
            Assert.That(float.IsNaN(moved.x), Is.False);
            Assert.That(float.IsNaN(moved.z), Is.False);
        }

        [Test]
        public void Constructor_NegativeSizes_AreTreatedAsZero()
        {
            // 负对照：策划在 Inspector 里填了负数，不该算出翻转的区间。
            var rules = new CameraConstraintRules(Vector2.zero, new Vector2(-4f, -4f), new Vector2(-2f, -2f), new Vector2(-8f, -8f));

            Assert.That(rules.CenterBounds, Is.EqualTo(new Rect(0f, 0f, 0f, 0f)));
            Assert.That(rules.ClampToBounds(new Vector2(5f, 5f)), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Resolve_OffsetBounds_ClampsAroundTheBoundsCenter()
        {
            // 边界不必以原点为中心（街面是一条横带，10_两界与场景结构.md:136 R8）。
            var rules = new CameraConstraintRules(new Vector2(100f, -5f), new Vector2(40f, 20f), new Vector2(4f, 4f), Vector2.zero);

            Assert.That(rules.ClampToBounds(new Vector2(1000f, 1000f)), Is.EqualTo(new Vector2(120f, 5f)));
            Assert.That(rules.Resolve(new Vector3(100f, 8f, -5f), new Vector3(300f, 0f, -5f)).x, Is.EqualTo(120f));
        }
    }
}
