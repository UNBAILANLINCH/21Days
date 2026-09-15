// 职责：所有 UI 面板的基类——三段生命周期 + 所在层 + 默认的淡入淡出过渡。
// 为什么新建：architecture.md 5.6 把它定成独立契约；面板的生命周期不能靠 Unity 的
//   Awake/OnEnable（那是同步的，开面板要 await 加载数据），必须另立一套异步钩子。

using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;

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
        private CanvasGroup canvasGroup;
        private MotionHandle transition;

        /// <summary>本面板挂在哪一层。子类必须给出。</summary>
        public abstract UILayer Layer { get; }

        /// <summary>
        /// 是不是全屏面板。全屏的 Panel 压栈时会把它下面的 Panel 隐藏掉（省一整层的绘制开销）。
        /// 默认「Panel 层就是全屏」——半透明的小面板、侧边栏要自己重写成 false。
        /// </summary>
        public virtual bool IsFullScreen => Layer == UILayer.Panel;

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
        /// 淡入。默认用 LitMotion 把 CanvasGroup.alpha 从 0 拉到 1；
        /// <paramref name="seconds"/> 来自 <see cref="UIConfig.TransitionSeconds"/>，为 0 时直接置 1 不等帧。
        /// <para>
        /// 声明成 protected internal：UIService（同程序集）要调它，玩法模块（别的程序集）要能重写它。
        /// </para>
        /// </summary>
        protected internal virtual UniTask PlayOpenTransitionAsync(float seconds, CancellationToken ct)
        {
            return FadeAsync(0f, 1f, seconds, ct);
        }

        /// <summary>淡出。默认把 alpha 从 1 降到 0。</summary>
        protected internal virtual UniTask PlayCloseTransitionAsync(float seconds, CancellationToken ct)
        {
            return FadeAsync(1f, 0f, seconds, ct);
        }

        /// <summary>
        /// alpha 动画。同一时刻只允许一个过渡在跑——开到一半又被关掉时，
        /// 老动画不掐断会在新动画之后把 alpha 又改回去。
        /// </summary>
        private UniTask FadeAsync(float from, float to, float seconds, CancellationToken ct)
        {
            if (transition.IsActive())
            {
                transition.Cancel();
            }

            CanvasGroup group = Group;
            if (seconds <= 0f)
            {
                group.alpha = to;
                return UniTask.CompletedTask;
            }

            group.alpha = from;

            // UpdateIgnoreTimeScale：暂停菜单要在 timeScale = 0 时也能淡入，不然开不出来。
            transition = LMotion.Create(from, to, seconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToAlpha(group);

            // cancelAwaitOnMotionCanceled = false：上一个过渡被新过渡掐断时，
            // 老的 await 正常结束而不是抛 OperationCanceledException——「开到一半又关掉」是正常操作，不是错误。
            return transition.ToUniTask(CancelBehavior.Cancel, false, ct);
        }

        private void OnDestroy()
        {
            if (transition.IsActive())
            {
                transition.Cancel();
            }
        }
    }
}
