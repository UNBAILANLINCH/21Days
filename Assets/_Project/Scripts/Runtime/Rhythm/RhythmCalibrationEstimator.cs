// 职责：跟拍原始误差的中位数与质量检查。保存 DTO 不承担统计，判定器不能混入个人校准流程，故独立纯逻辑。
using System;
using System.Collections.Generic;
using Game.Core.Simulation;
namespace Game.Rhythm
{
    public sealed class RhythmCalibrationEstimator
    {
        private readonly double[] targets;
        private readonly bool[] matched;
        private readonly List<double> errors = new List<double>();
        private readonly List<int> beatIndices = new List<int>();
        private readonly int minimum;
        private readonly double windowMs;
        private readonly double maxMadMs;
        public int Count => errors.Count;
        public int Rejected { get; private set; }
        public int Observed { get; private set; }
        public int Duplicate { get; private set; }
        public int Outside { get; private set; }
        public int Invalid { get; private set; }
        public RhythmCalibrationEstimator(double[] targets, int minimum, double windowMs, double maxMadMs)
        {
            if (targets == null || targets.Length < minimum || minimum < 3 || !RhythmRules.Finite(windowMs) || windowMs <= 0 ||
                !RhythmRules.Finite(maxMadMs) || maxMadMs <= 0) throw new ArgumentException("校准参数无效");
            this.targets = (double[])targets.Clone(); matched = new bool[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                if (!RhythmRules.Finite(targets[i]) || targets[i] < 0 || (i > 0 && targets[i] <= targets[i - 1])) throw new ArgumentException("校准节拍无效");
            this.minimum = minimum; this.windowMs = windowMs; this.maxMadMs = maxMadMs;
        }
        public void Add(double rawSongSeconds)
            => TryAdd(rawSongSeconds, out _);
        public bool TryAdd(double rawSongSeconds, out double rawErrorMs)
        {
            bool accepted = TryAdd(rawSongSeconds, out rawErrorMs, out _, out _);
            if (!accepted) rawErrorMs = 0;
            return accepted;
        }
        public bool TryAdd(double rawSongSeconds, out double rawErrorMs, out int beatIndex, out string disposition)
        {
            Observed++; rawErrorMs = 0; beatIndex = -1; disposition = "invalid";
            if (!RhythmRules.Finite(rawSongSeconds)) { Rejected++; Invalid++; return false; }
            int closest = -1; double error = double.PositiveInfinity;
            // 先找最近节拍，再检查已匹配，重复点击不能被分配到旁边的拍。
            for (int i = 0; i < targets.Length; i++)
            {
                double candidate = (rawSongSeconds - targets[i]) * 1000;
                if (GameMath.Abs(candidate) < GameMath.Abs(error)) { closest = i; error = candidate; }
            }
            beatIndex = closest; rawErrorMs = error; disposition = "outside";
            if (closest < 0 || GameMath.Abs(error) > windowMs) { Rejected++; Outside++; return false; }
            disposition = "duplicate";
            if (matched[closest]) { Rejected++; Duplicate++; return false; }
            disposition = "matched";
            matched[closest] = true; errors.Add(error); beatIndices.Add(closest); rawErrorMs = error;
            return true;
        }
        public RhythmCalibrationResult Analyze()
        {
            var sorted = errors.ToArray(); Array.Sort(sorted);
            double median = sorted.Length == 0 ? 0 : Median(sorted);
            var deviations = new double[sorted.Length];
            for (int i = 0; i < sorted.Length; i++) deviations[i] = GameMath.Abs(sorted[i] - median);
            Array.Sort(deviations);
            double rawMad = deviations.Length == 0 ? 0 : Median(deviations);
            double cutoff = rawMad * 3 > 20 ? rawMad * 3 : 20;
            var kept = new List<double>();
            int blockCount = targets.Length >= 8 ? 4 : targets.Length >= 4 ? 2 : 1;
            var blocks = new List<double>[blockCount];
            for (int i = 0; i < blockCount; i++) blocks[i] = new List<double>();
            for (int i = 0; i < errors.Count; i++)
                if (GameMath.Abs(errors[i] - median) <= cutoff)
                { kept.Add(errors[i]); blocks[beatIndices[i] * blockCount / targets.Length].Add(errors[i]); }
            kept.Sort();
            double offset = kept.Count == 0 ? median : Median(kept.ToArray());
            var filtered = new double[kept.Count];
            for (int i = 0; i < kept.Count; i++) filtered[i] = GameMath.Abs(kept[i] - offset);
            Array.Sort(filtered);
            double filteredMad = filtered.Length == 0 ? 0 : Median(filtered);
            bool coverage = true; double first = double.PositiveInfinity; double last = double.NegativeInfinity;
            for (int i = 0; i < blockCount; i++)
            {
                int size = ((i + 1) * targets.Length + blockCount - 1) / blockCount - (i * targets.Length + blockCount - 1) / blockCount;
                if (blocks[i].Count < (size + 1) / 2) { coverage = false; continue; }
                blocks[i].Sort(); double value = Median(blocks[i].ToArray());
                if (value < first) first = value; if (value > last) last = value;
            }
            double spread = coverage ? last - first : double.NaN;
            bool temporal = coverage && spread <= 20;
            RhythmCalibrationReason reason = Observed == 0 ? RhythmCalibrationReason.NoInput : Count == 0 ? RhythmCalibrationReason.Unmatched :
                GameMath.Abs(offset) > 300 || filteredMad > maxMadMs * 2 ? RhythmCalibrationReason.Unstable :
                !coverage || kept.Count < minimum ? RhythmCalibrationReason.Insufficient : !temporal ? RhythmCalibrationReason.Drift :
                rawMad > maxMadMs ? RhythmCalibrationReason.Suggested : RhythmCalibrationReason.Stable;
            return new RhythmCalibrationResult(reason, offset, rawMad, filteredMad, spread, Observed, Count, kept.Count,
                Duplicate, Outside, Invalid, temporal);
        }
        public bool TryEstimate(out double offsetMs, out double madMs, out int accepted)
        {
            offsetMs = 0; madMs = 0; accepted = 0;
            if (Count < minimum) return false;
            var sorted = errors.ToArray(); Array.Sort(sorted);
            double median = Median(sorted);
            var deviations = new double[sorted.Length];
            for (int i = 0; i < sorted.Length; i++) deviations[i] = GameMath.Abs(sorted[i] - median);
            Array.Sort(deviations); madMs = Median(deviations);
            if (madMs > maxMadMs || GameMath.Abs(median) > 300) return false;
            double cutoff = madMs * 3 > 20 ? madMs * 3 : 20;
            var kept = new List<double>();
            for (int i = 0; i < sorted.Length; i++) if (GameMath.Abs(sorted[i] - median) <= cutoff) kept.Add(sorted[i]);
            accepted = kept.Count;
            if (accepted < minimum) return false;
            offsetMs = Median(kept.ToArray());
            return true;
        }
        private static double Median(double[] sorted)
            => sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * 0.5;
    }
}
