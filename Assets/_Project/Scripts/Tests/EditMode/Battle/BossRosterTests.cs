// 职责：钉住 BOSS 名册——按 payload 查得到 / 查不到、名册写坏构造即抛（空行、缺 id、重复、数值非法，含本场玩家生命）。
// 为什么新建：BossRoster 是本波新增；一个被测类一个测试类。不读正式资产（unity-tests：测试不依赖具体资产路径）。
using System;
using Game.Battle;
using NUnit.Framework;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BossRosterTests
    {
        [Test]
        public void TryGet_KnownId_ReturnsDefinition()
        {
            var roster = new BossRoster(new[] { new BossDefinition("boss_a", "甲", 6, 50) });

            Assert.That(roster.TryGet("boss_a", out BossDefinition boss), Is.True);
            Assert.That(boss.MaxHealth, Is.EqualTo(6));
            Assert.That(boss.OutOfBattleDrunk, Is.EqualTo(50));
            Assert.That(boss.PlayerHealth, Is.EqualTo(BossDefinition.PlaceholderPlayerHealth), "本场玩家生命默认占位 10");
        }

        [Test]
        public void TryGet_UnknownOrEmptyId_ReturnsFalse()
        {
            var roster = new BossRoster(new[] { new BossDefinition("boss_a", "甲", 6, 50) });

            Assert.That(roster.TryGet("boss_b", out BossDefinition boss), Is.False);
            Assert.That(boss, Is.Null);
            Assert.That(roster.TryGet(string.Empty, out _), Is.False);
            Assert.That(roster.TryGet(null, out _), Is.False);
            Assert.That(BossRoster.Empty.TryGet("boss_a", out _), Is.False);
        }

        [Test]
        public void Construct_BrokenRoster_Throws()
        {
            Assert.Throws<ArgumentException>(() => new BossRoster(new BossDefinition[] { null }));
            Assert.Throws<ArgumentException>(() => new BossRoster(new[] { new BossDefinition("", "无名", 6, 0) }));
            Assert.Throws<ArgumentException>(() => new BossRoster(new[] { new BossDefinition("a", "甲", 0, 0) }));
            Assert.Throws<ArgumentException>(() => new BossRoster(new[] { new BossDefinition("a", "甲", 6, -1) }));
            Assert.Throws<ArgumentException>(() => new BossRoster(new[] { new BossDefinition("a", "甲", 6, 0, playerHealth: 0) }));
            Assert.Throws<ArgumentException>(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0), new BossDefinition("a", "乙", 6, 0),
            }));
        }
    }
}
