// 职责：钉住「一次通过的处决」结算出来的三件事（PRP §2.3）与三处埋点（§2.6）：
//   ① `stealth.assassinated` **只在真的执行成功之后**写（§2.5：能力 ≠ 事实）；② 目标死亡；
//   ③ 战斗结果承载成 Victory（只承载，不推进剧情）。
// 为什么新建：`ExecutionRulesTests` 只覆盖「能不能」；「能不能」到「真的做了」之间隔着写事实 / 杀人 / 结算三件事，
//   这一层此前没有任何测试，而它正是最容易做错的落点（拿 AssassinationAllowed 去点亮持久事实就是在这里出的事）。
// 负对照：不在背后 / 目标已察觉 / 物种不可处决 / 没有目标 / 不在遭遇里 —— 五条都必须**什么都没发生**。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Narrative;
using Game.Player;
using Game.Stealth;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Stealth
{
    public sealed class ExecutionResolverTests
    {
        private MonsterConfig monsterConfig;
        private PlayerConfig playerConfig;
        private AssassinationRules assassination;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            assassination = new AssassinationRules(new AssassinationSettings(120f, 1.2f, false));
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
        }

        [TearDown]
        public void TearDown()
        {
            telemetry.Dispose();
            Object.DestroyImmediate(monsterConfig);
            Object.DestroyImmediate(playerConfig);
        }

        // ──────────────────────── 成功：三件事都做到 ────────────────────────

        /// <summary>
        /// PRP §3.3：`defeat_method = 暗杀` 的怪可以被处决并死亡，**即便 `killable = false`**。
        /// 这里用「没接妖物表」的 <see cref="MonsterKind"/> 造出 `killable = false` 这一支
        /// （<c>MonsterKind.IsKillable</c> 查不到表时按 false），条件 ② 由调用方给的布尔表示。
        /// </summary>
        [Test]
        public void TryExecute_ExecutableSpecies_KillsWritesFactAndSettlesVictory()
        {
            MonsterRules target = NewTarget(new MonsterKind(9001, 7, "测试用暗杀怪",
                3, 1, 0.8f, 1f, 75f, 6f, 1.5f, 4f, 2f, yaoCatalog: null));
            EncounterStep encounter = NewEncounter(target);
            var resolver = new ExecutionResolver(assassination, (IStealthFactSink)encounter.Facts, encounter,
                telemetry.Scope("stealth"));

            Assert.That(target.IsKillable, Is.False, "这条用例的前提：killable = false（只能靠处决）");
            Assert.That(encounter.Facts.IsTrue(StealthFactKeys.Assassinated), Is.False, "杀之前不许有事实");

            ExecutionVerdict verdict = resolver.TryExecute(Behind(target), target);

            Assert.That(verdict.Allowed, Is.True);
            // ② 目标死亡（走显式处决入口，不是 ApplyDamage）
            Assert.That(target.Model.Health, Is.Zero);
            Assert.That(target.Model.Mode, Is.EqualTo(MonsterMode.Dead));
            // ① 事实只在命中之后写
            Assert.That(encounter.Facts.IsTrue(StealthFactKeys.Assassinated), Is.True);
            // ③ 战斗结果承载为 Victory（BattleOutcome 的四种之一，不自创结果键）
            Assert.That(encounter.BattleSettlement.HasValue, Is.True, "以处决收束的遭遇要承载一个战斗结果");
            Assert.That(encounter.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Victory));
            // 埋点：允许 + 执行各一条
            Assert.That(LineFor("stealth/stealth_execute_allowed"), Is.Not.Null);
            string executed = LineFor("stealth/stealth_executed");
            Assert.That(executed, Is.Not.Null);
            Assert.That(executed, Does.Contain("\"killable\":false"), "证明这只怪本来打不死，是被处决掉的");
            Assert.That(executed, Does.Contain("\"settled\":true"));
        }

        /// <summary>同一帧之后再按一次（目标已死）：判定层拒掉，不重复写事实、不重复埋 executed。</summary>
        [Test]
        public void TryExecute_SecondCallOnDeadTarget_IsRejectedAndDoesNotTrackExecutedAgain()
        {
            MonsterRules target = NewTarget();
            EncounterStep encounter = NewEncounter(target);
            var resolver = new ExecutionResolver(assassination, (IStealthFactSink)encounter.Facts, encounter,
                telemetry.Scope("stealth"));
            Assert.That(resolver.TryExecute(Behind(target), target).Allowed, Is.True);

            sink.Clear();
            ExecutionVerdict second = resolver.TryExecute(Behind(target), target);

            Assert.That(second.Allowed, Is.False);
            Assert.That(second.Reject, Is.EqualTo(ExecutionReject.TargetNotAlive));
            Assert.That(LineFor("stealth/stealth_executed"), Is.Null, "第二次没有执行，就不该有第二条 executed");
            Assert.That(LineFor("stealth/stealth_execute_rejected"), Is.Not.Null);
        }

        // ──────────────────────── 拒绝：什么都没发生（能力 ≠ 事实） ────────────────────────

        /// <summary>负对照：站在正面 → 不写事实、不掉血、不承载战斗结果，但现场留一条带原因的埋点。</summary>
        [Test]
        public void TryExecute_NotBehind_KillsNothingAndLeavesFactFalse()
        {
            MonsterRules target = NewTarget();
            EncounterStep encounter = NewEncounter(target);
            var resolver = new ExecutionResolver(assassination, (IStealthFactSink)encounter.Facts, encounter,
                telemetry.Scope("stealth"));
            int healthBefore = target.Model.Health;

            ExecutionVerdict verdict = resolver.TryExecute(
                new ExecutionInput(Input(target, new Vector2(1f, 0f), aware: false), true), target);

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.NotBehind));
            Assert.That(target.Model.Health, Is.EqualTo(healthBefore));
            Assert.That(encounter.Facts.IsTrue(StealthFactKeys.Assassinated), Is.False,
                "「能不能下刀」被拒，绝不能让持久事实为真（PRP §2.5）");
            Assert.That(encounter.BattleSettlement.HasValue, Is.False);
            Assert.That(LineFor("stealth/stealth_execute_rejected"), Does.Contain("\"reason\":\"not_behind\""));
        }

        /// <summary>负对照：目标已经察觉。</summary>
        [Test]
        public void TryExecute_TargetAware_KillsNothingAndLeavesFactFalse()
        {
            MonsterRules target = NewTarget();
            var facts = new EncounterFactLog();
            var resolver = new ExecutionResolver(assassination, facts, null, telemetry.Scope("stealth"));

            ExecutionVerdict verdict = resolver.TryExecute(
                new ExecutionInput(Input(target, new Vector2(-1f, 0f), aware: true), true), target);

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.TargetAware));
            Assert.That(target.Model.Health, Is.GreaterThan(0));
            Assert.That(facts.IsTrue(StealthFactKeys.Assassinated), Is.False);
            Assert.That(LineFor("stealth/stealth_execute_rejected"), Does.Contain("\"reason\":\"target_aware\""));
        }

        /// <summary>
        /// 负对照（PRP §3.2 的「绕背不许变万能解」）：**`killable = true` 的怪也不能一键处决**——
        /// 只要条件 ② 不成立（`defeat_method != 暗杀`），背后站得再正也只留一条拒绝埋点。
        /// </summary>
        [Test]
        public void TryExecute_KillableButSpeciesNotExecutable_KillsNothing()
        {
            MonsterRules killable = NewTarget(); // 没接种类表 → IsKillable = true（旧行为）
            var facts = new EncounterFactLog();
            var resolver = new ExecutionResolver(assassination, facts, null, telemetry.Scope("stealth"));

            Assert.That(killable.IsKillable, Is.True);
            ExecutionVerdict verdict = resolver.TryExecute(Behind(killable, speciesExecutable: false), killable);

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));
            Assert.That(killable.Model.Health, Is.EqualTo(monsterConfig.MaxHealth), "一滴血都不该掉");
            Assert.That(facts.IsTrue(StealthFactKeys.Assassinated), Is.False);
            Assert.That(LineFor("stealth/stealth_execute_rejected"),
                Does.Contain("\"reason\":\"" + ExecutionRules.ReasonSpeciesNotExecutable + "\""));
        }

        /// <summary>负对照：一个候选都没有（对着空气按 F）——也要留点，否则「没反应」和「功能坏了」分不开。</summary>
        [Test]
        public void TryExecute_NullTarget_ReportsNoTargetAndTouchesNothing()
        {
            var facts = new EncounterFactLog();
            var resolver = new ExecutionResolver(assassination, facts, null, telemetry.Scope("stealth"));

            ExecutionVerdict verdict = resolver.TryExecute(default, null);

            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.NoTarget));
            Assert.That(facts.IsTrue(StealthFactKeys.Assassinated), Is.False);
            Assert.That(LineFor("stealth/stealth_execute_rejected"), Does.Contain("\"reason\":\"no_target\""));
        }

        [Test]
        public void RejectNoTarget_ReportsNoTargetWithoutAnyStateChange()
        {
            var facts = new EncounterFactLog();
            var resolver = new ExecutionResolver(assassination, facts, null, telemetry.Scope("stealth"));

            ExecutionVerdict verdict = resolver.RejectNoTarget();

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.NoTarget));
            Assert.That(LineFor("stealth/stealth_execute_rejected"), Does.Contain("\"reason\":\"no_target\""));
            Assert.That(LineFor("stealth/stealth_execute_allowed"), Is.Null, "没目标就没有「允许」");
            Assert.That(LineFor("stealth/stealth_executed"), Is.Null);
        }

        /// <summary>负对照：不在遭遇里（没 Begin / 已 End）时不承载战斗结果——那时没有「这场遭遇」可言。</summary>
        [Test]
        public void TryExecute_WhenEncounterNotActive_LeavesBattleSettlementNull()
        {
            MonsterRules target = NewTarget();
            EncounterStep encounter = NewEncounter(target);
            encounter.End();
            var resolver = new ExecutionResolver(assassination, (IStealthFactSink)encounter.Facts, encounter,
                telemetry.Scope("stealth"));

            Assert.That(resolver.TryExecute(Behind(target), target).Allowed, Is.True);

            Assert.That(encounter.BattleSettlement.HasValue, Is.False, "不在遭遇里就不承载结果");
            Assert.That(LineFor("stealth/stealth_executed"), Does.Contain("\"settled\":false"));
        }

        // ──────────────────────── 表现钩子（06:63，本波只留接口） ────────────────────────

        [Test]
        public void OnExecuted_DefaultEmpty_DoesNothing_AndFiresOnceWhenSubscribed()
        {
            MonsterRules target = NewTarget();
            var facts = new EncounterFactLog();
            var resolver = new ExecutionResolver(assassination, facts, null, telemetry.Scope("stealth"));

            // 默认（没人订阅）时行为与不接表现完全一致：不抛、不拦。
            Assert.DoesNotThrow(() => resolver.TryExecute(Behind(target), target));
            Assert.That(target.Model.Health, Is.Zero);

            MonsterRules second = NewTarget();
            int calls = 0;
            ExecutionPresentation payload = default;
            resolver.OnExecuted += presentation => { calls++; payload = presentation; };
            Assert.That(resolver.TryExecute(Behind(second), second).Allowed, Is.True);

            Assert.That(calls, Is.EqualTo(1), "命中一次只回调一次");
            Assert.That(payload.Position, Is.EqualTo(second.Model.Position));

            // 负对照：被拒时不回调（表现钩子只服务「真的处决了」）。
            MonsterRules third = NewTarget();
            Assert.That(resolver.TryExecute(
                new ExecutionInput(Input(third, new Vector2(1f, 0f), aware: false), true), third).Allowed, Is.False);
            Assert.That(calls, Is.EqualTo(1));
        }

        // ──────────────────────── 辅助 ────────────────────────

        /// <summary>造一只站在原点、朝 +X 的怪；<paramref name="kind"/> 为 null 时没接种类表（IsKillable = true）。</summary>
        private MonsterRules NewTarget(MonsterKind kind = null)
        {
            var target = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul),
                NullTelemetryScope.Instance, kind);
            target.Reset(new[] { Vector2.zero, new Vector2(10f, 0f) });
            return target;
        }

        /// <summary>造一场已经开始（IsActive）的遭遇：玩家在 (-1, 0)，怪在原点朝 +X。</summary>
        private EncounterStep NewEncounter(MonsterRules target)
        {
            var player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            var step = new EncounterStep(player, target);
            step.Begin(new Vector2(-1f, 0f), new[] { Vector2.zero, new Vector2(10f, 0f) });
            return step;
        }

        /// <summary>
        /// 攻方贴在目标正后方的判定输入。条件 ② 默认按「允许」给（这一层测的是结算，不是门槛）；
        /// 真链路里条件 ② 由 <c>ExecutionRules.SpeciesExecutable</c> 从妖物表算出来，见 ExecutionRulesTests。
        /// </summary>
        private static ExecutionInput Behind(MonsterRules target, bool speciesExecutable = true) =>
            new ExecutionInput(Input(target, new Vector2(-1f, 0f), aware: false), speciesExecutable);

        private static AssassinationInput Input(MonsterRules target, Vector2 attackerPosition, bool aware)
        {
            MonsterModel model = target.Model;
            return new AssassinationInput(attackerPosition, Vector2.right, model.Position, model.Facing,
                model.Health > 0, aware, true);
        }

        // 埋点按行记；一条事件一行，取含事件名的那一行做断言（会话头那一行不含事件名）。
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
