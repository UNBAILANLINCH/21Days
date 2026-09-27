// 职责：UI 框架的可调数值——参考分辨率、缩放匹配系数、过渡动画时长。
// 为什么新建：这三个值要在 Inspector 上调（不同项目、不同平台取值不同），按 csharp-code.md
//   「数值配置进 ScriptableObject」不能写死在 UIService 里；工程内没有任何框架级的 Config 资产可扩展。

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

        /// <summary>CanvasScaler 的参考分辨率。</summary>
        public Vector2 ReferenceResolution => referenceResolution;

        /// <summary>CanvasScaler 的宽高匹配系数，0～1。1 = 按高度匹配：1080p 基准，宽屏两侧多看，UI 不缩放。</summary>
        public float MatchWidthOrHeight => matchWidthOrHeight;

        /// <summary>面板淡入淡出秒数，0 表示跳过动画。</summary>
        public float TransitionSeconds => transitionSeconds;

        /// <summary>通知默认停留秒数（<see cref="INotificationService.Show"/> 的 seconds ≤ 0 时取这个）。</summary>
        public float NotificationSeconds => notificationSeconds;
    }
}
