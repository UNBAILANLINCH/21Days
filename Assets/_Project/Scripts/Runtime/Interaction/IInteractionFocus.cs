// 职责：统一交互焦点的只读对外口（PRP/interaction §4）——当前焦点、焦点变化、交互已触发三件事。
// 为什么新建：别的模块（第二波的处决按键归属 D6、小人转向 D11、回放）只需要读焦点，不该拿到能驱动焦点的具体类；
//   接口与实现分开，读方依赖面最小。
using System;

namespace Game.Interaction
{
    /// <summary>交互焦点（只读）。全局任一时刻至多一个焦点；没有时 <see cref="Current"/> 为 null。</summary>
    public interface IInteractionFocus
    {
        /// <summary>当前焦点；没有时为 null。</summary>
        IInteractable Current { get; }

        /// <summary>焦点变化（含变为 null）。只在变化时触发，不每帧触发。</summary>
        event Action<IInteractable> OnFocusChanged;

        /// <summary>焦点系统刚对 <c>Current</c> 调过 <see cref="IInteractable.Interact"/>（交互键或点了交互提示）。</summary>
        event Action<IInteractable> OnInteracted;
    }
}
