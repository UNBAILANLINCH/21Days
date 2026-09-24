// 职责：标记「谁是玩家」——交互焦点系统以它为测距原点，NPC 未配 actor 时也用它判定交互半径。
// 为什么新建：复用——玩家根上现有的移动 / 表现组件属于其他模块，对白模块不该依赖它们的私有实现；
//   扩展——塞进 DialogueInteractable 不对（那是 NPC 侧），塞进移动组件会让探索模块反向依赖对白。只能是独立的空标记。
using UnityEngine;

namespace Game.Dialogue
{
    /// <summary>挂在玩家根物体上的空标记，不含逻辑。场景里应只有一个。</summary>
    [DisallowMultipleComponent]
    public sealed class DialogueInteractionActor : MonoBehaviour
    {
        /// <summary>测距锚点（即本物体）。</summary>
        public Transform Anchor => transform;
    }
}
