// 职责：醉酒值状态机（继承、+30 封顶、酩酊的 -50 与持续 2 回合、概率跳过）的 EditMode 测试。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:66`（进战斗继承战斗外醉酒值）、:70-73（四档）、
// :80（BOSS 招式 2 饮酒 +30）。

using Game.Core.Simulation;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class BossDrunkRulesTests
    {
        private static readonly DrunkSettings Settings = DrunkSettings.PlaceholderDefault;

        [Test]
        public void CarryIn_DrunkValueFromOutsideTheBattle_IsInherited()
        {
            // 07:66 进战斗继承战斗外的醉酒值。
            var rules = new BossDrunkRules(100, Settings);

            Assert.That(rules.Value, Is.EqualTo(100));
            Assert.That(rules.Tier, Is.EqualTo(DrunkTier.DeadDrunk));
        }

        [Test]
        public void CarryIn_ValuesAreClampedToRange()
        {
            // 负对照：越界的继承值被夹回合法区间，而不是带着脏数据往下跑。
            Assert.That(new BossDrunkRules(999, Settings).Value, Is.EqualTo(100));
            Assert.That(new BossDrunkRules(-5, Settings).Value, Is.EqualTo(0));
        }

        [Test]
        public void Add_DrinkAccumulates_AndClampsAtCap()
        {
            var rules = new BossDrunkRules(60, Settings);

            Assert.That(rules.Add(30), Is.EqualTo(30));
            Assert.That(rules.Value, Is.EqualTo(90));
            // 封顶后实际只加了 10。
            Assert.That(rules.Add(30), Is.EqualTo(10));
            Assert.That(rules.Value, Is.EqualTo(100));
            Assert.That(rules.Add(0), Is.EqualTo(0));
            Assert.That(rules.Add(-10), Is.EqualTo(0));
        }

        [Test]
        public void BeginMonsterTurn_Normal_NeverSkips()
        {
            // 负对照：正常档（0-49）无特殊效果，跑 200 个回合一次都不跳；
            // 用空剧本证明它连随机数都不抽。
            var rules = new BossDrunkRules(10, Settings);
            var empty = new FixedRandomStream();

            for (int i = 0; i < 200; i++)
            {
                DrunkTurnOutcome outcome = rules.BeginMonsterTurn(empty);
                Assert.That(outcome.Skipped, Is.False, "第 " + i + " 回合不该跳过");
            }

            Assert.That(empty.IsDrained, Is.True);
        }

        [Test]
        public void BeginMonsterTurn_Tipsy_UsesTwentyPercentBoundaryFromFixedRoll()
        {
            DrunkTurnOutcome skip = new BossDrunkRules(60, Settings).BeginMonsterTurn(FixedRandomStream.ForRolls(19));
            Assert.That(skip.Skipped, Is.True);
            Assert.That(skip.Reason, Is.EqualTo(DrunkSkipReason.Tipsy));
            Assert.That(skip.TierAtTurnStart, Is.EqualTo(DrunkTier.Tipsy));
            Assert.That(skip.HintText, Is.EqualTo(BattleHintTexts.TipsySkip));

            // 负对照：掷出 20 不跳过，且不带提示文案。
            DrunkTurnOutcome act = new BossDrunkRules(60, Settings).BeginMonsterTurn(FixedRandomStream.ForRolls(20));
            Assert.That(act.Skipped, Is.False);
            Assert.That(act.Reason, Is.EqualTo(DrunkSkipReason.None));
            Assert.That(act.HintText, Is.Null);
        }

        [Test]
        public void BeginMonsterTurn_Drunk_UsesFortyPercentBoundaryFromFixedRoll()
        {
            DrunkTurnOutcome skip = new BossDrunkRules(85, Settings).BeginMonsterTurn(FixedRandomStream.ForRolls(39));
            Assert.That(skip.Skipped, Is.True);
            Assert.That(skip.Reason, Is.EqualTo(DrunkSkipReason.Drunk));
            Assert.That(skip.HintText, Is.EqualTo(BattleHintTexts.DrunkSkip));

            // 负对照：掷出 40 不跳过。
            DrunkTurnOutcome act = new BossDrunkRules(85, Settings).BeginMonsterTurn(FixedRandomStream.ForRolls(40));
            Assert.That(act.Skipped, Is.False);
        }

        [Test]
        public void BeginMonsterTurn_Tipsy_AggregateChanceIsAboutTwentyPercent()
        {
            // 用固定种子的确定性随机源统计：2000 个微醺回合里跳过的次数应落在 20% 附近。
            var stream = new XorShiftRandomStream(20261007UL);
            var rules = new BossDrunkRules(60, Settings);
            int skips = 0;

            for (int i = 0; i < 2000; i++)
            {
                if (rules.BeginMonsterTurn(stream).Skipped)
                {
                    skips++;
                }
            }

            Assert.That(skips, Is.InRange(320, 480), "20% ±4% 的容差（实测 " + skips + "/2000）");
        }

        [Test]
        public void BeginMonsterTurn_Drunk_AggregateChanceIsAboutFortyPercent()
        {
            var stream = new XorShiftRandomStream(20261007UL);
            var rules = new BossDrunkRules(85, Settings);
            int skips = 0;

            for (int i = 0; i < 2000; i++)
            {
                if (rules.BeginMonsterTurn(stream).Skipped)
                {
                    skips++;
                }
            }

            Assert.That(skips, Is.InRange(640, 960), "40% ±8% 的容差（实测 " + skips + "/2000）");
        }

        [Test]
        public void BeginMonsterTurn_SameSeed_ProducesIdenticalSkipPattern()
        {
            // 可重复性：同一个种子跑两遍，逐回合结果必须完全相同。
            var first = new BossDrunkRules(60, Settings);
            var second = new BossDrunkRules(60, Settings);
            var streamA = new XorShiftRandomStream(777UL);
            var streamB = new XorShiftRandomStream(777UL);

            for (int i = 0; i < 200; i++)
            {
                Assert.That(
                    second.BeginMonsterTurn(streamB).Skipped,
                    Is.EqualTo(first.BeginMonsterTurn(streamA).Skipped),
                    "第 " + i + " 回合结果不一致");
            }
        }

        [Test]
        public void BeginMonsterTurn_DeadDrunk_DropsFiftyAndLastsTwoRounds()
        {
            var rules = new BossDrunkRules(100, Settings);
            var empty = new FixedRandomStream();

            // 第一回合：进入酩酊 → 100% 跳过、醉酒 -50、持续期从 2 扣到 1。
            DrunkTurnOutcome first = rules.BeginMonsterTurn(empty);
            Assert.That(first.Skipped, Is.True);
            Assert.That(first.Reason, Is.EqualTo(DrunkSkipReason.DeadDrunk));
            Assert.That(first.TierAtTurnStart, Is.EqualTo(DrunkTier.DeadDrunk));
            Assert.That(first.DrunkValueAfter, Is.EqualTo(50));
            Assert.That(first.DrunkValueDropped, Is.True);
            Assert.That(first.DeadDrunkRoundsRemaining, Is.EqualTo(1));
            Assert.That(first.HintText, Is.EqualTo(BattleHintTexts.DeadDrunkSkip));
            Assert.That(rules.Value, Is.EqualTo(50));

            // 第二回合：持续期内仍然 100% 跳过；-50 是进入酩酊时降一次，所以这一回合不再降。
            DrunkTurnOutcome second = rules.BeginMonsterTurn(empty);
            Assert.That(second.Skipped, Is.True);
            Assert.That(second.Reason, Is.EqualTo(DrunkSkipReason.DeadDrunk));
            Assert.That(second.DrunkValueAfter, Is.EqualTo(50));
            Assert.That(second.DrunkValueDropped, Is.False);
            Assert.That(second.DeadDrunkRoundsRemaining, Is.EqualTo(0));
            Assert.That(second.TierAtTurnStart, Is.EqualTo(DrunkTier.Tipsy), "此时醉酒值 50，已回到微醺档");

            // 第三回合：持续期结束，回到微醺档的概率（掷 99 不跳过）。
            DrunkTurnOutcome third = rules.BeginMonsterTurn(FixedRandomStream.ForRolls(99));
            Assert.That(third.Skipped, Is.False);
            Assert.That(third.Reason, Is.EqualTo(DrunkSkipReason.None));
        }

        [Test]
        public void BeginMonsterTurn_DeadDrunk_SecondRoundStillSkipsEvenIfRollWouldNot()
        {
            // 负对照：持续期的第二回合不掷骰子，用空剧本证明它没有消耗随机数。
            var rules = new BossDrunkRules(100, Settings);
            var empty = new FixedRandomStream();

            rules.BeginMonsterTurn(empty);
            DrunkTurnOutcome second = rules.BeginMonsterTurn(empty);

            Assert.That(second.Skipped, Is.True);
            Assert.That(empty.IsDrained, Is.True);
        }

        [Test]
        public void BeginMonsterTurn_DeadDrunkAgainAfterDrinking_ReentersTheState()
        {
            var rules = new BossDrunkRules(100, Settings);
            var empty = new FixedRandomStream();

            rules.BeginMonsterTurn(empty);   // 进入酩酊：100 → 50，剩余 1
            rules.BeginMonsterTurn(empty);   // 持续期第二回合：剩余 0
            rules.Add(50);                   // 再喝到 100

            DrunkTurnOutcome again = rules.BeginMonsterTurn(empty);
            Assert.That(again.Skipped, Is.True);
            Assert.That(again.Reason, Is.EqualTo(DrunkSkipReason.DeadDrunk));
            Assert.That(again.DrunkValueAfter, Is.EqualTo(50));
            Assert.That(again.DeadDrunkRoundsRemaining, Is.EqualTo(1));
        }

        [Test]
        public void BeginMonsterTurn_DeadDrunk_WhenConfiguredToLowerEveryRound_KeepsDropping()
        {
            // 原文没写 -50 是每次还是一进入就降；把开关拨到另一侧，行为应随之改变。
            var settings = new DrunkSettings(50, 80, 100, 100, 20, 40, 100, 50, 2, false);
            var rules = new BossDrunkRules(100, settings);
            var empty = new FixedRandomStream();

            DrunkTurnOutcome first = rules.BeginMonsterTurn(empty);
            DrunkTurnOutcome second = rules.BeginMonsterTurn(empty);

            Assert.That(first.DrunkValueAfter, Is.EqualTo(50));
            Assert.That(second.DrunkValueAfter, Is.EqualTo(0), "第二回合继续降 50（另一种读法）");
        }

        [Test]
        public void SkipChancePercent_FollowsCurrentTier()
        {
            Assert.That(new BossDrunkRules(10, Settings).SkipChancePercent, Is.EqualTo(0));
            Assert.That(new BossDrunkRules(60, Settings).SkipChancePercent, Is.EqualTo(20));
            Assert.That(new BossDrunkRules(85, Settings).SkipChancePercent, Is.EqualTo(40));
            Assert.That(new BossDrunkRules(100, Settings).SkipChancePercent, Is.EqualTo(100));
        }

        [Test]
        public void Set_OverridesValueAndClamps()
        {
            var rules = new BossDrunkRules(0, Settings);
            rules.Set(80);
            Assert.That(rules.Tier, Is.EqualTo(DrunkTier.Drunk));
            rules.Set(9999);
            Assert.That(rules.Value, Is.EqualTo(100));
        }
    }
}
