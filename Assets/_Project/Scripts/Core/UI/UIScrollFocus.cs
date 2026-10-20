// 职责：列表或网格项被导航选中时滚入视口，不拦截滚轮或拖动。
// 从任务列表提升到共用 UI，供任务和背包复用；父面板收不到子项 Select，故只实现 ISelectHandler。
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Core.UI
{
    public sealed class UIScrollFocus : MonoBehaviour, ISelectHandler
    {
        private ScrollRect scroll;
        private RectTransform row;
        private readonly Vector3[] corners = new Vector3[4];

        public void Bind(ScrollRect target)
        {
            scroll = target;
            row = (RectTransform)transform;
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (scroll == null || row == null) return;
            RectTransform content = scroll.content;
            float offset = content.anchoredPosition.y;
            float height = scroll.viewport.rect.height;
            row.GetWorldCorners(corners);
            float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
            for (int i = 0; i < corners.Length; i++)
            {
                float y = scroll.viewport.InverseTransformPoint(corners[i]).y;
                if (y > top) top = y;
                if (y < bottom) bottom = y;
            }
            if (top > scroll.viewport.rect.yMax) offset -= top - scroll.viewport.rect.yMax;
            else if (bottom < scroll.viewport.rect.yMin) offset += scroll.viewport.rect.yMin - bottom;
            offset = Mathf.Clamp(offset, 0f, Mathf.Max(0f, content.rect.height - height)); // lint-ok: 屏幕空间滚动裁剪，不参与确定性玩法模拟
            scroll.StopMovement();
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, offset);
        }
    }
}
