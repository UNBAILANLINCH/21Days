// 职责：真实试玩按钮验证借身份、错误推理、唯一领衣及当前店重试。
using System.Collections;
using Game.Tests.Showcase.ShopPlaytest;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace Game.Tests.Showcase.Clothing
{
    [Category("Showcase")]
    public sealed class ClothingShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "Clothing";
        [UnityTest]
        public IEnumerator Customers_ShowOnlyOwnDialogueAndChoices()
        {
            yield return Ready(1);
            for (int i = 0; i < 3; i++)
            {
                Game.ShopPlaytest.ShopPlaytestActor target = null;
                foreach (var actor in State.World.Actors) if (actor.StableId == "shop_customer" + i) target = actor;
                yield return OpenConversation(target);
                yield return ShowChoices();
                var rules = ResolveService<Game.Dialogue.DialogueRules>();
                Assert.That(rules.Current.SpeakerName, Is.EqualTo(target.DisplayName));
                Assert.That(rules.Current.Choices.Length, Is.EqualTo(3));
                foreach (var choice in rules.Current.Choices)
                    if (choice.Id.StartsWith("command")) Assert.That(choice.Id, Is.EqualTo("command" + i));
                yield return Snapshot(target.DisplayName + "对应对白与选项");
                yield return Choose("leave");
            }
        }
        [UnityTest]
        public IEnumerator Investigate_WrongClaimThenCorrectClaimAndRetry()
        {
            yield return Ready(1);
            yield return Borrow(0);
            yield return Command(0); yield return Command(3);
            yield return Check("甲不符合掌柜线索，没有发放衣物", () => State.Clothing.HasInvestigated(0) && !State.Clothing.Received);
            yield return Snapshot("错误答案拒绝");
            yield return Borrow(2);
            yield return Command(2);
            yield return Borrow(1);
            yield return Command(1); yield return Command(4);
            yield return Check("衣物领取完成，场景成果出现", () => State.Clothing.Received);
            yield return Snapshot("领衣完成");
            yield return Command(1); yield return Command(4);
            yield return Check("调查和成果保留，重复领取拒绝", () => State.Clothing.Received && State.Clothing.HasInvestigated(0));
            yield return CheckPreplacedObjects();
        }
    }
}
