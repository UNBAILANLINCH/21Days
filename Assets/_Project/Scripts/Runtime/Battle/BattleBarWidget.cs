// 职责：战斗界面上的一根条（BOSS 生命 / BOSS 醉酒 / 玩家生命）——按比例拉伸填充、写「当前 / 上限」，变化时用不受缩放的时间补间。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有现成的数值条组件（探索 HUD 不显示血量）；Slider 带交互与导航，拿来当只读条要处处关掉。
//   2. 扩展不行：塞进 BattleView 会让三根条的补间句柄、取消各写一遍。
// 填充靠改 anchorMax.x（不依赖 Image.Filled 必须有 Sprite 的限制，白盒纯色就能用）。
using Game.Core.Simulation;
using LitMotion;
using TMPro;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>数值条。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleBarWidget : MonoBehaviour
    {
        [Tooltip("填充块：锚点 x 从 0 拉到比例。")]
        [SerializeField] private RectTransform fill;

        [Tooltip("条上的「当前 / 上限」。")]
        [SerializeField] private TMP_Text label;

        [Tooltip("变化补间时长（秒，不受时间缩放）；0 = 立即。")]
        [SerializeField, Min(0f)] private float tweenSeconds = 0.25f;

        private float shown = -1f;
        private MotionHandle tween;

        /// <summary>当前显示的比例（0..1，测试 / 回放读）。</summary>
        public float Ratio => shown < 0f ? 0f : shown;

        private void OnDisable()
        {
            if (tween.IsActive()) tween.Complete();
        }

        /// <summary>设数值；<paramref name="animate"/> 为假或第一次设时直接跳到位。</summary>
        public void Set(int value, int max, string text, bool animate)
        {
            float target = max <= 0 ? 0f : GameMath.Clamp01((float)value / max);
            if (label != null) label.text = text;
            if (tween.IsActive()) tween.Cancel();
            if (!animate || shown < 0f || tweenSeconds <= 0f || !isActiveAndEnabled)
            {
                Apply(target);
                return;
            }

            tween = LMotion.Create(shown, target, tweenSeconds)
                .WithEase(Ease.OutCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(Apply)
                .AddTo(gameObject);
        }

        private void Apply(float ratio)
        {
            shown = ratio;
            if (fill == null) return;
            Vector2 max = fill.anchorMax;
            max.x = ratio;
            fill.anchorMax = max;
        }
    }
}
