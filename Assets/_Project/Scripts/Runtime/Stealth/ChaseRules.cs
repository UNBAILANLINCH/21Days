// 职责：追逐的三个判定点——触发追逐 / 摆脱 / 被抓的后果，以及「追兵速度必须追得动」的配置校验。
//
// 为什么新建：
// - 复用：Monster 现在只有「敌对后追向最后已知位置、跟丢 2 秒退回警戒」（`MonsterRules.Step`，
//   见 `Assets/_Project/Scripts/Runtime/Monster/MonsterRules.cs:155-174、235-246`），
//   没有「追逐」这个独立状态，也没有摆脱条件与失败后果；速度写死在 `MonsterConfig` 的倍率里。
// - 扩展：`MonsterConfig` / `MonsterRules` 本次任务明令不动（并行任务在改），且「被抓的后果」
//   原文未定（见下），塞进 Monster 会把未定值焊进主流程。
// - 新建：以上两条都不成立，故新建。
//
// 出处（真源）：
// - `docs/design/features-spotlight/04_追逐.md:63-90`（3.1 追逐从哪来，T1–T8 触发来源表）
// - `04:87-88`（R12 通用规则：感知后警戒、追击、攻击；R13 橙区警戒升满转敌对追击）
// - `04:101-106`（3.3 摆脱：R22 拾骨人篮子的「摆脱」、R23 失去目标 2 秒 / 6 秒回落、
//   R25「账簿、怀疑度、巡逻队怎么算摆脱，原文都没写」）
// - `04:108-112`（3.4 被抓的后果：R26 三处原文、R27「哪一条管追逐失败，原文没写」）
// - `04:165`（工程现状：追击速度 2.5 低于玩家步行 3，甩掉追兵毫无难度）
// - `04:192`（Q4 每种追逐的摆脱条件未定）、`04:190`（Q3 追逐失败的后果未定）
// - `00_功能总览.md:291`（§5 C6 背后处决、掩体范围差异）、`04:181`（§8 检验问题：追兵靠什么形成威胁）
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>追逐状态。</summary>
    public enum ChasePhase : byte
    {
        /// <summary>没在追。</summary>
        Idle = 0,

        /// <summary>正在追。</summary>
        Chasing = 1,

        /// <summary>追上了（后果由 <see cref="ChaseOutcomeSettings"/> 决定）。</summary>
        Caught = 2,
    }

    /// <summary>追逐触发条件。**数值全是占位**，原文只给了「丢目标 2 秒」一条。</summary>
    public readonly struct ChaseSettings
    {
        /// <summary>
        /// [待拍板] 额外触发半径：追兵与玩家距离超过它就不开始追（0 = 不限）。
        /// 占位 0，等 `04:192` Q4「摆脱条件」与 `00_功能总览.md:381` §8.1 #3 一起拍。
        /// </summary>
        public float TriggerRadius { get; }

        /// <summary>
        /// [已拍板] 跟丢多久算摆脱其一：2 秒，原文值（`04:104` R23 / mai 验收 3）。
        /// 默认从 `MonsterConfig.HostileLoseSeconds` 带入，两边必须是同一个数。
        /// </summary>
        public float LoseSightGraceSeconds { get; }

        /// <summary>
        /// [待拍板] 摆脱距离：拉开到这个距离并且满足其它条件才算摆脱。
        /// 占位 8（红区 2 / 橙区 6 之上再退一档），等 `04:192` Q4。
        /// </summary>
        public float EscapeDistance { get; }

        /// <summary>[待拍板] 追踪记忆时长：甩掉追兵后它还会找多久。占位 6，对应警戒回落 6 秒（`04:104` R23）。</summary>
        public float TrackMemorySeconds { get; }

        /// <summary>
        /// [待拍板] 摆脱是否要求「先跟丢」。占位 true：
        /// 「当着它的面跑远」不该算摆脱，先得断视线（`04:104` R23 的 2 秒失目标）。
        /// </summary>
        public bool EscapeRequiresLineOfSightLost { get; }

        public ChaseSettings(
            float triggerRadius,
            float loseSightGraceSeconds,
            float escapeDistance,
            float trackMemorySeconds,
            bool escapeRequiresLineOfSightLost)
        {
            if (triggerRadius < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(triggerRadius), "触发半径不可为负");
            }

            if (loseSightGraceSeconds <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(loseSightGraceSeconds), "跟丢宽限时长必须为正数");
            }

            if (escapeDistance <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(escapeDistance), "摆脱距离必须为正数");
            }

            if (trackMemorySeconds < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(trackMemorySeconds), "追踪记忆时长不可为负");
            }

            TriggerRadius = triggerRadius;
            LoseSightGraceSeconds = loseSightGraceSeconds;
            EscapeDistance = escapeDistance;
            TrackMemorySeconds = trackMemorySeconds;
            EscapeRequiresLineOfSightLost = escapeRequiresLineOfSightLost;
        }

        /// <summary>
        /// 工程占位默认值：触发不限距离、跟丢 2 秒（原文值）、摆脱距离 8、记忆 6 秒、摆脱必须先断视线。
        /// </summary>
        public static ChaseSettings PlaceholderDefault => new ChaseSettings(0f, 2f, 8f, 6f, true);
    }

    /// <summary>
    /// 玩家 / 追兵的移动速度基线。用来做「追兵追不追得动」的配置校验。
    /// <para>
    /// 现状问题（`04:165`）：怪物敌对速度 = `MonsterConfig.PatrolSpeed(2) × HostileSpeedMultiplier(1.25)`
    /// = 2.5，低于玩家步行 3、奔跑 5，所以「甩掉追兵毫无难度」。
    /// </para>
    /// </summary>
    public readonly struct ChaseBaseline
    {
        /// <summary>玩家步行速度（`PlayerConfig.MoveSpeed`）。</summary>
        public float PlayerWalkSpeed { get; }

        /// <summary>玩家奔跑速度（`PlayerConfig.RunSpeed`）。</summary>
        public float PlayerRunSpeed { get; }

        public ChaseBaseline(float playerWalkSpeed, float playerRunSpeed)
        {
            if (playerWalkSpeed <= 0f || playerRunSpeed <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(playerWalkSpeed), "玩家速度基线必须为正数");
            }

            PlayerWalkSpeed = playerWalkSpeed;
            PlayerRunSpeed = playerRunSpeed;
        }

        /// <summary>工程现值：`PlayerConfig` 的步行 3 / 奔跑 5（`PlayerConfig.cs:9-12`）。</summary>
        public static ChaseBaseline ProjectCurrent => new ChaseBaseline(3f, 5f);
    }

    /// <summary>被拒原因。用严重度分开：秒杀级（配置错误 / 状态非法）与警告级（速度追不动）。</summary>
    public enum ChaseReject : byte
    {
        /// <summary>没被拒。</summary>
        None = 0,

        /// <summary>追兵不在感知状态，不该起追。</summary>
        NotAware = 1,

        /// <summary>追兵已经死了。</summary>
        PursuerNotAlive = 2,

        /// <summary>超出触发半径。</summary>
        OutOfTriggerRadius = 3,

        /// <summary>配置错误：追兵速度不高于玩家步行，永远追不上（`04:165` 的现状问题）。</summary>
        TooSlowToCatch = 4,

        /// <summary>配置错误：追兵速度高于玩家奔跑，追逐变成必死，没有操作空间。</summary>
        TooFastToBeFair = 7,

        /// <summary>配置错误：跟丢宽限时长为 0 或负数。</summary>
        InvalidGrace = 5,

        /// <summary>已经在追了。</summary>
        AlreadyChasing = 6,
    }

    /// <summary>判定被拒时怎么处理：配置错误必须当场炸，玩法条件不满足只是「这次不起追」。</summary>
    public enum ChaseRejectSeverity : byte
    {
        /// <summary>玩法条件不满足，正常返回，不写键。</summary>
        Soft = 0,

        /// <summary>配置 / 状态非法，必须当场抛异常，不许静默跑。</summary>
        Fatal = 1,
    }

    /// <summary>追逐触发判定的结果。</summary>
    public readonly struct ChaseTriggerVerdict
    {
        public ChaseTriggerVerdict(bool triggered, ChaseReject reject, ChaseRejectSeverity severity)
        {
            Triggered = triggered;
            Reject = reject;
            Severity = severity;
        }

        /// <summary>这一 tick 要不要起追。</summary>
        public bool Triggered { get; }

        /// <summary>被拒原因。</summary>
        public ChaseReject Reject { get; }

        /// <summary>被拒的严重度；<see cref="ChaseRejectSeverity.Fatal"/> 时调用方应抛。</summary>
        public ChaseRejectSeverity Severity { get; }

        /// <summary>起追该写的键：`chase.active`（字典 §4.3，写入方日记为 Chase / Monster）。</summary>
        public string FactKey => Triggered ? StealthFactKeys.ChaseActive : string.Empty;

        /// <summary>起追时同时要写的「这是脚本化固定追逐」键；本判定不管固定追逐，恒空。</summary>
        public string FixedFactKey => string.Empty;
    }

    /// <summary>追逐触发判定的输入。</summary>
    public readonly struct ChaseTriggerInput
    {
        public ChaseTriggerInput(Vector2 pursuerPosition, Vector2 playerPosition, bool pursuerAlive, bool pursuerAware)
        {
            PursuerPosition = pursuerPosition;
            PlayerPosition = playerPosition;
            PursuerAlive = pursuerAlive;
            PursuerAware = pursuerAware;
        }

        /// <summary>追兵位置。</summary>
        public Vector2 PursuerPosition { get; }

        /// <summary>玩家位置。</summary>
        public Vector2 PlayerPosition { get; }

        /// <summary>追兵还活着。</summary>
        public bool PursuerAlive { get; }

        /// <summary>追兵已经察觉玩家（警戒或敌对；红区直接敌对见 `04:87` R12）。</summary>
        public bool PursuerAware { get; }
    }

    /// <summary>追逐每个 tick 的输入。</summary>
    public readonly struct ChaseInput
    {
        public ChaseInput(bool pursuerSeesTarget, float distance, bool targetAlive, bool pursuerAlive)
        {
            PursuerSeesTarget = pursuerSeesTarget;
            Distance = distance;
            TargetAlive = targetAlive;
            PursuerAlive = pursuerAlive;
        }

        /// <summary>追兵这一 tick 是否看得见玩家（含视线遮挡判定的结果）。</summary>
        public bool PursuerSeesTarget { get; }

        /// <summary>追兵与玩家的距离。</summary>
        public float Distance { get; }

        /// <summary>玩家还活着。</summary>
        public bool TargetAlive { get; }

        /// <summary>追兵还活着。</summary>
        public bool PursuerAlive { get; }
    }

    /// <summary>追逐每个 tick 的结果。</summary>
    public readonly struct ChaseVerdict
    {
        public ChaseVerdict(ChasePhase phase, bool escapedThisTick, bool caughtThisTick, float graceAccumulated)
        {
            Phase = phase;
            EscapedThisTick = escapedThisTick;
            CaughtThisTick = caughtThisTick;
            GraceAccumulated = graceAccumulated;
        }

        /// <summary>推进后的状态。</summary>
        public ChasePhase Phase { get; }

        /// <summary>这一 tick 是否刚刚达成摆脱（调用方写 `chase.escaped`）。</summary>
        public bool EscapedThisTick { get; }

        /// <summary>这一 tick 是否刚刚被追上（调用方写 `chase.caught`，后果见 `ChaseCaughtRules`）。</summary>
        public bool CaughtThisTick { get; }

        /// <summary>累计的「没看见」时长（秒），摆脱判定看的量。</summary>
        public float GraceAccumulated { get; }

        /// <summary>`chase.active` 的当前值。</summary>
        public bool ActiveFactValue => Phase == ChasePhase.Chasing;

        /// <summary>`chase.escaped` 这一 tick 该不该写。</summary>
        public string EscapedFactKey => EscapedThisTick ? StealthFactKeys.ChaseEscaped : string.Empty;

        /// <summary>`chase.caught` 这一 tick 该不该写。</summary>
        public string CaughtFactKey => CaughtThisTick ? StealthFactKeys.ChaseCaught : string.Empty;
    }

    /// <summary>
    /// 追逐规则：起追判定 + 每 tick 推进（含摆脱判定）。被抓的后果交给 <see cref="ChaseCaughtRules"/>。
    /// </summary>
    public sealed class ChaseRules
    {
        private readonly ChaseSettings settings;
        private readonly ChaseBaseline baseline;
        private float chaseSpeed;
        private float grace;
        private int escapeCount;

        public ChaseRules(ChaseSettings settings, ChaseBaseline baseline)
        {
            this.settings = settings;
            this.baseline = baseline;
            chaseSpeed = 0f;
        }

        /// <summary>当前生效的设置。</summary>
        public ChaseSettings Settings => settings;

        /// <summary>速度基线。</summary>
        public ChaseBaseline Baseline => baseline;

        /// <summary>本阶段成功摆脱过的次数。</summary>
        public int EscapeCount => escapeCount;

        /// <summary>是不是已经成功摆脱过至少一次（对应 `chase.escaped`）。</summary>
        public bool HasEscaped => escapeCount > 0;

        /// <summary>当前追兵速度（没配过就是 0，<see cref="TryStart"/> 会拒绝起追）。</summary>
        public float ChaseSpeed => chaseSpeed;

        /// <summary>
        /// 配置追兵速度。**必须高于玩家步行**（`04:165` 的现状问题：2.5 &lt; 3）。
        /// 这条校验故意放在配置入口而不是判定里：速度填错是接线错误，
        /// 让它在下一次起追时以 <see cref="ChaseReject.TooSlowToCatch"/> 暴露，而不是静默地追不上。
        /// </summary>
        public void ConfigureChaseSpeed(float speed)
        {
            if (speed < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(speed), "追兵速度不可为负");
            }

            chaseSpeed = speed;
        }

        /// <summary>
        /// 只做「追兵追不追得动」的校验：速度必须高于玩家步行、且不高于玩家奔跑。
        /// <para>
        /// 上限取「不高于奔跑」而不是「高于奔跑」：追兵比玩家奔跑还快会让追逐变成必死，
        /// 玩家就没有操作空间了（`04:175` 约束 1「要重到让人怕，又不能一抓就死」）。
        /// 这两个数都是**直觉值**，原文没写追兵移速（`04:143`）。
        /// </para>
        /// </summary>
        public ChaseReject VerifyChaseSpeed(float speed)
        {
            if (speed <= baseline.PlayerWalkSpeed)
            {
                return ChaseReject.TooSlowToCatch;
            }

            return speed > baseline.PlayerRunSpeed ? ChaseReject.TooFastToBeFair : ChaseReject.None;
        }

        /// <summary>按当前速度校验一遍（速度没配过时视为配置错误）。</summary>
        public ChaseReject VerifyChaseSpeed() => chaseSpeed <= 0f ? ChaseReject.TooSlowToCatch : VerifyChaseSpeed(chaseSpeed);

        /// <summary>
        /// 触发判定。<see cref="ChaseRejectSeverity.Fatal"/> 的拒绝必须由调用方当场抛异常
        /// （配置错误不许静默跑）；<see cref="ChaseRejectSeverity.Soft"/> 的正常返回。
        /// </summary>
        public ChaseTriggerVerdict TryStart(in ChaseTriggerInput input, ChasePhase current)
        {
            if (current == ChasePhase.Chasing)
            {
                return new ChaseTriggerVerdict(false, ChaseReject.AlreadyChasing, ChaseRejectSeverity.Soft);
            }

            if (!input.PursuerAlive)
            {
                return new ChaseTriggerVerdict(false, ChaseReject.PursuerNotAlive, ChaseRejectSeverity.Soft);
            }

            if (!input.PursuerAware)
            {
                return new ChaseTriggerVerdict(false, ChaseReject.NotAware, ChaseRejectSeverity.Soft);
            }

            if (settings.LoseSightGraceSeconds <= 0f)
            {
                return new ChaseTriggerVerdict(false, ChaseReject.InvalidGrace, ChaseRejectSeverity.Fatal);
            }

            if (settings.TriggerRadius > 0f
                && GameMath.Distance(input.PursuerPosition, input.PlayerPosition) > settings.TriggerRadius)
            {
                return new ChaseTriggerVerdict(false, ChaseReject.OutOfTriggerRadius, ChaseRejectSeverity.Soft);
            }

            ChaseReject speedReject = VerifyChaseSpeed();
            if (speedReject != ChaseReject.None)
            {
                return new ChaseTriggerVerdict(false, speedReject, ChaseRejectSeverity.Fatal);
            }

            grace = 0f;
            return new ChaseTriggerVerdict(true, ChaseReject.None, ChaseRejectSeverity.Soft);
        }

        /// <summary>
        /// 推进一 tick。返回这一 tick 之后的状态与两个瞬时事件（摆脱 / 被抓）。
        /// 摆脱条件：连续「没看见玩家」达到 <see cref="ChaseSettings.LoseSightGraceSeconds"/>，
        /// 并且拉开到 <see cref="ChaseSettings.EscapeDistance"/>（要求断视线时还得先满足前者）。
        /// </summary>
        public ChaseVerdict Tick(in ChaseInput input, ChasePhase current, float deltaTime)
        {
            if (deltaTime < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(deltaTime), "固定步长不可为负");
            }

            if (input.Distance < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(input), "距离不可为负");
            }

            if (current != ChasePhase.Chasing)
            {
                return new ChaseVerdict(ChasePhase.Idle, false, false, grace);
            }

            if (!input.TargetAlive || !input.PursuerAlive)
            {
                // 一方没了，追逐结束（死人不追、也不被追）。
                return new ChaseVerdict(ChasePhase.Idle, false, false, grace);
            }

            if (input.PursuerSeesTarget)
            {
                grace = 0f;
                return new ChaseVerdict(ChasePhase.Chasing, false, false, grace);
            }

            grace += deltaTime;
            bool farEnough = input.Distance >= settings.EscapeDistance;
            bool graceEnough = grace >= settings.LoseSightGraceSeconds;
            bool escaped = settings.EscapeRequiresLineOfSightLost ? graceEnough && farEnough : farEnough;

            if (escaped)
            {
                escapeCount++;
                grace = 0f;
                return new ChaseVerdict(ChasePhase.Idle, true, false, 0f);
            }

            return new ChaseVerdict(ChasePhase.Chasing, false, false, grace);
        }

        /// <summary>记一次「被抓」（后果由 <see cref="ChaseCaughtRules"/> 决定）。</summary>
        public ChaseVerdict Caught(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(deltaTime), "固定步长不可为负");
            }

            grace = 0f;
            return new ChaseVerdict(ChasePhase.Caught, false, true, grace);
        }

        /// <summary>清零（新一局）。阶段计数是否清零由调用方决定。</summary>
        public void Reset(bool clearEscapeCount)
        {
            grace = 0f;
            if (clearEscapeCount)
            {
                escapeCount = 0;
            }
        }

        /// <summary>被拒原因的中文描述，给调试面板 / 测试失败信息用。</summary>
        public static string Describe(ChaseReject reject)
        {
            switch (reject)
            {
                case ChaseReject.None:
                    return "可以起追";
                case ChaseReject.NotAware:
                    return "追兵还没察觉玩家";
                case ChaseReject.PursuerNotAlive:
                    return "追兵已经死了";
                case ChaseReject.OutOfTriggerRadius:
                    return "超出触发半径";
                case ChaseReject.TooSlowToCatch:
                    return "追兵速度不高于玩家步行，永远追不上";
                case ChaseReject.InvalidGrace:
                    return "跟丢宽限时长为 0 或负数";
                case ChaseReject.AlreadyChasing:
                    return "已经在追了";
                default:
                    return "未知原因";
            }
        }
    }
}
