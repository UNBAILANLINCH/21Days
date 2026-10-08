// 职责：统一交互的契约（PRP/interaction D5）——「走近 → 出提示 → 按键触发」的被交互一方要提供的最小集合。
// 为什么新建：原来 Dialogue、Loot 各写一套焦点，各自只认自己的组件类型（DialogueInteractable / SupplyCrate），
//   一次按键可能同时触发两件事；没有现成的接口可复用。只取覆盖现有对象的最小集合，头顶标记的显隐规则各模块不同，留在各自模块，
//   契约只给一个焦点变化回调让它们知道自己是不是焦点。
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// 可交互对象。实现方由各自模块的场景登记器登记进 <see cref="IInteractionRegistry"/>，由 <see cref="InteractionFocus"/> 统一选焦点、读交互键。
    /// 实现方若是 <see cref="Behaviour"/>，未激活 / 未启用 / 已销毁时选择函数直接跳过，<see cref="CanInteract"/> 不必再判这些。
    /// </summary>
    public interface IInteractable
    {
        /// <summary>测距用的世界坐标（三维距离）。</summary>
        Vector3 Position { get; }

        /// <summary>交互半径（世界单位）；小于等于 0 表示不限距离（沿用 Dialogue 的语义）。</summary>
        float InteractionRadius { get; }

        /// <summary>现在能否交互，**不含距离**（距离由选择函数按 <see cref="InteractionRadius"/> 判）。</summary>
        bool CanInteract { get; }

        /// <summary>优先级：只在距离相等时打平，大者胜；现有对象全是 0。</summary>
        int InteractionPriority { get; }

        /// <summary>提示内容（动词 + 名字），成为焦点时由提示 HUD 显示。</summary>
        InteractionPrompt Prompt { get; }

        /// <summary>按下交互键 / 点了交互提示：执行交互。焦点系统只在本帧焦点没变化时调用。</summary>
        void Interact();

        /// <summary>焦点变化回调：成为焦点传 true、失去焦点传 false。只有焦点系统调用；实现方据此驱动自己的头顶标记。</summary>
        void OnFocusChanged(bool focused);
    }
}
