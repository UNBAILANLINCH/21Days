// 职责：按记录操作与批次水位调用原判定器；它验证逻辑一致性，不证明物理声学延迟。
using System;
using System.Collections.Generic;
using Game.Core.Simulation;
using Data = Game.Rhythm.RhythmDiagnosticData;

namespace Game.Rhythm
{
    public static class RhythmDiagnosticReplay
    {
        public sealed class Report
        {
            public Data Replayed { get; }
            public bool Matches { get; }
            public int FirstMismatch { get; }
            public int Score { get; }
            public int MaxCombo { get; }
            public int LateInputs { get; }
            public int PendingInputs { get; }
            public bool IsComplete { get; }
            public bool ProvesPhysicalLatency => false;
            internal Report(Data replayed, bool matches, int mismatch, RhythmDiagnosticSession session, int pending)
            {
                Replayed = replayed; Matches = matches; FirstMismatch = mismatch;
                Score = session.Rules.Score; MaxCombo = session.Rules.MaxCombo; LateInputs = session.LateInputs;
                PendingInputs = pending;
                IsComplete = session.EndReason != Data.Reason.None && session.EndReason != Data.Reason.RecordingLimit;
            }
        }

        public readonly struct BridgeSummary
        {
            public int Count { get; }
            public double? MedianResidualMs { get; }
            public double? P95AbsoluteResidualMs { get; }
            public double? MaxAbsoluteResidualMs { get; }
            public double? MaxSampleWidthMs { get; }
            internal BridgeSummary(int count, double? median, double? p95, double? max, double? width)
            { Count = count; MedianResidualMs = median; P95AbsoluteResidualMs = p95; MaxAbsoluteResidualMs = max; MaxSampleWidthMs = width; }
        }

        public static Report Run(Data data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var notes = new RhythmNoteData[data.Notes.Count];
            for (int i = 0; i < notes.Length; i++) notes[i] = data.Notes[i];
            var session = new RhythmDiagnosticSession(data.SessionHeader, notes);
            long previousSequence = 0; bool ended = false;
            var pending = new List<double>();
            foreach (var c in data.Commands)
            {
                if (ended || c.Sequence != previousSequence + 1 || !Enum.IsDefined(typeof(Data.Kind), c.Type))
                    throw new ArgumentException("记录顺序非法或结束后仍有事件");
                Data.Check(c.Intent.SongSeconds, c.RawEventRealtime, c.CapturedRealtime, c.Dsp,
                    c.PreviousBoundary, c.CurrentBoundary, c.ReleasedWatermark, c.SampleWidthSeconds);
                if (c.SampleWidthSeconds < 0) throw new ArgumentException("采样宽度非法");
                previousSequence = c.Sequence;
                switch (c.Type)
                {
                    case Data.Kind.Input:
                        var intent = c.Intent;
                        session.Enqueue(in intent, c.RawEventRealtime, c.CapturedRealtime);
                        if (intent.Session == data.SessionHeader.Session) pending.Add(intent.SongSeconds);
                        break;
                    case Data.Kind.Batch:
                        session.Drain(c.PreviousBoundary, c.CurrentBoundary, c.ReleasedWatermark, c.CapturedRealtime, c.Dsp);
                        pending.RemoveAll(time => time <= c.ReleasedWatermark);
                        break;
                    case Data.Kind.Bridge:
                        session.SampleBridge(c.CapturedRealtime, c.Dsp, c.SampleWidthSeconds); break;
                    case Data.Kind.End:
                        session.End(c.EndReason); ended = true; break;
                    default: throw new ArgumentException("未知诊断命令");
                }
            }
            // 本版本序号连续；已拒绝缺失命令，不能偷偷将输入重新编号。
            var replayed = session.Snapshot();
            int mismatch = -1;
            int count = GameMath.Min(replayed.Judgements.Count, data.Judgements.Count);
            for (int i = 0; i < count; i++)
                if (!Same(replayed.Judgements[i], data.Judgements[i])) { mismatch = i; break; }
            if (mismatch < 0 && replayed.Judgements.Count != data.Judgements.Count) mismatch = count;
            return new Report(replayed, mismatch < 0, mismatch, session, pending.Count);
        }

        public static BridgeSummary SummarizeBridge(Data data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var residuals = new List<double>(); var absolute = new List<double>();
            double width = data.SessionHeader.BridgeWidthSeconds * 1000;
            var h = data.SessionHeader;
            foreach (var c in data.Commands)
            {
                if (c.Type != Data.Kind.Bridge && c.Type != Data.Kind.Batch) continue;
                double residual = ((c.Dsp - h.ScheduledDsp) - (c.CapturedRealtime - h.ScheduledRealtime)) * 1000;
                Data.Check(residual); residuals.Add(residual); absolute.Add(GameMath.Abs(residual));
                if (c.SampleWidthSeconds * 1000 > width) width = c.SampleWidthSeconds * 1000;
            }
            if (residuals.Count == 0) return new BridgeSummary(0, null, null, null, null);
            residuals.Sort(); absolute.Sort(); int n = residuals.Count;
            double median = n % 2 == 0 ? (residuals[n / 2 - 1] + residuals[n / 2]) / 2 : residuals[n / 2];
            return new BridgeSummary(n, median, absolute[(n * 95 + 99) / 100 - 1], absolute[n - 1], width);
        }

        private static bool Same(Data.Judgement a, Data.Judgement b) =>
            a.InputSequence == b.InputSequence && a.NoteId == b.NoteId && a.Lane == b.Lane && a.Grade == b.Grade &&
            a.Cause == b.Cause && a.SongSeconds == b.SongSeconds && a.TargetHeadSeconds == b.TargetHeadSeconds &&
            a.TargetTailSeconds == b.TargetTailSeconds && a.RawErrorMs == b.RawErrorMs &&
            a.CorrectedErrorMs == b.CorrectedErrorMs && a.ScoreDelta == b.ScoreDelta && a.Combo == b.Combo;
    }
}
