// 职责：把挂载对象的 RectTransform 收进屏幕安全区（避开刘海、圆角、手势条）。
// 为什么新建：project-root.md 要求「UI 用 Canvas Scaler 配安全区适配刘海与手势条」，
//   工程内没有任何适配组件；换算部分已经拆进 SafeAreaMath（纯函数、可测），这里只剩「什么时候重算」。

using UnityEngine;

namespace Game.Core.UI
{
    /// <summary>
    /// 安全区适配器。UIService 给每层 Canvas 建一个挂着它的内容根，面板都生在这个根下面。
    /// <para>
    /// 重算时机只有两个：<c>OnEnable</c>（第一次摆位）与 <c>OnRectTransformDimensionsChange</c>
    /// （分辨率、窗口尺寸、旋转屏变化时 Unity 自己会调）。**不在 Update 里轮询** ——
    /// 安全区一局游戏里最多变几次，每帧读 <c>Screen.safeArea</c> 是白花钱。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rectTransform;
        private Rect lastSafeArea;
        private int lastScreenWidth;
        private int lastScreenHeight;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            Apply(true);
        }

        // 屏幕尺寸 / 朝向变化时 Unity 会调这个回调，比自己在 Update 里比较 Screen.width 省。
        private void OnRectTransformDimensionsChange()
        {
            Apply(false);
        }

        /// <summary>
        /// 立刻按当前 <c>Screen.safeArea</c> 重摆一次。外部改了分辨率又不想等回调时可以手动调。
        /// </summary>
        public void Refresh()
        {
            Apply(true);
        }

        private void Apply(bool force)
        {
            if (rectTransform == null)
            {
                rectTransform = GetComponent<RectTransform>();
                if (rectTransform == null)
                {
                    return;
                }
            }

            Rect safeArea = Screen.safeArea;
            int width = Screen.width;
            int height = Screen.height;
            if (!force && safeArea == lastSafeArea && width == lastScreenWidth && height == lastScreenHeight)
            {
                return;
            }

            lastSafeArea = safeArea;
            lastScreenWidth = width;
            lastScreenHeight = height;

            SafeAreaMath.ToAnchors(safeArea, width, height, out Vector2 anchorMin, out Vector2 anchorMax);
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }
    }
}
