// 职责：背后暗杀 / 绕背判定。纯几何 + 三个布尔前提，不碰场景、不碰动画、不碰输入。
//
// 为什么新建：
// - 复用：工程里没有任何「绕背」判定。Monster 的感知只看距离与夹角（`MonsterRules.Sense`，
//   见 `Assets/_Project/Scripts/Runtime/Monster/MonsterRules.cs:318-340`），没有「目标背对我」这一说；
//   Player 的 `PlayerRules` 只管移动与攻击，也不认识目标朝向。
// - 扩展：Monster 的规则类正在被并行任务改，且暗杀是「玩家对怪物」的动作，硬塞进任一侧都会让
//   两个模块互相认识对方的私有状态。本模块用「只吃快照结构」的方式解耦。
// - 新建：以上两条都不成立，故新建。
//
// 出处（真源）：
// - `docs/design/features-spotlight/03_潜行与暗杀.md:90`（R5「暗杀 = 绕到怪物背后处决」）
// - `03:110-114`（R11–R13：柜台后无法绕背；阶段六站背后反而更可疑）
// - `03:177`（背后近距半径原文没写）、`03:236`（Q9 暗杀交互未定）
// - `00_功能总览.md:289`（§5 C4 本体打不过怪 vs 玩家可攻击怪）、`00:383`（§8.1 #5 暗杀放哪一版）
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>暗杀被拒的原因。测试与调试用，也给策划看「为什么这一刀不行」。</summary>
    public enum AssassinationReject : byte
    {
        /// <summary>没被拒（判定通过）。</summary>
        None = 0,

        /// <summary>距离超过 <see cref="AssassinationSettings.MaxDistance"/>。</summary>
        OutOfRange = 1,

        /// <summary>不在目标背后的锥内（角度超过 <see cref="AssassinationSettings.RearAngle"/> 的一半）。</summary>
        NotBehind = 2,

        /// <summary>目标已经察觉（警戒或敌对），不吃背后处决。</summary>
        TargetAware = 3,

        /// <summary>目标已经死了 / 不处于可被处决的状态。</summary>
        TargetNotAlive = 4,

        /// <summary>要求潜行而攻方没有潜行。</summary>
        NotSneaking = 5,

        /// <summary>攻方与目标重合（距离为零，朝向没有意义）。</summary>
        SamePosition = 6,
    }

    /// <summary>
    /// 暗杀的三个阈值。角度是「目标背后锥」的**总张角**，与 `MonsterConfig.VisionAngle` 同口径
    /// （75° 指前方扇区总张角，见 `MonsterRules.cs:40`），这样策划看两个数时不用换算。
    /// </summary>
    public readonly struct AssassinationSettings
    {
        /// <summary>
        /// 背后锥总张角（度）。180 = 整个后半平面；越接近 0 越要求正后方。
        /// <para>
        /// [待拍板] 占位 120，等 `00_功能总览.md:383` §8.1 #5（暗杀放哪一版、交互是什么）；
        /// 相关未定条：`03_潜行与暗杀.md:236` Q9、`03:224` 检查问题「背后可以下手的范围有多大」。
        /// </para>
        /// </summary>
        public float RearAngle { get; }

        /// <summary>
        /// 暗杀距离上限。[待拍板] 占位 1.2，与背后近距察觉 1.5（`MonsterConfig.nearSenseRadius`）
        /// 同量级但略小——先能贴到背后才能下手。等 `03_潜行与暗杀.md:177`「红区、橙区、背后近距的半径
        /// 原文没写」与 `00_功能总览.md:383` §8.1 #5 一起拍。
        /// </summary>
        public float MaxDistance { get; }

        /// <summary>
        /// 是否要求攻方处于潜行。[待拍板] 占位 false：原文只说「绕到背后」（`03:90` R5），
        /// 没说必须潜行；潜行在 mai 里免的是「背后近距察觉」（`03:121` R17），不是暗杀前置。
        /// </summary>
        public bool RequiresSneak { get; }

        public AssassinationSettings(float rearAngle, float maxDistance, bool requiresSneak)
        {
            if (rearAngle <= 0f || rearAngle > 180f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(rearAngle), "背后锥张角必须在 (0, 180] 度之间");
            }

            if (maxDistance <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maxDistance), "暗杀距离必须为正数");
            }

            RearAngle = rearAngle;
            MaxDistance = maxDistance;
            RequiresSneak = requiresSneak;
        }

        /// <summary>工程占位默认值（不是策划定稿）：背后 120°、距离 1.2、不要求潜行。</summary>
        public static AssassinationSettings PlaceholderDefault => new AssassinationSettings(120f, 1.2f, false);
    }

    /// <summary>
    /// 暗杀判定要的攻方 / 目标公开状态。
    /// <para>
    /// 刻意只收位置、朝向与几个布尔：Monster / Player 侧将来把 `MonsterModel`、`PlayerModel` 里的
    /// 对应字段填进来即可，本模块不认识它们的类型，也就不怕它们改内部结构。
    /// </para>
    /// </summary>
    public readonly struct AssassinationInput
    {
        public AssassinationInput(
            Vector2 attackerPosition,
            Vector2 attackerFacing,
            Vector2 targetPosition,
            Vector2 targetFacing,
            bool targetAlive,
            bool targetAware,
            bool attackerSneaking)
        {
            AttackerPosition = attackerPosition;
            AttackerFacing = attackerFacing;
            TargetPosition = targetPosition;
            TargetFacing = targetFacing;
            TargetAlive = targetAlive;
            TargetAware = targetAware;
            AttackerSneaking = attackerSneaking;
        }

        /// <summary>攻方（玩家本体）位置。</summary>
        public Vector2 AttackerPosition { get; }

        /// <summary>攻方朝向（只需方向，长度不限）。</summary>
        public Vector2 AttackerFacing { get; }

        /// <summary>目标（怪物）位置。</summary>
        public Vector2 TargetPosition { get; }

        /// <summary>目标朝向（只需方向，长度不限）。这是「背后」的参照。</summary>
        public Vector2 TargetFacing { get; }

        /// <summary>目标还活着（死了就没有处决可言）。</summary>
        public bool TargetAlive { get; }

        /// <summary>
        /// 目标已经察觉（警戒 Alert 或敌对 Hostile）。
        /// 口径由接线侧从 `MonsterMode` 填：`PatrolWalk` / `PatrolPause` = false，其余 = true。
        /// </summary>
        public bool TargetAware { get; }

        /// <summary>攻方处于潜行（`PlayerSnapshot.IsSneaking`）。</summary>
        public bool AttackerSneaking { get; }
    }

    /// <summary>暗杀判定结果。<see cref="Allowed"/> 为 false 时看 <see cref="Reject"/> 知道差在哪。</summary>
    public readonly struct AssassinationVerdict
    {
        public AssassinationVerdict(bool allowed, AssassinationReject reject)
        {
            Allowed = allowed;
            Reject = reject;
        }

        /// <summary>这一刀能不能下。</summary>
        public bool Allowed { get; }

        /// <summary>被拒原因；<see cref="AssassinationReject.None"/> 表示通过。</summary>
        public AssassinationReject Reject { get; }

        /// <summary>通过时该写的事实键（`stealth.assassinated`），被拒时空串。见 `ai-docs/docs/story-facts.md` §4.2。</summary>
        public string FactKey => Allowed ? StealthFactKeys.Assassinated : string.Empty;

        /// <summary>被拒时该写的事实键：没有——字典里没有「暗杀被拒」这种键，不要自己加。</summary>
        public string RejectFactKey => string.Empty;
    }

    /// <summary>
    /// 暗杀规则的纯函数集合。所有判定都能单独调，方便策划逐条对参数。
    /// </summary>
    public sealed class AssassinationRules
    {
        private readonly AssassinationSettings settings;
        private readonly float rearCosine;

        public AssassinationRules(AssassinationSettings settings)
        {
            this.settings = settings;
            rearCosine = GameMath.Cos(settings.RearAngle * 0.5f * 0.01745329252f);
        }

        /// <summary>当前生效的阈值。</summary>
        public AssassinationSettings Settings => settings;

        /// <summary>
        /// 攻方是否在目标背后的锥内。
        /// <para>
        /// 判据：以目标位置为顶点，把「目标朝向的反方向」当成锥心，攻方方向与锥心的夹角
        /// 不超过 <see cref="AssassinationSettings.RearAngle"/> 的一半。
        /// 攻方与目标重合（距离为零）时返回 false——朝向没有意义（对应 <see cref="AssassinationReject.SamePosition"/>）。
        /// </para>
        /// </summary>
        public bool IsBehind(Vector2 attackerPosition, Vector2 targetPosition, Vector2 targetFacing)
        {
            Vector2 toAttacker = attackerPosition - targetPosition;
            if (GameMath.SqrMagnitude(toAttacker) <= 0f)
            {
                return false;
            }

            Vector2 facing = GameMath.Normalize(targetFacing);
            if (GameMath.SqrMagnitude(facing) <= 0f)
            {
                // 目标朝向是零向量 = 朝向未知，任何方向都不算「背后」。
                return false;
            }

            Vector2 rear = -facing;
            Vector2 toAttackerDirection = GameMath.Normalize(toAttacker);
            return GameMath.Dot(toAttackerDirection, rear) >= rearCosine;
        }

        /// <summary>攻方是否在暗杀距离内（含边界；距离为零也算在范围内，但会因同位置被拒）。</summary>
        public bool IsInRange(Vector2 attackerPosition, Vector2 targetPosition) =>
            GameMath.Distance(attackerPosition, targetPosition) <= settings.MaxDistance;

        /// <summary>目标是否处于「未察觉」：活着且不在警戒 / 敌对。</summary>
        public bool IsTargetUnaware(in AssassinationInput input) => input.TargetAlive && !input.TargetAware;

        /// <summary>
        /// 完整判定：背后 + 距离 + 目标未察觉（+ 可选的潜行要求）。
        /// 判定顺序固定为「活着 → 未察觉 → 同位置 → 距离 → 背后 → 潜行」，
        /// 这样被拒原因永远指向最先不满足的那一条，测试与调试都能指名道姓。
        /// </summary>
        public AssassinationVerdict Evaluate(in AssassinationInput input)
        {
            if (!input.TargetAlive)
            {
                return new AssassinationVerdict(false, AssassinationReject.TargetNotAlive);
            }

            if (input.TargetAware)
            {
                return new AssassinationVerdict(false, AssassinationReject.TargetAware);
            }

            Vector2 toTarget = input.TargetPosition - input.AttackerPosition;
            if (GameMath.SqrMagnitude(toTarget) <= 0f)
            {
                return new AssassinationVerdict(false, AssassinationReject.SamePosition);
            }

            if (!IsInRange(input.AttackerPosition, input.TargetPosition))
            {
                return new AssassinationVerdict(false, AssassinationReject.OutOfRange);
            }

            if (!IsBehind(input.AttackerPosition, input.TargetPosition, input.TargetFacing))
            {
                return new AssassinationVerdict(false, AssassinationReject.NotBehind);
            }

            if (settings.RequiresSneak && !input.AttackerSneaking)
            {
                return new AssassinationVerdict(false, AssassinationReject.NotSneaking);
            }

            return new AssassinationVerdict(true, AssassinationReject.None);
        }

        /// <summary>
        /// 「绕背」判定：与 <see cref="Evaluate"/> 同源，但不问目标察觉到没有。
        /// 给附身复用（`03_潜行与暗杀.md:110-113` R11、R12：附身与暗杀可能共用一套绕背交互），
        /// 也用来写 `stealth.behind` 这个瞬时键。
        /// </summary>
        public bool IsBehindAndInRange(Vector2 attackerPosition, Vector2 targetPosition, Vector2 targetFacing) =>
            IsBehind(attackerPosition, targetPosition, targetFacing) && IsInRange(attackerPosition, targetPosition);

        /// <summary>把被拒原因翻译成一句中文，给调试面板 / 测试失败信息用。</summary>
        public static string Describe(AssassinationReject reject)
        {
            switch (reject)
            {
                case AssassinationReject.None:
                    return "可以处决";
                case AssassinationReject.OutOfRange:
                    return "不在暗杀距离内";
                case AssassinationReject.NotBehind:
                    return "不在背后锥内";
                case AssassinationReject.TargetAware:
                    return "目标已经察觉";
                case AssassinationReject.TargetNotAlive:
                    return "目标不处于可处决状态";
                case AssassinationReject.NotSneaking:
                    return "未潜行";
                case AssassinationReject.SamePosition:
                    return "与目标重合，朝向无意义";
                default:
                    return "未知原因";
            }
        }
    }
}
