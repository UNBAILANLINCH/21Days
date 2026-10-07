// 职责：统一等宽四轨和 Tap/Hold 网格。独立 UGUI Graphic 保持轨道与长条使用同一线性坐标。
using Game.Core.Simulation;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Rhythm
{
    public sealed class RhythmTrackGraphic : MaskableGraphic
    {
        private bool background;
        private int lane;
        private float head;
        private float tail;
        private bool hold;
        public void Configure(bool background)
        { this.background = background; raycastTarget = false; SetVerticesDirty(); }
        public void Show(int lane, float head, float tail, bool hold, Color tint)
        {
            this.lane = lane; this.head = head; this.tail = tail; this.hold = hold; color = tint; SetVerticesDirty();
        }
        // 不钳制头部：经过判定线后仍保持匀速，不粘在线上；长条超出轨道的部分只裁切显示。
        public static float PositionAt(double targetSeconds, double songSeconds, float visualOffsetMs, float approachSeconds)
            => (float)((targetSeconds - songSeconds + visualOffsetMs / 1000d) / approachSeconds);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            if (background)
            {
                for (int i = 0; i < 4; i++)
                {
                    var tint = RhythmView.LaneColor(i); tint.a = 0.1f;
                    Quad(vh, i, 0, 1, tint, rect, 0.008f);
                }
                return;
            }
            if (rect.height <= 0 || rect.width <= 0) return;
            if (hold && tail > head)
            {
                var bodyColor = color; bodyColor.a = 0.65f;
                float bottom = GameMath.Clamp01(head); float top = GameMath.Clamp01(tail);
                if (top > bottom) Quad(vh, lane, bottom, top, bodyColor, rect, 0.035f);
            }
            // 头部中心在目标时刻落到判定线，宽高不随远近缩放。
            Quad(vh, lane, head - 10 / rect.height, head + 10 / rect.height, color, rect, 0.018f);
        }
        private static void Quad(VertexHelper vh, int lane, float bottom, float top, Color tint, Rect rect, float padding)
        {
            float left = rect.xMin + (lane / 4f + padding) * rect.width;
            float right = rect.xMin + ((lane + 1) / 4f - padding) * rect.width;
            int index = vh.currentVertCount;
            vh.AddVert(new Vector3(left, rect.yMin + bottom * rect.height), tint, Vector2.zero);
            vh.AddVert(new Vector3(right, rect.yMin + bottom * rect.height), tint, Vector2.right);
            vh.AddVert(new Vector3(right, rect.yMin + top * rect.height), tint, Vector2.one);
            vh.AddVert(new Vector3(left, rect.yMin + top * rect.height), tint, Vector2.up);
            vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index + 2, index + 3, index);
        }
    }
}
