// 职责：钉住「一场遭遇多种怪」（PRP/combat-iteration D2）在 EncounterStep 上的四条口径：
//   ① 不同种类各用各的数值 / 可否击杀 / 掉落（TamingActor.kindId → ConfigureTaming → MonsterRules.CreateForActor）；
//   ② 两个字段为默认值的附加怪（巡逻者乙）与改动前逐字一致——不传两个数组、或传了但它那一位是默认值，结果相同；
//   ③ 掉落按 id 各一次，只有主怪与完整参与者会掉；
//   ④ 附加的完整参与者死亡（被打死或被处决）只是它自己死，不结算整场；整场胜负只认主怪。
// 为什么新建：`EncounterDropTests` 管「掉落调用侧只结一次」（单只主怪），`EncounterStepTests` 管同一种类的多目标驯服与回放；
//   「同一场里各用各的种类、只有完整参与者掉落 / 结算口径只认主怪」是这一波的新口径，塞进任一已有文件都会让它的职责说不通。
// 用例一律用**真实生成的表**（同 EncounterDropTests 的理由：MonsterKind 是 sealed 只读视图，不自造假种类）：
//   1003 市令（killable=false + 暗杀，妖 3）当主怪，同 Boot；1001 巡夜人（killable=false + 暗杀，生命 3，妖 1；2026-10-15 用户拍板改为只能暗杀）；1002 井边妇人（killable，生命 2，妖 2）。
// 假对象复用：掉落口用 `EncounterDropTests.RecordingDropSink`，配置服务用 `WorldTestSupport.StubConfigService`，
//   埋点用 `RecordingTelemetrySink` + `FakeTelemetryClock`。
using Game.Core.Config;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Mirror;
using Game.Monster;
using Game.Narrative;
using Game.Player;
using Game.Stealth;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using Game.Tests.EditMode.World;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    /// <summary>一场遭遇多种怪：种类、完整参与者、按 id 掉落、胜负只认主怪。</summary>
    public sealed class EncounterMultiKindTests
    {
        /// <summary>主怪：市令（`monster_species/1003.json` → `yao/3.json`：killable=false + 暗杀）。同 Boot 的 MonsterInstaller.kindId。</summary>
        private const int ShilingKindId = 1003;
        private const int ShilingYaoId = 3;

        /// <summary>巡夜人（`1001.json` → `yao/1.json`：killable=false + 暗杀（2026-10-15 用户拍板「只能暗杀」），生命 3，橙区 6）。</summary>
        private const int XunyeKindId = 1001;
        private const int XunyeYaoId = 1;

        /// <summary>井边妇人（`1002.json` → `yao/2.json`：killable，生命 2，橙区 4.5）。</summary>
        private const int JingbianKindId = 1002;
        private const int JingbianYaoId = 2;

        private const string PlayerId = "player";
        private const string PrimaryId = "enerme";
        private const string DefaultAdditionalId = "patrol_b";
        private const string XunyeId = "xunye";
        private const string JingbianId = "jingbian";

        private static readonly string[] Ids = { PrimaryId, DefaultAdditionalId, XunyeId, JingbianId };
        private static readonly string[] Names = { "巡逻者甲", "巡逻者乙", "巡夜人", "井边妇人" };

        // 四只怪各自一条路线，彼此相隔 10 米；玩家出生点离谁都远于橙区（6）+ 余量，夹具里没人会察觉玩家。
        private static readonly Vector2 FarSpawn = new Vector2(0f, -30f);
        private static readonly Vector2[] PrimaryRoute = { new Vector2(30f, 0f), new Vector2(35f, 0f) };
        private static readonly Vector2[] DefaultAdditionalRoute = { new Vector2(0f, 10f), new Vector2(5f, 10f) };
        private static readonly Vector2[] XunyeRoute = { new Vector2(10f, 0f), new Vector2(15f, 0f) };
        private static readonly Vector2[] JingbianRoute = { new Vector2(20f, 0f), new Vector2(25f, 0f) };

        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private YaoCatalog yao;
        private MonsterKindCatalog catalog;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            var configService = new WorldTestSupport.StubConfigService(
                ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            yao = new YaoCatalog(configService);
            catalog = new MonsterKindCatalog(configService, yao, monsterConfig.HostileRadius);
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

        // ──────────────────────── ① 各用各的种类 ────────────────────────

        /// <summary>
        /// 种类覆盖非 0 的附加怪按自己的种类查表：种类 id、生命、可否击杀、掉落内容都与主怪不同；
        /// 种类覆盖为 0 的附加怪沿用主怪种类。
        /// </summary>
        [Test]
        public void ConfigureTaming_WithKindOverrides_EachTargetResolvesItsOwnKind()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);

            MonsterRules primary = step.Taming.GetTarget(PrimaryId);
            MonsterRules patrolB = step.Taming.GetTarget(DefaultAdditionalId);
            MonsterRules xunye = step.Taming.GetTarget(XunyeId);
            MonsterRules jingbian = step.Taming.GetTarget(JingbianId);

            Assert.That(primary.Kind.KindId, Is.EqualTo(ShilingKindId));
            Assert.That(patrolB.Kind.KindId, Is.EqualTo(ShilingKindId), "种类覆盖为 0 = 沿用主怪种类");
            Assert.That(xunye.Kind.KindId, Is.EqualTo(XunyeKindId));
            Assert.That(jingbian.Kind.KindId, Is.EqualTo(JingbianKindId));

            // 数值：生命取各自那一行（巡夜人 3、井边妇人 2），橙区半径同理（6 与 4.5）。
            Assert.That(xunye.Model.Health, Is.EqualTo(catalog.Get(XunyeKindId).MaxHealth));
            Assert.That(jingbian.Model.Health, Is.EqualTo(catalog.Get(JingbianKindId).MaxHealth));
            Assert.That(jingbian.Model.Health, Is.Not.EqualTo(xunye.Model.Health), "前提：两种怪的生命在表里不同，否则这条测不出「各用各的」");
            Assert.That(monsterConfig.ResolveAlertRadius(xunye.Kind), Is.Not.EqualTo(monsterConfig.ResolveAlertRadius(jingbian.Kind)));

            // 可否击杀：市令 / 巡夜人只能暗杀（拒伤），井边妇人可常规击杀——「各用各的」由井边妇人这一只对照。
            // 巡夜人原为 killable = true；2026-10-15 用户拍板改为只能暗杀（yao/1.json killable = false）。
            Assert.That(primary.IsKillable, Is.False);
            Assert.That(patrolB.IsKillable, Is.False);
            Assert.That(xunye.IsKillable, Is.False);
            Assert.That(jingbian.IsKillable, Is.True);

            // 掉落内容：各自妖物行的 drop_items。
            Assert.That(xunye.Kind.YaoId, Is.EqualTo(XunyeYaoId));
            Assert.That(jingbian.Kind.YaoId, Is.EqualTo(JingbianYaoId));
            Assert.That(xunye.DropItemIds, Is.EqualTo(yao.DropItemsOf(XunyeYaoId)));
            Assert.That(jingbian.DropItemIds, Is.EqualTo(yao.DropItemsOf(JingbianYaoId)));
        }

        /// <summary>
        /// 常规伤害口径各按各的种类：同一下普攻，市令那一份拒伤、井边妇人那一份掉血。
        /// （「可击杀」那一方原是巡夜人；2026-10-15 巡夜人改为只能暗杀（用户拍板），换成井边妇人，断言语义不变。）
        /// </summary>
        [Test]
        public void ApplyDamage_PerKind_UnkillablePrimaryRefusesWhileKillableAdditionalLosesHealth()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);
            var damage = new DamageIntent(1);
            var attacker = new PlayerSnapshot(Vector2.zero, Vector2.right, false, false, 3);

            Assert.That(step.Taming.GetTarget(PrimaryId).ApplyDamage(in damage, in attacker), Is.False, "市令 killable=false 拒伤");
            Assert.That(step.Taming.GetTarget(JingbianId).ApplyDamage(in damage, in attacker), Is.True);
            Assert.That(step.Taming.GetTarget(JingbianId).Model.Health, Is.EqualTo(catalog.Get(JingbianKindId).MaxHealth - 1));
        }

        /// <summary>
        /// 种类覆盖指向表里没有的 id：查不到就按全局默认跑并记一条 Error（MonsterRules.ResolveKind 的既有口径），
        /// 报错来源点名到那只巡逻者的 TamingActor.kindId 上，而不是 MonsterInstaller。
        /// </summary>
        [Test]
        public void ConfigureTaming_UnknownKindOverride_FallsBackToGlobalAndNamesTheActorField()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("没有种类 9999.*巡逻者 xunye 的 TamingActor\\.kindId"));
            EncounterStep step = NewStep(withOptionalArrays: true, xunyeKindId: 9999);

            Assert.That(step.Taming.GetTarget(XunyeId).Kind, Is.Null, "查不到种类：整只怪走全局默认");
            Assert.That(step.Taming.GetTarget(XunyeId).Model.Health, Is.EqualTo(monsterConfig.MaxHealth));
        }

        // ──────────────────────── ② 巡逻者乙回归 ────────────────────────

        /// <summary>
        /// 巡逻者乙回归（静态面）：两个字段为默认值的附加怪，不论 ConfigureTaming 传不传两个可选数组，
        /// 都沿用主怪种类、不是完整参与者、不在处决目标里。
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void DefaultFieldsAdditional_SameKindAsPrimary_AndNotAFullParticipant(bool withOptionalArrays)
        {
            EncounterStep step = NewStep(withOptionalArrays);

            Assert.That(step.Taming.GetTarget(DefaultAdditionalId).Kind.KindId,
                Is.EqualTo(step.Taming.GetTarget(PrimaryId).Kind.KindId), "沿用主怪种类");
            Assert.That(step.IsFullParticipant(DefaultAdditionalId), Is.False);
            Assert.That(step.CollectFullParticipants(), Has.No.Member(step.Taming.GetTarget(DefaultAdditionalId)));
        }

        /// <summary>
        /// 巡逻者乙回归（动态面）：同一个随机种子下推进 10 秒，「不传两个数组」与「传了、但它那一位是默认值」
        /// 两场里巡逻者乙的状态逐字段相同（位置 / 朝向 / 模式 / 生命 / 警戒 / 巡逻计时 / 随机流状态）。
        /// </summary>
        [Test]
        public void DefaultFieldsAdditional_TicksIdenticallyWithOrWithoutOptionalArrays()
        {
            EncounterStep before = NewStep(withOptionalArrays: false);
            EncounterStep after = NewStep(withOptionalArrays: true);
            MonsterSaveData initial = after.Taming.GetTarget(DefaultAdditionalId).Capture();

            for (int i = 0; i < 600; i++)
            {
                Tick(before, 1f / 60f);
                Tick(after, 1f / 60f);
            }

            MonsterSaveData expected = before.Taming.GetTarget(DefaultAdditionalId).Capture();
            MonsterSaveData actual = after.Taming.GetTarget(DefaultAdditionalId).Capture();
            Assert.That(actual.HasSameState(initial), Is.False, "前提：它真的在巡逻（推进生效），否则这条比的是两份初值");
            Assert.That(actual.HasSameState(expected), Is.True, "巡逻者乙的行为不许因为加了两个字段而变");
        }

        /// <summary>巡逻者乙回归（掉落面）：它死了不掉——与 2026-10-08「只结主怪」时一致。</summary>
        [Test]
        public void DefaultFieldsAdditional_Dies_DropsNothing()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);
            var drops = new EncounterDropTests.RecordingDropSink();
            step.BindDropSink(drops);

            Assert.That(step.Taming.GetTarget(DefaultAdditionalId).ApplyExecution(), Is.True, "前提：它死了");

            Assert.That(step.SettleKillDropOnce(), Is.False);
            Assert.That(drops.Drops, Is.Empty);
        }

        // ──────────────────────── ③ 按 id 掉落 ────────────────────────

        /// <summary>主怪与两只完整参与者同时死亡：各按自己的妖物 id 结一次（主怪在先），重复调用不翻倍。</summary>
        [Test]
        public void SettleKillDropOnce_EachFullParticipantDropsOnceWithItsOwnKind()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);
            var drops = new EncounterDropTests.RecordingDropSink();
            step.BindDropSink(drops);
            Kill(step.Taming.GetTarget(PrimaryId));
            Kill(step.Taming.GetTarget(DefaultAdditionalId));
            Kill(step.Taming.GetTarget(XunyeId));
            Kill(step.Taming.GetTarget(JingbianId));

            Assert.That(step.SettleKillDropOnce(), Is.True);
            Assert.That(step.SettleKillDropOnce(), Is.False, "同一场里同一只只结一次");

            Assert.That(drops.Drops, Has.Count.EqualTo(3), "主怪 + 两只完整参与者；默认字段的巡逻者乙不掉");
            Assert.That(drops.Drops[0].Yao, Is.EqualTo(ShilingYaoId));
            Assert.That(drops.Drops[1].Yao, Is.EqualTo(XunyeYaoId));
            Assert.That(drops.Drops[2].Yao, Is.EqualTo(JingbianYaoId));
            Assert.That(drops.Drops[1].Items, Is.EqualTo(yao.DropItemsOf(XunyeYaoId)));
        }

        /// <summary>闸门按 id 各自独立：附加怪先死只结它自己，主怪后死照样能结；Begin 之后全部清零。</summary>
        [Test]
        public void SettleKillDropOnce_GatesAreIndependentPerTarget_AndBeginResetsAll()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);
            var drops = new EncounterDropTests.RecordingDropSink();
            step.BindDropSink(drops);

            Kill(step.Taming.GetTarget(XunyeId));
            Assert.That(step.SettleKillDropOnce(), Is.True);
            Assert.That(drops.Drops, Has.Count.EqualTo(1));
            Assert.That(drops.Drops[0].Yao, Is.EqualTo(XunyeYaoId));

            Kill(step.Taming.GetTarget(PrimaryId));
            Assert.That(step.SettleKillDropOnce(), Is.True, "主怪的闸门没被附加怪那一次占掉");
            Assert.That(drops.Drops, Has.Count.EqualTo(2));
            Assert.That(drops.Drops[1].Yao, Is.EqualTo(ShilingYaoId));

            step.Begin(FarSpawn, PrimaryRoute);
            Kill(step.Taming.GetTarget(XunyeId));
            Assert.That(step.SettleKillDropOnce(), Is.True, "新的一场要能再掉");
            Assert.That(drops.Drops, Has.Count.EqualTo(3));
        }

        /// <summary>掉落埋点带上目标 id（按 id 结算之后，同一场可能有几只怪各掉一次，没有 id 就对不上号）。</summary>
        [Test]
        public void SettleKillDropOnce_TracksTargetId()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);
            step.UseTelemetry(telemetry.Scope("monster"));
            step.BindDropSink(new EncounterDropTests.RecordingDropSink());
            Kill(step.Taming.GetTarget(XunyeId));

            step.SettleKillDropOnce();

            string line = LineFor("monster/kill_drop_settled");
            Assert.That(line, Is.Not.Null);
            Assert.That(line, Does.Contain("\"target\":\"" + XunyeId + "\""));
            Assert.That(line, Does.Contain("\"yao\":" + XunyeYaoId));
        }

        // ──────────────────────── ④ 胜负只认主怪 ────────────────────────

        /// <summary>
        /// 一仗进行中，附加的完整参与者被打死：它掉落，但整场不结算（PendingResult 仍为 None、没有战斗结果）；
        /// 之后主怪死亡照样判 Victory。
        /// </summary>
        [Test]
        public void Step_FullParticipantDies_DoesNotSettleEncounter_PrimaryDeathStillDoes()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);
            var drops = new EncounterDropTests.RecordingDropSink();
            step.BindDropSink(drops);
            step.StartBattle(1, 1);

            Kill(step.Taming.GetTarget(XunyeId));
            Tick(step, 0.1f);

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.None), "附加怪死亡不结算整场");
            Assert.That(step.BattleSettlement.HasValue, Is.False);
            Assert.That(drops.Drops, Has.Count.EqualTo(1), "但它自己的掉落照结");

            Kill(step.Taming.GetTarget(PrimaryId));
            Tick(step, 0.1f);

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Victory));
            Assert.That(step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Victory));
        }

        /// <summary>
        /// 背后处决附加的完整参与者：它死、事实照写，但**不承载**战斗结果；下一 tick 按 id 掉它自己的一次。
        /// 之后处决主怪才承载 Victory。
        /// </summary>
        [Test]
        public void Execute_FullParticipant_DiesWithoutSettling_PrimaryExecutionSettles()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);
            var drops = new EncounterDropTests.RecordingDropSink();
            step.BindDropSink(drops);
            var resolver = new ExecutionResolver(new AssassinationRules(new AssassinationSettings(120f, 1.2f, false)),
                step.FactSink, step);
            MonsterRules xunye = step.Taming.GetTarget(XunyeId);
            MonsterRules primary = step.Taming.GetTarget(PrimaryId);

            Assert.That(resolver.TryExecute(Behind(xunye), xunye).Allowed, Is.True);
            Assert.That(xunye.Model.Health, Is.Zero);
            Assert.That(step.BattleSettlement.HasValue, Is.False, "处决附加怪不结算整场");
            Tick(step, 0.1f);
            Assert.That(drops.Drops, Has.Count.EqualTo(1));
            Assert.That(drops.Drops[0].Yao, Is.EqualTo(XunyeYaoId));

            Assert.That(resolver.TryExecute(Behind(primary), primary).Allowed, Is.True);
            Assert.That(step.BattleSettlement.HasValue, Is.True, "处决主怪照旧承载 Victory");
            Assert.That(step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Victory));
        }

        /// <summary>
        /// 巡夜人按**真实表数据**过物种门槛（上一条的 <see cref="Behind"/> 把物种条件写死成「允许」，测的是结算口径，
        /// 这一条补它绕开的那一截）：门槛输入照 <c>ExecutionInteractor.BuildInput</c> 的写法从种类的 defeat_method 算，
        /// 种类 1001 → 妖 1 → 「暗杀」→ 可处决。出处：2026-10-15 用户拍板巡夜人 1001「只能暗杀」
        /// （PRP/combat-iteration W6a 补修，替策划定的内容；yao/1.json killable = false + 暗杀，与 yao.xml 的定义一致）。
        /// 负对照：井边妇人（1002 → 妖 2 → 可击杀（方式没写））照样被物种门槛拒，且一滴血不掉。
        /// </summary>
        [Test]
        public void SpeciesGate_RealTable_XunyeIsExecutable_JingbianIsNot()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);
            var resolver = new ExecutionResolver(new AssassinationRules(new AssassinationSettings(120f, 1.2f, false)),
                step.FactSink, step);
            MonsterRules xunye = step.Taming.GetTarget(XunyeId);
            MonsterRules jingbian = step.Taming.GetTarget(JingbianId);
            Assert.That(xunye.Kind, Is.Not.Null, "前提：巡夜人从真表查到了种类");
            Assert.That(xunye.Kind.KindId, Is.EqualTo(XunyeKindId));
            Assert.That(xunye.Kind.YaoId, Is.EqualTo(XunyeYaoId));
            Assert.That(jingbian.Kind, Is.Not.Null, "前提：井边妇人从真表查到了种类");

            Assert.That(ExecutionRules.SpeciesExecutable(xunye.Kind.DefeatMethod), Is.True, "妖 1 的 defeat_method 是「暗杀」");
            Assert.That(xunye.IsKillable, Is.False, "只能暗杀：常规击杀那条路走不通（killable = false）");
            Assert.That(ExecutionRules.SpeciesExecutable(jingbian.Kind.DefeatMethod), Is.False);

            ExecutionVerdict rejected = resolver.TryExecute(BehindWithTableGate(jingbian), jingbian);
            Assert.That(rejected.Reject, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));
            Assert.That(jingbian.Model.Health, Is.GreaterThan(0), "被物种门槛拒：一滴血不掉");

            Assert.That(resolver.TryExecute(BehindWithTableGate(xunye), xunye).Allowed, Is.True, "真表数据下这一刀能下");
            Assert.That(xunye.Model.Health, Is.Zero);
        }

        // ──────────────────────── 查询口 ────────────────────────

        /// <summary>完整参与者查询：主怪恒真、勾了的附加怪真、默认的附加怪假、不认识的 id 假；收集结果主怪在首位。</summary>
        [Test]
        public void FullParticipantQueries_PrimaryAlwaysIncluded_DefaultAdditionalExcluded()
        {
            EncounterStep step = NewStep(withOptionalArrays: true);

            Assert.That(step.PrimaryTargetId, Is.EqualTo(PrimaryId));
            Assert.That(step.IsPrimaryTarget(step.Taming.GetTarget(PrimaryId)), Is.True);
            Assert.That(step.IsPrimaryTarget(step.Taming.GetTarget(XunyeId)), Is.False);
            Assert.That(step.IsFullParticipant(PrimaryId), Is.True);
            Assert.That(step.IsFullParticipant(DefaultAdditionalId), Is.False);
            Assert.That(step.IsFullParticipant(XunyeId), Is.True);
            Assert.That(step.IsFullParticipant("unknown"), Is.False);
            Assert.That(step.CollectFullParticipants(), Is.EqualTo(new[]
            {
                step.Taming.GetTarget(PrimaryId), step.Taming.GetTarget(XunyeId), step.Taming.GetTarget(JingbianId),
            }));
        }

        /// <summary>主怪那一位的两个值被忽略：即便传 false / 别的种类，主怪仍是完整参与者、种类仍由注入方定。</summary>
        [Test]
        public void ConfigureTaming_PrimarySlotValuesAreIgnored()
        {
            EncounterStep step = NewStep(withOptionalArrays: true, primaryKindIdSlot: XunyeKindId, primaryFullSlot: false);

            Assert.That(step.IsFullParticipant(PrimaryId), Is.True);
            Assert.That(step.Taming.GetTarget(PrimaryId).Kind.KindId, Is.EqualTo(ShilingKindId));
        }

        /// <summary>没 ConfigureTaming 的旧白盒：只有主怪一只，它就是唯一的完整参与者。</summary>
        [Test]
        public void WithoutConfigureTaming_PrimaryIsTheOnlyFullParticipant()
        {
            MonsterRules primary = NewPrimary();
            var step = new EncounterStep(new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance), primary);
            step.Begin(FarSpawn, PrimaryRoute);

            Assert.That(step.CollectFullParticipants(), Is.EqualTo(new[] { primary }));
            Assert.That(step.IsFullParticipant(step.PrimaryTargetId), Is.True);
        }

        /// <summary>可选数组长度与身份数量不一致、或种类 id 为负：当场抛，不静默截断。</summary>
        [Test]
        public void ConfigureTaming_InvalidOptionalArrays_Throws()
        {
            var step = new EncounterStep(new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance), NewPrimary());
            Vector2[][] routes = { PrimaryRoute, XunyeRoute };
            string[] ids = { PrimaryId, XunyeId };
            string[] names = { "甲", "巡夜人" };

            Assert.Throws<System.ArgumentException>(() =>
                step.ConfigureTaming(PlayerId, "玩家", ids, names, routes, new[] { 0 }, null));
            Assert.Throws<System.ArgumentException>(() =>
                step.ConfigureTaming(PlayerId, "玩家", ids, names, routes, null, new[] { true, true, true }));
            Assert.Throws<System.ArgumentException>(() =>
                step.ConfigureTaming(PlayerId, "玩家", ids, names, routes, new[] { 0, -1 }, null));
        }

        // ──────────────────────── 夹具 ────────────────────────

        private MonsterRules NewPrimary() =>
            new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(7ul), NullTelemetryScope.Instance,
                kind: null, kindCatalog: catalog, kindId: ShilingKindId);

        /// <summary>
        /// 造一场四只怪的遭遇并 Begin（种类在 Begin 的 Reset 里查）。
        /// <paramref name="withOptionalArrays"/> 为假时按旧签名调 ConfigureTaming（不传两个数组）——巡逻者乙回归的对照组。
        /// </summary>
        private EncounterStep NewStep(bool withOptionalArrays, int xunyeKindId = XunyeKindId,
            int primaryKindIdSlot = 0, bool primaryFullSlot = false)
        {
            var player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            var step = new EncounterStep(player, NewPrimary());
            Vector2[][] routes = { PrimaryRoute, DefaultAdditionalRoute, XunyeRoute, JingbianRoute };
            if (withOptionalArrays)
            {
                step.ConfigureTaming(PlayerId, "玩家", Ids, Names, routes,
                    new[] { primaryKindIdSlot, 0, xunyeKindId, JingbianKindId },
                    new[] { primaryFullSlot, false, true, true });
            }
            else
            {
                step.ConfigureTaming(PlayerId, "玩家", Ids, Names, routes);
            }

            step.Begin(FarSpawn, PrimaryRoute);
            return step;
        }

        /// <summary>让一只怪死亡：走显式处决入口（市令 killable=false，常规伤害打不死它；处决对任何种类都成立）。</summary>
        private static void Kill(MonsterRules target)
        {
            Assert.That(target.ApplyExecution(), Is.True, "前提：这只怪原本活着");
        }

        /// <summary>攻方贴在目标正后方 0.5 米、潜行、目标未察觉；物种条件按「允许」给（本文件测结算口径，不测门槛）。</summary>
        private static ExecutionInput Behind(MonsterRules target)
        {
            MonsterModel model = target.Model;
            Vector2 attacker = model.Position - model.Facing * 0.5f;
            var snapshot = new AssassinationInput(attacker, model.Facing, model.Position, model.Facing,
                model.Health > 0, false, true);
            return new ExecutionInput(in snapshot, true);
        }

        /// <summary>同 <see cref="Behind"/> 的站位，但物种条件按真表算（与 <c>ExecutionInteractor.BuildInput</c> 同一写法）。</summary>
        private static ExecutionInput BehindWithTableGate(MonsterRules target)
        {
            MonsterModel model = target.Model;
            Vector2 attacker = model.Position - model.Facing * 0.5f;
            var snapshot = new AssassinationInput(attacker, model.Facing, model.Position, model.Facing,
                model.Health > 0, false, true);
            return new ExecutionInput(in snapshot, ExecutionRules.SpeciesExecutable(target.Kind == null ? null : target.Kind.DefeatMethod));
        }

        private static void Tick(EncounterStep step, float deltaTime)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, 0u, Vector2.zero, 0);
            var context = new SimulationContext(0, deltaTime, in command, new RandomService(7ul));
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
    }
}
