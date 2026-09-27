// 职责：表情轨道的混合器——当前片段变化（激活边沿）时调绑定演员的 SetExpression，片段结束不回退表情。
// 为什么新建（复用 → 扩展 → 新建）：Timeline 的混合器与轨道一一对应，表情轨道是新建的，混合器也只能新建。
using UnityEngine.Playables;

namespace Game.Performance.Timeline
{
    /// <summary>
    /// 表情混合器。绑定对象由 Timeline 作为 <c>playerData</c> 传入；未绑定时静默。
    /// 只在权重最大的片段换了时切一次表情，不每帧调演员。
    /// </summary>
    public sealed class ExpressionMixerBehaviour : PlayableBehaviour
    {
        private int activeInput = -1;
        private PerformanceActor activeActor;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var actor = playerData as PerformanceActor;
            if (actor == null)
            {
                activeActor = null;
                activeInput = -1;
                return;
            }

            int best = -1;
            float bestWeight = 0f;
            int count = playable.GetInputCount();
            for (int i = 0; i < count; i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight > bestWeight)
                {
                    bestWeight = weight;
                    best = i;
                }
            }

            if (best == activeInput && actor == activeActor) return;
            activeInput = best;
            activeActor = actor;
            // 片段之间的空白保持上一个表情，不回默认：动画师放下一个片段才换。
            if (best < 0) return;
            var input = (ScriptPlayable<ExpressionBehaviour>)playable.GetInput(best);
            string expression = input.GetBehaviour().ExpressionName;
            if (!string.IsNullOrEmpty(expression)) actor.SetExpression(expression);
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            activeActor = null;
            activeInput = -1;
        }
    }
}
