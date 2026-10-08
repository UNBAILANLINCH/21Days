// 职责：Interaction 测试共用的纯 C# 可交互替身——位置 / 半径 / 可交互 / 优先级可设，记录交互与焦点回调次数。
// 为什么新建：选择函数与焦点系统两组测试都要用；IInteractable 的生产实现都是场景组件，测纯逻辑时用替身更直接。
using Game.Interaction;
using UnityEngine;

namespace Game.Tests.EditMode.Interaction
{
    /// <summary>纯 C# 的可交互替身：位置 / 半径 / 可交互 / 优先级可设，记录交互与焦点回调次数。</summary>
    internal sealed class FakeInteractable : IInteractable
    {
        public FakeInteractable(Vector3 position, float radius)
        {
            Position = position;
            InteractionRadius = radius;
        }

        public Vector3 Position { get; set; }
        public float InteractionRadius { get; set; }
        public bool CanInteract { get; set; } = true;
        public int InteractionPriority { get; set; }
        public InteractionPrompt Prompt { get; set; } = new InteractionPrompt("对话", "替身");
        public int InteractCount { get; private set; }
        public bool Focused { get; private set; }
        public int FocusCallbacks { get; private set; }

        public void Interact() => InteractCount++;

        public void OnFocusChanged(bool focused)
        {
            Focused = focused;
            FocusCallbacks++;
        }
    }
}
