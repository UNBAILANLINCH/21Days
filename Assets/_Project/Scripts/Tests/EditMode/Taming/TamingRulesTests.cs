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

        private MonsterRules ConfigureTwo()
        {
            MonsterRules second = enemy.CreateForActor("patrol-b");
            second.Reset(new[] { Vector2.right * 20 });
            rules.Configure("player", "玩家");
            rules.RegisterTarget("patrol-a", "巡逻者 A", enemy);
            rules.RegisterTarget("patrol-b", "巡逻者 B", second);
            return second;
        }

        [Test]
        public void TryControl_UntamedOrUnknownTarget_IsRejected()
        {
            ConfigureTwo();
            Assert.That(rules.TryControl("patrol-a"), Is.False);
            Assert.That(rules.TryControl("missing"), Is.False);
            Assert.That(rules.TryControl(null), Is.False);
            Assert.That(rules.CurrentControlId, Is.EqualTo("player"));
        }

        [Test]
        public void TamedTargets_DirectedSwitches_OnlyOwnerMovesAndEventMatchesId()
        {
            MonsterRules second = ConfigureTwo();
            rules.TryTame("patrol-a");
            rules.TryTame("patrol-b");
            string announced = null;
            int changes = 0;
            rules.OnControlChanged += id => { announced = id; changes++; };
            for (int i = 0; i < 6; i++)
            {
                string id = i % 2 == 0 ? "patrol-a" : "patrol-b";
                Vector2 a = enemy.Model.Position;
                Vector2 b = second.Model.Position;
                Assert.That(rules.TryControl(id), Is.True);
                Assert.That(rules.TryControl(id), Is.True, "重复选择幂等");
                rules.Step(new TamingIntent(Vector2.right, false), 1f);
                Assert.That(announced, Is.EqualTo(rules.CurrentControlId));
                Assert.That(enemy.Model.Position.x, Is.EqualTo(a.x + (id == "patrol-a" ? enemyConfig.PatrolSpeed : 0f)));
                Assert.That(second.Model.Position.x, Is.EqualTo(b.x + (id == "patrol-b" ? enemyConfig.PatrolSpeed : 0f)));
                Assert.That(player.Model.Position, Is.EqualTo(Vector2.zero));
            }
            Assert.That(changes, Is.EqualTo(6));
            Assert.That(rules.GetDisplayName("patrol-b"), Is.EqualTo("巡逻者 B"));
        }

        [Test]
        public void UnavailableOwner_ReturnsToPlayerAndCannotBeSelected()
        {
            ConfigureTwo();
            rules.TryTame("patrol-b");
            rules.TryControl("patrol-b");
            rules.SetAvailable("patrol-b", false);
            Assert.That(rules.CurrentControlId, Is.EqualTo("player"));
            Assert.That(rules.TryControl("patrol-b"), Is.False);
        }

        [Test]
        public void DuplicateStableId_IsRejectedWithoutReplacingOriginal()
        {
            MonsterRules second = ConfigureTwo();
            Assert.Throws<System.ArgumentException>(() => rules.RegisterTarget("patrol-a", "重复", second));
            Assert.That(rules.GetTarget("patrol-a"), Is.SameAs(enemy));
        }

        [Test]
        public void CaptureRestore_PreservesBothTamesAndOwnerWhileResetClearsThem()
        {
            ConfigureTwo();
            rules.TryTame("patrol-a");
            rules.TryTame("patrol-b");
            rules.TryControl("patrol-b");
            TamingTargetSaveData[] saved = rules.Capture();
            rules.Reset();
            Assert.That(rules.IsTargetTamed("patrol-a"), Is.False);
            Assert.That(rules.CurrentControlId, Is.EqualTo("player"));
            rules.Restore(saved, "patrol-b", false);
            Assert.That(rules.IsTargetTamed("patrol-a"), Is.True);
            Assert.That(rules.IsTargetTamed("patrol-b"), Is.True);
            Assert.That(rules.CurrentControlId, Is.EqualTo("patrol-b"));
        }
    }
}
