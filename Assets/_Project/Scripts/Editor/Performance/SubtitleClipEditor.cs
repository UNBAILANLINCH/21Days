// 职责：字幕片段在 Timeline 窗口里的外观——片段上直接显示「说话者：正文前 12 字」，正文为空时标红提示。
// 为什么新建（复用 → 扩展 → 新建）：Timeline 默认只显示片段类名，动画师得逐个点开才知道哪句是哪句；
//   ClipEditor 必须按片段类型一一注册（CustomTimelineEditor），只能新建。
using Game.Performance.Timeline;
using UnityEditor.Timeline;
using UnityEngine.Timeline;

namespace Game.Editor.Performance
{
    [CustomTimelineEditor(typeof(SubtitleClip))]
    public sealed class SubtitleClipEditor : ClipEditor
    {
        private const int PreviewLength = 12;

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
            var subtitle = clip.asset as SubtitleClip;
            if (subtitle == null) return options;

            options.tooltip = string.IsNullOrEmpty(subtitle.Speaker) ? subtitle.Text : $"{subtitle.Speaker}：{subtitle.Text}";
            if (string.IsNullOrEmpty(options.errorText) && string.IsNullOrWhiteSpace(subtitle.Text))
                options.errorText = "这条字幕还没写正文。";
            return options;
        }

        /// <summary>片段名随内容走；只在不一致时赋值，避免每次重绘都改资产。</summary>
        private static void ApplyDisplayName(TimelineClip clip)
        {
            var subtitle = clip?.asset as SubtitleClip;
            if (subtitle == null) return;
            string body = string.IsNullOrWhiteSpace(subtitle.Text) ? "（空）" : Shorten(subtitle.Text.Replace('\n', ' '));
            string label = string.IsNullOrEmpty(subtitle.Speaker) ? body : $"{subtitle.Speaker}：{body}";
            if (clip.displayName != label) clip.displayName = label;
        }

        private static string Shorten(string text) =>
            text.Length <= PreviewLength ? text : text.Substring(0, PreviewLength) + "…";
    }
}
