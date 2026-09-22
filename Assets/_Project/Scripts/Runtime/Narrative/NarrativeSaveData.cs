// 职责：显式保存主阶段、局部遭遇与请求身份；异步调用栈无法用于存档恢复。
using System;
using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Narrative
{
    public sealed class NarrativeSaveData : ISaveData
    {
        public sealed class Frame
        {
            public string StoryId { get; set; } = string.Empty;
            public string StageId { get; set; } = string.Empty;
            public string TargetId { get; set; } = string.Empty;
            public long ActivationId { get; set; }
            public string ActionRequestId { get; set; } = string.Empty;
            public bool RequestIssued { get; set; }
            public HashSet<string> CompletedParts { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        }
        public int Version => 1;
        public long NextActivationId { get; set; }
        public Frame Current { get; set; }
        public Frame Parent { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public HashSet<string> ConsumedTriggers { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, bool> ConditionEdges { get; set; } = new Dictionary<string, bool>(StringComparer.Ordinal);
        public Dictionary<string, long> EdgeCounters { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
        public HashSet<string> StoryFlags { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        public void Migrate(int fromVersion) { }
    }
}
