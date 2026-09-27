// 职责：任务编辑器里单个目标的可编辑模型，与 Tables/Data/quest/*.json 的 objectives 元素一一对应。
// 为什么新建：运行时 QuestObjectiveDefinition 是只读 struct（且把 count<1 静默改成 1），编辑器要可改、要保留原值给校验报警告，不能复用；也不适合扩展进运行时类型（Runtime 不该背编辑态）。

using Game.Quest;

namespace Game.Editor.Quest
{
    /// <summary>一个任务目标的草稿。字段名与 JSON 字段对应：text / kind / key / count / location。</summary>
    public sealed class QuestObjectiveDraft
    {
        /// <summary>给玩家看的目标描述。</summary>
        public string Text { get; set; } = string.Empty;

        public QuestObjectiveKind Kind { get; set; } = QuestObjectiveKind.TalkTo;

        /// <summary>匹配键：TalkTo 是对话编号，ReachLocation 是地点键，Counter 是计数键。</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>所需次数；原样保留（小于 1 由校验器报警告，运行时按 1 处理）。</summary>
        public int Count { get; set; } = 1;

        /// <summary>指引地点键；空串 = 自动。</summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>深拷贝一份。</summary>
        public QuestObjectiveDraft Clone()
        {
            return new QuestObjectiveDraft
            {
                Text = Text,
                Kind = Kind,
                Key = Key,
                Count = Count,
                Location = Location
            };
        }
    }
}
