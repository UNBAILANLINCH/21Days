// 职责：TurnBasedConfig 的校验逻辑测试——默认占位配置必须自洽，坏配置必须逐条点名拒绝。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:70-84`（四档区间与 6:3:1 是硬约束）；
// 数值本身等 `待策划拍板问题.md:1256` C91，但内部一致性现在就得成立。

using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class TurnBasedConfigValidationTests
    {
        /// <summary>在默认占位设置上改一项，再交给校验。</summary>
        private static BattleSettings WithPlayerSkills(in PlayerSkillSettings skills) =>
            new BattleSettings(
                BattleEntrySettings.PlaceholderDefault,
                skills,
                BossSkillSettings.PlaceholderDefault,
                DrunkSettings.PlaceholderDefault,
                BattleFlowSettings.PlaceholderDefault,
                true,
                true);

        private static BattleSettings WithBossSkills(in BossSkillSettings skills) =>
            new BattleSettings(
                BattleEntrySettings.PlaceholderDefault,
                PlayerSkillSettings.PlaceholderDefault,
                skills,
                DrunkSettings.PlaceholderDefault,
                BattleFlowSettings.PlaceholderDefault,
                true,
                true);

        private static BattleSettings WithDrunk(in DrunkSettings drunk) =>
            new BattleSettings(
                BattleEntrySettings.PlaceholderDefault,
                PlayerSkillSettings.PlaceholderDefault,
                BossSkillSettings.PlaceholderDefault,
                drunk,
                BattleFlowSettings.PlaceholderDefault,
                true,
                true);

        private static BattleSettings WithFlow(in BattleFlowSettings flow) =>
            new BattleSettings(
                BattleEntrySettings.PlaceholderDefault,
                PlayerSkillSettings.PlaceholderDefault,
                BossSkillSettings.PlaceholderDefault,
                DrunkSettings.PlaceholderDefault,
                flow,
                true,
                true);

        [Test]
        public void PlaceholderDefaults_HaveNoIssue()
        {
            Assert.That(TurnBasedConfigValidation.Validate(BattleSettings.PlaceholderDefault), Is.Null);
        }

        [Test]
        public void RageMaxBelowSkill3Cost_IsRejected()
        {
            // 怒气上限 2 < 招式 3 消耗 3 → 招式 3 永远放不出来。
            var skills = new PlayerSkillSettings(2, 0, 1, 1, 1, 2, 50, 0, 3, 3, 1, 3, true);

            Assert.That(TurnBasedConfigValidation.Validate(WithPlayerSkills(skills)), Does.Contain("怒气上限"));
        }

        [Test]
        public void Skill1WithRageCost_IsRejected()
        {
            // 07:50 招式 1 是「无消耗」，配成有消耗就是与原文冲突。
            var skills = new PlayerSkillSettings(3, 1, 1, 1, 1, 2, 50, 0, 3, 3, 1, 3, true);

            Assert.That(TurnBasedConfigValidation.Validate(WithPlayerSkills(skills)), Does.Contain("招式 1"));
        }

        [Test]
        public void NegativeDamage_IsRejected()
        {
            var skills = new PlayerSkillSettings(3, 0, 1, -1, 1, 2, 50, 0, 3, 3, 1, 3, true);

            Assert.That(TurnBasedConfigValidation.Validate(WithPlayerSkills(skills)), Does.Contain("伤害"));
        }

        [Test]
        public void HealReductionPercentOutOfRange_IsRejected()
        {
            var skills = new PlayerSkillSettings(3, 0, 1, 1, 1, 2, 150, 0, 3, 3, 1, 3, true);

            Assert.That(TurnBasedConfigValidation.Validate(WithPlayerSkills(skills)), Does.Contain("减疗"));
        }

        [Test]
        public void DrunkThresholdsOutOfOrder_AreRejected()
        {
            // 薄醉下界不大于微醺下界（把 80 写成 40）。
            var drunk = new DrunkSettings(50, 40, 100, 100, 20, 40, 100, 50, 2, true);

            Assert.That(TurnBasedConfigValidation.Validate(WithDrunk(drunk)), Does.Contain("薄醉"));
        }

        [Test]
        public void DeadDrunkThresholdBelowDrunkThreshold_IsRejected()
        {
            var drunk = new DrunkSettings(50, 80, 70, 100, 20, 40, 100, 50, 2, true);

            Assert.That(TurnBasedConfigValidation.Validate(WithDrunk(drunk)), Does.Contain("酩酊"));
        }

        [Test]
        public void MaxDrunkValueBelowDeadDrunkThreshold_IsRejected()
        {
            // 上限 90 < 酩酊 100 → 酩酊态永远进不去。
            var drunk = new DrunkSettings(50, 80, 100, 90, 20, 40, 100, 50, 2, true);

            Assert.That(TurnBasedConfigValidation.Validate(WithDrunk(drunk)), Does.Contain("上限"));
        }

        [Test]
        public void SkipChanceOutOfRange_IsRejected()
        {
            var drunk = new DrunkSettings(50, 80, 100, 100, 120, 40, 100, 50, 2, true);

            Assert.That(TurnBasedConfigValidation.Validate(WithDrunk(drunk)), Does.Contain("概率"));
        }

        [Test]
        public void NonPositiveDeadDrunkDuration_IsRejected()
        {
            var drunk = new DrunkSettings(50, 80, 100, 100, 20, 40, 100, 50, 0, true);

            Assert.That(TurnBasedConfigValidation.Validate(WithDrunk(drunk)), Does.Contain("持续回合数"));
        }

        [Test]
        public void AllBossWeightsZero_IsRejected()
        {
            var skills = new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 0, 0);

            Assert.That(TurnBasedConfigValidation.Validate(WithBossSkills(skills)), Does.Contain("权重"));
        }

        [Test]
        public void NegativeBossWeight_IsRejected()
        {
            var skills = new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, -6, 3, 1);

            Assert.That(TurnBasedConfigValidation.Validate(WithBossSkills(skills)), Does.Contain("权重"));
        }

        [Test]
        public void HealPercentOutOfRange_IsRejected()
        {
            var skills = new BossSkillSettings(1, 30, 150, HealthPercentBase.MaxHealth, 30, 3, 1, 6, 3, 1);

            Assert.That(TurnBasedConfigValidation.Validate(WithBossSkills(skills)), Does.Contain("回复百分比"));
        }

        [Test]
        public void NegativeRoundLimit_IsRejected()
        {
            var flow = new BattleFlowSettings(-1, RoundLimitOutcome.CompareHealth, StunTurnPolicy.SkipTurn);

            Assert.That(TurnBasedConfigValidation.Validate(WithFlow(flow)), Does.Contain("回合上限"));
        }

        [Test]
        public void SneakLossPercentOutOfRange_IsRejected()
        {
            var settings = new BattleSettings(
                new BattleEntrySettings(120, HealthPercentBase.MaxHealth),
                PlayerSkillSettings.PlaceholderDefault,
                BossSkillSettings.PlaceholderDefault,
                DrunkSettings.PlaceholderDefault,
                BattleFlowSettings.PlaceholderDefault,
                true,
                true);

            Assert.That(TurnBasedConfigValidation.Validate(settings), Does.Contain("偷袭扣血"));
        }

        [Test]
        public void ZeroWeightsOnTwoSkills_AreAcceptedAsLongAsOneRemains()
        {
            // 负对照的反面：只要还有一个招式有权重就不是错误（例如策划想临时只让它饮酒）。
            var skills = new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 1, 0);

            Assert.That(TurnBasedConfigValidation.Validate(WithBossSkills(skills)), Is.Null);
        }
    }
}
