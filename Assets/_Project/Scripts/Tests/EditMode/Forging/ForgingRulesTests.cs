// 职责：库存上限、职业、ABC目标、点位、取消和重复提交的负对照。
using System;
using Game.Forging;
using NUnit.Framework;
namespace Game.Tests.EditMode.Forging
{
    public sealed class ForgingRulesTests
    {
        [Test]
        public void Complete_RefillsStockTo24_Consumes30AndSubmitsAllProductsOnce()
        {
            var rules = NewRules();
            Assert.That(rules.Apply(new ForgingIntent(ForgingAction.StartProduct, 0, false, false), out _), Is.False);
            Assert.That(rules.Apply(new ForgingIntent(ForgingAction.SetQuota, 24, false, true), out _), Is.False);
            Assert.That(Apply(rules, ForgingAction.StartProduct, 0), Is.True);
            Assert.That(rules.Available, Is.EqualTo(15));
            Assert.That(Apply(rules, ForgingAction.Strike, 3), Is.False);
            Assert.That(rules.Progress, Is.Zero); Assert.That(rules.Spent, Is.Zero);
            Strike(rules);
            Assert.That(rules.Count(0), Is.EqualTo(1)); Assert.That(rules.Quota, Is.EqualTo(15));
            Assert.That(Apply(rules, ForgingAction.StartProduct, 0), Is.False);
            Assert.That(Apply(rules, ForgingAction.Submit), Is.False);
            Craft(rules, 1); Craft(rules, 1); Craft(rules, 2);
            Assert.That(rules.Available, Is.Zero);
            Assert.That(Apply(rules, ForgingAction.StartProduct, 2), Is.False);
            Assert.That(rules.Apply(new ForgingIntent(ForgingAction.SetQuota, 25, true, false), out _), Is.False);
            Assert.That(rules.Apply(new ForgingIntent(ForgingAction.SetQuota, 24, true, false), out _), Is.True);
            Assert.That(rules.Spent, Is.EqualTo(20));
            Craft(rules, 2); Craft(rules, 2);
            Assert.That(rules.Count(0), Is.EqualTo(1)); Assert.That(rules.Count(1), Is.EqualTo(2)); Assert.That(rules.Count(2), Is.EqualTo(3));
            Assert.That(rules.CanSubmit, Is.True); Assert.That(rules.Spent, Is.EqualTo(30)); Assert.That(rules.Quota, Is.EqualTo(14));
            Assert.That(Apply(rules, ForgingAction.Submit), Is.True);
            Assert.That(Apply(rules, ForgingAction.Submit), Is.False);
            Assert.That(Apply(rules, ForgingAction.StartProduct, 0), Is.False);
            Assert.That(rules.Count(0) + rules.Count(1) + rules.Count(2), Is.Zero);
            Assert.That(rules.Spent, Is.EqualTo(30));
        }
        [Test]
        public void Cancel_ReleasesReservationAfterRefill_ButDoesNotRefundCompletedProduct()
        {
            var rules = NewRules(); Apply(rules, ForgingAction.StartProduct, 2); Apply(rules, ForgingAction.Strike, 0);
            Assert.That(rules.Available, Is.EqualTo(15));
            rules.Apply(new ForgingIntent(ForgingAction.SetQuota, 24, true, false), out _);
            Assert.That(rules.Available, Is.EqualTo(19));
            Assert.That(rules.Apply(new ForgingIntent(ForgingAction.SetQuota, 23, true, false), out _), Is.False);
            Apply(rules, ForgingAction.Cancel);
            Assert.That(rules.Available, Is.EqualTo(24)); Assert.That(rules.Spent, Is.Zero); Assert.That(rules.Count(2), Is.Zero);
            Craft(rules, 0); Assert.That(Apply(rules, ForgingAction.Cancel), Is.False);
            Assert.That(rules.Quota, Is.EqualTo(19)); Assert.That(rules.Count(0), Is.EqualTo(1));
            rules.Reset(); Assert.That(rules.Quota, Is.EqualTo(20)); Assert.That(rules.Spent, Is.Zero); Assert.That(rules.Count(0), Is.Zero);
        }
        [TestCase(-1)]
        [TestCase(3)]
        [TestCase(int.MaxValue)]
        public void UnknownProduct_RejectsWithoutReservingMetal(int product)
        {
            var rules = NewRules();
            Assert.That(Apply(rules, ForgingAction.StartProduct, product), Is.False);
            Assert.That(rules.IsForging, Is.False); Assert.That(rules.Available, Is.EqualTo(20));
        }
        [Test]
        public void InvalidOrOverflowingConfig_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new ForgingRules(20, 24, 25, new[] { 1, 2, 3 }, new[] { 0 }));
            Assert.Throws<ArgumentException>(() => new ForgingRules(0, int.MaxValue, int.MaxValue, new[] { int.MaxValue, int.MaxValue, int.MaxValue }, new[] { 0 }));
            Assert.Throws<ArgumentException>(() => new ForgingRules(20, 24, 5, new[] { 0, 2, 3 }, new[] { 0 }));
            Assert.Throws<ArgumentException>(() => new ForgingRules(20, 24, 5, new[] { 1, 2 }, new[] { 0 }));
            Assert.Throws<ArgumentException>(() => new ForgingRules(20, 24, 5, new[] { 1, 2, 3 }, new[] { 4 }));
        }
        private static ForgingRules NewRules() => new ForgingRules(20, 24, 5, new[] { 1, 2, 3 }, new[] { 0, 1, 2, 3 });
        private static bool Apply(ForgingRules rules, ForgingAction action, int value = 0) => rules.Apply(new ForgingIntent(action, value, false, true), out _);
        private static void Craft(ForgingRules rules, int product) { Assert.That(Apply(rules, ForgingAction.StartProduct, product), Is.True); Strike(rules); }
        private static void Strike(ForgingRules rules) { for (int i = 0; i < 4; i++) Assert.That(Apply(rules, ForgingAction.Strike, i), Is.True); }
    }
}
