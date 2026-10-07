// 职责：四轨单点音符的纯 C# 判定。没有已有节奏规则可复用，扩展战斗规则会混淆模块职责。
using System;
using System.Collections.Generic;
using Game.Core.Telemetry;
using Game.Core.Simulation;

namespace Game.Rhythm
{
    public sealed class RhythmRules
    {
        private readonly double[] times;
        private readonly RhythmNoteData[] notes;
        private readonly double[] ends;
        private readonly byte[] states; // 0 待按，1 按住，2 已结算
        private readonly RhythmGrade[] headGrades;
        private readonly double perfectMs;
        private readonly double goodMs;
        private readonly ITelemetryScope telemetry;
        private double lastSongSeconds = double.NegativeInfinity;
        public int Perfect { get; private set; }
        public int Good { get; private set; }
        public int Miss { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int CompletedHolds { get; private set; }
        public int Score => Perfect * 1000 + Good * 500;
        public int Count => times.Length;
        public bool Finished => Perfect + Good + Miss == Count;
        public double OffsetMs { get; }
        public double PerfectMs => perfectMs;
        public double GoodMs => goodMs;
        public double ChartOffsetMs { get; }
        public double LastEndSeconds { get; }

        public RhythmRules(double[] times, int[] lanes, double perfectMs, double goodMs, double offsetMs, ITelemetryScope telemetry)
            : this(FromLegacy(times, lanes), perfectMs, goodMs, offsetMs, 0, telemetry) { }

        public RhythmRules(RhythmNoteData[] source, double perfectMs, double goodMs, double offsetMs, double chartOffsetMs, ITelemetryScope telemetry)
        {
            if (source == null || source.Length == 0) throw new ArgumentException("谱面不能为空");
            if (!Finite(perfectMs) || !Finite(goodMs) || perfectMs <= 0 || goodMs < perfectMs || !Finite(offsetMs) || GameMath.Abs(offsetMs) > 300 || !Finite(chartOffsetMs))
                throw new ArgumentOutOfRangeException(nameof(perfectMs));
            notes = (RhythmNoteData[])source.Clone();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < notes.Length; i++)
            {
                var note = notes[i];
                if (note == null || string.IsNullOrWhiteSpace(note.Id) || !ids.Add(note.Id) || note.Lane < 0 || note.Lane > 3 ||
                    !Finite(note.TimeMs) || note.TimeMs < 0 || !Finite(note.DurationMs) || note.DurationMs < 0 ||
                    (note.Type != RhythmNoteType.Tap && note.Type != RhythmNoteType.Hold) ||
                    (note.Type == RhythmNoteType.Tap && note.DurationMs != 0) || (note.Type == RhythmNoteType.Hold && note.DurationMs <= 0) ||
                    !Finite(note.TimeMs + note.DurationMs + chartOffsetMs) || note.TimeMs + chartOffsetMs < 0)
                    throw new ArgumentException("音符 ID 重复或类型、轨道、毫秒时间无效，索引 " + i);
            }
            // 稳定排序只改副本，不写回 SO。
            for (int i = 1; i < notes.Length; i++)
            {
                var note = notes[i]; int j = i;
                while (j > 0 && notes[j - 1].TimeMs > note.TimeMs) { notes[j] = notes[j - 1]; j--; }
                notes[j] = note;
            }
            times = new double[notes.Length]; ends = new double[notes.Length]; states = new byte[notes.Length]; headGrades = new RhythmGrade[notes.Length];
            var previous = new int[] { -1, -1, -1, -1 };
            double lastEnd = 0;
            for (int i = 0; i < notes.Length; i++)
            {
                times[i] = (notes[i].TimeMs + chartOffsetMs) / 1000;
                ends[i] = times[i] + notes[i].DurationMs / 1000;
                int lane = notes[i].Lane; int before = previous[lane];
                if (before >= 0 && (times[i] - times[before] <= goodMs * 2 / 1000 + 1e-9 ||
                    (notes[before].Type == RhythmNoteType.Hold && times[i] - goodMs / 1000 <= ends[before] + 1e-9)))
                    throw new ArgumentException("同轨判定窗或长按占用冲突：" + notes[before].Id + " / " + notes[i].Id);
                previous[lane] = i; if (ends[i] > lastEnd) lastEnd = ends[i];
            }
            LastEndSeconds = lastEnd;
            this.perfectMs = perfectMs;
            this.goodMs = goodMs;
            OffsetMs = offsetMs;
            ChartOffsetMs = chartOffsetMs;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public static RhythmNoteData[] FromLegacy(double[] times, int[] lanes)
        {
            if (times == null || lanes == null || times.Length == 0 || times.Length != lanes.Length)
                throw new ArgumentException("旧谱面时间与轨道必须非空且一一对应");
            var result = new RhythmNoteData[times.Length];
            for (int i = 0; i < times.Length; i++)
            {
                if (!Finite(times[i]) || times[i] < 0 || (i > 0 && times[i] < times[i - 1])) throw new ArgumentException("旧谱面必须按时间升序排列");
                result[i] = new RhythmNoteData("legacy-" + i, lanes[i], times[i] * 1000);
            }
            return result;
        }
        public bool IsResolved(int note) => states[note] == 2;
        public bool IsHolding(int note) => states[note] == 1;
        public double NoteTime(int note) => times[note];
        public double NoteEnd(int note) => ends[note];
        public int NoteLane(int note) => notes[note].Lane;
        public RhythmNoteType NoteType(int note) => notes[note].Type;
        public string NoteId(int note) => notes[note].Id;
        /// <summary>诊断/回放读取原始毫秒记录副本；不重复编译秒时间或修改规则状态。</summary>
        public RhythmNoteData[] CopyNotes() => (RhythmNoteData[])notes.Clone();

        public RhythmHitResult Hit(in RhythmHitIntent intent)
        {
            if (intent.Lane < 0 || intent.Lane > 3 || !Finite(intent.SongSeconds) ||
                (intent.Edge != RhythmInputEdge.Press && intent.Edge != RhythmInputEdge.Release)) throw new ArgumentOutOfRangeException(nameof(intent));
            if (intent.Edge == RhythmInputEdge.Release) return Release(in intent);
            int best = -1;
            double error = double.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                if (states[i] != 0 || notes[i].Lane != intent.Lane) continue;
                if (notes[i].Type == RhythmNoteType.Hold && intent.SongSeconds - OffsetMs / 1000 > ends[i]) continue;
                double candidate = (intent.SongSeconds - times[i]) * 1000 - OffsetMs;
                if (GameMath.Abs(candidate) <= goodMs + 0.000001 && GameMath.Abs(candidate) < GameMath.Abs(error))
                { best = i; error = candidate; }
            }
            if (best < 0) return new RhythmHitResult(-1, RhythmGrade.None, 0, 0);
            RhythmGrade grade = GameMath.Abs(error) <= perfectMs + 0.000001 ? RhythmGrade.Perfect : RhythmGrade.Good;
            bool hold = notes[best].Type == RhythmNoteType.Hold;
            if (hold) { states[best] = 1; headGrades[best] = grade; }
            else Resolve(best, grade);
            telemetry.Track("hit", ("lane", intent.Lane), ("grade", (int)grade), ("error_ms", error));
            return new RhythmHitResult(best, grade, (intent.SongSeconds - times[best]) * 1000, error, hold);
        }

        private RhythmHitResult Release(in RhythmHitIntent intent)
        {
            for (int i = 0; i < Count; i++)
            {
                if (states[i] != 1 || notes[i].Lane != intent.Lane) continue;
                double error = (intent.SongSeconds - ends[i]) * 1000 - OffsetMs;
                var grade = error >= -0.000001 ? headGrades[i] : RhythmGrade.Miss;
                Resolve(i, grade);
                return new RhythmHitResult(i, grade, (intent.SongSeconds - ends[i]) * 1000, error);
            }
            return new RhythmHitResult(-1, RhythmGrade.None, 0, 0);
        }

        public int Advance(double songSeconds)
        {
            if (!Finite(songSeconds) || songSeconds < lastSongSeconds) throw new ArgumentOutOfRangeException(nameof(songSeconds));
            lastSongSeconds = songSeconds;
            int missed = 0;
            // ponytail: 到期项扫描最坏 O(n²)，当前 56 音符足够；长谱面改为预排序结算事件。
            while (true)
            {
                int next = -1;
                double earliest = double.PositiveInfinity;
                for (int i = 0; i < Count; i++)
                {
                    bool due = states[i] == 1
                        ? (songSeconds - ends[i]) * 1000 - OffsetMs >= -0.000001
                        : states[i] == 0 && (songSeconds - times[i]) * 1000 - OffsetMs > goodMs + 0.000001;
                    // 尾部包含边界，漏按不包含；排序也必须使用相同的容差阈值。
                    double deadline = (states[i] == 1 ? ends[i] - 1e-9 : times[i] + (goodMs + 0.000001) / 1000) + OffsetMs / 1000;
                    if (due && deadline < earliest) { next = i; earliest = deadline; }
                }
                if (next < 0) break;
                bool holding = states[next] == 1;
                Resolve(next, holding ? headGrades[next] : RhythmGrade.Miss);
                if (!holding) missed++;
            }
            // 每帧不埋点，漏按汇总在结算事件里报告。
            return missed;
        }

        private void Resolve(int note, RhythmGrade grade)
        {
            states[note] = 2;
            if (grade == RhythmGrade.Miss) { Miss++; Combo = 0; return; }
            if (notes[note].Type == RhythmNoteType.Hold) CompletedHolds++;
            if (grade == RhythmGrade.Perfect) Perfect++; else Good++;
            Combo++;
            MaxCombo = GameMath.Max(MaxCombo, Combo);
        }
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
