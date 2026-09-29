// 职责：通知 Session 合并保存请求；分区已经写回，订阅者不参与规则推进。
namespace Game.Narrative
{
    public readonly struct NarrativeChangedEvent
    {
        public NarrativeChangedEvent(string stage) { Stage = stage ?? string.Empty; }
        public string Stage { get; }
    }
}
