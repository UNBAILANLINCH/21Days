// 职责：钉住暂停菜单的打开条件（ShouldOpen）——启动完成、不在沉浸、不在标题 / 启动状态、没开着，四条缺一不开。
// 为什么新建：PauseMenuController 是新类，没有现成测试可扩展；打开条件错了会在标题界面 / 沉浸中弹出菜单，
//   或者 Esc 退沉浸时顺带开菜单（H11 Esc 优先级），必须有 EditMode 测试守着。

using Game.Core.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    /// <summary><see cref="PauseMenuController.ShouldOpen"/> 的 EditMode 测试，纯静态判定，不涉及 Unity 对象。</summary>
    public sealed class PauseMenuControllerTests
    {
        [Test]
        public void ShouldOpen_WhenInGameplayAndNothingBlocks_ReturnsTrue()
        {
            Assert.That(PauseMenuController.ShouldOpen(true, false, false, false), Is.True);
        }

        [Test]
        public void ShouldOpen_WhenBootNotCompleted_ReturnsFalse()
        {
            Assert.That(PauseMenuController.ShouldOpen(false, false, false, false), Is.False);
        }

        [Test]
        public void ShouldOpen_WhenHudHidden_ReturnsFalse()
        {
            Assert.That(PauseMenuController.ShouldOpen(true, true, false, false), Is.False,
                "沉浸中这次 Esc 归退出沉浸，不开菜单");
        }

        [Test]
        public void ShouldOpen_WhenOnTitleOrBoot_ReturnsFalse()
        {
            Assert.That(PauseMenuController.ShouldOpen(true, false, true, false), Is.False);
        }

        [Test]
        public void ShouldOpen_WhenAlreadyOpen_ReturnsFalse()
        {
            Assert.That(PauseMenuController.ShouldOpen(true, false, false, true), Is.False, "P 键只开不关");
        }
    }
}
