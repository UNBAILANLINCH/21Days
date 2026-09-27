// 职责：QuestNotificationPresenter.Compose 文案格式化的 EditMode 回归（正常格式、空格式、写坏的格式、空标题）。
// 为什么新建：对应被测类一个测试类；Compose 是纯静态函数，不需要容器与事件总线就能覆盖。

using Game.Quest;
using NUnit.Framework;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestNotificationPresenterTests
    {
        [Test]
        public void Compose_WithPlaceholder_InsertsTitle()
        {
            Assert.That(QuestNotificationPresenter.Compose("接取任务：{0}", "初到营地"), Is.EqualTo("接取任务：初到营地"));
        }

        [Test]
        public void Compose_WhenFormatEmpty_ReturnsTitle()
        {
            Assert.That(QuestNotificationPresenter.Compose(string.Empty, "初到营地"), Is.EqualTo("初到营地"));
        }

        [Test]
        public void Compose_WhenFormatBroken_FallsBackWithoutThrowing()
        {
            Assert.That(QuestNotificationPresenter.Compose("任务完成：{1}", "初到营地"), Is.EqualTo("任务完成：{1}初到营地"));
        }

        [Test]
        public void Compose_WhenTitleNull_TreatsAsEmpty()
        {
            Assert.That(QuestNotificationPresenter.Compose("任务完成：{0}", null), Is.EqualTo("任务完成："));
        }
    }
}
