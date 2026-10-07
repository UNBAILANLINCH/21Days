// 职责：怀疑度——阶段六的累计量，上限、档位阈值与「只增 / 可回落」两种口径都由构造参数注入。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有累计量设施；Monster 的警戒值是「每只怪物各一份」的逐帧感知量
//      （`02_身份暴露与怀疑.md:170` 数值表写明归每只怪物），怀疑度归玩家且跨怪累积，两者不同物。
//   2. 扩展不行：不能塞进 `IdentityState`——怀疑度的涨落与身份的借还有各自的时机，
//      合在一起后「露馅清状态」会顺手把怀疑度也清掉（原文没说露馅会清怀疑度）。
// 埋点：只在档位跨档时记一条（low / mid / high / none），不在每次 Add 上记——排队与站位会高频加怀疑度。

using System;
using Game.Core.Telemetry;

namespace Game.Identity
{
    /// <summary>
    /// 怀疑度（阶段六 分福祠）。原文：灰衣收入会检查玩家手中「福事」的数量来累计怀疑度，
    /// 到达上限后触发追逐战——<c>docs/design/features-spotlight/02_身份暴露与怀疑.md:125</c>（R24）。
    /// <para>
    /// <b>两种口径都在</b>：<see cref="CanDecay"/> 为 false 时是「只增」口径（只涨不落），
    /// 为 true 时按 <see cref="DecayPerSecond"/> 回落。原文明确没写会不会自然回落
    /// （`02_身份暴露与怀疑.md:127` R26），待拍板 <c>00_功能总览.md:387</c> §8.1 #9，
    /// 所以本类<b>不硬编</b>任何一种：开关、速率、档位阈值都从 <see cref="IdentitySettings"/> 构造进来。
    /// </para>
    /// <para>
    /// 上限同理由外部给（<see cref="IdentitySettings.SuspicionLimit"/>）；<b>≤ 0 表示关闭判定</b>，
    /// <see cref="IsAtLimit"/> 恒为 false。
    /// </para>
    /// </summary>
    public sealed class SuspicionState
    {
        private readonly float limit;
        private readonly bool canDecay;
        private readonly float decayPerSecond;
        private readonly float lowThreshold;
        private readonly float midThreshold;
        private readonly float highThreshold;
        private readonly ITelemetryScope telemetry;

        /// <summary>只要数值、不要档位与埋点（测试与纯算术场景）。此路径下 <see cref="Tier"/> 恒为 <see cref="FactTier.None"/>。</summary>
        public SuspicionState(float limit, bool canDecay, float decayPerSecond)
            : this(limit, canDecay, decayPerSecond, 0f, 0f, 0f, null)
        {
        }

        /// <summary>按数值块构造（上限、回落开关、速率、三档阈值一次取全）。</summary>
        public SuspicionState(IdentitySettings settings, ITelemetryScope telemetry = null)
            : this(
                (settings ?? throw new ArgumentNullException(nameof(settings))).SuspicionLimit,
                settings.SuspicionCanDecay,
                settings.SuspicionDecayPerSecond,
                settings.SuspicionLowThreshold,
                settings.SuspicionMidThreshold,
                settings.SuspicionHighThreshold,
                telemetry)
        {
        }

        /// <summary>全参构造。</summary>
        public SuspicionState(
            float limit,
            bool canDecay,
            float decayPerSecond,
            float lowThreshold,
            float midThreshold,
            float highThreshold,
            ITelemetryScope telemetry = null)
        {
            if (decayPerSecond < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(decayPerSecond), "回落速率不能为负；「不回落」请把 canDecay 传 false。");
            }

            this.limit = limit;
            this.canDecay = canDecay;
            this.decayPerSecond = decayPerSecond;
            this.lowThreshold = lowThreshold;
            this.midThreshold = midThreshold;
            this.highThreshold = highThreshold;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>由数值块构造（与构造函数等价，便于调用点读起来是「按配置建」）。</summary>
        public static SuspicionState FromSettings(IdentitySettings settings, ITelemetryScope telemetry = null)
            => new SuspicionState(settings, telemetry);

        /// <summary>当前怀疑度。</summary>
        public float Value { get; private set; }

        /// <summary>上限；≤ 0 = 关闭判定。</summary>
        public float Limit => limit;

        /// <summary>是否开启回落口径。</summary>
        public bool CanDecay => canDecay;

        /// <summary>回落速率（点 / 秒）。</summary>
        public float DecayPerSecond => decayPerSecond;

        /// <summary>是否已经到上限。<see cref="Limit"/> ≤ 0 时恒为 false（关闭判定）。</summary>
        public bool IsAtLimit => limit > 0f && Value >= limit;

        /// <summary>
        /// 当前档位（阈值来自配置）。剧情事实键 `identity.suspicion.low|mid|high` 与埋点都用它，
        /// 保证「同一时刻只有一个档为真」。
        /// </summary>
        public FactTier Tier => FactTierMath.TierOf(Value, lowThreshold, midThreshold, highThreshold);

        /// <summary>
        /// 累加怀疑度（到上限即封顶，不溢出）。<b>负值抛异常</b>：回落只走 <see cref="Advance"/>，
        /// 那条路受 <see cref="CanDecay"/> 管；让 Add 也能减，就等于偷偷开了一条不受开关控制的回落路径。
        /// </summary>
        public void Add(float amount)
        {
            if (amount < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "怀疑度只增；回落请用 Advance，它受「可回落」开关控制。");
            }

            if (amount == 0f)
            {
                return;
            }

            FactTier before = Tier;
            Value += amount;
            if (limit > 0f && Value > limit)
            {
                Value = limit;
            }

            TrackTierChange(before);
        }

        /// <summary>
        /// 按时间推进。<b>只增口径下什么都不做</b>（返回 false），可回落口径下按速率下降到 0 为止。
        /// 返回是否真的发生了变化，便于接线方决定要不要重算档位事实键。
        /// </summary>
        public bool Advance(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (!canDecay || decayPerSecond <= 0f || Value <= 0f || deltaTime <= 0f)
            {
                return false;
            }

            FactTier before = Tier;
            float previous = Value;
            Value -= decayPerSecond * deltaTime;
            if (Value < 0f)
            {
                Value = 0f;
            }

            TrackTierChange(before);
            return Value != previous;
        }

        /// <summary>清零（读档、重开本场时用；原文没写能不能降，见 `02:127` R26）。</summary>
        public void Reset()
        {
            FactTier before = Tier;
            Value = 0f;
            TrackTierChange(before);
        }

        /// <summary>
        /// 从存档恢复（由 <see cref="IdentityRules.Restore"/> 调用）。
        /// <b>不埋点</b>：读档不是玩家行为，记一条只会让「跨档」这件事在日志里看起来像刚发生。
        /// </summary>
        internal void RestoreValue(float value)
        {
            Value = value < 0f ? 0f : value;
            if (limit > 0f && Value > limit)
            {
                Value = limit;
            }
        }

        /// <summary>档位真的变了才记一条（事件名 <c>suspicion_tier</c>，属性 <c>tier</c> 与 <c>value</c>）。</summary>
        private void TrackTierChange(FactTier before)
        {
            FactTier now = Tier;
            if (now == before)
            {
                return;
            }

            string name = now == FactTier.None ? "none" : FactTierMath.SuffixOf(now);
            telemetry.Track("suspicion_tier", ("tier", name), ("value", Value));
        }
    }
}
