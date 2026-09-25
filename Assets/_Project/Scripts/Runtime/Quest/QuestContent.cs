// 职责：一整套任务定义的只读集合；构造时一次性校验表数据（id、目标、前置引用与环）。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Quest
{
    public sealed class QuestContent
    {
        private readonly QuestDefinition[] all;
        private readonly Dictionary<int, QuestDefinition> byId;

        /// <summary>任一校验失败抛 <see cref="ArgumentException"/>，消息带任务 id。</summary>
        public QuestContent(IReadOnlyList<QuestDefinition> quests)
        {
            if (quests == null) throw new ArgumentNullException(nameof(quests));

            all = new QuestDefinition[quests.Count];
            byId = new Dictionary<int, QuestDefinition>(quests.Count);
            for (int i = 0; i < quests.Count; i++)
            {
                QuestDefinition quest = quests[i];
                if (quest == null) throw new ArgumentException($"任务表第 {i} 条为空");
                if (quest.Id <= 0) throw new ArgumentException($"任务 {quest.Id}：id 必须为正数");
                if (byId.ContainsKey(quest.Id)) throw new ArgumentException($"任务 {quest.Id}：id 重复");
                if (!Enum.IsDefined(typeof(QuestKind), quest.Kind))
                {
                    throw new ArgumentException($"任务 {quest.Id}：任务类别 {(int)quest.Kind} 不在枚举内");
                }

                ValidateObjectives(quest);
                byId.Add(quest.Id, quest);
                all[i] = quest;
            }

            for (int i = 0; i < all.Length; i++)
            {
                IReadOnlyList<int> prerequisites = all[i].Prerequisites;
                for (int p = 0; p < prerequisites.Count; p++)
                {
                    if (!byId.ContainsKey(prerequisites[p]))
                    {
                        throw new ArgumentException($"任务 {all[i].Id}：前置任务 {prerequisites[p]} 不存在");
                    }
                }
            }

            ValidateNoCycle();
        }

        /// <summary>全部定义，顺序同构造时传入。</summary>
        public IReadOnlyList<QuestDefinition> All => all;

        public bool TryGet(int id, out QuestDefinition definition) => byId.TryGetValue(id, out definition);

        private static void ValidateObjectives(QuestDefinition quest)
        {
            IReadOnlyList<QuestObjectiveDefinition> objectives = quest.Objectives;
            if (objectives.Count == 0) throw new ArgumentException($"任务 {quest.Id}：目标列表为空");

            for (int i = 0; i < objectives.Count; i++)
            {
                QuestObjectiveDefinition objective = objectives[i];
                if (string.IsNullOrWhiteSpace(objective.Text))
                {
                    throw new ArgumentException($"任务 {quest.Id}：第 {i} 个目标的文本为空");
                }

                if (!Enum.IsDefined(typeof(QuestObjectiveKind), objective.Kind))
                {
                    throw new ArgumentException($"任务 {quest.Id}：第 {i} 个目标的类别 {(int)objective.Kind} 不在枚举内");
                }

                if (objective.Kind == QuestObjectiveKind.TalkTo &&
                    !int.TryParse(objective.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    throw new ArgumentException($"任务 {quest.Id}：第 {i} 个目标是对话，键「{objective.Key}」不是角色 id 整数");
                }

                if (objective.Kind == QuestObjectiveKind.ReachLocation && string.IsNullOrWhiteSpace(objective.Key))
                {
                    throw new ArgumentException($"任务 {quest.Id}：第 {i} 个目标是抵达地点，地点键为空");
                }
            }
        }

        // 三色 DFS：0 未访问、1 在栈上、2 已完成。遇到栈上节点即成环（含自环）。
        private void ValidateNoCycle()
        {
            var color = new Dictionary<int, int>(all.Length);
            var stack = new Stack<(int Id, int Next)>();
            for (int i = 0; i < all.Length; i++)
            {
                int root = all[i].Id;
                if (color.TryGetValue(root, out int rootColor) && rootColor != 0) continue;

                color[root] = 1;
                stack.Push((root, 0));
                while (stack.Count > 0)
                {
                    (int id, int next) = stack.Pop();
                    IReadOnlyList<int> prerequisites = byId[id].Prerequisites;
                    if (next >= prerequisites.Count)
                    {
                        color[id] = 2;
                        continue;
                    }

                    stack.Push((id, next + 1));
                    int child = prerequisites[next];
                    color.TryGetValue(child, out int childColor);
                    if (childColor == 1)
                    {
                        throw new ArgumentException($"任务 {id}：前置关系成环（经由任务 {child}）");
                    }

                    if (childColor == 0)
                    {
                        color[child] = 1;
                        stack.Push((child, 0));
                    }
                }
            }
        }
    }
}
