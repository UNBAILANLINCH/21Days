// 职责：场外饮酒身份闸、上限、实际效果与离区重置。
using System;
using Game.Tavern;
using NUnit.Framework;
namespace Game.Tests.EditMode.Tavern
{
    public sealed class TavernRulesTests
    {
        [Test]
        public void Drink_RequiresGuest_ClampsAndResetsAllEffectsOnLeave()
        {
            var rules = NewRules(); Assert.That(rules.CanSneak, Is.True); Assert.That(rules.SpeedMultiplier, Is.EqualTo(1));
            Assert.That(rules.Apply(new TavernIntent(TavernAction.DrinkPlayer, false), out _), Is.False);
            rules.Apply(new TavernIntent(TavernAction.DrinkPlayer, true), out _);
            Assert.That(rules.CanSneak, Is.False); Assert.That(rules.SpeedMultiplier, Is.EqualTo(.5f));
            for (int i = 0; i < 20; i++) { rules.Apply(new TavernIntent(TavernAction.DrinkBoss, true), out _); rules.Apply(new TavernIntent(TavernAction.DrinkCustomer, true), out _); }
            Assert.That(rules.BossDrunk, Is.EqualTo(100)); Assert.That(rules.CustomerDrunk, Is.EqualTo(20));
            Assert.That(rules.BossAwarenessMultiplier, Is.EqualTo(.5f)); Assert.That(rules.CustomerAwarenessMultiplier, Is.EqualTo(.5f));
            rules.Apply(new TavernIntent(TavernAction.Leave, false), out _);
            Assert.That(rules.PlayerDrunk + rules.BossDrunk + rules.CustomerDrunk, Is.Zero); Assert.That(rules.CanSneak, Is.True); Assert.That(rules.BossAwarenessMultiplier, Is.EqualTo(1));
        }
        [Test]
        public void NonFiniteEffect_IsRejected()
        { Assert.Throws<ArgumentException>(() => new TavernDrunkRules(10, 100, 100, 20, 10, float.NaN, .5f)); }
        private static TavernDrunkRules NewRules() => new TavernDrunkRules(10, 100, 100, 20, 10, .5f, .5f);
    }
}
