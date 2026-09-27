// 职责：字幕片段资产——一句字幕的说话者与正文；片段在时间轴上的起止即显示区间。
// 为什么新建（复用 → 扩展 → 新建）：Timeline 没有文本片段；字幕轨道要求片段类型一一对应（TrackClipType），只能新建。
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Performance.Timeline
{
    /// <summary>字幕片段。不支持混合 / 外插（<see cref="ClipCaps.None"/>）：字幕是硬切换。</summary>
    [System.Serializable]
    public sealed class SubtitleClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("说话者名字；留空表示旁白（面板隐藏名字栏）。")]
        [SerializeField] private string speaker = string.Empty;

        [Tooltip("字幕正文。")]
        [TextArea(2, 5)]
        [SerializeField] private string text = string.Empty;

        public string Speaker => speaker;
        public string Text => text;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<SubtitleBehaviour> playable = ScriptPlayable<SubtitleBehaviour>.Create(graph);
            SubtitleBehaviour behaviour = playable.GetBehaviour();
            behaviour.Speaker = speaker;
            behaviour.Text = text;
            return playable;
        }
    }
}
