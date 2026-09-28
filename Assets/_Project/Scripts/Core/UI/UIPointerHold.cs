// 职责：按住检测——指针（鼠标左键 / 触点）在本物体上按下之后、抬起或移出之前，IsHeld 为 true；供「按住某个控件等同长按某个键」的面板读。
// 为什么新建（复用 → 扩展 → 新增）：
//   1. 复用不行：Button 只有点击（按下再抬起才触发一次），读不到「正按着」；UIButtonFeedback 只做缩放反馈、不对外暴露状态。
//   2. 扩展不行：给 UIButtonFeedback 加状态会让纯表现组件承担输入语义，按住区域也不一定要缩放；
//      写进某个玩法面板则每个要「按住」的面板各抄一份指针回调。
//   所以在 Core/UI 新建一个只报告按住状态的通用小组件：不计时、不抛事件，计时与「按满算数」由读它的一方决定。
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Core.UI
{
    /// <summary>
    /// 指针按住检测。挂在能接射线的 UI 物体上（同物体要有 raycastTarget 打开的 Graphic，透明 Image 即可）。
    /// 左键 / 触点在本物体上按下 → <see cref="IsHeld"/> 为 true；抬起、指针移出、物体停用都复位为 false。
    /// 右键 / 中键不算。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIPointerHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>当前是否被按住。</summary>
        public bool IsHeld { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            IsHeld = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            IsHeld = false;
        }

        /// <summary>按着移出本物体即算松手：移回来不会自动恢复，要重新按下。</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            IsHeld = false;
        }

        // 面板关闭 / 物体被停用时收不到抬起事件，这里复位，免得下次打开还当成按着。
        private void OnDisable()
        {
            IsHeld = false;
        }
    }
}
