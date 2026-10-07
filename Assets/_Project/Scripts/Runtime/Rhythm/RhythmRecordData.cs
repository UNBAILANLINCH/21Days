// 职责：当前可比版本的个人纪录；保存完整同局结果，不能拼接不同轮次的最佳统计。
namespace Game.Rhythm
{
    public sealed class RhythmRecordData
    {
        public RhythmRunResult LastCompletedRun { get; set; }
        public RhythmRunResult BestScoreRun { get; set; }
        public bool Cleared { get; set; }
        public RhythmRecordData Copy() => new RhythmRecordData
        {
            LastCompletedRun = LastCompletedRun,
            BestScoreRun = BestScoreRun,
            Cleared = Cleared
        };
    }
}
