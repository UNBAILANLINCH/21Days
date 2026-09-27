// 职责：表情片段资产——片段开始时让绑定的演员切到 expressionName。
// 为什么新建（复用 → 扩展 → 新建）：表情轨道要求片段类型一一对应（TrackClipType），只能新建。
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Performance.Timeline
{
    /// <summary>表情片段。表情名须在演员的 <see cref="PerformanceActor.ExpressionNames"/> 里（编辑器校验会检查）。</summary>
    [System.Serializable]
    public sealed class ExpressionClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("表情名，须与演员表情列表里的名字一致（大小写敏感）。")]
        [SerializeField] private string expressionName = string.Empty;

        public string ExpressionName => expressionName;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<ExpressionBehaviour> playable = ScriptPlayable<ExpressionBehaviour>.Create(graph);
            playable.GetBehaviour().ExpressionName = expressionName;
            return playable;
        }
    }
}
