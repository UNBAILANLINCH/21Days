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
        private readonly int minimum;
        private readonly double windowMs;
        private readonly double maxMadMs;
        public int Count => errors.Count;
        public int Rejected { get; private set; }
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
        {
            if (!RhythmRules.Finite(rawSongSeconds)) { Rejected++; return; }
            int closest = -1; double error = double.PositiveInfinity;
            // 先找最近节拍，再检查已匹配，重复点击不能被分配到旁边的拍。
            for (int i = 0; i < targets.Length; i++)
            {
                double candidate = (rawSongSeconds - targets[i]) * 1000;
                if (GameMath.Abs(candidate) < GameMath.Abs(error)) { closest = i; error = candidate; }
            }
            if (closest < 0 || matched[closest] || GameMath.Abs(error) > windowMs) { Rejected++; return; }
            matched[closest] = true; errors.Add(error);
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
