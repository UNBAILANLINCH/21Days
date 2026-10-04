// 职责：校准候选和时序质量的纯数据；旧补偿 DTO 不能承载未确认的建议，估计器不应管理 UI/存档。
using Game.Core.Simulation;

namespace Game.Rhythm
{
    public enum RhythmCalibrationReason { NoInput, Unmatched, Insufficient, Unstable, Drift, Suggested, Stable }

    public sealed class RhythmCalibrationResult
    {
        public RhythmCalibrationReason Reason { get; }
        public double OffsetMs { get; }
        public double RawMadMs { get; }
        public double FilteredMadMs { get; }
        public double BlockSpreadMs { get; }
        public int Observed { get; }
        public int Matched { get; }
        public int Accepted { get; }
        public int Duplicate { get; }
        public int Outside { get; }
        public int Invalid { get; }
        public bool TemporalStable { get; }
        public bool CanSupplement => TemporalStable && Reason != RhythmCalibrationReason.Drift && Reason != RhythmCalibrationReason.Unstable;
        public bool HasCandidate => Reason == RhythmCalibrationReason.Stable || Reason == RhythmCalibrationReason.Suggested;
        public RhythmCalibrationResult(RhythmCalibrationReason reason, double offset, double rawMad, double filteredMad,
            double blockSpread, int observed, int matched, int accepted, int duplicate, int outside, int invalid, bool temporalStable)
        {
            Reason = reason; OffsetMs = offset; RawMadMs = rawMad; FilteredMadMs = filteredMad; BlockSpreadMs = blockSpread;
            Observed = observed; Matched = matched; Accepted = accepted; Duplicate = duplicate; Outside = outside; Invalid = invalid;
            TemporalStable = temporalStable;
        }
        // 补测是独立验证块；时序漂移/覆盖缺口不靠事后8拍伪装补齐，统计门仍是待真人验证的工程策略。
        public static RhythmCalibrationResult Supplement(RhythmCalibrationResult primary, RhythmCalibrationResult extra, int minimum, double maxMadMs)
        {
            bool consistent = primary.Reason != RhythmCalibrationReason.Unstable && primary.Reason != RhythmCalibrationReason.Drift &&
                primary.TemporalStable && extra.HasCandidate && extra.TemporalStable &&
                primary.Accepted >= minimum - 8 && primary.Accepted + extra.Accepted >= minimum &&
                GameMath.Abs(primary.OffsetMs - extra.OffsetMs) <= 20;
            var reason = consistent ? primary.RawMadMs > maxMadMs || extra.RawMadMs > maxMadMs ? RhythmCalibrationReason.Suggested : RhythmCalibrationReason.Stable :
                primary.TemporalStable && extra.HasCandidate && GameMath.Abs(primary.OffsetMs - extra.OffsetMs) > 20 ? RhythmCalibrationReason.Drift : primary.Reason;
            // 不一致时撤销原建议资格，必须保留旧值或完整重测。
            if (!consistent && (reason == RhythmCalibrationReason.Stable || reason == RhythmCalibrationReason.Suggested)) reason = RhythmCalibrationReason.Insufficient;
            if (!consistent && primary.Observed + extra.Observed > 0 && (reason == RhythmCalibrationReason.NoInput || reason == RhythmCalibrationReason.Unmatched))
                reason = primary.Matched + extra.Matched > 0 ? RhythmCalibrationReason.Insufficient : RhythmCalibrationReason.Unmatched;
            return new RhythmCalibrationResult(reason, primary.OffsetMs, primary.RawMadMs, primary.FilteredMadMs,
                primary.BlockSpreadMs, primary.Observed + extra.Observed, primary.Matched + extra.Matched,
                primary.Accepted + extra.Accepted, primary.Duplicate + extra.Duplicate, primary.Outside + extra.Outside,
                primary.Invalid + extra.Invalid, consistent);
        }
    }
}
