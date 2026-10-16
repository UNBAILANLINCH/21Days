// 职责：钉住遭遇里的被抓后果（PRP/combat-iteration D4，用户 2026-10-14 拍板「先击倒，没血才判死」）：
//   ① 口径 None（不装配内核时的默认）→ 被抓只写事实，不改血量（加之前的行为）；
//   ② 口径 KnockdownThenDeath + 次数 0 → 被抓多少次都只击倒，血量归零才判死；对照：次数 2 时第二次被抓直接判死；
//   ③ 被抓判据放宽到任一完整参与者：主怪按原判据（追逐中打到人）、附加完整参与者打到人即算；
//      字段为默认值的附加怪（巡逻者乙）打到人不算被抓（负对照）；
//   ④ 剧情阶段（一仗进行中）内被抓判死 → 同一 tick 判 Downed；
//   ⑤ 埋点 chase_caught 带口径 / 是否失败 / 实时血量 / 此前次数。
// 为什么新建：`EncounterStealthTests` 管遮挡 / 击倒 / 追逐键的结算，`StealthRulesTests` 管 ChaseCaughtRules 纯函数；
//   「被抓 → 按实时血量裁决 → 置 0」是本波新接的一段 tick 逻辑。
// 夹具：怪物不接种类表（全局默认：生命 3、伤害 1、攻击距离 0.8、冷却 1 秒），玩家生命 3；
//   口径经真实的 StealthKernel 装配（UseStealth 存下的就是内核那份 ChaseOutcomeSettings），配置用内存里建的 StealthConfig。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Narrative;
using Game.Player;
using Game.Stealth;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterChaseCaughtTests
    {
        private const string CaughtEvent = "monster/chase_caught";
        private const string PlayerId = "player";
        private const string PrimaryId = "enerme";
        private const string AdditionalId = "xunye";
        private const float Dt = 0.1f;

        private static readonly Vector2 PlayerSpot = Vector2.zero;

        /// <summary>主怪贴着玩家右侧 0.5 米、面朝玩家：第一 tick 就转敌对并出手。</summary>
        private static readonly Vector2[] PrimaryAdjacentRoute = { new Vector2(0.5f, 0f), new Vector2(-10f, 0f) };

        /// <summary>主怪在远处：不参与。</summary>
        private static readonly Vector2[] PrimaryFarRoute = { new Vector2(30f, 0f), new Vector2(40f, 0f) };

        /// <summary>附加怪贴着玩家上方 0.5 米、面朝玩家：第一 tick 就转敌对并出手。</summary>
        private static readonly Vector2[] AdditionalAdjacentRoute = { new Vector2(0f, 0.5f), new Vector2(0f, -10f) };

        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private StealthConfig stealthConfig;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            stealthConfig = ScriptableObject.CreateInstance<StealthConfig>();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
        }

        [TearDown]
        public void TearDown()
        {
            telemetry.Dispose();
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(monsterConfig);
            Object.DestroyImmediate(stealthConfig);
        }

        // ──────────────────────── ① 口径 None ────────────────────────

        /// <summary>不装配内核（代码默认口径 None）：完整参与者打到人 → 写 chase.caught，血量只按伤害扣，与加之前一致。</summary>
        [Test]
        public void Caught_WithoutKernel_PolicyNone_OnlyWritesFact()
        {
            Rig rig = NewRig(PrimaryFarRoute, AdditionalAdjacentRoute, additionalIsFullParticipant: true, policy: null);
            Assert.That(rig.Step.ChaseOutcomeSettings.Policy, Is.EqualTo(ChaseCaughtPolicy.None), "代码默认口径 = 旧行为");

            Tick(rig.Step);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseCaught), Is.True);
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - 1), "只扣了这一下的伤害");
        }

        /// <summary>装配了内核、口径写 None：同样什么都不做。</summary>
        [Test]
        public void Caught_PolicyNoneFromKernel_KeepsHealth()
        {
            Rig rig = NewRig(PrimaryFarRoute, AdditionalAdjacentRoute, additionalIsFullParticipant: true,
                policy: ChaseCaughtPolicy.None, failCount: 0);

            Tick(rig.Step);

            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - 1));
        }

        // ──────────────────────── ② 先击倒，没血才判死 ────────────────────────

        /// <summary>
        /// 次数 0（用户拍板写进资产的口径）：被抓第 1、2 次都只击倒、血量照常递减，第 3 次血量归零才死。
        /// </summary>
        [Test]
        public void Caught_KnockdownThenDeath_ZeroCount_OnlyKnockdownUntilHealthRunsOut()
        {
            Rig rig = NewRig(PrimaryFarRoute, AdditionalAdjacentRoute, additionalIsFullParticipant: true,
                policy: ChaseCaughtPolicy.KnockdownThenDeath, failCount: 0);

            TickUntilHealth(rig, playerConfig.MaxHealth - 1);
            Assert.That(rig.Player.Knockdown.IsDowned, Is.True, "第一次被抓：击倒");

            TickUntilHealth(rig, playerConfig.MaxHealth - 2);
            Assert.That(rig.Player.Model.Health, Is.EqualTo(1), "第二次被抓：还有血就不判死（不按次数）");
            Assert.That(CountOf(CaughtEvent), Is.EqualTo(2));

            TickUntilHealth(rig, 0);
            Assert.That(rig.Player.Model.Health, Is.Zero, "血量归零才死");
            Assert.That(LastLineFor(CaughtEvent), Does.Contain("\"failure\":true"));
        }

        /// <summary>对照组：次数 2（代码默认）时第二次被抓直接判死——哪怕这一下伤害后还剩 1 血。</summary>
        [Test]
        public void Caught_KnockdownThenDeath_CountTwo_SecondCatchIsFatalEvenWithHealthLeft()
        {
            Rig rig = NewRig(PrimaryFarRoute, AdditionalAdjacentRoute, additionalIsFullParticipant: true,
                policy: ChaseCaughtPolicy.KnockdownThenDeath, failCount: 2);

            TickUntilHealth(rig, playerConfig.MaxHealth - 1);
            Assert.That(rig.Player.Model.Health, Is.EqualTo(2), "第一次被抓：只击倒");

            TickUntilHealthBelow(rig, 2);
            Assert.That(rig.Player.Model.Health, Is.Zero, "第二次被抓按次数判死，生命置 0");
            string line = LastLineFor(CaughtEvent);
            Assert.That(line, Does.Contain("\"failure\":true"));
            Assert.That(line, Does.Contain("\"health\":1"), "埋点记的是裁决时的实时血量（这一下伤害之后）");
            Assert.That(line, Does.Contain("\"count\":1"), "此前被抓（击倒）一次");
            Assert.That(line, Does.Contain("\"policy\":" + (int)ChaseCaughtPolicy.KnockdownThenDeath));
        }

        // ──────────────────────── ③ 被抓判据：任一完整参与者 ────────────────────────

        /// <summary>主怪按原判据：第一下打到人时还没起追（不算被抓），追逐中再打到 → 被抓并按口径判死。</summary>
        [Test]
        public void Caught_ByPrimaryWhileChasing_AppliesPolicy()
        {
            Rig rig = NewRig(PrimaryAdjacentRoute, null, false, policy: ChaseCaughtPolicy.Death, failCount: 0);

            Tick(rig.Step);
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - 1), "第一下：起追的那一 tick 不算被抓");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseActive), Is.True, "前提：已起追");

            TickUntilHealthBelow(rig, playerConfig.MaxHealth - 1);
            Assert.That(rig.Player.Model.Health, Is.Zero, "追逐中被主怪打到 = 被抓，口径 Death 判死");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseCaught), Is.True);
        }

        /// <summary>附加完整参与者打到人即算被抓（不经追逐相位）：口径 Death → 第一下就判死。</summary>
        [Test]
        public void Caught_ByFullParticipantAdditional_CountsAsCaught()
        {
            Rig rig = NewRig(PrimaryFarRoute, AdditionalAdjacentRoute, additionalIsFullParticipant: true,
                policy: ChaseCaughtPolicy.Death, failCount: 0);

            Tick(rig.Step);

            Assert.That(rig.Additional.AttackedThisStep, Is.True, "前提：这一 tick 是它打到了人");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseCaught), Is.True);
            Assert.That(rig.Player.Model.Health, Is.Zero);
        }

        /// <summary>负对照：字段为默认值的附加怪（巡逻者乙）打到人不算被抓——同一口径 Death 下只扣这一下的血。</summary>
        [Test]
        public void Caught_ByDefaultAdditional_DoesNotCount()
        {
            Rig rig = NewRig(PrimaryFarRoute, AdditionalAdjacentRoute, additionalIsFullParticipant: false,
                policy: ChaseCaughtPolicy.Death, failCount: 0);

            Tick(rig.Step);

            Assert.That(rig.Additional.AttackedThisStep, Is.True, "前提：它确实打到了人");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseCaught), Is.False);
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - 1));
            Assert.That(LastLineFor(CaughtEvent), Is.Null);
        }

        // ──────────────────────── ④ 剧情阶段内归零走 Downed ────────────────────────

        [Test]
        public void Caught_InStoryBattle_FatalCatchSettlesDownedSameTick()
        {
            Rig rig = NewRig(PrimaryFarRoute, AdditionalAdjacentRoute, additionalIsFullParticipant: true,
                policy: ChaseCaughtPolicy.Death, failCount: 0);
            rig.Step.StartBattle(1, 1);

            Tick(rig.Step);

            Assert.That(rig.Player.Model.Health, Is.Zero);
            Assert.That(rig.Step.PendingResult, Is.EqualTo(EncounterStep.Result.Defeat));
            Assert.That(rig.Step.BattleSettlement.HasValue, Is.True);
            Assert.That(rig.Step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Downed));
        }

        // ──────────────────────── 夹具 ────────────────────────

        /// <param name="policy">null = 不装配内核（代码默认）；否则按此口径与 <paramref name="failCount"/> 建内核再 UseStealth。</param>
        private Rig NewRig(Vector2[] primaryRoute, Vector2[] additionalRoute, bool additionalIsFullParticipant,
            ChaseCaughtPolicy? policy, int failCount = 0)
        {
            var player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            var primary = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul), NullTelemetryScope.Instance);
            var step = new EncounterStep(player, primary);
            step.UseTelemetry(telemetry.Scope("monster"));
            if (policy.HasValue)
            {
                var serialized = new SerializedObject(stealthConfig);
                serialized.FindProperty("chaseCaughtPolicy").intValue = (int)policy.Value;
                serialized.FindProperty("chaseCaughtFailCount").intValue = failCount;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                step.UseStealth(new StealthKernel(stealthConfig));
                Assert.That(step.ChaseOutcomeSettings.Policy, Is.EqualTo(policy.Value), "前提：UseStealth 存下了内核的口径");
                Assert.That(step.ChaseOutcomeSettings.FailAfterCaughtCount, Is.EqualTo(failCount));
            }

            if (additionalRoute != null)
            {
                step.ConfigureTaming(PlayerId, "玩家", new[] { PrimaryId, AdditionalId }, new[] { "巡逻者甲", "巡夜人" },
                    new[] { primaryRoute, additionalRoute }, null, new[] { false, additionalIsFullParticipant });
            }

            step.Begin(PlayerSpot, primaryRoute);
            return new Rig(step, player, additionalRoute == null ? null : step.Taming.GetTarget(AdditionalId));
        }

        private static void Tick(EncounterStep step)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, 0u, Vector2.zero, 0);
            var context = new SimulationContext(0, Dt, in command, new RandomService(3ul));
            step.Step(in context);
        }

        /// <summary>推进到玩家生命恰好等于 <paramref name="health"/>（怪物攻击冷却 1 秒，最多推 5 秒）。</summary>
        private static void TickUntilHealth(Rig rig, int health)
        {
            for (int i = 0; i < 50 && rig.Player.Model.Health != health; i++)
            {
                Tick(rig.Step);
            }

            Assert.That(rig.Player.Model.Health, Is.EqualTo(health), "5 秒内没打到这个血量：夹具站位或冷却变了");
        }

        /// <summary>推进到玩家生命低于 <paramref name="health"/>（最多推 5 秒）。</summary>
        private static void TickUntilHealthBelow(Rig rig, int health)
        {
            for (int i = 0; i < 50 && rig.Player.Model.Health >= health; i++)
            {
                Tick(rig.Step);
            }

            Assert.That(rig.Player.Model.Health, Is.LessThan(health), "5 秒内没再挨打：夹具站位或冷却变了");
        }

        private string LastLineFor(string eventName)
        {
            for (int i = sink.Lines.Count - 1; i >= 0; i--)
            {
                if (sink.Lines[i].Contains(eventName))
                {
                    return sink.Lines[i];
                }
            }

            return null;
        }

        private int CountOf(string eventName)
        {
            int count = 0;
            for (int i = 0; i < sink.Lines.Count; i++)
            {
                if (sink.Lines[i].Contains(eventName))
                {
                    count++;
                }
            }

            return count;
        }

        private readonly struct Rig
        {
            public Rig(EncounterStep step, PlayerRules player, MonsterRules additional)
            {
                Step = step;
                Player = player;
                Additional = additional;
            }

            public EncounterStep Step { get; }
            public PlayerRules Player { get; }
            public MonsterRules Additional { get; }
        }
    }
}
