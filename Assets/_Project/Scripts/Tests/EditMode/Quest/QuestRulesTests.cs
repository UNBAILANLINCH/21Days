// 职责：锁定 QuestRules 的核心规则——激活（前置 + 主线唯一）、上报推进、完成连锁、排序、追踪与事件顺序。
// 为什么新建：任务系统首次落地（PRP/quest-system），规则层是纯 C#，必须有 EditMode 测试守着。

using System.Collections.Generic;
using Game.Core.Telemetry;
using Game.Quest;
using NUnit.Framework;
using static Game.Tests.EditMode.Quest.QuestTestContent;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestRulesTests
    {
        private QuestRules rules;

        [SetUp]
        public void SetUp()
        {
            rules = NewRules();
        }

        // ---------- A1 激活 ----------

        [Test]
        public void ActivateAvailable_OnFreshRules_ActivatesRootQuestsOnly()
        {
            rules.ActivateAvailable();

            Assert.That(StateOf(Main1), Is.EqualTo(QuestState.InProgress));
            Assert.That(StateOf(Side1), Is.EqualTo(QuestState.InProgress));
            Assert.That(StateOf(Main2), Is.EqualTo(QuestState.Inactive));
            Assert.That(StateOf(Main3), Is.EqualTo(QuestState.Inactive));
            Assert.That(StateOf(Side2), Is.EqualTo(QuestState.Inactive));
        }

        [Test]
        public void ActivateAvailable_WhenTwoMainsEligible_ActivatesLowestIdOnly()
        {
            rules.ActivateAvailable();
            CompleteMain1(rules);

            Assert.That(StateOf(Main2), Is.EqualTo(QuestState.InProgress));
            Assert.That(StateOf(Main3), Is.EqualTo(QuestState.Inactive), "已有主线进行中，1003 不该同时激活");
            Assert.That(rules.CurrentMainId, Is.EqualTo(Main2));

            rules.Report(QuestObjectiveKind.TalkTo, "1002");

            Assert.That(StateOf(Main2), Is.EqualTo(QuestState.Completed));
            Assert.That(StateOf(Main3), Is.EqualTo(QuestState.InProgress));
            Assert.That(rules.CurrentMainId, Is.EqualTo(Main3));
        }

        // ---------- A2 上报推进 ----------

        [Test]
        public void Report_CurrentObjectiveMatches_AdvancesToNext()
        {
            rules.ActivateAvailable();

            int advanced = rules.Report(QuestObjectiveKind.TalkTo, "1001");

            Assert.That(advanced, Is.EqualTo(1));
            Assert.That(Progress(Main1).ObjectiveIndex, Is.EqualTo(1));
            Assert.That(Progress(Main1).Count, Is.EqualTo(0));
            Assert.That(Progress(Main1).State, Is.EqualTo(QuestState.InProgress));
        }

        [Test]
        public void Report_NonCurrentObjective_IsIgnored()
        {
            rules.ActivateAvailable();

            int advanced = rules.Report(QuestObjectiveKind.ReachLocation, "camp");

            Assert.That(advanced, Is.EqualTo(0));
            Assert.That(Progress(Main1).ObjectiveIndex, Is.EqualTo(0));
            Assert.That(Progress(Main1).Count, Is.EqualTo(0));
        }

        [Test]
        public void Report_CounterBelowRequired_KeepsCounting()
        {
            rules.ActivateAvailable();
            CompleteMain1(rules);
            rules.Report(QuestObjectiveKind.TalkTo, "1002");

            rules.Report(QuestObjectiveKind.Counter, "x", 2);

            Assert.That(Progress(Main3).ObjectiveIndex, Is.EqualTo(0));
            Assert.That(Progress(Main3).Count, Is.EqualTo(2));
            Assert.That(Progress(Main3).State, Is.EqualTo(QuestState.InProgress));

            rules.Report(QuestObjectiveKind.Counter, "x", 1);

            Assert.That(Progress(Main3).State, Is.EqualTo(QuestState.Completed));
        }

        [Test]
        public void Report_LastObjectiveDone_CompletesQuestAndActivatesDependents()
        {
            rules.ActivateAvailable();

            rules.Report(QuestObjectiveKind.ReachLocation, "lookout");

            Assert.That(StateOf(Side1), Is.EqualTo(QuestState.Completed));
            Assert.That(StateOf(Side2), Is.EqualTo(QuestState.InProgress));
        }

        [Test]
        public void Report_ZeroAmount_ReturnsZero()
        {
            rules.ActivateAvailable();

            Assert.That(rules.Report(QuestObjectiveKind.TalkTo, "1001", 0), Is.EqualTo(0));
            Assert.That(Progress(Main1).ObjectiveIndex, Is.EqualTo(0));
        }

        [Test]
        public void Report_ReturnsNumberOfAdvancedObjectives()
        {
            var content = new QuestContent(new[]
            {
                Def(1, QuestKind.Side, new int[0], Objective(QuestObjectiveKind.Counter, "shared", 2)),
                Def(2, QuestKind.Side, new int[0], Objective(QuestObjectiveKind.Counter, "shared", 5))
            });
            var local = new QuestRules(content, NullTelemetryScope.Instance);
            local.ActivateAvailable();

            Assert.That(local.Report(QuestObjectiveKind.Counter, "shared"), Is.EqualTo(2));
        }

        // ---------- 排序 ----------

        [Test]
        public void GetOrdered_WithMainAndSides_PutsMainFirstThenSidesByAcceptOrder()
        {
            rules.ActivateAvailable(); // 1001(序 1)、2001(序 2)
            rules.Report(QuestObjectiveKind.ReachLocation, "lookout"); // 2001 完成 → 2002(序 3)
            CompleteMain1(rules); // 1001 完成 → 1002(序 4)
            var buffer = new List<QuestProgress>();

            rules.GetOrdered(buffer);

            Assert.That(Ids(buffer), Is.EqualTo(new[] { Main2, Side2 }));
        }

        [Test]
        public void GetOrdered_WithoutMain_StartsWithSides()
        {
            var content = new QuestContent(new[]
            {
                Def(5, QuestKind.Side, new int[0], Objective(QuestObjectiveKind.Counter, "a")),
                Def(3, QuestKind.Side, new int[0], Objective(QuestObjectiveKind.Counter, "b"))
            });
            var local = new QuestRules(content, NullTelemetryScope.Instance);
            local.ActivateAvailable();
            var buffer = new List<QuestProgress>();

            local.GetOrdered(buffer);

            Assert.That(local.CurrentMainId, Is.EqualTo(0));
            Assert.That(Ids(buffer), Is.EqualTo(new[] { 3, 5 }), "按 id 升序激活，所以激活序号 3 在前");
        }

        [Test]
        public void GetOrdered_ExcludesCompleted()
        {
            rules.ActivateAvailable();
            rules.Report(QuestObjectiveKind.ReachLocation, "lookout");
            var buffer = new List<QuestProgress>();

            rules.GetOrdered(buffer);

            Assert.That(Ids(buffer), Is.EqualTo(new[] { Main1, Side2 }));
        }

        // ---------- A3 追踪 ----------

        [Test]
        public void ActivateAvailable_FirstTime_TracksCurrentMain()
        {
            var changes = new List<int>();
            rules.OnTrackingChanged += changes.Add;

            rules.ActivateAvailable();

            Assert.That(rules.Initialized, Is.True);
            Assert.That(rules.TrackedId, Is.EqualTo(Main1));
            Assert.That(changes, Is.EqualTo(new[] { Main1 }));
        }

        [Test]
        public void Track_QuestNotInProgress_ReturnsFalse()
        {
            rules.ActivateAvailable();

            Assert.That(rules.Track(Main2), Is.False);
            Assert.That(rules.Track(9999), Is.False);
            Assert.That(rules.TrackedId, Is.EqualTo(Main1));
        }

        [Test]
        public void Track_ThenUntrack_RaisesTrackingChangedOnceEach()
        {
            rules.ActivateAvailable();
            var changes = new List<int>();
            rules.OnTrackingChanged += changes.Add;

            Assert.That(rules.Track(Side1), Is.True);
            rules.Untrack();
            rules.Untrack();

            Assert.That(changes, Is.EqualTo(new[] { Side1, 0 }));
            Assert.That(rules.TrackedId, Is.EqualTo(0));
        }

        [Test]
        public void Report_TrackedSideCompletes_FallsBackToCurrentMain()
        {
            rules.ActivateAvailable();
            rules.Track(Side1);

            rules.Report(QuestObjectiveKind.ReachLocation, "lookout");

            Assert.That(rules.TrackedId, Is.EqualTo(Main1));
        }

        [Test]
        public void Report_TrackedMainCompletes_NoMainLeft_TrackedBecomesZero()
        {
            rules.ActivateAvailable();
            CompleteMain1(rules);
            Assert.That(rules.TrackedId, Is.EqualTo(Main2), "1001 完成后追踪应切到新主线 1002");
            rules.Report(QuestObjectiveKind.TalkTo, "1002");
            Assert.That(rules.TrackedId, Is.EqualTo(Main3));

            rules.Report(QuestObjectiveKind.Counter, "x", 3);

            Assert.That(rules.CurrentMainId, Is.EqualTo(0));
            Assert.That(rules.TrackedId, Is.EqualTo(0));
        }

        [Test]
        public void ActivateAvailable_AfterUntrack_DoesNotRetrack()
        {
            rules.ActivateAvailable();
            rules.Untrack();

            CompleteMain1(rules);

            Assert.That(StateOf(Main2), Is.EqualTo(QuestState.InProgress));
            Assert.That(rules.TrackedId, Is.EqualTo(0));
        }

        [Test]
        public void Track_SameId_DoesNotRaiseEvent()
        {
            rules.ActivateAvailable();
            var changes = new List<int>();
            rules.OnTrackingChanged += changes.Add;

            Assert.That(rules.Track(Main1), Is.True);

            Assert.That(changes, Is.Empty);
        }

        // ---------- 事件顺序 ----------

        [Test]
        public void Report_CompletingQuest_RaisesProgressedCompletedActivatedInOrder()
        {
            rules.ActivateAvailable();
            rules.Report(QuestObjectiveKind.TalkTo, "1001");
            var log = new List<string>();
            rules.OnProgressed += (id, index, count, required, done) => log.Add($"progressed:{id}:{index}:{count}:{required}:{done}");
            rules.OnCompleted += id => log.Add($"completed:{id}");
            rules.OnActivated += id => log.Add($"activated:{id}");
            rules.OnTrackingChanged += id => log.Add($"tracking:{id}");

            rules.Report(QuestObjectiveKind.ReachLocation, "camp");

            Assert.That(log, Is.EqualTo(new[]
            {
                "progressed:1001:1:1:1:True",
                "completed:1001",
                "activated:1002",
                "tracking:1002"
            }));
        }

        private QuestProgress Progress(int id)
        {
            Assert.That(rules.TryGet(id, out QuestProgress progress), Is.True, $"任务 {id} 应存在");
            return progress;
        }

        private QuestState StateOf(int id) => Progress(id).State;

        private static int[] Ids(List<QuestProgress> buffer)
        {
            var ids = new int[buffer.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = buffer[i].Id;
            }

            return ids;
        }
    }
}
