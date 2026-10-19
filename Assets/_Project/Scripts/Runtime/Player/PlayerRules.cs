// 职责：只用意图与固定步长推进玩家移动、动作和生命。MonoBehaviour 不承担玩法判断。
// 击倒接线（2026-10-07，S3 接进正式流程）：状态机本体在 `Game.Stealth.KnockdownRules`（纯规则、已验收），
//   本类只负责「挨打 → 喂给它」「它的阶段 → 压住攻击与移动速度」这两处。
//   **不接线时（knockdown 为 null）行为与接线前逐字一致**——这是本波「只接击倒、不改别的语义」的边界。
// 治疗口（2026-10-07，S5 背包「使用」通路）：新增 Heal(int)，与 ApplyDamage 对称；道具效果侧经
//   Game.Inventory 的薄适配器 PlayerItemEffectTarget 调它，**不让 Player 认识 Inventory**（依赖方向是 Inventory → Player）。
//   本类不实现 IItemEffectTarget：那会新开一条反方向的依赖边（契约声明在使用方那一侧）。
// 设定生命口（2026-10-14，PRP/combat-iteration D4 / D5）：新增只读 MaxHealth 与 SetHealth(int)。
//   露馅判死、被抓判死（W2）与回合制战后血量回写（W3）都要「把生命直接置成某个值」，
//   而 ApplyDamage / Heal 都是增量口径，且 Heal 对已倒下的人拒绝——两者都答不了「置 0 / 置成战后余量」。
//   放在本类而不是让调用方写 PlayerModel.Health：上限、击倒状态与埋点都归本类（同 Heal 的理由）。
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
        public event Action OnDamaged;
        public event Action OnDied;
        public float AttackRange => config.AttackRange;
        public int AttackDamage => config.AttackDamage;

        /// <summary>生命上限（唯一定义在 <see cref="PlayerConfig.MaxHealth"/>；本属性只转读，不另存一份）。</summary>
        public int MaxHealth => config.MaxHealth;

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
                OnDamaged?.Invoke();
                if (model.Health == 0) OnDied?.Invoke();
            }
        }

        /// <summary>
        /// 回复 <paramref name="amount"/> 点生命，封顶到 <see cref="PlayerConfig.MaxHealth"/>（道具效果走这里，
        /// 而不是让道具侧直接写 <c>PlayerModel.Health</c>——生命的上限、死亡与埋点都归本类）。
        /// <para>
        /// 与 <see cref="ApplyDamage"/> 对称：同一个「先判参数、再判当前状态、改完就埋点」的写法。
        /// 差别只有两处，都是刻意的：① 参数非法**不抛**而是返回 0——治疗是道具触发的软失败，
        /// 抛异常会把「背包面板里点一下」变成崩栈，而伤害来自受控的战斗流程；② 返回实际回复量，
        /// 调用方要拿它拼给玩家看的提示（「回复了 N 点生命」）。
        /// </para>
        /// </summary>
        /// <returns>实际回复的点数；<paramref name="amount"/> 非正、已满血、已倒下（<c>Health &lt;= 0</c>）时返回 0 且不改状态。</returns>
        public int Heal(int amount)
        {
            // amount 非正直接返回 0，不改状态也不埋点（没有发生任何事，不该在埋点里留痕）。
            if (amount <= 0) return 0;

            // 已倒下不回血：生命归零即失败/结局，回血应该走 Reset（重开），不该由一件道具把结局抹掉。
            // 依据：PlayerRules.Step 在 Health <= 0 时直接返回 false（倒地不再行动）、ApplyDamage 也只在 Health > 0 时结算。
            if (model.Health <= 0) return 0;

            int before = model.Health;
            // 封顶用 config.MaxHealth 而不是 PlayerModel 里的任何字段：上限只有一处定义（PlayerConfig.MaxHealth）。
            model.Health = GameMath.Min(config.MaxHealth, before + amount);
            int healed = model.Health - before;
            if (healed <= 0) return 0; // 已经满血：不改状态、不埋点（同 amount 非正的口径）

            telemetry.Track("healed", ("hp", model.Health), ("amount", healed));
            return healed;
        }

        /// <summary>
        /// 把生命直接设成 <paramref name="health"/>，夹在 [0, <see cref="MaxHealth"/>]；同时<b>清掉击倒状态</b>
        /// （只清阶段、不清累计次数，同 <see cref="Reset"/> 的口径）。
        /// <para>
        /// 调用方（PRP/combat-iteration）：露馅口径为「死亡」、被抓口径判定失败时置 0（D1 / D4，走既有的生命归零收尾：
        /// 剧情阶段内判 Downed、自由探索弹镜碎页）；回合制战后按比例写回实时血量（D5）。
        /// 清击倒的理由：被设定的血量是「结算之后的终态」，战后不该还趴着；置 0 时也不留一个半截的倒地计时。
        /// </para>
        /// <para>
        /// 与 <see cref="Heal"/> 的分工：Heal 是道具的增量回复、对已倒下的人拒绝；本方法是流程结算的直接设定，
        /// 不看当前是否倒下（战后写回可以把 0 以外的值写进来，由调用方保证语义）。不在 tick 里逐帧调。
        /// </para>
        /// </summary>
        /// <returns>夹值之后实际写入的生命。</returns>
        public int SetHealth(int health)
        {
            int before = model.Health;
            int clamped = GameMath.Clamp(health, 0, config.MaxHealth);
            model.Health = clamped;
            if (before > 0 && clamped == 0) OnDied?.Invoke();
            if (knockdown != null)
            {
                knockdown.Reset(false);
            }

            if (clamped != before)
            {
                telemetry.Track("health_set", ("hp", clamped), ("from", before), ("requested", health));
                if (clamped == 0)
                {
                    telemetry.Track("dead");
                }
            }

            return clamped;
        }
    }
}
