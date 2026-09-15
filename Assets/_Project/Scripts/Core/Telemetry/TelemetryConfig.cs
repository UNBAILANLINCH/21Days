// 职责：埋点的可调数值资产——总开关、最低级别、模块过滤、采样与限流参数（docs/telemetry.md 第 3 节那张表）。
// 为什么新建：这些值要在 Inspector 上按场合改（查某个模块时填过滤表、压测时把采样间隔调小），
//   按 csharp-code.md「数值配置进 ScriptableObject」不能写死在 TelemetryService 里；
//   UIConfig / AudioConfig 管的是各自服务的参数，没有可扩展的现成配置资产。

using UnityEngine;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点配置。资产在 <c>Assets/_Project/Data/Telemetry/TelemetryConfig.asset</c>，
    /// 拖到 Boot 场景 GameBootstrap 物体上的 GameLifetimeScope 里。
    /// <para>运行时只读：服务启动时调一次 <see cref="ToOptions"/> 取快照，之后再改资产不影响本次运行
    /// （改了也会写回资产，见 unity-assets.md #ScriptableObject 配置）。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "TelemetryConfig", menuName = "21Days/Core/Telemetry Config")]
    public sealed class TelemetryConfig : ScriptableObject
    {
        [Header("开关与过滤")]
        [Tooltip("总开关。关掉后所有 Track 变成空调用，一条埋点都不会写出去。")]
        [SerializeField] private bool enabled = true;

        [Tooltip("最低级别。设 Warn 就只留警告与错误；Debug 级在正式包里本来就整句被剔除。")]
        [SerializeField] private TelemetryLevel minLevel = TelemetryLevel.Info;

        [Tooltip("模块过滤。留空 = 全埋；填了就只埋这几个模块，按点号边界前缀匹配（填 core 能捞到 core.ui）。")]
        [SerializeField] private string[] moduleFilter = new string[0];

        [Header("性能采样")]
        [Tooltip("周期采样间隔秒数。设 0 关闭周期采样，连采样器都不工作。")]
        [Min(0f)]
        [SerializeField] private float sampleIntervalSeconds = 5f;

        [Tooltip("帧尖峰阈值毫秒。单帧超过就立刻打一条 core.perf/spike；同一次卡顿只打一条。")]
        [Min(0f)]
        [SerializeField] private float spikeThresholdMs = 100f;

        [Header("限流与开销")]
        [Tooltip("每秒最多条数。超出的丢弃，并在下一秒补一条 core/throttled 说明丢了多少。设 0 表示不限流。")]
        [Min(0)]
        [SerializeField] private int maxEventsPerSecond = 200;

        [Tooltip("启动时关掉 Debug.Log 的堆栈采集。埋点开销的大头；影响全工程的 Debug.Log，Warning / Error 不受影响。")]
        [SerializeField] private bool disableLogStackTrace = true;

        [Header("编辑器镜像")]
        [Tooltip("Logs/telemetry/ 下保留最近几个会话文件，更老的在启动时删掉。设 0 表示不清理。只影响编辑器，真机不写镜像。")]
        [Min(0)]
        [SerializeField] private int editorMirrorKeepSessions = 20;

        /// <summary>总开关。</summary>
        public bool Enabled => enabled;

        /// <summary>最低级别。</summary>
        public TelemetryLevel MinLevel => minLevel;

        /// <summary>模块过滤表，可能为空数组。</summary>
        public string[] ModuleFilter => moduleFilter;

        /// <summary>周期采样间隔秒数，0 表示关闭。</summary>
        public float SampleIntervalSeconds => sampleIntervalSeconds;

        /// <summary>帧尖峰阈值毫秒。</summary>
        public float SpikeThresholdMs => spikeThresholdMs;

        /// <summary>每秒最多条数。</summary>
        public int MaxEventsPerSecond => maxEventsPerSecond;

        /// <summary>是否关掉 Debug.Log 的堆栈采集。</summary>
        public bool DisableLogStackTrace => disableLogStackTrace;

        /// <summary>编辑器镜像目录里保留最近几个会话文件，0 表示不清理。</summary>
        public int EditorMirrorKeepSessions => editorMirrorKeepSessions;

        /// <summary>取一份运行期快照。服务只认快照，不持有资产引用——避免运行中被改资产改出半截状态。</summary>
        public TelemetryOptions ToOptions()
        {
            return new TelemetryOptions(
                enabled,
                minLevel,
                moduleFilter,
                sampleIntervalSeconds,
                spikeThresholdMs,
                maxEventsPerSecond,
                disableLogStackTrace,
                editorMirrorKeepSessions);
        }
    }
}
