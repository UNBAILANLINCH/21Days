// 职责：核对节点、区域、完成和遮挡闸门，及三店自由顺序的动作选择。
using Game.Narrative;
using NUnit.Framework;
namespace Game.Tests.EditMode.Narrative
{
    public sealed class StoryGuideRulesTests
    {
        [TestCase("action_r0070", false)]
        [TestCase("enter_r0083", false)]
        [TestCase("enter_r0129", true)]
        [TestCase("enter_r0188", false)]
        [TestCase("enter_r0171", false)]
        [TestCase("action_r0197", false)]
        public void EntryPolicy_DoesNotTurnHumanSceneChangesIntoTasks(string stage, bool automatic)
        { Assert.That(StoryGuideRules.IsAutomaticEntry(stage), Is.EqualTo(automatic)); }
        [Test]
        public void Objective_MentorTeaching_RequestsTurnBasedBattle()
        { Assert.That(StoryGuideRules.Objective("action_r0070", true), Is.EqualTo("与李诏阳切磋，学习回合制战斗")); }
        [TestCase("enter_r0083")]
        [TestCase("talk_21006")]
        public void Objective_ZhaoShisi_RequestsFindingAndConversation(string stage)
        {
            Assert.That(StoryGuideRules.Objective(stage, true), Is.EqualTo("寻找并与赵十四交谈"));
            Assert.That(StoryGuideRules.Objective(stage, false), Is.Empty);
        }
        [Test]
        public void Select_RequiresCurrentNodeAndRegionAndUnfinishedUnblocked()
        {
            for (int bits = 0; bits < 16; bits++)
                Assert.That(StoryGuideRules.Select((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0,
                    StoryGuideRules.Action.Interact), Is.EqualTo(bits == 3 ? StoryGuideRules.Action.Interact : StoryGuideRules.Action.None));
        }
        [Test]
        public void ShopAction_RequiresSkinThenEquipmentThenRealAction()
        {
            Assert.That(StoryGuideRules.ShopAction(0, "", false, false, false, false, false, false), Is.EqualTo(StoryGuideRules.Action.Sneak));
            Assert.That(StoryGuideRules.ShopAction(0, "", true, false, false, false, false, false), Is.EqualTo(StoryGuideRules.Action.Equip));
            Assert.That(StoryGuideRules.ShopAction(0, "apprentice", true, false, false, false, false, false), Is.EqualTo(StoryGuideRules.Action.Verify));
            Assert.That(StoryGuideRules.RequiredRole(0, true, false, true), Is.EqualTo("director"));
            Assert.That(StoryGuideRules.RequiredRole(2, false, false, true), Is.EqualTo("smith"));
            Assert.That(StoryGuideRules.ShopAction(4, "guest", true, false, false, false, true, true), Is.EqualTo(StoryGuideRules.Action.Challenge));
            Assert.That(StoryGuideRules.ShopAction(1, "customer1", true, false, false, true, true, true), Is.EqualTo(StoryGuideRules.Action.None));
        }
    }
}
