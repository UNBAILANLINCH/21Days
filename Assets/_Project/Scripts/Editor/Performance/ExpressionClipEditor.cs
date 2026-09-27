// 职责：表情片段在 Timeline 窗口里的外观——片段上显示表情名，轨道没绑演员或演员没有该表情时标红提示。
// 为什么新建（复用 → 扩展 → 新建）：ClipEditor 必须按片段类型一一注册（CustomTimelineEditor），
//   SubtitleClipEditor 管的是另一种片段，只能新建。
using System;
using System.Collections.Generic;
using Game.Performance;
using Game.Performance.Timeline;
using UnityEditor.Timeline;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Editor.Performance
{
    [CustomTimelineEditor(typeof(ExpressionClip))]
    public sealed class ExpressionClipEditor : ClipEditor
    {
        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            ApplyDisplayName(clip);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            ApplyDisplayName(clip);
        }

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            ApplyDisplayName(clip);
            var expression = clip.asset as ExpressionClip;
            if (expression == null || !string.IsNullOrEmpty(options.errorText)) return options;

            options.tooltip = $"切到表情「{expression.ExpressionName}」";
            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null) return options;

            var actor = director.GetGenericBinding(clip.GetParentTrack()) as PerformanceActor;
            if (actor == null)
                options.errorText = "表情轨道没有绑定演员。";
            else if (!Contains(actor.ExpressionNames, expression.ExpressionName))
                options.errorText = $"演员「{actor.name}」没有表情「{expression.ExpressionName}」。";
            return options;
        }

        /// <summary>片段名随表情名走；只在不一致时赋值，避免每次重绘都改资产。</summary>
        private static void ApplyDisplayName(TimelineClip clip)
        {
            var expression = clip?.asset as ExpressionClip;
            if (expression == null) return;
            string label = string.IsNullOrEmpty(expression.ExpressionName) ? "（未填表情）" : expression.ExpressionName;
            if (clip.displayName != label) clip.displayName = label;
        }

        private static bool Contains(IReadOnlyList<string> names, string value)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
