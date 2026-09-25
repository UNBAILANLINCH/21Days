// 职责：锁定 QuestContent 的构造期校验——合法内容可查，各类非法表数据一律抛 ArgumentException。
// 为什么新建：任务系统首次落地（PRP/quest-system），表数据错误必须在加载时就炸，不能拖到运行中卡关。

using System;
using Game.Quest;
using NUnit.Framework;
using static Game.Tests.EditMode.Quest.QuestTestContent;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestContentTests
    {
        private static readonly int[] NoPrerequisites = new int[0];

        [Test]
        public void Construct_ValidContent_CanTryGetEveryQuest()
        {
            QuestContent content = Build();

            Assert.That(content.All.Count, Is.EqualTo(5));
            Assert.That(content.TryGet(Main3, out QuestDefinition definition), Is.True);
            Assert.That(definition.Prerequisites, Is.EqualTo(new[] { Main1 }));
            Assert.That(content.TryGet(9999, out _), Is.False);
        }

        [Test]
        public void Construct_DuplicateId_Throws()
        {
            Assert.That(() => new QuestContent(new[]
            {
                Def(1, QuestKind.Side, NoPrerequisites, Objective(QuestObjectiveKind.Counter, "a")),
                Def(1, QuestKind.Side, NoPrerequisites, Objective(QuestObjectiveKind.Counter, "b"))
            }), Throws.ArgumentException.With.Message.Contains("1"));
        }

        [Test]
        public void Construct_NonPositiveId_Throws()
        {
            Assert.That(() => new QuestContent(new[]
            {
                Def(0, QuestKind.Side, NoPrerequisites, Objective(QuestObjectiveKind.Counter, "a"))
            }), Throws.ArgumentException);
        }

        [Test]
        public void Construct_EmptyObjectives_Throws()
        {
            Assert.That(() => new QuestContent(new[]
            {
                Def(7, QuestKind.Side, NoPrerequisites)
            }), Throws.ArgumentException.With.Message.Contains("7"));
        }

        [Test]
        public void Construct_BlankObjectiveText_Throws()
        {
            var blank = new QuestObjectiveDefinition("  ", QuestObjectiveKind.Counter, "a", 1, null);

            Assert.That(() => new QuestContent(new[]
            {
                Def(8, QuestKind.Side, NoPrerequisites, blank)
            }), Throws.ArgumentException.With.Message.Contains("8"));
        }

        [Test]
        public void Construct_TalkToKeyNotInteger_Throws()
        {
            Assert.That(() => new QuestContent(new[]
            {
                Def(9, QuestKind.Side, NoPrerequisites, Objective(QuestObjectiveKind.TalkTo, "npc_a"))
            }), Throws.ArgumentException.With.Message.Contains("9"));
        }

        [Test]
        public void Construct_ReachLocationKeyEmpty_Throws()
        {
            Assert.That(() => new QuestContent(new[]
            {
                Def(10, QuestKind.Side, NoPrerequisites, Objective(QuestObjectiveKind.ReachLocation, ""))
            }), Throws.ArgumentException.With.Message.Contains("10"));
        }

        [Test]
        public void Construct_MissingPrerequisite_Throws()
        {
            Assert.That(() => new QuestContent(new[]
            {
                Def(11, QuestKind.Side, new[] { 404 }, Objective(QuestObjectiveKind.Counter, "a"))
            }), Throws.ArgumentException.With.Message.Contains("404"));
        }

        [Test]
        public void Construct_SelfPrerequisite_Throws()
        {
            Assert.That(() => new QuestContent(new[]
            {
                Def(12, QuestKind.Side, new[] { 12 }, Objective(QuestObjectiveKind.Counter, "a"))
            }), Throws.ArgumentException.With.Message.Contains("12"));
        }

        [Test]
        public void Construct_MutualPrerequisites_Throws()
        {
            Assert.That(() => new QuestContent(new[]
            {
                Def(13, QuestKind.Side, new[] { 14 }, Objective(QuestObjectiveKind.Counter, "a")),
                Def(14, QuestKind.Side, new[] { 13 }, Objective(QuestObjectiveKind.Counter, "b"))
            }), Throws.ArgumentException);
        }

        [Test]
        public void ObjectiveDefinition_RequiredCountZero_TreatedAsOne()
        {
            var objective = new QuestObjectiveDefinition("目标", QuestObjectiveKind.Counter, "a", 0, null);

            Assert.That(objective.RequiredCount, Is.EqualTo(1));
            Assert.That(objective.LocationKey, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Construct_NullList_ThrowsArgumentNull()
        {
            Assert.That(() => new QuestContent(null), Throws.TypeOf<ArgumentNullException>());
        }
    }
}
