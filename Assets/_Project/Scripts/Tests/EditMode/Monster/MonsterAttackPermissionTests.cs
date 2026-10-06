// 职责：钉住「敌人攻击许可」的两层关系——旧伪装布尔 + 身份生效中（S1 接线）。
// 为什么新建：`MonsterRulesTests` 覆盖的是感知 / 警戒 / 追击，没有一条测「这一 tick 到底打不打」；
//   而这一处正是本波唯一的**兼容层**（IdentityAttackRules 要求「身份不生效时旧语义逐字保留」）。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class MonsterAttackPermissionTests
    {
        private MonsterConfig config;
        private MonsterModel model;
        private MonsterRules rules;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<MonsterConfig>();
            model = new MonsterModel();
            rules = new MonsterRules(config, model, new RandomService(7ul), NullTelemetryScope.Instance);
            // 巡逻点决定初始位置与朝向：站在原点、朝 +X 看向 0.5 米外的玩家（红区内、攻击距离内）。
            rules.Reset(new[] { Vector2.zero, Vector2.right * 10f });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        /// <summary>玩家的这一 tick 快照；位置固定在怪物正前方 0.5 米（红区 2、攻击距离 0.8 之内）。</summary>
        private static PlayerSnapshot Target(bool disguised) =>
            new PlayerSnapshot(new Vector2(0.5f, 0f), Vector2.left, false, disguised, 10, false);

        /// <summary>
        /// 四象限：伪装布尔 × 身份生效中。第 2 行是旧语义的逐字保留，第 3 行是本波新增的一层，
        /// 第 1 行是「两层都不成立 → 恢复攻击」的负对照。
        /// </summary>
        [TestCase(false, false, true, TestName = "两层都不成立_敌人照常攻击")]
        [TestCase(true, false, false, TestName = "只伪装_旧语义逐字保留_不攻击")]
        [TestCase(false, true, false, TestName = "只身份生效_不攻击")]
        [TestCase(true, true, false, TestName = "两层都成立_不攻击")]
        public void Step_AttackPermission_CombinesDisguiseAndIdentity(bool disguised, bool identityInEffect, bool attacks)
        {
            bool attacked = rules.Step(new MonsterIntent(Target(disguised), 0.1f, identityInEffect));

            Assert.That(attacked, Is.EqualTo(attacks));
            Assert.That(model.Mode, Is.EqualTo(MonsterMode.Hostile), "红区内一律进敌对，攻击许可只管打不打");
        }

        /// <summary>身份失效（IsInEffect 由 true 变 false）后必须恢复攻击：同一只怪、同一位置，只换这一个布尔。</summary>
        [Test]
        public void Step_IdentityExpires_RestoresAttack()
        {
            // 第一个 tick 的步长足够把攻击冷却走完，所以第二个 tick 能不能打只取决于身份那一层。
            Assert.That(rules.Step(new MonsterIntent(Target(false), 1f, true)), Is.False, "身份生效中不攻击");
            Assert.That(rules.Step(new MonsterIntent(Target(false), 0.1f, false)), Is.True, "身份失效后恢复攻击");
        }

        /// <summary>身份生效中的这一层来自 IdentityAttackRules 的公开判据，不是本类自己写的布尔组合。</summary>
        [Test]
        public void IdentityAttackRules_MatchesTheWiredSemantics()
        {
            Assert.That(IdentityAttackRules.AllowsEnemyAttack(false, false), Is.True);
            Assert.That(IdentityAttackRules.AllowsEnemyAttack(true, false), Is.False);
            Assert.That(IdentityAttackRules.AllowsEnemyAttack(false, true), Is.False);
            Assert.That(IdentityAttackRules.AllowsEnemyAttack(true, true), Is.False);
        }

        /// <summary>装配层：EncounterStep 把身份状态逐 tick 填进 MonsterIntent（未接身份时恒 false）。</summary>
        [Test]
        public void EncounterStep_WithIdentityInEffect_EnemyDoesNotDamagePlayer()
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            try
            {
                var playerModel = new PlayerModel();
                var player = new PlayerRules(pc, playerModel, NullTelemetryScope.Instance);
                var step = new EncounterStep(player, rules);
                step.Begin(Vector2.zero, new[] { new Vector2(0.5f, 0f), new Vector2(10f, 0f) });
                IdentityState identity = BorrowedIdentity();

                var random = new RandomService(11ul);
                var command = new InputCommand(Vector2.zero, Vector2.zero, 0u, Vector2.zero, 0);
                var context = new SimulationContext(0, 0.1f, in command, random);

                step.BindIdentity(identity);
                step.Step(in context);
                Assert.That(playerModel.Health, Is.EqualTo(pc.MaxHealth), "身份生效中：敌人不攻击");

                step.BindIdentity(null); // 退回本体（等价于 IdentityState.IsInEffect 变 false）
                step.Step(in context);
                Assert.That(playerModel.Health, Is.LessThan(pc.MaxHealth), "身份失效后：恢复攻击");
            }
            finally { Object.DestroyImmediate(pc); }
        }

        /// <summary>借一个身份，让它处于「生效中」——判据与接线用的是同一个 IdentityState。</summary>
        private static IdentityState BorrowedIdentity()
        {
            var settings = new IdentitySettings();
            var state = new IdentityState();
            var ledger = new IdentityLedger();
            var catalog = IdentityCatalog.From(new[]
            {
                IdentityDefinition.Create("duzhi", "都知", IdentityOrigin.Possession, "duzhi_npc"),
            });
            IdentityEnterResult result = new IdentityRules(settings, catalog).TryEnter(state, ledger, IdentityId.From("duzhi"));
            Assert.That(result, Is.EqualTo(IdentityEnterResult.Entered));
            Assert.That(state.IsInEffect, Is.True, "占位时限为 0 = 无时限，借到即生效");
            return state;
        }
    }
}
