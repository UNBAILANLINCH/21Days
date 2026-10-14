// 职责：工件/补料快照恢复与旧钥匙首饰记录留存，损坏数据拒绝时不部分修改。
using Game.Forging;
using Game.ShopPlaytest;
using Newtonsoft.Json;
using NUnit.Framework;
namespace Game.Tests.EditMode.Forging
{
    public sealed class ForgingProgressTests
    {
        private static ForgingRules Create() => new ForgingRules(20, 24, 5, new[] { 1, 2, 3 }, new[] { 0, 1, 2, 3 });
        [Test]
        public void Restore_UnfinishedC_KeepsReservationNextPointAndRefilledStock()
        {
            var source = Create();
            source.Apply(new ForgingIntent(ForgingAction.StartProduct, 2, false, true), out _);
            source.Apply(new ForgingIntent(ForgingAction.Strike, 0, false, true), out _);
            source.Apply(new ForgingIntent(ForgingAction.SetQuota, 24, true, false), out _);
            var restored = Create(); restored.Restore(source.Capture());
            Assert.That(restored.Available, Is.EqualTo(19)); Assert.That(restored.NextPoint, Is.EqualTo(1)); Assert.That(restored.CurrentProduct, Is.EqualTo(2));
            restored.Apply(new ForgingIntent(ForgingAction.Cancel, 0, false, true), out _);
            Assert.That(restored.Available, Is.EqualTo(24)); Assert.That(restored.Count(2), Is.Zero);
        }
        [Test]
        public void Restore_InvalidAccountingDoesNotMutateCurrentState()
        {
            var rules = Create(); var damaged = rules.Capture(); damaged.Products[0] = 1;
            Assert.Throws<System.ArgumentException>(() => rules.Restore(damaged));
            Assert.That(rules.Quota, Is.EqualTo(20)); Assert.That(rules.Count(0), Is.Zero);
            damaged = rules.Capture(); damaged.Schema = 0;
            Assert.Throws<System.ArgumentException>(() => rules.Restore(damaged));
            damaged = rules.Capture(); damaged.Product = 3;
            Assert.Throws<System.ArgumentException>(() => rules.Restore(damaged));
        }
        [Test]
        public void CaptureRestore_ProductsAreCopiedAndSubmitted30CanExceedStockCap()
        {
            var source = Create();
            for (int product = 0; product < 3; product++) for (int n = 0; n < source.Required(product); n++)
            {
                source.Apply(new ForgingIntent(ForgingAction.SetQuota, 24, true, false), out _);
                source.Apply(new ForgingIntent(ForgingAction.StartProduct, product, false, true), out _);
                for (int point = 0; point < 4; point++) source.Apply(new ForgingIntent(ForgingAction.Strike, point, false, true), out _);
            }
            var snapshot = source.Capture(); var restored = Create(); restored.Restore(snapshot);
            snapshot.Products[2] = 0;
            Assert.That(restored.Count(2), Is.EqualTo(3)); Assert.That(source.Count(2), Is.EqualTo(3));
            source.Apply(new ForgingIntent(ForgingAction.Submit, 0, false, true), out _);
            restored.Restore(JsonConvert.DeserializeObject<ForgingProgressData>(JsonConvert.SerializeObject(source.Capture())));
            Assert.That(restored.Submitted, Is.True); Assert.That(restored.Spent, Is.EqualTo(30)); Assert.That(restored.Quota, Is.EqualTo(19));
        }
        [Test]
        public void MigrateV1_PreservesOldForgingRecordAndOtherShopProgress()
        {
            var old = new ForgingProgressData { Quota = 8, Spent = 6, Keys = 1, Ornaments = 0, Submitted = true };
            var partition = new ShopProgressSaveData { Initialized = true, ClothesReceived = true, TheaterCompleted = true, Forging = old };
            partition.Migrate(1);
            Assert.That(partition.Version, Is.EqualTo(2)); Assert.That(partition.Forging, Is.Null);
            Assert.That(partition.LegacyForging, Is.SameAs(old)); Assert.That(partition.ClothesReceived && partition.TheaterCompleted, Is.True);
            var reloaded = JsonConvert.DeserializeObject<ShopProgressSaveData>(JsonConvert.SerializeObject(partition));
            Assert.That(reloaded.LegacyForging.Keys, Is.EqualTo(1)); Assert.That(reloaded.LegacyForging.Spent, Is.EqualTo(6));
            Assert.That(reloaded.Forging, Is.Null);
        }
    }
}
