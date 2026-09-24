// 职责：对话结束的广播载荷（编号、出口、是否跳过）。
// 新建原因：DialogueResult 是返回给发起方的值，广播事件与之语义分离，便于各自演进；一个文件一个类型。
namespace Game.Dialogue
{
    public readonly struct DialogueEndedEvent
    {
        public DialogueEndedEvent(int dialogueId, string outcome, bool skipped)
        {
            DialogueId = dialogueId;
            Outcome = outcome ?? string.Empty;
            Skipped = skipped;
        }

        public int DialogueId { get; }
        public string Outcome { get; }
        public bool Skipped { get; }
    }
}
