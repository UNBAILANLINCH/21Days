// 职责：小版本、确定性二进制诊断读写；现有 profile 与固定 tick ReplayFormat 均不适合此契约。
using System;
using System.IO;
using System.Text;
using Data = Game.Rhythm.RhythmDiagnosticData;

namespace Game.Rhythm
{
    public static class RhythmDiagnosticCodec
    {
        private const int Magic = 0x31444852; // RHD1，little endian。
        public const int MaximumBytes = 32 * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static void Write(Stream destination, Data data)
        {
            if (destination == null || data == null) throw new ArgumentNullException();
            // 先检查可回放性，避免导出半个无效记录。调用者拥有目的流与落盘路径。
            if (!RhythmDiagnosticReplay.Run(data).Matches) throw new InvalidDataException("判定记录与回放不一致");
            using (var buffer = new MemoryStream())
            {
                using (var w = new BinaryWriter(buffer, Utf8, true))
                {
                    w.Write(Magic); w.Write(Data.FormatVersion); w.Write(Data.RulesVersion);
                    var h = data.SessionHeader;
                    w.Write(h.Session); Text(w, h.ChartId); Text(w, h.AudioId); Text(w, h.ContentVersion);
                    Text(w, h.Environment); Text(w, h.InputUpdateMode);
                    w.Write(h.ClipStartSeconds); w.Write(h.ScheduledDsp); w.Write(h.ScheduledRealtime);
                    w.Write(h.BridgeWidthSeconds); w.Write(h.ChartOffsetMs); w.Write(h.InputOffsetMs);
                    w.Write(h.VisualOffsetMs); w.Write(h.PerfectMs); w.Write(h.GoodMs);
                    w.Write(data.Notes.Count);
                    foreach (var n in data.Notes)
                    { Text(w, n.Id); w.Write(n.Lane); w.Write(n.TimeMs); w.Write((int)n.Type); w.Write(n.DurationMs); }
                    w.Write(data.Commands.Count);
                    foreach (var c in data.Commands)
                    {
                        w.Write((int)c.Type); w.Write(c.Sequence); w.Write(c.Intent.Lane); w.Write(c.Intent.SongSeconds);
                        w.Write((int)c.Intent.Edge); w.Write(c.Intent.Session);
                        w.Write(c.RawEventRealtime); w.Write(c.CapturedRealtime); w.Write(c.Dsp);
                        w.Write(c.PreviousBoundary); w.Write(c.CurrentBoundary); w.Write(c.ReleasedWatermark);
                        w.Write(c.SampleWidthSeconds); w.Write((int)c.EndReason);
                    }
                    w.Write(data.Judgements.Count);
                    foreach (var j in data.Judgements)
                    {
                        w.Write(j.InputSequence); Text(w, j.NoteId); w.Write(j.Lane); w.Write((int)j.Grade);
                        w.Write((int)j.Cause); w.Write(j.SongSeconds); Nullable(w, j.TargetHeadSeconds);
                        Nullable(w, j.TargetTailSeconds); Nullable(w, j.RawErrorMs); Nullable(w, j.CorrectedErrorMs);
                        w.Write(j.ScoreDelta); w.Write(j.Combo);
                    }
                }
                if (buffer.Length > MaximumBytes) throw new InvalidDataException("诊断导出超出字节限额");
                buffer.Position = 0; buffer.CopyTo(destination);
            }
        }

        public static Data Read(Stream source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            // 限制可定位流，从长度检查到字符串/数组检查均先于分配；不接收无界网络流。
            if (!source.CanSeek || source.Length - source.Position > MaximumBytes) throw new InvalidDataException("诊断流必须可定位且有界");
            using (var r = new BinaryReader(source, Utf8, true))
            {
                if (r.ReadInt32() != Magic || r.ReadInt32() != Data.FormatVersion || r.ReadInt32() != Data.RulesVersion)
                    throw new InvalidDataException("不支持的诊断格式或判定版本");
                var h = new Data.Header(r.ReadInt32(), Text(r), Text(r), Text(r), Text(r), Text(r),
                    Number(r), Number(r), Number(r), Number(r), Number(r), Number(r), Number(r), Number(r), Number(r));
                var notes = new RhythmNoteData[Count(r, Data.MaximumNotes)];
                for (int i = 0; i < notes.Length; i++)
                    notes[i] = new RhythmNoteData(Text(r), r.ReadInt32(), Number(r),
                        EnumValue<RhythmNoteType>(r), Number(r));
                var commands = new Data.Command[Count(r, Data.MaximumCommands + 1)];
                for (int i = 0; i < commands.Length; i++)
                {
                    var kind = EnumValue<Data.Kind>(r); long seq = r.ReadInt64(); int lane = r.ReadInt32(); double song = Number(r);
                    var edge = EnumValue<RhythmInputEdge>(r); int session = r.ReadInt32();
                    commands[i] = new Data.Command(kind, seq, new RhythmHitIntent(lane, song, edge, session),
                        Number(r), Number(r), Number(r), Number(r), Number(r), Number(r), Number(r), EnumValue<Data.Reason>(r));
                }
                var judgements = new Data.Judgement[Count(r, Data.MaximumCommands + Data.MaximumNotes)];
                for (int i = 0; i < judgements.Length; i++)
                    judgements[i] = new Data.Judgement(r.ReadInt64(), Text(r), r.ReadInt32(), EnumValue<RhythmGrade>(r),
                        EnumValue<Data.Reason>(r), Number(r), Nullable(r), Nullable(r), Nullable(r), Nullable(r), r.ReadInt32(), r.ReadInt32());
                if (source.Position != source.Length) throw new InvalidDataException("诊断数据存在尾随字节");
                var data = new Data(h, notes, commands, judgements);
                if (!RhythmDiagnosticReplay.Run(data).Matches) throw new InvalidDataException("诊断判定不一致");
                return data;
            }
        }

        private static int Count(BinaryReader r, int limit)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > limit) throw new InvalidDataException("诊断数组超出限额");
            return n;
        }
        private static double Number(BinaryReader r)
        {
            double value = r.ReadDouble();
            if (!RhythmRules.Finite(value)) throw new InvalidDataException("诊断数值非法");
            return value;
        }
        private static T EnumValue<T>(BinaryReader r) where T : struct
        {
            int value = r.ReadInt32();
            if (!Enum.IsDefined(typeof(T), value)) throw new InvalidDataException("未知枚举值");
            return (T)Enum.ToObject(typeof(T), value);
        }
        private static void Text(BinaryWriter w, string text)
        {
            if (text == null) { w.Write(-1); return; }
            Data.Text(text); byte[] bytes = Utf8.GetBytes(text); w.Write(bytes.Length); w.Write(bytes);
        }
        private static string Text(BinaryReader r)
        {
            int length = r.ReadInt32(); if (length == -1) return null;
            if (length < 0 || length > 4096) throw new InvalidDataException("诊断字符串超出限额");
            byte[] bytes = r.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            string text = Utf8.GetString(bytes); Data.Text(text); return text;
        }
        private static void Nullable(BinaryWriter w, double? value)
        { w.Write(value.HasValue); if (value.HasValue) w.Write(value.Value); }
        private static double? Nullable(BinaryReader r) => r.ReadBoolean() ? Number(r) : (double?)null;
    }
}
