// 职责：只用意图与固定步长推进玩家移动、动作和生命。MonoBehaviour 不承担玩法判断。
// 击倒接线（2026-10-07，S3 接进正式流程）：状态机本体在 `Game.Stealth.KnockdownRules`（纯规则、已验收），
//   本类只负责「挨打 → 喂给它」「它的阶段 → 压住攻击与移动速度」这两处。
//   **不接线时（knockdown 为 null）行为与接线前逐字一致**——这是本波「只接击倒、不改别的语义」的边界。
using System;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Stealth;
using UnityEngine;

namespace Game.Player
{
    public sealed class PlayerRules
    {
        private readonly PlayerConfig config;
        private readonly PlayerModel model;
        private readonly ITelemetryScope telemetry;

        // 为 null = 没接击倒：挨打只扣血，仍可攻击、按原速移动（接线前的行为）。
        private KnockdownRules knockdown;

        public PlayerRules(PlayerConfig config, PlayerModel model, ITelemetryScope telemetry)
        {
            this.config = config == null ? throw new ArgumentNullException(nameof(config)) : config;
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            if (config.MoveSpeed <= 0f || config.SneakSpeed <= 0f || config.RunSpeed <= 0f || config.AttackRange <= 0f
                || config.AttackCooldown < 0f || config.MaxHealth <= 0 || config.AttackDamage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(config), "PlayerConfig 数值必须为正，攻击冷却可为零");
            }
        }

        public PlayerModel Model => model;
        public float AttackRange => config.AttackRange;
        public int AttackDamage => config.AttackDamage;

        /// <summary>
        /// 击倒状态机；为 null 表示本次流程没接击倒（挨打只扣血）。接线方是遭遇流程
        /// （<c>Monster.EncounterStep</c> 构造时调 <see cref="UseKnockdown"/>）。
        /// </summary>
        public KnockdownRules Knockdown => knockdown;

        /// <summary>
        /// 接入击倒状态机（`03_潜行与暗杀.md:85` R3：挨打 → 击倒 → 只能缓慢移动、不能攻击）。
        /// <para>
        /// 做成后置装配而不是构造参数：本类是 VContainer 注册类型，构造参数一变容器就解析不到
        /// （VContainer 取参数最多的构造函数、且不看默认值），接线必须走方法。
        /// </para>
        /// </summary>
        public void UseKnockdown(KnockdownRules rules) => knockdown = rules;

        public void Reset(Vector2 position)
        {
            model.Position = position;
            model.SyncPreviousPosition();
            model.Facing = Vector2.right;
            model.IsSneaking = false;
            model.IsRunning = false;
            model.IsDisguised = false;
            model.Health = config.MaxHealth;
            model.AttackCooldownLeft = 0f;
            model.PreviousDisguise = false;
            model.PreviousAttack = false;
            model.PreviousRun = false;
            // 只清阶段不清计数：累计击倒次数是「本阶段」的量，归零由阶段迁移负责（KnockdownRules.Reset 的约定）。
            if (knockdown != null)
            {
                knockdown.Reset(false);
            }
        }

        public bool Step(in PlayerIntent intent, float deltaTime)
        {
            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            // 表现层插值的起点：放在任何提前返回之前，死亡 / 不动的 tick 也要对齐，否则视图会在旧两点间来回插。
            model.SyncPreviousPosition();
            if (model.Health <= 0)
            {
                return false;
            }

            model.AttackCooldownLeft = GameMath.Max(0f, model.AttackCooldownLeft - deltaTime);
            model.IsSneaking = intent.Sneak;
            // 走 / 跑按下沿切换（写法同伪装）：长按只切一次；潜行不改奔跑模式，只在移动时临时压过它。
            if (intent.Run && !model.PreviousRun)
            {
                model.IsRunning = !model.IsRunning;
                telemetry.Track("run_changed", ("active", model.IsRunning));
            }

            if (intent.Disguise && !model.PreviousDisguise)
            {
                model.IsDisguised = !model.IsDisguised;
                telemetry.Track("disguise_changed", ("active", model.IsDisguised));
            }

            // 击倒状态机先推进一步（计时 / 起身），后面两处读它的结果：倒地 0 速、挣扎按倍率、两个阶段都不能攻击。
            if (knockdown != null)
            {
                knockdown.Tick(KnockdownInput.Normal(model.Health, intent.Attack, GameMath.SqrMagnitude(intent.Movement) > 0f), deltaTime);
            }

            Vector2 movement = intent.Movement;
            if (GameMath.SqrMagnitude(movement) > 1f)
            {
                movement = GameMath.Normalize(movement);
            }

            if (GameMath.SqrMagnitude(movement) > 0f)
            {
                model.Facing = GameMath.Normalize(movement);
                // 三档速度：潜行按住 > 奔跑模式 > 步行。
                float speed = intent.Sneak ? config.SneakSpeed : (model.IsRunning ? config.RunSpeed : config.MoveSpeed);
                if (knockdown != null)
                {
                    speed *= knockdown.MoveSpeedMultiplier;
                }

                model.Position += movement * speed * deltaTime;
            }

            bool attack = intent.Attack && !model.PreviousAttack && model.AttackCooldownLeft <= 0f
                && (knockdown == null || knockdown.CanAttack(intent.Attack));
            if (attack)
            {
                model.AttackCooldownLeft = config.AttackCooldown;
                telemetry.Track("attack");
            }

            model.PreviousDisguise = intent.Disguise;
            model.PreviousAttack = intent.Attack;
            model.PreviousRun = intent.Run;
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
                // 挨打这一下喂给击倒状态机：deltaTime 传 0——扣血事件不推进计时，倒地时长从下一个 tick 开始走。
                if (knockdown != null)
                {
                    knockdown.Tick(KnockdownInput.TakeHitNow(model.Health), 0f);
                }

                if (model.Health == 0)
                {
                    telemetry.Track("dead");
                }
            }
        }
    }
}
