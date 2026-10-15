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
            foreach (var actor in State.World.Actors)
                if (actor.StableId == "shop_station_1")
                {
                    Assert.That(actor.CanInteract, Is.False, "旧柜台不再冒充裁衣匠交谈");
                    foreach (var label in actor.GetComponentsInChildren<TMPro.TMP_Text>(true))
                        Assert.That(label.gameObject.activeSelf, Is.False, "旧柜台浮字不能盖到顾客头上");
                }
            for (int i = 0; i < 3; i++)
            {
                Game.ShopPlaytest.ShopPlaytestActor target = null;
                foreach (var actor in State.World.Actors) if (actor.StableId == "shop_customer" + i) target = actor;
                yield return OpenConversation(target);
                yield return ShowChoices();
                var rules = ResolveService<Game.Dialogue.DialogueRules>();
                Assert.That(rules.Current.SpeakerName, Is.EqualTo(target.DisplayName));
                Assert.That(rules.Current.Choices.Length, Is.EqualTo(1));
                Assert.That(rules.Current.Choices[0].Id, Is.EqualTo("leave"));
                Assert.That(target.CanInteractSecondary, Is.False, "对白期间借用按钮让位");
                yield return Snapshot(target.DisplayName + "对应对白与选项");
                yield return Choose("leave");
            }
        }
        [UnityTest]
        public IEnumerator Investigate_WrongClaimThenCorrectClaimAndRetry()
        {
            yield return Ready(1);
            yield return Borrow(0);
            yield return TalkToTailor("头一回来", false);
            yield return Check("甲不符合掌柜线索，没有发放衣物", () => State.Clothing.HasInvestigated(0) && !State.Clothing.Received);
            yield return Snapshot("裁衣匠认出甲_没有订衣");
            yield return Borrow(2);
            yield return TalkToTailor("陪人来的", false);
            yield return Borrow(1);
            yield return Command(4);
            yield return Check("衣物领取完成，场景成果出现", () => State.Clothing.Received);
            yield return Snapshot("领衣完成");
            yield return TalkToTailor("已经取走", false);
            yield return Check("调查和成果保留，重复领取拒绝", () => State.Clothing.Received && State.Clothing.HasInvestigated(0));
            yield return CheckPreplacedObjects();
        }
        [UnityTest]
        public IEnumerator EveryNpc_HasIdentity_TailorCanBeBorrowedWithoutTalking()
        {
            yield return Ready(1);
            int count = 0;
            foreach (var actor in State.World.Actors)
            {
                if (actor.StableId.StartsWith("shop_station_")) continue;
                Assert.That(actor.Role, Is.Not.Null.And.Not.Empty, actor.StableId);
                Assert.That(actor.GetComponent<Game.Taming.TamingActor>(), Is.Not.Null, actor.StableId);
                count++;
            }
            Assert.That(count, Is.EqualTo(15));
            yield return Borrow(3);
            Assert.That(State.CurrentRole, Is.EqualTo("tailor"));
            Assert.That(DialogueRunning, Is.False);
        }
        private IEnumerator TalkToTailor(string expected, bool claim)
        {
            Game.ShopPlaytest.ShopPlaytestActor tailor = null;
            foreach (var actor in State.World.Actors) if (actor.StableId == "shop_tailor") tailor = actor;
            yield return OpenConversation(tailor);
            yield return ShowChoices();
            var rules = ResolveService<Game.Dialogue.DialogueRules>();
            Assert.That(rules.Current.Text, Does.Contain(expected));
            Assert.That(System.Array.Exists(rules.Current.Choices, choice => choice.Id.StartsWith("command")), Is.EqualTo(claim));
            yield return Snapshot("裁衣匠回应_" + State.CurrentRole);
            yield return Choose("leave");
        }
    }
}
