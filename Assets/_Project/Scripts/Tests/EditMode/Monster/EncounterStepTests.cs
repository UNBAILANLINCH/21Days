// 职责：补齐普通攻击距离、朝向、按钮边缘和击杀的回归证据。
using Game.Core.Simulation;
using Game.Core.Replay;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterStepTests
    {
        [Test]
        public void TamePressLatch_AllowsFastSecondPress_ButHeldButtonDoesNotRepeat()
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            var mc = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var random = new RandomService(21ul);
                var player = new PlayerRules(pc, new PlayerModel(), NullTelemetryScope.Instance);
                var monster = new MonsterRules(mc, new MonsterModel(), random, NullTelemetryScope.Instance);
                var step = new EncounterStep(player, monster);
                step.Begin(Vector2.zero, new[] { new Vector2(10, 0) });
                var pressed = new InputCommand(Vector2.zero, Vector2.zero, InputCommand.ButtonTame | InputCommand.ButtonTamePressed, Vector2.zero, 0);
                var context = new SimulationContext(0, 0f, in pressed, random);
                step.Step(in context);
                Assert.That(step.Taming.IsControllingEnemy, Is.True);
                var held = new InputCommand(Vector2.zero, Vector2.zero, InputCommand.ButtonTame, Vector2.zero, 0);
                context = new SimulationContext(1, 0f, in held, random);
                step.Step(in context);
                Assert.That(step.Taming.IsControllingEnemy, Is.True, "长按不能反复切换");
                context = new SimulationContext(2, 0f, in pressed, random);
                step.Step(in context);
                Assert.That(step.CurrentControlId, Is.EqualTo(step.Taming.PlayerId), "新按下沿不依赖采到松开 tick");
            }
            finally { Object.DestroyImmediate(pc); Object.DestroyImmediate(mc); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MultiActorReplay_RoundTripsBeforeAndAfterBegin(bool active)
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            var mc = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var player = new PlayerRules(pc, new PlayerModel(), NullTelemetryScope.Instance);
                var monster = new MonsterRules(mc, new MonsterModel(), new RandomService(21ul), NullTelemetryScope.Instance);
                var step = new EncounterStep(player, monster);
                Vector2[] route = { new Vector2(10, 0) };
                step.ConfigureTaming("player", "玩家", new[] { "a", "b" }, new[] { "甲", "乙" }, new[] { route, new[] { new Vector2(20, 0) } });
                if (active)
                {
                    step.Begin(Vector2.zero, route);
                    step.Taming.TryTame("a");
                    step.Taming.TryTame("b");
                    step.Taming.TryControl("b");
                }
                var bytes = new StateBuffer();
                step.Serialize(bytes);
                step.Begin(Vector2.zero, route);
                step.Deserialize(bytes);
                Assert.That(bytes.Remaining, Is.Zero);
                Assert.That(step.IsActive, Is.EqualTo(active));
                Assert.That(step.CurrentControlId, Is.EqualTo(active ? "b" : "player"));
                Assert.That(step.Taming.IsTargetTamed("a"), Is.EqualTo(active));
                Assert.That(step.Taming.IsTargetTamed("b"), Is.EqualTo(active));
                var roundTrip = new StateBuffer();
                step.Serialize(roundTrip);
                Assert.That(roundTrip.Length, Is.EqualTo(bytes.Length));
                for (int i = 0; i < bytes.Length; i++) Assert.That(roundTrip.GetBuffer()[i], Is.EqualTo(bytes.GetBuffer()[i]), "字节 " + i);
            }
            finally { Object.DestroyImmediate(pc); Object.DestroyImmediate(mc); }
        }

        [Test]
        public void MultiActorRestore_RejectsMismatchBeforeMutation_AndRestoresBothOwners()
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            var mc = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var player = new PlayerRules(pc, new PlayerModel(), NullTelemetryScope.Instance);
                var monster = new MonsterRules(mc, new MonsterModel(), new RandomService(21ul), NullTelemetryScope.Instance);
                var step = new EncounterStep(player, monster);
                Vector2[] route = { new Vector2(10, 0) };
                step.ConfigureTaming("player", "玩家", new[] { "a", "b" }, new[] { "甲", "乙" }, new[] { route, new[] { new Vector2(20, 0) } });
                step.Begin(Vector2.zero, route);
                step.Taming.TryTame("a");
                step.Taming.TryTame("b");
                step.Taming.TryControl("b");
                var saved = step.Capture(1);
                step.Begin(Vector2.one, route);
                Assert.That(step.Taming.GetTarget("b").Model.Position, Is.EqualTo(new Vector2(20, 0)));
                saved.TamingTargets[1].Id = "unknown";
                Assert.Throws<System.ArgumentException>(() => step.Restore(saved));
                Assert.That(player.Model.Position, Is.EqualTo(Vector2.one), "错误目标不能部分改写玩家");
                saved.TamingTargets[1].Id = "b";
                step.Restore(saved);
                Assert.That(step.CurrentControlId, Is.EqualTo("b"));
                Assert.That(step.Taming.TryControl("a"), Is.True);
                Assert.That(step.Taming.TryControl("b"), Is.True);
                saved.TamingTargets = null;
                step.Restore(saved);
                Assert.That(step.CurrentControlId, Is.EqualTo("player"));
                Assert.That(step.Taming.IsTargetTamed("b"), Is.False, "旧档默认未驯服");
                Assert.That(step.Taming.GetTarget("b").Model.Position, Is.EqualTo(new Vector2(20, 0)));
            }
            finally { Object.DestroyImmediate(pc); Object.DestroyImmediate(mc); }
        }

        [TestCase(0.5f, true)]
        [TestCase(1.1f, false)]
        [TestCase(-0.5f, false)]
        public void Attack_UsesDistanceFacingAndPressEdges(float enemyX, bool hits)
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            var mc = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var player = new PlayerRules(pc, new PlayerModel(), NullTelemetryScope.Instance);
                var enemy = new MonsterRules(mc, new MonsterModel(), new RandomService(21ul), NullTelemetryScope.Instance);
                var step = new EncounterStep(player, enemy);
                step.Begin(Vector2.zero, new[] { new Vector2(enemyX, 0) });
                var random = new RandomService(21ul);
                for (int i = 0; i < 3; i++)
                {
                    // 每次复位玩家只为隔离敌人的反击；攻击本身仍经过 EncounterStep。
                    player.Reset(Vector2.zero);
                    var command = new InputCommand(Vector2.zero, Vector2.zero, InputCommand.ButtonAttack, Vector2.zero, 0);
                    var context = new SimulationContext(i, 0f, in command, random);
                    step.Step(in context);
                    Assert.That(enemy.Model.Health, Is.EqualTo(hits ? mc.MaxHealth - i - 1 : mc.MaxHealth));
                    step.Step(in context);
                    Assert.That(enemy.Model.Health, Is.EqualTo(hits ? mc.MaxHealth - i - 1 : mc.MaxHealth), "长按不重复攻击");
                }
                if (hits) Assert.That(enemy.Model.Mode, Is.EqualTo(MonsterMode.Dead));
            }
            finally { Object.DestroyImmediate(pc); Object.DestroyImmediate(mc); }
        }

        // 波 9：表现层碰撞回写——激活时改写玩家逻辑位置。
        [Test]
        public void Step_AdvancesBothPatrolsExactlyOnce()
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            var mc = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var random = new RandomService(21ul);
                var player = new PlayerRules(pc, new PlayerModel(), NullTelemetryScope.Instance);
                var enemy = new MonsterRules(mc, new MonsterModel(), random, NullTelemetryScope.Instance);
                var step = new EncounterStep(player, enemy);
                Vector2[] route = { Vector2.right * 10, Vector2.right * 20 };
                step.ConfigureTaming("player", "玩家", new[] { "a", "b" }, new[] { "甲", "乙" },
                    new[] { route, new[] { Vector2.right * 30, Vector2.right * 40 } });
                step.Begin(Vector2.zero, route);
                InputCommand command = InputCommand.Empty;
                var context = new SimulationContext(0, 0.1f, in command, random);

                step.Step(in context);

                Assert.That(enemy.Model.PatrolWalkElapsed, Is.EqualTo(0.1f));
                Assert.That(step.Taming.GetTarget("b").Model.PatrolWalkElapsed, Is.EqualTo(0.1f));
            }
            finally { Object.DestroyImmediate(pc); Object.DestroyImmediate(mc); }
        }

        // 波 9：表现层碰撞回写——激活时改写玩家逻辑位置。
        [Test]
        public void CorrectPlayerPosition_WhenActive_OverridesPlayerPosition()
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            var mc = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var model = new PlayerModel();
                var player = new PlayerRules(pc, model, NullTelemetryScope.Instance);
                var enemy = new MonsterRules(mc, new MonsterModel(), new RandomService(21ul), NullTelemetryScope.Instance);
                var step = new EncounterStep(player, enemy);
                step.Begin(Vector2.zero, new[] { new Vector2(10f, 0f) });

                step.CorrectPlayerPosition(new Vector2(2f, -0.6f));

                Assert.That(model.Position, Is.EqualTo(new Vector2(2f, -0.6f)));
            }
            finally { Object.DestroyImmediate(pc); Object.DestroyImmediate(mc); }
        }

        // 波 9：遭遇未激活（离场后 / 尚未 Begin）时回写无效，避免场景残留事件改写下一次遭遇的玩家。
        [Test]
        public void CorrectPlayerPosition_WhenInactive_IsIgnored()
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            var mc = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var model = new PlayerModel();
                var player = new PlayerRules(pc, model, NullTelemetryScope.Instance);
                var enemy = new MonsterRules(mc, new MonsterModel(), new RandomService(21ul), NullTelemetryScope.Instance);
                var step = new EncounterStep(player, enemy);
                step.Begin(new Vector2(1f, 1f), new[] { new Vector2(10f, 0f) });
                step.End();

                step.CorrectPlayerPosition(new Vector2(5f, 5f));

                Assert.That(model.Position, Is.EqualTo(new Vector2(1f, 1f)));
            }
            finally { Object.DestroyImmediate(pc); Object.DestroyImmediate(mc); }
        }

        // 渲染插值 × 碰撞回写：被挡的轴（值被改写）把 PreviousPosition 也设成同一值，下一帧不从墙里倒插；
        // 没改写的轴保留 PreviousPosition，贴墙滑动时沿墙那一轴继续平滑插值。
        [Test]
        public void CorrectPlayerPosition_AlignsPreviousOnlyOnCorrectedAxis()
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            var mc = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var model = new PlayerModel();
                var player = new PlayerRules(pc, model, NullTelemetryScope.Instance);
                var enemy = new MonsterRules(mc, new MonsterModel(), new RandomService(21ul), NullTelemetryScope.Instance);
                var step = new EncounterStep(player, enemy);
                step.Begin(Vector2.zero, new[] { new Vector2(10f, 0f) });
                var random = new RandomService(21ul);
                var command = new InputCommand(new Vector2(1f, 1f), Vector2.zero, 0u, Vector2.zero, 0);
                var context = new SimulationContext(0, 0.1f, in command, random);
                step.Step(in context);
                Vector2 previous = model.PreviousPosition;
                Vector2 current = model.Position;
                Assert.That(current.x, Is.GreaterThan(previous.x));

                // 视图的分轴合成：X 被墙挡回到 0.02，Y 没被挡、原样是逻辑值。
                step.CorrectPlayerPosition(new Vector2(0.02f, current.y));

                Assert.That(model.Position, Is.EqualTo(new Vector2(0.02f, current.y)));
                Assert.That(model.PreviousPosition.x, Is.EqualTo(0.02f));
                Assert.That(model.PreviousPosition.y, Is.EqualTo(previous.y));
            }
            finally { Object.DestroyImmediate(pc); Object.DestroyImmediate(mc); }
        }

        // 双方规则都不推进的 tick（未激活 / 结果待结算）也要对齐上一 tick 位置，否则视图在最后一步的两点间来回插值。
        [Test]
        public void Step_WhenInactive_AlignsPreviousPositions()
        {
            var pc = ScriptableObject.CreateInstance<PlayerConfig>();
            var mc = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var model = new PlayerModel();
                var monsterModel = new MonsterModel();
                var player = new PlayerRules(pc, model, NullTelemetryScope.Instance);
                var enemy = new MonsterRules(mc, monsterModel, new RandomService(21ul), NullTelemetryScope.Instance);
                var step = new EncounterStep(player, enemy);
                step.Begin(Vector2.zero, new[] { new Vector2(10f, 0f), new Vector2(20f, 0f) });
                Assert.That(model.PreviousPosition, Is.EqualTo(model.Position), "Begin 后对齐");
                Assert.That(monsterModel.PreviousPosition, Is.EqualTo(monsterModel.Position), "Begin 后对齐");

                var random = new RandomService(21ul);
                var command = new InputCommand(Vector2.up, Vector2.zero, 0u, Vector2.zero, 0);
                var context = new SimulationContext(0, 0.1f, in command, random);
                step.Step(in context);
                Assert.That(model.PreviousPosition, Is.Not.EqualTo(model.Position));
                Assert.That(monsterModel.PreviousPosition, Is.Not.EqualTo(monsterModel.Position));

                step.End();
                step.Step(in context);
                Assert.That(model.PreviousPosition, Is.EqualTo(model.Position));
                Assert.That(monsterModel.PreviousPosition, Is.EqualTo(monsterModel.Position));
            }
            finally { Object.DestroyImmediate(pc); Object.DestroyImmediate(mc); }
        }
    }
}
