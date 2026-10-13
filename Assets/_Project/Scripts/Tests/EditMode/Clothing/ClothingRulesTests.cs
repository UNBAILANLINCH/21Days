// 职责：防止错误身份、错误答案或重复点击领衣，并验证重试清理。
using Game.Clothing;
using NUnit.Framework;
namespace Game.Tests.EditMode.Clothing
{
    public sealed class ClothingRulesTests
    {
        [Test]
        public void Claim_RequiresIdentityClueAndCorrectCustomer_OnlyOnce()
        {
            var rules = new ClothingRules(3, 1);
            Assert.That(rules.Apply(new ClothingIntent(1, true, true), out _), Is.False);
            Assert.That(rules.Apply(new ClothingIntent(1, false, false), out _), Is.False);
            Assert.That(rules.Apply(new ClothingIntent(0, false, true), out _), Is.True);
            Assert.That(rules.Apply(new ClothingIntent(0, true, true), out _), Is.False);
            Assert.That(rules.Apply(new ClothingIntent(1, false, true), out _), Is.True);
            Assert.That(rules.Apply(new ClothingIntent(1, true, true), out _), Is.True);
            Assert.That(rules.Apply(new ClothingIntent(1, true, true), out _), Is.False);
            rules.Reset(); Assert.That(rules.Received, Is.False); Assert.That(rules.HasInvestigated(1), Is.False);
        }
        [Test]
        public void InvalidCustomer_DoesNotMutateProgress()
        { var rules = new ClothingRules(3, 1); Assert.That(rules.Apply(new ClothingIntent(99, false, true), out _), Is.False); Assert.That(rules.Received, Is.False); }
    }
}
