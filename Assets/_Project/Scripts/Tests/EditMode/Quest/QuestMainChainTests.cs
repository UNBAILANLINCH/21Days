// 职责：锁定主线编排纯函数——链推导（线性 / 分叉 / 空）、▲▼ 调序重写前置、支线解锁分组名。
// 为什么新建：QuestMainChain 是从任务编辑器窗口拆出的新静态类（PRP/quest-editor），现有测试文件都不针对它。

using System.Collections.Generic;
using System.Linq;
using Game.Editor.Quest;
using Game.Quest;
using NUnit.Framework;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestMainChainTests
    {
        [Test]
        public void TryBuildChain_LinearMains_ReturnsInOrder()
        {
            var drafts = new List<QuestDraft> { Draft(1002, QuestKind.Main, 1001), Draft(2001, QuestKind.Side), Draft(1001, QuestKind.Main) };
            var chain = new List<QuestDraft>();

            bool linear = QuestMainChain.TryBuildChain(drafts, chain, out string reason);

            Assert.That(linear, Is.True, reason);
            Assert.That(Ids(chain), Is.EqualTo(new[] { 1001, 1002 }));
        }

        [Test]
        public void TryBuildChain_SidePrerequisiteIgnored_StillLinear()
        {
            var drafts = new List<QuestDraft>
            {
                Draft(1001, QuestKind.Main),
                Draft(2001, QuestKind.Side),
                Draft(1002, QuestKind.Main, 2001, 1001)
            };
            var chain = new List<QuestDraft>();

            Assert.That(QuestMainChain.TryBuildChain(drafts, chain, out _), Is.True);
            Assert.That(Ids(chain), Is.EqualTo(new[] { 1001, 1002 }));
        }

        [Test]
        public void TryBuildChain_Branch_ReturnsFalseWithReason()
        {
            var drafts = new List<QuestDraft>
            {
                Draft(1001, QuestKind.Main),
                Draft(1002, QuestKind.Main, 1001),
                Draft(1003, QuestKind.Main, 1001)
            };
            var chain = new List<QuestDraft>();

            bool linear = QuestMainChain.TryBuildChain(drafts, chain, out string reason);

            Assert.That(linear, Is.False);
            Assert.That(chain, Is.Empty);
            Assert.That(reason, Does.Contain("1001"));
        }

        [Test]
        public void TryBuildChain_TwoMainPrerequisites_ReturnsFalse()
        {
            var drafts = new List<QuestDraft>
            {
                Draft(1001, QuestKind.Main),
                Draft(1002, QuestKind.Main, 1001),
                Draft(1003, QuestKind.Main, 1001, 1002)
            };

            Assert.That(QuestMainChain.TryBuildChain(drafts, new List<QuestDraft>(), out string reason), Is.False);
            Assert.That(reason, Does.Contain("1003"));
        }

        [Test]
        public void TryBuildChain_TwoHeads_ReturnsFalse()
        {
            var drafts = new List<QuestDraft> { Draft(1001, QuestKind.Main), Draft(1003, QuestKind.Main) };

            Assert.That(QuestMainChain.TryBuildChain(drafts, new List<QuestDraft>(), out _), Is.False);
        }

        [Test]
        public void TryBuildChain_Cycle_ReturnsFalse()
        {
            var drafts = new List<QuestDraft>
            {
                Draft(1001, QuestKind.Main),
                Draft(1002, QuestKind.Main, 1003),
                Draft(1003, QuestKind.Main, 1002)
            };

            Assert.That(QuestMainChain.TryBuildChain(drafts, new List<QuestDraft>(), out _), Is.False);
        }

        [Test]
        public void TryBuildChain_NoMains_ReturnsTrueEmpty()
        {
            var drafts = new List<QuestDraft> { Draft(2001, QuestKind.Side) };
            var chain = new List<QuestDraft> { Draft(9, QuestKind.Main) };

            Assert.That(QuestMainChain.TryBuildChain(drafts, chain, out _), Is.True);
            Assert.That(chain, Is.Empty);
        }

        [Test]
        public void Move_Down_RewritesPrerequisitesKeepingSideOnes()
        {
            QuestDraft first = Draft(1001, QuestKind.Main, 2001);
            QuestDraft second = Draft(1002, QuestKind.Main, 2002, 1001);
            QuestDraft third = Draft(1003, QuestKind.Main, 1002);
            var drafts = new List<QuestDraft> { first, second, third, Draft(2001, QuestKind.Side), Draft(2002, QuestKind.Side) };
            var chain = new List<QuestDraft>();
            Assert.That(QuestMainChain.TryBuildChain(drafts, chain, out _), Is.True);

            bool moved = QuestMainChain.Move(chain, 0, 1);

            Assert.That(moved, Is.True);
            Assert.That(Ids(chain), Is.EqualTo(new[] { 1002, 1001, 1003 }));
            Assert.That(second.Prerequisites, Is.EqualTo(new[] { 2002 }));
            Assert.That(first.Prerequisites, Is.EqualTo(new[] { 2001, 1002 }));
            Assert.That(third.Prerequisites, Is.EqualTo(new[] { 1001 }));

            var rebuilt = new List<QuestDraft>();
            Assert.That(QuestMainChain.TryBuildChain(drafts, rebuilt, out _), Is.True);
            Assert.That(Ids(rebuilt), Is.EqualTo(new[] { 1002, 1001, 1003 }));
        }

        [Test]
        public void Move_Up_SwapsWithPrevious()
        {
            var chain = new List<QuestDraft>
            {
                Draft(1001, QuestKind.Main),
                Draft(1002, QuestKind.Main, 1001)
            };

            Assert.That(QuestMainChain.Move(chain, 1, -1), Is.True);
            Assert.That(Ids(chain), Is.EqualTo(new[] { 1002, 1001 }));
            Assert.That(chain[0].Prerequisites, Is.Empty);
            Assert.That(chain[1].Prerequisites, Is.EqualTo(new[] { 1002 }));
        }

        [Test]
        public void Move_OutOfRange_ReturnsFalseAndKeepsOrder()
        {
            var chain = new List<QuestDraft>
            {
                Draft(1001, QuestKind.Main),
                Draft(1002, QuestKind.Main, 1001)
            };

            Assert.That(QuestMainChain.Move(chain, 0, -1), Is.False);
            Assert.That(QuestMainChain.Move(chain, 1, 1), Is.False);
            Assert.That(Ids(chain), Is.EqualTo(new[] { 1001, 1002 }));
            Assert.That(chain[1].Prerequisites, Is.EqualTo(new[] { 1001 }));
        }

        [Test]
        public void SideUnlockLabel_NoPrerequisites_Opening()
        {
            QuestDraft side = Draft(2001, QuestKind.Side);

            Assert.That(QuestMainChain.SideUnlockLabel(side, new List<QuestDraft> { side }), Is.EqualTo("开局"));
        }

        [Test]
        public void SideUnlockLabel_OnePrerequisite_NamesIt()
        {
            QuestDraft main = Draft(1001, QuestKind.Main);
            main.Title = "找到落脚处";
            QuestDraft side = Draft(2001, QuestKind.Side, 1001);

            Assert.That(QuestMainChain.SideUnlockLabel(side, new List<QuestDraft> { main, side }),
                Is.EqualTo("在 1001 找到落脚处 之后"));
        }

        [Test]
        public void SideUnlockLabel_ManyPrerequisites_JoinsAll()
        {
            QuestDraft first = Draft(1001, QuestKind.Main);
            first.Title = "找到落脚处";
            QuestDraft second = Draft(1002, QuestKind.Main, 1001);
            second.Title = "与旅人叙旧";
            QuestDraft side = Draft(2003, QuestKind.Side, 1001, 1002);

            Assert.That(QuestMainChain.SideUnlockLabel(side, new List<QuestDraft> { first, second, side }),
                Is.EqualTo("在 1001 找到落脚处、1002 与旅人叙旧 之后"));
        }

        private static QuestDraft Draft(int id, QuestKind kind, params int[] prerequisites)
        {
            return new QuestDraft
            {
                Id = id,
                Kind = kind,
                Title = string.Empty,
                Prerequisites = new List<int>(prerequisites)
            };
        }

        private static int[] Ids(List<QuestDraft> chain) => chain.Select(draft => draft.Id).ToArray();
    }
}
