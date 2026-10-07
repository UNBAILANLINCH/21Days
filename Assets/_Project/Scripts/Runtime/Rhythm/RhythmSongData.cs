// 职责：曲库条目的可序列化元数据；RhythmConfig 复用音频与谱面，不混入另一曲的解锁关系。
using System;
using UnityEngine;

namespace Game.Rhythm
{
    [Serializable]
    public sealed class RhythmSongData
    {
        [SerializeField] private string id;
        [SerializeField] private string title;
        [SerializeField] private string difficulty;
        [SerializeField] private RhythmConfig chart;
        [SerializeField] private string revision = "1";
        [SerializeField] private string rulesetId = "four-lane-tap-hold";
        [SerializeField] private string scoringVersion = "1";
        [SerializeField] private string prerequisiteId;
        [SerializeField, Range(1, 100)] private int passScorePercent = 60;
        public string Id => id;
        public string Title => title;
        public string Difficulty => difficulty;
        public RhythmConfig Chart => chart;
        public string Revision => revision;
        public string RulesetId => rulesetId;
        public string ScoringVersion => scoringVersion;
        public string RecordKey => RhythmProgressRules.RecordKey(id, chart.ChartId, revision, rulesetId, scoringVersion);
        public string PrerequisiteId => prerequisiteId;
        public double PassScoreRatio => passScorePercent / 100d;
        public string ProgressKey => RhythmProgressRules.ProgressKey(id, chart.ChartId, revision);
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(difficulty) ||
                string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(rulesetId) || string.IsNullOrWhiteSpace(scoringVersion) ||
                chart == null || chart.Song == null || chart.Input == null)
                throw new ArgumentException("曲库条目标识或资源缺失");
            RhythmProgressRules.RequiredScore(chart.CreateRules(0, null).Count, PassScoreRatio);
        }
    }
}
