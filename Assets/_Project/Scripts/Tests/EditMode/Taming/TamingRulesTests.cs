// 职责：独立验证驯服切换、移动归属、友善状态和死亡边界。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.Taming;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Taming
{
    public sealed class TamingRulesTests
    {
        private PlayerConfig playerConfig;
        private MonsterConfig enemyConfig;
        private PlayerRules player;
        private MonsterRules enemy;
        private TamingRules rules;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            enemyConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            enemy = new MonsterRules(enemyConfig, new MonsterModel(), new RandomService(21ul), NullTelemetryScope.Instance);
            player.Reset(Vector2.zero);
            enemy.Reset(new[] { Vector2.right * 10 });
            rules = new TamingRules(player, enemy, NullTelemetryScope.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(enemyConfig);
        }

        [Test]
        public void Toggle_NoDistanceCondition_RoutesMovementAndReturnsWithoutLosingTame()
        {
            rules.Step(new TamingIntent(Vector2.right, true), 1f);
            Assert.That(rules.IsControllingEnemy, Is.True);
            Assert.That(player.Model.Position, Is.EqualTo(Vector2.zero));
            Assert.That(enemy.Model.Position.x, Is.EqualTo(10f + enemyConfig.PatrolSpeed));
            rules.Step(new TamingIntent(Vector2.zero, true), 0f);
            Assert.That(rules.IsControllingEnemy, Is.True, "按住不能来回切换");
            rules.Step(new TamingIntent(Vector2.zero, false), 0f);
            Vector2 stopped = enemy.Model.Position;
            rules.Step(new TamingIntent(Vector2.up, true), 1f);
            Assert.That(rules.IsControllingEnemy, Is.False);
            Assert.That(rules.IsTamed, Is.True);
            Assert.That(enemy.Model.Position, Is.EqualTo(stopped));
            Assert.That(player.Model.Position.y, Is.EqualTo(playerConfig.MoveSpeed));
        }

        [Test]
        public void Tamed_ReturnToPlayer_CloseEnemyNeverAttacks()
        {
            enemy.Reset(new[] { Vector2.zero });
            rules.Step(new TamingIntent(Vector2.zero, true), 0f);
            rules.Step(new TamingIntent(Vector2.zero, false), 0f);
            rules.Step(new TamingIntent(Vector2.zero, true), 0f);
            for (int i = 0; i < 10; i++) rules.Step(new TamingIntent(Vector2.zero, false), 1f);
            Assert.That(player.Model.Health, Is.EqualTo(playerConfig.MaxHealth));
        }

        [Test]
        public void EnemyDies_ControlReturnsAndCannotRetake()
        {
            rules.Step(new TamingIntent(Vector2.zero, true), 0f);
            var snapshot = player.Model.Snapshot;
            enemy.ApplyDamage(new DamageIntent(enemyConfig.MaxHealth), in snapshot);
            rules.Step(new TamingIntent(Vector2.right, false), 1f);
            Assert.That(rules.IsControllingEnemy, Is.False);
            rules.Step(new TamingIntent(Vector2.zero, true), 0f);
            Assert.That(rules.IsControllingEnemy, Is.False);
        }
    }
}
