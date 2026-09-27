// 职责：演出管线的全局参数——长按跳过秒数、黑边高度、进出场黑场时长、停顿提示与跳过提示文案、默认策略开关。
// 为什么新建（复用 → 扩展 → 新建）：数值配置按规则进 ScriptableObject；DialogueConfig 是对白专用，
//   塞进去会让 Performance 依赖 Dialogue（方向禁止），只能新建本模块的配置。
using UnityEngine;

namespace Game.Performance
{
    /// <summary>演出配置。资产放 <c>Data/Performance/PerformanceConfig.asset</c>，拖到 Boot 场景的 PerformanceInstaller 上。运行时只读。</summary>
    [CreateAssetMenu(menuName = "21Days/Performance/PerformanceConfig", fileName = "PerformanceConfig")]
    public sealed class PerformanceConfig : ScriptableObject
    {
        [Tooltip("长按跳过键多少秒触发跳过（秒，必须大于 0）。")]
        [Min(0.05f)]
        [SerializeField] private float skipHoldSeconds = 1f;

        [Tooltip("上下黑边的目标高度（参考分辨率下的像素）。")]
        [Min(0f)]
        [SerializeField] private float letterboxHeight = 90f;

        [Tooltip("进出场黑场淡变、黑边推入的时长（秒，unscaled）。0 = 立即。")]
        [Min(0f)]
        [SerializeField] private float fadeSeconds = 0.25f;

        [Tooltip("时间轴停在「等待输入」标记上时显示的提示符（对白面板右下角）；操作说明小字在面板预制体的 HoldHint 里。")]
        [SerializeField] private string holdPromptText = "▼";

        [Tooltip("跳过键位提示的格式串（显示在「跳过 ▶」下方的小字），{0} 会替换成跳过键的键位名（取不到时替换成「跳过」）。")]
        [SerializeField] private string skipHintFormat = "长按 {0}";

        [Tooltip("新建演出时「暂停世界」开关的默认值（模板工厂用；每段演出以舞台上的开关为准）。")]
        [SerializeField] private bool defaultPauseWorld = true;

        [Tooltip("新建演出时「隐藏 HUD」开关的默认值（模板工厂用；每段演出以舞台上的开关为准）。")]
        [SerializeField] private bool defaultHideHud = true;

        [Tooltip("新建演出时「上下黑边」开关的默认值（模板工厂用；每段演出以舞台上的开关为准）。")]
        [SerializeField] private bool defaultLetterbox = true;

        /// <summary>长按跳过秒数；资产里被改成非正数时按 1 秒兜底（策略构造要求大于 0）。</summary>
        public float SkipHoldSeconds => skipHoldSeconds > 0f ? skipHoldSeconds : 1f;

        public float LetterboxHeight => letterboxHeight;
        public float FadeSeconds => fadeSeconds;
        public string HoldPromptText => holdPromptText;
        public string SkipHintFormat => skipHintFormat;
        public bool DefaultPauseWorld => defaultPauseWorld;
        public bool DefaultHideHud => defaultHideHud;
        public bool DefaultLetterbox => defaultLetterbox;

        /// <summary>按默认开关组装的策略（可跳过）。</summary>
        public PerformancePolicy DefaultPolicy => BuildPolicy(true, defaultPauseWorld, defaultHideHud, defaultLetterbox);

        /// <summary>用给定开关 + 本配置的长按秒数组装策略。</summary>
        public PerformancePolicy BuildPolicy(bool skippable, bool pauseWorld, bool hideHud, bool letterbox)
        {
            return new PerformancePolicy(skippable, SkipHoldSeconds, pauseWorld, hideHud, letterbox);
        }
    }
}
