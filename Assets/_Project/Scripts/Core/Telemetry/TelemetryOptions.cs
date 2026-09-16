// 职责：埋点的运行期取值（开关、最低级别、模块过滤、采样与限流参数），以及模块过滤的判定逻辑。
// 为什么新建：TelemetryConfig 是资产，只有 Unity 能造，EditMode 测试要改一个「最低级别」就得去动
//   序列化字段——没法写脱离编辑器的确定性测试。把资产读出来的值收进一个不可变对象，服务只认这个对象，
//   于是测试直接 new 一个就能跑，正式运行时由资产 ToOptions() 出来，两条路进同一个门。
//   没有扩展进 TelemetryConfig.cs：那个文件是 Inspector 面板的形状（[Header]/[Tooltip]/[Min]），
//   这里是运行期的形状，把两者绑死等于让测试也去依赖 UnityEngine 的序列化。

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点运行期取值。不可变，启动时由 <see cref="TelemetryConfig.ToOptions"/> 造一次。
    /// <para>默认值以 <see cref="TelemetryConfig"/> 的字段初始值为准，<see cref="Default"/> 是它的代码版镜像，改一处要同步改另一处。</para>
    /// </summary>
    public sealed class TelemetryOptions
    {
        private readonly string[] moduleFilter;

        public TelemetryOptions(
            bool enabled,
            TelemetryLevel minLevel,
            string[] moduleFilter,
            float sampleIntervalSeconds,
            float spikeThresholdMs,
            int maxEventsPerSecond,
            bool disableLogStackTrace,
            int editorMirrorKeepSessions = 20)
        {
            Enabled = enabled;
            MinLevel = minLevel;
            this.moduleFilter = moduleFilter;
            SampleIntervalSeconds = sampleIntervalSeconds;
            SpikeThresholdMs = spikeThresholdMs;
            MaxEventsPerSecond = maxEventsPerSecond;
            DisableLogStackTrace = disableLogStackTrace;
            EditorMirrorKeepSessions = editorMirrorKeepSessions;
        }

        /// <summary>出厂默认，同 docs/telemetry.md 第 3 节那张表。</summary>
        public static TelemetryOptions Default { get; } =
            new TelemetryOptions(true, TelemetryLevel.Info, null, 5f, 100f, 200, true, 20);

        /// <summary>总开关。关掉后所有 Track 变成空调用（过滤在最前面，属性照样不装箱）。</summary>
        public bool Enabled { get; }

        /// <summary>最低级别，低于它的整条丢弃。</summary>
        public TelemetryLevel MinLevel { get; }

        /// <summary>性能采样间隔秒数，0 表示关闭周期采样（连采样器都不工作）。</summary>
        public float SampleIntervalSeconds { get; }

        /// <summary>帧尖峰阈值毫秒，单帧超过就立刻补一条 spike。</summary>
        public float SpikeThresholdMs { get; }

        /// <summary>每秒最多条数，超出的丢弃并在下一秒补一条 <c>core/throttled</c>。小于等于 0 表示不限流。</summary>
        public int MaxEventsPerSecond { get; }

        /// <summary>启动时是否关掉 Debug.Log 的堆栈采集。开销的大头，默认开。</summary>
        public bool DisableLogStackTrace { get; }

        /// <summary>
        /// 编辑器镜像目录（<c>Logs/telemetry/</c>）里保留最近几个会话文件，小于等于 0 表示不清理。
        /// 只在编辑器下有意义：真机不写镜像。
        /// </summary>
        public int EditorMirrorKeepSessions { get; }

        /// <summary>模块过滤条目数，0 表示不过滤。</summary>
        public int ModuleFilterCount => moduleFilter == null ? 0 : moduleFilter.Length;

        /// <summary>
        /// 这个模块放不放行。过滤表为空时全放行；非空时按**点号边界的前缀**匹配，
        /// 所以填 <c>core</c> 能捞到 <c>core.ui</c> 与 <c>core.flow</c>，但捞不到 <c>coreplay</c>。
        /// <para>全程不分配：不拼 <c>filter + "."</c>，改成比长度加比一个字符。</para>
        /// </summary>
        public bool AllowsModule(string module)
        {
            if (moduleFilter == null || moduleFilter.Length == 0)
            {
                return true;
            }

            if (module == null)
            {
                return false;
            }

            for (int i = 0; i < moduleFilter.Length; i++)
            {
                string filter = moduleFilter[i];
                if (string.IsNullOrEmpty(filter))
                {
                    continue;
                }

                if (module.Length < filter.Length)
                {
                    continue;
                }

                if (string.CompareOrdinal(module, 0, filter, 0, filter.Length) != 0)
                {
                    continue;
                }

                if (module.Length == filter.Length || module[filter.Length] == '.')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
