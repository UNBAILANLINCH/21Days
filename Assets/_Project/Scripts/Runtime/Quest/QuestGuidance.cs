// 职责：任务系统屏幕指引结果的只读数据载体（屏内返回投影点 / 屏外贴边 + 箭头角度；HUD 屏内不读这份结果，改用世界空间头顶标记）。
// 为什么新建：框架里没有「世界目标 → 屏幕指示（屏内投影点 / 屏外贴边 + 箭头）」的现成组件，任务系统首次落地（PRP/quest-system 3.8）。
using UnityEngine;

namespace Game.Quest
{
    /// <summary>
    /// 任务目标屏幕指引的计算结果：是否在屏幕内、UI 锚点坐标（以画布中心为原点）、
    /// 屏外箭头角度（0 = 朝上，逆时针为正）以及是否需要显示箭头。
    /// </summary>
    public readonly struct QuestGuidance
    {
        public QuestGuidance(bool onScreen, Vector2 anchoredPosition, float arrowAngleDeg, bool showArrow)
        {
            OnScreen = onScreen;
            AnchoredPosition = anchoredPosition;
            ArrowAngleDeg = arrowAngleDeg;
            ShowArrow = showArrow;
        }

        /// <summary>目标是否在屏幕可视范围内（含边界）。</summary>
        public bool OnScreen { get; }

        /// <summary>UI 锚点坐标，以画布中心为原点，单位像素。</summary>
        public Vector2 AnchoredPosition { get; }

        /// <summary>屏外箭头角度，0 = 朝上，逆时针为正，可直接赋给 RectTransform 的 Z 轴旋转。</summary>
        public float ArrowAngleDeg { get; }

        /// <summary>是否需要显示屏外箭头。</summary>
        public bool ShowArrow { get; }
    }
}
