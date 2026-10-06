// 职责：钉住「表现层真的在调相机约束」——不是再验一遍纯数学（CameraConstraintRulesTests 已穷举），
//   而是验 SmoothCameraFollow 确实把 CameraConstraintRules 接了起来、且**不设边界时与接线前逐字一致**。
// 为什么新建：A6 的纯规则那份早就做完且全绿，缺的正是「谁在 LateUpdate 里调它」这半；
//   而 EditMode 没有帧循环，所以验证入口是 ResolveDesiredPosition()（LateUpdate 用的就是它）。
using System;
using Game.IsometricExploration;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="SmoothCameraFollow"/> 与 <see cref="CameraConstraintPolicy"/> 的接线测试。</summary>
    public sealed class CameraConstraintWiringTests
    {
        private const float CameraHeight = 8f;

        private GameObject cameraObject;
        private GameObject targetObject;
        private SmoothCameraFollow follow;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("测试相机");
            cameraObject.transform.position = new Vector3(0f, CameraHeight, 0f);
            follow = cameraObject.AddComponent<SmoothCameraFollow>(); // [RequireComponent(Camera)] 会自动补相机

            targetObject = new GameObject("跟随目标");
            follow.SetTarget(targetObject.transform);
            // 构图偏移 = 相机在目标正上方 8 米（真实场景里由 Start 从初始构图取；EditMode 里 Start 不跑，所以显式设）。
            // 「不设边界」的期望值就是 target.position + 这个偏移。
            follow.SetOffset(new Vector3(0f, CameraHeight, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }

        // ---------------------------------------------------------------- 不设边界 = 旧行为（本波接线的安全边界）

        [Test]
        public void ResolveDesiredPosition_WithoutBounds_IsExactlyTheOldFormula()
        {
            targetObject.transform.position = new Vector3(3f, 0f, -2f);
            follow.SetOffset(new Vector3(0f, CameraHeight, -4f));

            Assert.That(follow.ConstraintPolicy.HasBounds, Is.False, "没设边界");
            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(3f, CameraHeight, -6f)),
                "不设边界时结果就是接线前的 target.position + offset，约束一行都不参与");
        }

        [Test]
        public void ClearConstraintBounds_ReturnsToOldBehavior()
        {
            follow.SetConstraintBounds(Vector2.zero, new Vector2(4f, 4f));
            targetObject.transform.position = new Vector3(10f, 0f, 0f);

            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(2f, CameraHeight, 0f)),
                "有边界时确实被约束了（否则下面的负对照不成立：取消边界看不出区别）");

            follow.ClearConstraintBounds();

            Assert.That(follow.ConstraintPolicy.HasBounds, Is.False);
            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(10f, CameraHeight, 0f)),
                "取消边界 = 回到接线前的行为");
        }

        // ---------------------------------------------------------------- 表现层真的在调规则

        [Test]
        public void ResolveDesiredPosition_TargetInsideDeadZone_KeepsTheCameraWhereItIs()
        {
            follow.SetConstraintBounds(Vector2.zero, new Vector2(40f, 20f), new Vector2(4f, 4f));
            targetObject.transform.position = new Vector3(1f, 0f, 1f);

            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(cameraObject.transform.position),
                "目标在死区内：镜头一动都不动（返回当前位置 = SmoothDamp 的目标就是原地）");
        }

        [Test]
        public void ResolveDesiredPosition_TargetBeyondDeadZone_PushesTheCameraToTheDeadZoneEdge()
        {
            follow.SetConstraintBounds(Vector2.zero, new Vector2(40f, 20f), new Vector2(4f, 4f));
            targetObject.transform.position = new Vector3(10f, 0f, 0f);

            // 死区半宽 2：镜头推到「目标刚好落在死区边上」的 x = 8，不是把目标摆到画面正中。
            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(8f, CameraHeight, 0f)));
        }

        [Test]
        public void ResolveDesiredPosition_BoundsSmallerThanViewport_CentersOnTheBounds()
        {
            // 边界比视野还小时几何上无解，规则给的口径是「退化成边界中点」，保证结果确定不抖。
            follow.SetConstraintBounds(new Vector2(5f, 3f), new Vector2(4f, 2f), Vector2.zero, new Vector2(20f, 10f));
            targetObject.transform.position = new Vector3(100f, 0f, 100f);

            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(5f, CameraHeight, 3f)));
        }

        [Test]
        public void ResolveDesiredPosition_OnlyXOutOfBounds_DoesNotDriftTheOtherAxis()
        {
            cameraObject.transform.position = new Vector3(0f, CameraHeight, 5f);
            follow.SetConstraintBounds(Vector2.zero, new Vector2(40f, 20f), Vector2.zero);

            // x 越界被推（推到目标本身），z 必须原样跟着目标走 —— 不许把某个轴的钳制串到另一个轴上。
            targetObject.transform.position = new Vector3(10f, 0f, 5f);
            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(10f, CameraHeight, 5f)));

            targetObject.transform.position = new Vector3(10f, 0f, -5f);
            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(10f, CameraHeight, -5f)));
        }

        [Test]
        public void ResolveDesiredPosition_ConstraintsPreserveTheCameraHeight()
        {
            // y 轴不参与约束：镜头高度由构图偏移决定（规则层已钉过，这里验表现层没把它改掉）。
            follow.SetConstraintBounds(Vector2.zero, new Vector2(4f, 4f));
            targetObject.transform.position = new Vector3(10f, 0f, 10f);

            Assert.That(follow.ResolveDesiredPosition().y, Is.EqualTo(CameraHeight));
        }

        // ---------------------------------------------------------------- 与容器侧共享同一份策略

        [Test]
        public void BindConstraintPolicy_SharedPolicySetLater_TakesEffect()
        {
            var shared = new CameraConstraintPolicy();
            follow.BindConstraintPolicy(shared);

            Assert.That(follow.ConstraintPolicy, Is.SameAs(shared),
                "绑定之后表现层用的是容器那份策略（不是自己 new 的另一份）");

            targetObject.transform.position = new Vector3(10f, 0f, 0f);
            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(10f, CameraHeight, 0f)),
                "共享策略还没设边界 → 旧行为");

            // 直接改共享策略（不碰组件）：表现层必须立刻按新边界走。
            shared.SetBounds(Vector2.zero, new Vector2(40f, 20f), new Vector2(4f, 4f), Vector2.zero);
            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(8f, CameraHeight, 0f)),
                "容器侧（WorldSceneState / 将来的构图切换）设的边界立刻生效");
        }

        [Test]
        public void BindConstraintPolicy_ComponentBounds_ArePushedIntoTheSharedPolicy()
        {
            // 边界的数据源是作者在场景里摆的那几列（PRP §2.3 方案①）：绑定时要灌进共享策略，
            // 否则容器侧看到的是「没有边界」，A6 的另一半（对话构图切换）拿不到边界。
            follow.SetConstraintBounds(new Vector2(1f, 2f), new Vector2(30f, 10f), new Vector2(4f, 4f));
            var shared = new CameraConstraintPolicy();

            follow.BindConstraintPolicy(shared);

            Assert.That(shared.HasBounds, Is.True);
            Assert.That(shared.Rules.CenterBounds, Is.EqualTo(new Rect(-14f, -3f, 30f, 10f)));
        }

        [Test]
        public void BindConstraintPolicy_Null_KeepsTheCurrentPolicy()
        {
            CameraConstraintPolicy before = follow.ConstraintPolicy;

            Assert.That(() => follow.BindConstraintPolicy(null), Throws.Nothing);
            Assert.That(follow.ConstraintPolicy, Is.SameAs(before), "空引用不改变现状（场景里没接线时就是这样）");
        }

        // ---------------------------------------------------------------- 策略本身的两条边界

        [Test]
        public void CameraConstraintPolicy_WithoutBounds_ReportsNoConstraint()
        {
            var policy = new CameraConstraintPolicy();
            var camera = new Vector3(0f, 8f, 0f);
            var followPoint = new Vector3(50f, 0f, 50f);

            Assert.That(policy.HasBounds, Is.False);
            Assert.That(policy.TryResolve(camera, followPoint, out Vector3 resolved), Is.False, "没有边界 = 明确说「不约束」");
            Assert.That(resolved, Is.EqualTo(followPoint), "false 时给的是原样值，调用方按旧行为走");
        }

        [Test]
        public void CameraConstraintPolicy_WithBounds_ResolvesAndClearRestoresNoConstraint()
        {
            var policy = new CameraConstraintPolicy();
            policy.SetBounds(Vector2.zero, new Vector2(40f, 20f), new Vector2(4f, 4f), Vector2.zero);

            Assert.That(policy.HasBounds, Is.True);
            Assert.That(policy.TryResolve(new Vector3(0f, 8f, 0f), new Vector3(10f, 0f, 0f), out Vector3 resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(new Vector3(8f, 8f, 0f)));

            policy.ClearBounds();

            Assert.That(policy.HasBounds, Is.False);
            Assert.That(policy.TryResolve(new Vector3(0f, 8f, 0f), new Vector3(10f, 0f, 0f), out _), Is.False);
        }

        [Test]
        public void SetConstraintBounds_ZeroSizedBounds_IsADegenerateBoundsNotNoBounds()
        {
            // 口径：尺寸为 0 是「退化的边界」（结果落在边界上），要取消约束得用 ClearConstraintBounds。
            follow.SetConstraintBounds(Vector2.zero, Vector2.zero);
            targetObject.transform.position = new Vector3(10f, 0f, 10f);

            Assert.That(follow.ConstraintPolicy.HasBounds, Is.True);
            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(0f, CameraHeight, 0f)));
        }

        [Test]
        public void SetOffset_SetsTheFollowOffset()
        {
            // 测试依赖 SetOffset 的确定性（EditMode 里 Start 不会跑，构图偏移要靠它设）：这里把它的语义钉住。
            follow.SetOffset(new Vector3(0f, 3f, 0f));
            Assert.That(follow.Offset, Is.EqualTo(new Vector3(0f, 3f, 0f)));

            targetObject.transform.position = new Vector3(1f, 0f, 1f);
            Assert.That(follow.ResolveDesiredPosition(), Is.EqualTo(new Vector3(1f, 3f, 1f)), "偏移参与跟随点计算");
        }
    }
}
