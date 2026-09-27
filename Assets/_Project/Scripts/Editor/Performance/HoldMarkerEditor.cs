// 职责：停顿标记在 Timeline 窗口里的外观——悬停提示「等待玩家确认」，标记旁画一个「▼」，放在开头 / 末尾之外时标红。
// 为什么新建（复用 → 扩展 → 新建）：MarkerEditor 必须按标记类型注册（CustomTimelineEditor），工程里没有别的标记编辑器，只能新建。
using Game.Performance.Timeline;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace Game.Editor.Performance
{
    [CustomTimelineEditor(typeof(HoldMarker))]
    public sealed class HoldMarkerEditor : MarkerEditor
    {
        private const float GlyphWidth = 14f;

        private static GUIStyle glyphStyle;

        public override MarkerDrawOptions GetMarkerOptions(IMarker marker)
        {
            MarkerDrawOptions options = base.GetMarkerOptions(marker);
            var hold = marker as HoldMarker;
            options.tooltip = hold == null || string.IsNullOrEmpty(hold.Label) ? "等待玩家确认" : $"等待玩家确认：{hold.Label}";

            TimelineAsset timeline = marker.parent == null ? null : marker.parent.timelineAsset;
            if (timeline != null && (marker.time <= 0d || marker.time >= timeline.duration))
                options.errorText = "停顿标记放在了时间轴开头或末尾之外，玩家可能等不到它。";
            return options;
        }

        public override void DrawOverlay(IMarker marker, MarkerUIStates uiState, MarkerOverlayRegion region)
        {
            if (glyphStyle == null)
            {
                glyphStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
                glyphStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
            }

            Rect markerRect = region.markerRegion;
            var glyphRect = new Rect(markerRect.xMax, markerRect.y, GlyphWidth, markerRect.height);
            GUI.Label(glyphRect, "▼", glyphStyle);
        }
    }
}
