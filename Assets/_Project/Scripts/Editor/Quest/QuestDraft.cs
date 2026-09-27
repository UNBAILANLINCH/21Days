// 职责：任务编辑器里单条任务的可编辑模型，与 Tables/Data/quest/<id>.json 一一对应。
// 为什么新建：运行时 QuestDefinition 构造后只读、且不记来源文件，编辑器要逐字段改并在保存时按文件名搬迁，不能复用；把编辑态塞进 Runtime 类型会让运行时背上编辑器职责。

using System.Collections.Generic;
using Game.Quest;

namespace Game.Editor.Quest
{
    /// <summary>一条任务的草稿。字段名与 JSON 字段对应：id / kind / title / description / prerequisites / objectives。</summary>
    public sealed class QuestDraft
    {
        public QuestDraft()
        {
        }

        /// <param name="sourcePath">载入时的文件路径（或文件名）；新建任务传 null。</param>
        public QuestDraft(string sourcePath)
        {
            SourcePath = sourcePath;
        }

        public int Id { get; set; }

        public QuestKind Kind { get; set; } = QuestKind.Main;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>前置任务编号，顺序即 JSON 里的顺序。</summary>
        public List<int> Prerequisites { get; set; } = new List<int>();

        /// <summary>按顺序推进的目标。</summary>
        public List<QuestObjectiveDraft> Objectives { get; set; } = new List<QuestObjectiveDraft>();

        /// <summary>载入时的文件路径；新建为 null。只有 <see cref="QuestTableFile"/> 在保存 / 删除后改写它。</summary>
        public string SourcePath { get; internal set; }

        /// <summary>深拷贝一份（含来源路径）。</summary>
        public QuestDraft Clone()
        {
            var copy = new QuestDraft(SourcePath)
            {
                Id = Id,
                Kind = Kind,
                Title = Title,
                Description = Description,
                Prerequisites = new List<int>(Prerequisites ?? new List<int>())
            };

            if (Objectives != null)
            {
                for (int i = 0; i < Objectives.Count; i++)
                {
                    copy.Objectives.Add(Objectives[i] == null ? null : Objectives[i].Clone());
                }
            }

            return copy;
        }
    }
}
