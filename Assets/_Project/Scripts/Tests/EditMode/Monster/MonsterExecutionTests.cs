// 职责：钉住 `MonsterRules` 的**显式处决入口**与 `ApplyDamage` 的分工（PRP §3.5）。
// 为什么新建：`MonsterRulesTests` 覆盖的是常规伤害链路；「处决能杀 `killable = false` 的怪」这条
//   看起来与「ApplyDamage 拒伤」矛盾，必须在测试里正面钉死，否则下一个人会来「修」掉其中一个。
// 负对照：被处决的目标已经死了再调一次要返回 false（不许重复算一次处决）。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class MonsterExecutionTests
    {
        /// <summary>与 Tables/Data/yao/1.json 无关的合成种类：`yaoCatalog = null` → 查不到表 → IsKillable = false。</summary>
        private const int ImmortalKindId = 1001;

        private MonsterConfig config;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<MonsterConfig>();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
        }

        [TearDown]
        public void TearDown()
        {
            telemetry.Dispose();
            Object.DestroyImmediate(config);
        }

        /// <summary>
        /// **本波最重要的一条断言**：同一只 `killable = false` 的怪，
        /// `ApplyDamage` 仍然拒伤（返回 false、不掉血），而 `ApplyExecution` 能杀死它。
        /// 两条并存是设计不是 bug——`defeat_method = 暗杀` 的怪只有处决这一条击杀途径
        /// （依据：`MonsterKind.cs` 的两列分工、`Tables/Defines/yao.xml` 的 defeat_method 列注释）。
        /// </summary>
        [Test]
        public void ApplyDamageStillRejects_WhileApplyExecutionKills_TheSameNotKillableMonster()
        {
            MonsterRules immortal = NewRules(NotKillableKind());
            var damage = new DamageIntent(1);
            PlayerSnapshot attacker = new PlayerSnapshot(new Vector2(-1f, 0f), Vector2.right, true, false, 3);

            Assert.That(immortal.IsKillable, Is.False, "这条用例的前提：常规伤害杀不掉它");
            Assert.That(immortal.ApplyDamage(in damage, in attacker), Is.False, "常规伤害被拒（不是「不可能死」）");
            Assert.That(immortal.Model.Health, Is.EqualTo(3), "被拒的伤害一滴血都不掉");
            Assert.That(immortal.Model.Mode, Is.Not.EqualTo(MonsterMode.Dead));

            Assert.That(immortal.ApplyExecution(), Is.True, "显式处决入口能杀掉它——绕背处决是它唯一的死法");
            Assert.That(immortal.Model.Health, Is.Zero);
            Assert.That(immortal.Model.Mode, Is.EqualTo(MonsterMode.Dead), "走的是既有死亡流程（Mode = Dead）");
        }

        /// <summary>负对照：已经死透的目标再调一次处决返回 false（不许重复写事实 / 重复埋点）。</summary>
        [Test]
        public void ApplyExecution_OnAlreadyDeadMonster_ReturnsFalse()
        {
            MonsterRules immortal = NewRules(NotKillableKind());
            Assert.That(immortal.ApplyExecution(), Is.True);
            sink.Clear();

            Assert.That(immortal.ApplyExecution(), Is.False);
            Assert.That(immortal.Model.Health, Is.Zero, "血量还停在 0，没有被再算一遍");
            Assert.That(sink.Count, Is.Zero, "第二次没有执行，就不该再埋一条 executed");
        }

        /// <summary>处决是「秒杀」不是「击退」：位置与朝向都不动（表现层据此播倒地动画）。</summary>
        [Test]
        public void ApplyExecution_LeavesPositionAndFacingUntouched()
        {
            MonsterRules immortal = NewRules(NotKillableKind());
            Vector2 position = immortal.Model.Position;
            Vector2 facing = immortal.Model.Facing;

            Assert.That(immortal.ApplyExecution(), Is.True);

            Assert.That(immortal.Model.Position, Is.EqualTo(position));
            Assert.That(immortal.Model.Facing, Is.EqualTo(facing));
        }

        /// <summary>能常规杀的怪也能被处决：处决入口本身**不判物种**（门槛归 Game.Stealth 的 ExecutionRules）。</summary>
        [Test]
        public void ApplyExecution_OnKillableMonster_AlsoWorks()
        {
            MonsterRules killable = NewRules();
            Assert.That(killable.IsKillable, Is.True, "没接种类表时保持旧行为");

            Assert.That(killable.ApplyExecution(), Is.True);
            Assert.That(killable.Model.Health, Is.Zero);
            Assert.That(killable.Model.Mode, Is.EqualTo(MonsterMode.Dead));
        }

        [Test]
        public void ApplyExecution_TracksExecutedTelemetry()
        {
            MonsterRules immortal = NewRules(NotKillableKind(), telemetry.Scope("monster"));

            Assert.That(immortal.ApplyExecution(), Is.True);

            Assert.That(sink.Last, Does.Contain("monster/executed"), "埋点与常规命中的 hit 分开：处决是可数的独立事件");
            Assert.That(sink.Last, Does.Contain("\"kind\":" + ImmortalKindId));
        }

        /// <summary>查不到妖物表的种类：`DefeatMethod` 为 null，`IsKillable` 为 false——正是「只能靠处决」那一支。</summary>
        private static MonsterKind NotKillableKind() =>
            new MonsterKind(ImmortalKindId, 2, "测试用不可常规击杀", 3, 1, 0.8f, 1f, 75f, 6f, 1.5f, 4f, 2f,
                yaoCatalog: null);

        private MonsterRules NewRules(MonsterKind kind = null, ITelemetryScope scope = null)
        {
            var rules = new MonsterRules(config, new MonsterModel(), new RandomService(3ul),
                scope ?? NullTelemetryScope.Instance, kind);
            rules.Reset(new[] { Vector2.zero, Vector2.right * 100f });
            return rules;
        }
    }
}
