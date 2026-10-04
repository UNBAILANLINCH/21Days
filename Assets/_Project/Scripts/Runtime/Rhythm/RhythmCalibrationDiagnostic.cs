// 职责：有限容量的本地校准排查记录；演奏RHD要求音符判定回放，不能冒充校准原始证据。
// 不监听键盘，只接收RhythmState已经处理的四轨Press；不记录文本、其他键或设备身份。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Core.Simulation;
using Newtonsoft.Json;

namespace Game.Rhythm
{
    public sealed class RhythmCalibrationDiagnostic
    {
        public const int MaximumSamples = 256;
        public const int MaximumBytes = 128 * 1024;
        public const int RetainedRounds = 5;
        private readonly double[] targets;
        private readonly object header;
        private readonly List<Sample> samples = new List<Sample>();
        private bool truncated;
        private sealed class Sample
        {
            public double? EventTime { get; set; }
            public double? SongTime { get; set; }
            public double? CapturedRealtime { get; set; }
            public double? CapturedDsp { get; set; }
            public double? CaptureSpanSeconds { get; set; }
            public int BeatIndex { get; set; }
            public double? TargetTime { get; set; }
            public double? ErrorMs { get; set; }
            public string Disposition { get; set; }
        }
        public RhythmCalibrationDiagnostic(double[] targets, object header)
        {
            this.targets = (double[])targets.Clone(); this.header = header;
        }
        public void Record(double eventTime, double songTime, int beat, double error, string disposition,
            double before = double.NaN, double dsp = double.NaN, double after = double.NaN)
        {
            if (samples.Count >= MaximumSamples) { truncated = true; return; }
            samples.Add(new Sample {
                EventTime = Number(eventTime), SongTime = Number(songTime), BeatIndex = beat,
                CapturedRealtime = Number((before + after) * 0.5), CapturedDsp = Number(dsp),
                CaptureSpanSeconds = Number(after - before),
                TargetTime = beat >= 0 && beat < targets.Length ? Number(targets[beat]) : null,
                ErrorMs = disposition == "matched" || disposition == "duplicate" || disposition == "outside" ? Number(error) : null,
                Disposition = disposition
            });
        }
        public string ToJson(RhythmCalibrationResult result, string endReason)
        {
            var matched = samples.Where(s => s.Disposition == "matched" && s.ErrorMs.HasValue).ToArray();
            var sorted = matched.Select(s => s.ErrorMs.Value).OrderBy(x => x).ToArray();
            double median = sorted.Length == 0 ? 0 : Median(sorted);
            double cutoff = result.RawMadMs * 3 > 20 ? result.RawMadMs * 3 : 20;
            int blockCount = targets.Length >= 8 ? 4 : targets.Length >= 4 ? 2 : 1;
            var rows = samples.Select(s => new {
                s.EventTime, s.SongTime, s.CapturedRealtime, s.CapturedDsp, s.CaptureSpanSeconds,
                s.BeatIndex, s.TargetTime, s.ErrorMs, s.Disposition,
                outlier = !truncated && s.Disposition == "matched" ? (bool?)(GameMath.Abs(s.ErrorMs.Value - median) > cutoff) : null
            }).ToArray();
            var blocks = Enumerable.Range(0, blockCount).Select(block => {
                var kept = matched.Where(s => s.BeatIndex * blockCount / targets.Length == block &&
                    GameMath.Abs(s.ErrorMs.Value - median) <= cutoff).Select(s => s.ErrorMs.Value).OrderBy(x => x).ToArray();
                int first = (block * targets.Length + blockCount - 1) / blockCount;
                int after = ((block + 1) * targets.Length + blockCount - 1) / blockCount;
                return new { block, firstBeat = first, lastBeat = after - 1, kept = kept.Length, minimum = (after - first + 1) / 2,
                    medianMs = !truncated && kept.Length > 0 ? (double?)Median(kept) : null,
                    minimumMs = kept.Length > 0 ? (double?)kept[0] : null,
                    maximumMs = kept.Length > 0 ? (double?)kept[kept.Length - 1] : null };
            }).ToArray();
            return JsonConvert.SerializeObject(new {
                format = "rhythm-calibration-v1", capturedAtUtc = DateTime.UtcNow.ToString("O"), header,
                endReason, complete = !truncated, maximumSamples = MaximumSamples, cutoffMs = cutoff, targets,
                samples = rows, blocks, result = new {
                    reason = result.Reason.ToString(), result.OffsetMs, result.RawMadMs, result.FilteredMadMs,
                    blockSpreadMs = Number(result.BlockSpreadMs), result.Observed, result.Matched, result.Accepted,
                    result.Duplicate, result.Outside, result.Invalid, result.TemporalStable
                }
            }, Formatting.Indented);
        }
        public bool TrySave(string directory, RhythmCalibrationResult result, string endReason, out string path, out string error)
        {
            path = null; error = null;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(ToJson(result, endReason));
                if (bytes.Length > MaximumBytes) { error = "记录超过字节限额"; return false; }
                Directory.CreateDirectory(directory);
                // 仅回收此格式专属文件，不删除演奏RHD或用户导出的其他诊断。
                var existing = new DirectoryInfo(directory).GetFiles("calibration-v1-*.json")
                    .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name).ToArray();
                for (int i = 0; i <= existing.Length - RetainedRounds; i++) existing[i].Delete();
                string candidate = Path.Combine(directory, "calibration-v1-" + Guid.NewGuid().ToString("N") + ".json");
                using (var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    stream.Write(bytes, 0, bytes.Length);
                path = candidate; return true;
            }
            catch (IOException e) { error = e.Message; return false; }
            catch (UnauthorizedAccessException e) { error = e.Message; return false; }
            catch (JsonException e) { error = e.Message; return false; }
        }
        private static double? Number(double value) => RhythmRules.Finite(value) ? (double?)value : null;
        private static double Median(double[] sorted) => sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] :
            (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * 0.5;
    }
}
