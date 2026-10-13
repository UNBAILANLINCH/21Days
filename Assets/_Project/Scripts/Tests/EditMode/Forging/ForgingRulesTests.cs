// 职责：额度、角色、点位、取消退款和提交边界的负对照。
using System;
using Game.Forging;
using NUnit.Framework;
namespace Game.Tests.EditMode.Forging
{
    public sealed class ForgingRulesTests
    {
        [Test]
        public void Complete_RequiresOfficerQuotaAndSmith_ConsumesExactlyOnce()
        {
            var rules = NewRules();
            Assert.That(Apply(rules, ForgingAction.StartKey), Is.False);
            Assert.That(rules.Apply(new ForgingIntent(ForgingAction.SetQuota, 8, false, true), out _), Is.False);
            Assert.That(rules.Apply(new ForgingIntent(ForgingAction.SetQuota, 8, true, false), out _), Is.True);
            Assert.That(Apply(rules, ForgingAction.StartKey), Is.True);
            Assert.That(Apply(rules, ForgingAction.Strike, 3), Is.False);
            Assert.That(rules.Progress, Is.Zero);
            Strike(rules); Assert.That(rules.Keys, Is.EqualTo(1)); Assert.That(rules.Spent, Is.EqualTo(4));
            Assert.That(Apply(rules, ForgingAction.Strike, 3), Is.False);
            Assert.That(Apply(rules, ForgingAction.Submit), Is.False);
            Apply(rules, ForgingAction.StartOrnament); Strike(rules);
            Assert.That(Apply(rules, ForgingAction.Submit), Is.True);
            Assert.That(Apply(rules, ForgingAction.Submit), Is.False);
            Assert.That(rules.Keys, Is.EqualTo(1)); Assert.That(rules.Ornaments, Is.Zero); Assert.That(rules.Spent, Is.EqualTo(6));
        }
        [Test]
        public void Cancel_RefundsReservation_NotCompletedProducts_ResetRestoresInitialQuota()
        {
            var rules = NewRules(); rules.Apply(new ForgingIntent(ForgingAction.SetQuota, 8, true, false), out _);
            Apply(rules, ForgingAction.StartKey); Apply(rules, ForgingAction.Strike, 0);
            Assert.That(rules.Available, Is.EqualTo(4)); Apply(rules, ForgingAction.Cancel);
            Assert.That(rules.Available, Is.EqualTo(8)); Assert.That(rules.Keys, Is.Zero);
            Apply(rules, ForgingAction.StartKey); Strike(rules); Apply(rules, ForgingAction.Cancel);
            Assert.That(rules.Available, Is.EqualTo(4)); rules.Reset(); Assert.That(rules.Quota, Is.EqualTo(2)); Assert.That(rules.Keys, Is.Zero);
        }
        [Test]
        public void ImpossibleOrOverflowingRequiredMaterial_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new ForgingRules(2, 5, 4, 2, 1, new[] { 0 }));
            Assert.Throws<ArgumentException>(() => new ForgingRules(0, int.MaxValue, int.MaxValue, 2, int.MaxValue, new[] { 0 }));
        }
        private static ForgingRules NewRules() => new ForgingRules(2, 8, 4, 2, 1, new[] { 0, 1, 2, 3 });
        private static bool Apply(ForgingRules rules, ForgingAction action, int value = 0) => rules.Apply(new ForgingIntent(action, value, false, true), out _);
        private static void Strike(ForgingRules rules) { for (int i = 0; i < 4; i++) Assert.That(Apply(rules, ForgingAction.Strike, i), Is.True); }
    }
}
