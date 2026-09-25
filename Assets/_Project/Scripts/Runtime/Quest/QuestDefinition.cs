// 职责：单条任务的只读定义（id、类别、标题、描述、前置、有序目标列表）。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

using System;
using System.Collections.Generic;

namespace Game.Quest
{
    public sealed class QuestDefinition
    {
        public QuestDefinition(
            int id,
            QuestKind kind,
            string title,
            string description,
            IReadOnlyList<int> prerequisites,
            IReadOnlyList<QuestObjectiveDefinition> objectives)
        {
            Id = id;
            Kind = kind;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Prerequisites = prerequisites == null ? Array.Empty<int>() : Copy(prerequisites);
            Objectives = objectives == null ? Array.Empty<QuestObjectiveDefinition>() : Copy(objectives);
        }

        public int Id { get; }

        public QuestKind Kind { get; }

        public string Title { get; }

        public string Description { get; }

        /// <summary>前置任务 id；全部 Completed 后本任务才可激活。</summary>
        public IReadOnlyList<int> Prerequisites { get; }

        /// <summary>按顺序推进的目标。</summary>
        public IReadOnlyList<QuestObjectiveDefinition> Objectives { get; }

        // 拷一份，防止调用方事后改源列表绕过 QuestContent 的构造期校验。
        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            var result = new T[source.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }
    }
}
