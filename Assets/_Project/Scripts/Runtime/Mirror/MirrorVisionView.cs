// 职责：镜裂视野遮罩（Hud 层最底下）——全屏径向暗角随可见范围收缩；沉浸模式下仍显示（遮罩是视野，不是 HUD）。只显示，不注入服务。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：没有任何全屏遮罩类面板；ExplorationHudView 虽然沉浸时也留着，但它有按钮、在 Hud 层的普通位置，会被暗角压住。
//   2. 扩展不行：放进 MirrorHudView 会跟镜图标一起被沉浸藏掉（VisibleWhenHudHidden 是整个面板一个开关）。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Simulation;
using Game.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Mirror
{
    /// <summary>
    /// 视野遮罩。预制体 <c>Prefabs/UI/MirrorVisionView.prefab</c>，Addressables 地址 <c>MirrorVisionView</c>。
    /// <para>
    /// 层级：Hud 层（sortingOrder 0，<see cref="UILayer"/> 声明顺序 Hud &lt; Panel &lt; Popup &lt; Top），打开时移到 Hud 层最底（第一个子物体），
    /// 所以任务栏、镜图标等 Hud 面板画在它上面；对白框（DialogueView，Popup）、演出面板（PerformanceView，Panel）都在更高层，遮不住。
    /// 对白 / 演出期间整层 Hud 被 SetLayerVisible 藏掉，遮罩随之消失；全屏面板（标题、暂停菜单）盖住 Hud 时同样看不到。
    /// </para>
    /// <para>
    /// 做法：暗角图（ui_mirror_vignette，中心透明、四周渐黑）铺满画布，按可见范围 r（0..1）缩放：
    /// r ≥ 1 时整张隐藏；r 越小缩放越接近 <see cref="minScale"/>（暗角最重）。缩放始终 ≥ 1，画面四周不会露出未遮盖的边。
    /// </para>
    /// </summary>
    public sealed class MirrorVisionView : UIView
    {
        [Tooltip("暗角图 Image（ui_mirror_vignette），铺满本面板、不吃射线。")]
        [SerializeField] private Image vignette;
        [Tooltip("可见范围为 0 时的缩放（暗角最重）。≥ 1，保证铺满画布。")]
        [SerializeField, Min(1f)] private float minScale = 1f;
        [Tooltip("可见范围接近 1 时的缩放（暗角几乎推出画面）。")]
        [SerializeField, Min(1f)] private float maxScale = 2.5f;

        private float shownRadius = -1f;

        public override UILayer Layer => UILayer.Hud;
        public override bool IsFullScreen => false;
        public override bool VisibleWhenHudHidden => true;

        /// <summary>暗角当前对应的可见范围（0..1；≥ 1 表示不遮、暗角隐藏，越小暗角越重）；还没打开过时为 1。</summary>
        public float ShownRadius => shownRadius < 0f ? 1f : shownRadius;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (vignette == null) throw new InvalidOperationException("MirrorVisionView 引用未接线：vignette");
            vignette.raycastTarget = false;
            // 压到 Hud 层最底：之后打开的 Hud 面板都在它上面（UIService 新实例挂在层末尾）。
            transform.SetAsFirstSibling();
            shownRadius = -1f;
            SetRadius(1f);
            return UniTask.CompletedTask;
        }

        /// <summary>按可见范围（0..1，1 = 不遮）更新暗角；与当前相同时不动。事件驱动调用，不在每帧路径上。</summary>
        public void SetRadius(float radius)
        {
            if (radius == shownRadius) return;
            shownRadius = radius;
            bool visible = radius < 1f;
            vignette.enabled = visible;
            if (!visible) return;
            float scale = ScaleFor(radius, minScale, maxScale);
            vignette.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>可见范围 → 暗角缩放：线性插在 [minScale, maxScale]，结果不小于 1。纯函数，供视图与测试共用。</summary>
        public static float ScaleFor(float radius, float minScale, float maxScale)
        {
            float scale = GameMath.Lerp(minScale, maxScale, GameMath.Clamp01(radius));
            return GameMath.Max(1f, scale);
        }
    }
}
