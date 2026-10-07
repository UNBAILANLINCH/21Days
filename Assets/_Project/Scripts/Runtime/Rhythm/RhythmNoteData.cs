// 职责：毫秒制 Tap/Hold 谱面记录。旧并行数组无法表达类型和时长，独立记录避免把序列化字段塞进判定器。
using System;
using UnityEngine;
namespace Game.Rhythm
{
    public enum RhythmNoteType { Tap, Hold }
    [Serializable]
    public sealed class RhythmNoteData
    {
        [SerializeField] private string id;
        [SerializeField] private int lane;
        [SerializeField] private double timeMs;
        [SerializeField] private RhythmNoteType type;
        [SerializeField] private double durationMs;
        public string Id => id;
        public int Lane => lane;
        public double TimeMs => timeMs;
        public RhythmNoteType Type => type;
        public double DurationMs => durationMs;
        public RhythmNoteData(string id, int lane, double timeMs, RhythmNoteType type = RhythmNoteType.Tap, double durationMs = 0)
        { this.id = id; this.lane = lane; this.timeMs = timeMs; this.type = type; this.durationMs = durationMs; }
    }
}
