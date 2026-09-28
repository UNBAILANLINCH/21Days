// 职责：演出管线的全局参数——长按跳过秒数、进场黑场时长、停顿提示与跳过提示文案、字幕逐字节奏、「自动」继续间隔、默认策略开关。
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

        [Tooltip("进场黑场淡变的时长（秒，unscaled）。0 = 立即。")]
        [Min(0f)]
        [SerializeField] private float fadeSeconds = 0.25f;

        [Tooltip("时间轴停在「等待输入」标记上时显示的提示符（对白面板右下角）；操作说明小字在面板预制体的 HoldHint 里。")]
        [SerializeField] private string holdPromptText = "▼";

        [Tooltip("跳过键位提示的格式串（显示在「跳过 ▶」下方的小字），{0} 会替换成跳过键的键位名（取不到时替换成「跳过」）。")]
        [SerializeField] private string skipHintFormat = "长按 {0}";

        [Tooltip("字幕逐字显示速度（字 / 秒，unscaled）。0 = 不逐字，整句直出。")]
        [Min(0f)]
        [SerializeField] private float subtitleCharactersPerSecond = 35f;

        [Tooltip("字幕打出标点后停顿多少秒再继续出字；0 = 不停。连续标点只在最后一个后停，句末不停。")]
        [Min(0f)]
        [SerializeField] private float subtitlePunctuationPauseSeconds = 0.12f;

        [Tooltip("字幕逐字时哪些字符算标点（打出后短停顿）；空 = 不停。")]
        [SerializeField] private string subtitlePunctuationChars = "，。！？…；：、,.!?";

        [Tooltip("开「自动」后，停顿（▼）处当前句字幕打完再等多少秒自动继续（秒，unscaled）；0 = 打完立即继续。语义同对白的自动间隔。")]
        [Min(0f)]
        [SerializeField] private float autoAdvanceSeconds = PerformancePolicy.DefaultAutoAdvanceSeconds;

        [Tooltip("新建演出时「暂停世界」开关的默认值（模板工厂用；每段演出以舞台上的开关为准）。")]
        [SerializeField] private bool defaultPauseWorld = true;

        [Tooltip("新建演出时「隐藏 HUD」开关的默认值（模板工厂用；每段演出以舞台上的开关为准）。")]
        [SerializeField] private bool defaultHideHud = true;

        /// <summary>长按跳过秒数；资产里被改成非正数时按 1 秒兜底（策略构造要求大于 0）。</summary>
        public float SkipHoldSeconds => skipHoldSeconds > 0f ? skipHoldSeconds : 1f;

        public float FadeSeconds => fadeSeconds;
        public string HoldPromptText => holdPromptText;
        public string SkipHintFormat => skipHintFormat;
        /// <summary>字幕逐字速度（字 / 秒）；0 或负数 = 整句直出。</summary>
        public float SubtitleCharactersPerSecond => subtitleCharactersPerSecond;
        /// <summary>字幕标点后停顿秒数；负数按 0 兜底。</summary>
        public float SubtitlePunctuationPauseSeconds => subtitlePunctuationPauseSeconds > 0f ? subtitlePunctuationPauseSeconds : 0f;
        public string SubtitlePunctuationChars => subtitlePunctuationChars;

        /// <summary>
        /// 「自动」继续间隔（秒）；资产里被改成负数 / NaN / 无穷时按 <see cref="PerformancePolicy.DefaultAutoAdvanceSeconds"/> 兜底
        /// （策略构造要求不小于 0 的有限数）。
        /// </summary>
        public float AutoAdvanceSeconds => autoAdvanceSeconds >= 0f && !float.IsInfinity(autoAdvanceSeconds)
            ? autoAdvanceSeconds
            : PerformancePolicy.DefaultAutoAdvanceSeconds;

        public bool DefaultPauseWorld => defaultPauseWorld;
        public bool DefaultHideHud => defaultHideHud;

        /// <summary>按默认开关组装的策略（可跳过）。</summary>
        public PerformancePolicy DefaultPolicy => BuildPolicy(true, defaultPauseWorld, defaultHideHud);

        /// <summary>用给定开关 + 本配置的长按秒数与「自动」继续间隔组装策略。</summary>
        public PerformancePolicy BuildPolicy(bool skippable, bool pauseWorld, bool hideHud)
        {
            return new PerformancePolicy(skippable, SkipHoldSeconds, pauseWorld, hideHud, AutoAdvanceSeconds);
        }
    }
}
