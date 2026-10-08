// 职责：标记「谁是玩家」——统一交互焦点以它为测距原点；Dialogue 的 NPC 未配 actor 时、Narrative 的叙事目标也用它判定交互距离。
// 为什么迁入并改名（PRP/interaction D7）：这个标记原在 Dialogue 模块（类名带 Dialogue 前缀），本来就不是对白私有的，Loot、Narrative 已在借用；
//   git mv 连同 .meta 移入 Interaction 模块，GUID 不变，场景里玩家身上的组件引用不断。
// 为什么当初新建：复用——玩家根上现有的移动 / 表现组件属于其他模块，交互不该依赖它们的私有实现；
//   扩展——塞进可交互物体不对（那是被交互的一侧），塞进移动组件会让探索模块反向依赖交互。只能是独立的空标记。
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>挂在玩家根物体上的空标记，不含逻辑。场景里应只有一个；由 <see cref="InteractionRegistry"/> 在场景加载时找出。</summary>
    [DisallowMultipleComponent]
    public sealed class InteractionActor : MonoBehaviour
    {
        /// <summary>测距锚点（即本物体）。</summary>
        public Transform Anchor => transform;
    }
}
