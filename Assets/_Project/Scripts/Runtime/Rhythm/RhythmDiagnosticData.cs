// 职责：独立诊断契约；固定 tick 回放不保留原始输入时间，现有判定 DTO 不承担会话记录。
using System;
using System.Collections.Generic;

namespace Game.Rhythm
{
    public sealed class RhythmDiagnosticData
    {
        public const int FormatVersion = 1;
        public const int RulesVersion = 1;
        public const int MaximumCommands = 65536;
        public const int MaximumNotes = 4096;
        public enum Kind { Input, Batch, Bridge, End }
        public enum Reason { None, Hit, HoldHead, EarlyRelease, AutomaticMiss, AutomaticTail, NoTarget,
            LateInput, DuplicateEdge, StaleSession, Finished, Restart, FocusLost, AudioChanged,
            DeviceChanged, ClockRollback, Stopped, RecordingLimit,
            AudioPaused, ApplicationPaused, InputModeChanged, ClockDiscontinuity, InvalidClock }

        public sealed class Header
        {
            public int Session { get; }
            public string ChartId { get; }
            public string AudioId { get; }
            public string ContentVersion { get; }
            public string Environment { get; }
            public string InputUpdateMode { get; }
            public double ClipStartSeconds { get; }
            public double ScheduledDsp { get; }
            public double ScheduledRealtime { get; }
            public double BridgeWidthSeconds { get; }
            public double ChartOffsetMs { get; }
            public double InputOffsetMs { get; }
            public double VisualOffsetMs { get; }
            public double PerfectMs { get; }
            public double GoodMs { get; }
            public Header(int session, string chartId, string audioId, string contentVersion,
                string environment, string inputUpdateMode, double clipStartSeconds,
                double scheduledDsp, double scheduledRealtime, double bridgeWidthSeconds,
                double chartOffsetMs = 0, double inputOffsetMs = 0, double visualOffsetMs = 0,
                double perfectMs = 65, double goodMs = 140)
            {
                Session = session; ChartId = Text(chartId); AudioId = Text(audioId);
                ContentVersion = Text(contentVersion); Environment = Text(environment);
                InputUpdateMode = Text(inputUpdateMode);
                Check(clipStartSeconds, scheduledDsp, scheduledRealtime, bridgeWidthSeconds,
                    chartOffsetMs, inputOffsetMs, visualOffsetMs, perfectMs, goodMs);
                if (clipStartSeconds < 0 || bridgeWidthSeconds < 0) throw new ArgumentOutOfRangeException(nameof(clipStartSeconds));
                ClipStartSeconds = clipStartSeconds; ScheduledDsp = scheduledDsp;
                ScheduledRealtime = scheduledRealtime; BridgeWidthSeconds = bridgeWidthSeconds;
                ChartOffsetMs = chartOffsetMs; InputOffsetMs = inputOffsetMs; VisualOffsetMs = visualOffsetMs;
                PerfectMs = perfectMs; GoodMs = goodMs;
            }
            public double SongAtRealtime(double realtime) => realtime - ScheduledRealtime;
        }

        public readonly struct Command
        {
            public Kind Type { get; }
            public long Sequence { get; }
            public RhythmHitIntent Intent { get; }
            public double RawEventRealtime { get; }
            public double CapturedRealtime { get; }
            public double Dsp { get; }
            public double PreviousBoundary { get; }
            public double CurrentBoundary { get; }
            public double ReleasedWatermark { get; }
            public double SampleWidthSeconds { get; }
            public Reason EndReason { get; }
            public Command(Kind type, long sequence, RhythmHitIntent intent = default,
                double rawEventRealtime = 0, double capturedRealtime = 0, double dsp = 0,
                double previousBoundary = 0, double currentBoundary = 0, double releasedWatermark = 0,
                double sampleWidthSeconds = 0, Reason endReason = Reason.None)
            {
                Type = type; Sequence = sequence; Intent = intent; RawEventRealtime = rawEventRealtime;
                CapturedRealtime = capturedRealtime; Dsp = dsp; PreviousBoundary = previousBoundary;
                CurrentBoundary = currentBoundary; ReleasedWatermark = releasedWatermark;
                SampleWidthSeconds = sampleWidthSeconds; EndReason = endReason;
            }
        }

        public readonly struct Judgement
        {
            public long InputSequence { get; }
            public string NoteId { get; }
            public int Lane { get; }
            public RhythmGrade Grade { get; }
            public Reason Cause { get; }
            public double SongSeconds { get; }
            public double? TargetHeadSeconds { get; }
            public double? TargetTailSeconds { get; }
            public double? RawErrorMs { get; }
            public double? CorrectedErrorMs { get; }
            public int ScoreDelta { get; }
            public int Combo { get; }
            public Judgement(long inputSequence, string noteId, int lane, RhythmGrade grade, Reason cause,
                double songSeconds, double? targetHeadSeconds, double? targetTailSeconds,
                double? rawErrorMs, double? correctedErrorMs, int scoreDelta, int combo)
            {
                InputSequence = inputSequence; NoteId = noteId; Lane = lane; Grade = grade; Cause = cause;
                SongSeconds = songSeconds; TargetHeadSeconds = targetHeadSeconds; TargetTailSeconds = targetTailSeconds;
                RawErrorMs = rawErrorMs; CorrectedErrorMs = correctedErrorMs; ScoreDelta = scoreDelta; Combo = combo;
            }
        }

        public Header SessionHeader { get; }
        public IReadOnlyList<RhythmNoteData> Notes { get; }
        public IReadOnlyList<Command> Commands { get; }
        public IReadOnlyList<Judgement> Judgements { get; }
        public RhythmDiagnosticData(Header header, RhythmNoteData[] notes, Command[] commands, Judgement[] judgements)
        {
            SessionHeader = header ?? throw new ArgumentNullException(nameof(header));
            if (notes == null || notes.Length == 0 || notes.Length > MaximumNotes || commands == null ||
                commands.Length > MaximumCommands + 1 || judgements == null || judgements.Length > MaximumCommands + MaximumNotes)
                throw new ArgumentException("诊断数据超出限额或缺失");
            var copy = new RhythmNoteData[notes.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                var n = notes[i] ?? throw new ArgumentException("音符为空");
                copy[i] = new RhythmNoteData(Text(n.Id), n.Lane, n.TimeMs, n.Type, n.DurationMs);
            }
            Notes = Array.AsReadOnly(copy); Commands = Array.AsReadOnly((Command[])commands.Clone());
            Judgements = Array.AsReadOnly((Judgement[])judgements.Clone());
        }
        internal static string Text(string value)
        {
            if (value == null || value.Length > 1024) throw new ArgumentException("诊断标识缺失或过长");
            return value;
        }
        internal static void Check(params double[] values)
        {
            foreach (double value in values)
                if (!RhythmRules.Finite(value)) throw new ArgumentException("诊断时间必须有限");
        }
    }
}
