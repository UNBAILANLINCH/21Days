// 职责：玩家在对话中选定选项的广播载荷（对话编号、节点、选项）。
// 新建原因：DialogueRules.OnChoiceSelected 只携带内容选项，不含对话编号；跨模块广播需要独立载荷，一个文件一个类型。
namespace Game.Dialogue
{
    public readonly struct DialogueChoiceSelectedEvent
    {
        public DialogueChoiceSelectedEvent(int dialogueId, string nodeId, string choiceId)
        {
            DialogueId = dialogueId;
            NodeId = nodeId ?? string.Empty;
            ChoiceId = choiceId ?? string.Empty;
        }

        public int DialogueId { get; }
        public string NodeId { get; }
        public string ChoiceId { get; }
    }
}
