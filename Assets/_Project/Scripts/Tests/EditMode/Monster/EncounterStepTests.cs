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
    }
}
