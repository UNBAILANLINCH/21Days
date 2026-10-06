// 职责：线段与简单形状的纯数学求交。没有物理查询、没有场景 API、没有随机数与时间，
// 只吃 Vector2 与 GameMath，因此可以进确定性内核，也可以在 EditMode 里用纯逻辑测。
//
// 为什么新建：Core 只有 `GameMath`（转发 Mathf / Vector2 的基础运算），没有任何几何求交；
// UnityEngine 自带的 `Physics2D.Linecast` / `Collider2D` 是物理查询，跨平台不保证确定性、 // lint-ok: 注释里说明为何禁用物理查询，正文不含任何物理调用
// 也进不了纯逻辑测试，本内核明令不许用（project-lint 的 `gameplay-physics-query` 同样盯着这一条）。
//
// 出处：`docs/design/features-spotlight/03_潜行与暗杀.md:129-136`（R24–R28 掩体与地形）。
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>
    /// 遮挡求交：轴对齐矩形用「线段 vs 盒」的板层（slab）裁剪，圆用「圆心到线段的最近距离 &lt; 半径」。
    /// <para>
    /// 约定：贴着边界也算挡住（线段恰好擦过一个矩形的角、恰好与圆相切都返回 true）。
    /// 理由是玩法上宁可多算挡住：擦着掩体边缘还能被怪物一眼看见，玩家会认为掩体是坏的。
    /// 零长度线段（起点终点同一个点）永远不挡——那是一次「自我遮蔽」，没有意义。
    /// </para>
    /// </summary>
    public static class StealthGeometry
    {
        /// <summary>线段与遮挡体是否相交。</summary>
        public static bool SegmentIntersectsOccluder(Vector2 from, Vector2 to, in StealthOccluder occluder)
        {
            switch (occluder.Kind)
            {
                case StealthOccluderKind.Rectangle:
                    return SegmentIntersectsRectangle(from, to, occluder);
                case StealthOccluderKind.Circle:
                    return SegmentIntersectsCircle(from, to, occluder.Center, occluder.Radius);
                default:
                    return false;
            }
        }

        /// <summary>线段与轴对齐矩形是否相交（含贴边）。矩形由「中心 + 半宽半高」给出。</summary>
        public static bool SegmentIntersectsRectangle(Vector2 from, Vector2 to, Vector2 center, Vector2 halfSize)
        {
            if (halfSize.x < 0f || halfSize.y < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(halfSize), "矩形半宽半高不可为负");
            }

            Vector2 direction = to - from;
            if (GameMath.SqrMagnitude(direction) <= 0f)
            {
                return false;
            }

            float minX = center.x - halfSize.x;
            float maxX = center.x + halfSize.x;
            float minY = center.y - halfSize.y;
            float maxY = center.y + halfSize.y;

            // 板层裁剪：对 x、y 两轴各截一次参数区间，区间空了就是不相交。
            float enter = 0f;
            float exit = 1f;
            return ClipAxis(from.x, direction.x, minX, maxX, ref enter, ref exit)
                && ClipAxis(from.y, direction.y, minY, maxY, ref enter, ref exit);
        }

        private static bool SegmentIntersectsRectangle(Vector2 from, Vector2 to, in StealthOccluder occluder)
        {
            Vector2 center = occluder.Center;
            Vector2 halfSize = occluder.HalfSize;
            return SegmentIntersectsRectangle(from, to, center, halfSize);
        }

        /// <summary>
        /// 线段与圆是否相交（含相切）。判据是圆心到线段的最近距离 &lt;= 半径，
        /// 最近点取「起点到终点参数 t 钳制到 [0, 1]」的那一点，线段退化成点时返回到该点的距离。
        /// </summary>
        public static bool SegmentIntersectsCircle(Vector2 from, Vector2 to, Vector2 center, float radius)
        {
            if (radius < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(radius), "圆半径不可为负");
            }

            Vector2 direction = to - from;
            float lengthSquared = GameMath.SqrMagnitude(direction);
            float t;
            if (lengthSquared <= 0f)
            {
                // 退化成点：只判点是否落在圆内。
                return GameMath.SqrMagnitude(center - from) <= radius * radius;
            }

            t = GameMath.Clamp01(GameMath.Dot(center - from, direction) / lengthSquared);
            Vector2 closest = from + direction * t;
            return GameMath.SqrMagnitude(center - closest) <= radius * radius;
        }

        /// <summary>
        /// 点到线段的最近距离。遮挡体积木之外也用得上（例如将来给「背后近距察觉」做遮挡判定）。
        /// </summary>
        public static float DistancePointToSegment(Vector2 point, Vector2 from, Vector2 to)
        {
            Vector2 direction = to - from;
            float lengthSquared = GameMath.SqrMagnitude(direction);
            if (lengthSquared <= 0f)
            {
                return GameMath.Distance(point, from);
            }

            float t = GameMath.Clamp01(GameMath.Dot(point - from, direction) / lengthSquared);
            return GameMath.Distance(point, from + direction * t);
        }

        // 对单轴裁剪参数区间；返回 false 表示区间已空（线段整段落在板外）。
        private static bool ClipAxis(float origin, float direction, float min, float max, ref float enter, ref float exit)
        {
            if (GameMath.Abs(direction) <= 0f)
            {
                // 该轴不变化：起点必须已经落在板内，否则整段在板外。
                return origin >= min && origin <= max;
            }

            float inverse = 1f / direction;
            float t0 = (min - origin) * inverse;
            float t1 = (max - origin) * inverse;
            if (t0 > t1)
            {
                float swap = t0;
                t0 = t1;
                t1 = swap;
            }

            if (t0 > enter)
            {
                enter = t0;
            }

            if (t1 < exit)
            {
                exit = t1;
            }

            return enter <= exit;
        }
    }
}
