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

        [TestCase(false)]
        [TestCase(true)]
        public void ObservationOnly_KeepsDetectionWithoutExplorationDamage(bool observesOnly)
        {
            enemy.Reset(new[] { Vector2.right * .5f, Vector2.zero });
            rules.ConfigureActor("enemy", false, false, observesOnly);
            bool attacked = rules.AdvanceTargets(Vector2.zero, 0f);
            Assert.That(enemy.Model.Mode, Is.EqualTo(MonsterMode.Hostile), "观测者仍有真实感知与追近状态");
            Assert.That(attacked, Is.EqualTo(!observesOnly));
            Assert.That(player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - (observesOnly ? 0 : enemyConfig.AttackDamage)));
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

        [TestCase(false)]
        [TestCase(true)]
        public void AdvanceTargets_PropagatesIdentityProtectionToBothEnemies(bool identityInEffect)
        {
            MonsterRules second = ConfigureTwo();
            enemy.Reset(new[] { Vector2.right * 0.5f, Vector2.zero });
            second.Reset(new[] { Vector2.left * 0.5f, Vector2.zero });

            bool primaryAttacked = rules.AdvanceTargets(Vector2.zero, 0f, identityInEffect);

            Assert.That(primaryAttacked, Is.EqualTo(!identityInEffect));
            Assert.That(player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - (identityInEffect ? 0 : 2 * enemyConfig.AttackDamage)));
        }

        /// <summary>
        /// 「接管这一下」回调（PRP/combat-iteration D5）：回调返回 true 的那只怪不扣玩家血、也不算「主怪出手了」；
        /// 回调收到的是出手那只怪的稳定 id 与规则对象；另一只（回调返回 false）照旧扣血。
        /// </summary>
        [Test]
        public void AdvanceTargets_TakeOverHitReturnsTrue_SkipsDamageForThatHitOnly()
        {
            MonsterRules second = ConfigureTwo();
            enemy.Reset(new[] { Vector2.right * 0.5f, Vector2.zero });
            second.Reset(new[] { Vector2.left * 0.5f, Vector2.zero });
            var seen = new System.Collections.Generic.List<(string Id, MonsterRules Rules)>();

            bool primaryAttacked = rules.AdvanceTargets(Vector2.zero, 0f, false, (id, target) =>
            {
                seen.Add((id, target));
                return id == "patrol-a";
            });

            Assert.That(seen.Count, Is.EqualTo(2), "两只都出手了，回调各被问一次");
            Assert.That(seen[0].Id, Is.EqualTo("patrol-a"));
            Assert.That(seen[0].Rules, Is.SameAs(enemy));
            Assert.That(seen[1].Rules, Is.SameAs(second));
            Assert.That(primaryAttacked, Is.False, "主怪那一下被接管：不算它打中了人");
            Assert.That(player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - enemyConfig.AttackDamage), "只扣了没被接管的那一下");
        }

        /// <summary>负对照：回调一律返回 false 时与不传回调逐字相同（两下都扣、主怪算出手）。</summary>
        [Test]
        public void AdvanceTargets_TakeOverHitReturnsFalse_BehavesAsWithoutCallback()
        {
            MonsterRules second = ConfigureTwo();
            enemy.Reset(new[] { Vector2.right * 0.5f, Vector2.zero });
            second.Reset(new[] { Vector2.left * 0.5f, Vector2.zero });

            bool primaryAttacked = rules.AdvanceTargets(Vector2.zero, 0f, false, (id, target) => false);

            Assert.That(primaryAttacked, Is.True);
            Assert.That(player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - 2 * enemyConfig.AttackDamage));
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

        // PRP/combat-iteration D2：按稳定 id 校验，不再要求与场景数量相等（场景加 / 删了怪，旧档仍要读得进来）。
        [Test]
        public void ValidateRestore_FewerOrUnknownIds_IsAcceptedAndRestoreAlignsById()
        {
            MonsterRules second = ConfigureTwo();
            rules.TryTame("patrol-b");
            TamingTargetSaveData[] saved = rules.Capture();
            TamingTargetSaveData[] fewer = { saved[1] };
            var unknown = new TamingTargetSaveData { Id = "patrol-gone", IsTamed = true, Monster = saved[0].Monster };
            TamingTargetSaveData[] more = { saved[0], saved[1], unknown };

            Assert.DoesNotThrow(() => rules.ValidateRestore(fewer), "存档比场景少是合法的");
            Assert.DoesNotThrow(() => rules.ValidateRestore(more), "存档里有场景没有的 id 也是合法的（恢复时丢弃）");

            rules.Reset();
            rules.Restore(more, "player", false);
            Assert.That(rules.IsTargetTamed("patrol-b"), Is.True, "对得上的 id 照常恢复");
            Assert.That(rules.IsTargetTamed("patrol-gone"), Is.False, "场景里没有的那条被丢弃");
            Assert.That(rules.GetTarget("patrol-gone"), Is.Null);
            Assert.That(second.Model.Health, Is.EqualTo(enemyConfig.MaxHealth));
        }

        // 负对照：存档本身坏了（条目为空、标识为空 / 重复、快照缺失）仍然拒绝——放宽的只是「与场景数量相等」那一条。
        [Test]
        public void ValidateRestore_BrokenEntries_StillThrow()
        {
            ConfigureTwo();
            TamingTargetSaveData[] saved = rules.Capture();

            Assert.Throws<System.ArgumentException>(() => rules.ValidateRestore(new[] { saved[0], null }));
            Assert.Throws<System.ArgumentException>(() => rules.ValidateRestore(new[] { saved[0], saved[0] }));
            Assert.Throws<System.ArgumentException>(() => rules.ValidateRestore(
                new[] { new TamingTargetSaveData { Id = " ", Monster = saved[0].Monster } }));
            Assert.Throws<System.ArgumentException>(() => rules.ValidateRestore(
                new[] { new TamingTargetSaveData { Id = "patrol-a", Monster = null } }));
        }

        [Test]
        public void PassiveActor_NearPlayer_PatrolsWithoutAlertOrDamage()
        {
            enemy.Reset(new[] { Vector2.zero, Vector2.right * 3 });
            rules.ConfigureActor("enemy", true, false);
            for (int i = 0; i < 10; i++) rules.AdvanceTargets(Vector2.zero, .1f);
            Assert.That(player.Model.Health, Is.EqualTo(playerConfig.MaxHealth));
            Assert.That(enemy.Model.Alert, Is.Zero);
            Assert.That(enemy.Model.Position.x, Is.GreaterThan(0));
        }

        [Test]
        public void AddedShopActor_OldSaveKeepsOriginalOwnerAndNewActorInitialState()
        {
            rules.TryTame("enemy"); rules.TryControl("enemy");
            var saved = rules.Capture();
            MonsterRules added = enemy.CreateForActor("shop_smith"); added.Reset(new[] { Vector2.right * 30 });
            rules.RegisterTarget("shop_smith", "金银匠", added); rules.ConfigureActor("shop_smith", true, true);
            rules.Restore(saved, "enemy", false);
            Assert.That(rules.CurrentControlId, Is.EqualTo("enemy"));
            Assert.That(added.Model.Position, Is.EqualTo(Vector2.right * 30));
            Assert.That(rules.IsTargetTamed("shop_smith"), Is.False);
        }

        [Test]
        public void SelectionPermission_HiddenActorCannotBeBorrowed_ExistingOwnerRemainsControlled()
        {
            ConfigureTwo(); rules.TryTame("patrol-a"); rules.TryControl("patrol-a");
            rules.BindSelection(_ => false);
            Assert.That(rules.TryTame("patrol-b"), Is.False);
            rules.ValidateControl();
            Assert.That(rules.CurrentControlId, Is.EqualTo("patrol-a"));
        }

        [Test]
        public void ChangePatrolRoute_PreservesHealthControlAndPosition()
        {
            rules.TryTame("enemy"); rules.TryControl("enemy");
            int hp = enemy.Model.Health; Vector2 position = enemy.Model.Position;
            enemy.ChangePatrolRoute(new[] { Vector2.one * 50 });
            Assert.That(enemy.Model.Health, Is.EqualTo(hp));
            Assert.That(enemy.Model.Position, Is.EqualTo(position));
            Assert.That(rules.CurrentControlId, Is.EqualTo("enemy"));
            Assert.That(enemy.Capture().WaypointX[0], Is.EqualTo(50));
        }
        }
    }
}
