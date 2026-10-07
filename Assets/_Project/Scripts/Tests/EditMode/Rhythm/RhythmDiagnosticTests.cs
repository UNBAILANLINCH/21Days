// 职责：验证诊断契约与真实 Queue/Rules 的一致性；不访问编辑器、音频设备或正式存档。
using System;
using System.IO;
using System.Linq;
using Game.Rhythm;
using NUnit.Framework;
using Data = Game.Rhythm.RhythmDiagnosticData;

namespace Game.Tests.EditMode.Rhythm
{
    public sealed class RhythmDiagnosticTests
    {
        private static Data.Header Header(double chart = 0, double input = 0, double visual = 0) =>
            new Data.Header(7, "practice-v2", "reference", "test-1", "pure-logic", "Dynamic",
                16, 100, 200, 0.0001, chart, input, visual);
        private static RhythmNoteData[] Notes() => new[] {
            new RhythmNoteData("tap", 0, 1000),
            new RhythmNoteData("hold", 1, 1500, RhythmNoteType.Hold, 500),
            new RhythmNoteData("miss", 2, 3000) };
        private static void Input(RhythmDiagnosticSession s, int lane, double song,
            RhythmInputEdge edge = RhythmInputEdge.Press, int session = 7, double deliveryDelay = 0)
        {
            var intent = new RhythmHitIntent(lane, song, edge, session);
            s.Enqueue(in intent, 200 + song, 200 + song + deliveryDelay);
        }
        private static void Drain(RhythmDiagnosticSession s, double previous, double current, double watermark, double dsp = 999) =>
            s.Drain(previous, current, watermark, 200 + current, dsp);

        [Test]
        public void Replay_NormalTapHoldMiss_PreservesResultsAndNullableAutomaticErrors()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes());
            Input(s, 0, 1); Input(s, 1, 1.5);
            Drain(s, 0, 2.1, 2.1); Drain(s, 2.1, 3.2, 3.2);
            s.End(Data.Reason.Finished);
            var replay = RhythmDiagnosticReplay.Run(s.Snapshot());
            Assert.That(replay.Matches, Is.True);
            Assert.That(replay.Score, Is.EqualTo(2000));
            Assert.That(replay.MaxCombo, Is.EqualTo(2));
            Assert.That(replay.Replayed.Judgements.Select(j => j.Cause), Is.EqualTo(new[] {
                Data.Reason.Hit, Data.Reason.HoldHead, Data.Reason.AutomaticTail, Data.Reason.AutomaticMiss }));
            Assert.That(replay.Replayed.Judgements.Last().RawErrorMs, Is.Null);
            Assert.That(replay.Replayed.Judgements.Last().CorrectedErrorMs, Is.Null);
            Assert.That(replay.Replayed.Judgements[1].ScoreDelta, Is.Zero);
            Assert.That(replay.Replayed.Judgements[2].NoteId, Is.EqualTo("hold"));
            Assert.That(replay.ProvesPhysicalLatency, Is.False);
        }

        [Test]
        public void Replay_StalledBatchAndUnorderedDelivery_UsesEventTimeAndRecordedBoundary()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes());
            Input(s, 1, 1.5, deliveryDelay: 0.2); Input(s, 0, 1, deliveryDelay: 0.7);
            Drain(s, 0.8, 1.7, 0.8, 9999);
            Assert.That(s.Rules.Score, Is.Zero);
            Drain(s, 1.7, 2.3, 1.7, 99999);
            Assert.That(s.Rules.Perfect, Is.EqualTo(1));
            Assert.That(s.Rules.IsHolding(1), Is.True);
            s.End(Data.Reason.Stopped);
            var data = s.Snapshot(); var replay = RhythmDiagnosticReplay.Run(data);
            Assert.That(replay.Matches, Is.True);
            Assert.That(replay.Replayed.Judgements[0].InputSequence, Is.EqualTo(2));
            Assert.That(replay.Replayed.Judgements[0].RawErrorMs, Is.EqualTo(0));
            Assert.That(data.Commands[1].CapturedRealtime - data.Commands[1].RawEventRealtime, Is.EqualTo(0.7).Within(1e-9));
        }

        [Test]
        public void Replay_LateInputAndOldSession_PreservesRejectionWithoutClamping()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes());
            Drain(s, 0, 1.2, 1.2);
            Input(s, 0, 1); Input(s, 1, 1.5, session: 6);
            Drain(s, 1.2, 1.6, 1.6); s.End(Data.Reason.FocusLost);
            var r = RhythmDiagnosticReplay.Run(s.Snapshot());
            Assert.That(r.Matches, Is.True);
            Assert.That(r.LateInputs, Is.EqualTo(1));
            Assert.That(r.Replayed.Judgements.Single(j => j.Cause == Data.Reason.LateInput).SongSeconds, Is.EqualTo(1));
            Assert.That(r.Replayed.Judgements.Any(j => j.Cause == Data.Reason.StaleSession), Is.True);
            Assert.That(r.Score, Is.Zero);
        }

        [TestCase(-100)]
        [TestCase(0)]
        [TestCase(100)]
        public void Replay_Offsets_SeparatesChartInputAndVisual(double offset)
        {
            var s = new RhythmDiagnosticSession(Header(100, offset, 200), Notes());
            Input(s, 0, 1.1 + offset / 1000); Drain(s, 0, 2, 2); s.End(Data.Reason.Stopped);
            var r = RhythmDiagnosticReplay.Run(s.Snapshot()); var hit = r.Replayed.Judgements.First();
            Assert.That(r.Matches, Is.True);
            Assert.That(hit.TargetHeadSeconds, Is.EqualTo(1.1));
            Assert.That(hit.RawErrorMs, Is.EqualTo(offset).Within(1e-9));
            Assert.That(hit.CorrectedErrorMs, Is.EqualTo(0).Within(1e-9));
            Assert.That(hit.Grade, Is.EqualTo(RhythmGrade.Perfect));
        }

        [Test]
        public void Replay_SameTimestampPressRelease_PreservesArrivalOrderAndEarlyReleaseMiss()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes());
            Input(s, 1, 1.5); Input(s, 1, 1.5, RhythmInputEdge.Release);
            Input(s, 0, 1.6); Input(s, 0, 1.6);
            Drain(s, 0, 1.8, 1.8); s.End(Data.Reason.Stopped);
            var r = RhythmDiagnosticReplay.Run(s.Snapshot());
            Assert.That(r.Matches, Is.True);
            Assert.That(s.Rules.Miss, Is.EqualTo(2));
            Assert.That(r.Replayed.Judgements.Single(j => j.Cause == Data.Reason.EarlyRelease).NoteId, Is.EqualTo("hold"));
            Assert.That(r.Replayed.Judgements.Count(j => j.Cause == Data.Reason.DuplicateEdge), Is.EqualTo(1));
        }

        [Test]
        public void Session_EndedAndRestarted_IsolatesPendingAndOldInputs()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes());
            Input(s, 0, 1); s.End(Data.Reason.Restart);
            Drain(s, 0, 2, 2); Input(s, 0, 1);
            Assert.That(s.Rules.Score, Is.Zero);
            Assert.That(s.Snapshot().Commands.Count, Is.EqualTo(2));
            var r = RhythmDiagnosticReplay.Run(s.Snapshot());
            Assert.That(r.Matches, Is.True); Assert.That(r.PendingInputs, Is.EqualTo(1));
            var restarted = new RhythmDiagnosticSession(new Data.Header(8, "chart", "audio", "v1", "test", "Dynamic", 0, 1, 1, 0), Notes());
            Input(restarted, 0, 1); Drain(restarted, 0, 2, 2);
            Assert.That(restarted.Rules.Score, Is.Zero);
        }

        [Test]
        public void Recording_CapacityReached_SealsReplayablePrefixAndKeepsGameRunning()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes(), commandLimit: 2);
            Input(s, 0, 1); Drain(s, 0, 1, 1);
            Input(s, 1, 1.5); Drain(s, 1, 2.1, 2.1);
            var snapshot = s.Snapshot();
            Assert.That(snapshot.Commands.Count, Is.EqualTo(3));
            Assert.That(s.EndReason, Is.EqualTo(Data.Reason.RecordingLimit));
            Assert.That(s.Rules.Score, Is.EqualTo(2000));
            var r = RhythmDiagnosticReplay.Run(snapshot);
            Assert.That(r.Matches, Is.True); Assert.That(r.Score, Is.EqualTo(1000));
            Assert.That(r.IsComplete, Is.False);
        }

        [Test]
        public void Codec_RoundTripAndRepeatedReplay_ProducesIdenticalBytesAndResults()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes());
            Input(s, 0, 1); s.SampleBridge(201, 101.001, 0.0002);
            Drain(s, 0, 3.2, 3.2, 103.202); s.End(Data.Reason.AudioChanged);
            using (var first = new MemoryStream())
            using (var second = new MemoryStream())
            {
                RhythmDiagnosticCodec.Write(first, s.Snapshot()); first.Position = 0;
                var restored = RhythmDiagnosticCodec.Read(first);
                RhythmDiagnosticCodec.Write(second, restored);
                Assert.That(second.ToArray(), Is.EqualTo(first.ToArray()));
                for (int i = 0; i < 5; i++) Assert.That(RhythmDiagnosticReplay.Run(restored).Matches, Is.True);
                var stats = RhythmDiagnosticReplay.SummarizeBridge(restored);
                Assert.That(stats.Count, Is.EqualTo(2));
                Assert.That(stats.MedianResidualMs, Is.EqualTo(1.5).Within(1e-8));
                Assert.That(stats.P95AbsoluteResidualMs, Is.EqualTo(2).Within(1e-8));
                Assert.That(stats.MaxSampleWidthMs, Is.EqualTo(0.2));
            }
        }

        [Test]
        public void Codec_UnsupportedVersionTruncationAndTrailingBytes_AreRejected()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes()); s.End(Data.Reason.DeviceChanged);
            using (var stream = new MemoryStream())
            {
                RhythmDiagnosticCodec.Write(stream, s.Snapshot()); byte[] bytes = stream.ToArray();
                byte[] badVersion = (byte[])bytes.Clone(); badVersion[4] = 99;
                Assert.Throws<InvalidDataException>(() => RhythmDiagnosticCodec.Read(new MemoryStream(badVersion)));
                Assert.Throws<EndOfStreamException>(() => RhythmDiagnosticCodec.Read(new MemoryStream(bytes.Take(bytes.Length - 1).ToArray())));
                Assert.Throws<InvalidDataException>(() => RhythmDiagnosticCodec.Read(new MemoryStream(bytes.Concat(new byte[] { 0 }).ToArray())));
            }
        }

        [TestCase(Data.Reason.Stopped)]
        [TestCase(Data.Reason.AudioPaused)]
        [TestCase(Data.Reason.ApplicationPaused)]
        [TestCase(Data.Reason.InputModeChanged)]
        [TestCase(Data.Reason.ClockDiscontinuity)]
        [TestCase(Data.Reason.InvalidClock)]
        [TestCase(Data.Reason.ClockRollback)]
        [TestCase(Data.Reason.LateInput)]
        public void Codec_EndReason_RoundTripRetainsSpecificCause(Data.Reason reason)
        {
            Assert.That((int)Data.Reason.Stopped, Is.EqualTo(16));
            Assert.That((int)Data.Reason.RecordingLimit, Is.EqualTo(17));
            var session = new RhythmDiagnosticSession(Header(), Notes());
            session.End(reason);
            using (var stream = new MemoryStream())
            {
                RhythmDiagnosticCodec.Write(stream, session.Snapshot());
                stream.Position = 0;
                var restored = RhythmDiagnosticCodec.Read(stream);
                Assert.That(restored.Commands.Last().EndReason, Is.EqualTo(reason));
                var replay = RhythmDiagnosticReplay.Run(restored);
                Assert.That(replay.Matches, Is.True);
                Assert.That(replay.Replayed.Commands.Last().EndReason, Is.EqualTo(reason));
            }
        }

        [Test]
        public void Session_InvalidTimeLaneOrWatermark_DoesNotRecordOrAdvance()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes());
            Assert.Throws<ArgumentException>(() => Input(s, 4, 1));
            Assert.Throws<ArgumentException>(() => Input(s, 0, double.NaN));
            Drain(s, 0, 1, 1);
            Assert.Throws<ArgumentException>(() => Drain(s, 1, 2, 0.5));
            Assert.That(s.Snapshot().Commands.Count, Is.EqualTo(1));
        }

        [Test]
        public void Replay_ChangedJudgement_IsDetected()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes()); Input(s, 0, 1); Drain(s, 0, 1, 1);
            var data = s.Snapshot();
            var corrupted = new Data(data.SessionHeader, data.Notes.ToArray(), data.Commands.ToArray(), Array.Empty<Data.Judgement>());
            var r = RhythmDiagnosticReplay.Run(corrupted);
            Assert.That(r.Matches, Is.False); Assert.That(r.FirstMismatch, Is.Zero);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void Replay_FrameRateWithTwoHundredMillisecondStall_MatchesDirectQueue(int fps)
        {
            var s = new RhythmDiagnosticSession(Header(), Notes());
            var baseline = new RhythmRules(Notes(), 65, 140, 0, 0, null);
            var queue = new RhythmInputQueue(); queue.Begin(7);
            var inputs = new[] { new RhythmHitIntent(0, 1, session: 7), new RhythmHitIntent(1, 1.5, session: 7) };
            int next = 0; double previous = 0;
            for (int frame = 1; frame <= fps * 4; frame++)
            {
                double now = (double)frame / fps;
                if (now > 0.9 && now < 1.2) continue;
                while (next < inputs.Length && inputs[next].SongSeconds <= now)
                {
                    var input = inputs[next++];
                    s.Enqueue(in input, 200 + input.SongSeconds, 200 + now); queue.Enqueue(in input);
                }
                s.Drain(previous, now, previous, 200 + now, 100 + now);
                queue.Drain(baseline, previous, null); previous = now;
            }
            s.End(Data.Reason.Finished); var r = RhythmDiagnosticReplay.Run(s.Snapshot());
            Assert.That(r.Matches, Is.True); Assert.That(r.Score, Is.EqualTo(2000));
            Assert.That(s.Rules.Score, Is.EqualTo(baseline.Score));
            Assert.That(s.Rules.MaxCombo, Is.EqualTo(baseline.MaxCombo));
            Assert.That(s.Rules.Miss, Is.EqualTo(baseline.Miss));
            Assert.That(r.LateInputs, Is.Zero);
        }

        [Test]
        public void Replay_AutomaticResolutionBeforeInput_RetainsDeadlineOrderAndScoreDelta()
        {
            var notes = new[] { new RhythmNoteData("tap-miss", 0, 1000),
                new RhythmNoteData("hold-success", 1, 500, RhythmNoteType.Hold, 640),
                new RhythmNoteData("tap-hit", 2, 1200) };
            var s = new RhythmDiagnosticSession(Header(), notes);
            Input(s, 1, 0.5); Input(s, 2, 1.2); Drain(s, 0, 1.3, 1.3);
            s.End(Data.Reason.Finished); var data = s.Snapshot();
            Assert.That(RhythmDiagnosticReplay.Run(data).Matches, Is.True);
            Assert.That(data.Judgements.Select(j => j.Cause), Is.EqualTo(new[] { Data.Reason.HoldHead,
                Data.Reason.AutomaticTail, Data.Reason.AutomaticMiss, Data.Reason.Hit }));
            Assert.That(data.Judgements.Sum(j => j.ScoreDelta), Is.EqualTo(s.Rules.Score));
            Assert.That(data.Judgements.Last().Combo, Is.EqualTo(s.Rules.Combo));
        }

        [Test]
        public void Replay_MissingCommandOrPostTerminationInput_IsRejected()
        {
            var s = new RhythmDiagnosticSession(Header(), Notes());
            Input(s, 0, 1); Drain(s, 0, 1, 1); s.End(Data.Reason.Stopped);
            var data = s.Snapshot();
            Assert.Throws<ArgumentException>(() => RhythmDiagnosticReplay.Run(new Data(data.SessionHeader,
                data.Notes.ToArray(), data.Commands.Skip(1).ToArray(), data.Judgements.ToArray())));
            Assert.Throws<ArgumentException>(() => RhythmDiagnosticReplay.Run(new Data(data.SessionHeader,
                data.Notes.ToArray(), data.Commands.Concat(new[] { new Data.Command(Data.Kind.Input, 4) }).ToArray(), data.Judgements.ToArray())));
        }
    }
}
