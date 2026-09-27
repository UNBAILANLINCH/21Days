// 职责：按钮按下 / 抬起（或指针移出）时的缩放反馈，纯表现，不碰点击逻辑。
// 为什么新建：roadmap.md D5 要按钮有按压反馈；UIView 是面板基类，管的是整块面板的开关过渡，
//   不该跟着管单个按钮的按压动画，只能新建一个可选挂载的 MonoBehaviour，哪个按钮要就挂哪个。
// 音效钩子：等 Assets/_Project/Audio/ 有按压音效资产后再接 AudioService，目前只做缩放反馈。

using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Core.UI
{
    /// <summary>
    /// 按钮按压反馈：按下把 <see cref="RectTransform.localScale"/> 缩到 <see cref="pressedScale"/>，
    /// 抬起或指针移出时弹回 1。挂在按钮物体上即可，不接管 <c>Button.onClick</c>。
    /// <para>
    /// 同物体上如果有 <see cref="Selectable"/> 且 <c>interactable == false</c>，按下不响应
    /// （抬起 / 移出仍然会把 scale 复位，避免按下后中途被禁用导致卡在缩小状态）。
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("按下时缩到的比例，1 表示不缩放。")]
        [Range(0.5f, 1f)]
        [SerializeField] private float pressedScale = 0.94f;

        [Tooltip("缩放动画的秒数，0 表示直接跳变不等帧。")]
        [Min(0f)]
        [SerializeField] private float seconds = 0.08f;

        private RectTransform rectTransform;
        private MotionHandle scaleHandle;

        private RectTransform Rect
        {
            get
            {
                if (rectTransform == null)
                {
                    rectTransform = GetComponent<RectTransform>();
                }

                return rectTransform;
            }
        }

        private void OnDisable()
        {
            CancelAndReset();
        }

        private void OnDestroy()
        {
            CancelAndReset();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable())
            {
                return;
            }

            AnimateTo(pressedScale);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            AnimateTo(1f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            AnimateTo(1f);
        }

        private bool IsInteractable()
        {
            // 现取不缓存：原来在 Awake() 里缓存过，但指针事件可能在 Awake 跑之前就到达
            // （脚本刚 AddComponent 就被立刻拿去用是常见的极端时序，尤其是测试代码），
            // 缓存值这时候还是 null，判断就形同虚设。GetComponent 不是每帧路径，直接现查更稳。
            // component == null：没有 Selectable 组件，不受它的 interactable 开关约束。
            var component = GetComponent<Selectable>();
            return component == null || component.interactable;
        }

        private void AnimateTo(float scale)
        {
            if (scaleHandle.IsActive())
            {
                scaleHandle.Cancel();
            }

            RectTransform rect = Rect;
            var target = new Vector3(scale, scale, scale);
            if (seconds <= 0f)
            {
                rect.localScale = target;
                return;
            }

            // UpdateIgnoreTimeScale：暂停菜单上的按钮在 timeScale = 0 时也得能按出反馈。
            // AddTo(gameObject)：物体被直接销毁（没走 OnDisable/OnPointerUp 那条正常收尾路径）时随之掐断，
            // 不指望只靠 OnDestroy 里的手动 Cancel——同一份 handle 双保险，谁先跑都行（Cancel 前都判过 IsActive）。
            scaleHandle = LMotion.Create(rect.localScale, target, seconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToLocalScale(rect)
                .AddTo(gameObject);
        }

        private void CancelAndReset()
        {
            if (scaleHandle.IsActive())
            {
                scaleHandle.Cancel();
            }

            if (rectTransform != null)
            {
                rectTransform.localScale = Vector3.one;
            }
        }
    }
}
