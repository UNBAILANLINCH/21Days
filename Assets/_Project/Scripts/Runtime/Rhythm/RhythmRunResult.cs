// 职责：单局不可变交付快照；判定规则不承担跨模块结果身份或存档序列化。
using System;
using Newtonsoft.Json;

namespace Game.Rhythm
{
    public enum RhythmPlayMode { FreePlay, Combat, Practice }
    public enum RhythmRunCompletion { Completed, Aborted, TechnicalError }

    public sealed class RhythmRunResult
    {
        public string RunId { get; }
        public string ContextId { get; }
        public RhythmPlayMode Mode { get; }
        public RhythmRunCompletion Completion { get; }
        public string EndReason { get; }
        public string SongId { get; }
        public string ChartId { get; }
        public string Revision { get; }
        public string RulesetId { get; }
        public string ScoringVersion { get; }
        public int NoteCount { get; }
        public int Perfect { get; }
        public int Good { get; }
        public int Miss { get; }
        public int CompletedHolds { get; }
        public int Score { get; }
        public int MaxCombo { get; }
        public double OffsetMs { get; }
        public int Resolved => Perfect + Good + Miss;

        [JsonConstructor]
        public RhythmRunResult(string runId, string contextId, RhythmPlayMode mode, RhythmRunCompletion completion,
            string endReason, string songId, string chartId, string revision, string rulesetId, string scoringVersion,
            int noteCount, int perfect, int good, int miss, int completedHolds, int score, int maxCombo, double offsetMs = 0)
        {
            RunId = runId; ContextId = contextId; Mode = mode; Completion = completion; EndReason = endReason;
            SongId = songId; ChartId = chartId; Revision = revision; RulesetId = rulesetId; ScoringVersion = scoringVersion;
            NoteCount = noteCount; Perfect = perfect; Good = good; Miss = miss; CompletedHolds = completedHolds;
            Score = score; MaxCombo = maxCombo; OffsetMs = offsetMs;
            Validate();
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(RunId) || string.IsNullOrWhiteSpace(EndReason) ||
                !Enum.IsDefined(typeof(RhythmPlayMode), Mode) || !Enum.IsDefined(typeof(RhythmRunCompletion), Completion) ||
                (Mode == RhythmPlayMode.Combat && string.IsNullOrWhiteSpace(ContextId)))
                throw new ArgumentException("单局身份、模式或结束原因无效");
            RhythmProgressRules.RecordKey(SongId, ChartId, Revision, RulesetId, ScoringVersion);
            if (NoteCount < 1 || NoteCount > int.MaxValue / 1000 || Perfect < 0 || Good < 0 || Miss < 0 ||
                (long)Perfect + Good + Miss > NoteCount || CompletedHolds < 0 || CompletedHolds > (long)Perfect + Good ||
                MaxCombo < 0 || MaxCombo > (long)Perfect + Good || Score < 0 ||
                Score != (long)Perfect * 1000 + (long)Good * 500 || double.IsNaN(OffsetMs) || double.IsInfinity(OffsetMs))
                throw new ArgumentException("单局判定统计无效");
            if (Completion == RhythmRunCompletion.Completed && Resolved != NoteCount)
                throw new ArgumentException("完成结果必须结算所有音符");
        }
    }
}
