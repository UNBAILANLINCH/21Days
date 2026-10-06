// 职责：醉酒四档阈值与跳过概率的 EditMode 测试（49/50、79/80、99/100 三处边界逐个数到）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:70-73`：
//   正常 0-49 / 微醺 50-79（20%）/ 薄醉 80-99（40%）/ 酩酊 100（100%）。

using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class DrunkTierRulesTests
    {
        private static readonly DrunkSettings Settings = DrunkSettings.PlaceholderDefault;

        [TestCase(0, DrunkTier.Normal)]
        [TestCase(1, DrunkTier.Normal)]
        [TestCase(49, DrunkTier.Normal)]
        [TestCase(50, DrunkTier.Tipsy)]
        [TestCase(79, DrunkTier.Tipsy)]
        [TestCase(80, DrunkTier.Drunk)]
        [TestCase(99, DrunkTier.Drunk)]
        [TestCase(100, DrunkTier.DeadDrunk)]
        public void Resolve_AtEachDocumentedBoundary_MatchesTheTable(int drunkValue, DrunkTier expected)
        {
            Assert.That(DrunkTierRules.Resolve(drunkValue, Settings), Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_AboveTheDeadDrunkThreshold_StaysDeadDrunk()
        {
            // 上限之外的值不该掉回别的档（07 只写 100 是酩酊，没写超过 100）。
            Assert.That(DrunkTierRules.Resolve(120, Settings), Is.EqualTo(DrunkTier.DeadDrunk));
        }

        [Test]
        public void Resolve_NegativeValue_IsNormalNotAnException()
        {
            // 负对照：负数在数据上不该出现，但也不该让规则崩——按正常档处理。
            Assert.That(DrunkTierRules.Resolve(-1, Settings), Is.EqualTo(DrunkTier.Normal));
        }

        [TestCase(DrunkTier.Normal, 0)]
        [TestCase(DrunkTier.Tipsy, 20)]
        [TestCase(DrunkTier.Drunk, 40)]
        [TestCase(DrunkTier.DeadDrunk, 100)]
        public void SkipChancePercent_MatchesTheDocumentedTable(DrunkTier tier, int expected)
        {
            Assert.That(DrunkTierRules.SkipChancePercent(tier, Settings), Is.EqualTo(expected));
        }

        [Test]
        public void ShouldSkipTurn_Tipsy_RollNineteenSkips()
        {
            // 20% 的下边界：掷出 19 要跳过。
            Assert.That(DrunkTierRules.ShouldSkipTurn(DrunkTier.Tipsy, Settings, FixedRandomStream.ForRolls(19)), Is.True);
        }

        [Test]
        public void ShouldSkipTurn_Tipsy_RollTwentyDoesNotSkip()
        {
            // 负对照：20% 的上边界，掷出 20 不跳过。
            Assert.That(DrunkTierRules.ShouldSkipTurn(DrunkTier.Tipsy, Settings, FixedRandomStream.ForRolls(20)), Is.False);
        }

        [Test]
        public void ShouldSkipTurn_Drunk_RollThirtyNineSkips()
        {
            // 40% 的下边界。
            Assert.That(DrunkTierRules.ShouldSkipTurn(DrunkTier.Drunk, Settings, FixedRandomStream.ForRolls(39)), Is.True);
        }

        [Test]
        public void ShouldSkipTurn_Drunk_RollFortyDoesNotSkip()
        {
            // 负对照：40% 的上边界。
            Assert.That(DrunkTierRules.ShouldSkipTurn(DrunkTier.Drunk, Settings, FixedRandomStream.ForRolls(40)), Is.False);
        }

        [Test]
        public void ShouldSkipTurn_Normal_NeverSkipsAndDoesNotConsumeRandom()
        {
            // 负对照：0% 是确定事件，空剧本也不该被抽——抽了就会让后面所有随机判定错位。
            var empty = new FixedRandomStream();

            Assert.That(DrunkTierRules.ShouldSkipTurn(DrunkTier.Normal, Settings, empty), Is.False);
            Assert.That(empty.IsDrained, Is.True);
            Assert.That(empty.Remaining, Is.EqualTo(0));
        }

        [Test]
        public void ShouldSkipTurn_DeadDrunk_AlwaysSkipsAndDoesNotConsumeRandom()
        {
            var empty = new FixedRandomStream();

            Assert.That(DrunkTierRules.ShouldSkipTurn(DrunkTier.DeadDrunk, Settings, empty), Is.True);
            Assert.That(empty.IsDrained, Is.True);
        }

        [Test]
        public void ReasonOf_AndName_MatchTheTier()
        {
            Assert.That(DrunkTierRules.ReasonOf(DrunkTier.Tipsy), Is.EqualTo(DrunkSkipReason.Tipsy));
            Assert.That(DrunkTierRules.ReasonOf(DrunkTier.Drunk), Is.EqualTo(DrunkSkipReason.Drunk));
            Assert.That(DrunkTierRules.ReasonOf(DrunkTier.DeadDrunk), Is.EqualTo(DrunkSkipReason.DeadDrunk));
            Assert.That(DrunkTierRules.ReasonOf(DrunkTier.Normal), Is.EqualTo(DrunkSkipReason.None));

            Assert.That(DrunkTierRules.Name(DrunkTier.Normal), Is.EqualTo("正常"));
            Assert.That(DrunkTierRules.Name(DrunkTier.Tipsy), Is.EqualTo("微醺"));
            Assert.That(DrunkTierRules.Name(DrunkTier.Drunk), Is.EqualTo("薄醉"));
            Assert.That(DrunkTierRules.Name(DrunkTier.DeadDrunk), Is.EqualTo("酩酊"));
        }

        [Test]
        public void HintTexts_AreTheDocumentedCopy()
        {
            // 07:71-73 的三句文案逐字对照（原文用词是「醉酒状态」而不是「酩酊」）。
            Assert.That(BattleHintTexts.TipsySkip, Is.EqualTo("怪物处于微醺状态，本回合无法行动。"));
            Assert.That(BattleHintTexts.DrunkSkip, Is.EqualTo("怪物处于薄醉状态，本回合无法行动。"));
            Assert.That(BattleHintTexts.DeadDrunkSkip, Is.EqualTo("怪物处于醉酒状态，本回合无法行动。"));
            Assert.That(BattleHintTexts.ForReason(DrunkSkipReason.None), Is.Null);
        }
    }
}
