// 职责：道具使用判定（拥有 + 本场没用过）的 EditMode 测试。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:42`（可用则高亮、用过了置暗、未拥有不显示）、
// :58（施放招式之前使用道具）。

using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class ItemUseRulesTests
    {
        [Test]
        public void Decide_OwnedAndUnused_Allows()
        {
            ItemUseDecision decision = ItemUseRules.Decide(true, false, true);

            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.Reject, Is.EqualTo(ItemUseReject.None));
            Assert.That(decision.Describe(), Does.Contain("可以使用"));
        }

        [Test]
        public void Decide_NotOwned_Refuses()
        {
            // 负对照：07:42「未拥有……则不显示」，自然也不能用。
            ItemUseDecision decision = ItemUseRules.Decide(false, false, true);

            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reject, Is.EqualTo(ItemUseReject.NotOwned));
            Assert.That(decision.Describe(), Does.Contain("未拥有"));
        }

        [Test]
        public void Decide_UsedThisBattle_Refuses()
        {
            // 负对照：07:42「用过了则置暗」，一次战斗内每件只能用一次。
            ItemUseDecision decision = ItemUseRules.Decide(true, true, true);

            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reject, Is.EqualTo(ItemUseReject.UsedThisBattle));
        }

        [Test]
        public void Decide_UsedButOncePerBattleDisabled_Allows()
        {
            // 负对照的反面：把「一场一次」关掉之后，同样的输入应当放行——证明这条规则真的来自配置。
            Assert.That(ItemUseRules.Decide(true, true, false).Allowed, Is.True);
        }

        [Test]
        public void Describe_CoversEveryRejectReason()
        {
            Assert.That(ItemUseRules.Describe(ItemUseReject.UsedThisBattle), Does.Contain("已经用过"));
            Assert.That(ItemUseRules.Describe(ItemUseReject.WrongPhase), Does.Contain("施放招式之前"));
            Assert.That(ItemUseRules.Describe(ItemUseReject.InvalidItem), Does.Contain("道具 id"));
            Assert.That(ItemUseRules.Describe(ItemUseReject.None), Does.Contain("未拒绝"));
        }
    }
}
