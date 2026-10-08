// 职责：钉住交互层那一层——「按 F 之后选谁、拿什么门槛、什么时候什么都不做」。
// 为什么新建：`ExecutionResolverTests` 从判定输入直接进入结算，跳过了「从 MonsterRules / PlayerModel 采快照」
//   这一段；条件 ② 的取值（物种 defat_method）与条件 ① 的察觉口径都只在交互层里组装，
//   而真实表里两行怪都是 `可击杀（方式没写）`——本文件用**真表数据**钉住「它们绝不能被处决」。
// 负对照：没有目标 / 目标在正面 / 超出距离 / 目标已察觉 / 物种不可处决 —— 五条都必须什么都没发生。
// F 键归属（PRP/interaction D6）：统一交互焦点非空时按键让位（不处决、记 interaction_focus），焦点为空 / 没接焦点时照旧。
using Game.Core.Config;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Interaction;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Stealth;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Interaction;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Stealth
{
    public sealed class ExecutionInteractorTests
    {
        private MonsterConfig monsterConfig;
        private PlayerConfig playerConfig;
        private GameObject host;
        private ExecutionInteractor interactor;
        private EncounterFactLog facts;
        private AssassinationRules assassination;

        [SetUp]
        public void SetUp()
        {
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            host = new GameObject("ExecutionInteractorTests");
            interactor = host.AddComponent<ExecutionInteractor>();
            facts = new EncounterFactLog();
            assassination = new AssassinationRules(new AssassinationSettings(120f, 1.2f, false));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(monsterConfig);
            Object.DestroyImmediate(playerConfig);
        }

        /// <summary>负对照（主窗口点名要的那条）：**没有目标时按 F 什么都不发生**，只留一条 no_target 埋点。</summary>
        [Test]
        public void TryExecute_NoTargets_ReportsNoTargetAndChangesNothing()
        {
            PlayerRules player = NewPlayer(new Vector2(-1f, 0f));
            interactor.Configure(player.Model, System.Array.Empty<MonsterRules>(), facts, assassination);

            ExecutionVerdict verdict = interactor.TryExecute();

            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.NoTarget));
            Assert.That(facts.Count, Is.Zero, "没有目标就不该写任何事实");
            Assert.That(player.Model.Health, Is.GreaterThan(0));
        }

        /// <summary>
        /// PRP §3.2（真表数据）：当前表里的怪（`killable = true` + `defeat_method = 可击杀（方式没写）`）
        /// 站在背后也**不能**被处决——否则绕背变成万能解。
        /// </summary>
        [Test]
        public void TryExecute_RealTableMonsterRightBehind_IsRejectedByTheSpeciesGate()
        {
            MonsterRules target = NewRealTableTarget();
            PlayerRules player = NewPlayer(new Vector2(-1f, 0f));
            interactor.Configure(player.Model, target, facts, assassination);

            Assert.That(target.Kind.DefeatMethod, Is.EqualTo("可击杀（方式没写）"), "真表里两行都是这个值");
            Assert.That(target.IsKillable, Is.True, "它是能被常规打死的——只是不该被一键处决");
            Assert.That(interactor.TryExecute().Reject, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));

            Assert.That(target.Model.Health, Is.GreaterThan(0), "一滴血都不该掉");
            Assert.That(target.Model.Mode, Is.Not.EqualTo(MonsterMode.Dead));
            Assert.That(facts.IsTrue(StealthFactKeys.Assassinated), Is.False,
                "被拒不能让 `stealth.assassinated` 为真（能力 ≠ 事实，PRP §2.5）");
        }

        /// <summary>负对照：站在目标正面（不在背后锥内）—— 位置条件先报，轮不到物种条件。</summary>
        [Test]
        public void TryExecute_StandingInFront_IsRejectedAsNotBehind()
        {
            MonsterRules target = NewRealTableTarget();
            PlayerRules player = NewPlayer(new Vector2(1f, 0f));
            interactor.Configure(player.Model, target, facts, assassination);

            ExecutionVerdict verdict = interactor.TryExecute();

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.NotBehind));
            Assert.That(target.Model.Health, Is.GreaterThan(0));
            Assert.That(facts.Count, Is.Zero);
        }

        /// <summary>负对照：方向对（正后方）但超出暗杀距离 1.2。</summary>
        [Test]
        public void TryExecute_BehindButTooFar_IsRejectedAsOutOfRange()
        {
            MonsterRules target = NewRealTableTarget();
            PlayerRules player = NewPlayer(new Vector2(-5f, 0f));
            interactor.Configure(player.Model, target, facts, assassination);

            ExecutionVerdict verdict = interactor.TryExecute();

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.OutOfRange));
            Assert.That(target.Model.Health, Is.GreaterThan(0));
        }

        /// <summary>负对照：目标已经察觉（警戒）—— 察觉先于几何被检查。</summary>
        [Test]
        public void TryExecute_AwareTarget_IsRejectedAsTargetAware()
        {
            MonsterRules target = NewRealTableTarget();
            PlayerRules player = NewPlayer(new Vector2(4f, 0f));
            // 让怪看见玩家：站在它正前方 4 米（橙区 6 米内）推进一个零步长的 tick → 进入警戒。
            var intent = new MonsterIntent(player.Model.Snapshot, 0f);
            target.Step(in intent);
            Assert.That(target.Model.Mode, Is.EqualTo(MonsterMode.Alert), "这条用例的前提：目标已察觉");
            // 再把玩家挪到它背后（潜行不潜行都不影响「已察觉」这一条）。
            player.Reset(new Vector2(-1f, 0f));
            interactor.Configure(player.Model, target, facts, assassination);

            ExecutionVerdict verdict = interactor.TryExecute();

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.TargetAware));
            Assert.That(target.Model.Health, Is.GreaterThan(0));
            Assert.That(facts.IsTrue(StealthFactKeys.Assassinated), Is.False);
        }

        /// <summary>
        /// 多个候选时：**拒绝原因取离玩家最近的那一只**（玩家最想知道的是最近这只为什么不行）。
        /// 近的那只：玩家站在它正面（not_behind）；远的那只：玩家在它背后 5 米（out_of_range）。
        /// </summary>
        [Test]
        public void TryExecute_MultipleTargets_ReportsTheNearestOneReason()
        {
            PlayerRules player = NewPlayer(Vector2.zero);
            // 近的那只朝 +X：玩家在它**正面**（not_behind）；远的那只朝 -Y：玩家在它背后 5 米（out_of_range）。
            MonsterRules near = NewTarget(new[] { new Vector2(-1f, 0f), new Vector2(9f, 0f) });
            MonsterRules far = NewTarget(new[] { new Vector2(0f, -5f), new Vector2(0f, -15f) });
            interactor.Configure(player.Model, new[] { far, near }, facts, assassination);

            ExecutionVerdict verdict = interactor.TryExecute();

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.NotBehind), "报的是最近那只（近处 1 米）的原因");
            Assert.That(near.Model.Health, Is.GreaterThan(0));
            Assert.That(far.Model.Health, Is.GreaterThan(0));
        }

        [Test]
        public void SetTargets_ReplacesCandidates_AndEmptyStopsResponding()
        {
            MonsterRules target = NewRealTableTarget();
            PlayerRules player = NewPlayer(new Vector2(-1f, 0f));
            interactor.Configure(player.Model, target, facts, assassination);
            Assert.That(interactor.TryExecute().Reject, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));

            interactor.SetTargets(System.Array.Empty<MonsterRules>());
            Assert.That(interactor.TryExecute().Reject, Is.EqualTo(ExecutionReject.NoTarget));

            interactor.SetTargets(new[] { target });
            Assert.That(interactor.TryExecute().Reject, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));
        }

        /// <summary>负对照：没接线时按 F 静默返回，不写事实也不埋点（埋点作用域也是接线的一部分）。</summary>
        [Test]
        public void TryExecute_NotConfigured_ReturnsNoTargetWithoutSideEffects()
        {
            Assert.That(interactor.IsConfigured, Is.False);
            Assert.That(interactor.TryExecute().Reject, Is.EqualTo(ExecutionReject.NoTarget));
            Assert.That(facts.Count, Is.Zero);
        }

        /// <summary>
        /// 只读查询 <see cref="ExecutionInteractor.Inspect"/>（接线那一波新增，白盒面板与回放靠它看状态）：
        /// 它能报出「此刻能不能下刀」，且**什么都不改**——不写事实、不杀怪、不记最近结果、不承载战斗结果。
        /// 「能不能下刀」与「已经杀过」仍然是两件事（PRP §2.5）。
        /// </summary>
        [Test]
        public void Inspect_ExecutableTargetBehind_IsAllowedAndChangesNothing()
        {
            PlayerRules player = NewPlayer(new Vector2(-1f, 0f));
            MonsterRules target = NewExecutableTableTarget();
            interactor.Configure(player.Model, target, facts, assassination);

            ExecutionHint hint = interactor.Inspect();

            Assert.That(hint.Configured, Is.True);
            Assert.That(hint.HasTarget, Is.True);
            Assert.That(hint.Allowed, Is.True, "背后 1 米、未察觉、物种是「暗杀」——这一刀此刻能下");
            Assert.That(hint.Reject, Is.EqualTo(ExecutionReject.None));
            Assert.That(hint.ToLabel(), Does.Contain("可处决"), "面板上要能看出「可以按 F」");
            Assert.That(interactor.LastVerdict.HasValue, Is.False, "查询不是一次尝试，不该留下最近结果");
            Assert.That(target.Model.Health, Is.GreaterThan(0), "查询不杀人");
            Assert.That(facts.Count, Is.Zero, "查询不写事实");
        }

        /// <summary>
        /// <see cref="ExecutionInteractor.LastVerdict"/> 如实记下**按了之后**的结果（含拒绝）：
        /// 「按 F 没反应」与「功能坏了」在现场靠它分开。查询与按键必须给同一个原因，否则面板会骗人。
        /// </summary>
        [Test]
        public void TryExecute_RejectedPress_RecordsTheSameReasonTheQueryShows()
        {
            PlayerRules player = NewPlayer(new Vector2(-1f, 0f));
            MonsterRules target = NewTarget(); // 没接种类表 → 物种门槛拒（「对于部分怪物」那条限定）
            interactor.Configure(player.Model, target, facts, assassination);

            ExecutionReject shown = interactor.Inspect().Reject;
            Assert.That(shown, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));
            Assert.That(interactor.LastVerdict.HasValue, Is.False, "还没按过");

            ExecutionVerdict verdict = interactor.TryExecute();

            Assert.That(verdict.Reject, Is.EqualTo(shown), "面板说的原因与按下去得到的原因必须是同一条");
            Assert.That(interactor.LastVerdict.HasValue, Is.True);
            Assert.That(interactor.LastVerdict.Value.Reject, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));
            Assert.That(interactor.LastVerdict.Value.Allowed, Is.False);
        }

        // ──────────────────────── F 键归属（PRP/interaction D6） ────────────────────────

        /// <summary>
        /// 屏幕上有交互提示（统一焦点非空）时按 F：这一下归交互，处决让位——即便背后这一刀本来能下。
        /// 不杀人、不写事实，只记一条拒绝（reason = interaction_focus），面板（Inspect）与按键给同一个原因。
        /// </summary>
        [Test]
        public void HandleExecuteKey_InteractionFocusPresent_YieldsAndDoesNotExecute()
        {
            var sink = new RecordingTelemetrySink();
            using var telemetryService = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
            PlayerRules player = NewPlayer(new Vector2(-1f, 0f));
            MonsterRules target = NewExecutableTableTarget();
            var focus = new FakeFocus { Current = new FakeInteractable(Vector3.zero, 2f) };
            interactor.Configure(player.Model, target, facts, assassination, null, telemetryService.Scope("stealth"),
                null, focus);

            ExecutionHint hint = interactor.Inspect();
            ExecutionVerdict verdict = interactor.HandleExecuteKey();

            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.YieldedToInteraction));
            Assert.That(hint.Reject, Is.EqualTo(ExecutionReject.YieldedToInteraction), "面板与按键同一条归属规则");
            Assert.That(interactor.LastVerdict.HasValue && interactor.LastVerdict.Value.Reject == ExecutionReject.YieldedToInteraction,
                Is.True, "按了、被让位：最近结果如实记下");
            Assert.That(target.Model.Health, Is.GreaterThan(0), "让位不杀人");
            Assert.That(facts.Count, Is.Zero, "让位不写事实");
            string line = FindLine(sink, "stealth/stealth_execute_rejected");
            Assert.That(line, Is.Not.Null, "让位要留一条拒绝埋点，现场才分得清是让位还是判定不过");
            Assert.That(line, Does.Contain("\"reason\":\"" + ExecutionRules.ReasonYieldedToInteraction + "\""));
        }

        /// <summary>负对照：焦点为空（屏幕上没有交互提示）时按 F 照常处决，D6 不影响这条路。</summary>
        [Test]
        public void HandleExecuteKey_FocusEmpty_ExecutesAsBefore()
        {
            PlayerRules player = NewPlayer(new Vector2(-1f, 0f));
            MonsterRules target = NewExecutableTableTarget();
            interactor.Configure(player.Model, target, facts, assassination, null, null, null, new FakeFocus());

            ExecutionVerdict verdict = interactor.HandleExecuteKey();

            Assert.That(verdict.Allowed, Is.True);
            Assert.That(target.Model.Health, Is.Zero, "焦点为空：这一刀照常下");
        }

        /// <summary>没接焦点（独立原型场景、旧接线）时不做这层裁决，与 TryExecute 完全相同。</summary>
        [Test]
        public void HandleExecuteKey_NoFocusWired_BehavesLikeTryExecute()
        {
            PlayerRules player = NewPlayer(new Vector2(-1f, 0f));
            MonsterRules target = NewExecutableTableTarget();
            interactor.Configure(player.Model, target, facts, assassination);

            Assert.That(interactor.HandleExecuteKey().Allowed, Is.True);
            Assert.That(target.Model.Health, Is.Zero);
        }

        [Test]
        public void Configure_NullPlayerOrSink_IsRejected()
        {
            // 负对照：接线参数漏了要当场炸，不许静默变成一个「按了没反应」的组件。
            Assert.Throws<System.ArgumentNullException>(
                () => interactor.Configure(null, NewTarget(), facts, assassination));
            Assert.Throws<System.ArgumentNullException>(
                () => interactor.Configure(NewPlayer(Vector2.zero).Model, NewTarget(), null, assassination));
        }

        // ──────────────────────── 辅助 ────────────────────────

        /// <summary>玩家站在指定位置（<c>PlayerModel.Position</c> 是 internal set，只能经 PlayerRules.Reset 摆）。</summary>
        private PlayerRules NewPlayer(Vector2 position)
        {
            var player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            player.Reset(position);
            return player;
        }

        /// <summary>没接种类表的怪：IsKillable = true（旧行为），站在原点朝 +X。</summary>
        private MonsterRules NewTarget() => NewTarget(new[] { Vector2.zero, new Vector2(10f, 0f) });

        private MonsterRules NewTarget(Vector2[] waypoints)
        {
            var target = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul),
                NullTelemetryScope.Instance);
            target.Reset(waypoints);
            return target;
        }

        /// <summary>用**真实生成的表**造一只怪（走「表 → 目录 → 规则」这条真链路）。</summary>
        private MonsterRules NewRealTableTarget()
        {
            var stub = new StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            var kinds = new MonsterKindCatalog(stub, new YaoCatalog(stub), monsterConfig.HostileRadius);
            var target = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul),
                NullTelemetryScope.Instance, kinds.Get(1001));
            target.Reset(new[] { Vector2.zero, new Vector2(10f, 0f) });
            return target;
        }

        /// <summary>
        /// 真表里**只能靠暗杀解决**的那只：`Tables/Data/yao/3.json`（市令，killable = false + defeat_method = 暗杀）
        /// 与 `monster_species/1003.json`。用它才能覆盖交互层的成功路径（表里第一条不是暗杀，会被物种门槛拒）。
        /// </summary>
        private MonsterRules NewExecutableTableTarget()
        {
            var stub = new StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            var kinds = new MonsterKindCatalog(stub, new YaoCatalog(stub), monsterConfig.HostileRadius);
            var target = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul),
                NullTelemetryScope.Instance, kinds.Get(1003));
            target.Reset(new[] { Vector2.zero, new Vector2(10f, 0f) });
            return target;
        }

        private static string FindLine(RecordingTelemetrySink sink, string eventKey)
        {
            for (int i = 0; i < sink.Lines.Count; i++)
            {
                if (sink.Lines[i].Contains(eventKey)) return sink.Lines[i];
            }

            return null;
        }

        /// <summary>只读焦点替身：Current 可设，事件不触发（处决只读 Current）。</summary>
        private sealed class FakeFocus : IInteractionFocus
        {
            public IInteractable Current { get; set; }

            public event System.Action<IInteractable> OnFocusChanged
            {
                add { }
                remove { }
            }

            public event System.Action<IInteractable> OnInteracted
            {
                add { }
                remove { }
            }
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>（同 MonsterRulesTests 的写法）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;

            public global::cfg.Tables Tables { get; }

            public ulong ContentHash => throw new System.NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
