// 职责：单个任务目标的只读定义（文本、判定类别、匹配键、所需次数、地点键）。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

using System;

namespace Game.Quest
{
    public readonly struct QuestObjectiveDefinition
    {
        public QuestObjectiveDefinition(
            string text,
            QuestObjectiveKind kind,
            string key,
            int requiredCount,
            string locationKey)
        {
            Text = text;
            Kind = kind;
            Key = key ?? string.Empty;
            RequiredCount = requiredCount < 1 ? 1 : requiredCount;
            LocationKey = locationKey ?? string.Empty;
        }

        /// <summary>给玩家看的目标描述。</summary>
        public string Text { get; }

        public QuestObjectiveKind Kind { get; }

        /// <summary>匹配键；null 按空串。</summary>
        public string Key { get; }

        /// <summary>需要累计的次数，至少为 1。</summary>
        public int RequiredCount { get; }

        /// <summary>目标所在地点键（给导航 / 标记用），null 按空串。</summary>
        public string LocationKey { get; }

        /// <summary>类别相等且键序数相等。</summary>
        public bool Matches(QuestObjectiveKind kind, string key) =>
            Kind == kind && string.Equals(Key, key, StringComparison.Ordinal);
    }
}
