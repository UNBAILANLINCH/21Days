// 职责：任务表校验的一条结果（等级、任务编号、目标序号、中文消息）。
// 为什么新建：运行时 QuestContent 遇错直接抛异常、没有「结果条目」的数据形态；AssetAuditWindow.Finding 是私有的资产路径壳，字段对不上，故新建。

using System.Globalization;

namespace Game.Editor.Quest
{
    /// <summary>一条校验结果。<see cref="Message"/> 不含「任务 X · 第 N 个目标」前缀，前缀由 <see cref="ToString"/> 按字段拼。</summary>
    public readonly struct QuestIssue
    {
        public QuestIssue(QuestIssueSeverity severity, int questId, int objectiveIndex, string message)
        {
            Severity = severity;
            QuestId = questId;
            ObjectiveIndex = objectiveIndex;
            Message = message ?? string.Empty;
        }

        public QuestIssueSeverity Severity { get; }

        /// <summary>出问题的任务编号；0 = 表级问题。</summary>
        public int QuestId { get; }

        /// <summary>目标下标（从 0 起）；-1 = 任务级问题。显示时 +1。</summary>
        public int ObjectiveIndex { get; }

        public string Message { get; }

        /// <summary>显示用：「[错误] 任务 1003 · 第 1 个目标：…」「[警告] 任务表：…」。</summary>
        public override string ToString()
        {
            string level = Severity == QuestIssueSeverity.Error ? "[错误]" : "[警告]";
            string where = QuestId == 0 ? "任务表" : "任务 " + QuestId.ToString(CultureInfo.InvariantCulture);
            if (ObjectiveIndex >= 0)
            {
                where += $" · 第 {(ObjectiveIndex + 1).ToString(CultureInfo.InvariantCulture)} 个目标";
            }

            return $"{level} {where}：{Message}";
        }
    }
}
