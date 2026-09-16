// 职责：回放录制的可调数值资产——总开关、常驻缓冲长度、状态哈希 / 完整快照的采样间隔、
//   快照保留个数、自动保存开关与限流、两个调试热键、重放静音开关。
//   它只负责「这些数字是多少」，环怎么开、什么时候存盘是 ReplayRecorder 的事。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内现成的配置资产里没有一份管得着这些值。TelemetryConfig 管埋点的开关、
//      级别与限流，SimulationConfig 管逻辑步长、追帧上限与种子，UIConfig / AudioConfig 更不沾边；
//      换个参数都变不成「常驻缓冲多少秒」。而按 csharp-code.md「数值配置进 ScriptableObject」，
//      这些数恰恰是要在真机上按表现调的（缓冲开多大吃多少内存、快照多久打一次卡多久）。
//   2. 扩展不行：不能把这几个字段塞进 SimulationConfig。那份资产是**确定性内核**的参数，
//      录制器是内核之外的旁观者：改回放缓冲不该让人以为自己动了逻辑步长，更不该让一份
//      「关掉录制」的配置改动看起来像是在改玩法。两者的生效时机也不同——步长一局定死，
//      录制开关随时可关。
//   3. 快照类型 Options 嵌在本类里，而不是另建 ReplayOptions.cs：它只有 ReplayConfig 一个产地、
//      只有 ReplayRecorder 一个消费者，字段表与上面那串 [SerializeField] 是同一件事的两面，
//      拆成两个文件只会让「加一个可调参数」要改两处（理由同 ReplayFormat.ChunkType 嵌在
//      ReplayFormat 里）。TelemetryOptions 单独成文件是因为它还要被 EditorMirrorTelemetrySink 等
//      多个实现读，这里没有那个情况。

using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Core.Replay
{
    /// <summary>
    /// 回放录制配置。资产建好后拖到 GameLifetimeScope 上（接线是后续任务，本文件不建 .asset）。
    /// <para>
    /// 运行时只读：<see cref="ReplayRecorder"/> 构造时调一次 <see cref="ToOptions"/> 取快照，
    /// 之后再改资产不影响本次运行——缓冲容量是构造时按秒数一次性预分配的，中途改秒数等于要重开一次环。
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "ReplayConfig", menuName = "21Days/Core/Replay Config")]
    public sealed class ReplayConfig : ScriptableObject
    {
        [Header("总开关")]
        [Tooltip("录制什么时候开。Auto = 跟着 Debug.isDebugBuild 走（编辑器与 Development 包开、正式包关），"
                 + "ForceOn / ForceOff 是两个覆盖档：正式包要抓线上偶现就临时设 ForceOn，"
                 + "开发期嫌它吃内存就设 ForceOff。")]
        [SerializeField] private EnableMode enableMode = EnableMode.Auto;

        [Header("常驻缓冲")]
        [Tooltip("常驻录多长一段（秒）。默认 300 = 最近 5 分钟。输入环按「秒数 × tickRate」预分配定长数组，"
                 + "写满回卷；调大直接吃内存（60Hz 下每秒约 36 字节 × 60 = 2KB），别无脑往上堆。")]
        [Range(1f, 1800f)]
        [SerializeField] private float bufferSeconds = 300f;

        [Tooltip("每多少个 tick 记一次状态哈希。60Hz 下 60 = 每秒一次。0 表示不记哈希——"
                 + "那样重放时只能看结果对不对，说不出是从哪个 tick 开始漂的。")]
        [Min(0)]
        [SerializeField] private int stateHashIntervalTicks = 60;

        [Tooltip("每多少个 tick 记一次完整快照。60Hz 下 600 = 每 10 秒一次。0 表示不记快照——"
                 + "那样只能从缓冲最早那一 tick 从头重放，没法跳到中途。")]
        [Min(0)]
        [SerializeField] private int snapshotIntervalTicks = 600;

        [Tooltip("快照环保留最近几个。和输入分开配，是因为两者频率差两个数量级："
                 + "挤在一个环里，要么快照被输入挤掉、要么输入被快照撑爆，没法各自调。"
                 + "0 表示不留快照。")]
        [Min(0)]
        [SerializeField] private int snapshotRingSize = 30;

        [Header("自动保存")]
        [Tooltip("收到 Error / Exception / Assert 时自动把现场存下来。关掉之后只剩热键与代码 API 两条路。")]
        [SerializeField] private bool autoSaveOnError = true;

        [Tooltip("自动保存的限流：两次自动保存至少隔这么多秒。一次崩溃常连报十几条 Error，"
                 + "不限流就会在磁盘上摊开十几份内容几乎一样的回放。0 表示不限流（不建议）。")]
        [Min(0f)]
        [SerializeField] private float autoSaveMinIntervalSeconds = 30f;

        [Header("调试热键")]
        [Tooltip("手动存一份现场的热键。None 表示不绑。录制器在框架层，这个键直接读 Keyboard.current，"
                 + "不走玩法的 Action Map——它是调试键，不该出现在玩家的按键设置里。")]
        [SerializeField] private Key saveHotkey = Key.F9;

        [Tooltip("QA 打点热键（「就是这一帧卡住的」）。None 表示不绑。打点写成独立的 QaMarker chunk，"
                 + "不占每 tick 的字节，也不污染输入流。")]
        [SerializeField] private Key qaMarkerHotkey = Key.F10;

        [Header("重放")]
        [Tooltip("放录像时静音。默认开：重放常要连着放好几遍同一段，音效跟着重复十几次很折磨人。"
                 + "本开关由播放器读取（后续任务），录制器不用它。")]
        [SerializeField] private bool muteDuringPlayback = true;

        /// <summary>录制开关的取值。</summary>
        public enum EnableMode
        {
            /// <summary>跟着 <c>Debug.isDebugBuild</c> 走：编辑器与 Development 包开，正式包关。</summary>
            Auto = 0,

            /// <summary>强制开，正式包也录。</summary>
            ForceOn = 1,

            /// <summary>强制关。</summary>
            ForceOff = 2,
        }

        /// <summary>录制开关档位。真正生效的布尔值看 <see cref="Options.Enabled"/>。</summary>
        public EnableMode Mode => enableMode;

        /// <summary>常驻缓冲长度（秒）。</summary>
        public float BufferSeconds => bufferSeconds;

        /// <summary>状态哈希间隔 tick，0 表示不记。</summary>
        public int StateHashIntervalTicks => stateHashIntervalTicks;

        /// <summary>完整快照间隔 tick，0 表示不记。</summary>
        public int SnapshotIntervalTicks => snapshotIntervalTicks;

        /// <summary>快照环保留个数，0 表示不留。</summary>
        public int SnapshotRingSize => snapshotRingSize;

        /// <summary>报错时是否自动保存。</summary>
        public bool AutoSaveOnError => autoSaveOnError;

        /// <summary>两次自动保存的最小间隔秒数，0 表示不限流。</summary>
        public float AutoSaveMinIntervalSeconds => autoSaveMinIntervalSeconds;

        /// <summary>手动保存热键，<see cref="Key.None"/> 表示不绑。</summary>
        public Key SaveHotkey => saveHotkey;

        /// <summary>QA 打点热键，<see cref="Key.None"/> 表示不绑。</summary>
        public Key QaMarkerHotkey => qaMarkerHotkey;

        /// <summary>放录像时是否静音。由播放器读取，录制器不用。</summary>
        public bool MuteDuringPlayback => muteDuringPlayback;

        /// <summary>
        /// 取一份运行期快照。<see cref="EnableMode.Auto"/> 在这里就地解析成布尔值——
        /// 录制器只认「开还是关」，不该每次判断都再去问一遍 <c>Debug.isDebugBuild</c>
        /// （那会让「同一次运行里前后两次判断不一致」成为可能）。
        /// </summary>
        public Options ToOptions()
        {
            return new Options(
                Resolve(enableMode),
                bufferSeconds,
                stateHashIntervalTicks,
                snapshotIntervalTicks,
                snapshotRingSize,
                autoSaveOnError,
                autoSaveMinIntervalSeconds,
                saveHotkey,
                qaMarkerHotkey,
                muteDuringPlayback);
        }

        /// <summary>
        /// 把开关档位解析成布尔值。<b>用 <c>Debug.isDebugBuild</c> 判断，不写平台宏</b>——
        /// 平台条件编译只许出现在 <c>Core/Platform/</c>（project-root.md 硬约束），
        /// 而且这里要区分的本来就不是平台，是「这是不是一个开发用的包」。
        /// </summary>
        private static bool Resolve(EnableMode mode)
        {
            switch (mode)
            {
                case EnableMode.ForceOn:
                    return true;
                case EnableMode.ForceOff:
                    return false;
                default:
                    return Debug.isDebugBuild;
            }
        }

        /// <summary>
        /// 配置的运行期快照。<see cref="ReplayRecorder"/> 构造时拿一份就再也不碰资产，
        /// 免得运行中有人改了 Inspector，让环的容量和它自称的秒数对不上。
        /// <para>
        /// 做成 <c>readonly struct</c> 而不是 class：它是一份取完就不该再变的值；
        /// 顺带让 EditMode 测试与 <c>execute_code</c> 里能直接 new 一份出来试，
        /// 不必为了调一个参数去建一个 <c>.asset</c>。
        /// </para>
        /// <para>
        /// <b>传参不用 <c>in</c></b>：理由同 <see cref="ReplayHeader"/>——带 <c>in</c> 的方法在
        /// Unity MCP 的 <c>execute_code</c>（C# 6 的 CodeDom 后端）里调不起来，
        /// 而「从编辑器临时敲一段代码验一下录制器」正是这套调试设施的日常用法。
        /// </para>
        /// </summary>
        public readonly struct Options
        {
            /// <summary>按字段顺序构造一份快照。参数含义与 <see cref="ReplayConfig"/> 上同名字段一致。</summary>
            public Options(
                bool enabled,
                float bufferSeconds,
                int stateHashIntervalTicks,
                int snapshotIntervalTicks,
                int snapshotRingSize,
                bool autoSaveOnError,
                float autoSaveMinIntervalSeconds,
                Key saveHotkey,
                Key qaMarkerHotkey,
                bool muteDuringPlayback)
            {
                Enabled = enabled;
                BufferSeconds = bufferSeconds;
                StateHashIntervalTicks = stateHashIntervalTicks;
                SnapshotIntervalTicks = snapshotIntervalTicks;
                SnapshotRingSize = snapshotRingSize;
                AutoSaveOnError = autoSaveOnError;
                AutoSaveMinIntervalSeconds = autoSaveMinIntervalSeconds;
                SaveHotkey = saveHotkey;
                QaMarkerHotkey = qaMarkerHotkey;
                MuteDuringPlayback = muteDuringPlayback;
            }

            /// <summary>录制总开关（<see cref="EnableMode.Auto"/> 已解析成布尔值）。</summary>
            public bool Enabled { get; }

            /// <summary>常驻缓冲长度（秒）。</summary>
            public float BufferSeconds { get; }

            /// <summary>状态哈希间隔 tick，0 表示不记。</summary>
            public int StateHashIntervalTicks { get; }

            /// <summary>完整快照间隔 tick，0 表示不记。</summary>
            public int SnapshotIntervalTicks { get; }

            /// <summary>快照环保留个数，0 表示不留。</summary>
            public int SnapshotRingSize { get; }

            /// <summary>报错时是否自动保存。</summary>
            public bool AutoSaveOnError { get; }

            /// <summary>两次自动保存的最小间隔秒数，0 表示不限流。</summary>
            public float AutoSaveMinIntervalSeconds { get; }

            /// <summary>手动保存热键。</summary>
            public Key SaveHotkey { get; }

            /// <summary>QA 打点热键。</summary>
            public Key QaMarkerHotkey { get; }

            /// <summary>放录像时是否静音。</summary>
            public bool MuteDuringPlayback { get; }
        }
    }
}
