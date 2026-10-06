// 职责：钉住传送点组件的两种触发方式——「进入范围即触发」与「需要交互键」，
//   以及不在这里读输入、不依赖 Physics 这两条设计约束。
// 为什么新建：PortalAnchor 是 scene 里唯一新增的可挂组件，判定逻辑必须能在 EditMode 里直接测，
//   所以测试全部用 AddComponent + 直接调 NotifyEntered / TryInteract，不碰触发体与物理。
using System;
using Game.World;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="PortalAnchor"/> 的 EditMode 测试。</summary>
    public sealed class PortalAnchorTests
    {
        private GameObject host;
        private PortalAnchor portal;
        private int triggeredCount;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("PortalAnchorTests");
            portal = host.AddComponent<PortalAnchor>();
            SetField("targetSceneKey", "yao_fangshi");
            SetField("targetSpawnId", "fangshi_street");
            triggeredCount = 0;
            portal.OnTriggered += _ => triggeredCount++;
        }

        [TearDown]
        public void TearDown()
        {
            // 组件与宿主都是本测试 new 出来的，必须清掉（unity-tests.md 的清理要求）。
            UnityEngine.Object.DestroyImmediate(host);
        }

        // ---------------------------------------------------------------- 进入范围即触发

        [Test]
        public void EnterRange_FiresOnEnterOnce()
        {
            SetField("triggerKind", PortalTriggerKind.EnterRange);

            portal.NotifyEntered();

            Assert.That(portal.HasTriggered, Is.True);
            Assert.That(triggeredCount, Is.EqualTo(1), "进门那一下就走");
            Assert.That(portal.PlayerInRange, Is.True);
        }

        [Test]
        public void EnterRange_RepeatedEnterNotification_FiresOnlyOnce()
        {
            SetField("triggerKind", PortalTriggerKind.EnterRange);

            portal.NotifyEntered();
            portal.NotifyEntered();
            portal.NotifyEntered();

            Assert.That(triggeredCount, Is.EqualTo(1), "同一个出口只能触发一次，否则会在门口反复转场");
        }

        [Test]
        public void EnterRange_ReEnterAfterLeaving_DoesNotFireAgain()
        {
            SetField("triggerKind", PortalTriggerKind.EnterRange);

            portal.NotifyEntered();
            portal.NotifyExited();
            portal.NotifyEntered();

            Assert.That(triggeredCount, Is.EqualTo(1));
            Assert.That(portal.PlayerInRange, Is.True);
        }

        [Test]
        public void EnterRange_DoesNotNeedInteraction()
        {
            SetField("triggerKind", PortalTriggerKind.EnterRange);
            portal.NotifyEntered();

            // 已经触发过了，TryInteract 返回 false（不是「需要交互」而是「已经走过」）。
            Assert.That(portal.TryInteract(), Is.False);
            Assert.That(triggeredCount, Is.EqualTo(1), "不该因为多按一次交互键又触发一次");
        }

        // ---------------------------------------------------------------- 需要交互键

        [Test]
        public void Interact_OutsideRange_Fails()
        {
            SetField("triggerKind", PortalTriggerKind.Interact);

            Assert.That(portal.TryInteract(), Is.False, "还没走到出口跟前，按键没用");
            Assert.That(portal.HasTriggered, Is.False);
            Assert.That(triggeredCount, Is.Zero);

            // 负对照：进入范围本身不该触发交互式出口。
            portal.NotifyEntered();
            Assert.That(portal.HasTriggered, Is.False, "交互式出口要在范围内按键才走");
            Assert.That(triggeredCount, Is.Zero);
        }

        [Test]
        public void Interact_InRange_FiresOnInteraction()
        {
            SetField("triggerKind", PortalTriggerKind.Interact);

            portal.NotifyEntered();
            bool fired = portal.TryInteract();

            Assert.That(fired, Is.True);
            Assert.That(portal.HasTriggered, Is.True);
            Assert.That(triggeredCount, Is.EqualTo(1));
        }

        [Test]
        public void Interact_SecondPress_DoesNotFireAgain()
        {
            SetField("triggerKind", PortalTriggerKind.Interact);
            portal.NotifyEntered();

            Assert.That(portal.TryInteract(), Is.True);
            Assert.That(portal.TryInteract(), Is.False, "走都走了，不该再触发");
            Assert.That(triggeredCount, Is.EqualTo(1));
        }

        [Test]
        public void Interact_LeavingRangeThenPressing_Fails()
        {
            // 负对照：先进入范围再离开，这时按键不该触发。
            SetField("triggerKind", PortalTriggerKind.Interact);
            portal.NotifyEntered();
            portal.NotifyExited();

            Assert.That(portal.TryInteract(), Is.False);
            Assert.That(triggeredCount, Is.Zero);
        }

        [Test]
        public void Interact_OptionalRangeRequirement_CanBeTurnedOff()
        {
            // requirePlayerInRange=false 是给「远程交互」这类接法留的口子；默认是 true。
            SetField("triggerKind", PortalTriggerKind.Interact);
            SetField("requirePlayerInRange", false);

            Assert.That(portal.TryInteract(), Is.True);
        }

        // ---------------------------------------------------------------- 配置与判定

        [Test]
        public void CanTrigger_WithoutTargetScene_False()
        {
            // 负对照：没填目标场景就是没接线，走到跟前也不该把人传送到某处。
            SetField("targetSceneKey", string.Empty);
            SetField("triggerKind", PortalTriggerKind.EnterRange);

            Assert.That(portal.CanTrigger(), Is.False);
            portal.NotifyEntered();

            Assert.That(portal.HasTriggered, Is.False);
            Assert.That(triggeredCount, Is.Zero);
        }

        [Test]
        public void ResetTriggered_AllowsFiringAgain()
        {
            // 重置关卡 / 回放需要「再走一次」。
            SetField("triggerKind", PortalTriggerKind.EnterRange);
            portal.NotifyEntered();
            portal.ResetTriggered();

            Assert.That(portal.HasTriggered, Is.False);
            portal.NotifyExited();
            portal.NotifyEntered();

            Assert.That(triggeredCount, Is.EqualTo(2));
        }

        [Test]
        public void TargetFields_AreExposedForTheCaller()
        {
            // 转场由调用方做：它要能读到目标场景键与目标出生点 id。
            SetField("targetSceneKey", "human_jingyang");
            SetField("targetSpawnId", string.Empty);

            Assert.That(portal.TargetSceneKey, Is.EqualTo("human_jingyang"));
            Assert.That(portal.TargetSpawnId, Is.Empty, "留空 = 由 WorldRules 回退到目标场景的默认出生点");
            Assert.That(portal.AnchorId, Is.Empty);
        }

        [Test]
        public void EmptyTargetSpawnId_StillTriggers()
        {
            // 目标出生点留空是合法配置（回退到默认出生点），不该被判定为「没接线」。
            SetField("targetSpawnId", string.Empty);
            SetField("triggerKind", PortalTriggerKind.EnterRange);

            portal.NotifyEntered();

            Assert.That(portal.HasTriggered, Is.True);
        }

        [Test]
        public void EnteredNotification_IsIdempotentForState()
        {
            SetField("triggerKind", PortalTriggerKind.Interact);

            portal.NotifyEntered();
            portal.NotifyEntered();

            Assert.That(portal.PlayerInRange, Is.True);
            Assert.That(portal.HasTriggered, Is.False, "交互式出口光站着不会走");
        }

        // ---------------------------------------------------------------- 辅助

        // 私有 [SerializeField] 字段在 EditMode 测试里经反射写（同 MirrorSceneBinderTests 的做法）。
        private void SetField(string field, object value)
        {
            var member = typeof(PortalAnchor).GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(member, Is.Not.Null, $"PortalAnchor 上没有字段 {field}——测试与实现不同步了");
            member.SetValue(portal, value);
        }
    }
}
