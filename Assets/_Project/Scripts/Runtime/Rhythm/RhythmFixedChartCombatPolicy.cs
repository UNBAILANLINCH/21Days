// 职责：固定谱面的战斗资格与成功判定；现有可注入接口不提供身份过滤，个人通关规则不消费战斗结果。
// 配置由调用方传入并复制，不改曲库SO、不定义伤害，也不接Boot。
using System;
namespace Game.Rhythm
{
    public sealed class RhythmFixedChartCombatPolicy : IRhythmCombatPolicy
    {
        public string SongId { get; }
        public string ChartId { get; }
        public string Revision { get; }
        public string RulesetId { get; }
        public string ScoringVersion { get; }
        public int PassScorePercent { get; }
        public RhythmFixedChartCombatPolicy(string songId, string chartId, string revision, string rulesetId,
            string scoringVersion, int passScorePercent = 60)
        {
            RhythmProgressRules.RecordKey(songId, chartId, revision, rulesetId, scoringVersion);
            if (passScorePercent < 1 || passScorePercent > 100) throw new ArgumentOutOfRangeException(nameof(passScorePercent));
            SongId = songId; ChartId = chartId; Revision = revision; RulesetId = rulesetId;
            ScoringVersion = scoringVersion; PassScorePercent = passScorePercent;
        }
        public bool Matches(RhythmRunResult result) => result != null && result.SongId == SongId &&
            result.ChartId == ChartId && result.Revision == Revision && result.RulesetId == RulesetId && result.ScoringVersion == ScoringVersion;
        public bool IsSuccess(RhythmRunResult result) => Matches(result) && result.Mode == RhythmPlayMode.Combat &&
            result.Completion == RhythmRunCompletion.Completed &&
            result.Score >= RhythmProgressRules.RequiredScore(result.NoteCount, PassScorePercent / 100d);
    }
}
