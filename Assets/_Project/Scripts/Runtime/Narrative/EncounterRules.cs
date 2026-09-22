// 职责：事件候选的纯条件仲裁与重入策略；怪物感知只报告事实，不能代替剧情内容路由。
using System;
using System.Collections.Generic;

namespace Game.Narrative
{
    public sealed class EncounterRules
    {
        public enum RepeatPolicy { Once, Reenter, RisingCondition }
        public sealed class Rule
        {
            public string Id { get; set; } = string.Empty;
            public string TriggerKind { get; set; } = string.Empty;
            public string TargetKind { get; set; } = string.Empty;
            public int Priority { get; set; }
            public string StoryId { get; set; } = string.Empty;
            public string EntryStageId { get; set; } = string.Empty;
            public RepeatPolicy Repeat { get; set; }
            public NarrativeCondition[][] Conditions { get; set; } = Array.Empty<NarrativeCondition[]>();
        }
        public sealed class Candidate
        {
            public string TriggerId { get; set; } = string.Empty;
            public string TriggerKind { get; set; } = string.Empty;
            public long EntryEpoch { get; set; }
            public EncounterContext Context { get; set; }
        }
        private readonly Rule[] rules;
        private readonly NarrativeRules narrative;
        public EncounterRules(IEnumerable<Rule> source, NarrativeRules narrative)
        {
            this.narrative = narrative ?? throw new ArgumentNullException(nameof(narrative));
            rules = new List<Rule>(source ?? throw new ArgumentNullException(nameof(source))).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Rule rule in rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) ||
                    string.IsNullOrWhiteSpace(rule.TriggerKind) || string.IsNullOrWhiteSpace(rule.StoryId) ||
                    string.IsNullOrWhiteSpace(rule.EntryStageId) || !Enum.IsDefined(typeof(RepeatPolicy), rule.Repeat))
                    throw new ArgumentException("遭遇规则非法");
                NarrativeCondition.Matches(rule.Conditions, new EncounterContext("validate", "", true, false, false, true, false, false));
            }
        }

        // 调用方在安全边界一次提交当前有效候选；忙碌时不缓存过期事件。
        public bool TryActivate(IEnumerable<Candidate> candidates)
        {
            Rule best = null;
            Candidate target = null;
            string bestKey = null;
            string bestEdge = null;
            foreach (Candidate candidate in candidates ?? throw new ArgumentNullException(nameof(candidates)))
            {
                if (candidate == null || candidate.Context == null || candidate.EntryEpoch < 0 || string.IsNullOrWhiteSpace(candidate.TriggerId))
                    throw new ArgumentException("触发候选非法");
                if (!candidate.Context.PlayerAlive || !candidate.Context.TargetAlive) continue;
                Rule match = null;
                int matchCount = 0;
                string matchKey = null;
                string matchEdge = null;
                foreach (Rule rule in rules)
                {
                    if (rule.TriggerKind != candidate.TriggerKind ||
                        (!string.IsNullOrEmpty(rule.TargetKind) && rule.TargetKind != candidate.Context.TargetKind)) continue;
                    string edge = Key(rule.Id, candidate.Context.TargetId);
                    bool matches = NarrativeCondition.Matches(rule.Conditions, candidate.Context);
                    if (!matches) { narrative.SetEdge(edge, false); continue; }
                    if (rule.Repeat == RepeatPolicy.RisingCondition && narrative.ReadEdge(edge)) continue;
                    string key = edge;
                    if (rule.Repeat == RepeatPolicy.Reenter) key = Key(edge, candidate.TriggerId) + ":" + candidate.EntryEpoch;
                    if (rule.Repeat == RepeatPolicy.RisingCondition) key += ":rise:" + (narrative.EdgeCounter(edge) + 1);
                    if (narrative.IsConsumed(key)) continue;
                    if (match != null && rule.Priority == match.Priority) matchCount++;
                    if (match == null || rule.Priority > match.Priority)
                    { match = rule; matchKey = key; matchEdge = edge; matchCount = 1; }
                }
                if (matchCount > 1) throw new InvalidOperationException("同一目标的最高优先级遭遇规则冲突：" + candidate.Context.TargetId);
                if (match != null && (best == null || match.Priority > best.Priority ||
                    (match.Priority == best.Priority && string.CompareOrdinal(candidate.Context.TargetId, target.Context.TargetId) < 0)))
                { best = match; target = candidate; bestKey = matchKey; bestEdge = matchEdge; }
            }
            if (best == null || !narrative.CanEnterEncounter) return false;
            if (!narrative.EnterEncounter(best.StoryId, best.EntryStageId, target.Context.TargetId, bestKey)) return false;
            if (best.Repeat == RepeatPolicy.RisingCondition) narrative.ConsumeEdge(bestEdge);
            return true;
        }
        private static string Key(string first, string second) => first.Length + ":" + first + second.Length + ":" + second;
    }
}
