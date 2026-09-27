// 职责：空标记——挂在玩家根上，PerformanceTrigger 只认带它的碰撞体进入。
// 为什么新建（复用 → 扩展 → 新建）：DialogueInteractionActor 语义相同但属于 Dialogue，Performance 不得依赖 Dialogue；
//   用 tag 判定会与其他模块抢 tag，且字符串比较易错。一个场景一个。
using UnityEngine;

namespace Game.Performance
{
    /// <summary>演出触发者标记。挂在玩家根物体上（碰撞体可在子物体，触发器按 GetComponentInParent 找）。</summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceTriggerActor : MonoBehaviour
    {
    }
}
