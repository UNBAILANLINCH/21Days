// 职责：对话开始的广播载荷，供其他模块订阅。
// 新建原因：现有对话类型都是内部状态，没有跨模块广播的事件载荷；一个文件一个类型。
namespace Game.Dialogue
{
    public readonly struct DialogueStartedEvent
    {
        public DialogueStartedEvent(int dialogueId) => DialogueId = dialogueId;

        public int DialogueId { get; }
    }
}
