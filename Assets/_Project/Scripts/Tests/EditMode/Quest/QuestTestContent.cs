// 职责：Quest 测试共用的任务内容与构造辅助（3 条主线 + 2 条支线，覆盖前置、主线唯一、计数目标）。
// 为什么新建：任务系统首次落地（PRP/quest-system），多个测试类要共用同一份内容，放在各自类里会三处重复。

using Game.Core.Telemetry;
using Game.Quest;

namespace Game.Tests.EditMode.Quest
{
    internal static class QuestTestContent
    {
        public const int Main1 = 1001;
        public const int Main2 = 1002;
        public const int Main3 = 1003;
        public const int Side1 = 2001;
        public const int Side2 = 2002;

        /// <summary>
        /// 1001 主线：TalkTo "1001" → ReachLocation "camp"；1002 主线（前置 1001）：TalkTo "1002"；
        /// 1003 主线（前置 1001）：Counter "x" ×3；2001 支线：ReachLocation "lookout"；2002 支线（前置 2001）：Counter "y"。
        /// </summary>
        public static QuestContent Build() => new QuestContent(new[]
        {
            Def(Main1, QuestKind.Main, new int[0],
                Objective(QuestObjectiveKind.TalkTo, "1001"),
                Objective(QuestObjectiveKind.ReachLocation, "camp")),
            Def(Main2, QuestKind.Main, new[] { Main1 },
                Objective(QuestObjectiveKind.TalkTo, "1002")),
            Def(Main3, QuestKind.Main, new[] { Main1 },
                Objective(QuestObjectiveKind.Counter, "x", 3)),
            Def(Side1, QuestKind.Side, new int[0],
                Objective(QuestObjectiveKind.ReachLocation, "lookout")),
            Def(Side2, QuestKind.Side, new[] { Side1 },
                Objective(QuestObjectiveKind.Counter, "y"))
        });

        public static QuestRules NewRules() => new QuestRules(Build(), NullTelemetryScope.Instance);

        public static QuestDefinition Def(int id, QuestKind kind, int[] prerequisites, params QuestObjectiveDefinition[] objectives) =>
            new QuestDefinition(id, kind, "任务" + id, "描述" + id, prerequisites, objectives);

        public static QuestObjectiveDefinition Objective(QuestObjectiveKind kind, string key, int requiredCount = 1) =>
            new QuestObjectiveDefinition("目标 " + key, kind, key, requiredCount, null);

        /// <summary>把 1001 做完（两步），会连带激活 1002。</summary>
        public static void CompleteMain1(QuestRules rules)
        {
            rules.Report(QuestObjectiveKind.TalkTo, "1001");
            rules.Report(QuestObjectiveKind.ReachLocation, "camp");
        }
    }
}
