// 职责：普通客人按稳定ID累计，离区统一清理，拒绝缺ID。
using Game.Tavern;
using NUnit.Framework;
namespace Game.Tests.EditMode.Tavern
{
    public sealed class TavernPerActorTests
    {
        [Test]
        public void DrinkDifferentCustomers_ClampsIndependentlyAndClearsOnLeave()
        {
            var rules = new TavernDrunkRules(10, 100, 100, 20, 10, .5f, .5f);
            rules.Apply(new TavernIntent(TavernAction.DrinkCustomer, true, "a"), out _);
            for (int i = 0; i < 3; i++) rules.Apply(new TavernIntent(TavernAction.DrinkCustomer, true, "b"), out _);
            Assert.That(rules.CustomerDrunkFor("a"), Is.EqualTo(10));
            Assert.That(rules.CustomerDrunkFor("b"), Is.EqualTo(20));
            Assert.That(rules.CustomerAwareness("a"), Is.GreaterThan(rules.CustomerAwareness("b")));
            Assert.That(rules.Apply(new TavernIntent(TavernAction.DrinkCustomer, true, ""), out _), Is.False);
            rules.Apply(new TavernIntent(TavernAction.Leave, false), out _);
            Assert.That(rules.CustomerDrunkFor("a") + rules.CustomerDrunkFor("b"), Is.Zero);
        }
    }
}
