// 职责：InventoryHotkeyPresenter.ShouldOpen 的分支回归——何时按背包键开面板。
// 为什么新建：对应被测类一个测试类；被测方法是纯静态函数，不需要容器、事件总线与 UIService。

using Game.Inventory;
using NUnit.Framework;

namespace Game.Tests.EditMode.Inventory
{
    public sealed class InventoryHotkeyPresenterTests
    {
        [Test]
        public void ShouldOpen_WhenIdle_ReturnsTrue()
        {
            Assert.That(InventoryHotkeyPresenter.ShouldOpen(false, false, false), Is.True);
        }

        [Test]
        public void ShouldOpen_WhenDialogueRunning_ReturnsFalse()
        {
            Assert.That(InventoryHotkeyPresenter.ShouldOpen(true, false, false), Is.False);
        }

        [Test]
        public void ShouldOpen_WhenHudHidden_ReturnsFalse()
        {
            Assert.That(InventoryHotkeyPresenter.ShouldOpen(false, true, false), Is.False);
        }

        [Test]
        public void ShouldOpen_WhenPanelBusy_ReturnsFalse()
        {
            Assert.That(InventoryHotkeyPresenter.ShouldOpen(false, false, true), Is.False);
        }
    }
}
