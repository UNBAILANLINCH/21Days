// 职责：钉住探索 HUD「取消键何时退出沉浸」的纯规则。
// 为什么新建：IsometricExploration 此前只有表现类（纸片 / 相机），EditMode 里没有它的测试类可扩展；
//   沉浸规则放进 EncounterSceneViewTests（Monster 目录）名字和职责都对不上。
using Game.IsometricExploration;
using NUnit.Framework;

namespace Game.Tests.EditMode.IsometricExploration
{
    public sealed class ExplorationHudPresenterTests
    {
        [Test]
        public void ShouldExitOnCancel_WhenImmersiveAndNoDialogue_ReturnsTrue()
        {
            Assert.That(ExplorationHudPresenter.ShouldExitOnCancel(true, false), Is.True);
        }

        [Test]
        public void ShouldExitOnCancel_WhenDialogueRunning_ReturnsFalse()
        {
            Assert.That(ExplorationHudPresenter.ShouldExitOnCancel(true, true), Is.False,
                "对白里的取消键归对白自己用，不能顺带退出沉浸");
        }

        [Test]
        public void ShouldExitOnCancel_WhenNotImmersive_ReturnsFalse()
        {
            Assert.That(ExplorationHudPresenter.ShouldExitOnCancel(false, false), Is.False);
        }
    }
}
