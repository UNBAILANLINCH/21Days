// 职责：跟拍原始误差的中位数与质量检查。保存 DTO 不承担统计，判定器不能混入个人校准流程，故独立纯逻辑。
using System;
using System.Collections.Generic;
using Game.Core.Simulation;
namespace Game.Rhythm
{
    public sealed class RhythmCalibrationEstimator
    {
        public const string Strategy = "median-interval-permutation-v2";
        private const double PrecisionMs = 20;
        private const double Significance = 0.05;
        private const int Permutations = 1024;
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
            MedianInterval(sorted, out double lower, out double upper);
            double below = GameMath.Abs(offset - lower), above = GameMath.Abs(upper - offset);
            double radius = RhythmRules.Finite(lower) && RhythmRules.Finite(upper) ?
                below > above ? below : above : double.PositiveInfinity;
            // 20ms是实际变化参考；置换只提供时段结构证据，不把噪声单独判成无效估计。
            double probability = coverage && spread > PrecisionMs ? TemporalProbability(kept.ToArray(), blocks, spread) : 1;
            RhythmCalibrationReason reason = Observed == 0 ? RhythmCalibrationReason.NoInput : Count == 0 ? RhythmCalibrationReason.Unmatched :
                GameMath.Abs(offset) > 300 || filteredMad > maxMadMs * 2 ? RhythmCalibrationReason.Unstable :
                !coverage || kept.Count < minimum ? RhythmCalibrationReason.Insufficient :
                spread > PrecisionMs && probability <= Significance ? RhythmCalibrationReason.Drift :
                !temporal || radius > PrecisionMs || rawMad > maxMadMs ? RhythmCalibrationReason.Suggested : RhythmCalibrationReason.Stable;
            return new RhythmCalibrationResult(reason, offset, rawMad, filteredMad, spread, Observed, Count, kept.Count,
                Duplicate, Outside, Invalid, temporal, lower, upper, probability);
        }
        // 全部匹配样本的保守95%排序区间；不对离群剔除后的样本宣称exact覆盖。
        // 独立同分布假设不代表真人/设备满足；不足6拍没有有限端点的95%区间。
        private static void MedianInterval(double[] sorted, out double lower, out double upper)
        {
            lower = upper = double.NaN;
            int n = sorted.Length; double term = 1;
            for (int i = 0; i < n; i++) term *= .5;
            if (n < 6 || term == 0) return;
            double tail = 0; int rank = 0;
            for (int k = 1; k <= n / 2; k++)
            {
                tail += term;
                if (2 * tail > Significance) break;
                rank = k; term *= (double)(n - k + 1) / k;
            }
            if (rank > 0) { lower = sorted[rank - 1]; upper = sorted[n - rank]; }
        }
        private static double TemporalProbability(double[] values, List<double>[] blocks, double observed)
        {
            var buffers = new double[blocks.Length][];
            for (int i = 0; i < buffers.Length; i++) buffers[i] = new double[blocks[i].Count];
            var random = new Random(20261005); int exceed = 0;
            for (int run = 0; run < Permutations; run++)
            {
                for (int i = values.Length - 1; i > 0; i--)
                { int other = random.Next(i + 1); double v = values[i]; values[i] = values[other]; values[other] = v; }
                double smallest = double.PositiveInfinity, largest = double.NegativeInfinity; int index = 0;
                foreach (var buffer in buffers)
                {
                    Array.Copy(values, index, buffer, 0, buffer.Length); index += buffer.Length; Array.Sort(buffer);
                    double median = Median(buffer);
                    if (median < smallest) smallest = median; if (median > largest) largest = median;
                }
                if (largest - smallest >= observed - 1e-9) exceed++;
            }
            return (exceed + 1d) / (Permutations + 1);
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
