// 职责：钉住交互层那一层——「按 F 之后选谁、拿什么门槛、什么时候什么都不做」。
// 为什么新建：`ExecutionResolverTests` 从判定输入直接进入结算，跳过了「从 MonsterRules / PlayerModel 采快照」
//   这一段；条件 ② 的取值（物种 defat_method）与条件 ① 的察觉口径都只在交互层里组装，
//   而真实表里两行怪都是 `可击杀（方式没写）`——本文件用**真表数据**钉住「它们绝不能被处决」。
// 负对照：没有目标 / 目标在正面 / 超出距离 / 目标已察觉 / 物种不可处决 —— 五条都必须什么都没发生。
using Game.Core.Config;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Stealth;
using Game.Tests.EditMode.Core;
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

        /// <summary>只递一份现成的 <c>cfg.Tables</c>（同 MonsterRulesTests 的写法）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;

            public global::cfg.Tables Tables { get; }

            public ulong ContentHash => throw new System.NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
