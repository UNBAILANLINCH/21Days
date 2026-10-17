// 职责：钉住遭遇里的露馅结算（PRP/combat-iteration D1，用户 2026-10-14 拍板「露馅 → 进追逐，且借来的身份当场失效」）：
//   ① 借身份进红区（口径：追逐 + 六种全失效）→ 身份失效、察觉者转敌对；
//   ② 规则层原因（账簿超量）在橙区里 → 察觉者被惊动（从警戒直接转敌对，证明惊动不是红区自己顺带的）；
//   ③ 负对照：没在借身份不结算（账簿超量也不惊动）；
//   ④ 字段为默认值的附加怪（巡逻者乙）不产生信号、不被惊动，完整参与者会；
//   ⑤ 死亡口径 → 玩家生命归零（剧情阶段内判 Downed）；
//   ⑥ 策略 None（代码默认）→ 身份按掩码照常（警戒值不夺身份）、不惊动；持续站在红区只结算一次（边沿闸）。
// 为什么新建：`EncounterKernelWiringTests` 守的是容器接线，`MonsterAttackPermissionTests` 守的是「身份生效中不挨打」，
//   露馅结算是本波新开的一段 tick 逻辑，口径与负对照都多，单列一个文件。
// 夹具：怪物不接种类表（全局默认：视野 75°、红区 2、橙区 6、贴身 1.5），身份内容复用 `IdentityTestContent`，
//   策略用生产的 `ConfiguredExposurePolicy`（改数值块切口径），埋点用 `RecordingTelemetrySink`。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Monster;
using Game.Narrative;
using Game.Player;
using Game.Tests.EditMode.Identity;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterExposureTests
    {
        private const string ExposureEvent = "monster/exposure_applied";
        private const string PlayerId = "player";
        private const string PrimaryId = "enerme";
        private const string AdditionalId = "patrol_b";

        private static readonly Vector2 PlayerSpot = Vector2.zero;

        /// <summary>主怪在玩家右侧 1.5 米、面朝玩家：红区（≤ 2、在视锥里）。</summary>
        private static readonly Vector2[] PrimaryRedRoute = { new Vector2(1.5f, 0f), new Vector2(-10f, 0f) };

        /// <summary>主怪在玩家右侧 4 米、面朝玩家：橙区（2 &lt; 4 ≤ 6、在视锥里）。</summary>
        private static readonly Vector2[] PrimaryOrangeRoute = { new Vector2(4f, 0f), new Vector2(-10f, 0f) };

        /// <summary>主怪在远处：谁都察觉不到玩家。</summary>
        private static readonly Vector2[] PrimaryFarRoute = { new Vector2(30f, 0f), new Vector2(40f, 0f) };

        /// <summary>附加怪在玩家上方 1.5 米、面朝玩家：红区。</summary>
        private static readonly Vector2[] AdditionalRedRoute = { new Vector2(0f, 1.5f), new Vector2(0f, -10f) };

        /// <summary>附加怪在玩家上方 4 米、面朝玩家：橙区。</summary>
        private static readonly Vector2[] AdditionalOrangeRoute = { new Vector2(0f, 4f), new Vector2(0f, -10f) };

        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        private IdentitySettings settings;
        private IdentityRules identityRules;
        private IdentityState identity;
        private IdentityLedger ledger;
        private SuspicionState suspicion;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);

            settings = IdentityTestContent.NewSettings();
            identityRules = new IdentityRules(settings, IdentityTestContent.NewCatalog(),
                new ConfiguredExposurePolicy(settings), telemetry.Scope("identity"));
            identity = new IdentityState();
            ledger = new IdentityLedger();
            suspicion = SuspicionState.FromSettings(settings);
        }

        [TearDown]
        public void TearDown()
        {
            telemetry.Dispose();
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(monsterConfig);
        }

        // ──────────────────────── ① 红区露馅：身份失效 + 察觉者转敌对 ────────────────────────

        [Test]
        public void Step_BorrowingInRedZone_WithChaseAndAllSixCauses_LosesIdentityAndPerceiverTurnsHostile()
        {
            UseUserDecidedPolicy();
            Rig rig = NewRig(PrimaryRedRoute);
            Borrow(IdentityTestContent.StewardId);

            Tick(rig.Step);

            Assert.That(identity.IsBorrowing, Is.False, "借来的身份当场失效");
            Assert.That(identity.ExposureCount, Is.EqualTo(1));
            Assert.That(rig.Primary.Model.Mode, Is.EqualTo(MonsterMode.Hostile), "察觉者转敌对");
            string line = LineFor(ExposureEvent);
            Assert.That(line, Is.Not.Null, "露馅结算那一刻记一条 exposure_applied");
            Assert.That(line, Does.Contain("\"cause\":" + (int)ExposureCause.Alertness));
            Assert.That(line, Does.Contain("\"outcome\":" + (int)ExposureOutcome.Chase));
            Assert.That(line, Does.Contain("\"identity_lost\":true"));
            Assert.That(line, Does.Contain("\"provoked\":1"));
        }

        /// <summary>
        /// 惊动是真起作用的：橙区里怪自己只会进警戒；账簿超量（规则层原因，身份规则自己读）让身份失效 →
        /// 本 tick 察觉到玩家的主怪被惊动，<b>当场</b>转敌对。
        /// </summary>
        [Test]
        public void Step_LedgerOverLimitWhilePerceivedInOrangeZone_ProvokesPerceiverToHostile()
        {
            Rig rig = NewRig(PrimaryOrangeRoute);
            OverfillLedgerAndBorrowSteward();

            Tick(rig.Step);

            Assert.That(identity.IsBorrowing, Is.False, "账簿在默认掩码里：身份失效");
            Assert.That(rig.Primary.SensedThisStep, Is.EqualTo(1), "前提：本 tick 它在橙区察觉到玩家");
            Assert.That(rig.Primary.Model.Mode, Is.EqualTo(MonsterMode.Hostile), "被惊动：不等警戒升满就转敌对");
        }

        /// <summary>对照组：同一站位、账簿没超量 → 只是橙区察觉，怪停在警戒，不转敌对。</summary>
        [Test]
        public void Step_BorrowingInOrangeZoneWithoutExposure_PerceiverOnlyGetsAlert()
        {
            Rig rig = NewRig(PrimaryOrangeRoute);
            Borrow(IdentityTestContent.StewardId);

            Tick(rig.Step);

            Assert.That(identity.IsInEffect, Is.True, "身份生效中，橙区不算露馅");
            Assert.That(rig.Primary.Model.Mode, Is.EqualTo(MonsterMode.Alert));
            Assert.That(LineFor(ExposureEvent), Is.Null);
        }

        // ──────────────────────── ③ 负对照：没在借身份 ────────────────────────

        /// <summary>
        /// 没在借身份就不结算：账簿超量本身是持续状态（不借身份也成立），若不先判「在借身份」，
        /// 这里会被当成露馅惊动怪物。
        /// </summary>
        [Test]
        public void Step_NotBorrowing_DoesNotSettleExposureEvenIfLedgerOverLimit()
        {
            UseUserDecidedPolicy();
            Rig rig = NewRig(PrimaryOrangeRoute);
            OverfillLedgerAndBorrowSteward();
            identityRules.TryExit(identity);
            Assert.That(identity.IsBorrowing, Is.False, "前提：已回到本体");

            Tick(rig.Step);

            Assert.That(rig.Primary.Model.Mode, Is.EqualTo(MonsterMode.Alert), "没被惊动，仍按橙区只进警戒");
            Assert.That(identity.ExposureCount, Is.Zero);
            Assert.That(LineFor(ExposureEvent), Is.Null);
        }

        /// <summary>没 BindExposure（独立原型场景 / 纯 Monster 作用域）：借着身份站进红区也不结算，与加之前一致。</summary>
        [Test]
        public void Step_WithoutBindExposure_DoesNotSettleExposure()
        {
            UseUserDecidedPolicy();
            Rig rig = NewRig(PrimaryRedRoute, bindExposure: false);
            Borrow(IdentityTestContent.StewardId);

            Tick(rig.Step);

            Assert.That(identity.IsInEffect, Is.True);
            Assert.That(LineFor(ExposureEvent), Is.Null);
        }

        // ──────────────────────── ④ 巡逻者乙不产生信号、不被惊动 ────────────────────────

        /// <summary>只有字段为默认值的附加怪在红区里看见玩家（主怪在远处）：它不产生露馅信号，身份照常。</summary>
        [Test]
        public void Step_OnlyDefaultAdditionalSeesBorrowerInRedZone_ProducesNoSignal()
        {
            UseUserDecidedPolicy();
            Rig rig = NewRig(PrimaryFarRoute, AdditionalRedRoute, additionalIsFullParticipant: false);
            Borrow(IdentityTestContent.StewardId);

            Tick(rig.Step);

            Assert.That(rig.Additional.SensedThisStep, Is.EqualTo(2), "前提：它确实在红区里看见了玩家");
            Assert.That(identity.IsInEffect, Is.True, "巡逻者乙不产生露馅信号");
            Assert.That(LineFor(ExposureEvent), Is.Null);
        }

        /// <summary>正对照：同一站位、附加怪勾了完整参与者 → 它的红区信号算数，身份失效。</summary>
        [Test]
        public void Step_FullParticipantAdditionalSeesBorrowerInRedZone_LosesIdentity()
        {
            UseUserDecidedPolicy();
            Rig rig = NewRig(PrimaryFarRoute, AdditionalRedRoute, additionalIsFullParticipant: true);
            Borrow(IdentityTestContent.StewardId);

            Tick(rig.Step);

            Assert.That(identity.IsBorrowing, Is.False);
            Assert.That(LineFor(ExposureEvent), Is.Not.Null);
        }

        /// <summary>
        /// 惊动只惊完整参与者：主怪与巡逻者乙都在橙区察觉到玩家，账簿超量让身份失效 →
        /// 主怪转敌对，巡逻者乙仍停在警戒。
        /// </summary>
        [Test]
        public void Step_ExposureWithBothPerceiving_ProvokesPrimaryButNotDefaultAdditional()
        {
            Rig rig = NewRig(PrimaryOrangeRoute, AdditionalOrangeRoute, additionalIsFullParticipant: false);
            OverfillLedgerAndBorrowSteward();

            Tick(rig.Step);

            Assert.That(rig.Additional.SensedThisStep, Is.EqualTo(1), "前提：巡逻者乙也察觉到了玩家");
            Assert.That(rig.Primary.Model.Mode, Is.EqualTo(MonsterMode.Hostile));
            Assert.That(rig.Additional.Model.Mode, Is.EqualTo(MonsterMode.Alert), "巡逻者乙不被惊动");
        }

        /// <summary>正对照：同一站位、附加怪是完整参与者 → 两只都被惊动。</summary>
        [Test]
        public void Step_ExposureWithFullParticipantPerceiving_ProvokesBoth()
        {
            Rig rig = NewRig(PrimaryOrangeRoute, AdditionalOrangeRoute, additionalIsFullParticipant: true);
            OverfillLedgerAndBorrowSteward();

            Tick(rig.Step);

            Assert.That(rig.Primary.Model.Mode, Is.EqualTo(MonsterMode.Hostile));
            Assert.That(rig.Additional.Model.Mode, Is.EqualTo(MonsterMode.Hostile));
            Assert.That(LineFor(ExposureEvent), Does.Contain("\"provoked\":2"));
        }

        // ──────────────────────── ⑤ 死亡口径 ────────────────────────

        /// <summary>口径为死亡：玩家生命归零；剧情阶段（一仗进行中）内同一 tick 判 Downed。</summary>
        [Test]
        public void Step_DeathOutcome_PlayerHealthDropsToZeroAndStoryBattleSettlesDowned()
        {
            settings.ExposureOutcome = ExposureOutcome.Death;
            Rig rig = NewRig(PrimaryRedRoute);
            Borrow(IdentityTestContent.StewardId);
            rig.Step.StartBattle(1, 1);

            Tick(rig.Step);

            Assert.That(rig.Player.Model.Health, Is.Zero);
            Assert.That(identity.Dead, Is.True, "身份规则那一侧也记了死亡");
            Assert.That(rig.Step.PendingResult, Is.EqualTo(EncounterStep.Result.Defeat));
            Assert.That(rig.Step.BattleSettlement.HasValue, Is.True);
            Assert.That(rig.Step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Downed));
        }

        // ──────────────────────── ⑥ 策略 None（代码默认） ────────────────────────

        /// <summary>
        /// 代码默认（口径 None、掩码五种）：借着身份站进红区 → 判出警戒值露馅、记一条埋点，但身份照常（警戒值不在默认掩码里）、
        /// 不惊动（provoked 0）、玩家不挨打（身份生效中）——与本波之前的玩法逐字一致，只多了一条埋点。
        /// </summary>
        [Test]
        public void Step_CodeDefaults_RedZoneKeepsIdentityAndProvokesNobody()
        {
            Rig rig = NewRig(PrimaryRedRoute);
            Borrow(IdentityTestContent.StewardId);

            Tick(rig.Step);

            Assert.That(identity.IsInEffect, Is.True);
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth), "身份生效中不挨打");
            string line = LineFor(ExposureEvent);
            Assert.That(line, Is.Not.Null);
            Assert.That(line, Does.Contain("\"outcome\":" + (int)ExposureOutcome.None));
            Assert.That(line, Does.Contain("\"identity_lost\":false"));
            Assert.That(line, Does.Contain("\"provoked\":0"));
        }

        /// <summary>边沿闸：同一组原因持续成立（一直站在红区里）只结算一次，不每 tick 刷埋点。</summary>
        [Test]
        public void Step_StayingInRedZone_SettlesOnlyOnce()
        {
            Rig rig = NewRig(PrimaryRedRoute);
            Borrow(IdentityTestContent.StewardId);

            for (int i = 0; i < 10; i++)
            {
                Tick(rig.Step);
            }

            Assert.That(CountOf(ExposureEvent), Is.EqualTo(1));
            Assert.That(CountOf("identity/exposed"), Is.EqualTo(1), "身份规则那一侧的露馅埋点同样只有一组");
        }

        // ──────────────────────── 夹具 ────────────────────────

        /// <summary>用户 2026-10-14 拍板的口径（W5 写进资产）：露馅进追逐 + 六种原因全让身份失效。</summary>
        private void UseUserDecidedPolicy()
        {
            settings.ExposureOutcome = ExposureOutcome.Chase;
            settings.IdentityLossCauses = IdentitySettings.DefaultIdentityLossCauses | ExposureCause.Alertness;
        }

        private void Borrow(IdentityId id)
        {
            Assert.That(identityRules.TryEnter(identity, ledger, id), Is.EqualTo(IdentityEnterResult.Entered));
            Assert.That(identity.IsInEffect, Is.True, "占位时限 0 = 借到即生效");
        }

        /// <summary>账簿上限 1、先后借两个不同身份 → 账簿超量（规则层原因），当前借着都知。</summary>
        private void OverfillLedgerAndBorrowSteward()
        {
            settings.LedgerLimit = 1;
            Borrow(IdentityTestContent.MusicianId);
            Borrow(IdentityTestContent.StewardId);
            Assert.That(ledger.IsOverLimit(settings.LedgerMode, settings.LedgerLimit), Is.True, "前提：账簿超量");
        }

        private Rig NewRig(Vector2[] primaryRoute, Vector2[] additionalRoute = null, bool additionalIsFullParticipant = false,
            bool bindExposure = true)
        {
            var player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            var primary = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(5ul), NullTelemetryScope.Instance);
            var step = new EncounterStep(player, primary);
            step.UseTelemetry(telemetry.Scope("monster"));
            if (additionalRoute != null)
            {
                step.ConfigureTaming(PlayerId, "玩家", new[] { PrimaryId, AdditionalId }, new[] { "巡逻者甲", "巡逻者乙" },
                    new[] { primaryRoute, additionalRoute }, null, new[] { false, additionalIsFullParticipant });
            }

            step.BindIdentity(identity);
            if (bindExposure)
            {
                step.BindExposure(identityRules, ledger, suspicion);
            }

            step.Begin(PlayerSpot, primaryRoute);
            return new Rig(step, player, primary, additionalRoute == null ? null : step.Taming.GetTarget(AdditionalId));
        }

        private static void Tick(EncounterStep step)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, 0u, Vector2.zero, 0);
            var context = new SimulationContext(0, 0.1f, in command, new RandomService(5ul));
            step.Step(in context);
        }

        private string LineFor(string eventName)
        {
            for (int i = 0; i < sink.Lines.Count; i++)
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
            public Rig(EncounterStep step, PlayerRules player, MonsterRules primary, MonsterRules additional)
            {
                Step = step;
                Player = player;
                Primary = primary;
                Additional = additional;
            }

            public EncounterStep Step { get; }
            public PlayerRules Player { get; }
            public MonsterRules Primary { get; }
            public MonsterRules Additional { get; }
        }
    }
}
