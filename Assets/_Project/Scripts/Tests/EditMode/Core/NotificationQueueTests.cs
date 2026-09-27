// 职责：钉住通知队列的规则——入队顺序、到时出队换下一条、空队列与空标题、同标题合并、秒数下限、清空。
// 为什么新建：NotificationQueue 是纯 C# 类，不需要场景与帧循环就能覆盖；对应被测类一个测试类，不能塞进别的 Tests 文件。

using Game.Core.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    public sealed class NotificationQueueTests
    {
        private NotificationQueue queue;

        [SetUp]
        public void SetUp()
        {
            queue = new NotificationQueue(true);
        }

        [Test]
        public void Advance_WhenEmpty_ReturnsFalseAndHasNoCurrent()
        {
            Assert.That(queue.Advance(1f), Is.False);
            Assert.That(queue.HasCurrent, Is.False);
            Assert.That(queue.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Enqueue_WhenTitleEmpty_IsIgnored()
        {
            Assert.That(queue.Enqueue(string.Empty, "正文", 1f), Is.False);
            Assert.That(queue.Enqueue(null, "正文", 1f), Is.False);
            Assert.That(queue.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Enqueue_DoesNotBecomeCurrentUntilAdvance()
        {
            queue.Enqueue("甲", null, 1f);

            Assert.That(queue.HasCurrent, Is.False);
            Assert.That(queue.Advance(0f), Is.True);
            Assert.That(queue.HasCurrent, Is.True);
            Assert.That(queue.Current.Title, Is.EqualTo("甲"));
        }

        [Test]
        public void Advance_ShowsEntriesInEnqueueOrder()
        {
            queue.Enqueue("甲", null, 1f);
            queue.Enqueue("乙", "正文", 1f);
            queue.Enqueue("丙", null, 1f);

            queue.Advance(0f);
            Assert.That(queue.Current.Title, Is.EqualTo("甲"));
            queue.Advance(1f);
            Assert.That(queue.Current.Title, Is.EqualTo("乙"));
            Assert.That(queue.Current.Body, Is.EqualTo("正文"));
            queue.Advance(1f);
            Assert.That(queue.Current.Title, Is.EqualTo("丙"));
        }

        [Test]
        public void Advance_BeforeTimeout_KeepsCurrent()
        {
            queue.Enqueue("甲", null, 2f);
            queue.Enqueue("乙", null, 2f);
            queue.Advance(0f);

            Assert.That(queue.Advance(0.5f), Is.False);
            Assert.That(queue.Advance(1f), Is.False);
            Assert.That(queue.Current.Title, Is.EqualTo("甲"));
            Assert.That(queue.Remaining, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Advance_OnTimeoutOfLast_ClearsCurrentAndReportsChange()
        {
            queue.Enqueue("甲", null, 1f);
            queue.Advance(0f);

            Assert.That(queue.Advance(1f), Is.True);
            Assert.That(queue.HasCurrent, Is.False);
            Assert.That(queue.Advance(1f), Is.False);
        }

        [Test]
        public void Enqueue_WhenSameTitlePending_ReplacesInPlace()
        {
            queue.Enqueue("正在显示", null, 1f);
            queue.Advance(0f);
            queue.Enqueue("甲", "旧", 1f);
            queue.Enqueue("乙", null, 1f);
            queue.Enqueue("甲", "新", 3f);

            Assert.That(queue.PendingCount, Is.EqualTo(2));
            queue.Advance(1f);
            Assert.That(queue.Current.Title, Is.EqualTo("甲"));
            Assert.That(queue.Current.Body, Is.EqualTo("新"));
            Assert.That(queue.Current.Seconds, Is.EqualTo(3f));
        }

        [Test]
        public void Enqueue_WhenSameTitleIsCurrent_AppendsInsteadOfMerging()
        {
            queue.Enqueue("甲", null, 1f);
            queue.Advance(0f);
            queue.Enqueue("甲", null, 1f);

            Assert.That(queue.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void Enqueue_WhenMergeDisabled_KeepsDuplicates()
        {
            var plain = new NotificationQueue(false);
            plain.Enqueue("甲", null, 1f);
            plain.Enqueue("甲", null, 1f);

            Assert.That(plain.PendingCount, Is.EqualTo(2));
        }

        [Test]
        public void Enqueue_WhenSecondsBelowMinimum_ClampsToMinimum()
        {
            queue.Enqueue("甲", null, 0f);
            queue.Advance(0f);

            Assert.That(queue.Current.Seconds, Is.EqualTo(NotificationQueue.MinSeconds));
        }

        [Test]
        public void Clear_DropsCurrentAndPending()
        {
            queue.Enqueue("甲", null, 1f);
            queue.Enqueue("乙", null, 1f);
            queue.Advance(0f);

            queue.Clear();

            Assert.That(queue.HasCurrent, Is.False);
            Assert.That(queue.PendingCount, Is.EqualTo(0));
            Assert.That(queue.Advance(1f), Is.False);
        }
    }
}
