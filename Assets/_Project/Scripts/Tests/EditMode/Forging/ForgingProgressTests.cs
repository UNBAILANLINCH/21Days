// 职责：未完工预留和完成成果可保存恢复，损坏快照先拒绝再写状态。
using Game.Forging;
using NUnit.Framework;
namespace Game.Tests.EditMode.Forging
{
    public sealed class ForgingProgressTests
    {
        private static ForgingRules Create() => new ForgingRules(2, 8, 4, 2, 1, new[] { 0, 1, 2, 3 });
        [Test]
        public void Restore_UnfinishedProductKeepsReservationAndNextPoint_CancelDoesNotDuplicateMaterials()
        {
            var source = Create(); source.Apply(new ForgingIntent(ForgingAction.SetQuota, 8, true, false), out _);
            source.Apply(new ForgingIntent(ForgingAction.StartKey, 0, false, true), out _);
            source.Apply(new ForgingIntent(ForgingAction.Strike, 0, false, true), out _);
            var restored = Create(); restored.Restore(source.Capture());
            Assert.That(restored.Available, Is.EqualTo(4)); Assert.That(restored.NextPoint, Is.EqualTo(1));
            restored.Apply(new ForgingIntent(ForgingAction.Cancel, 0, false, true), out _);
            Assert.That(restored.Available, Is.EqualTo(8)); Assert.That(restored.Keys, Is.Zero);
        }
        [Test]
        public void Restore_InvalidProductAccountingDoesNotMutateCurrentState()
        {
            var rules = Create(); var damaged = rules.Capture(); damaged.Keys = 1;
            Assert.Throws<System.ArgumentException>(() => rules.Restore(damaged));
            Assert.That(rules.Quota, Is.EqualTo(2)); Assert.That(rules.Keys, Is.Zero);
        }
    }
}
