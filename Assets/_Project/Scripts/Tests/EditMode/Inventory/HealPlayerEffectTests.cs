// 职责：钉住占位治疗效果 HealPlayerEffect——目标口在 / 不在、满血、已倒下、参数非法这几种情况分别回什么。
// 为什么新建：一个被测类一个测试类；它是本波次**唯一接了执行者的效果**，
//   它的分支决定了玩家用治疗药水时会看到哪句话（不能只留结果码）。
// 日志断言：「目标口缺失」那一支会打一条 Error（设计如此：不许静默失败），UTF 把未声明的 Error 判为失败，
//   所以那两条用例里用 LogAssert.Expect 显式声明（同 BattleFlowTests 的做法）。正则只取唯一命中的中段子串，
//   文案一改就跟着改这一处。
using System.Text.RegularExpressions;
using Game.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Inventory
{
    public sealed class HealPlayerEffectTests
    {
        private const int Potion = 1004;

        [Test]
        public void Apply_WhenHurt_HealsAndReportsTheAmount()
        {
            var target = new FakeTarget(health: 1, maxHealth: 3);
            var effect = new HealPlayerEffect(2);

            ItemEffectOutcome outcome = effect.Apply(new ItemEffectContext(Potion, effect.EffectId, target));

            Assert.That(outcome.Status, Is.EqualTo(ItemEffectStatus.Applied));
            Assert.That(outcome.Amount, Is.EqualTo(2));
            Assert.That(outcome.Reason, Is.Empty, "成功时不给玩家看原因");
            Assert.That(target.CurrentHealth, Is.EqualTo(3));
        }

        [Test]
        public void Apply_WhenAlreadyAtFullHealth_IsRejectedWithoutChangingAnything()
        {
            var target = new FakeTarget(health: 3, maxHealth: 3);

            ItemEffectOutcome outcome = new HealPlayerEffect(2).Apply(new ItemEffectContext(Potion, "heal.player_hp", target));

            Assert.That(outcome.Status, Is.EqualTo(ItemEffectStatus.Rejected));
            Assert.That(outcome.Reason, Is.EqualTo("生命已经满了"));
            Assert.That(outcome.Amount, Is.Zero);
            Assert.That(target.CurrentHealth, Is.EqualTo(3));
        }

        [Test]
        public void Apply_WhenDown_IsRejectedWithItsOwnReason()
        {
            // 已倒下与满血都回「实际回复 0」，提示不能混为一谈（靠治疗前读到的生命区分）。
            var target = new FakeTarget(health: 0, maxHealth: 3);

            ItemEffectOutcome outcome = new HealPlayerEffect(2).Apply(new ItemEffectContext(Potion, "heal.player_hp", target));

            Assert.That(outcome.Status, Is.EqualTo(ItemEffectStatus.Rejected));
            Assert.That(outcome.Reason, Is.EqualTo("你已经倒下了"));
        }

        // 玩家生命口没接（独立测试作用域会走到这一支）：回 BadTarget + 非空中文原因，绝不静默。
        // 这一支刻意打一条 Error（不许静默失败），先声明它，否则 UTF 会把未声明的 Error 判成本用例失败。
        [Test]
        public void Apply_WhenTheTargetIsMissing_ReportsBadTarget()
        {
            LogAssert.Expect(LogType.Error, new Regex("玩家生命口.*没接进来"));

            ItemEffectOutcome outcome = new HealPlayerEffect(2).Apply(new ItemEffectContext(Potion, "heal.player_hp", null));

            Assert.That(outcome.Status, Is.EqualTo(ItemEffectStatus.BadTarget));
            Assert.That(outcome.Reason, Is.EqualTo("治疗口还没接上"));
        }

        [Test]
        public void Apply_WithNullContext_ReportsBadTarget()
        {
            // 这条**不**打 Error（context 为 null 是在进效果之前就该拦住的调用错误，本效果只回结果码），
            // 所以这里不声明 LogAssert；万一将来它开始打日志，这条会红，正好提醒补声明。
            ItemEffectOutcome outcome = new HealPlayerEffect(2).Apply(null);

            Assert.That(outcome.Status, Is.EqualTo(ItemEffectStatus.BadTarget));
            Assert.That(outcome.Reason, Is.Not.Empty);
        }

        [Test]
        public void Constructor_WithNonPositiveAmount_FallsBackToThePlaceholder()
        {
            // 0 点的「治疗」没有意义，而且会让玩家白扣一件道具。
            Assert.That(new HealPlayerEffect(0).Amount, Is.EqualTo(HealPlayerEffect.PlaceholderHealAmount));
            Assert.That(new HealPlayerEffect(-5).Amount, Is.EqualTo(HealPlayerEffect.PlaceholderHealAmount));
        }

        [Test]
        public void EffectId_IsThePlaceholderId()
        {
            Assert.That(new HealPlayerEffect().EffectId, Is.EqualTo("heal.player_hp"));
            Assert.That(new HealPlayerEffect().EffectId, Is.EqualTo(HealPlayerEffect.PlaceholderEffectId));
        }

        /// <summary>可写的假目标口。</summary>
        private sealed class FakeTarget : IItemEffectTarget
        {
            public FakeTarget(int health, int maxHealth)
            {
                CurrentHealth = health;
                MaxHealth = maxHealth;
            }

            public int CurrentHealth { get; private set; }

            public int MaxHealth { get; }

            public int TryHeal(int amount)
            {
                if (amount <= 0 || CurrentHealth <= 0 || CurrentHealth >= MaxHealth) return 0;
                int before = CurrentHealth;
                CurrentHealth = System.Math.Min(MaxHealth, CurrentHealth + amount);
                return CurrentHealth - before;
            }
        }
    }
}
