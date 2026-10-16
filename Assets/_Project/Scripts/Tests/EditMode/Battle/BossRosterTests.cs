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
            Assert.That(boss.PlayerHealth, Is.EqualTo(BossDefinition.DefaultPlayerHealth), "本场玩家生命默认 120（sp07:129-133）");
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

        // ─────────── 按怪物种类查（PRP/combat-iteration D3：怪物的回合制战斗数据复用 BOSS 定义的一行） ───────────

        [Test]
        public void TryGetByKind_ConfiguredKind_ReturnsThatRow()
        {
            var roster = new BossRoster(new[]
            {
                new BossDefinition("sample_boss", "BOSS", 12, 50),
                new BossDefinition("shiling", "市令", 8, 0, playerHealth: 10, monsterKindId: 1003),
            });

            Assert.That(roster.TryGetByKind(1003, out BossDefinition row), Is.True);
            Assert.That(row.Id, Is.EqualTo("shiling"));
            Assert.That(row.MonsterKindId, Is.EqualTo(1003));
            Assert.That(roster.TryGet("shiling", out _), Is.True, "怪物行照样能按 id 查");
        }

        [Test]
        public void TryGetByKind_UnknownOrNonPositiveKind_ReturnsFalse()
        {
            var roster = new BossRoster(new[] { new BossDefinition("shiling", "市令", 8, 0, monsterKindId: 1003) });

            Assert.That(roster.TryGetByKind(1001, out BossDefinition boss), Is.False, "巡夜人没配");
            Assert.That(boss, Is.Null);
            Assert.That(roster.TryGetByKind(-1, out _), Is.False);
            Assert.That(BossRoster.Empty.TryGetByKind(1003, out _), Is.False);
        }

        /// <summary>默认 0 = 不是怪物行：不参与按种类查询，多行都是 0 也不算重复（旧名册逐字可用）。</summary>
        [Test]
        public void TryGetByKind_DefaultZeroRows_DoNotParticipate()
        {
            var roster = new BossRoster(new[]
            {
                new BossDefinition("boss_a", "甲", 6, 0), new BossDefinition("boss_b", "乙", 6, 0),
            });

            Assert.That(new BossDefinition("boss_a", "甲", 6, 0).MonsterKindId, Is.Zero, "代码默认 0 = 旧行为");
            Assert.That(roster.Count, Is.EqualTo(2), "两行 0 不撞");
            Assert.That(roster.TryGetByKind(0, out BossDefinition boss), Is.False, "0 永远查不到（没接种类表的怪不升级）");
            Assert.That(boss, Is.Null);
        }

        [Test]
        public void Construct_DuplicateOrNegativeKind_Throws()
        {
            Assert.Throws<ArgumentException>(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0, monsterKindId: 1003), new BossDefinition("b", "乙", 6, 0, monsterKindId: 1003),
            }), "同一种怪两行数据：开哪一行成了注册顺序的巧合，构造即抛");
            Assert.Throws<ArgumentException>(() => new BossRoster(new[] { new BossDefinition("a", "甲", 6, 0, monsterKindId: -5) }));
        }

        // ─────────── 主动发难（PRP/combat-iteration D6：开关与冲刺速度在名册这一行） ───────────

        [Test]
        public void Construct_DefaultRow_AggroOffWithPlaceholders()
        {
            // 默认值 = 加字段之前的行为（pitfalls.md:243）：不发难；速度 / 半径 / 反应时间 / 拴绳只是占位。
            var roster = new BossRoster(new[] { new BossDefinition("a", "甲", 6, 0) });

            Assert.That(roster.TryGet("a", out BossDefinition boss), Is.True);
            Assert.That(boss.AggroEnabled, Is.False);
            Assert.That(boss.AggroChargeSpeed, Is.EqualTo(BossDefinition.PlaceholderAggroChargeSpeed));
            Assert.That(boss.AggroRadius, Is.EqualTo(5f), "用户 2026-10-15 拍板占位 5 米");
            Assert.That(boss.AggroReactionSeconds, Is.EqualTo(3f), "用户 2026-10-15 拍板占位 3 秒");
            Assert.That(boss.AggroLeashDistanceSetting, Is.EqualTo(0f), "0 = 自动");
            Assert.That(boss.AggroLeashDistance, Is.EqualTo(10f), "自动 = 发难半径 × 2");
        }

        [Test]
        public void AggroLeashDistance_ExplicitPositive_IsUsedAsIs()
        {
            var boss = new BossDefinition("a", "甲", 6, 0, aggroEnabled: true, aggroRadius: 5f, aggroLeashDistance: 7f);

            Assert.That(boss.AggroLeashDistance, Is.EqualTo(7f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Construct_AggroEnabledWithInvalidRadius_Throws(float radius)
        {
            Assert.Throws<ArgumentException>(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0, aggroEnabled: true, aggroRadius: radius),
            }), "发难半径非正 / 非有限 = 永不警觉或处处警觉");
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Construct_AggroEnabledWithInvalidReactionSeconds_Throws(float seconds)
        {
            Assert.Throws<ArgumentException>(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0, aggroEnabled: true, aggroReactionSeconds: seconds),
            }));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Construct_AggroEnabledWithInvalidLeash_Throws(float leash)
        {
            Assert.Throws<ArgumentException>(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0, aggroEnabled: true, aggroLeashDistance: leash),
            }));
        }

        [Test]
        public void Construct_AggroEnabledWithZeroLeash_IsAccepted()
        {
            // 负对照：拴绳 0 是合法的「自动 = 半径 × 2」。
            Assert.DoesNotThrow(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0, aggroEnabled: true, aggroLeashDistance: 0f),
            }));
        }

        [Test]
        public void Construct_AggroEnabledWithNonPositiveSpeed_Throws()
        {
            Assert.Throws<ArgumentException>(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0, aggroEnabled: true, aggroChargeSpeed: 0f),
            }), "开了发难却冲不动：BOSS 原地「冲刺」、接触永远不来，构造即抛");
            Assert.Throws<ArgumentException>(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0, aggroEnabled: true, aggroChargeSpeed: float.NaN),
            }));
            Assert.Throws<ArgumentException>(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0, aggroEnabled: true, aggroChargeSpeed: float.PositiveInfinity),
            }));
        }

        [Test]
        public void Construct_AggroDisabledWithZeroAggroFields_IsAccepted()
        {
            // 负对照：没开发难时这几项都不校验（字段只是占位，资产里写 0 / 负数不该拦启动）。
            Assert.DoesNotThrow(() => new BossRoster(new[]
            {
                new BossDefinition("a", "甲", 6, 0, aggroEnabled: false, aggroChargeSpeed: 0f, aggroRadius: 0f,
                    aggroReactionSeconds: 0f, aggroLeashDistance: -1f),
            }));
        }
    }
}
