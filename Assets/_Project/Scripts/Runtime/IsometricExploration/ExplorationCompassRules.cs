// 职责：万向标的纯摆位规则——屏内兴趣点不画，屏外 / 身后的贴屏幕边并给出箭头角度；
//   同边多个标记按最小间距错开，避免文字互相压住（PRP/exploration-whitebox 波 8）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用：贴边与翻转算法直接复用 QuestGuidanceMath.Solve，不重写。
//   2. 扩展不行：QuestGuidanceMath 属于 Quest 模块，「屏内不画」是万向标的规则（任务指引屏内要画头顶标记），
//      塞进去会让任务模块认识探索 HUD。这里只包一层判定，EditMode 可测。
using System;
using System.Collections.Generic;
using Game.Core.Simulation;
using Game.Quest;
using UnityEngine;

namespace Game.IsometricExploration
{
    /// <summary>万向标摆位纯函数。调用方负责 <see cref="Camera.WorldToViewportPoint(Vector3)"/> 与画布尺寸。</summary>
    public static class ExplorationCompassRules
    {
        /// <summary>一个万向标算好的最终摆位：锚点位置与箭头角度，对应 <see cref="TryPlace"/> 的两个 out 参数。</summary>
        public readonly struct CompassPlacement
        {
            public CompassPlacement(Vector2 anchoredPosition, float angleDeg)
            {
                AnchoredPosition = anchoredPosition;
                AngleDeg = angleDeg;
            }

            /// <summary>相对画布中心的锚点位置。</summary>
            public Vector2 AnchoredPosition { get; }

            /// <summary>箭头 Z 轴旋转角度。</summary>
            public float AngleDeg { get; }
        }

        private enum CompassEdge { Left, Right, Top, Bottom }
        /// <summary>
        /// 计算一个兴趣点的万向标位置。
        /// </summary>
        /// <param name="viewportPoint">视口坐标（z &lt; 0 表示在相机背后）。</param>
        /// <param name="canvasSize">画布宽高。</param>
        /// <param name="edgeMargin">贴边内缩像素。</param>
        /// <param name="anchoredPosition">相对画布中心的锚点位置（标记锚点须居中）。</param>
        /// <param name="angleDeg">箭头 Z 轴旋转（0 = 朝上，逆时针为正）。</param>
        /// <returns>屏内（0≤x,y≤1 且 z&gt;0）返回 false，不画；屏外或身后返回 true。</returns>
        public static bool TryPlace(Vector3 viewportPoint, Vector2 canvasSize, float edgeMargin,
            out Vector2 anchoredPosition, out float angleDeg)
        {
            bool onScreen = viewportPoint.z > 0f
                            && viewportPoint.x >= 0f && viewportPoint.x <= 1f
                            && viewportPoint.y >= 0f && viewportPoint.y <= 1f;
            if (onScreen)
            {
                anchoredPosition = Vector2.zero;
                angleDeg = 0f;
                return false;
            }

            // z == 0 恰在相机平面上：按身后处理，交给 Solve 的翻转逻辑（Solve 以 z < 0 判身后）。
            Vector3 point = viewportPoint.z == 0f
                ? new Vector3(viewportPoint.x, viewportPoint.y, -1f)
                : viewportPoint;
            QuestGuidance guidance = QuestGuidanceMath.Solve(point, canvasSize, edgeMargin);
            anchoredPosition = guidance.AnchoredPosition;
            angleDeg = guidance.ArrowAngleDeg;
            return true;
        }

        /// <summary>
        /// 把同一条边上间距过近的万向标沿边错开，原地调整 <paramref name="placements"/> 的
        /// <see cref="CompassPlacement.AnchoredPosition"/>（<see cref="CompassPlacement.AngleDeg"/> 不变）。
        /// </summary>
        /// <param name="placements">已经过 <see cref="TryPlace"/> 摆位的标记列表，调用方复用的预分配 List。</param>
        /// <param name="canvasSize">画布宽高，须与摆位时一致。</param>
        /// <param name="edgeMargin">与摆位时相同的贴边内缩像素，用来算出每条边可用的坐标范围与判定贴的是哪条边。</param>
        /// <param name="minSpacing">同边相邻标记的最小间距（沿边坐标，像素）；≤ 0 视为不需要错开。</param>
        /// <remarks>
        /// 判边：分别算 |x| 到 hx=canvasSize.x/2-edgeMargin、|y| 到 hy=canvasSize.y/2-edgeMargin 的距离，
        /// 更接近的那一侧就是贴的边（左右边按 y 坐标错开，上下边按 x 坐标错开）——<see cref="TryPlace"/> 摆位时
        /// 两个方向只有一个会真正贴到内缩边界，另一个方向的坐标由方向向量决定，不会同时贴两条边。
        /// 同边内按坐标从小到大做「最小间距」正向推挤；若末端推出了边界范围，整体从末端往回夹紧，
        /// 再检查起始端是否被夹出另一侧边界，需要则反向再夹一次——两端始终钳制在 [-h, h] 内，
        /// 挤不下时宁可压缩间距也不越界。栈上分配下标与坐标缓冲区（<see cref="Span{T}"/>），不产生堆分配。
        /// </remarks>
        public static void SpreadAlongEdges(List<CompassPlacement> placements, Vector2 canvasSize, float edgeMargin, float minSpacing)
        {
            if (placements == null || minSpacing <= 0f) return;
            int count = placements.Count;
            if (count < 2) return;

            float halfWidth = GameMath.Max(0f, canvasSize.x / 2f - edgeMargin);
            float halfHeight = GameMath.Max(0f, canvasSize.y / 2f - edgeMargin);

            Span<int> indices = count <= 64 ? stackalloc int[count] : new int[count];
            SpreadEdge(placements, indices, count, CompassEdge.Left, halfWidth, halfHeight, minSpacing);
            SpreadEdge(placements, indices, count, CompassEdge.Right, halfWidth, halfHeight, minSpacing);
            SpreadEdge(placements, indices, count, CompassEdge.Top, halfWidth, halfHeight, minSpacing);
            SpreadEdge(placements, indices, count, CompassEdge.Bottom, halfWidth, halfHeight, minSpacing);
        }

        private static CompassEdge ClassifyEdge(Vector2 position, float halfWidth, float halfHeight)
        {
            float distX = GameMath.Abs(GameMath.Abs(position.x) - halfWidth);
            float distY = GameMath.Abs(GameMath.Abs(position.y) - halfHeight);
            if (distX <= distY)
            {
                return position.x >= 0f ? CompassEdge.Right : CompassEdge.Left;
            }
            return position.y >= 0f ? CompassEdge.Top : CompassEdge.Bottom;
        }

        // 收集落在 edge 上的下标（复用调用方传入的 indices 缓冲区）、按沿边坐标插入排序，
        // 做最小间距正向推挤 + 越界整体回退 + 反向夹紧，结果写回 placements。
        private static void SpreadEdge(List<CompassPlacement> placements, Span<int> indices, int count,
            CompassEdge edge, float halfWidth, float halfHeight, float minSpacing)
        {
            bool vertical = edge == CompassEdge.Left || edge == CompassEdge.Right;
            float half = vertical ? halfHeight : halfWidth;

            int n = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 pos = placements[i].AnchoredPosition;
                if (ClassifyEdge(pos, halfWidth, halfHeight) != edge) continue;

                float coord = vertical ? pos.y : pos.x;
                int insertAt = n;
                while (insertAt > 0)
                {
                    Vector2 prevPos = placements[indices[insertAt - 1]].AnchoredPosition;
                    float prevCoord = vertical ? prevPos.y : prevPos.x;
                    if (prevCoord <= coord) break;
                    indices[insertAt] = indices[insertAt - 1];
                    insertAt--;
                }
                indices[insertAt] = i;
                n++;
            }

            if (n < 2) return;

            Span<float> coords = n <= 64 ? stackalloc float[n] : new float[n];
            for (int k = 0; k < n; k++)
            {
                Vector2 pos = placements[indices[k]].AnchoredPosition;
                coords[k] = vertical ? pos.y : pos.x;
            }

            for (int k = 1; k < n; k++)
            {
                float min = coords[k - 1] + minSpacing;
                if (coords[k] < min) coords[k] = min;
            }

            if (coords[n - 1] > half)
            {
                coords[n - 1] = half;
                for (int k = n - 2; k >= 0; k--)
                {
                    float max = coords[k + 1] - minSpacing;
                    if (coords[k] > max) coords[k] = max;
                }
            }

            if (coords[0] < -half)
            {
                coords[0] = -half;
                for (int k = 1; k < n; k++)
                {
                    float min = coords[k - 1] + minSpacing;
                    if (coords[k] < min) coords[k] = min;
                }
            }

            for (int k = 0; k < n; k++)
            {
                int idx = indices[k];
                Vector2 pos = placements[idx].AnchoredPosition;
                Vector2 newPos = vertical ? new Vector2(pos.x, coords[k]) : new Vector2(coords[k], pos.y);
                placements[idx] = new CompassPlacement(newPos, placements[idx].AngleDeg);
            }
        }
    }
}
