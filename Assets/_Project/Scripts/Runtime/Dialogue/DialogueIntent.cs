// 职责：UI 提交只读意图及访问身份；现有玩法意图不表达对白访问与旧回调隔离。
namespace Game.Dialogue
{
    public readonly struct DialogueIntent
    {
        public enum Action { Advance, Choose }
        public DialogueIntent(Action kind, long generation, long visit, string choiceId = null)
        {
            Kind = kind;
            Generation = generation;
            Visit = visit;
            ChoiceId = choiceId;
        }
        public Action Kind { get; }
        public long Generation { get; }
        public long Visit { get; }
        public string ChoiceId { get; }
    }
}
