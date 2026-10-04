// 职责：验证带时间戳判定的边界与补偿。其它模块测试不包含音符，不能扩展购物或战斗测试。
using System;
using System.Reflection;
using Game.Rhythm;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Rhythm
{
    public sealed class RhythmRulesTests
    {
        private static RhythmRules Create(double offset = 0) => new RhythmRules(new[] { 1d, 2d, 3d, 4d }, new[] { 0, 1, 2, 3 }, 65, 140, offset, null);

        [TestCase(-0.065, RhythmGrade.Perfect)]
        [TestCase(0.065, RhythmGrade.Perfect)]
        [TestCase(-0.14, RhythmGrade.Good)]
        [TestCase(0.14, RhythmGrade.Good)]
        [TestCase(0.141, RhythmGrade.None)]
        public void Hit_WindowEdges_UseInclusiveBoundary(double error, RhythmGrade grade)
        {
            var rules = Create();
            Assert.That(rules.Hit(new RhythmHitIntent(0, 1 + error)).Grade, Is.EqualTo(grade));
        }
        [Test]
        public void Hit_DuplicateAndWrongLane_DoNotScoreTwice()
        {
            var rules = Create();
            Assert.That(rules.Hit(new RhythmHitIntent(1, 1)).Grade, Is.EqualTo(RhythmGrade.None));
            rules.Hit(new RhythmHitIntent(0, 1));
            Assert.That(rules.Hit(new RhythmHitIntent(0, 1)).Grade, Is.EqualTo(RhythmGrade.None));
            Assert.That(rules.Score, Is.EqualTo(1000));
        }
        [TestCase(100)]
        [TestCase(-100)]
        public void Hit_SignedOffset_CompensatesWithoutHidingRawError(double offset)
        {
            var rules = Create(offset);
            var result = rules.Hit(new RhythmHitIntent(0, 1 + offset / 1000));
            Assert.That(result.Grade, Is.EqualTo(RhythmGrade.Perfect));
            Assert.That(result.ErrorMs, Is.EqualTo(0).Within(0.000001));
            Assert.That(result.RawErrorMs, Is.EqualTo(offset).Within(0.000001));
        }
        [Test]
        public void Advance_DroppedFrames_ResolveAllExpiredNotesOnce()
        {
            var rules = Create();
            rules.Hit(new RhythmHitIntent(0, 1));
            Assert.That(rules.Advance(5), Is.EqualTo(3));
            Assert.That(rules.Advance(5), Is.Zero);
            Assert.That(rules.Miss, Is.EqualTo(3));
            Assert.That(rules.Combo, Is.Zero);
            Assert.That(rules.Finished, Is.True);
        }
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void Hit_SameTimestampAtDifferentFrameRates_ProducesSameScore(int frameRate)
        {
            var rules = Create();
            int next = 0;
            for (int frame = 0; frame <= frameRate * 5; frame++)
            {
                double now = (double)frame / frameRate;
                while (next < 4 && next + 1d <= now)
                { rules.Hit(new RhythmHitIntent(next, next + 1d)); next++; }
                rules.Advance(now);
            }
            Assert.That(rules.Perfect, Is.EqualTo(4));
            Assert.That(rules.Score, Is.EqualTo(4000));
            Assert.That(rules.Miss, Is.Zero);
        }
        [Test]
        public void Constructor_InvalidChart_RejectsIt()
        {
            Assert.Throws<ArgumentException>(() => new RhythmRules(new[] { 2d, 1d }, new[] { 0, 1 }, 65, 140, 0, null));
            Assert.Throws<ArgumentException>(() => new RhythmRules(new[] { 1d, 1.1 }, new[] { 0, 0 }, 65, 140, 0, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(double.NaN));
        }

        private static RhythmRules Hold(double offset = 0) => new RhythmRules(
            new[] { new RhythmNoteData("hold", 0, 1000, RhythmNoteType.Hold, 1000) }, 65, 140, offset, 0, null);

        [TestCase(-100)]
        [TestCase(0)]
        [TestCase(100)]
        public void Advance_MixedHoldAndMiss_DroppedFramesPreserveComboOrder(double offset)
        {
            var chart = new[] { new RhythmNoteData("hold", 0, 1000, RhythmNoteType.Hold, 2000),
                new RhythmNoteData("hit", 1, 1500), new RhythmNoteData("miss", 2, 2000) };
            var fine = new RhythmRules(chart, 65, 140, offset, 0, null);
            var coarse = new RhythmRules(chart, 65, 140, offset, 0, null);
            foreach (var rules in new[] { fine, coarse })
            {
                rules.Hit(new RhythmHitIntent(0, 1 + offset / 1000));
                rules.Hit(new RhythmHitIntent(1, 1.5 + offset / 1000));
            }
            fine.Advance(2.2 + offset / 1000);
            fine.Advance(3.1 + offset / 1000);
            coarse.Advance(3.1 + offset / 1000);
            Assert.That(coarse.Combo, Is.EqualTo(1));
            Assert.That(coarse.MaxCombo, Is.EqualTo(1));
            Assert.That(coarse.Combo, Is.EqualTo(fine.Combo));
            Assert.That(coarse.MaxCombo, Is.EqualTo(fine.MaxCombo));
            Assert.That(coarse.Score, Is.EqualTo(fine.Score));
            Assert.That(coarse.Miss, Is.EqualTo(fine.Miss));
        }

        [TestCase(-100)]
        [TestCase(0)]
        [TestCase(100)]
        public void Advance_HoldTailAndMissShareDeadline_PreservesInclusiveTailOrder(double offset)
        {
            var chart = new[] { new RhythmNoteData("miss", 0, 1860),
                new RhythmNoteData("hold", 1, 1950, RhythmNoteType.Hold, 50) };
            var fine = new RhythmRules(chart, 65, 140, offset, 0, null);
            var coarse = new RhythmRules(chart, 65, 140, offset, 0, null);
            foreach (var rules in new[] { fine, coarse }) rules.Hit(new RhythmHitIntent(1, 1.95 + offset / 1000));
            fine.Advance(2 + offset / 1000);
            fine.Advance(2.1 + offset / 1000);
            coarse.Advance(2.1 + offset / 1000);
            Assert.That(coarse.Combo, Is.Zero);
            Assert.That(coarse.Combo, Is.EqualTo(fine.Combo));
            Assert.That(coarse.MaxCombo, Is.EqualTo(fine.MaxCombo));
            Assert.That(coarse.Score, Is.EqualTo(fine.Score));
        }

        [TestCase(-100)]
        [TestCase(0)]
        [TestCase(100)]
        public void Hold_HeadAndAutomaticTail_UseSameSignedOffset(double offset)
        {
            var rules = Hold(offset);
            var result = rules.Hit(new RhythmHitIntent(0, 1 + offset / 1000));
            Assert.That(result.HoldStarted, Is.True);
            Assert.That(rules.Score, Is.Zero);
            rules.Advance(1.99 + offset / 1000);
            Assert.That(rules.IsHolding(0), Is.True);
            rules.Advance(2 + offset / 1000);
            Assert.That(rules.Perfect, Is.EqualTo(1));
            Assert.That(rules.CompletedHolds, Is.EqualTo(1));
            Assert.That(rules.Score, Is.EqualTo(1000));
            rules.Advance(3);
            rules.Hit(new RhythmHitIntent(0, 3, RhythmInputEdge.Release));
            Assert.That(rules.Perfect + rules.Good + rules.Miss, Is.EqualTo(1));
        }
        [TestCase(-100)]
        [TestCase(100)]
        public void Hold_EarlyRelease_FailsOnceAndCannotRecover(double offset)
        {
            var rules = Hold(offset);
            rules.Hit(new RhythmHitIntent(0, 1 + offset / 1000));
            Assert.That(rules.Hit(new RhythmHitIntent(0, 1.9 + offset / 1000, RhythmInputEdge.Release)).Grade, Is.EqualTo(RhythmGrade.Miss));
            Assert.That(rules.Hit(new RhythmHitIntent(0, 1.95)).Grade, Is.EqualTo(RhythmGrade.None));
            rules.Advance(5);
            Assert.That(rules.Miss, Is.EqualTo(1));
            Assert.That(rules.Score, Is.Zero);
        }
        [Test]
        public void Hold_MissingHead_DoesNotScoreFromReleaseOrTail()
        {
            var rules = Hold();
            rules.Hit(new RhythmHitIntent(0, 2, RhythmInputEdge.Release));
            rules.Advance(3);
            Assert.That(rules.Miss, Is.EqualTo(1));
            Assert.That(rules.Score, Is.Zero);
        }
        [Test]
        public void Records_StableSortChartOffsetAndZeroTime_PreserveInputData()
        {
            var source = new[] { new RhythmNoteData("later", 0, 1000), new RhythmNoteData("a", 1, 0), new RhythmNoteData("b", 2, 0) };
            var rules = new RhythmRules(source, 65, 140, 100, 50, null);
            Assert.That(rules.NoteId(0), Is.EqualTo("a"));
            Assert.That(rules.NoteId(1), Is.EqualTo("b"));
            Assert.That(source[0].Id, Is.EqualTo("later"));
            Assert.That(rules.Hit(new RhythmHitIntent(1, 0.15)).Grade, Is.EqualTo(RhythmGrade.Perfect));
            Assert.That(rules.LastEndSeconds, Is.EqualTo(1.05).Within(1e-9));
        }
        [Test]
        public void Records_DuplicateIdInvalidDurationAndHoldConflict_Reject()
        {
            Assert.Throws<ArgumentException>(() => new RhythmRules(new[] { new RhythmNoteData("a", 0, 0), new RhythmNoteData("a", 1, 1000) }, 65, 140, 0, 0, null));
            Assert.Throws<ArgumentException>(() => new RhythmRules(new[] { new RhythmNoteData("a", 0, double.NaN) }, 65, 140, 0, 0, null));
            Assert.Throws<ArgumentException>(() => new RhythmRules(new[] { new RhythmNoteData("a", 0, 0, RhythmNoteType.Tap, 100) }, 65, 140, 0, 0, null));
            Assert.Throws<ArgumentException>(() => new RhythmRules(new[] { new RhythmNoteData("a", 0, 0, RhythmNoteType.Hold, 1000), new RhythmNoteData("b", 0, 900) }, 65, 140, 0, 0, null));
            Assert.Throws<ArgumentException>(() => RhythmConfig.ValidateBpm(new[] { new RhythmBpmData(0, 120), new RhythmBpmData(0, 100) }));
        }
        [Test]
        public void Queue_BacklogAndIndependentChordTimes_AreJudgedBeforeCurrentTimeout()
        {
            var rules = Create(); var queue = new RhythmInputQueue(); queue.Begin(1);
            queue.Enqueue(new RhythmHitIntent(1, 2.08, RhythmInputEdge.Press, 1));
            queue.Enqueue(new RhythmHitIntent(0, 1, RhythmInputEdge.Press, 1));
            queue.Drain(rules, 2.2, null);
            Assert.That(rules.Perfect, Is.EqualTo(1));
            Assert.That(rules.Good, Is.EqualTo(1));
            Assert.That(rules.Miss, Is.Zero);
        }
        [Test]
        public void Queue_DuplicatePressAndOldSession_DoNotConsumeNewNotes()
        {
            var rules = new RhythmRules(new[] { 1d, 2d }, new[] { 0, 0 }, 65, 140, 0, null);
            var queue = new RhythmInputQueue(); queue.Begin(3);
            queue.Enqueue(new RhythmHitIntent(0, 1, RhythmInputEdge.Press, 3));
            queue.Enqueue(new RhythmHitIntent(0, 2, RhythmInputEdge.Press, 3));
            queue.Drain(rules, 2, null);
            Assert.That(rules.Perfect, Is.EqualTo(1));
            queue.Clear(); queue.Begin(4);
            queue.Enqueue(new RhythmHitIntent(0, 2, RhythmInputEdge.Press, 3));
            queue.Drain(rules, 2, null);
            Assert.That(rules.Perfect, Is.EqualTo(1));
            queue.Enqueue(new RhythmHitIntent(0, 2, RhythmInputEdge.Press, 4));
            queue.Drain(rules, 2, null);
            Assert.That(rules.Perfect, Is.EqualTo(2));
        }
        [Test]
        public void Queue_InputEarlierThanWatermark_IsDiagnosedWithoutRollback()
        {
            var rules = Create(); var queue = new RhythmInputQueue(); queue.Begin(1);
            queue.Drain(rules, 1.2, null);
            queue.Enqueue(new RhythmHitIntent(0, 1, RhythmInputEdge.Press, 1));
            queue.Drain(rules, 1.3, null);
            Assert.That(queue.LateInputs, Is.EqualTo(1));
            Assert.That(rules.Miss, Is.EqualTo(1));
        }
        [Test]
        public void Queue_PreviousCompletedBatch_AcceptsDelayedHeadAndEarlyReleaseBeforeTail()
        {
            var rules = Hold(); var queue = new RhythmInputQueue(); queue.Begin(1);
            queue.Drain(rules, 0.98, null);
            // 送达时 DSP 已到 1.08；输入批次水位仍在 0.98，保留原事件 1.00 判定。
            queue.Enqueue(new RhythmHitIntent(0, 1, RhythmInputEdge.Press, 1));
            queue.Drain(rules, 1.01, null);
            Assert.That(rules.IsHolding(0), Is.True);
            queue.Drain(rules, 1.95, null);
            // 送达时 DSP 已越过尾部，但早放事件没有被自动尾部结算抢先吞掉。
            queue.Enqueue(new RhythmHitIntent(0, 1.99, RhythmInputEdge.Release, 1));
            queue.Drain(rules, 2.02, null);
            Assert.That(rules.Miss, Is.EqualTo(1));
            Assert.That(rules.Perfect, Is.Zero);
            Assert.That(queue.LateInputs, Is.Zero);
        }
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void Hold_WithQueuedEdgesAtDifferentFrameRates_CompletesIdentically(int frameRate)
        {
            var rules = Hold(100); var queue = new RhythmInputQueue(); queue.Begin(1);
            queue.Enqueue(new RhythmHitIntent(0, 1.1, RhythmInputEdge.Press, 1));
            queue.Enqueue(new RhythmHitIntent(0, 2.1, RhythmInputEdge.Release, 1));
            for (int i = 0; i <= frameRate * 3; i++) queue.Drain(rules, (double)i / frameRate, null);
            Assert.That(rules.Perfect, Is.EqualTo(1));
            Assert.That(rules.Miss, Is.Zero);
            Assert.That(queue.LateInputs, Is.Zero);
        }
        [Test]
        public void Calibration_OutliersAreExcluded_AndRawBiasIsNotAddedToOldOffset()
        {
            var targets = new double[32]; for (int i = 0; i < targets.Length; i++) targets[i] = i + 1;
            var estimator = new RhythmCalibrationEstimator(targets, 24, 250, 30);
            for (int i = 0; i < targets.Length; i++) estimator.Add(targets[i] + (i < 28 ? 0.08 : 0.22));
            double offset; double mad; int accepted;
            Assert.That(estimator.TryEstimate(out offset, out mad, out accepted), Is.True);
            Assert.That(offset, Is.EqualTo(80).Within(1e-6));
            Assert.That(accepted, Is.EqualTo(28));
        }
        [Test]
        public void Calibration_TooFewOrUnstableSamples_DoesNotProduceCandidate()
        {
            var targets = new double[32]; for (int i = 0; i < targets.Length; i++) targets[i] = i + 1;
            var estimator = new RhythmCalibrationEstimator(targets, 24, 250, 30);
            estimator.Add(1.08); estimator.Add(1.09);
            double offset; double mad; int accepted;
            Assert.That(estimator.TryEstimate(out offset, out mad, out accepted), Is.False);
            Assert.That(estimator.Count, Is.EqualTo(1));
            var unstable = new RhythmCalibrationEstimator(targets, 24, 250, 30);
            for (int i = 0; i < targets.Length; i++) unstable.Add(targets[i] + (i % 2 == 0 ? -0.1 : 0.1));
            Assert.That(unstable.TryEstimate(out offset, out mad, out accepted), Is.False);
            Assert.That(mad, Is.GreaterThan(30));
        }
        [TestCase(-100)]
        [TestCase(0)]
        [TestCase(100)]
        public void StraightTrack_VisualOffsetAndHoldEndpoints_UseLinearSongTime(float visualOffset)
        {
            double atLine = 1 + visualOffset / 1000d;
            Assert.That(RhythmTrackGraphic.PositionAt(1, atLine, visualOffset, 2.5f), Is.Zero.Within(1e-6));
            Assert.That(RhythmTrackGraphic.PositionAt(1, atLine - 2.5, visualOffset, 2.5f), Is.EqualTo(1).Within(1e-6));
            Assert.That(RhythmTrackGraphic.PositionAt(1, atLine + 0.25, visualOffset, 2.5f), Is.EqualTo(-0.1).Within(1e-6));
            float head = RhythmTrackGraphic.PositionAt(1, atLine - 0.5, visualOffset, 2.5f);
            float tail = RhythmTrackGraphic.PositionAt(2, atLine - 0.5, visualOffset, 2.5f);
            Assert.That(tail - head, Is.EqualTo(0.4).Within(1e-6));
            float earlier = RhythmTrackGraphic.PositionAt(1, atLine - 0.75, visualOffset, 2.5f);
            float later = RhythmTrackGraphic.PositionAt(1, atLine - 0.25, visualOffset, 2.5f);
            Assert.That(earlier - head, Is.EqualTo(head - later).Within(1e-6));
            Assert.That(RhythmTrackGraphic.PositionAt(1, atLine - 0.14, visualOffset, 2.5f), Is.EqualTo(0.14 / 2.5).Within(1e-6));
            Assert.That(RhythmTrackGraphic.PositionAt(1, atLine + 0.14, visualOffset, 2.5f), Is.EqualTo(-0.14 / 2.5).Within(1e-6));
            var rules = Create(); rules.Hit(new RhythmHitIntent(0, 1)); // 视觉值不改变判定。
            Assert.That(rules.Perfect, Is.EqualTo(1));
        }
        [TestCase(600, 650)]
        [TestCase(360, 480)]
        [TestCase(900, 900)]
        public void StraightTrack_MeshKeepsEqualWidthsHoldLengthAndLineAtWindowSizes(float width, float height)
        {
            var host = new GameObject("StraightTrackTest", typeof(RectTransform), typeof(CanvasRenderer), typeof(RhythmTrackGraphic));
            try
            {
                var graphic = host.GetComponent<RhythmTrackGraphic>();
                graphic.rectTransform.pivot = new Vector2(0.5f, 0);
                graphic.rectTransform.sizeDelta = new Vector2(width, height);
                var populate = typeof(RhythmTrackGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic);
                using var mesh = new VertexHelper();
                graphic.Configure(true);
                populate.Invoke(graphic, new object[] { mesh });
                Assert.That(mesh.currentVertCount, Is.EqualTo(16));
                for (int lane = 0; lane < 4; lane++)
                {
                    var a = Vertex(mesh, lane * 4); var b = Vertex(mesh, lane * 4 + 1);
                    var c = Vertex(mesh, lane * 4 + 2); var d = Vertex(mesh, lane * 4 + 3);
                    Assert.That(c.x, Is.EqualTo(b.x).Within(1e-4));
                    Assert.That(d.x, Is.EqualTo(a.x).Within(1e-4));
                    Assert.That(b.x - a.x, Is.EqualTo(width * 0.234f).Within(1e-4));
                }
                graphic.Configure(false);
                graphic.Show(2, 0.2f, 0.6f, true, Color.white);
                populate.Invoke(graphic, new object[] { mesh });
                Assert.That(mesh.currentVertCount, Is.EqualTo(8));
                Assert.That(Vertex(mesh, 2).y - Vertex(mesh, 0).y, Is.EqualTo(height * 0.4f).Within(1e-4));
                Assert.That(Vertex(mesh, 2).x, Is.EqualTo(Vertex(mesh, 1).x).Within(1e-4));
                float noteWidth = Vertex(mesh, 5).x - Vertex(mesh, 4).x;
                graphic.Show(2, 0, 0, false, Color.white);
                populate.Invoke(graphic, new object[] { mesh });
                Assert.That((Vertex(mesh, 0).y + Vertex(mesh, 2).y) * 0.5f, Is.Zero.Within(1e-4));
                Assert.That(Vertex(mesh, 1).x - Vertex(mesh, 0).x, Is.EqualTo(noteWidth).Within(1e-4));
                Assert.That((Vertex(mesh, 0).x + Vertex(mesh, 1).x) * 0.5f, Is.EqualTo(width * 0.125f).Within(1e-4));
                Assert.That(Vertex(mesh, 2).y - Vertex(mesh, 0).y, Is.EqualTo(20).Within(1e-4));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
        private static Vector3 Vertex(VertexHelper mesh, int index)
        {
            var vertex = new UIVertex(); mesh.PopulateUIVertex(ref vertex, index); return vertex.position;
        }
        [Test]
        public void Practice_DefaultConfig_HasTwoTapTwoHoldAndIndependentAlignment()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<RhythmConfig>();
            try
            {
                var rules = config.CreatePracticeRules(100, null);
                Assert.That(rules.Count, Is.EqualTo(4));
                Assert.That(rules.NoteType(0), Is.EqualTo(RhythmNoteType.Tap));
                Assert.That(rules.NoteType(1), Is.EqualTo(RhythmNoteType.Tap));
                Assert.That(rules.NoteType(2), Is.EqualTo(RhythmNoteType.Hold));
                Assert.That(rules.NoteType(3), Is.EqualTo(RhythmNoteType.Hold));
                Assert.That(rules.LastEndSeconds, Is.EqualTo(8));
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }
    }
}
