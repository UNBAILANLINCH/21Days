// 职责：玩家的「挨打之后」状态机——击倒 → 缓慢移动 → 起身，以及**另一套口径**（扣血死亡）的开关。
//
// 为什么新建：
// - 复用：工程里没有这个状态。Player 现在是「生命归零直接死亡」（`PlayerRules.ApplyDamage`，
//   见 `Assets/_Project/Scripts/Runtime/Player/PlayerRules.cs:102-118`），`PlayerRules.Step` 只处理
//   移动 / 潜行 / 奔跑 / 伪装 / 攻击，没有任何受击状态；Mirror 的「镜裂三次」是另一套旧版失败流程。
// - 扩展：`PlayerRules` / `PlayerConfig` 属于本次任务明令不许改的文件（并行任务在改 Monster 侧，
//   Player 侧也要留出接线口），且击倒的时长与口径本身**没有拍板**（见下），先做进规则类会把
//   未定值焊死在主流程里。
// - 新建：以上两条都不成立，故新建独立规则类，等策划拍板后由接线侧注入。
//
// 出处（真源）：
// - `docs/design/features-spotlight/03_潜行与暗杀.md:85`（R3 原文：怪物攻击会把主角击倒，
//   击倒期间「只能缓慢移动，不能攻击」）
// - `03:86`（R4 待定：持续多久、怎样起身、击倒中再挨打会怎样、算不算失败，原文都没写）
// - `03:178`（数值表：击倒时长、缓慢移动的速度 = 原文没写 [待定]）
// - `03:214`（约束 2：挨打不是直接死，是留给玩家的逃生窗口；时长要与 04 的摆脱条件一起定）
// - `03:229`（Q2 原文矛盾）、`00_功能总览.md:290`（§5 **C5 挨打：击倒还是死亡**）
// - `00:383`（§8.1 #4：挨打的后果与「本体能不能攻击」是阻塞问题）
// - `docs/roadmap.md:348`（风险 3：击倒与 BOSS 血量怎么并存还没定，C5 与 S3 / S7 之前要决定字段去留）
using Game.Core.Simulation;

namespace Game.Stealth
{
    /// <summary>玩家受击后的阶段。</summary>
    public enum KnockdownPhase : byte
    {
        /// <summary>正常（没被击倒）。</summary>
        None = 0,

        /// <summary>倒地期：只能爬，不能攻击。</summary>
        Downed = 1,

        /// <summary>挣扎期：缓慢移动，不能攻击。</summary>
        Crawling = 2,

        /// <summary>已死亡（只在 <see cref="DownedHitPolicy.KnockdownThenDeath"/> 口径下出现）。</summary>
        Dead = 3,
    }

    /// <summary>
    /// 挨打的后果口径。**这是 §5 C5 那对原文矛盾的策略开关，不是数值**：
    /// sp00 说「击倒后还能慢慢挪」，mai 说「生命归零即死亡」，两说并存，由这里二选一注入。
    /// </summary>
    public enum DownedHitPolicy : byte
    {
        /// <summary>
        /// sp00 口径（`03:85` R3）：挨打只击倒，**扣血不会致死**，生命到底也还是击倒。
        /// 对应「本体打不过怪、但挨一下有逃生窗口」（`03:214` 约束 2）。
        /// </summary>
        KnockdownOnly = 1,

        /// <summary>
        /// mai 口径（`03:62` 验收 5 / `00:290` C5 的另一半）：生命归零即死亡，不再有击倒窗口。
        /// </summary>
        HealthDeath = 2,

        /// <summary>
        /// 折中口径：**默认按击倒处理，只有生命归零才死**。
        /// 这是把 C5 两说都留着的写法——策划拍板选前两者之一即可，选之前用这个不会把任何一侧做死。
        /// </summary>
        KnockdownThenDeath = 3,
    }

    /// <summary>
    /// 击倒状态机的可调项。时长与速度**全是占位**，原文没写（`03:178`）。
    /// </summary>
    public readonly struct KnockdownSettings
    {
        /// <summary>[待拍板] 倒地期时长（秒）。占位 1.5，等 `00_功能总览.md:383` §8.1 #4。</summary>
        public float DownedSeconds { get; }

        /// <summary>
        /// [待拍板] 挣扎期（缓慢移动）时长（秒）。占位 3，等 §8.1 #4；
        /// `03:214` 要求它与 `04_追逐.md` 的摆脱条件一起定。
        /// </summary>
        public float CrawlSeconds { get; }

        /// <summary>
        /// [待拍板] 挣扎期移动速度倍率（相对玩家步行）。占位 0.35，等 `03:178`。
        /// </summary>
        public float CrawlSpeedMultiplier { get; }

        /// <summary>
        /// [待拍板] 击倒中再挨打的处理：true = 重置计时（重新倒地），false = 不重置（原计时走完）。
        /// 占位 true，等 `03:86` R4「击倒中再挨打会怎样，原文都没写」。
        /// </summary>
        public bool ResetOnHitWhileDowned { get; }

        /// <summary>
        /// [待拍板] 击倒中再挨打的节流窗口（秒）：窗口内的重复命中只计数不重置。
        /// 占位 0.5，等 R4 一起拍。设 0 = 每次都算。
        /// </summary>
        public float HitThrottleSeconds { get; }

        /// <summary>挨打口径（§5 C5 的策略开关，不是数值）。</summary>
        public DownedHitPolicy HitPolicy { get; }

        /// <summary>玩家生命上限，用来判 <see cref="DownedHitPolicy.HealthDeath"/> 分支。</summary>
        public int MaxHealth { get; }

        public KnockdownSettings(
            float downedSeconds,
            float crawlSeconds,
            float crawlSpeedMultiplier,
            bool resetOnHitWhileDowned,
            float hitThrottleSeconds,
            DownedHitPolicy hitPolicy,
            int maxHealth)
        {
            if (downedSeconds <= 0f || crawlSeconds <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(downedSeconds), "击倒时长必须为正数");
            }

            if (crawlSpeedMultiplier <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(crawlSpeedMultiplier), "缓慢移动速度倍率必须为正数");
            }

            if (hitThrottleSeconds < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(hitThrottleSeconds), "节流窗口不可为负");
            }

            if (maxHealth <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maxHealth), "生命上限必须为正数");
            }

            if (hitPolicy == DownedHitPolicy.KnockdownOnly || hitPolicy == DownedHitPolicy.HealthDeath
                || hitPolicy == DownedHitPolicy.KnockdownThenDeath)
            {
                // 枚举值合法，继续。
            }
            else
            {
                throw new System.ArgumentOutOfRangeException(nameof(hitPolicy), "未知的挨打口径");
            }

            DownedSeconds = downedSeconds;
            CrawlSeconds = crawlSeconds;
            CrawlSpeedMultiplier = crawlSpeedMultiplier;
            ResetOnHitWhileDowned = resetOnHitWhileDowned;
            HitThrottleSeconds = hitThrottleSeconds;
            HitPolicy = hitPolicy;
            MaxHealth = maxHealth;
        }

        /// <summary>
        /// 工程占位默认值（**不是策划定稿**）：倒地 1.5 秒、挣扎 3 秒、移动 0.35 倍、
        /// 再挨打重置计时（0.5 秒节流）、折中口径（先击倒、血尽才死）、生命上限 3。
        /// </summary>
        public static KnockdownSettings PlaceholderDefault =>
            new KnockdownSettings(1.5f, 3f, 0.35f, true, 0.5f, DownedHitPolicy.KnockdownThenDeath, 3);
    }

    /// <summary>一个 tick 的击倒输入。</summary>
    public readonly struct KnockdownInput
    {
        private KnockdownInput(bool hit, bool inputAttack, bool inputMove, int health)
        {
            TakeHit = hit;
            InputAttack = inputAttack;
            InputMove = inputMove;
            Health = health;
        }

        /// <summary>本 tick 是否挨了一下。</summary>
        public bool TakeHit { get; }

        /// <summary>本 tick 玩家是否按了攻击键（用来验证「击倒期间不能攻击」）。</summary>
        public bool InputAttack { get; }

        /// <summary>本 tick 玩家是否有移动输入（用来算缓慢移动速度）。</summary>
        public bool InputMove { get; }

        /// <summary>当前生命。判 <see cref="DownedHitPolicy.HealthDeath"/> 用。</summary>
        public int Health { get; }

        /// <summary>什么都不发生的 tick（不挨打、不按攻击、不移动）。</summary>
        public static KnockdownInput Idle(int health) => new KnockdownInput(false, false, false, health);

        /// <summary>挨了一下；<paramref name="health"/> 传**扣血之后**的生命。</summary>
        public static KnockdownInput TakeHitNow(int health, bool attacking = false, bool moving = false) =>
            new KnockdownInput(true, attacking, moving, health);

        /// <summary>普通 tick（可能按着攻击 / 移动）。</summary>
        public static KnockdownInput Normal(int health, bool attacking, bool moving) =>
            new KnockdownInput(false, attacking, moving, health);
    }

    /// <summary>击倒状态机的查询结果，一帧一份。</summary>
    public readonly struct KnockdownStatus
    {
        public KnockdownStatus(
            KnockdownPhase phase,
            float elapsedInPhase,
            float remainingInPhase,
            bool canAttack,
            float moveSpeedMultiplier)
        {
            Phase = phase;
            ElapsedInPhase = elapsedInPhase;
            RemainingInPhase = remainingInPhase;
            CanAttack = canAttack;
            MoveSpeedMultiplier = moveSpeedMultiplier;
        }

        /// <summary>当前阶段。</summary>
        public KnockdownPhase Phase { get; }

        /// <summary>当前阶段已过时间（秒）。</summary>
        public float ElapsedInPhase { get; }

        /// <summary>当前阶段剩余时间（秒）；<see cref="KnockdownPhase.None"/> / <see cref="KnockdownPhase.Dead"/> 为 0。</summary>
        public float RemainingInPhase { get; }

        /// <summary>现在能不能攻击。击倒的两个阶段恒为 false（`03:85` R3）。</summary>
        public bool CanAttack { get; }

        /// <summary>移动速度倍率：正常 / 死亡 0，挣扎期是配置的缓慢倍率。</summary>
        public float MoveSpeedMultiplier { get; }

        /// <summary>是不是处于被击倒的两个阶段之一。</summary>
        public bool IsDowned => Phase == KnockdownPhase.Downed || Phase == KnockdownPhase.Crawling;

        /// <summary>是不是死了。</summary>
        public bool IsDead => Phase == KnockdownPhase.Dead;

        /// <summary>该写的瞬时事实键：`stealth.knockdown`（字典 §4.2，瞬时态不入档）。</summary>
        public string FactKey => StealthFactKeys.Knockdown;

        /// <summary>`stealth.knockdown` 的当前值。</summary>
        public bool FactValue => IsDowned;
    }

    /// <summary>
    /// 击倒状态机。纯计时 + 纯策略，不读 `Time`、不读输入设备、不认识 MonoBehaviour。
    /// <para>
    /// 两套口径都走这一个状态机：<see cref="DownedHitPolicy"/> 决定「血量尽时死不死」，
    /// 状态机的阶段推进逻辑只有一份，避免两套代码各自演化。
    /// </para>
    /// </summary>
    public sealed class KnockdownRules
    {
        private readonly KnockdownSettings settings;
        private KnockdownPhase phase;
        private float elapsedInPhase;
        private float timeSinceHit;
        private int knockdownCount;

        public KnockdownRules(KnockdownSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>当前生效的设置。</summary>
        public KnockdownSettings Settings => settings;

        /// <summary>当前阶段。</summary>
        public KnockdownPhase Phase => phase;

        /// <summary>本阶段（本局内，由调用方负责在阶段迁移时归零）累计被击倒次数。</summary>
        public int KnockdownCount => knockdownCount;

        /// <summary>是否处于被击倒的两个阶段之一。</summary>
        public bool IsDowned => phase == KnockdownPhase.Downed || phase == KnockdownPhase.Crawling;

        /// <summary>是否已死亡（只在会死的口径下为真）。</summary>
        public bool IsDead => phase == KnockdownPhase.Dead;

        /// <summary>
        /// 现在能不能攻击：被击倒 / 死亡期间**一律不能**（`03:85` R3）。
        /// <paramref name="inputAttack"/> 只是把玩家的输入一并报出来，输入为真也照样返回 false。
        /// </summary>
        public bool CanAttack(bool inputAttack)
        {
            if (phase == KnockdownPhase.Downed || phase == KnockdownPhase.Crawling || phase == KnockdownPhase.Dead)
            {
                return false;
            }

            return inputAttack;
        }

        /// <summary>移动速度倍率：正常 1、挣扎期按配置、倒地与死亡 0。</summary>
        public float MoveSpeedMultiplier
        {
            get
            {
                switch (phase)
                {
                    case KnockdownPhase.Crawling:
                        return settings.CrawlSpeedMultiplier;
                    case KnockdownPhase.Downed:
                    case KnockdownPhase.Dead:
                        return 0f;
                    default:
                        return 1f;
                }
            }
        }

        /// <summary>本阶段的剩余时间；无阶段的返回 0。</summary>
        public float RemainingInPhase
        {
            get
            {
                switch (phase)
                {
                    case KnockdownPhase.Downed:
                        return GameMath.Max(0f, settings.DownedSeconds - elapsedInPhase);
                    case KnockdownPhase.Crawling:
                        return GameMath.Max(0f, settings.CrawlSeconds - elapsedInPhase);
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>把当前状态打包成一份查询结果。</summary>
        public KnockdownStatus Status => new KnockdownStatus(phase, elapsedInPhase, RemainingInPhase, CanAttack(false), MoveSpeedMultiplier);

        /// <summary>清零（新一局、读档后重来）。累计击倒次数由调用方决定清不清（阶段迁移才清）。</summary>
        public void Reset(bool clearCount)
        {
            phase = KnockdownPhase.None;
            elapsedInPhase = 0f;
            timeSinceHit = 0f;
            if (clearCount)
            {
                knockdownCount = 0;
            }
        }

        /// <summary>
        /// 推进一步。<paramref name="deltaTime"/> 为负会抛——负步长是调用方的 bug，
        /// 静默吞掉会让回放对不齐（与 `MonsterRules.Step` 同一纪律，见 `MonsterRules.cs:137-140`）。
        /// </summary>
        public void Tick(in KnockdownInput input, float deltaTime)
        {
            if (deltaTime < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(deltaTime), "固定步长不可为负");
            }

            timeSinceHit += deltaTime;

            bool enteredOrResetDowned = false;
            if (input.TakeHit)
            {
                enteredOrResetDowned = ApplyHit(input.Health);
            }
            else if (IsDeadByHealth(input.Health))
            {
                // 口径选择「生命归零即死」时，血尽不一定来自这一 tick 的命中（可能是别的伤害源）。
                phase = KnockdownPhase.Dead;
                elapsedInPhase = 0f;
                return;
            }

            if (phase == KnockdownPhase.None || phase == KnockdownPhase.Dead)
            {
                return;
            }

            // 挨打的那一 tick 不推进倒地计时：被刚从正常状态打倒时，这一 tick 的剩余时间还没开始走
            // （否则倒地期会被当场吃掉一个 step，Boss 一 tick 一次命中就永远起不来）。
            // 状态机自己的推进（倒地 → 挣扎 → 起身）另算一步。
            if (enteredOrResetDowned)
            {
                elapsedInPhase = 0f;
                return;
            }

            elapsedInPhase += deltaTime;
            if (phase == KnockdownPhase.Downed && elapsedInPhase >= settings.DownedSeconds)
            {
                phase = KnockdownPhase.Crawling;
                elapsedInPhase = 0f;
            }
            else if (phase == KnockdownPhase.Crawling && elapsedInPhase >= settings.CrawlSeconds)
            {
                phase = KnockdownPhase.None;
                elapsedInPhase = 0f;
            }
        }

        /// <summary>
        /// 应用一次命中。返回 true 表示这一 tick 让玩家**进入或重置了倒地循环**
        /// （调用方据此决定这一 tick 不推进倒计时）。
        /// </summary>
        private bool ApplyHit(int health)
        {
            bool throttled = settings.HitThrottleSeconds > 0f && timeSinceHit < settings.HitThrottleSeconds;
            timeSinceHit = 0f;

            if ((settings.HitPolicy == DownedHitPolicy.HealthDeath
                    || settings.HitPolicy == DownedHitPolicy.KnockdownThenDeath) && health <= 0)
            {
                phase = KnockdownPhase.Dead;
                elapsedInPhase = 0f;
                return false;
            }

            if (IsDead)
            {
                // 已经死了就不再被击倒（死人不进入击倒循环）。
                return false;
            }

            if (IsDowned && !settings.ResetOnHitWhileDowned)
            {
                // 口径：击倒中再挨打不重置计时；但仍计数（节流窗口内的连环命中不重复计数）。
                if (!throttled)
                {
                    knockdownCount++;
                }

                return false;
            }

            if (IsDowned && throttled)
            {
                // 口径：重置计时，但节流窗口内的重复命中不重复计数，避免 Boss 连击把次数刷爆。
                return false;
            }

            knockdownCount++;
            phase = KnockdownPhase.Downed;
            elapsedInPhase = 0f;
            return true;
        }

        private bool IsDeadByHealth(int health)
        {
            if (health > 0)
            {
                return false;
            }

            return settings.HitPolicy == DownedHitPolicy.HealthDeath
                || settings.HitPolicy == DownedHitPolicy.KnockdownThenDeath;
        }

        /// <summary>
        /// 由累计击倒次数算出档位。阈值来自配置，**不进事实表**（`ai-docs/docs/story-facts.md` §6.4），
        /// 表里只写 `stealth.knockdownCount.low|mid|high` 三个布尔键。
        /// </summary>
        public KnockdownTier Tier(int lowMax, int midMax) =>
            StealthFacts.ResolveKnockdownTier(knockdownCount, lowMax, midMax);
    }
}
