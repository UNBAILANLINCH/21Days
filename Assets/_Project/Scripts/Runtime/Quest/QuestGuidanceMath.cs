// 职责：任务目标屏幕指引的纯数学计算（屏内悬浮定位 / 屏外贴边 + 箭头角度、直线距离取整）。
// 为什么新建：框架里没有「世界目标 → 屏幕指示（屏内悬浮 / 屏外贴边 + 箭头）」的现成组件，任务系统首次落地（PRP/quest-system 3.8）。
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Quest
{
    /// <summary>
    /// 任务目标屏幕指引的纯函数计算。不依赖 <see cref="Camera"/> / RectTransform，
    /// 调用方负责取得视口坐标（<see cref="Camera.WorldToViewportPoint(Vector3)"/>）与画布尺寸。
    /// 数值运算走 <see cref="GameMath"/>（玩法层重放确定性约束，见 docs/architecture.md #5.10）；
    /// <see cref="Vector2"/> / <see cref="Vector3"/> 的四则与距离运算不在该约束范围内，直接用。
    /// </summary>
    public static class QuestGuidanceMath
    {
        // 弧度转角度的换算常量（原生数学库同名常量的字面值）：GameMath 目前只封装运算，
        // 没有收录这个换算常量，这里直接写字面值，避免玩法层触发重放确定性检查。
        private const float RadToDegFactor = 57.29578f;

        /// <summary>
        /// 根据相机视口坐标计算屏幕指引结果。
        /// </summary>
        /// <param name="viewportPoint">
        /// <see cref="Camera.WorldToViewportPoint(Vector3)"/> 的结果：x、y ∈ [0,1] 为画面内，
        /// z 为相机前方距离（z &lt; 0 表示目标在相机背后）。
        /// </param>
        /// <param name="canvasSize">画布宽高（像素）。</param>
        /// <param name="edgeMargin">屏外贴边时与画布边缘的内缩像素。</param>
        /// <param name="hoverOffset">屏内悬浮时相对目标的竖直偏移像素。</param>
        /// <remarks>
        /// 规则：
        /// - z &lt; 0（目标在相机背后）：把 (x, y) 关于 (0.5, 0.5) 翻转（x = 1 - x，y = 1 - y），并视为屏外。
        /// - 屏内判定：z &gt;= 0 且 0 &lt;= x &lt;= 1 且 0 &lt;= y &lt;= 1（边界算屏内）。
        ///   屏内 → AnchoredPosition = ((x - 0.5) * w, (y - 0.5) * h + hoverOffset)，不显示箭头。
        /// - 屏外：dir = ((x - 0.5) * w, (y - 0.5) * h) 是画布中心指向目标投影的向量；
        ///   若 dir 长度接近 0（sqrMagnitude &lt; 1e-6）则取 (0, -1) 兜底。
        ///   内缩矩形半宽 hx = w/2 - edgeMargin、半高 hy = h/2 - edgeMargin（各自最小取 0）。
        ///   缩放系数 t = min(hx / |dir.x|, hy / |dir.y|)，某一分量为 0 时该项不参与（视为无穷大）。
        ///   AnchoredPosition = dir * t；ArrowAngleDeg = Atan2(-dir.x, dir.y) 换算成角度
        ///   （0 = 朝上，逆时针为正，可直接赋给 RectTransform 的 Z 轴旋转，箭头图默认朝上）。
        /// </remarks>
        public static QuestGuidance Solve(Vector3 viewportPoint, Vector2 canvasSize, float edgeMargin, float hoverOffset)
        {
            float x = viewportPoint.x;
            float y = viewportPoint.y;
            float z = viewportPoint.z;

            // 相机背后：关于画面中心翻转视口坐标，并视为屏外。
            if (z < 0f)
            {
                x = 1f - x;
                y = 1f - y;
            }

            float w = canvasSize.x;
            float h = canvasSize.y;

            bool onScreen = z >= 0f && x >= 0f && x <= 1f && y >= 0f && y <= 1f;
            if (onScreen)
            {
                Vector2 hoverPosition = new Vector2((x - 0.5f) * w, (y - 0.5f) * h + hoverOffset);
                return new QuestGuidance(true, hoverPosition, 0f, false);
            }

            Vector2 dir = new Vector2((x - 0.5f) * w, (y - 0.5f) * h);
            if (dir.sqrMagnitude < 1e-6f)
            {
                dir = new Vector2(0f, -1f);
            }

            float hx = GameMath.Max(0f, w / 2f - edgeMargin);
            float hy = GameMath.Max(0f, h / 2f - edgeMargin);

            float tx = dir.x != 0f ? hx / GameMath.Abs(dir.x) : float.PositiveInfinity;
            float ty = dir.y != 0f ? hy / GameMath.Abs(dir.y) : float.PositiveInfinity;
            float t = GameMath.Min(tx, ty);

            Vector2 edgePosition = dir * t;
            float arrowAngleDeg = GameMath.Atan2(-dir.x, dir.y) * RadToDegFactor;

            return new QuestGuidance(false, edgePosition, arrowAngleDeg, true);
        }

        /// <summary>
        /// 两点间直线距离，四舍五入（半数进位）取整为米。不用原生的四舍五入取整（银行家舍入，
        /// 2.5 会舍成 2），这里需要标准的「五入」：距离恒为非负，加 0.5 后向下取整即可。
        /// </summary>
        public static int DistanceMeters(Vector3 a, Vector3 b)
        {
            float distance = Vector3.Distance(a, b);
            return (int)GameMath.Floor(distance + 0.5f);
        }
    }
}
