// 职责：钉住 BattleActor.StandingHeadPosition——战斗界面的头顶条与飘字锚在「站立时的头顶」，角色倒地 / 冲出去之后这个位置不变。
//   负对照：头顶锚点本身（Head.position）确实跟着姿势走了，说明直接跟锚点就会把条带走（20261007-194728 回放「胜利一瞬」里
//   BOSS 倒地，名字 / 状态字 / 两根条被带到屏幕右缘截断）。
// 为什么新建：BattleActor 之前没有 EditMode 测试（演出是补间，靠回放看）；这一条是纯几何，适合在 EditMode 钉住。
//   头顶锚点是序列化私有字段，Game.Runtime 未对测试程序集开 InternalsVisibleTo，经反射赋值（同 DialogueInteractableTests 的做法）。
using System.Reflection;
using Game.Battle;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleActorTests
    {
        private const float HeadHeight = 2.4f;

        /// <summary>BattleActor 倒地转到的角度（朝左的 BOSS 为负）。</summary>
        private const float FallenAngle = -82f;

        private GameObject host;
        private BattleActor actor;
        private Transform head;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("BossSlot");
            host.transform.position = new Vector3(3f, -1000f, 0f);
            head = new GameObject("Head").transform;
            head.SetParent(host.transform, false);
            head.localPosition = new Vector3(0f, HeadHeight, 0f);
            actor = host.AddComponent<BattleActor>();
            FieldInfo field = typeof(BattleActor).GetField("head", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "前置：BattleActor 的头顶锚点字段名是 head");
            field.SetValue(actor, head);
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void StandingHeadPosition_AfterFallingOver_StaysAboveHome()
        {
            Vector3 expected = actor.HomePosition + Vector3.up * HeadHeight;

            host.transform.rotation = Quaternion.Euler(0f, 0f, FallenAngle);

            Assert.That(Vector3.Distance(actor.StandingHeadPosition, expected), Is.LessThan(1e-4f), "倒地后仍是站立时的头顶");
            Assert.That(Vector3.Distance(head.position, expected), Is.GreaterThan(HeadHeight), "负对照：头顶锚点本身已横甩出去");
        }

        [Test]
        public void StandingHeadPosition_AfterDashingAway_StaysAboveHome()
        {
            Vector3 expected = actor.HomePosition + Vector3.up * HeadHeight;

            host.transform.position = actor.HomePosition + Vector3.left * 4f; // 冲到对手身前

            Assert.That(Vector3.Distance(actor.StandingHeadPosition, expected), Is.LessThan(1e-4f), "冲出去时条留在原位");
            Assert.That(head.position.x, Is.EqualTo(expected.x - 4f).Within(1e-4f), "负对照：头顶锚点本身跟着冲出去了");
        }
    }
}
