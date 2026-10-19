// 职责：验证人物选项不会串店、串顾客，并覆盖当前身份与工件阶段。
// 新建原因：各店规则测试不负责组合接线，已有对话测试也不认识店铺人物。
using Game.ShopPlaytest;
using NUnit.Framework;
namespace Game.Tests.EditMode.ShopPlaytest
{
    public sealed class ShopPlaytestStateTests
    {
        [TestCase("shop_customer_a", 2)]
        [TestCase("shop_customer_b", 4)]
        public void Customer_OnlyOwnCommand(string id, int command)
            => Assert.That(ShopPlaytestState.CommandsFor(id, "guest", false, 0), Is.EqualTo(new[] { command }));
        [TestCase("shop_customer0")]
        [TestCase("shop_customer1")]
        [TestCase("shop_customer2")]
        public void ClothingCustomer_NoMechanicalClueButtons(string id)
            => Assert.That(ShopPlaytestState.CommandsFor(id, "", false, 0), Is.Empty);
        [Test]
        public void Tailor_UsesBorrowedIdentityAndDoesNotOfferOtherCustomers()
        {
            Assert.That(ShopPlaytestState.CommandsFor("shop_tailor", "", false, 0), Is.Empty);
            Assert.That(ShopPlaytestState.CommandsFor("shop_tailor", "customer1", false, 0), Is.EqualTo(new[] { 4 }));
        }
        [Test]
        public void Forge_DialogueOnlyOffersOfficerQuotaAndNoWorkbenchOperations()
        {
            Assert.That(ShopPlaytestState.CommandsFor("shop_station_2", "officer", false, 0), Is.EqualTo(new[] { 0 }));
            Assert.That(ShopPlaytestState.CommandsFor("shop_station_2", "smith", true, 2), Is.Empty);
            Assert.That(ShopPlaytestState.CommandsFor("shop_station_2", "smith", false, 0), Is.Empty);
        }
        [Test]
        public void Boss_OnlyDrinkAndChallenge()
            => Assert.That(ShopPlaytestState.CommandsFor("shop_boss", "guest", false, 0), Is.EqualTo(new[] { 1, 3 }));
        [Test]
        public void UnknownActor_DoesNotAcquireStoreCommands()
            => Assert.That(ShopPlaytestState.CommandsFor("unknown", "smith", true, 1), Is.Empty);
        [Test]
        public void ClothingStation_DoesNotDuplicateTailorCommands()
            => Assert.That(ShopPlaytestState.CommandsFor("shop_station_1", "customer1", false, 0), Is.Empty);
        [Test]
        public void Greetings_RecognizeCurrentIdentity()
        {
            Assert.That(ShopPlaytestState.GreetingKey("shop_patrol", "apprentice"), Is.EqualTo("theater_verify"));
            Assert.That(ShopPlaytestState.GreetingKey("shop_patrol", "director"), Is.EqualTo("theater_command"));
            Assert.That(ShopPlaytestState.GreetingKey("shop_boss", "guest"), Is.EqualTo("boss_guest"));
            Assert.That(ShopPlaytestState.GreetingKey("shop_boss", ""), Is.EqualTo("boss_visitor"));
        }
        [Test]
        public void PointerClick_OnlyLeftClickOnVisibleBoundActorInteracts()
        {
            var root = new UnityEngine.GameObject("Shop actor");
            try
            {
                var actor = root.AddComponent<ShopPlaytestActor>();
#if UNITY_EDITOR
                var collider = root.AddComponent<UnityEngine.BoxCollider>();
                var serialized = new UnityEditor.SerializedObject(actor);
                serialized.FindProperty("clickCollider").objectReferenceValue = collider;
                serialized.ApplyModifiedPropertiesWithoutUndo();
#endif
                int calls = 0;
                actor.Bind(_ => calls++, _ => true);
                actor.SetVisible(true);
#if UNITY_EDITOR
                Assert.That(collider.enabled, Is.True);
#endif
                var click = new UnityEngine.EventSystems.PointerEventData(null);
                click.button = UnityEngine.EventSystems.PointerEventData.InputButton.Right;
                actor.OnPointerClick(click);
                Assert.That(calls, Is.Zero);
                click.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
                actor.OnPointerClick(click);
                Assert.That(calls, Is.EqualTo(1));
                actor.SetVisible(false);
#if UNITY_EDITOR
                Assert.That(collider.enabled, Is.False);
#endif
                actor.OnPointerClick(click);
                Assert.That(calls, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
