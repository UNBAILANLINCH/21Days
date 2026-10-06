// 职责：BOSS 侧状态（生命、饮酒回血、减疗、招式 1 增伤挂起）的 EditMode 测试。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:52`（减少对方 50% 治疗效果）、
// :66（继承醉酒值）、:80（饮酒 +30 醉酒 / 回 10% 生命 / 下次招式 1 +30%）。

using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class BossBattleRulesTests
    {
        private static readonly BossSkillSettings Skills = BossSkillSettings.PlaceholderDefault;
        private static readonly DrunkSettings Drunk = DrunkSettings.PlaceholderDefault;

        private static BossBattleRules NewBoss(
            int maxHealth = 100,
            int health = 100,
            int drunkValue = 0,
            bool inheritsDrunkValue = true,
            BossSkillSettings? skills = null) =>
            new BossBattleRules(
                new BossBattleSnapshot(maxHealth, health, drunkValue),
                skills ?? Skills,
                Drunk,
                inheritsDrunkValue);

        [Test]
        public void PercentOfHealth_MaxBaseAndCurrentBase_Differ()
        {
            BossBattleRules boss = NewBoss(100, 50);

            Assert.That(boss.PercentOfHealth(20, HealthPercentBase.MaxHealth), Is.EqualTo(20));
            // 负对照：换成当前生命基数，同样 20% 只有 10。
            Assert.That(boss.PercentOfHealth(20, HealthPercentBase.CurrentHealth), Is.EqualTo(10));
            Assert.That(boss.PercentOfHealth(0, HealthPercentBase.MaxHealth), Is.EqualTo(0));
        }

        [Test]
        public void ApplyDamage_ClampsAtZeroAndIgnoresNonPositive()
        {
            BossBattleRules boss = NewBoss(100, 30);

            boss.ApplyDamage(10);
            Assert.That(boss.Health, Is.EqualTo(20));
            Assert.That(boss.IsDefeated, Is.False);

            boss.ApplyDamage(0);
            boss.ApplyDamage(-10);
            Assert.That(boss.Health, Is.EqualTo(20), "0 与负数不该改血量");

            boss.ApplyDamage(999);
            Assert.That(boss.Health, Is.EqualTo(0));
            Assert.That(boss.IsDefeated, Is.True);
        }

        [Test]
        public void Heal_TenPercentOfMaxHealth()
        {
            // 07:80「回复10%生命值」
            BossBattleRules boss = NewBoss(100, 50);

            int healed = boss.Heal(10, HealthPercentBase.MaxHealth);

            Assert.That(healed, Is.EqualTo(10));
            Assert.That(boss.Health, Is.EqualTo(60));
        }

        [Test]
        public void Heal_DoesNotExceedMaxHealth()
        {
            // 负对照：满血附近回血只补到上限，返回实际回血量。
            BossBattleRules boss = NewBoss(100, 98);

            int healed = boss.Heal(10, HealthPercentBase.MaxHealth);

            Assert.That(healed, Is.EqualTo(2));
            Assert.That(boss.Health, Is.EqualTo(100));
        }

        [Test]
        public void Heal_WithFiftyPercentReduction_IsHalved()
        {
            // 07:52 玩家招式 2「减少对方50%治疗效果」。
            BossBattleRules boss = NewBoss(100, 50);
            boss.ReceiveHealReduction(50, 0);

            int healed = boss.Heal(10, HealthPercentBase.MaxHealth);

            Assert.That(healed, Is.EqualTo(5));
            Assert.That(boss.Health, Is.EqualTo(55));
        }

        [Test]
        public void Heal_WithOddAmount_FloorsTowardZero()
        {
            // 35 上限的 10% = 3（向下取整）；再减半：3 - 3*50/100 = 2（整数运算各步都向下取整）。
            BossBattleRules boss = NewBoss(35, 0);
            Assert.That(boss.Heal(10, HealthPercentBase.MaxHealth), Is.EqualTo(3));

            BossBattleRules reduced = NewBoss(35, 0);
            reduced.ReceiveHealReduction(50, 0);
            Assert.That(reduced.Heal(10, HealthPercentBase.MaxHealth), Is.EqualTo(2));
        }

        [Test]
        public void HealReduction_WithRounds_ExpiresAfterThoseBossTurns()
        {
            BossBattleRules boss = NewBoss(100, 0);
            boss.ReceiveHealReduction(50, 2);

            Assert.That(boss.HealReductionRoundsRemaining, Is.EqualTo(2));
            Assert.That(boss.Heal(10, HealthPercentBase.MaxHealth), Is.EqualTo(5));

            boss.TickHealReduction();
            Assert.That(boss.HealReductionPercent, Is.EqualTo(50), "第一个回合结束后还没到期");

            boss.TickHealReduction();
            Assert.That(boss.HealReductionPercent, Is.EqualTo(0));
            Assert.That(boss.HealReductionRoundsRemaining, Is.EqualTo(0));

            // 负对照：到期之后回血恢复满额。
            Assert.That(boss.Heal(10, HealthPercentBase.MaxHealth), Is.EqualTo(10));
        }

        [Test]
        public void HealReduction_WithZeroRounds_LastsUntilTheEndOfBattle()
        {
            BossBattleRules boss = NewBoss(100, 0);
            boss.ReceiveHealReduction(50, 0);

            Assert.That(boss.HealReductionIsPermanent, Is.True);
            for (int i = 0; i < 5; i++)
            {
                boss.TickHealReduction();
            }

            Assert.That(boss.HealReductionPercent, Is.EqualTo(50));
            Assert.That(boss.Heal(10, HealthPercentBase.MaxHealth), Is.EqualTo(5));
        }

        [Test]
        public void HealReduction_SecondApplication_OverwritesInsteadOfStacking()
        {
            // 原文没写叠加规则，占位取覆盖：30% 之后再挂 50%，结果就是 50%。
            BossBattleRules boss = NewBoss(100, 0);
            boss.ReceiveHealReduction(30, 3);
            boss.ReceiveHealReduction(50, 1);

            Assert.That(boss.HealReductionPercent, Is.EqualTo(50));
            Assert.That(boss.HealReductionRoundsRemaining, Is.EqualTo(1));
        }

        [Test]
        public void Skill1DamageBonus_IsGrantedThenConsumedOnce()
        {
            var skills = new BossSkillSettings(10, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 6, 3, 1);
            BossBattleRules boss = NewBoss(skills: skills);

            Assert.That(boss.NextSkill1DamageBonusPending, Is.False);
            Assert.That(boss.Skill1DamageNow(), Is.EqualTo(10));
            // 负对照：没挂加成时吃不到加成。
            Assert.That(boss.ConsumeSkill1DamageBonus(), Is.False);

            boss.GrantSkill1DamageBonus();
            Assert.That(boss.Skill1DamageNow(), Is.EqualTo(13), "10 + 30%");
            Assert.That(boss.ConsumeSkill1DamageBonus(), Is.True);
            Assert.That(boss.Skill1DamageNow(), Is.EqualTo(10), "「下次」只用一次");
            Assert.That(boss.ConsumeSkill1DamageBonus(), Is.False);
        }

        [Test]
        public void Drunk_IsInheritedOnlyWhenConfigured()
        {
            BossBattleRules inherited = NewBoss(drunkValue: 100);
            Assert.That(inherited.DrunkValue, Is.EqualTo(100));
            Assert.That(inherited.Tier, Is.EqualTo(DrunkTier.DeadDrunk));

            // 负对照：把继承开关关掉，战斗从 0 醉酒开始（07:66 的接线选择）。
            BossBattleRules fresh = NewBoss(drunkValue: 100, inheritsDrunkValue: false);
            Assert.That(fresh.DrunkValue, Is.EqualTo(0));
            Assert.That(fresh.Tier, Is.EqualTo(DrunkTier.Normal));
        }

        [Test]
        public void Drunk_DrinkingRaisesValueAndStopsAtCap()
        {
            BossBattleRules boss = NewBoss(drunkValue: 90);

            Assert.That(boss.Drunk.Add(30), Is.EqualTo(10));
            Assert.That(boss.DrunkValue, Is.EqualTo(100));
            Assert.That(boss.Tier, Is.EqualTo(DrunkTier.DeadDrunk));
        }

        [Test]
        public void Snapshot_InvalidValues_Throw()
        {
            // 负对照：脏快照在构造时就该炸。
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new BossBattleSnapshot(0, 0, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new BossBattleSnapshot(10, 11, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new BossBattleSnapshot(10, 10, -1));
        }
    }
}
