// 职责：只用意图与固定步长推进玩家移动、动作和生命。MonoBehaviour 不承担玩法判断。
using System;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using UnityEngine;

namespace Game.Player
{
    public sealed class PlayerRules
    {
        private readonly PlayerConfig config;
        private readonly PlayerModel model;
        private readonly ITelemetryScope telemetry;

        public PlayerRules(PlayerConfig config, PlayerModel model, ITelemetryScope telemetry)
        {
            this.config = config == null ? throw new ArgumentNullException(nameof(config)) : config;
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            if (config.MoveSpeed <= 0f || config.SneakSpeed <= 0f || config.AttackRange <= 0f
                || config.AttackCooldown < 0f || config.MaxHealth <= 0 || config.AttackDamage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(config), "PlayerConfig 数值必须为正，攻击冷却可为零");
            }
        }

        public PlayerModel Model => model;
        public float AttackRange => config.AttackRange;
        public int AttackDamage => config.AttackDamage;

        public void Reset(Vector2 position)
        {
            model.Position = position;
            model.Facing = Vector2.right;
            model.IsSneaking = false;
            model.IsDisguised = false;
            model.Health = config.MaxHealth;
            model.AttackCooldownLeft = 0f;
            model.PreviousDisguise = false;
            model.PreviousAttack = false;
        }

        public bool Step(in PlayerIntent intent, float deltaTime)
        {
            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (model.Health <= 0)
            {
                return false;
            }

            model.AttackCooldownLeft = GameMath.Max(0f, model.AttackCooldownLeft - deltaTime);
            model.IsSneaking = intent.Sneak;
            if (intent.Disguise && !model.PreviousDisguise)
            {
                model.IsDisguised = !model.IsDisguised;
            }

            Vector2 movement = intent.Movement;
            if (GameMath.SqrMagnitude(movement) > 1f)
            {
                movement = GameMath.Normalize(movement);
            }

            if (GameMath.SqrMagnitude(movement) > 0f)
            {
                model.Facing = GameMath.Normalize(movement);
                model.Position += movement * (intent.Sneak ? config.SneakSpeed : config.MoveSpeed) * deltaTime;
            }

            bool attack = intent.Attack && !model.PreviousAttack && model.AttackCooldownLeft <= 0f;
            if (attack)
            {
                model.AttackCooldownLeft = config.AttackCooldown;
                telemetry.Track("attack");
            }

            model.PreviousDisguise = intent.Disguise;
            model.PreviousAttack = intent.Attack;
            return attack;
        }

        public void ApplyDamage(in DamageIntent intent)
        {
            if (intent.Amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intent), "伤害必须为正数");
            }

            if (model.Health > 0)
            {
                model.Health = GameMath.Max(0, model.Health - intent.Amount);
                telemetry.Track("hit", ("hp", model.Health), ("damage", intent.Amount));
                if (model.Health == 0)
                {
                    telemetry.Track("dead");
                }
            }
        }
    }
}
