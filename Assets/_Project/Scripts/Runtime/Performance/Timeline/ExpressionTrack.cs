// 职责：时间轴上的表情轨道——绑定一个演员，片段开始时切到片段指定的表情。
// 为什么新建（复用 → 扩展 → 新建）：Timeline 自带轨道没有「按名字切表情」的语义；Animation 轨道能换 Sprite，
//   但 Live2D 表情走 CubismExpressionController 的索引而不是动画曲线，需要一层与演员实现无关的抽象，只能新建。
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Performance.Timeline
{
    /// <summary>表情轨道。绑定 <see cref="PerformanceActor"/>（占位 Sprite 演员或 Live2D 演员）。</summary>
    [TrackColor(0.45f, 0.75f, 0.95f)]
    [TrackBindingType(typeof(PerformanceActor))]
    [TrackClipType(typeof(ExpressionClip))]
    public sealed class ExpressionTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<ExpressionMixerBehaviour>.Create(graph, inputCount);
        }

        // 编辑器里拖时间线预览会改演员的显示属性；登记成「预览驱动」的属性，退出预览时 Timeline 会把它们还原，不弄脏预制体。
        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            if (director != null)
            {
                var actor = director.GetGenericBinding(this) as PerformanceActor;
                if (actor != null) actor.GatherPreviewProperties(driver);
            }
            base.GatherProperties(director, driver);
        }
    }
}
