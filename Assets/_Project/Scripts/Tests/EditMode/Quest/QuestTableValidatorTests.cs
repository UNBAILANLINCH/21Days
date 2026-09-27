// 职责：锁定任务表编辑期校验——PRP 3.2 每条规则的触发与不触发、聚合、排序、运行时兜底，以及 CollectContext 能从真实工程收集到已知键。
// 为什么新建：QuestTableValidator 是新增的编辑器校验器（PRP/quest-editor）；QuestContentTests 测的是运行时遇错即抛，语义不同，不宜混放。

using System.Collections.Generic;
using System.Linq;
using Game.Editor.Quest;
using Game.Quest;
using NUnit.Framework;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestTableValidatorTests
    {
        private QuestValidationContext context;

        [SetUp]
        public void SetUp()
        {
            context = new QuestValidationContext(
                new[] { 1001, 1002 },
                new Dictionary<string, string> { { "camp", "SampleScene" }, { "lookout", "SampleScene" } },
                new[] { "crate" });
        }

        // ---------- 基线 ----------

        [Test]
        public void Validate_ValidTable_NoIssues()
        {
            List<QuestIssue> issues = QuestTableValidator.Validate(ValidTable(), context);

            Assert.That(issues.Select(issue => issue.ToString()), Is.Empty);
        }

        [Test]
        public void Validate_ValidTable_NoRuntimeFallbackIssue()
        {
            List<QuestIssue> issues = QuestTableValidator.Validate(ValidTable(), context);

            Assert.That(issues.Any(issue => issue.Message.StartsWith("运行时校验：")), Is.False);
        }

        // ---------- 编号与文件名 ----------

        [Test]
        public void Validate_NonPositiveId_Error()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2002).Id = 0;

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 0, -1, "编号必须是正整数");
        }

        [Test]
        public void Validate_DuplicateId_ErrorOnBoth()
        {
            List<QuestDraft> table = ValidTable();
            QuestDraft copy = Side(2001, "lookout");
            table.Add(copy);

            List<QuestIssue> issues = QuestTableValidator.Validate(table, context);

            Assert.That(issues.Count(issue => issue.Message.StartsWith("编号 2001 重复")), Is.EqualTo(2));
        }

        [Test]
        public void Validate_FileNameMismatch_Error()
        {
            List<QuestDraft> table = ValidTable();
            var moved = new QuestDraft("1003.json") { Id = 2003, Kind = QuestKind.Side, Title = "改过编号" };
            moved.Objectives.Add(Objective(QuestObjectiveKind.Counter, "crate"));
            table.Add(moved);

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 2003, -1,
                "文件名 1003.json 与编号 2003 不一致");
        }

        [Test]
        public void Validate_FileNameMatches_NoError()
        {
            List<QuestDraft> table = ValidTable();
            var loaded = new QuestDraft("2003.json") { Id = 2003, Kind = QuestKind.Side, Title = "从文件读" };
            loaded.Objectives.Add(Objective(QuestObjectiveKind.Counter, "crate"));
            table.Add(loaded);

            Assert.That(QuestTableValidator.Validate(table, context), Is.Empty);
        }

        [Test]
        public void Validate_MainIdOutsideRange_Warning()
        {
            List<QuestDraft> table = ValidTable();
            QuestDraft main = Main(3001, 1002);
            table.Add(main);

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Warning, 3001, -1, "主线惯例用 1xxx");
        }

        [Test]
        public void Validate_SideIdOutsideRange_Warning()
        {
            List<QuestDraft> table = ValidTable();
            table.Add(Side(1500, "camp"));

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Warning, 1500, -1, "支线惯例用 2xxx");
        }

        // ---------- 标题 ----------

        [Test]
        public void Validate_EmptyTitle_Error()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2001).Title = "  ";

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 2001, -1, "标题为空");
        }

        // ---------- 前置 ----------

        [Test]
        public void Validate_MissingPrerequisite_Error()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2001).Prerequisites.Add(1009);

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 2001, -1, "前置任务 1009 不存在");
        }

        [Test]
        public void Validate_SelfPrerequisite_Error()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 1002).Prerequisites = new List<int> { 1002 };

            List<QuestIssue> issues = QuestTableValidator.Validate(table, context);

            AssertHas(issues, QuestIssueSeverity.Error, 1002, -1, "前置任务不能是自己（1002）");
            Assert.That(issues.Any(issue => issue.Message.StartsWith("前置关系成环")), Is.False, "自环只报一次");
        }

        [Test]
        public void Validate_Cycle_ErrorListsWholeCycle()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 1001).Prerequisites.Add(1002);

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 1001, -1,
                "前置关系成环：1001 → 1002 → 1001");
        }

        [Test]
        public void Validate_LongCycle_ReportedOnce()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2001).Prerequisites.Add(2002);
            Find(table, 2002).Prerequisites.Add(1002);
            Find(table, 1001).Prerequisites.Add(2001);

            List<QuestIssue> issues = QuestTableValidator.Validate(table, context);

            Assert.That(issues.Where(issue => issue.Message.StartsWith("前置关系成环")).Select(issue => issue.Message),
                Is.EqualTo(new[] { "前置关系成环：1001 → 2001 → 2002 → 1002 → 1001" }));
        }

        [Test]
        public void Validate_SecondMainHead_Warning()
        {
            List<QuestDraft> table = ValidTable();
            table.Add(Main(1003));

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Warning, 1003, -1,
                "主线 1003 没有接在任何主线后面，会与 1001 同时可接");
        }

        [Test]
        public void Validate_MainChainedAfterMain_NoHeadWarning()
        {
            List<QuestDraft> table = ValidTable();
            table.Add(Main(1003, 1002, 2001));

            Assert.That(QuestTableValidator.Validate(table, context), Is.Empty);
        }

        // ---------- 目标 ----------

        [Test]
        public void Validate_NoObjectives_Error()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2001).Objectives.Clear();

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 2001, -1, "至少要有一个目标");
        }

        [Test]
        public void Validate_EmptyObjectiveText_ErrorNumberedFromOne()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 1001).Objectives[1].Text = string.Empty;

            List<QuestIssue> issues = QuestTableValidator.Validate(table, context);

            AssertHas(issues, QuestIssueSeverity.Error, 1001, 1, "文本为空");
            Assert.That(issues.Single().ToString(), Is.EqualTo("[错误] 任务 1001 · 第 2 个目标：文本为空"));
        }

        [Test]
        public void Validate_CountBelowOne_Warning()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2002).Objectives[0].Count = 0;

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Warning, 2002, 0, "次数 0 会按 1 处理");
        }

        [Test]
        public void Validate_TalkToKeyNotInteger_Error()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 1002).Objectives[0].Key = "长者";

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 1002, 0, "对话编号「长者」不是整数");
        }

        [Test]
        public void Validate_TalkToDialogueMissing_Error()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 1002).Objectives[0].Key = "1009";

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 1002, 0,
                "对话 1009 不在 Tables/Data/dialogue/ 里");
        }

        [Test]
        public void Validate_ReachLocationKeyEmpty_Error()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2001).Objectives[0].Key = string.Empty;

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 2001, 0, "地点键为空");
        }

        [Test]
        public void Validate_ReachLocationKeyUnknown_WarningListsKnown()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2001).Objectives[0].Key = "well";

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Warning, 2001, 0,
                "地点键「well」在任何场景里都没找到（现有：camp@SampleScene、lookout@SampleScene）");
        }

        [Test]
        public void Validate_GuidanceLocationUnknown_Warning()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2002).Objectives[0].Location = "well";

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Warning, 2002, 0, "指引地点「well」");
        }

        [Test]
        public void Validate_GuidanceLocationKnown_NoIssue()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2002).Objectives[0].Location = "camp";

            Assert.That(QuestTableValidator.Validate(table, context), Is.Empty);
        }

        [Test]
        public void Validate_CounterKeyEmpty_Error()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2002).Objectives[0].Key = " ";

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Error, 2002, 0, "计数键为空");
        }

        [Test]
        public void Validate_CounterKeyUnknown_WarningListsKnown()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2002).Objectives[0].Key = "crate2";

            AssertHas(QuestTableValidator.Validate(table, context), QuestIssueSeverity.Warning, 2002, 0,
                "计数键「crate2」没有任何玩法会上报（已知：crate）");
        }

        [Test]
        public void Validate_NullContext_SkipsCrossReferences()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 1002).Objectives[0].Key = "1009";
            Find(table, 2001).Objectives[0].Key = "well";
            Find(table, 2002).Objectives[0].Key = "crate2";

            Assert.That(QuestTableValidator.Validate(table, null), Is.Empty);
        }

        // ---------- 聚合与排序 ----------

        [Test]
        public void Validate_ThreeErrors_AllReported()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 1002).Prerequisites = new List<int> { 1002 };
            Find(table, 1002).Objectives[0].Key = "abc";
            Find(table, 2001).Title = string.Empty;

            List<QuestIssue> issues = QuestTableValidator.Validate(table, context);

            Assert.That(issues.Count(issue => issue.Severity == QuestIssueSeverity.Error), Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void Validate_MixedIssues_ErrorsBeforeWarningsThenById()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 1001).Objectives[1].Key = "well";          // 1001 警告
            Find(table, 2002).Title = string.Empty;                 // 2002 错误
            Find(table, 2001).Title = string.Empty;                 // 2001 错误
            Find(table, 2002).Objectives[0].Count = 0;              // 2002 警告

            List<QuestIssue> issues = QuestTableValidator.Validate(table, context);

            Assert.That(issues.Select(issue => (issue.Severity, issue.QuestId)), Is.EqualTo(new[]
            {
                (QuestIssueSeverity.Error, 2001),
                (QuestIssueSeverity.Error, 2002),
                (QuestIssueSeverity.Warning, 1001),
                (QuestIssueSeverity.Warning, 2002)
            }));
        }

        // ---------- 运行时兜底 ----------

        // 编辑期规则不检查「类别是否在枚举内」（草稿用枚举类型、Deserialize 已按名字严格解析，正常输入到不了这里），
        // 手工塞一个越界枚举值正好是「编辑期漏掉、QuestContent 会抛」的情况，用来锁住兜底路径。
        [Test]
        public void Validate_RuntimeRejects_ReportedAsRuntimeError()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2001).Kind = (QuestKind)5;

            List<QuestIssue> issues = QuestTableValidator.Validate(table, context);

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo(QuestIssueSeverity.Error));
            Assert.That(issues[0].Message, Does.StartWith("运行时校验：").And.Contains("2001"));
        }

        [Test]
        public void Validate_RuntimeRejectsObjectiveKind_ReportedAsRuntimeError()
        {
            List<QuestDraft> table = ValidTable();
            Find(table, 2002).Objectives[0].Kind = (QuestObjectiveKind)9;

            List<QuestIssue> issues = QuestTableValidator.Validate(table, context);

            Assert.That(issues.Select(issue => issue.Message), Has.Some.StartsWith("运行时校验："));
        }

        // ---------- 真实工程 ----------

        // 读真实工程：Tables/Data/dialogue/、Assets 下场景里的 QuestLocation、LootConfig 资产。
        // 对话 1001/1002、地点 camp/lookout 改名或删除时，这条要同步改。
        // 计数键（crate）按 "t:LootConfig" 找资产，Loot 模块可能不在当前检出里（另一会话尚未提交时）——
        // 这种情况下断言退化成「集合为空且不抛」，避免测试跟着别的会话的工作区状态忽绿忽红。
        [Test]
        public void CollectContext_RealProject_ContainsKnownKeys()
        {
            QuestValidationContext real = QuestTableValidator.CollectContext();

            Assert.That(real.DialogueIds, Has.Member(1001));
            Assert.That(real.DialogueIds, Has.Member(1002));
            Assert.That(real.LocationKeys.Keys, Has.Member("camp"));
            Assert.That(real.LocationKeys.Keys, Has.Member("lookout"));

            if (UnityEditor.AssetDatabase.FindAssets("t:LootConfig").Length > 0)
            {
                Assert.That(real.CounterKeys, Has.Member("crate"));
            }
            else
            {
                Assert.That(real.CounterKeys, Is.Empty);
            }
        }

        // ---------- 构造工具 ----------

        // 与现有 Tables/Data/quest/ 同形：两条主线串成链，两条开局支线。
        private static List<QuestDraft> ValidTable()
        {
            QuestDraft first = Main(1001);
            first.Objectives.Clear();
            first.Objectives.Add(Objective(QuestObjectiveKind.TalkTo, "1001"));
            first.Objectives.Add(Objective(QuestObjectiveKind.ReachLocation, "camp"));

            QuestDraft counter = new QuestDraft { Id = 2002, Kind = QuestKind.Side, Title = "清点物资" };
            QuestObjectiveDraft crate = Objective(QuestObjectiveKind.Counter, "crate");
            crate.Count = 3;
            counter.Objectives.Add(crate);

            return new List<QuestDraft> { first, Main(1002, 1001), Side(2001, "lookout"), counter };
        }

        private static QuestDraft Main(int id, params int[] prerequisites)
        {
            var draft = new QuestDraft
            {
                Id = id,
                Kind = QuestKind.Main,
                Title = "主线 " + id,
                Prerequisites = new List<int>(prerequisites)
            };
            draft.Objectives.Add(Objective(QuestObjectiveKind.TalkTo, "1002"));
            return draft;
        }

        private static QuestDraft Side(int id, string locationKey)
        {
            var draft = new QuestDraft { Id = id, Kind = QuestKind.Side, Title = "支线 " + id };
            draft.Objectives.Add(Objective(QuestObjectiveKind.ReachLocation, locationKey));
            return draft;
        }

        private static QuestObjectiveDraft Objective(QuestObjectiveKind kind, string key) =>
            new QuestObjectiveDraft { Text = "目标", Kind = kind, Key = key, Count = 1 };

        private static QuestDraft Find(List<QuestDraft> table, int id) => table.First(draft => draft.Id == id);

        private static void AssertHas(
            List<QuestIssue> issues, QuestIssueSeverity severity, int questId, int objectiveIndex, string messagePart)
        {
            bool found = issues.Any(issue =>
                issue.Severity == severity &&
                issue.QuestId == questId &&
                issue.ObjectiveIndex == objectiveIndex &&
                issue.Message.Contains(messagePart));
            Assert.That(found, Is.True,
                $"没找到 {severity} / 任务 {questId} / 目标 {objectiveIndex} /「{messagePart}」。实际：\n" +
                string.Join("\n", issues.Select(issue => issue.ToString())));
        }
    }
}
