// 职责：钉住 PerformanceSaveData 的默认值、去重记录与查询。
// 为什么新建：Performance 模块首次落地（PRP/performance-pipeline 波 1），一个被测类一个测试类。
using Game.Performance;
using NUnit.Framework;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformanceSaveDataTests
    {
        private PerformanceSaveData data;

        [SetUp]
        public void SetUp() => data = new PerformanceSaveData();

        [Test]
        public void Default_IsEmptyVersionOne()
        {
            Assert.That(data.Version, Is.EqualTo(1));
            Assert.That(data.PlayedIds, Is.Not.Null.And.Empty);
        }

        [Test]
        public void MarkPlayed_FirstTime_AddsAndReportsTrue()
        {
            Assert.That(data.MarkPlayed("perf_a"), Is.True);
            Assert.That(data.HasPlayed("perf_a"), Is.True);
        }

        [Test]
        public void MarkPlayed_SameIdTwice_DoesNotDuplicate()
        {
            data.MarkPlayed("perf_a");

            Assert.That(data.MarkPlayed("perf_a"), Is.False);
            Assert.That(data.PlayedIds, Is.EqualTo(new[] { "perf_a" }));
        }

        [Test]
        public void MarkPlayed_KeepsOrder()
        {
            data.MarkPlayed("perf_b");
            data.MarkPlayed("perf_a");

            Assert.That(data.PlayedIds, Is.EqualTo(new[] { "perf_b", "perf_a" }));
        }

        [TestCase(null)]
        [TestCase("")]
        public void MarkPlayed_EmptyId_ReturnsFalse(string id)
        {
            Assert.That(data.MarkPlayed(id), Is.False);
            Assert.That(data.PlayedIds, Is.Empty);
            Assert.That(data.HasPlayed(id), Is.False);
        }

        [Test]
        public void HasPlayed_IsCaseSensitive()
        {
            data.MarkPlayed("perf_a");

            Assert.That(data.HasPlayed("PERF_A"), Is.False);
        }

        [Test]
        public void MarkPlayed_NullListFromOldSave_Recovers()
        {
            data.PlayedIds = null;

            Assert.That(data.HasPlayed("perf_a"), Is.False);
            Assert.That(data.MarkPlayed("perf_a"), Is.True);
            Assert.That(data.HasPlayed("perf_a"), Is.True);
        }
    }
}
