// 职责：UI 框架的可调数值——参考分辨率、缩放匹配系数、过渡动画时长、通知停留时长、加载黑幕时长。
// 为什么新建：这三个值要在 Inspector 上调（不同项目、不同平台取值不同），按 csharp-code.md
//   「数值配置进 ScriptableObject」不能写死在 UIService 里；工程内没有任何框架级的 Config 资产可扩展。
//   之后的通知停留、加载黑幕（roadmap E4）时长同属「UI 框架的可调数值」，扩展本文件而不是另建配置资产。

using UnityEngine;

namespace Game.Core.UI
{
    /// <summary>
    /// UI 配置。资产在 <c>Assets/_Project/Data/UI/UIConfig.asset</c>，拖到 Boot 场景的 GameLifetimeScope 上。
    /// 运行时只读——改了会写回资产（unity-assets.md #ScriptableObject 配置）。
    /// </summary>
    [CreateAssetMenu(fileName = "UIConfig", menuName = "21Days/Core/UI Config")]
    public sealed class UIConfig : ScriptableObject
    {
        [Header("画布缩放")]
        [Tooltip("CanvasScaler 的参考分辨率。UI 按这个尺寸摆，运行时整体缩放到实际屏幕。")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

        // 用户 2026-09-26 定：1080p / 16:9 基准，UI 按高度匹配，宽屏横向扩展。
        [Tooltip("1 = 按高度匹配：1080p 基准，宽屏两侧多看，UI 不缩放。0 = 只按宽度，0.5 = 两者折中。")]
        [Range(0f, 1f)]
        [SerializeField] private float matchWidthOrHeight = 1f;

        [Header("过渡动画")]
        [Tooltip("面板开关时 CanvasGroup 淡入淡出的秒数。0 表示不做动画，直接显示/隐藏。")]
        [Min(0f)]
        [SerializeField] private float transitionSeconds = 0.15f;

        [Header("通知")]
        [Tooltip("INotificationService.Show 不指定时长时，每条通知停留的秒数（真实时间，世界暂停也照走）。")]
        [Min(0.1f)]
        [SerializeField] private float notificationSeconds = 2.5f;

        [Header("加载过渡")]
        [Tooltip("切场景时加载黑幕淡入、淡出各自的秒数（真实时间，暂停时也照走）。0 表示不做动画，直接盖上 / 揭开。")]
        [Min(0f)]
        [SerializeField] private float loadingFadeSeconds = 0.25f;

        [Tooltip("黑幕完全盖住后再过多少秒还没揭开，右下角才淡入加载指示（真实时间）。加载快时不会闪一下指示。")]
        [Min(0f)]
        [SerializeField] private float loadingHintDelaySeconds = 0.5f;

        /// <summary>CanvasScaler 的参考分辨率。</summary>
        public Vector2 ReferenceResolution => referenceResolution;

        /// <summary>CanvasScaler 的宽高匹配系数，0～1。1 = 按高度匹配：1080p 基准，宽屏两侧多看，UI 不缩放。</summary>
        public float MatchWidthOrHeight => matchWidthOrHeight;

        /// <summary>面板淡入淡出秒数，0 表示跳过动画。</summary>
        public float TransitionSeconds => transitionSeconds;

        /// <summary>通知默认停留秒数（<see cref="INotificationService.Show"/> 的 seconds ≤ 0 时取这个）。</summary>
        public float NotificationSeconds => notificationSeconds;

        /// <summary>加载黑幕淡入、淡出各自的秒数，0 表示直接盖上 / 揭开。</summary>
        public float LoadingFadeSeconds => loadingFadeSeconds;

        /// <summary>黑幕完全盖住后多少秒还没揭开才出加载指示。</summary>
        public float LoadingHintDelaySeconds => loadingHintDelaySeconds;
    }
}
