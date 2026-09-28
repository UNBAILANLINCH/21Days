// 职责：对话结束的广播载荷（编号、出口、是否跳过）。
// 新建原因：DialogueResult 是返回给发起方的值，广播事件与之语义分离，便于各自演进；一个文件一个类型。
namespace Game.Dialogue
{
    public readonly struct DialogueEndedEvent
    {
        public DialogueEndedEvent(int dialogueId, string outcome, bool skipped, bool completed)
        {
            DialogueId = dialogueId;
            Outcome = outcome ?? string.Empty;
            Skipped = skipped;
            Completed = completed;
        }

        public int DialogueId { get; }
        public string Outcome { get; }
        public bool Skipped { get; }
        /// <summary>正常抵达出口（含跳过）为 true；取消或失败为 false。不能用 Outcome 判定。</summary>
        public bool Completed { get; }
    }
}
