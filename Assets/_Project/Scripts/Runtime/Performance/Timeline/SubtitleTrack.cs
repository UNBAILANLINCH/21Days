// 职责：时间轴上的字幕轨道——装字幕片段，建混合器并把舞台交给它（混合器经舞台拿字幕输出端）。
// 为什么新建（复用 → 扩展 → 新建）：Timeline 自带轨道（Animation / Audio / Activation / Signal）都没有「说话者 + 正文」的片段语义；
//   工程里之前没有任何自定义轨道，只能新建。
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Performance.Timeline
{
    /// <summary>
    /// 字幕轨道。**无绑定**：输出端来自 PlayableDirector 所在物体上的 <see cref="PerformanceStage"/>
    /// （所以 Director 必须和 PerformanceStage 挂在同一个物体上，即演出预制体根）。
    /// </summary>
    [TrackColor(0.95f, 0.8f, 0.3f)]
    [TrackClipType(typeof(SubtitleClip))]
    public sealed class SubtitleTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<SubtitleMixerBehaviour> mixer = ScriptPlayable<SubtitleMixerBehaviour>.Create(graph, inputCount);
            // 建图时取一次舞台引用（不在每帧 GetComponent）；输出端每帧从舞台读，服务晚于建图挂上也能生效。
            PerformanceStage stage = go == null ? null : go.GetComponent<PerformanceStage>();
            mixer.GetBehaviour().Stage = stage;
            return mixer;
        }
    }
}
