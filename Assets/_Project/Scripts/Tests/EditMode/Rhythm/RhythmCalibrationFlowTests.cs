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
            Assert.That(RhythmCalibrationResult.Supplement(r, Extra(80), 24, 40).Reason, Is.EqualTo(RhythmCalibrationReason.Stable));
            Assert.That(RhythmCalibrationResult.Supplement(r, Extra(80), 24, 20).Reason, Is.EqualTo(RhythmCalibrationReason.Suggested));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void LowGlobalMad_DoesNotHideTimeDrift(int mode)
        {
            var r = Analyze(i => 80 + (mode == 0 ? i * 50d / 31 : mode == 1 ? i < 16 ? 0 : 50 : i / 8 % 2 == 0 ? -25 : 25));
            Assert.That(r.Reason, Is.EqualTo(RhythmCalibrationReason.Drift)); Assert.That(r.HasCandidate, Is.False);
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
            Assert.That(RhythmCalibrationResult.Supplement(primary, Extra(130), 24, 30).Reason, Is.EqualTo(RhythmCalibrationReason.Drift));
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
