// 职责：钉住开局操作说明的「最短展示时长」这条规则——时长没到，按什么键都不算数。
// 为什么新建：这条规则错了不会编译报错，只会表现成「点了开始，教程一闪而过」，
//   而那个输入正是玩家点「开始」按钮的那一下，靠试玩很难稳定复现，所以按 UIStackTests 的做法抽成纯函数直接钉住。
//
// 只测纯静态判定，不碰 MonoBehaviour 生命周期与真实输入设备：那部分归 NewGameTutorialTests 与编辑器实测。

using Game.Core.UI.Views;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    /// <summary><see cref="TutorialView.CanDismiss"/> 的 EditMode 测试。</summary>
    public sealed class TutorialViewTests
    {
        [Test]
        public void CanDismiss_BeforeMinSeconds_IsFalse()
        {
            Assert.That(TutorialView.CanDismiss(0f, 3f), Is.False, "刚打开时点「开始」的那一下输入不算数");
            Assert.That(TutorialView.CanDismiss(2.99f, 3f), Is.False, "差一帧也不行");
        }

        [Test]
        public void CanDismiss_AtOrAfterMinSeconds_IsTrue()
        {
            Assert.That(TutorialView.CanDismiss(3f, 3f), Is.True, "满 3 秒即可关闭");
            Assert.That(TutorialView.CanDismiss(3.5f, 3f), Is.True);
        }

        [Test]
        public void CanDismiss_WhenMinSecondsIsZero_IsTrueImmediately()
        {
            Assert.That(TutorialView.CanDismiss(0f, 0f), Is.True, "配成 0 就是「不等，随时可按任意键跳过」");
        }
    }
}
