// 职责：演出的纯规则阶段机——Idle → Playing ⇄ Holding → Finished；停顿确认、长按跳过计时、结果归类、播放计时与状态迁移埋点。
// 为什么新建（复用 → 扩展 → 新建）：PerformanceService 要接资源 / UI / 输入 / 相机，把阶段语义抽成不引 UnityEngine 的纯 C#
//   才能脱离容器单测（同 DialogueRules / LootRules 的分法）；DialogueRules 是对白推进机，语义不同且 Performance 不得依赖 Dialogue。
using System;
using Game.Core.Telemetry;

namespace Game.Performance
{
    /// <summary>
    /// 演出规则。不认识 Unity、不读输入：服务每帧把 unscaled 时间与按键状态喂进来。
    /// <para>
    /// 计时约定：<see cref="ElapsedSeconds"/> 只由 <see cref="Tick"/> 累计（Playing / Holding 期间，含停顿等待时间）；
    /// <see cref="TickSkip"/> 只管长按进度，不累计播放时长——不可跳过的演出不调它，时长照样要算。
    /// </para>
    /// <para>
    /// 埋点（模块 performance，状态迁移尺子）：started、hold_entered、hold_confirmed、skipped、ended；每帧的 Tick / TickSkip 不埋。
    /// </para>
    /// </summary>
    public sealed class PerformanceRules
    {
        /// <summary>埋点 skipped 的 source 取值：玩家长按满。</summary>
        public const string SkipSourceHold = "hold";

        /// <summary>埋点 skipped 的 source 取值：代码调用（回放 / 编辑器试播 / 触屏跳过按钮）。</summary>
        public const string SkipSourceCode = "code";

        private readonly ITelemetryScope telemetry;
        private PerformancePolicy policy;
        private float skipHeldSeconds;

        public PerformanceRules(ITelemetryScope telemetry)
        {
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>当前阶段。</summary>
        public PerformancePhase Phase { get; private set; } = PerformancePhase.Idle;

        /// <summary>结果；只在 <see cref="PerformancePhase.Finished"/> 时有值。</summary>
        public PerformanceOutcome? Outcome { get; private set; }

        /// <summary>当前（或最近一次）演出 id；从未开始时为 null。</summary>
        public string Id { get; private set; }

        /// <summary>当前演出的策略快照。</summary>
        public PerformancePolicy Policy => policy;

        /// <summary>长按跳过进度 0–1；松手、不可跳过、非播放阶段为 0。</summary>
        public float SkipProgress
        {
            get
            {
                if (!policy.IsValid || skipHeldSeconds <= 0f) return 0f;
                float ratio = skipHeldSeconds / policy.SkipHoldSeconds;
                return ratio >= 1f ? 1f : ratio;
            }
        }

        /// <summary>本次演出进入停顿的次数。</summary>
        public int HoldCount { get; private set; }

        /// <summary>本次演出累计播放秒数（unscaled，由 <see cref="Tick"/> 累计，含停顿等待）。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>是否在 Playing / Holding。</summary>
        public bool IsActive => Phase == PerformancePhase.Playing || Phase == PerformancePhase.Holding;

        /// <summary>开始一段演出：Idle / Finished → Playing，清零计数。</summary>
        /// <exception cref="ArgumentException"><paramref name="id"/> 为空，或 <paramref name="policy"/> 是未经构造的 default 值。</exception>
        /// <exception cref="InvalidOperationException">已有演出在 Playing / Holding。</exception>
        public void Start(string id, PerformancePolicy policy)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("演出 id 不能为空", nameof(id));
            if (!policy.IsValid) throw new ArgumentException("演出策略未初始化（default 值）", nameof(policy));
            if (IsActive) throw new InvalidOperationException($"演出 {Id} 正在进行，不能再开始 {id}");

            Id = id;
            this.policy = policy;
            Phase = PerformancePhase.Playing;
            Outcome = null;
            HoldCount = 0;
            ElapsedSeconds = 0f;
            skipHeldSeconds = 0f;
            telemetry.Track("started", ("id", id), ("skippable", policy.Skippable));
        }

        /// <summary>累计播放时间。只在 Playing / Holding 生效；非正值与非活动阶段忽略。</summary>
        public void Tick(float unscaledDelta)
        {
            if (!IsActive || !(unscaledDelta > 0f)) return;
            ElapsedSeconds += unscaledDelta;
        }

        /// <summary>时间轴走到 HoldMarker：Playing → Holding。其他阶段返回 false。</summary>
        public bool EnterHold()
        {
            if (Phase != PerformancePhase.Playing) return false;
            Phase = PerformancePhase.Holding;
            HoldCount++;
            telemetry.Track("hold_entered", ("id", Id), ("index", HoldCount), ("at", ElapsedSeconds));
            return true;
        }

        /// <summary>玩家确认继续：只在 Holding 有效（→ Playing），其他阶段返回 false。</summary>
        public bool Confirm()
        {
            if (Phase != PerformancePhase.Holding) return false;
            Phase = PerformancePhase.Playing;
            telemetry.Track("hold_confirmed", ("id", Id), ("index", HoldCount));
            return true;
        }

        /// <summary>
        /// 长按跳过计时。不可跳过或非活动阶段恒返回 false；<paramref name="held"/> 为 false 时进度归零；
        /// 累计达到 <see cref="PerformancePolicy.SkipHoldSeconds"/> 时置 Skipped + Finished 并返回 true（只返回一次）。
        /// </summary>
        public bool TickSkip(bool held, float unscaledDelta)
        {
            if (!IsActive || !policy.Skippable || !held)
            {
                skipHeldSeconds = 0f;
                return false;
            }
            if (unscaledDelta > 0f) skipHeldSeconds += unscaledDelta;
            if (skipHeldSeconds < policy.SkipHoldSeconds) return false;
            Finish(PerformanceOutcome.Skipped, SkipSourceHold);
            return true;
        }

        /// <summary>时间轴自然播完：Playing / Holding → Finished(Completed)。非活动阶段返回 false。</summary>
        public bool Complete() => Finish(PerformanceOutcome.Completed);

        /// <summary>立即跳过（代码触发，不经长按）；策略的 Skippable 只约束玩家输入，这里不看。非活动阶段返回 false。</summary>
        public bool Skip() => Finish(PerformanceOutcome.Skipped, SkipSourceCode);

        /// <summary>外部取消：→ Finished(Cancelled)。非活动阶段返回 false。</summary>
        public bool Cancel() => Finish(PerformanceOutcome.Cancelled);

        /// <summary>播放途中出错：→ Finished(Failed)。非活动阶段返回 false。</summary>
        public bool Fail() => Finish(PerformanceOutcome.Failed);

        private bool Finish(PerformanceOutcome outcome, string skipSource = null)
        {
            if (!IsActive) return false;
            if (outcome == PerformanceOutcome.Skipped)
                telemetry.Track("skipped", ("id", Id), ("at", ElapsedSeconds), ("source", skipSource ?? SkipSourceCode));
            Phase = PerformancePhase.Finished;
            Outcome = outcome;
            skipHeldSeconds = 0f;
            telemetry.Track("ended", ("id", Id), ("outcome", OutcomeName(outcome)), ("duration", ElapsedSeconds));
            return true;
        }

        /// <summary>结果的埋点取值（小写，稳定：改枚举名不改日志值）。</summary>
        public static string OutcomeName(PerformanceOutcome outcome)
        {
            switch (outcome)
            {
                case PerformanceOutcome.Completed: return "completed";
                case PerformanceOutcome.Skipped: return "skipped";
                case PerformanceOutcome.Cancelled: return "cancelled";
                default: return "failed";
            }
        }
    }
}
