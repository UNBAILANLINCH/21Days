// 职责：验证伪装独立于距离、敌对状态和受击，不允许敌人造成攻击。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Disguise
{
    public sealed class DisguiseRulesTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Disguised_CloseToEnemy_BlocksAttackUntilRemoved(bool alreadyHostile, bool hitEnemy)
        {
            var config = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var rules = new MonsterRules(config, new MonsterModel(), new RandomService(21ul), NullTelemetryScope.Instance);
                rules.Reset(new[] { Vector2.zero });
                var visible = new PlayerSnapshot(Vector2.right * 0.2f, Vector2.left, false, false, 3);
                if (alreadyHostile)
                {
                    var exposed = new MonsterIntent(visible, 0f);
                    Assert.That(rules.Step(in exposed), Is.True);
                }
                var hidden = new PlayerSnapshot(visible.Position, visible.Facing, false, true, 3);
                var hit = new DamageIntent(1);
                if (hitEnemy) rules.ApplyDamage(in hit, in hidden);
                for (int i = 0; i < 10; i++)
                {
                    var disguised = new MonsterIntent(hidden, 1f);
                    Assert.That(rules.Step(in disguised), Is.False);
                }
                var revealed = new MonsterIntent(visible, 1f);
                Assert.That(rules.Step(in revealed), Is.True);
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
