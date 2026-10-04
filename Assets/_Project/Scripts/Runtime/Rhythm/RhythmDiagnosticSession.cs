// 职责：旁路记录并调用现有 Rules/Queue；不复制判定器，不监听键盘，不读写 Profile。
using System;
using System.Collections.Generic;
using Game.Core.Telemetry;
using Data = Game.Rhythm.RhythmDiagnosticData;

namespace Game.Rhythm
{
    public sealed class RhythmDiagnosticSession
    {
        private readonly Data.Header header;
        private readonly RhythmNoteData[] notes;
        private readonly RhythmInputQueue queue = new RhythmInputQueue();
        private readonly List<Data.Command> commands = new List<Data.Command>();
        private readonly List<Data.Command> pending = new List<Data.Command>();
        private readonly List<Data.Judgement> judgements = new List<Data.Judgement>();
        private readonly bool[] pressed = new bool[4];
        private readonly bool[] observedResolved;
        private readonly bool[] observedHolding;
        private readonly RhythmGrade[] headGrades;
        private readonly int commandLimit;
        private long sequence;
        private double watermark = double.NegativeInfinity;
        private int combo;
        private bool ended;
        public RhythmRules Rules { get; }
        public bool IsRecording { get; private set; } = true;
        public Data.Reason EndReason { get; private set; }
        public int LateInputs => queue.LateInputs;

        public RhythmDiagnosticSession(Data.Header header, RhythmNoteData[] notes,
            int commandLimit = Data.MaximumCommands, ITelemetryScope telemetry = null)
        {
            this.header = header ?? throw new ArgumentNullException(nameof(header));
            if (notes == null || notes.Length > Data.MaximumNotes || commandLimit < 1 || commandLimit > Data.MaximumCommands)
                throw new ArgumentOutOfRangeException(nameof(commandLimit));
            this.notes = (RhythmNoteData[])notes.Clone(); this.commandLimit = commandLimit;
            Rules = new RhythmRules(this.notes, header.PerfectMs, header.GoodMs, header.InputOffsetMs, header.ChartOffsetMs, telemetry);
            foreach (var note in notes) Data.Text(note.Id);
            observedResolved = new bool[Rules.Count]; observedHolding = new bool[Rules.Count];
            headGrades = new RhythmGrade[Rules.Count]; queue.Begin(header.Session);
        }

        public bool Enqueue(in RhythmHitIntent intent, double rawEventRealtime, double capturedRealtime)
        {
            Data.Check(intent.SongSeconds, rawEventRealtime, capturedRealtime);
            if (intent.Lane < 0 || intent.Lane > 3 || !Enum.IsDefined(typeof(RhythmInputEdge), intent.Edge))
                throw new ArgumentException("仅接收四轨游戏 Press/Release");
            if (ended) return false;
            var command = new Data.Command(Data.Kind.Input, ++sequence, intent, rawEventRealtime, capturedRealtime);
            bool recorded = Record(command);
            if (recorded)
            {
                if (intent.Session != header.Session) Reject(command, Data.Reason.StaleSession);
                else
                {
                    int i = pending.Count; pending.Add(command);
                    while (i > 0 && pending[i - 1].Intent.SongSeconds > intent.SongSeconds)
                    { pending[i] = pending[i - 1]; i--; }
                    pending[i] = command;
                }
            }
            queue.Enqueue(in intent); return recorded;
        }

        public void Drain(double previousBoundary, double currentBoundary, double releasedWatermark,
            double realtime, double dsp, Action<RhythmHitResult> onResult = null)
        {
            Data.Check(previousBoundary, currentBoundary, releasedWatermark, realtime, dsp);
            if (ended) return;
            if (currentBoundary < previousBoundary || releasedWatermark > currentBoundary || releasedWatermark < watermark)
                throw new ArgumentException("批次边界或释放水位回退");
            var batch = new Data.Command(Data.Kind.Batch, ++sequence, capturedRealtime: realtime, dsp: dsp,
                previousBoundary: previousBoundary, currentBoundary: currentBoundary, releasedWatermark: releasedWatermark);
            if (!Record(batch))
            { queue.Drain(Rules, releasedWatermark, onResult); watermark = releasedWatermark; return; }
            // 只标记 Queue 的过滤原因；判定与边界推进仍完全由 Queue 执行。
            var accepted = new Queue<Data.Command>();
            int consumed = 0; double cursor = watermark;
            for (; consumed < pending.Count && pending[consumed].Intent.SongSeconds <= releasedWatermark; consumed++)
            {
                var input = pending[consumed]; var intent = input.Intent;
                if (intent.SongSeconds < cursor) { Reject(input, Data.Reason.LateInput); continue; }
                cursor = intent.SongSeconds;
                bool down = intent.Edge == RhythmInputEdge.Press;
                if (pressed[intent.Lane] == down) { Reject(input, Data.Reason.DuplicateEdge); continue; }
                pressed[intent.Lane] = down; accepted.Enqueue(input);
            }
            queue.Drain(Rules, releasedWatermark, result =>
            {
                var input = accepted.Dequeue();
                ObserveAutomatic(result.Note, input.Intent.SongSeconds);
                var cause = result.HoldStarted ? Data.Reason.HoldHead : result.Grade == RhythmGrade.None
                    ? Data.Reason.NoTarget : result.Grade == RhythmGrade.Miss ? Data.Reason.EarlyRelease : Data.Reason.Hit;
                Observe(result.Note, result.Grade, cause, input.Sequence, input.Intent.Lane,
                    input.Intent.SongSeconds, result.Note < 0 ? (double?)null : result.RawErrorMs,
                    result.Note < 0 ? (double?)null : result.ErrorMs, !result.HoldStarted);
                if (result.HoldStarted) { observedHolding[result.Note] = true; headGrades[result.Note] = result.Grade; }
                onResult?.Invoke(result);
            });
            ObserveAutomatic(-1, releasedWatermark);
            pending.RemoveRange(0, consumed); watermark = releasedWatermark;
        }

        public bool SampleBridge(double realtime, double dsp, double sampleWidthSeconds = 0)
        {
            Data.Check(realtime, dsp, sampleWidthSeconds);
            if (sampleWidthSeconds < 0) throw new ArgumentOutOfRangeException(nameof(sampleWidthSeconds));
            return !ended && Record(new Data.Command(Data.Kind.Bridge, ++sequence,
                capturedRealtime: realtime, dsp: dsp, sampleWidthSeconds: sampleWidthSeconds));
        }

        public void End(Data.Reason reason)
        {
            if (!Enum.IsDefined(typeof(Data.Reason), reason) ||
                (reason < Data.Reason.Finished && reason != Data.Reason.LateInput))
                throw new ArgumentOutOfRangeException(nameof(reason));
            if (ended) return;
            if (IsRecording)
            {
                commands.Add(new Data.Command(Data.Kind.End, ++sequence, endReason: reason));
                IsRecording = false; EndReason = reason;
            }
            ended = true; queue.Clear(); pending.Clear();
        }

        public Data Snapshot() => new Data(header, notes, commands.ToArray(), judgements.ToArray());

        private bool Record(Data.Command command)
        {
            if (!IsRecording) return false;
            if (commands.Count >= commandLimit)
            {
                commands.Add(new Data.Command(Data.Kind.End, command.Sequence, endReason: Data.Reason.RecordingLimit));
                IsRecording = false; EndReason = Data.Reason.RecordingLimit; pending.Clear(); return false;
            }
            commands.Add(command); return true;
        }

        private void Reject(Data.Command command, Data.Reason reason) =>
            Observe(-1, RhythmGrade.None, reason, command.Sequence, command.Intent.Lane, command.Intent.SongSeconds, null, null, false);

        private void ObserveAutomatic(int excludedNote, double now)
        {
            // Rules 已完成结算；按它的 deadline/稳定索引顺序恢复自动输出，不虚构输入误差。
            while (true)
            {
                int next = -1; double earliest = double.PositiveInfinity;
                for (int i = 0; i < Rules.Count; i++)
                {
                    if (i == excludedNote || observedResolved[i] || !Rules.IsResolved(i)) continue;
                    double deadline = (observedHolding[i] ? Rules.NoteEnd(i) - 1e-9
                        : Rules.NoteTime(i) + (header.GoodMs + 0.000001) / 1000) + header.InputOffsetMs / 1000;
                    if (deadline < earliest) { earliest = deadline; next = i; }
                }
                if (next < 0) return;
                bool hold = observedHolding[next];
                Observe(next, hold ? headGrades[next] : RhythmGrade.Miss,
                    hold ? Data.Reason.AutomaticTail : Data.Reason.AutomaticMiss, 0,
                    Rules.NoteLane(next), now, null, null, true);
            }
        }

        private void Observe(int note, RhythmGrade grade, Data.Reason cause, long inputSequence,
            int lane, double song, double? raw, double? corrected, bool resolves)
        {
            int delta = 0;
            if (note >= 0 && resolves)
            {
                observedResolved[note] = true;
                if (grade == RhythmGrade.Miss) combo = 0;
                else { combo++; delta = grade == RhythmGrade.Perfect ? 1000 : 500; }
            }
            judgements.Add(new Data.Judgement(inputSequence, note < 0 ? null : Rules.NoteId(note), lane,
                grade, cause, song, note < 0 ? (double?)null : Rules.NoteTime(note),
                note < 0 ? (double?)null : Rules.NoteEnd(note), raw, corrected, delta, combo));
        }
    }
}
