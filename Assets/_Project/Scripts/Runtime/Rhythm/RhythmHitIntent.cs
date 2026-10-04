// 职责：带歌曲时间戳的四轨按下意图。现有 InputCommand 只有 tick 快照，无法复用格内时间；不改旧录像布局。
namespace Game.Rhythm
{
    public enum RhythmInputEdge { Press, Release }
    public readonly struct RhythmHitIntent
    {
        public int Lane { get; }
        public double SongSeconds { get; }
        public RhythmInputEdge Edge { get; }
        public int Session { get; }
        public RhythmHitIntent(int lane, double songSeconds, RhythmInputEdge edge = RhythmInputEdge.Press, int session = 0)
        { Lane = lane; SongSeconds = songSeconds; Edge = edge; Session = session; }
    }
}
