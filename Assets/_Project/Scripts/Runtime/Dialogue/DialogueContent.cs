// 职责：对白内容的强类型边界；Luban 适配与测试共用，不把台词配置写进通用 UI。
using System;
using System.Collections.Generic;
using Game.Narrative;

namespace Game.Dialogue
{
    public sealed class DialogueContent
    {
        // 立绘只有对话框左上 / 右上两个槽位；说话者在哪一侧由内容适配层翻译成 Portraits。
        public const int SlotCount = 2;
        public enum PortraitSlot { Left = 0, Right = 1 }
        public enum NodeKind { Line, Choice, End }
        public enum PortraitAction { Keep, Show, Clear }
        public sealed class Portrait
        {
            public int Slot { get; set; }
            public PortraitAction Action { get; set; }
            public string CharacterId { get; set; } = string.Empty;
            public string ExpressionId { get; set; } = string.Empty;
        }
        public sealed class Choice
        {
            public string Id { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public string Next { get; set; } = string.Empty;
            public string Outcome { get; set; } = string.Empty;
            public bool HideWhenUnavailable { get; set; }
            public string UnavailableReason { get; set; } = string.Empty;
            /// <summary>选项图标的 Addressables 地址；空 = 无图标。</summary>
            public string IconKey { get; set; } = string.Empty;
            public NarrativeCondition[][] Conditions { get; set; } = Array.Empty<NarrativeCondition[]>();
        }
        public sealed class Node
        {
            public string Id { get; set; } = string.Empty;
            public int Revision { get; set; } = 1;
            public NodeKind Kind { get; set; }
            public string SpeakerId { get; set; } = string.Empty;
            public string SpeakerName { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public string Next { get; set; } = string.Empty;
            public string Outcome { get; set; } = string.Empty;
            public bool Blocking { get; set; } = true;
            /// <summary>本节点展示前先播放的演出 id（Addressables 地址）；空 = 不插播。</summary>
            public string PerformanceId { get; set; } = string.Empty;
            public Portrait[] Portraits { get; set; } = Array.Empty<Portrait>();
            public Choice[] Choices { get; set; } = Array.Empty<Choice>();
        }
        private readonly Dictionary<string, Node> nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        public DialogueContent(string id, string entry, IEnumerable<Node> source)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("会话 ID 不可为空");
            Id = id;
            Entry = entry;
            foreach (Node node in source ?? throw new ArgumentNullException(nameof(source)))
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id) || node.Revision < 1 ||
                    !Enum.IsDefined(typeof(NodeKind), node.Kind)) throw new ArgumentException("对白节点非法");
                if (nodes.ContainsKey(node.Id)) throw new ArgumentException("重复对白节点：" + node.Id);
                nodes.Add(node.Id, node);
            }
            Get(entry);
            foreach (Node node in nodes.Values)
            {
                if (node.Kind == NodeKind.Line) Get(node.Next);
                if (node.Portraits == null || node.Choices == null) throw new ArgumentException("节点数组不可为空");
                var slots = new HashSet<int>();
                foreach (Portrait portrait in node.Portraits)
                    if (portrait == null || portrait.Slot < 0 || portrait.Slot >= SlotCount || !slots.Add(portrait.Slot) ||
                        !Enum.IsDefined(typeof(PortraitAction), portrait.Action) ||
                        (portrait.Action == PortraitAction.Show && (string.IsNullOrWhiteSpace(portrait.CharacterId) ||
                            string.IsNullOrWhiteSpace(portrait.ExpressionId))))
                        throw new ArgumentException("立绘槽位或映射非法：" + node.Id);
                if (node.Kind == NodeKind.Choice && node.Choices.Length == 0)
                    throw new ArgumentException("选择节点没有选项：" + node.Id);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (Choice choice in node.Choices)
                {
                    if (choice == null || string.IsNullOrWhiteSpace(choice.Id) || !ids.Add(choice.Id) ||
                        string.IsNullOrWhiteSpace(choice.Next) == string.IsNullOrWhiteSpace(choice.Outcome))
                        throw new ArgumentException("选项必须有且只有一个出口：" + node.Id);
                    if (!string.IsNullOrWhiteSpace(choice.Next)) Get(choice.Next);
                    NarrativeCondition.Matches(choice.Conditions,
                        new EncounterContext("validate", "", true, false, false, true, false, false));
                }
            }
        }
        public string Id { get; }
        public string Entry { get; }
        public IEnumerable<Node> Nodes => nodes.Values;
        public Node Get(string id)
        {
            if (id == null || !nodes.TryGetValue(id, out Node node))
                throw new ArgumentException("对白节点不存在：" + id);
            return node;
        }
    }
}
