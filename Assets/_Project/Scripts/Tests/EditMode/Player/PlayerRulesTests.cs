// 职责：验证玩家意图、生命与快照在固定输入下可恢复。
using Game.Core.Replay;
using Game.Core.Telemetry;
using Game.Player;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Player
{
    public sealed class PlayerRulesTests
    {
        private PlayerConfig config;
        private PlayerModel model;
        private PlayerRules rules;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<PlayerConfig>();
            model = new PlayerModel();
            rules = new PlayerRules(config, model, NullTelemetryScope.Instance);
            rules.Reset(Vector2.zero);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void FixedIntent_ControlsMovementActionsDamageAndDeath()
        {
            rules.Step(new PlayerIntent(Vector2.right, true, true, false), 1f);
            Assert.That(model.Position.x, Is.EqualTo(config.SneakSpeed).Within(0.001f));
            Assert.That(model.Snapshot.IsSneaking, Is.True);
            Assert.That(model.Snapshot.IsDisguised, Is.True);

            rules.Step(new PlayerIntent(Vector2.zero, false, true, false), 0f);
            Assert.That(model.IsDisguised, Is.True, "长按伪装只切换一次");
            rules.Step(new PlayerIntent(Vector2.zero, false, false, true), 0f);
            Assert.That(rules.Step(new PlayerIntent(Vector2.zero, false, false, true), 0f), Is.False,
                "长按攻击只触发一次");

            rules.ApplyDamage(new DamageIntent(3));
            Assert.That(model.Snapshot.IsAlive, Is.False);
            Assert.That(rules.Step(new PlayerIntent(Vector2.right, false, false, true), 1f), Is.False);
            Assert.That(model.Position.x, Is.EqualTo(config.SneakSpeed).Within(0.001f));
        }

        [Test]
        public void ReplaySnapshot_RestoresActionEdgesAndCooldown()
        {
            rules.Step(new PlayerIntent(Vector2.up, false, true, true), 0.25f);
            var buffer = new StateBuffer();
            model.Serialize(buffer);
            Vector2 position = model.Position;
            float cooldown = model.AttackCooldownLeft;

            rules.Reset(Vector2.right * 10f);
            buffer.SeekToStart();
            model.Deserialize(buffer);

            Assert.That(buffer.Remaining, Is.Zero);
            Assert.That(model.Position, Is.EqualTo(position));
            Assert.That(model.IsDisguised, Is.True);
            Assert.That(model.AttackCooldownLeft, Is.EqualTo(cooldown));
            Assert.That(rules.Step(new PlayerIntent(Vector2.zero, false, true, true), 1f), Is.False,
                "恢复后的按键边缘不能重复攻击");
            Assert.That(model.IsDisguised, Is.True);
        }
    }
}
