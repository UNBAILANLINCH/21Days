// 职责：本地校准证据与补测资格的确定性检查，不运行音频或读取玩家档案。
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Game.Rhythm;
namespace Game.Tests.EditMode.Rhythm
{
    public sealed class RhythmCalibrationDiagnosticTests
    {
        private static double[] Targets() => Enumerable.Range(0, 32).Select(i => (i + 9) * .5).ToArray();
        [Test]
        public void Diagnostic_RecordsMatchingRejectionOutliersAndExactBlocks()
        {
            var targets = Targets(); var e = new RhythmCalibrationEstimator(targets, 24, 250, 30);
            var d = new RhythmCalibrationDiagnostic(targets, new { source = "offline-fixture", originalOffsetMs = 100 });
            d.Record(101, 1, -1, 0, "warmup");
            for (int i = 0; i < 32; i++)
            {
                double song = targets[i] + (i < 10 ? 86 : i < 19 ? 100 : i < 29 ? 114 : 200) / 1000d;
                e.TryAdd(song, out double error, out int beat, out string disposition);
                d.Record(100 + song, song, beat, error, disposition, 120, 80, 120.0001);
            }
            foreach (double song in new[] { targets[0] + .09, 100, double.NaN })
            {
                e.TryAdd(song, out double error, out int beat, out string disposition);
                d.Record(100 + song, song, beat, error, disposition);
            }
            var r = e.Analyze(); var json = JObject.Parse(d.ToJson(r, "Completed"));
            Assert.That(r.Reason, Is.EqualTo(RhythmCalibrationReason.Drift));
            Assert.That(r.CanSupplement, Is.False); Assert.That(r.Accepted, Is.EqualTo(29));
            Assert.That(r.RawMadMs, Is.EqualTo(14).Within(1e-6)); Assert.That(r.BlockSpreadMs, Is.EqualTo(28).Within(1e-6));
            var rows = (JArray)json["samples"];
            Assert.That(rows.Count, Is.EqualTo(36));
            Assert.That(rows.Count(s => (bool?)s["outlier"] == true), Is.EqualTo(3));
            Assert.That(rows[1]["EventTime"].Value<double>() - rows[1]["SongTime"].Value<double>(), Is.EqualTo(100).Within(1e-6));
            Assert.That(rows[1]["BeatIndex"].Value<int>(), Is.Zero);
            Assert.That(rows[1]["CaptureSpanSeconds"].Value<double>(), Is.EqualTo(.0001).Within(1e-6));
            Assert.That(rows[33]["Disposition"].Value<string>(), Is.EqualTo("duplicate"));
            Assert.That(rows[34]["Disposition"].Value<string>(), Is.EqualTo("outside"));
            Assert.That(rows[35]["Disposition"].Value<string>(), Is.EqualTo("invalid"));
            Assert.That(rows[35]["SongTime"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(json["blocks"].Select(b => b["kept"].Value<int>()), Is.EqualTo(new[] { 8, 8, 8, 5 }));
            Assert.That(json["blocks"].Select(b => b["medianMs"].Value<double>()).Max() -
                json["blocks"].Select(b => b["medianMs"].Value<double>()).Min(), Is.EqualTo(r.BlockSpreadMs).Within(1e-6));
        }
        [Test]
        public void RecordingLimit_IsExplicitAndDoesNotChangeEstimation()
        {
            var t = Targets(); var e = new RhythmCalibrationEstimator(t, 24, 250, 30);
            var d = new RhythmCalibrationDiagnostic(t, new { source = "offline-fixture" });
            for (int i = 0; i < 300; i++) d.Record(i, i, -1, 0, "outside");
            foreach (double target in t) e.Add(target + .08);
            var r = e.Analyze(); var json = JObject.Parse(d.ToJson(r, "Stopped"));
            Assert.That(json["complete"].Value<bool>(), Is.False);
            Assert.That(json["samples"].Count(), Is.EqualTo(RhythmCalibrationDiagnostic.MaximumSamples));
            Assert.That(json["samples"].All(row => row["outlier"].Type == JTokenType.Null), Is.True);
            Assert.That(r.HasCandidate, Is.True); Assert.That(r.OffsetMs, Is.EqualTo(80).Within(1e-6));
        }
        [Test]
        public void Save_RetainsFiveOnlyAndFailureLeavesResultAndForeignFilesAlone()
        {
            string directory = Path.Combine(Path.GetTempPath(), "rhythm-calibration-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var t = Targets(); var e = new RhythmCalibrationEstimator(t, 24, 250, 30);
                foreach (double target in t) e.Add(target + .08);
                var r = e.Analyze(); var d = new RhythmCalibrationDiagnostic(t, new { source = "offline-fixture" });
                string sentinel = Path.Combine(directory, "user-export.rhd"); File.WriteAllText(sentinel, "keep");
                for (int i = 0; i < 8; i++)
                {
                    Assert.That(d.TrySave(directory, r, "Completed", out string path, out string error), Is.True, error);
                    Assert.That(new FileInfo(path).Length, Is.LessThanOrEqualTo(RhythmCalibrationDiagnostic.MaximumBytes));
                    Assert.That(JObject.Parse(File.ReadAllText(path))["result"]["reason"].Value<string>(), Is.EqualTo("Stable"));
                }
                Assert.That(Directory.GetFiles(directory, "calibration-v1-*.json").Length, Is.EqualTo(5));
                Assert.That(File.ReadAllText(sentinel), Is.EqualTo("keep"));
                Assert.That(d.TrySave(sentinel, r, "Stopped", out _, out string failed), Is.False);
                Assert.That(failed, Is.Not.Empty); Assert.That(r.OffsetMs, Is.EqualTo(80).Within(1e-6));
                var huge = new RhythmCalibrationDiagnostic(t, new { invalidOversizedHeader = new string('x', 140000) });
                Assert.That(huge.TrySave(directory, r, "Completed", out _, out _), Is.False);
                Assert.That(Directory.GetFiles(directory, "calibration-v1-*.json").Length, Is.EqualTo(5));
            }
            finally { Directory.Delete(directory, true); }
        }
        [TestCase(RhythmCalibrationReason.Drift, true, false)]
        [TestCase(RhythmCalibrationReason.Unstable, true, false)]
        [TestCase(RhythmCalibrationReason.Insufficient, false, false)]
        [TestCase(RhythmCalibrationReason.Insufficient, true, true)]
        [TestCase(RhythmCalibrationReason.Stable, true, true)]
        [TestCase(RhythmCalibrationReason.Suggested, true, true)]
        public void SupplementEligibility_RequiresRecoverablePrimary(RhythmCalibrationReason reason, bool temporal, bool allowed)
        {
            var result = new RhythmCalibrationResult(reason, 80, 14, 14, temporal ? 0 : 28, 33, 32, 29, 0, 1, 0, temporal);
            Assert.That(result.CanSupplement, Is.EqualTo(allowed));
        }
    }
}
