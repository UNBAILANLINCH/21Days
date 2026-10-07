// 职责：BOSS 招式选择与效果的 EditMode 测试——重点是 6:3:1 的分布、可重复性与权重为 0 的招式不会被选中。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:79-84`。

using Game.Core.Simulation;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class BossSkillRulesTests
    {
        private static readonly BossSkillSettings Settings = BossSkillSettings.PlaceholderDefault;

        private const ulong Seed = 20261007UL;

        private static int[] SampleCounts(in BossSkillSettings settings, ulong seed, int draws)
        {
            var stream = new XorShiftRandomStream(seed);
            var counts = new int[4];
            for (int i = 0; i < draws; i++)
            {
                counts[(int)BossSkillRules.SelectWeighted(settings, stream)]++;
            }

            return counts;
        }

        [Test]
        public void SelectWeighted_SixThreeOneDistribution_HoldsWithinTolerance()
        {
            const int draws = 2000;
            int[] counts = SampleCounts(Settings, Seed, draws);

            // 6:3:1 → 期望 1200 / 600 / 200。
            Assert.That(counts[(int)BossSkill.Skill1], Is.InRange(1120, 1280), "招式1 次数 " + counts[1]);
            Assert.That(counts[(int)BossSkill.Skill2], Is.InRange(540, 660), "招式2 次数 " + counts[2]);
            Assert.That(counts[(int)BossSkill.Skill3], Is.InRange(150, 250), "招式3 次数 " + counts[3]);
            Assert.That(counts[(int)BossSkill.None], Is.EqualTo(0), "不该选出空招式");
            Assert.That(counts[1] + counts[2] + counts[3], Is.EqualTo(draws));
        }

        [Test]
        public void SelectWeighted_SameSeed_ProducesIdenticalSequence()
        {
            var first = new XorShiftRandomStream(Seed);
            var second = new XorShiftRandomStream(Seed);

            for (int i = 0; i < 200; i++)
            {
                Assert.That(
                    BossSkillRules.SelectWeighted(Settings, second),
                    Is.EqualTo(BossSkillRules.SelectWeighted(Settings, first)),
                    "第 " + i + " 次选招不一致");
            }
        }

        [Test]
        public void SelectWeighted_DifferentSeeds_ProduceDifferentSequences()
        {
            // 负对照：换种子结果应当不同，否则「随机」是假的（只比 200 次的前 50 次）。
            var a = new XorShiftRandomStream(1UL);
            var b = new XorShiftRandomStream(2UL);
            bool differs = false;

            for (int i = 0; i < 50 && !differs; i++)
            {
                differs = BossSkillRules.SelectWeighted(Settings, a) != BossSkillRules.SelectWeighted(Settings, b);
            }

            Assert.That(differs, Is.True);
        }

        [Test]
        public void SelectWeighted_ZeroWeightSkill_IsNeverChosen()
        {
            var onlyDrink = new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 1, 0);
            var onlyHeavy = new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 0, 1);

            int[] drinkCounts = SampleCounts(onlyDrink, Seed, 300);
            int[] heavyCounts = SampleCounts(onlyHeavy, Seed, 300);

            Assert.That(drinkCounts[(int)BossSkill.Skill2], Is.EqualTo(300));
            Assert.That(drinkCounts[(int)BossSkill.Skill1], Is.EqualTo(0));
            Assert.That(drinkCounts[(int)BossSkill.Skill3], Is.EqualTo(0));

            Assert.That(heavyCounts[(int)BossSkill.Skill3], Is.EqualTo(300));
            Assert.That(heavyCounts[(int)BossSkill.Skill2], Is.EqualTo(0));
        }

        [Test]
        public void SelectWeighted_AllWeightsZero_Throws()
        {
            // 负对照：权重全 0 选不出招式，必须炸而不是悄悄返回招式 1。
            var empty = new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 0, 0);
            var stream = new XorShiftRandomStream(Seed);

            Assert.Throws<System.InvalidOperationException>(() => BossSkillRules.SelectWeighted(empty, stream));
        }

        [Test]
        public void SelectWeighted_ConsumesExactlyOneRandomPerDraw()
        {
            // 每次选招恰好消耗一个随机数（回放对不上时能定位到第几次选招）。
            var stream = new XorShiftRandomStream(Seed);
            ulong before = stream.State;

            BossSkillRules.SelectWeighted(Settings, stream);
            ulong afterOne = stream.State;
            Assert.That(afterOne, Is.Not.EqualTo(before));

            BossSkillRules.SelectWeighted(Settings, stream);
            Assert.That(stream.State, Is.Not.EqualTo(afterOne));

            // 空剧本 + 权重全 0 之外的正常路径：抽一次就必须消耗一个数。
            var scripted = FixedRandomStream.ForRolls(0);
            BossSkillRules.SelectWeighted(Settings, scripted);
            Assert.That(scripted.IsDrained, Is.True, "一次选招只该抽一个数");
        }

        [Test]
        public void Skill1Damage_WithAndWithoutBonus()
        {
            var skills = new BossSkillSettings(10, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 6, 3, 1);

            Assert.That(BossSkillRules.Skill1Damage(skills, false), Is.EqualTo(10));
            Assert.That(BossSkillRules.Skill1Damage(skills, true), Is.EqualTo(13));
        }

        [Test]
        public void Skill3StunRounds_OnlyInDrunkTier()
        {
            // 07:82 只有薄醉态（80-99）的招式 3 才晕眩玩家。
            Assert.That(BossSkillRules.Skill3StunRounds(DrunkTier.Drunk, Settings), Is.EqualTo(1));

            // 负对照：正常 / 微醺 / 酩酊都不晕眩。
            Assert.That(BossSkillRules.Skill3StunRounds(DrunkTier.Normal, Settings), Is.EqualTo(0));
            Assert.That(BossSkillRules.Skill3StunRounds(DrunkTier.Tipsy, Settings), Is.EqualTo(0));
            Assert.That(BossSkillRules.Skill3StunRounds(DrunkTier.DeadDrunk, Settings), Is.EqualTo(0));
        }

        [Test]
        public void Describe_NamesEachSkill()
        {
            Assert.That(BossSkillRules.Describe(BossSkill.Skill1), Does.Contain("普通攻击"));
            Assert.That(BossSkillRules.Describe(BossSkill.Skill2), Does.Contain("饮酒"));
            Assert.That(BossSkillRules.Describe(BossSkill.Skill3), Does.Contain("晕眩"));
            Assert.That(BossSkillRules.Describe(BossSkill.None), Does.Contain("未知"));
        }
    }
}
