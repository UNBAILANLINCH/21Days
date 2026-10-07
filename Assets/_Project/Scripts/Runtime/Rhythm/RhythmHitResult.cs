// 职责：判定的只读结果。现有战斗结果没有节奏误差；不能把音游语义加进其它玩法。
namespace Game.Rhythm
{
    public enum RhythmGrade { None, Perfect, Good, Miss }
    public readonly struct RhythmHitResult
    {
        public int Note { get; }
        public RhythmGrade Grade { get; }
        public double RawErrorMs { get; }
        public double ErrorMs { get; }
        public bool HoldStarted { get; }
        public RhythmHitResult(int note, RhythmGrade grade, double rawErrorMs, double errorMs, bool holdStarted = false)
        { Note = note; Grade = grade; RawErrorMs = rawErrorMs; ErrorMs = errorMs; HoldStarted = holdStarted; }
    }
}
