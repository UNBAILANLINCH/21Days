// 职责：钉住 UI 栈的分层规则——全屏面板剔除下层、弹窗叠加、Top 优先级、Hud/Top 不进栈。
// 为什么新建：这几条是整套 UI 里最容易被后续改动悄悄破坏的约定，而 UIStack 是纯 C# 类，
//   不需要场景也不需要帧循环就能覆盖；对应的被测类一个测试类，不能塞进别的 Tests 文件。

using System.Collections.Generic;
using Game.Core.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// UIStack 的 EditMode 测试。条目用假实现，不涉及任何 UnityEngine.Object。
    /// </summary>
    public sealed class UIStackTests
    {
        private UIStack stack;

        [SetUp]
        public void SetUp()
        {
            stack = new UIStack();
        }

        [Test]
        public void Push_WhenPanelIsNotFullScreen_HidesNothingBelow()
        {
            var below = new Entry(UILayer.Panel, true);
            stack.Push(below);

            var overlay = new Entry(UILayer.Panel, false);
            IReadOnlyList<IUIStackEntry> hidden = stack.Push(overlay);

            Assert.That(hidden, Is.Empty, "半透明 / 非全屏面板不该把下面的面板藏掉");
            Assert.That(stack.IsHidden(below), Is.False);
        }

        [Test]
        public void Push_WhenPanelIsFullScreen_HidesEveryPanelBelow()
        {
            var first = new Entry(UILayer.Panel, true);
            var second = new Entry(UILayer.Panel, false);
            stack.Push(first);
            stack.Push(second);

            var fullScreen = new Entry(UILayer.Panel, true);
            IReadOnlyList<IUIStackEntry> hidden = stack.Push(fullScreen);

            Assert.That(hidden, Is.EquivalentTo(new IUIStackEntry[] { first, second }));
            Assert.That(stack.IsHidden(fullScreen), Is.False, "自己不能把自己藏了");
        }

        [Test]
        public void Remove_WhenFullScreenPanelPopped_ShowsThePanelDirectlyBelow()
        {
            var below = new Entry(UILayer.Panel, true);
            var top = new Entry(UILayer.Panel, true);
            stack.Push(below);
            stack.Push(top);
            Assert.That(stack.IsHidden(below), Is.True);

            IReadOnlyList<IUIStackEntry> shown = stack.Remove(top);

            Assert.That(shown, Is.EquivalentTo(new IUIStackEntry[] { below }));
            Assert.That(stack.IsHidden(below), Is.False);
            Assert.That(stack.Top(), Is.SameAs(below));
        }

        [Test]
        public void Remove_WhenFullScreenAboveOverlay_RestoresBothAndKeepsOverlayAfterBottomCloses()
        {
            // 三层交错：全屏 A → 非全屏 B（盖在 A 上但不遮住它）→ 全屏 C（把 A、B 全遮住）。
            // 这是增量记账（「我隐藏了谁就恢复谁」）唯一漏恢复的形状，Resync 的整体重算就是为它写的。
            var fullScreenA = new Entry(UILayer.Panel, true);
            var overlayB = new Entry(UILayer.Panel, false);
            var fullScreenC = new Entry(UILayer.Panel, true);

            stack.Push(fullScreenA);
            Assert.That(stack.Push(overlayB), Is.Empty, "非全屏面板压上来不隐藏下层");

            IReadOnlyList<IUIStackEntry> hidden = stack.Push(fullScreenC);
            Assert.That(hidden, Is.EquivalentTo(new IUIStackEntry[] { fullScreenA, overlayB }),
                "全屏面板压上来要把下面整叠都藏掉");

            IReadOnlyList<IUIStackEntry> shown = stack.Remove(fullScreenC);
            Assert.That(shown, Is.EquivalentTo(new IUIStackEntry[] { fullScreenA, overlayB }),
                "关掉最上面的全屏面板后，下面那一叠（全屏 A + 叠在它上面的非全屏 B）都要恢复显示");
            Assert.That(stack.IsHidden(fullScreenA), Is.False);
            Assert.That(stack.IsHidden(overlayB), Is.False);
            Assert.That(stack.Top(), Is.SameAs(overlayB));

            // 再把底下的全屏 A 关掉：B 是独立压进来的，不跟着 A 走，仍在栈里且可见。
            IReadOnlyList<IUIStackEntry> afterBottomClosed = stack.Remove(fullScreenA);

            Assert.That(afterBottomClosed, Is.Empty, "B 本来就是可见的，不该被再翻一次");
            Assert.That(stack.Contains(overlayB), Is.True);
            Assert.That(stack.IsHidden(overlayB), Is.False);
            Assert.That(stack.Top(), Is.SameAs(overlayB));
        }

        [Test]
        public void Push_WhenPopupsStack_DoesNotTouchPanelLayer()
        {
            var panel = new Entry(UILayer.Panel, true);
            stack.Push(panel);

            var firstPopup = new Entry(UILayer.Popup, true);
            var secondPopup = new Entry(UILayer.Popup, true);
            IReadOnlyList<IUIStackEntry> firstHidden = stack.Push(firstPopup);
            IReadOnlyList<IUIStackEntry> secondHidden = stack.Push(secondPopup);

            Assert.That(firstHidden, Is.Empty);
            Assert.That(secondHidden, Is.Empty, "弹窗可以叠加，互相不隐藏");
            Assert.That(stack.IsHidden(panel), Is.False, "弹窗不该影响 Panel 层");
            Assert.That(stack.Popups.Count, Is.EqualTo(2));
        }

        [Test]
        public void Top_WhenBothLayersHaveEntries_PrefersPopup()
        {
            var panel = new Entry(UILayer.Panel, true);
            var popup = new Entry(UILayer.Popup, true);
            stack.Push(panel);
            stack.Push(popup);

            Assert.That(stack.Top(), Is.SameAs(popup));

            stack.Remove(popup);
            Assert.That(stack.Top(), Is.SameAs(panel), "弹窗关掉后回落到最上面的面板");
        }

        [Test]
        public void Push_WhenLayerIsHudOrTop_DoesNotEnterStack()
        {
            var hud = new Entry(UILayer.Hud, false);
            var top = new Entry(UILayer.Top, false);

            Assert.That(stack.Push(hud), Is.Empty);
            Assert.That(stack.Push(top), Is.Empty);
            Assert.That(stack.Contains(hud), Is.False);
            Assert.That(stack.Contains(top), Is.False);
            Assert.That(stack.Top(), Is.Null, "Hud / Top 是常驻层，不该被 CloseTop 关掉");
        }

        /// <summary>栈条目的假实现：只有层和是否全屏两个属性，不碰 Unity。</summary>
        private sealed class Entry : IUIStackEntry
        {
            public Entry(UILayer layer, bool isFullScreen)
            {
                Layer = layer;
                IsFullScreen = isFullScreen;
            }

            public UILayer Layer { get; }

            public bool IsFullScreen { get; }
        }
    }
}
