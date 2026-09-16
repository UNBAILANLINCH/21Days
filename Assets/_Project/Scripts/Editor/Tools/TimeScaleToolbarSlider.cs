// 职责：工具栏上的播放速度条——拖动即改 Time.timeScale，点右边的数字复位成 1。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：现有工具栏元素都是「点一下做一件事」的按钮，没有一个带可拖动的连续取值。
//   2. 扩展不行：塞进 GameFlowRestartShortcut 名实不符——那个管重跑流程，这个管时间缩放，
//      而且那个只在播放中可用，这个在编辑期设好也算数（会带进下一次播放）。
//
// 退出播放时会把 timeScale 复位成 1：上一次调成 0.1 倍速忘了改回来，下次进游戏一片龟速却
// 找不到原因，这种坑不值得留给任何人。编辑期手动设的值不受影响。

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Editor
{
    /// <summary>
    /// 播放速度条。拖滑块改 <see cref="Time.timeScale"/>：0 是完全静止，1 是原速。
    /// <para>
    /// 上限取 4：再高物理步进会明显失真（固定步长撑不住），调试价值还不如 2 倍速稳定。
    /// 要更大就改 <see cref="MaxScale"/>，但别指望高倍速下的物理表现能当数。
    /// </para>
    /// <para>
    /// 每帧会把外部对 timeScale 的修改同步回滑块（玩法代码里有子弹时间、暂停之类的也会改它），
    /// 这样条上显示的永远是真实值，而不是「上次拖到哪」。
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class TimeScaleToolbarSlider
    {
        /// <summary>速度上限。</summary>
        private const float MaxScale = 4f;

        /// <summary>
        /// 放 PlayMode 区末尾，也就是刷新按钮的右边——速度和播放控制是一组，摆一起才顺手。
        /// PlayMode 区是 Row 布局，末尾＝视觉最右；右区那种 RowReverse 的反向坑这里没有。
        /// </summary>
        private const int PlayModeZoneIndex = -1;

        private static Slider slider;
        private static Label valueLabel;

        /// <summary>上一帧同步过的值。只有真的变了才动 UI，避免每帧刷一遍标签文本。</summary>
        private static float lastSynced = -1f;

        static TimeScaleToolbarSlider()
        {
            MainToolbarInjector.Register(MainToolbarInjector.Zone.PlayMode, Create, PlayModeZoneIndex);
            EditorApplication.update += SyncFromEngine;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static VisualElement Create()
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Row;
            group.style.alignItems = Align.Center;
            group.style.marginLeft = 4f;
            group.style.marginRight = 4f;

            var caption = new Label("速度");
            caption.style.marginRight = 4f;
            caption.style.unityTextAlign = TextAnchor.MiddleLeft;
            group.Add(caption);

            slider = new Slider(0f, MaxScale);
            slider.value = Time.timeScale;
            slider.style.width = 90f;
            slider.style.marginTop = 0f;
            slider.style.marginBottom = 0f;
            slider.tooltip = "播放速度（Time.timeScale）：0 静止，1 原速，最高 " + MaxScale + " 倍。"
                             + "退出播放时自动复位成 1。";
            slider.RegisterValueChangedCallback(OnSliderChanged);
            group.Add(slider);

            valueLabel = new Label(Format(Time.timeScale));
            valueLabel.style.marginLeft = 4f;
            valueLabel.style.minWidth = 26f;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            valueLabel.tooltip = "点一下复位成 1。";
            valueLabel.RegisterCallback<ClickEvent>(OnValueLabelClicked);
            group.Add(valueLabel);

            lastSynced = Time.timeScale;
            return group;
        }

        private static void OnSliderChanged(ChangeEvent<float> evt)
        {
            Apply(evt.newValue);
        }

        private static void OnValueLabelClicked(ClickEvent evt)
        {
            Apply(1f);
        }

        /// <summary>写回引擎并刷新显示。走同一个出口，滑块与数字标签不会各说各话。</summary>
        private static void Apply(float scale)
        {
            Time.timeScale = scale;
            lastSynced = scale;

            if (slider != null)
            {
                // SetValueWithoutNotify：否则回调里再改值会绕回 OnSliderChanged，白跑一圈。
                slider.SetValueWithoutNotify(scale);
            }

            if (valueLabel != null)
            {
                valueLabel.text = Format(scale);
            }
        }

        /// <summary>
        /// 把引擎里的真实 timeScale 同步到条上。玩法代码自己改了（子弹时间、暂停）也能看见。
        /// 每帧就是一个 float 比较，值没变就立刻返回。
        /// </summary>
        private static void SyncFromEngine()
        {
            if (slider == null)
            {
                return;
            }

            float current = Time.timeScale;
            if (Mathf.Approximately(current, lastSynced))
            {
                return;
            }

            lastSynced = current;
            slider.SetValueWithoutNotify(Mathf.Clamp(current, 0f, MaxScale));
            if (valueLabel != null)
            {
                valueLabel.text = Format(current);
            }
        }

        /// <summary>
        /// 退出播放时复位成 1。只在 ExitingPlayMode 这一刻做，编辑期手动设的值不碰。
        /// </summary>
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                Apply(1f);
            }
        }

        /// <summary>显示成最多两位小数，整数不拖小数点（1 而不是 1.00）。</summary>
        private static string Format(float scale)
        {
            return scale.ToString("0.##");
        }
    }
}
