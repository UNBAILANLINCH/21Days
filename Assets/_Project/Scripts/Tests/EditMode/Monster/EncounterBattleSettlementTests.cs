// 职责：钉住 C5 在战斗侧的一半——EncounterStep 承载 BattleResult（承载而不是替换 Result 枚举）。
// 为什么新建：`NarrativeBattleTests` 覆盖的是剧情侧的 CompleteBattle 契约；「战斗侧把结果记下来」这一半
//   在接线前只有字段没有消费方（`docs/roadmap.md:223`），需要一条测试说明它现在到底承载什么、什么时候为 null。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Narrative;
using Game.Player;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterBattleSettlementTests
    {
        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
        }

        [TearDown]
        public void TearDown()
        {
            telemetry.Dispose();
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(monsterConfig);
        }

        /// <summary>四种结果各自带着自己的出口键被承载（`BossPhaseChanged` 连形态一起）。</summary>
        [TestCase("Downed", "Downed")]
        [TestCase("Exposed", "Exposed")]
        [TestCase("BossPhaseChanged:form2", "BossPhaseChanged:form2")]
        [TestCase("Victory", "Victory")]
        public void SettleBattle_CarriesEachResultWithItsExitKey(string key, string exitKey)
        {
            EncounterStep step = NewStep(out _, out _);
            step.StartBattle(1, 7);

            Assert.That(step.SettleBattle(BattleResult.Parse(key)), Is.True);
            Assert.That(step.BattleSettlement.HasValue, Is.True);
            Assert.That(step.BattleSettlement.Value.ExitKey, Is.EqualTo(exitKey));
        }

        /// <summary>未结算时为 null——这是「非战斗收尾不产生战斗结果」的判据。</summary>
        [Test]
        public void BattleSettlement_BeforeAnySettlement_IsNull()
        {
            EncounterStep step = NewStep(out _, out _);

            Assert.That(step.BattleSettlement.HasValue, Is.False);
            step.StartBattle(1, 7);
            Assert.That(step.BattleSettlement.HasValue, Is.False, "建立战斗关联不等于结算");
        }

        /// <summary>中途放弃（Abort）是流程收尾，不是战斗结果：Result 变 Aborted，BattleSettlement 仍为 null。</summary>
        [Test]
        public void AbortBattle_NonBattleEnding_LeavesNoBattleResult()
        {
            EncounterStep step = NewStep(out _, out _);
            step.StartBattle(1, 7);

            step.AbortBattle();

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Aborted));
            Assert.That(step.BattleSettlement.HasValue, Is.False);
        }

        /// <summary>一场遭遇只结算一次：第二次调用被拒（避免旧回调把结算覆盖掉）。</summary>
        [Test]
        public void SettleBattle_SecondCall_IsRejected()
        {
            EncounterStep step = NewStep(out _, out _);
            step.StartBattle(1, 7);

            Assert.That(step.SettleBattle(BattleResult.Parse("Victory")), Is.True);
            Assert.That(step.SettleBattle(BattleResult.Parse("Downed")), Is.False);
            Assert.That(step.BattleSettlement.Value.ExitKey, Is.EqualTo("Victory"));
        }

        /// <summary>结算写 `battle_settled` 埋点（PRP §2.6 的战斗侧那一条）。</summary>
        [Test]
        public void SettleBattle_TracksBattleSettledTelemetry()
        {
            EncounterStep step = NewStep(out _, out _);
            step.UseTelemetry(telemetry.Scope("monster"));
            step.StartBattle(3, 9);

            step.SettleBattle(BattleResult.Parse("Exposed"));

            // 不数行数：TelemetryService 可能先写一条会话头，只钉最后一条是本事件。
            Assert.That(sink.Last, Does.Contain("monster/battle_settled"));
            Assert.That(sink.Last, Does.Contain("\"result\":\"Exposed\""));
            Assert.That(sink.Last, Does.Contain("\"activation\":9"));
        }

        /// <summary>小怪清空自动结算 Victory（唯一无歧义、不依赖未拍板口径的映射）。</summary>
        [Test]
        public void Step_WhenMonsterDies_AutoSettlesVictory()
        {
            EncounterStep step = NewStep(out _, out MonsterRules monster);
            step.StartBattle(1, 7);
            var damage = new DamageIntent(monsterConfig.MaxHealth);
            var attacker = new PlayerSnapshot(Vector2.zero, Vector2.right, false, false, 3, false);
            Assert.That(monster.ApplyDamage(in damage, in attacker), Is.True);

            Tick(step);

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Victory));
            Assert.That(step.BattleSettlement.HasValue, Is.True);
            Assert.That(step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Victory));
        }

        /// <summary>
        /// 负对照：玩家被打死**不**自动写战斗结果——「被击倒 / 被看穿」哪一种是失败口径属
        /// `00_功能总览.md` §8.1 #3/#4 的拍板项，本波留空（流程仍按 Result.Defeat 收尾）。
        /// </summary>
        [Test]
        public void Step_WhenPlayerDies_LeavesBattleResultUnset()
        {
            EncounterStep step = NewStep(out PlayerRules player, out _);
            step.StartBattle(1, 7);
            var damage = new DamageIntent(playerConfig.MaxHealth);
            player.ApplyDamage(in damage);

            Tick(step);

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Defeat));
            Assert.That(step.BattleSettlement.HasValue, Is.False, "失败口径未拍板，战斗侧不替策划决定");
        }

        /// <summary>Begin 会清掉上一场的结算（重开一场不会带着旧结果）。</summary>
        [Test]
        public void Begin_ClearsPreviousSettlement()
        {
            EncounterStep step = NewStep(out _, out _);
            step.StartBattle(1, 7);
            Assert.That(step.SettleBattle(BattleResult.Parse("Victory")), Is.True);

            step.Begin(Vector2.zero, new[] { new Vector2(10f, 0f) });

            Assert.That(step.BattleSettlement.HasValue, Is.False);
        }

        private EncounterStep NewStep(out PlayerRules player, out MonsterRules monster)
        {
            player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            monster = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul), NullTelemetryScope.Instance);
            var step = new EncounterStep(player, monster);
            step.Begin(Vector2.zero, new[] { new Vector2(10f, 0f), new Vector2(20f, 0f) });
            return step;
        }

        private static void Tick(EncounterStep step)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, 0u, Vector2.zero, 0);
            var context = new SimulationContext(0, 0.1f, in command, new RandomService(3ul));
            step.Step(in context);
        }
    }
}
