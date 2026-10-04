// 职责：确认式校准的分类、时序及单次补测证据；旧判定测试只覆盖单一 MAD 质量门，不能替代新流程。
using System;
using NUnit.Framework;
using Game.Rhythm;

namespace Game.Tests.EditMode.Rhythm
{
    public sealed class RhythmCalibrationFlowTests
    {
        private static RhythmCalibrationEstimator Estimator(int count = 32, int minimum = 24)
        {
            var targets = new double[count]; for (int i = 0; i < count; i++) targets[i] = (i + 1) * 0.5;
            return new RhythmCalibrationEstimator(targets, minimum, 250, 30);
        }
        private static RhythmCalibrationResult Analyze(Func<int, double> error, Func<int, bool> include = null)
        {
            var e = Estimator();
            for (int i = 0; i < 32; i++) if (include == null || include(i)) e.Add((i + 1) * 0.5 + error(i) / 1000);
            return e.Analyze();
        }
        private static RhythmCalibrationResult Extra(double error)
        {
            var e = Estimator(8, 6); for (int i = 0; i < 8; i++) e.Add((i + 1) * 0.5 + error / 1000); return e.Analyze();
        }
        [Test]
        public void Classification_NoObservedInputDiffersFromObservedUnmatched()
        {
            var e = Estimator(); Assert.That(e.Analyze().Reason, Is.EqualTo(RhythmCalibrationReason.NoInput));
            e.Add(-3); e.Add(double.NaN);
            var r = e.Analyze(); Assert.That(r.Reason, Is.EqualTo(RhythmCalibrationReason.Unmatched));
            Assert.That(r.Observed, Is.EqualTo(2)); Assert.That(r.Outside, Is.EqualTo(1)); Assert.That(r.Invalid, Is.EqualTo(1));
        }
        [Test]
        public void Mad33_WithConsistentTimeBlocks_OffersSuggestion()
        {
            var pattern = new[] { -55d, -44, -22, -11, 11, 22, 44, 55 };
            var r = Analyze(i => 80 + pattern[i % 8]);
            Assert.That(r.Reason, Is.EqualTo(RhythmCalibrationReason.Suggested)); Assert.That(r.HasCandidate, Is.True);
            Assert.That(r.OffsetMs, Is.EqualTo(80).Within(1e-6)); Assert.That(r.RawMadMs, Is.EqualTo(33).Within(1e-6));
            Assert.That(r.BlockSpreadMs, Is.Zero.Within(1e-6)); Assert.That(r.Accepted, Is.EqualTo(32));
            Assert.That(RhythmCalibrationResult.Supplement(r, Extra(80), 24, 40).Reason, Is.EqualTo(RhythmCalibrationReason.Suggested), "8拍不能把主轮不确定性洗成可靠");
            Assert.That(RhythmCalibrationResult.Supplement(r, Extra(80), 24, 20).Reason, Is.EqualTo(RhythmCalibrationReason.Suggested));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void LowGlobalMad_DoesNotHideTimeDrift(int mode)
        {
            var r = Analyze(i => 80 + (mode == 0 ? i * 50d / 31 : mode == 1 ? i < 16 ? 0 : 50 : i / 8 % 2 == 0 ? -25 : 25));
            Assert.That(r.Reason, Is.EqualTo(mode == 0 ? RhythmCalibrationReason.Drift : RhythmCalibrationReason.Suggested)); Assert.That(r.HasCandidate, Is.True);
            Assert.That(r.Confidence, Is.EqualTo(RhythmCalibrationConfidence.Low));
            if (mode == 0) Assert.That(r.TemporalProbability, Is.LessThanOrEqualTo(.05));
            else Assert.That(r.TemporalProbability, Is.GreaterThan(.05));
            Assert.That(r.CanSupplement, Is.False);
            Assert.That(r.RawMadMs, Is.LessThan(30));
        }
        [Test]
        public void ScatteredOutliersDifferFromLosingTheLastTimeBlock()
        {
            var scattered = Analyze(i => i % 8 == 7 ? 220 : 80);
            Assert.That(scattered.Reason, Is.EqualTo(RhythmCalibrationReason.Stable)); Assert.That(scattered.Accepted, Is.EqualTo(28));
            var last = Analyze(i => i >= 24 ? 220 : 80);
            Assert.That(last.Reason, Is.EqualTo(RhythmCalibrationReason.Insufficient)); Assert.That(last.Accepted, Is.EqualTo(24));
            Assert.That(RhythmCalibrationResult.Supplement(last, Extra(80), 24, 30).HasCandidate, Is.False);
        }
        [Test]
        public void Supplement_RequiresCoverageAndIndependentAgreement()
        {
            var primary = Analyze(i => 80, i => i % 4 != 0 && i != 29);
            Assert.That(primary.Accepted, Is.EqualTo(23)); Assert.That(primary.TemporalStable, Is.True);
            Assert.That(primary.Reason, Is.EqualTo(RhythmCalibrationReason.Insufficient));
            Assert.That(RhythmCalibrationResult.Supplement(primary, Extra(80), 24, 30).HasCandidate, Is.True);
            var disagreement = RhythmCalibrationResult.Supplement(primary, Extra(130), 24, 30);
            Assert.That(disagreement.Reason, Is.EqualTo(RhythmCalibrationReason.Insufficient));
            Assert.That(disagreement.HasCandidate, Is.False);
            Assert.That(RhythmCalibrationResult.Supplement(Estimator().Analyze(), Extra(80), 24, 30).HasCandidate, Is.False);
        }
        [Test]
        public void Supplement_CannotWashAwayLargeDispersion()
        {
            var noisy = Analyze(i => 80 + (i % 2 == 0 ? -100 : 100));
            Assert.That(noisy.Reason, Is.EqualTo(RhythmCalibrationReason.Unstable));
            Assert.That(RhythmCalibrationResult.Supplement(noisy, Extra(80), 24, 30).HasCandidate, Is.False);
        }
        [Test]
        public void DuplicateAndOldSession_DontInflateEffectiveSamples()
        {
            var old = Estimator();
            for (int i = 0; i < 32; i++) { old.Add((i + 1) * 0.5 + 0.08); old.Add((i + 1) * 0.5 + 0.09); }
            Assert.That(old.Count, Is.EqualTo(32)); Assert.That(old.Duplicate, Is.EqualTo(32));
            var fresh = Estimator(); Assert.That(fresh.Count, Is.Zero);
            for (int i = 0; i < 32; i++) fresh.Add((i + 1) * 0.5 - 0.08);
            Assert.That(fresh.Analyze().OffsetMs, Is.EqualTo(-80).Within(1e-6));
        }
        [Test]
        public void FiniteSampleNoise_SpreadAloneDoesNotInvalidateEstimate()
        {
            var pattern = new[] { -55d, -44, -22, -11, 11, 22, 44, 55 };
            var shifts = new[] { -15d, 15, -10, 10 };
            var r = Analyze(i => 80 + pattern[i % 8] + shifts[i / 8]);
            Assert.That(r.BlockSpreadMs, Is.GreaterThan(20));
            Assert.That(r.TemporalProbability, Is.GreaterThan(.05));
            Assert.That(r.Reason, Is.EqualTo(RhythmCalibrationReason.Suggested));
            Assert.That(r.Confidence, Is.EqualTo(RhythmCalibrationConfidence.Low)); Assert.That(r.HasCandidate, Is.True);
        }
        [Test]
        public void IndependentNoiseEnsemble_ValidSamplesRemainUsableWithoutClaimingReliable()
        {
            int usable = 0, reliable = 0;
            for (int seed = 0; seed < 64; seed++)
            {
                var random = new Random(seed); var r = Analyze(i => 80 + (random.NextDouble() + random.NextDouble() - 1) * 90);
                if (r.HasCandidate) usable++; if (r.Confidence == RhythmCalibrationConfidence.Reliable) reliable++;
                if (r.Confidence == RhythmCalibrationConfidence.Reliable)
                    Assert.That(r.PrecisionRadiusMs, Is.LessThanOrEqualTo(20));
            }
            Assert.That(usable, Is.GreaterThanOrEqualTo(60), "有限样本噪声不应被单独20ms门大量判为无值可用");
            Assert.That(reliable, Is.LessThan(usable), "可用不等于可靠");
        }
        [TestCase(80)] [TestCase(-80)]
        public void PreciseSignedSamples_ReturnReliableNarrowInterval(double offset)
        {
            var r = Analyze(i => offset + i % 3 - 1);
            Assert.That(r.Confidence, Is.EqualTo(RhythmCalibrationConfidence.Reliable));
            Assert.That(r.OffsetMs, Is.EqualTo(offset).Within(1));
            Assert.That(r.MedianLowerMs, Is.InRange(offset - 1.000001, offset + 1.000001));
            Assert.That(r.MedianUpperMs, Is.InRange(offset - 1.000001, offset + 1.000001));
        }
        [Test]
        public void FiveSamples_DoNotAdvertiseFinite95PercentInterval()
        {
            var e = Estimator(5, 3); for (int i = 0; i < 5; i++) e.Add((i + 1) * .5 + .08);
            var r = e.Analyze(); Assert.That(r.HasCandidate, Is.True);
            Assert.That(r.Confidence, Is.EqualTo(RhythmCalibrationConfidence.Low)); Assert.That(double.IsNaN(r.MedianLowerMs), Is.True);
        }
        [Test]
        public void Session36_RecordedErrorsKeepLowConfidenceAndOriginalWithinInterval()
        {
            // 真人已落盘误差的只读回放；无事件/设备身份，不宣称真人成功率。
            var errors = new[] {138.0665,140.875,134.5269,162.9101,149.0276,124.2244,103.2162,97.8378,
                160.4721,126.4858,123.3582,138.0292,165.2635,175.7121,184.3341,204.8246,
                218.3103,202.9313,170.8636,145.291,193.1743,181.351,188.5354,151.1244,
                201.6667,141.3897,176.9006,180.7461,151.933,114.0943,136.8911,143.0909};
            var r = Analyze(i => errors[i]); var repeat = Analyze(i => errors[i]);
            Assert.That(r.OffsetMs, Is.EqualTo(151.5287).Within(.00001));
            Assert.That(r.RawMadMs, Is.EqualTo(24.61315).Within(.00001));
            Assert.That(r.MedianLowerMs, Is.EqualTo(138.0665).Within(.00001));
            Assert.That(r.MedianUpperMs, Is.EqualTo(176.9006).Within(.00001));
            Assert.That(140.26935, Is.InRange(r.MedianLowerMs, r.MedianUpperMs));
            Assert.That(r.Reason, Is.EqualTo(RhythmCalibrationReason.Drift)); Assert.That(r.HasCandidate, Is.True);
            Assert.That(r.Confidence, Is.EqualTo(RhythmCalibrationConfidence.Low));
            Assert.That(repeat.TemporalProbability, Is.EqualTo(r.TemporalProbability));
        }
        [Test]
        public void CrossLaneChordsAndHolds_SameTimestampBacklogScoresEachNoteOnce()
        {
            var notes = new[] { new RhythmNoteData("a",0,500), new RhythmNoteData("b",1,500),
                new RhythmNoteData("c",2,1000,RhythmNoteType.Hold,1000), new RhythmNoteData("d",3,1000,RhythmNoteType.Hold,1000),
                new RhythmNoteData("e",0,1000), new RhythmNoteData("f",1,1500) };
            foreach (bool reverse in new[] { false, true })
            {
                var rules = new RhythmRules(notes,65,140,0,0,null); var queue = new RhythmInputQueue(); queue.Begin(9);
                var events = new[] { new RhythmHitIntent(0,.5,RhythmInputEdge.Press,9), new RhythmHitIntent(1,.5,RhythmInputEdge.Press,9),
                    new RhythmHitIntent(0,.55,RhythmInputEdge.Release,9), new RhythmHitIntent(1,.55,RhythmInputEdge.Release,9),
                    new RhythmHitIntent(2,1,RhythmInputEdge.Press,9), new RhythmHitIntent(3,1,RhythmInputEdge.Press,9),
                    new RhythmHitIntent(0,1,RhythmInputEdge.Press,9), new RhythmHitIntent(1,1.5,RhythmInputEdge.Press,9),
                    new RhythmHitIntent(2,2.02,RhythmInputEdge.Release,9), new RhythmHitIntent(3,2.02,RhythmInputEdge.Release,9) };
                if (reverse) Array.Reverse(events);
                foreach (var edge in events) { var intent = edge; queue.Enqueue(in intent); }
                queue.Drain(rules,3,null);
                Assert.That(rules.Perfect, Is.EqualTo(6)); Assert.That(rules.Score, Is.EqualTo(6000));
                Assert.That(rules.Combo, Is.EqualTo(6)); Assert.That(rules.CompletedHolds, Is.EqualTo(2)); Assert.That(rules.Miss, Is.Zero);
            }
        }
        [Test]
        public void SameLaneConflictStillRejectsChordOrTapInsideHold()
        {
            Assert.Throws<ArgumentException>(() => new RhythmRules(new[] {new RhythmNoteData("a",0,500),new RhythmNoteData("b",0,500)},65,140,0,0,null));
            Assert.Throws<ArgumentException>(() => new RhythmRules(new[] {new RhythmNoteData("a",0,500,RhythmNoteType.Hold,1000),new RhythmNoteData("b",0,1000)},65,140,0,0,null));
        }
    }
}
