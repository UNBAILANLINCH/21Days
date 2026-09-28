// 职责：钉住 MirrorInputPresenter 的纯判定——让位原因与优先级（PRD V11：对白中、暂停、沉浸、结果画面开着时照镜键无效）、
//   冷却、结果画面到时 / 确认键关闭。
// 为什么新建：一个被测类一个测试类；入口点的接线（输入、UI、资源）由回放验证，这里只测抽出来的纯函数。
using Game.Mirror;
using NUnit.Framework;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorInputPresenter"/> 纯判定的 EditMode 测试。</summary>
    public sealed class MirrorInputPresenterTests
    {
        private const float Cooldown = 0.4f;

        [Test]
        public void BlockReason_NothingBlocking_ReturnsNull()
        {
            Assert.That(MirrorInputPresenter.BlockReason(false, false, false, false, 10f, Cooldown), Is.Null);
        }

        [TestCase(true, false, false, false, 10f, "view_open")]
        [TestCase(false, true, false, false, 10f, "dialogue")]
        [TestCase(false, false, true, false, 10f, "paused")]
        [TestCase(false, false, false, true, 10f, "hud_hidden")]
        [TestCase(false, false, false, false, 0.1f, "cooldown")]
        public void BlockReason_EachCondition_Reported(bool viewOpen, bool dialogue, bool paused, bool hudHidden,
            float since, string expected)
        {
            Assert.That(MirrorInputPresenter.BlockReason(viewOpen, dialogue, paused, hudHidden, since, Cooldown),
                Is.EqualTo(expected));
        }

        [Test]
        public void BlockReason_Several_FirstInOrderWins()
        {
            Assert.That(MirrorInputPresenter.BlockReason(true, true, true, true, 0f, Cooldown), Is.EqualTo("view_open"));
            Assert.That(MirrorInputPresenter.BlockReason(false, true, true, true, 0f, Cooldown), Is.EqualTo("dialogue"));
            Assert.That(MirrorInputPresenter.BlockReason(false, false, true, true, 0f, Cooldown), Is.EqualTo("paused"));
        }

        [Test]
        public void BlockReason_CooldownBoundary_ElapsedEqualsCooldownAllowed()
        {
            Assert.That(MirrorInputPresenter.BlockReason(false, false, false, false, Cooldown, Cooldown), Is.Null);
        }

        [Test]
        public void ShouldCloseResult_ConfirmPressed_ClosesImmediately()
        {
            Assert.That(MirrorInputPresenter.ShouldCloseResult(0f, 2.5f, true), Is.True);
        }

        [Test]
        public void ShouldCloseResult_ByTime()
        {
            Assert.That(MirrorInputPresenter.ShouldCloseResult(2.4f, 2.5f, false), Is.False);
            Assert.That(MirrorInputPresenter.ShouldCloseResult(2.5f, 2.5f, false), Is.True);
        }

        [Test]
        public void ShouldCloseResult_ZeroSeconds_OnlyByKey()
        {
            Assert.That(MirrorInputPresenter.ShouldCloseResult(100f, 0f, false), Is.False);
            Assert.That(MirrorInputPresenter.ShouldCloseResult(100f, 0f, true), Is.True);
        }
    }
}
