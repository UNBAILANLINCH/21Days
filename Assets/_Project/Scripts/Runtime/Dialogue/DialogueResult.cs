// 职责：一段对话结束后返回给调用方的结果（编号、出口、是否跳过）。
// 新建原因：DialogueRules 只暴露运行中状态，没有面向调用方的一次性结果；Narrative 的结果类型不含对话编号与跳过语义。
namespace Game.Dialogue
{
    public readonly struct DialogueResult
    {
        public DialogueResult(int dialogueId, string outcome, bool skipped)
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
