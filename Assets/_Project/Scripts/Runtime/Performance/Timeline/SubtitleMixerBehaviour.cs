// 职责：字幕轨道的混合器——每帧找权重最大的字幕片段，变化时通知输出端显示 / 收起。
// 为什么新建（复用 → 扩展 → 新建）：Timeline 的混合器与轨道一一对应，字幕轨道是新建的，混合器也只能新建。
using UnityEngine;
using UnityEngine.Playables;

namespace Game.Performance.Timeline
{
    /// <summary>
    /// 字幕混合器。只在「当前片段」或「输出端」变化时调输出端，不每帧重写文本。
    /// 输出端为 null（编辑器预览、服务还没挂上）时静默。
    /// </summary>
    public sealed class SubtitleMixerBehaviour : PlayableBehaviour
    {
        private int shownInput = -1;
        private IPerformanceSubtitleSink shownSink;

        /// <summary>建图时由 <see cref="SubtitleTrack"/> 设置；可为 null。</summary>
        public PerformanceStage Stage { get; set; }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            // Stage 是 UnityEngine.Object，判空只用 == null。
            IPerformanceSubtitleSink sink = Stage == null ? null : Stage.SubtitleSink;
            if (sink == null)
            {
                shownSink = null;
                shownInput = -1;
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

            if (best == shownInput && sink == shownSink) return;
            shownInput = best;
            shownSink = sink;
            if (best < 0)
            {
                sink.HideSubtitle();
                return;
            }
            var input = (ScriptPlayable<SubtitleBehaviour>)playable.GetInput(best);
            SubtitleBehaviour data = input.GetBehaviour();
            // 只在片段变化的边沿查一次头像（名单很短，线性查找），不每帧查。
            Stage.TryGetAvatar(data.Speaker, out Sprite avatar);
            sink.ShowSubtitle(data.Speaker, data.Text, avatar);
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            // 图销毁（演出停止 / 实例归还）时收起字幕，免得最后一句残留在面板上。
            if (shownSink != null && shownInput >= 0) shownSink.HideSubtitle();
            shownSink = null;
            shownInput = -1;
        }
    }
}
