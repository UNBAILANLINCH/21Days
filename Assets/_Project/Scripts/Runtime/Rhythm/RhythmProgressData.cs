// 职责：选曲成绩与达标进度档案；校准档案只管偏移，不能混入关卡解锁状态。
using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Rhythm
{
    public sealed class RhythmProgressData : ISaveData
    {
        public int Version => 2;
        public Dictionary<string, int> BestScores { get; set; } = new Dictionary<string, int>();
        public HashSet<string> ClearedCharts { get; set; } = new HashSet<string>();
        public HashSet<string> UnlockedSongs { get; set; } = new HashSet<string>();
        public Dictionary<string, RhythmRecordData> Records { get; set; } = new Dictionary<string, RhythmRecordData>();
        public HashSet<string> ProcessedRunIds { get; set; } = new HashSet<string>();
        public void Migrate(int fromVersion)
        {
            if (fromVersion < 1 || fromVersion > Version) throw new System.ArgumentOutOfRangeException(nameof(fromVersion));
            // 无法从旧最高分推断 Perfect/Good/Hold；原容器保留为 legacy，不伪造完整局。
            RhythmProgressRules.Normalize(this);
        }
        public RhythmProgressData Copy()
        {
            RhythmProgressRules.Normalize(this);
            var copy = new RhythmProgressData
            {
                BestScores = new Dictionary<string, int>(BestScores),
                ClearedCharts = new HashSet<string>(ClearedCharts),
                UnlockedSongs = new HashSet<string>(UnlockedSongs),
                ProcessedRunIds = new HashSet<string>(ProcessedRunIds)
            };
            foreach (var pair in Records) copy.Records.Add(pair.Key, pair.Value.Copy());
            return copy;
        }
    }
}
