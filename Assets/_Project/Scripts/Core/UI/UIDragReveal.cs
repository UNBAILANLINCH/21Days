// 职责：UI 下拉手柄、张手/握手光标及操作区显隐。
// 复用 UIPointerHold 不行：它不计算拖拽距离；扩展它会改变现有按住检测契约，因此独立提供拖拽表现。
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Game.Core.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIDragReveal : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private CanvasGroup reveal;
        [SerializeField, Min(1f)] private float pullDistance = 110f;
        [SerializeField] private Texture2D grabCursor;
        [SerializeField] private Texture2D heldCursor;
        [SerializeField] private Vector2 cursorHotspot;

        private Vector2 restPosition;
        private Vector2 pressPosition;
        private float pressPull;
        private float pull;
        private bool initialized;
        private bool available;
        private bool hovered;
        private bool held;
        private bool ownsCursor;
        private static UIDragReveal cursorOwner;
        private int pointerId;

        public bool IsRevealed => available && pull >= pullDistance;
        public bool IsHeld => held;
        public event System.Action OnRevealed;

        public void SetAvailable(bool value)
        {
            if (content == null || reveal == null) throw new System.InvalidOperationException("UIDragReveal 未接内容或操作区");
            if (!initialized) { restPosition = content.anchoredPosition; initialized = true; }
            available = value;
            held = hovered = false;
            SetPull(0f);
            RestoreCursor();
        }

        public void OnPointerEnter(PointerEventData data)
        {
            hovered = true;
            if (available && IsMouse(data)) ShowCursor(held);
        }

        public void OnPointerExit(PointerEventData data)
        {
            hovered = false;
            if (!held) RestoreCursor();
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (!available || held || data.button != PointerEventData.InputButton.Left) return;
            if (!LocalPoint(data, out pressPosition)) return;
            pointerId = data.pointerId;
            pressPull = pull;
            held = true;
            if (IsMouse(data)) ShowCursor(true);
        }

        public void OnBeginDrag(PointerEventData data) { }

        // Input System 单独查找点击接收者；下部点击必须在手柄终止，不能冒泡到读档按钮。
        public void OnPointerClick(PointerEventData data) { }

        public void OnDrag(PointerEventData data)
        {
            if (!held || data.pointerId != pointerId || !LocalPoint(data, out Vector2 point)) return;
            SetPull(pressPull + pressPosition.y - point.y);
        }

        public void OnPointerUp(PointerEventData data) => Finish(data);
        public void OnEndDrag(PointerEventData data) => Finish(data);

        private void Finish(PointerEventData data)
        {
            if (!held || data.pointerId != pointerId || data.button != PointerEventData.InputButton.Left) return;
            bool newlyRevealed = pressPull < pullDistance && pull >= pullDistance * 0.5f;
            held = false;
            SetPull(pull >= pullDistance * 0.5f ? pullDistance : 0f);
            if (hovered && IsMouse(data)) ShowCursor(false);
            else RestoreCursor();
            if (newlyRevealed) OnRevealed?.Invoke();
        }

        private bool LocalPoint(PointerEventData data, out Vector2 point) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)content.parent,
                data.position, data.pressEventCamera, out point);

        private static bool IsMouse(PointerEventData data) => data is ExtendedPointerEventData extended
            ? extended.pointerType == UIPointerType.MouseOrPen : data.pointerId < 0;

        private void SetPull(float distance)
        {
            pull = Mathf.Clamp(distance, 0f, pullDistance);
            content.anchoredPosition = restPosition + Vector2.down * pull;
            reveal.alpha = pull / pullDistance;
            reveal.interactable = reveal.blocksRaycasts = IsRevealed && !held;
        }

        private void ShowCursor(bool grabbing)
        {
            Texture2D texture = grabbing ? heldCursor : grabCursor;
            if (texture == null) return;
            if (cursorOwner != null && cursorOwner != this && cursorOwner.held) return;
            if (cursorOwner != null) cursorOwner.ownsCursor = false;
            cursorOwner = this;
            Cursor.SetCursor(texture, cursorHotspot, CursorMode.Auto);
            ownsCursor = true;
        }

        private void RestoreCursor()
        {
            if (!ownsCursor || cursorOwner != this) return;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            ownsCursor = false;
            cursorOwner = null;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ResetInteraction();
        }

        private void OnDisable() => ResetInteraction();

        private void ResetInteraction()
        {
            held = hovered = false;
            if (initialized && content != null && reveal != null) SetPull(0f);
            RestoreCursor();
        }
    }
}
