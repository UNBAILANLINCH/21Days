// 职责：统一交互的登记表对外口（PRP/interaction §4）——各模块的场景登记器把自己的可交互对象登记 / 注销进来，
//   焦点系统只从这里读候选；玩家锚点（InteractionActor）也从这里取。
// 为什么新建：原来候选表与玩家锚点都在 DialogueSceneBinder 里，Loot / Narrative 要拿玩家锚点只能反向依赖对白；
//   接口与实现分开，是为了让 Dialogue / Loot / Narrative 只认这份契约，测试里也能直接 new 实现。
using System;
using System.Collections.Generic;

namespace Game.Interaction
{
    /// <summary>可交互对象登记表 + 玩家锚点。只在事件驱动路径（场景加载 / 卸载）上变，不每帧扫描。</summary>
    public interface IInteractionRegistry
    {
        /// <summary>已登记的候选（含暂时不可交互的），焦点系统每帧只读这个列表。</summary>
        IReadOnlyList<IInteractable> Candidates { get; }

        /// <summary>场景里的玩家标记；没有或所在场景已卸载时为 null（用 == null 判）。</summary>
        InteractionActor Actor { get; }

        /// <summary>玩家标记变化（含变为 null）。只在场景加载 / 卸载时触发。</summary>
        event Action<InteractionActor> OnActorChanged;

        /// <summary>登记一个候选；重复登记忽略。</summary>
        void Register(IInteractable interactable);

        /// <summary>注销一个候选；没登记过忽略。</summary>
        void Unregister(IInteractable interactable);
    }
}
