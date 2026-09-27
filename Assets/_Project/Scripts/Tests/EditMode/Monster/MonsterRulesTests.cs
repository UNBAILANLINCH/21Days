// 职责：验证巡逻、感知优先级、警戒时间、战斗与独立快照恢复。
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class MonsterRulesTests
    {
        private MonsterConfig config;
        private MonsterRules rules;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<MonsterConfig>();
            rules = NewRules();
            rules.Reset(new[] { Vector2.zero, Vector2.right * 100f });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void Patrol_PausesAfterSevenToTenSeconds_ForTwoSeconds()
        {
            for (int i = 0; i < 6; i++)
            {
                Step(NoTarget(), 1f);
            }

            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
            for (int i = 0; i < 4 && rules.Model.Mode == MonsterMode.PatrolWalk; i++)
            {
                Step(NoTarget(), 1f);
            }

            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolPause));
            Vector2 stoppedAt = rules.Model.Position;
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.Position, Is.EqualTo(stoppedAt));
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
        }

        [Test]
        public void Perception_PrioritizesRed_AndRespectsDisguiseSneakAndRearProximity()
        {
            Step(Target(new Vector2(4f, 0f), disguised: true), 0f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
            Step(Target(new Vector2(-1f, 0f), sneaking: true), 0f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
            Step(Target(new Vector2(-1f, 0f)), 0f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Alert));

            rules.Reset(new[] { Vector2.zero, Vector2.right * 100f });
            Step(Target(Vector2.right, sneaking: true, disguised: true), 0f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Hostile), "红区优先于伪装与潜行");
        }

        [Test]
        public void Alert_FillsInFourSeconds_DecaysInSix_AfterHostileLossInTwo()
        {
            for (int i = 0; i < 4; i++)
            {
                Step(Target(rules.Model.Position + Vector2.right * 4f), 1f);
            }

            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Hostile));
            Assert.That(rules.Model.Alert, Is.EqualTo(1f));
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Hostile));
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Alert));
            Assert.That(rules.Model.Alert, Is.EqualTo(1f));
            Step(NoTarget(), 3f);
            Assert.That(rules.Model.Alert, Is.EqualTo(0.75f).Within(0.001f));
            Step(NoTarget(), 3f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
        }

        [Test]
        public void Combat_HitsAndDies_ThenCannotAct()
        {
            Assert.That(Step(Target(Vector2.right), 0f), Is.False);
            Assert.That(Step(Target(Vector2.right), 0.5f), Is.True);
            var damage = new DamageIntent(3);
            PlayerSnapshot attacker = Target(Vector2.right);
            rules.ApplyDamage(in damage, in attacker);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Dead));
            Assert.That(rules.Model.Health, Is.Zero);
            Assert.That(Step(attacker, 1f), Is.False);
        }

        [Test]
        public void ReplaySnapshot_RestoresPatrolPointsTimersAndRandomState()
        {
            Step(NoTarget(), 1f);
            var buffer = new StateBuffer();
            rules.Serialize(buffer);

            MonsterRules restored = NewRules();
            buffer.SeekToStart();
            restored.Deserialize(buffer);
            Assert.That(buffer.Remaining, Is.Zero);
            Assert.That(restored.Model.Position, Is.EqualTo(rules.Model.Position));
            Assert.That(restored.Model.NextPauseAfter, Is.EqualTo(rules.Model.NextPauseAfter));

            for (int i = 0; i < 10; i++)
            {
                var intent = new MonsterIntent(NoTarget(), 1f);
                rules.Step(in intent);
                restored.Step(in intent);
            }

            Assert.That(restored.Model.Position, Is.EqualTo(rules.Model.Position));
            Assert.That(restored.Model.Mode, Is.EqualTo(rules.Model.Mode));
            Assert.That(restored.Model.NextPauseAfter, Is.EqualTo(rules.Model.NextPauseAfter));
        }

        private MonsterRules NewRules() => new MonsterRules(config, new MonsterModel(),
            new RandomService(123ul), NullTelemetryScope.Instance);

        private bool Step(PlayerSnapshot target, float deltaTime)
        {
            var intent = new MonsterIntent(target, deltaTime);
            return rules.Step(in intent);
        }

        private static PlayerSnapshot NoTarget() => new PlayerSnapshot(Vector2.right * 1000f,
            Vector2.right, false, false, 3);

        private static PlayerSnapshot Target(Vector2 position, bool sneaking = false, bool disguised = false) =>
            new PlayerSnapshot(position, Vector2.left, sneaking, disguised, 3);

        // 渲染插值：每 tick 推进前记下上一 tick 位置；Reset / 快照恢复 / 存档恢复时对齐，不跨瞬移插值。
        [Test]
        public void Step_RecordsPreviousPosition_AndResetRestoreAlignIt()
        {
            Assert.That(rules.Model.PreviousPosition, Is.EqualTo(rules.Model.Position), "Reset 后对齐");
            Vector2 before = rules.Model.Position;
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.PreviousPosition, Is.EqualTo(before));
            Assert.That(rules.Model.Position, Is.Not.EqualTo(before));

            MonsterSaveData saved = rules.Capture();
            var buffer = new StateBuffer();
            rules.Serialize(buffer);

            MonsterRules restored = NewRules();
            buffer.SeekToStart();
            restored.Deserialize(buffer);
            Assert.That(buffer.Remaining, Is.Zero, "PreviousPosition 不进快照，字节布局不变");
            Assert.That(restored.Model.PreviousPosition, Is.EqualTo(restored.Model.Position));

            MonsterRules loaded = NewRules();
            loaded.Restore(saved);
            Assert.That(loaded.Model.PreviousPosition, Is.EqualTo(loaded.Model.Position));
        }

        // 驯服接管的移动入口同样先记上一 tick 位置。
        [Test]
        public void MoveControlled_RecordsPreviousPositionBeforeMoving()
        {
            Vector2 before = rules.Model.Position;
            rules.MoveControlled(Vector2.up, 1f);
            Assert.That(rules.Model.PreviousPosition, Is.EqualTo(before));
            Assert.That(rules.Model.Position.y, Is.GreaterThan(before.y));
        }
    }
}
