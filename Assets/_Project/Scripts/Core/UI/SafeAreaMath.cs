// 职责：把屏幕安全区（像素矩形）换算成 RectTransform 的 anchorMin / anchorMax（0～1 归一化）。
// 为什么新建：这段换算是刘海屏适配唯一会算错的地方（除零、越界、竖屏横屏），必须能 EditMode 测；
//   写在 SafeAreaFitter（MonoBehaviour）里就只能靠真机验证。纯函数单独一个文件，职责清楚。

using UnityEngine;

namespace Game.Core.UI
{
    /// <summary>
    /// 安全区换算。纯函数，不读 <c>Screen</c>、不碰任何 Unity 对象——屏幕尺寸由调用方传进来。
    /// </summary>
    public static class SafeAreaMath
    {
        /// <summary>
        /// 把安全区矩形换算成锚点。
        /// <para>
        /// 屏幕宽高任一为 0（编辑器刚启动、窗口最小化时会出现）时返回全屏 (0,0)-(1,1)：
        /// 除零会得到 NaN，NaN 赋给 anchor 会让整个 RectTransform 消失，比不适配严重得多。
        /// </para>
        /// 结果一律夹到 0～1 并保证 min ≤ max。
        /// </summary>
        /// <param name="safeArea">安全区，像素坐标，原点在屏幕左下（<c>Screen.safeArea</c> 的语义）。</param>
        /// <param name="screenWidth">屏幕宽，像素。</param>
        /// <param name="screenHeight">屏幕高，像素。</param>
        /// <param name="anchorMin">输出：左下锚点。</param>
        /// <param name="anchorMax">输出：右上锚点。</param>
        public static void ToAnchors(Rect safeArea, int screenWidth, int screenHeight,
            out Vector2 anchorMin, out Vector2 anchorMax)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                return;
            }

            float minX = Mathf.Clamp01(safeArea.xMin / screenWidth);
            float minY = Mathf.Clamp01(safeArea.yMin / screenHeight);
            float maxX = Mathf.Clamp01(safeArea.xMax / screenWidth);
            float maxY = Mathf.Clamp01(safeArea.yMax / screenHeight);

            anchorMin = new Vector2(Mathf.Min(minX, maxX), Mathf.Min(minY, maxY));
            anchorMax = new Vector2(Mathf.Max(minX, maxX), Mathf.Max(minY, maxY));
        }
    }
}
