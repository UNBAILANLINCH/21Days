// 职责：单条任务的运行时进度（状态、当前目标索引、计数、激活序号）；只有 QuestRules 能改。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

using System;

namespace Game.Quest
{
    public sealed class QuestProgress
    {
        internal QuestProgress(QuestDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public QuestDefinition Definition { get; }

        public QuestState State { get; internal set; }

        /// <summary>当前目标在 <see cref="QuestDefinition.Objectives"/> 里的下标。</summary>
        public int ObjectiveIndex { get; internal set; }

        /// <summary>当前目标已累计的次数。</summary>
        public int Count { get; internal set; }

        /// <summary>激活序号，越小越早接取；未激活为 0。</summary>
        public int AcceptOrder { get; internal set; }

        public int Id => Definition.Id;

        public bool HasCurrentObjective =>
            State == QuestState.InProgress && ObjectiveIndex >= 0 && ObjectiveIndex < Definition.Objectives.Count;

        /// <summary>只在 <see cref="HasCurrentObjective"/> 为 true 时可取，否则抛 <see cref="InvalidOperationException"/>。</summary>
        public QuestObjectiveDefinition CurrentObjective
        {
            get
            {
                if (!HasCurrentObjective)
                {
                    throw new InvalidOperationException($"任务 {Id} 当前没有进行中的目标（状态 {State}，下标 {ObjectiveIndex}）");
                }

                return Definition.Objectives[ObjectiveIndex];
            }
        }

        internal void Reset()
        {
            State = QuestState.Inactive;
            ObjectiveIndex = 0;
            Count = 0;
            AcceptOrder = 0;
        }
    }
}
