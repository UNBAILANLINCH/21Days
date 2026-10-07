// 职责：加载黑幕的表现——全屏黑底淡入淡出（盖住时挡点击）、盖久了在右下角安全区内淡入几个依次脉动的圆点。
//   只显示，不记「在不在盖」、不读配置；时长由 LoadingCurtain 传进来。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：NotificationView 是顶部卡片、明确不挡点击；TranscriptView 是记录面板；UIView 默认过渡只管面板自身
//      开关时的一次淡入，没有「盖住后延时出指示」，也压不过落幕之后才打开的同层兄弟面板。
//   2. 扩展不行：塞进任何现有视图都会让它背上与自身无关的职责（全屏、挡点击、跨状态常驻）。
//      照 NotificationService / NotificationView 的分工：状态在 LoadingCurtain，这里只剩表现，所以单独一个 UIView 子类。
//   指示不用 TMP 文字：编辑器里运行时会把字形烘进动态字体资产（ai-docs/pitfalls.md「TMP Dynamic 字体资产…」）。

using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 加载黑幕视图。预制体 <c>Assets/_Project/Prefabs/UI/LoadingView.prefab</c>，Addressables 地址 <c>LoadingView</c>（UI 组）。
    /// Top 层、不全屏（不进栈）、Esc 关不掉；由 <see cref="LoadingCurtain"/> 首次落幕时打开并常驻，
    /// 揭开后整块 alpha 0、不挡点击、黑底与指示两个子物体停用（不画），空闲时没有任何动画在跑。
    /// 不靠关掉自带 Canvas 来藏：子图形会改挂到上一层 Canvas 上照样参与绘制。
    /// <para>
    /// 压在 Top 层最上面靠根节点自带的 Canvas（overrideSorting，sortingOrder = 所在层 Canvas + <see cref="SortingAboveLayer"/>），
    /// 不靠 <c>SetAsLastSibling</c>：落幕之后才打开的同层面板（比如通知卡片第一次 Show）会生成在兄弟节点最后，又把黑幕压下去。
    /// 自带 Canvas 的图形只归它自己的 GraphicRaycaster 管，所以根节点上缺 Canvas / GraphicRaycaster 时运行时补上。
    /// </para>
    /// <para>
    /// 面板根节点被 UIService 撑满 SafeArea；黑底每次落幕前按当前画布重算一次，撑满整块画布（刘海、手势条一起盖住），
    /// 加载指示留在 SafeArea 内的右下角。
    /// </para>
    /// </summary>
    public sealed class LoadingView : UIView
    {
        /// <summary>自带 Canvas 比所在层 Canvas 高出的排序值。UIService 相邻两层隔 100，取一半：压过本层，不碰上一层。</summary>
        private const int SortingAboveLayer = 50;

        private static readonly Vector3[] CanvasCorners = new Vector3[4];

        [Tooltip("全屏黑底（Image，raycastTarget 开着：盖住时挡点击）。每次落幕前由代码撑满整块画布。")]
        [SerializeField] private RectTransform background;

        [Tooltip("加载指示的 CanvasGroup（右下角，锚在安全区内）。盖住超过 UIConfig.LoadingHintDelaySeconds 才淡入。")]
        [SerializeField] private CanvasGroup hintGroup;

        [Tooltip("加载指示里依次脉动的圆点，按数组顺序错开相位。")]
        [SerializeField] private RectTransform[] hintDots;

        [Tooltip("加载指示淡入的秒数（真实时间）。")]
        [Min(0.01f)]
        [SerializeField] private float hintFadeSeconds = 0.2f;

        [Tooltip("每个圆点一次放大或缩小的秒数（真实时间）。")]
        [Min(0.05f)]
        [SerializeField] private float dotPulseSeconds = 0.4f;

        [Tooltip("相邻两个圆点开始脉动的时间差（秒）。")]
        [Min(0f)]
        [SerializeField] private float dotStaggerSeconds = 0.15f;

        [Tooltip("圆点缩到最小时的缩放比例。")]
        [Range(0f, 1f)]
        [SerializeField] private float dotMinScale = 0.4f;

        private Canvas ownCanvas;
        private MotionHandle fadeMotion;
        private MotionHandle hintMotion;
        private MotionHandle[] dotMotions;

        /// <summary>每次落幕 / 揭幕加一。淡入淡出的 await 回来时版本变了，说明被反方向的调用接手了，不再做收尾。</summary>
        private int fadeVersion;

        /// <summary>落幕开始到揭幕完成之间为 true。</summary>
        private bool shown;

        public override UILayer Layer => UILayer.Top;

        public override bool IsFullScreen => false;

        public override bool CloseOnCancel => false;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            EnsureCanvas();
            ApplySorting();
            if (!shown)
            {
                ApplyHidden();
            }

            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            StopMotions();
            shown = false;
            return UniTask.CompletedTask;
        }

        // 显隐全由 CoverAsync / RevealAsync 管：UIService 开 / 关面板时的通用淡入淡出会和黑幕自己的淡入抢根节点 alpha。
        protected internal override UniTask PlayOpenTransitionAsync(float seconds, CancellationToken ct) => UniTask.CompletedTask;

        protected internal override UniTask PlayCloseTransitionAsync(float seconds, CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 落幕：激活并撑满黑底、挡点击，从当前透明度淡入到全黑；盖严之后过 <paramref name="hintDelaySeconds"/> 秒出加载指示。
        /// </summary>
        public async UniTask CoverAsync(float fadeSeconds, float hintDelaySeconds, CancellationToken ct)
        {
            Validate();
            EnsureCanvas();
            ApplySorting();
            int version = ++fadeVersion;
            shown = true;
            if (hintMotion.IsActive()) hintMotion.Cancel();
            StopDots();
            background.gameObject.SetActive(true);
            hintGroup.gameObject.SetActive(true);
            FitBackgroundToCanvas();
            Group.blocksRaycasts = true;

            await FadeAsync(1f, fadeSeconds, ct);
            if (version == fadeVersion)
            {
                StartHint(hintDelaySeconds);
            }
        }

        /// <summary>揭幕：从当前透明度淡出，结束后不挡点击、停用黑底与指示、停掉指示动画。</summary>
        public async UniTask RevealAsync(float fadeSeconds, CancellationToken ct)
        {
            int version = ++fadeVersion;

            // 还在等延时的指示不许在淡出途中冒出来；已经出来的随根节点一起淡掉。
            if (hintMotion.IsActive()) hintMotion.Cancel();
            await FadeAsync(0f, fadeSeconds, ct);
            if (version == fadeVersion)
            {
                ApplyHidden();
            }
        }

        /// <summary>不播动画，直接揭开。揭幕被取消或出错时由 <see cref="LoadingCurtain"/> 兜底调用。</summary>
        public void HideImmediate()
        {
            fadeVersion++;
            ApplyHidden();
        }

        // 写法同 UIView.RunTransitionAsync：同一时刻只留一条淡入淡出，新的先掐断旧的（旧的 await 正常结束、不抛取消）；
        // UpdateIgnoreTimeScale 让暂停菜单把 timeScale 置 0 后回标题时也能播。
        private UniTask FadeAsync(float to, float seconds, CancellationToken ct)
        {
            if (fadeMotion.IsActive()) fadeMotion.Cancel();
            CanvasGroup group = Group;
            float from = group.alpha;

            // 时长按剩余距离折算：揭幕途中又落幕时从半透明接着淡，不会比完整一次更慢。
            float duration = seconds * Mathf.Abs(to - from);
            if (duration <= 0f)
            {
                group.alpha = to;
                return UniTask.CompletedTask;
            }

            // AddTo(gameObject)：视图被 UIService 直接销毁（退出时 ReleaseAllViews 不走 OnCloseAsync）时随之掐断。
            // 不自己写 OnDestroy——那会遮住基类 UIView 的同名私有消息。
            fadeMotion = LMotion.Create(from, to, duration)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToAlpha(group)
                .AddTo(gameObject);
            return fadeMotion.ToUniTask(CancelBehavior.Cancel, false, ct);
        }

        private void StartHint(float delaySeconds)
        {
            StopDots();
            hintGroup.alpha = 0f;
            float delay = Mathf.Max(0f, delaySeconds);
            hintMotion = LMotion.Create(0f, 1f, hintFadeSeconds)
                .WithDelay(delay)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToAlpha(hintGroup)
                .AddTo(gameObject);

            if (dotMotions == null || dotMotions.Length != hintDots.Length)
            {
                dotMotions = new MotionHandle[hintDots.Length];
            }

            Vector3 small = Vector3.one * dotMinScale;
            for (int i = 0; i < hintDots.Length; i++)
            {
                RectTransform dot = hintDots[i];
                if (dot == null) continue;
                dot.localScale = small;

                // 无限往返；延时只作用在第一轮（DelayType.FirstLoop），之后各点保持错开的相位。
                dotMotions[i] = LMotion.Create(small, Vector3.one, dotPulseSeconds)
                    .WithDelay(delay + i * dotStaggerSeconds)
                    .WithLoops(-1, LoopType.Yoyo)
                    .WithEase(Ease.InOutSine)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToLocalScale(dot)
                    .AddTo(gameObject);
            }
        }

        /// <summary>完全揭开后的静止态：透明、不挡点击、子物体停用不画、没有动画在跑。</summary>
        private void ApplyHidden()
        {
            // 退出游戏时 UIService 先把视图随 UIRoot 销毁，揭幕的收尾还会走到这里：已销毁就什么都不做，免得多一条警告。
            if (this == null) return;
            StopMotions();
            shown = false;
            CanvasGroup group = Group;
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            hintGroup.alpha = 0f;
            hintGroup.blocksRaycasts = false;
            background.gameObject.SetActive(false);
            hintGroup.gameObject.SetActive(false);
        }

        /// <summary>根节点自带 Canvas 与 GraphicRaycaster；预制体上缺了就补（同 UIView.Group 缺 CanvasGroup 时的做法）。</summary>
        private void EnsureCanvas()
        {
            if (ownCanvas == null)
            {
                ownCanvas = GetComponent<Canvas>();
                if (ownCanvas == null)
                {
                    ownCanvas = gameObject.AddComponent<Canvas>();
                }
            }

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        /// <summary>
        /// 每次落幕前重设排序：overrideSorting 在物体挪层级 / 重新激活之后不保证还在，重设一遍最稳。
        /// </summary>
        private void ApplySorting()
        {
            Canvas layerCanvas = LayerCanvas();
            ownCanvas.overrideSorting = true;
            ownCanvas.sortingOrder = (layerCanvas == null ? 0 : layerCanvas.sortingOrder) + SortingAboveLayer;
        }

        /// <summary>本面板所在层的 Canvas（父链上最近的一个，不含自己那个）。</summary>
        private Canvas LayerCanvas()
        {
            Transform parent = transform.parent;
            return parent == null ? null : parent.GetComponentInParent<Canvas>();
        }

        /// <summary>
        /// 把黑底撑满整块根画布：根画布四角换算到黑底父节点的本地坐标，减去父节点自身的矩形得到 offset。
        /// 父节点（面板根）被 SafeArea 收窄过，不这样算的话刘海、手势条那几条边会露出来。
        /// </summary>
        private void FitBackgroundToCanvas()
        {
            var parent = background.parent as RectTransform;
            Canvas layerCanvas = LayerCanvas();
            if (parent == null || layerCanvas == null)
            {
                return;
            }

            var rootRect = layerCanvas.rootCanvas.transform as RectTransform;
            if (rootRect == null)
            {
                return;
            }

            rootRect.GetWorldCorners(CanvasCorners);
            Vector2 min = parent.InverseTransformPoint(CanvasCorners[0]);
            Vector2 max = parent.InverseTransformPoint(CanvasCorners[2]);
            Rect parentRect = parent.rect;
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = min - parentRect.min;
            background.offsetMax = max - parentRect.max;
        }

        private void StopDots()
        {
            if (dotMotions == null) return;
            for (int i = 0; i < dotMotions.Length; i++)
            {
                if (dotMotions[i].IsActive()) dotMotions[i].Cancel();
            }
        }

        private void StopMotions()
        {
            if (fadeMotion.IsActive()) fadeMotion.Cancel();
            if (hintMotion.IsActive()) hintMotion.Cancel();
            StopDots();
        }

        private void Validate()
        {
            if (background == null || hintGroup == null || hintDots == null)
            {
                throw new System.InvalidOperationException(
                    "LoadingView 预制体缺少 background / hintGroup / hintDots 引用，检查 Prefabs/UI/LoadingView.prefab 的接线");
            }
        }
    }
}
