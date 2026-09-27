// 职责：补齐普通攻击距离、朝向、按钮边缘和击杀的回归证据。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterStepTests
    {
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
