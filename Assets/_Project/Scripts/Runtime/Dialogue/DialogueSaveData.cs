// 职责：对白稳定恢复点；通用 Save 不能持有台词名词，现有模块也不拥有阅读进度。
using System;
using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Dialogue
{
    public sealed class DialogueSaveData : ISaveData
    {
        public enum Phase { Closed, Preparing, Typing, AwaitAdvance, AwaitChoice, Completed }
        public sealed class HistoryEntry
        {
            public long Visit { get; set; }
            public string NodeId { get; set; } = string.Empty;
            public string Speaker { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public bool IsChoice { get; set; }
        }
        public int Version => 1;
        public string ConversationId { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public int NodeRevision { get; set; }
        public long Visit { get; set; }
        public Phase CurrentPhase { get; set; }
        public string ResolvedText { get; set; } = string.Empty;
        public string ResolvedSpeaker { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public bool Recorded { get; set; }
        public bool HistoryTruncated { get; set; }
        public List<HistoryEntry> History { get; set; } = new List<HistoryEntry>();
        public DialogueContent.Portrait[] Portraits { get; set; } = Array.Empty<DialogueContent.Portrait>();
        public void Migrate(int fromVersion) { }
    }
}
