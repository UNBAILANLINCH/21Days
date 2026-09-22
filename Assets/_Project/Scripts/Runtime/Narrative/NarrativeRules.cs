// 职责：纯规则阶段迁移及一层父阶段续接；对白规则和全局 GameFlow 均不拥有这些状态。
using System;
using System.Collections.Generic;
using Game.Core.Telemetry;
using Newtonsoft.Json;

namespace Game.Narrative
{
    public sealed class NarrativeRules
    {
        private readonly Dictionary<string, NarrativeContent> stories = new Dictionary<string, NarrativeContent>(StringComparer.Ordinal);
        private readonly ITelemetryScope telemetry;
        private NarrativeSaveData state = new NarrativeSaveData();
        public NarrativeRules(IEnumerable<NarrativeContent> contents, ITelemetryScope telemetry)
        {
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            foreach (NarrativeContent content in contents ?? throw new ArgumentNullException(nameof(contents))) stories.Add(content.Id, content);
        }
        public long Generation { get; private set; }
        public NarrativeSaveData.Frame Current => state.Current;
        public NarrativeContent.Stage Stage => Current == null ? null : stories[Current.StoryId].Get(Current.StageId);
        public bool CanEnterEncounter => state.Parent == null && (Stage == null || Stage.AllowEncounter);
        public string Outcome => state.Outcome;
        public IEnumerable<string> StoryFlags => state.StoryFlags;

        public void Start(string storyId, string targetId)
        {
            NarrativeContent story = GetStory(storyId);
            Generation++;
            state.Parent = null;
            state.Outcome = string.Empty;
            Enter(storyId, story.Entry, targetId);
        }
        public bool EnterEncounter(string storyId, string entry, string targetId, string consumptionKey)
        {
            if (!CanEnterEncounter || state.ConsumedTriggers.Contains(consumptionKey)) return false;
            GetStory(storyId).Get(entry);
            if (string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(consumptionKey)) throw new ArgumentException("遭遇身份非法");
            state.Parent = state.Current;
            Enter(storyId, entry, targetId);
            state.ConsumedTriggers.Add(consumptionKey);
            return true;
        }
        public bool IsConsumed(string key) => state.ConsumedTriggers.Contains(key);
        public bool ReadEdge(string key) => state.ConditionEdges.TryGetValue(key, out bool value) && value;
        public void SetEdge(string key, bool value) => state.ConditionEdges[key] = value;
        public long EdgeCounter(string key) => state.EdgeCounters.TryGetValue(key, out long value) ? value : 0;
        public void ConsumeEdge(string key)
        {
            state.EdgeCounters[key] = checked(EdgeCounter(key) + 1);
            state.ConditionEdges[key] = true;
        }
        public void SetFlag(string flag)
        {
            if (string.IsNullOrWhiteSpace(flag)) throw new ArgumentException("剧情标记不可为空");
            state.StoryFlags.Add(flag);
        }
        public bool MarkRequestIssued(long activation)
        {
            if (Current == null || Current.ActivationId != activation || Current.RequestIssued) return false;
            Current.RequestIssued = true;
            return true;
        }
        public void ResolveAutomatic(EncounterContext context)
        {
            for (int i = 0; i < 128 && Stage != null; i++)
            {
                if (Stage.Kind == NarrativeContent.StageKind.Condition)
                {
                    Move(NarrativeCondition.Matches(Stage.Conditions, context) ? "True" : "False");
                    continue;
                }
                if (Stage.Kind == NarrativeContent.StageKind.End)
                {
                    string result = Stage.Outcome;
                    if (state.Parent != null)
                    {
                        state.Current = state.Parent;
                        state.Parent = null;
                        if (Stage.Exits.ContainsKey(result)) Move(result);
                        // 无专门出口则恢复原等待，保留旧请求及已完成部分。
                    }
                    else { state.Outcome = result; state.Current = null; }
                    return;
                }
                return;
            }
            if (Stage != null && Stage.Kind == NarrativeContent.StageKind.Condition)
                throw new InvalidOperationException("单次自动剧情推进超过限制");
        }
        public bool Apply(in NarrativeIntent intent)
        {
            if (Current == null || intent.Generation != Generation || intent.ActivationId != Current.ActivationId ||
                intent.TargetId != Current.TargetId || intent.RequestId != Current.ActionRequestId) return false;
            if (!string.IsNullOrEmpty(intent.Part))
            {
                if (Array.IndexOf(Stage.RequiredParts, intent.Part) < 0 || intent.Result != "Success") return false;
                if (!Stage.Exits.ContainsKey("Success") || !Current.CompletedParts.Add(intent.Part)) return false;
                if (Current.CompletedParts.Count < Stage.RequiredParts.Length) return true;
            }
            else if (intent.Result == "Success" && Stage.RequiredParts.Length > Current.CompletedParts.Count) return false;
            if (intent.Result == null || !Stage.Exits.ContainsKey(intent.Result))
            {
                telemetry.TrackWarn("result_rejected");
                return false;
            }
            Move(intent.Result);
            return true;
        }
        public NarrativeSaveData Capture() => JsonConvert.DeserializeObject<NarrativeSaveData>(JsonConvert.SerializeObject(state));
        public void Restore(NarrativeSaveData saved)
        {
            if (saved == null || saved.NextActivationId < 0 || saved.ConsumedTriggers == null || saved.ConditionEdges == null || saved.EdgeCounters == null || saved.StoryFlags == null)
                throw new ArgumentException("剧情快照非法");
            ValidateFrame(saved.Current, saved.NextActivationId);
            ValidateFrame(saved.Parent, saved.NextActivationId);
            if (saved.Parent != null && (saved.Current == null || !GetStory(saved.Parent.StoryId).Get(saved.Parent.StageId).AllowEncounter))
                throw new ArgumentException("局部遭遇父阶段非法");
            state = JsonConvert.DeserializeObject<NarrativeSaveData>(JsonConvert.SerializeObject(saved));
            Generation++;
            telemetry.Track("restored");
        }
        private void ValidateFrame(NarrativeSaveData.Frame frame, long maximum)
        {
            if (frame == null) return;
            NarrativeContent.Stage stage = GetStory(frame.StoryId).Get(frame.StageId);
            if (frame.ActivationId < 1 || frame.ActivationId > maximum || string.IsNullOrEmpty(frame.TargetId) ||
                string.IsNullOrEmpty(frame.ActionRequestId) || frame.CompletedParts == null) throw new ArgumentException("阶段身份非法");
            foreach (string part in frame.CompletedParts)
                if (Array.IndexOf(stage.RequiredParts, part) < 0) throw new ArgumentException("未知行为完成记录");
        }
        private NarrativeContent GetStory(string id)
        {
            if (id == null || !stories.TryGetValue(id, out NarrativeContent story)) throw new ArgumentException("未知剧情：" + id);
            return story;
        }
        private void Move(string result)
        {
            string next = Stage.Exits[result];
            Enter(Current.StoryId, next, Current.TargetId);
        }
        private void Enter(string story, string stage, string target)
        {
            GetStory(story).Get(stage);
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("阶段目标不可为空");
            long activation = checked(++state.NextActivationId);
            state.Current = new NarrativeSaveData.Frame { StoryId = story, StageId = stage, TargetId = target,
                ActivationId = activation, ActionRequestId = story.Length + ":" + story + ":" + activation };
            telemetry.Track("stage_entered", ("story", story), ("stage", stage));
        }
    }
}
