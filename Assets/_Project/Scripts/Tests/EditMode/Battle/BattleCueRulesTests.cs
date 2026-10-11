// 职责：钉住「事件 → 演出」的口径——BOSS 跳过回合闪的是 07 原文（三档各一句，只搬不改）、招式 3 / BOSS 招式 3 是加重版、
//   饮酒带「饮酒」字样与回血、晕眩、胜负、回合用尽、未知事件不抛。
// 为什么新建：BattleCueRules 是 W2a 新增的纯规则；一个被测类一个测试类。
using Game.Battle;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleCueRulesTests
    {
        [TestCase(DrunkTier.Tipsy, DrunkSkipReason.Tipsy, "怪物处于微醺状态，本回合无法行动。")]
        [TestCase(DrunkTier.Drunk, DrunkSkipReason.Drunk, "怪物处于薄醉状态，本回合无法行动。")]
        [TestCase(DrunkTier.DeadDrunk, DrunkSkipReason.DeadDrunk, "怪物处于醉酒状态，本回合无法行动。")]
        public void BossTurnSkipped_FlashesDesignOriginalText(DrunkTier tier, DrunkSkipReason reason, string original)
        {
            BattleCue cue = BattleCueRules.Cue(BattleEvent.BossTurnSkipped(tier, reason, BattleHintTexts.ForReason(reason)));

            Assert.That(cue.Kind, Is.EqualTo(BattleCueKind.BossSkip));
            Assert.That(cue.Text, Is.EqualTo(original), "07:71-73 原文，逐字");
        }

        [Test]
        public void BossTurnSkipped_WithoutHint_FallsBackToSameOriginalTable()
        {
            BattleCue cue = BattleCueRules.Cue(BattleEvent.BossTurnSkipped(DrunkTier.Tipsy, DrunkSkipReason.Tipsy, null));

            Assert.That(cue.Text, Is.EqualTo(BattleHintTexts.TipsySkip));
        }

        [Test]
        public void SkillCast_Skill3IsHeavy_Others_AreNot()
        {
            Assert.That(BattleCueRules.Cue(BattleEvent.SkillCast(PlayerSkill.Skill3, 5)).Heavy, Is.True, "招式3 加重版（07:54）");
            Assert.That(BattleCueRules.Cue(BattleEvent.SkillCast(PlayerSkill.Skill1, 1)).Heavy, Is.False);
            Assert.That(BattleCueRules.Cue(BattleEvent.SkillCast(PlayerSkill.Skill2, 2)).Heavy, Is.False);
            BattleCue cue = BattleCueRules.Cue(BattleEvent.SkillCast(PlayerSkill.Skill2, 2));
            Assert.That(cue.Kind, Is.EqualTo(BattleCueKind.PlayerStrike));
            Assert.That(cue.Amount, Is.EqualTo(2));
        }

        [Test]
        public void BossSkill_HeavyOnlyForSkill3()
        {
            BattleCue heavy = BattleCueRules.Cue(BattleEvent.BossSkillUsed(BossSkill.Skill3, 3));
            BattleCue normal = BattleCueRules.Cue(BattleEvent.BossSkillUsed(BossSkill.Skill1, 1));

            Assert.That(heavy.Kind, Is.EqualTo(BattleCueKind.BossStrike));
            Assert.That(heavy.Heavy, Is.True, "BOSS 重击加重版（07:81-82）");
            Assert.That(normal.Heavy, Is.False, "普攻：冲上去打再退回（07:79）");
        }

        [Test]
        public void BossDrank_CarriesDrinkTextDrunkAndHeal()
        {
            BattleCue cue = BattleCueRules.Cue(BattleEvent.BossDrank(30, 1));

            Assert.That(cue.Kind, Is.EqualTo(BattleCueKind.BossDrink));
            Assert.That(cue.Text, Is.EqualTo("饮酒"));
            Assert.That(cue.Amount, Is.EqualTo(30), "醉酒条上涨量");
            Assert.That(cue.SecondaryAmount, Is.EqualTo(1), "回血飘绿字");
        }

        [Test]
        public void StunAndOutcomes_MapToTheirCues()
        {
            Assert.That(BattleCueRules.Cue(BattleEvent.PlayerStunned(1)).Kind, Is.EqualTo(BattleCueKind.PlayerStun));
            Assert.That(BattleCueRules.Cue(BattleEvent.PlayerStunnedTurnSkipped(0)).Text, Is.EqualTo(BattleCueRules.PlayerStunSkipText));
            Assert.That(BattleCueRules.Cue(BattleEvent.BossDefeated()).Kind, Is.EqualTo(BattleCueKind.BossDown));
            Assert.That(BattleCueRules.Cue(BattleEvent.PlayerDefeated()).Kind, Is.EqualTo(BattleCueKind.PlayerDown));
            Assert.That(BattleCueRules.Cue(BattleEvent.RoundLimitReached(BattleOutcome.Victory)).Text, Is.EqualTo(BattleCueRules.RoundLimitVictoryText));
            Assert.That(BattleCueRules.Cue(BattleEvent.RoundLimitReached(BattleOutcome.Defeat)).Text, Is.EqualTo(BattleCueRules.RoundLimitDefeatText));
            BattleCue item = BattleCueRules.Cue(BattleEvent.ItemUsed("1004", 3));
            Assert.That(item.Kind, Is.EqualTo(BattleCueKind.ItemUse));
            Assert.That(item.ItemId, Is.EqualTo("1004"));
            Assert.That(item.Amount, Is.EqualTo(3));
        }

        [Test]
        public void UnknownEvent_IsNoneAndDoesNotThrow()
        {
            Assert.That(BattleCueRules.Cue(default(BattleEvent)).Kind, Is.EqualTo(BattleCueKind.None));
        }
    }
}
