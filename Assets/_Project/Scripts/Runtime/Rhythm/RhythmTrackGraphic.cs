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
        private bool prompt;
        private float holdProgress;
        private bool holding;
        public void Configure(bool background)
        { this.background = background; raycastTarget = false; SetVerticesDirty(); }
        public void Show(int lane, float head, float tail, bool hold, Color tint)
        {
            prompt = false;
            this.lane = lane; this.head = head; this.tail = tail; this.hold = hold; color = tint; SetVerticesDirty();
        }
        // 外圈闭合至目标圈只表达歌曲时间；是否命中仍由规则层决定。
        public void ShowPrompt(float remaining, bool holding, float progress, Color tint)
        {
            prompt = true; head = remaining; this.holding = holding;
            holdProgress = GameMath.Clamp01(progress); color = tint; SetVerticesDirty();
        }
        public static float PromptRadius(float remaining) => 0.23f + 0.22f * GameMath.Clamp01(remaining);
        // 不钳制头部：经过判定线后仍保持匀速，不粘在线上；长条超出轨道的部分只裁切显示。
        public static float PositionAt(double targetSeconds, double songSeconds, float visualOffsetMs, float approachSeconds)
            => (float)((targetSeconds - songSeconds + visualOffsetMs / 1000d) / approachSeconds);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            if (prompt)
            {
                if (rect.height <= 0 || rect.width <= 0) return;
                float size = GameMath.Min(rect.width, rect.height);
                var target = color; target.a *= 0.45f;
                Ring(vh, rect.center, size * 0.23f, size * 0.015f, 1, target);
                Ring(vh, rect.center, size * (holding ? 0.3f : PromptRadius(head)), size * 0.025f,
                    holding ? holdProgress : 1, color);
                return;
            }
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
        private static void Ring(VertexHelper vh, Vector2 center, float radius, float thickness, float progress, Color tint)
        {
            const int segments = 64;
            const float fullTurn = 6.28318530718f;
            for (int i = 0; i < segments && i / (float)segments < progress; i++)
            {
                float a = (0.25f - i / (float)segments) * fullTurn;
                float b = (0.25f - GameMath.Min((i + 1) / (float)segments, progress)) * fullTurn;
                var first = new Vector2(GameMath.Cos(a), GameMath.Sin(a));
                var second = new Vector2(GameMath.Cos(b), GameMath.Sin(b));
                int index = vh.currentVertCount;
                vh.AddVert(center + first * (radius - thickness), tint, Vector2.zero);
                vh.AddVert(center + first * radius, tint, Vector2.zero);
                vh.AddVert(center + second * radius, tint, Vector2.zero);
                vh.AddVert(center + second * (radius - thickness), tint, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index + 2, index + 3, index);
            }
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
