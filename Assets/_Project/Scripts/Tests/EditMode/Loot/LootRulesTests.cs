// 职责：钉住 LootRules 的开箱规则（幂等、累加、非法参数、重置清空）与 LootSaveData 默认值。
// 为什么新建：Loot 模块首次落地（PRP/exploration-whitebox 3.1），一个被测类一个测试类；LootSaveData 只有默认值一条，并在这里。
using Game.Loot;
using NUnit.Framework;

namespace Game.Tests.EditMode.Loot
{
    public sealed class LootRulesTests
    {
        private LootSaveData data;

        [SetUp]
        public void SetUp() => data = new LootSaveData();

        [Test]
        public void SaveData_Default_IsEmptyVersionOne()
        {
            Assert.That(data.Version, Is.EqualTo(1));
            Assert.That(data.CollectedCrates, Is.Not.Null.And.Empty);
            Assert.That(data.Items, Is.Not.Null.And.Empty);
        }

        [Test]
        public void Collect_FirstTime_RecordsKeyAndAddsItems()
        {
            Assert.That(LootRules.Collect(data, "crate_a", 1001, 2), Is.True);
            Assert.That(LootRules.IsCollected(data, "crate_a"), Is.True);
            Assert.That(data.Items[1001], Is.EqualTo(2));
        }

        [Test]
        public void Collect_SameKeyTwice_SecondReturnsFalseWithoutChangingData()
        {
            LootRules.Collect(data, "crate_a", 1001, 2);

            Assert.That(LootRules.Collect(data, "crate_a", 1001, 5), Is.False);
            Assert.That(data.CollectedCrates.Count, Is.EqualTo(1));
            Assert.That(data.Items[1001], Is.EqualTo(2));
        }

        [Test]
        public void Collect_DifferentKeysSameItem_Accumulates()
        {
            LootRules.Collect(data, "crate_a", 1001, 2);
            LootRules.Collect(data, "crate_b", 1001, 3);
            LootRules.Collect(data, "crate_c", 1002, 1);

            Assert.That(data.Items[1001], Is.EqualTo(5));
            Assert.That(data.Items[1002], Is.EqualTo(1));
            Assert.That(data.CollectedCrates, Is.EqualTo(new[] { "crate_a", "crate_b", "crate_c" }));
        }

        [TestCase(null, 1)]
        [TestCase("", 1)]
        [TestCase("crate_a", 0)]
        [TestCase("crate_a", -3)]
        public void Collect_InvalidArguments_ReturnsFalseWithoutThrowing(string key, int count)
        {
            Assert.That(LootRules.Collect(data, key, 1001, count), Is.False);
            Assert.That(data.CollectedCrates, Is.Empty);
            Assert.That(data.Items, Is.Empty);
        }

        [Test]
        public void Collect_NullData_ReturnsFalse()
        {
            Assert.That(LootRules.Collect(null, "crate_a", 1001, 1), Is.False);
            Assert.That(LootRules.IsCollected(null, "crate_a"), Is.False);
        }

        [Test]
        public void Reset_AfterCollecting_ClearsKeysAndItems()
        {
            LootRules.Collect(data, "crate_a", 1001, 2);
            LootRules.Collect(data, "crate_b", 1002, 1);

            LootRules.Reset(data);

            Assert.That(data.CollectedCrates, Is.Empty);
            Assert.That(data.Items, Is.Empty);
            Assert.That(LootRules.IsCollected(data, "crate_a"), Is.False);
            Assert.That(LootRules.Collect(data, "crate_a", 1001, 2), Is.True, "重置后同一箱子可以再开");
        }
    }
}
