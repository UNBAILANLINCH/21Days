// 职责：所有 UI 面板的基类——三段生命周期 + 所在层 + 可选的过渡预设（默认淡入淡出）。
// 为什么新建：architecture.md 5.6 把它定成独立契约；面板的生命周期不能靠 Unity 的
//   Awake/OnEnable（那是同步的，开面板要 await 加载数据），必须另立一套异步钩子。

using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI
{
    /// <summary>
    /// UI 面板基类。预制体放 <c>Assets/_Project/Prefabs/UI/</c>，
    /// **Addressables 地址必须等于类名**（UIService 按 <c>typeof(T).Name</c> 找预制体）。
    /// <para>
    /// 三段生命周期：<see cref="OnOpenAsync"/>（绑数据、加监听）→ <see cref="OnRefresh"/>（数据变了重画）
    /// → <see cref="OnCloseAsync"/>（摘监听、释放）。不要在 Awake/OnEnable 里做这些——
    /// 面板被全屏面板盖住时会 SetActive(false)，OnEnable 会重复触发。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UIView : MonoBehaviour, IUIStackEntry
    {
        /// <summary>Slide 过渡时离开屏幕的偏移量（像素，参考分辨率下的锚点单位）。</summary>
        private const float SlideOffset = 40f;

        /// <summary>Scale 过渡的起 / 讫缩放比例。</summary>
        private const float ScaleHidden = 0.92f;

        private CanvasGroup canvasGroup;
        private RectTransform rectTransform;
        private MotionHandle transitionHandle;
        private Vector2 restAnchoredPosition;
        private bool restAnchoredPositionCaptured;

        [Tooltip("面板开关的过渡方式；默认淡入淡出。")]
        [SerializeField] private UITransition transition = UITransition.Fade;

        /// <summary>本面板挂在哪一层。子类必须给出。</summary>
        public abstract UILayer Layer { get; }

        /// <summary>
        /// 是不是全屏面板。全屏的 Panel 压栈时会把它下面的 Panel 隐藏掉（省一整层的绘制开销）。
        /// 默认「Panel 层就是全屏」——半透明的小面板、侧边栏要自己重写成 false。
        /// </summary>
        public virtual bool IsFullScreen => Layer == UILayer.Panel;

        /// <summary>
        /// 沉浸模式（<see cref="IHudVisibility.SetHudHidden"/>(true)）下是否仍显示。只对 Hud 层面板有意义；
        /// 默认 false（跟着隐藏），沉浸开关按钮这类「隐藏后还得点得到」的面板重写为 true。
        /// </summary>
        public virtual bool VisibleWhenHudHidden => false;

        [Tooltip("面板打开后默认选中的控件；键盘 / 手柄导航从它开始。可空。")]
        [SerializeField] private Selectable defaultSelected;

        /// <summary>
        /// 面板成为栈顶时 UIService 让 EventSystem 选中的控件（键盘 / 手柄导航的起点）。
        /// 可空：为空时打开后清空当前选中，避免选中落在被盖住的控件上；关掉上层面板回到本面板时也按它重新选中。
        /// </summary>
        public Selectable DefaultSelected => defaultSelected;

        /// <summary>
        /// 按 UI/Cancel（Esc / 手柄 B）时能不能被 <see cref="UICancelRouter"/> 关掉。
        /// 默认 Panel / Popup 层可关；标题、对白这类「关了就卡住流程」的面板重写为 false。
        /// </summary>
        public virtual bool CloseOnCancel => Layer == UILayer.Panel || Layer == UILayer.Popup;

        /// <summary>面板开关用哪种过渡预设，Inspector 上选。默认实现按这个值分派，见 <see cref="PlayOpenTransitionAsync"/>。</summary>
        protected UITransition Transition => transition;

        /// <summary>本面板的 CanvasGroup。预制体根节点上没有的话运行时补一个。</summary>
        protected CanvasGroup Group
        {
            get
            {
                // CanvasGroup 是 UnityEngine.Object，判空只用 == null（Unity 重载了 ==）。
                if (canvasGroup == null)
                {
                    canvasGroup = GetComponent<CanvasGroup>();
                    if (canvasGroup == null)
                    {
                        canvasGroup = gameObject.AddComponent<CanvasGroup>();
                    }
                }

                return canvasGroup;
            }
        }

        /// <summary>
        /// 面板根节点的 RectTransform。Slide / Scale 过渡要改位置和缩放；预制体根节点本来就该是
        /// RectTransform（开发手册 11.1 步骤 2），这里只是缓存。
        /// </summary>
        protected RectTransform Rect
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

        /// <summary>
        /// Slide 过渡的「原位」锚点。只在第一次用到时取一次当前 anchoredPosition 并固定住——
        /// 不固定的话反复开关会在动画中途取值，越开越偏。
        /// </summary>
        private Vector2 RestAnchoredPosition
        {
            get
            {
                if (!restAnchoredPositionCaptured)
                {
                    restAnchoredPosition = Rect.anchoredPosition;
                    restAnchoredPositionCaptured = true;
                }

                return restAnchoredPosition;
            }
        }

        /// <summary>
        /// 打开之前把 alpha 摆到过渡的起点：有动画就 0，没动画就 1。
        /// 少了这一步，OnOpenAsync 里一旦 await 跨帧，玩家会先看到一帧完整界面再被淡入动画抹回去。
        /// </summary>
        internal void PrepareForOpen(float seconds)
        {
            Group.alpha = seconds > 0f ? 0f : 1f;
        }

        /// <summary>打开时调用，早于淡入动画。<paramref name="arg"/> 是 OpenAsync 传进来的参数，可空。</summary>
        public virtual UniTask OnOpenAsync(object arg, CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>数据变了重画界面。由面板自己或持有它的状态调用，UIService 不主动调。</summary>
        public virtual void OnRefresh()
        {
        }

        /// <summary>关闭时调用，晚于淡出动画。摘事件监听、释放自己加载的句柄。</summary>
        public virtual UniTask OnCloseAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 给按钮挂点击监听（在 <see cref="OnOpenAsync"/> 里用）。先 Remove 再 Add：
        /// OpenAsync 对已开面板会再调一次 OnOpenAsync，这样不会叠两份监听。按钮为空时什么都不做。
        /// </summary>
        protected static void Hook(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(handler);
            button.onClick.AddListener(handler);
        }

        /// <summary>摘掉 <see cref="Hook"/> 挂的监听（在 <see cref="OnCloseAsync"/> 里成对调用）。按钮为空时什么都不做。</summary>
        protected static void Unhook(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(handler);
            }
        }

        /// <summary>
        /// 打开过渡。默认按 <see cref="Transition"/> 分派到 Fade / SlideUp / SlideDown / Scale 四种预设；
        /// <paramref name="seconds"/> 来自 <see cref="UIConfig.TransitionSeconds"/>，为 0 时直接置终值不等帧。
        /// <para>
        /// 声明成 protected internal：UIService（同程序集）要调它，玩法模块（别的程序集）要能重写它。
        /// 要加预设之外的花样（比如带弹性的曲线）就重写这个方法，不受枚举限制。
        /// </para>
        /// </summary>
        protected internal virtual UniTask PlayOpenTransitionAsync(float seconds, CancellationToken ct)
        {
            return RunTransitionAsync(true, seconds, ct);
        }

        /// <summary>关闭过渡，四种预设的收尾动作，见 <see cref="PlayOpenTransitionAsync"/>。</summary>
        protected internal virtual UniTask PlayCloseTransitionAsync(float seconds, CancellationToken ct)
        {
            return RunTransitionAsync(false, seconds, ct);
        }

        /// <summary>
        /// 按 <see cref="transition"/> 播开 / 关过渡。<paramref name="opening"/> 为 true 时从「隐藏态」播到
        /// 「显示态」，false 相反。同一时刻只允许一个过渡在跑——开到一半又被关掉时，老动画不掐断会在新动画
        /// 之后把状态又改回去。
        /// <para>
        /// Slide / Scale 要 alpha 和位置 / 缩放两段同时跑。这里选 <see cref="LSequence"/> 把两段拼成一条
        /// <see cref="MotionHandle"/>，没有选「两条 motion 并行 + <c>UniTask.WhenAll</c>」：后者要么多开一个
        /// 字段记第二条 handle（打断时两条谁先 Cancel、各自的 await 顺序都要单独处理），要么把 WhenAll 的结果
        /// 再包一层转 UniTask，徒增一层包装。LSequence 把子 motion 的驱动权收进一条虚拟时间轴，Cancel 顶层
        /// handle 时两段一起停（见包内 <c>MotionSequenceSource.OnCancel</c>），<c>transitionHandle</c> 这一个
        /// 字段就管得住，和原来只有 Fade 时的写法保持同一种形状。
        /// </para>
        /// </summary>
        private UniTask RunTransitionAsync(bool opening, float seconds, CancellationToken ct)
        {
            if (transitionHandle.IsActive())
            {
                transitionHandle.Cancel();
            }

            CanvasGroup group = Group;
            float alphaFrom = opening ? 0f : 1f;
            float alphaTo = opening ? 1f : 0f;

            bool useSlide = transition == UITransition.SlideUp || transition == UITransition.SlideDown;
            bool useScale = transition == UITransition.Scale;

            RectTransform rect = useSlide || useScale ? Rect : null;
            Vector2 positionFrom = default;
            Vector2 positionTo = default;
            Vector3 scaleFrom = default;
            Vector3 scaleTo = default;

            if (useSlide)
            {
                float offsetY = transition == UITransition.SlideUp ? -SlideOffset : SlideOffset;
                Vector2 rest = RestAnchoredPosition;
                Vector2 hidden = rest + new Vector2(0f, offsetY);
                positionFrom = opening ? hidden : rest;
                positionTo = opening ? rest : hidden;
            }
            else if (useScale)
            {
                var hidden = new Vector3(ScaleHidden, ScaleHidden, ScaleHidden);
                scaleFrom = opening ? hidden : Vector3.one;
                scaleTo = opening ? Vector3.one : hidden;
            }

            if (seconds <= 0f)
            {
                group.alpha = alphaTo;
                if (useSlide)
                {
                    rect.anchoredPosition = positionTo;
                }
                else if (useScale)
                {
                    rect.localScale = scaleTo;
                }

                return UniTask.CompletedTask;
            }

            group.alpha = alphaFrom;

            // UpdateIgnoreTimeScale：暂停菜单要在 timeScale = 0 时也能播过渡，不然开不出来 / 关不掉。
            MotionHandle alphaHandle = LMotion.Create(alphaFrom, alphaTo, seconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToAlpha(group);

            // AddTo(gameObject)：面板被直接销毁（没走 CloseAsync 那条正常收尾路径，比如场景卸载）时
            // 随之掐断，不指望只靠 OnDestroy 里的手动 Cancel——同一份 handle 双保险，谁先跑都行
            // （两边 Cancel 前都判过 IsActive，不会重复触发）。顶层 handle（不管是不是 LSequence 拼出来的）
            // 都能直接 AddTo，不需要分别挂子 motion。
            if (transition == UITransition.Fade)
            {
                transitionHandle = alphaHandle.AddTo(gameObject);
            }
            else if (useSlide)
            {
                rect.anchoredPosition = positionFrom;
                MotionHandle positionHandle = LMotion.Create(positionFrom, positionTo, seconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToAnchoredPosition(rect);

                transitionHandle = LSequence.Create()
                    .Join(alphaHandle)
                    .Join(positionHandle)
                    .Run(sequence => sequence.WithScheduler(MotionScheduler.UpdateIgnoreTimeScale))
                    .AddTo(gameObject);
            }
            else
            {
                rect.localScale = scaleFrom;
                MotionHandle scaleHandle = LMotion.Create(scaleFrom, scaleTo, seconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToLocalScale(rect);

                transitionHandle = LSequence.Create()
                    .Join(alphaHandle)
                    .Join(scaleHandle)
                    .Run(sequence => sequence.WithScheduler(MotionScheduler.UpdateIgnoreTimeScale))
                    .AddTo(gameObject);
            }

            // cancelAwaitOnMotionCanceled = false：上一个过渡被新过渡掐断时，
            // 老的 await 正常结束而不是抛 OperationCanceledException——「开到一半又关掉」是正常操作，不是错误。
            return transitionHandle.ToUniTask(CancelBehavior.Cancel, false, ct);
        }

        private void OnDestroy()
        {
            if (transitionHandle.IsActive())
            {
                transitionHandle.Cancel();
            }
        }
    }
}
