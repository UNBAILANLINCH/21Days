// 职责：QuestHudPresenter 纯逻辑的 EditMode 回归：任务键（Gameplay/Journal）何时开面板、键位提示取哪条绑定，
//   以及任务标记接管 NPC 头顶图标的成对接管 / 交还（SwitchIconOverride）。
// 为什么新建：对应被测类一个测试类；现有 Quest 测试各对一个被测类（通知、规则、指引数学），塞进去名实不符。
//   被测方法都是纯静态函数，不需要容器、事件总线与 UIService 就能覆盖。

using Game.Dialogue;
using Game.Quest;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestHudPresenterTests
    {
        [Test]
        public void ShouldOpenOnJournal_WhenHudReadyAndIdle_ReturnsTrue()
        {
            Assert.That(QuestHudPresenter.ShouldOpenOnJournal(true, false, false), Is.True);
        }

        [Test]
        public void ShouldOpenOnJournal_WhenBlocked_ReturnsFalse()
        {
            Assert.That(QuestHudPresenter.ShouldOpenOnJournal(false, false, false), Is.False, "HUD 还没开好（标题界面前）");
            Assert.That(QuestHudPresenter.ShouldOpenOnJournal(true, true, false), Is.False, "对白进行中");
            Assert.That(QuestHudPresenter.ShouldOpenOnJournal(true, false, true), Is.False, "面板已开着，任务键不负责关");
        }

        [Test]
        public void KeyboardBindingDisplay_WithKeyboardAndGamepad_ReturnsKeyboardText()
        {
            // 与 GameInput 里 Gameplay/Journal 同形：先手柄后键盘也要取到键盘那条。
            var action = new InputAction("Journal", InputActionType.Button);
            action.AddBinding("<Gamepad>/select");
            action.AddBinding("<Keyboard>/tab");
            try
            {
                Assert.That(QuestHudPresenter.KeyboardBindingDisplay(action), Is.EqualTo("Tab"));
            }
            finally
            {
                action.Dispose();
            }
        }

        [Test]
        public void KeyboardBindingDisplay_WithoutKeyboardBinding_ReturnsEmpty()
        {
            var action = new InputAction("Journal", InputActionType.Button);
            action.AddBinding("<Gamepad>/select");
            try
            {
                Assert.That(QuestHudPresenter.KeyboardBindingDisplay(action), Is.Empty);
                Assert.That(QuestHudPresenter.KeyboardBindingDisplay(null), Is.Empty);
            }
            finally
            {
                action.Dispose();
            }
        }

        [Test]
        public void SwitchIconOverride_TakeSwitchRelease_PairsCorrectly()
        {
            var a = new GameObject("NpcA");
            var b = new GameObject("NpcB");
            try
            {
                DialogueInteractable npcA = a.AddComponent<DialogueInteractable>();
                DialogueInteractable npcB = b.AddComponent<DialogueInteractable>();

                DialogueInteractable current = QuestHudPresenter.SwitchIconOverride(null, npcA);
                Assert.That(current, Is.SameAs(npcA));
                Assert.That(npcA.MarkerOverridden, Is.True, "接管 A");

                current = QuestHudPresenter.SwitchIconOverride(current, npcA);
                Assert.That(current, Is.SameAs(npcA));
                Assert.That(npcA.MarkerOverridden, Is.True, "同一目标重复调用保持接管");

                current = QuestHudPresenter.SwitchIconOverride(current, npcB);
                Assert.That(current, Is.SameAs(npcB));
                Assert.That(npcA.MarkerOverridden, Is.False, "换目标时交还 A");
                Assert.That(npcB.MarkerOverridden, Is.True, "换目标时接管 B");

                current = QuestHudPresenter.SwitchIconOverride(current, null);
                Assert.That(current == null, Is.True);
                Assert.That(npcB.MarkerOverridden, Is.False, "隐藏标记时交还 B");
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void SwitchIconOverride_CurrentDestroyed_SkipsReleaseAndTakesNext()
        {
            var a = new GameObject("NpcA");
            var b = new GameObject("NpcB");
            try
            {
                DialogueInteractable npcA = a.AddComponent<DialogueInteractable>();
                DialogueInteractable npcB = b.AddComponent<DialogueInteractable>();
                DialogueInteractable current = QuestHudPresenter.SwitchIconOverride(null, npcA);
                Object.DestroyImmediate(a);

                // 场景卸载后旧目标已伪空：不应抛异常，照常接管新目标。
                current = QuestHudPresenter.SwitchIconOverride(current, npcB);
                Assert.That(current, Is.SameAs(npcB));
                Assert.That(npcB.MarkerOverridden, Is.True);
            }
            finally
            {
                if (a != null) Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }
    }
}
