// 职责：校准候选和时序质量的纯数据；旧补偿 DTO 不能承载未确认的建议，估计器不应管理 UI/存档。
using Game.Core.Simulation;

namespace Game.Rhythm
{
    public enum RhythmCalibrationReason { NoInput, Unmatched, Insufficient, Unstable, Drift, Suggested, Stable }
    public enum RhythmCalibrationConfidence { Invalid, Low, Reliable }

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
        public double MedianLowerMs { get; }
        public double MedianUpperMs { get; }
        public double TemporalProbability { get; }
        public double PrecisionRadiusMs => RhythmRules.Finite(MedianLowerMs) && RhythmRules.Finite(MedianUpperMs) ?
            GameMath.Abs(OffsetMs - MedianLowerMs) > GameMath.Abs(MedianUpperMs - OffsetMs) ?
                GameMath.Abs(OffsetMs - MedianLowerMs) : GameMath.Abs(MedianUpperMs - OffsetMs) : double.PositiveInfinity;
        public RhythmCalibrationConfidence Confidence => !HasCandidate ? RhythmCalibrationConfidence.Invalid :
            Reason == RhythmCalibrationReason.Stable ? RhythmCalibrationConfidence.Reliable : RhythmCalibrationConfidence.Low;
        public bool CanSupplement => TemporalStable && Reason != RhythmCalibrationReason.Drift && Reason != RhythmCalibrationReason.Unstable;
        public bool HasCandidate => RhythmRules.Finite(OffsetMs) && (Reason == RhythmCalibrationReason.Stable ||
            Reason == RhythmCalibrationReason.Suggested || Reason == RhythmCalibrationReason.Drift);
        public RhythmCalibrationResult(RhythmCalibrationReason reason, double offset, double rawMad, double filteredMad,
            double blockSpread, int observed, int matched, int accepted, int duplicate, int outside, int invalid, bool temporalStable,
            double medianLowerMs = double.NaN, double medianUpperMs = double.NaN, double temporalProbability = double.NaN)
        {
            Reason = reason; OffsetMs = offset; RawMadMs = rawMad; FilteredMadMs = filteredMad; BlockSpreadMs = blockSpread;
            Observed = observed; Matched = matched; Accepted = accepted; Duplicate = duplicate; Outside = outside; Invalid = invalid;
            TemporalStable = temporalStable;
            MedianLowerMs = medianLowerMs; MedianUpperMs = medianUpperMs; TemporalProbability = temporalProbability;
        }
        // 补测是独立验证块；时序漂移/覆盖缺口不靠事后8拍伪装补齐，统计门仍是待真人验证的工程策略。
        public static RhythmCalibrationResult Supplement(RhythmCalibrationResult primary, RhythmCalibrationResult extra, int minimum, double maxMadMs)
        {
            bool consistent = primary.Reason != RhythmCalibrationReason.Unstable && primary.Reason != RhythmCalibrationReason.Drift &&
                primary.TemporalStable && extra.HasCandidate && extra.TemporalStable &&
                primary.Accepted >= minimum - 8 && primary.Accepted + extra.Accepted >= minimum &&
                GameMath.Abs(primary.OffsetMs - extra.OffsetMs) <= 20;
            var reason = consistent ? primary.Reason == RhythmCalibrationReason.Suggested || extra.Confidence != RhythmCalibrationConfidence.Reliable ||
                primary.PrecisionRadiusMs > 20 || primary.RawMadMs > maxMadMs || extra.RawMadMs > maxMadMs ? RhythmCalibrationReason.Suggested : RhythmCalibrationReason.Stable :
                primary.HasCandidate && primary.TemporalStable && extra.HasCandidate && GameMath.Abs(primary.OffsetMs - extra.OffsetMs) > 20 ? RhythmCalibrationReason.Drift : primary.Reason;
            // 不一致时撤销原建议资格，必须保留旧值或完整重测。
            if (!consistent && (reason == RhythmCalibrationReason.Stable || reason == RhythmCalibrationReason.Suggested)) reason = RhythmCalibrationReason.Insufficient;
            if (!consistent && primary.Observed + extra.Observed > 0 && (reason == RhythmCalibrationReason.NoInput || reason == RhythmCalibrationReason.Unmatched))
                reason = primary.Matched + extra.Matched > 0 ? RhythmCalibrationReason.Insufficient : RhythmCalibrationReason.Unmatched;
            return new RhythmCalibrationResult(reason, primary.OffsetMs, primary.RawMadMs, primary.FilteredMadMs,
                primary.BlockSpreadMs, primary.Observed + extra.Observed, primary.Matched + extra.Matched,
                primary.Accepted + extra.Accepted, primary.Duplicate + extra.Duplicate, primary.Outside + extra.Outside,
                primary.Invalid + extra.Invalid, consistent, primary.MedianLowerMs, primary.MedianUpperMs, primary.TemporalProbability);
        }
    }
}
