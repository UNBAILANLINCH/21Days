// 职责：统一交互的选焦点纯函数（PRP/interaction D2）——在候选里选「可交互、半径内、离原点最近」的一个，距离相等时按优先级打平。
// 为什么新建：选焦点的规则原来写了三遍（对白焦点、物资箱焦点——两者已删——与 WorldSceneDriver），距离算法各不相同；
//   收成一个静态纯函数，焦点系统与测试共用。放在焦点系统里会和输入、UI 绑在一起，没法单独测。
using System.Collections.Generic;
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>选焦点的纯函数：静态、无分配、不读输入与场景。</summary>
    public static class InteractionSelector
    {
        /// <summary>
        /// 在 <paramref name="candidates"/> 里选离 <paramref name="origin"/> 最近（三维距离）的可交互对象；没有返回 null。
        /// 跳过：空项、已销毁 / 未激活 / 未启用的组件、<see cref="IInteractable.CanInteract"/> 为 false、半径 &gt; 0 且超出半径。
        /// 距离恰好相等时 <see cref="IInteractable.InteractionPriority"/> 大者胜；再相等取列表里靠前的。
        /// </summary>
        public static IInteractable Select(Vector3 origin, IReadOnlyList<IInteractable> candidates)
        {
            if (candidates == null) return null;
            IInteractable best = null;
            float bestSqr = 0f;
            int bestPriority = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                IInteractable candidate = candidates[i];
                if (!IsAvailable(candidate) || !candidate.CanInteract) continue;
                float sqr = (candidate.Position - origin).sqrMagnitude;
                float radius = candidate.InteractionRadius;
                if (radius > 0f && sqr > radius * radius) continue;
                int priority = candidate.InteractionPriority;
                if (best == null || sqr < bestSqr || (sqr == bestSqr && priority > bestPriority))
                {
                    best = candidate;
                    bestSqr = sqr;
                    bestPriority = priority;
                }
            }
            return best;
        }

        /// <summary>
        /// 候选是否还在场：null 与已销毁的 Unity 对象（伪空）不在；Behaviour 还要激活且启用。纯 C# 实现视为恒在场。
        /// 类型判断只做模式匹配，不分配。
        /// </summary>
        public static bool IsAvailable(IInteractable candidate)
        {
            if (candidate == null) return false;
            if (candidate is Behaviour behaviour) return behaviour != null && behaviour.isActiveAndEnabled;
            if (candidate is Object unityObject) return unityObject != null;
            return true;
        }
    }
}
