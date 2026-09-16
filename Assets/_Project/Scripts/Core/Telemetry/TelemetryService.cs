// 职责：ITelemetryService 的唯一实现——会话生命周期、序号、级别与模块过滤、每秒限流，最后把行交给 sink。
// 为什么新建：埋点是波 1 新加的框架能力，工程内没有任何可复用或可扩展的服务；
//   格式在 TelemetryFormat、取值在 TelemetryOptions、输出在 ITelemetrySink，这里只剩「策略」。
//
// 关于输出为什么不走 Logging/Log.cs：那个门面会再加一层 [Game] 前缀，行首变成 [Game] [Game][T] ...，
// 契约正则 ^\[Game\]\[T\] 就整份日志都匹配不上了。埋点行的行首属于契约，只能由 TelemetryFormat 说了算。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Logging;
using UnityEngine;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点服务。注册在根作用域里、**紧跟 Platform 之后**，因为它后面的每个服务都要埋点。
    /// <para>
    /// 热路径（Track → 过滤 → 拼行 → sink）只有一次堆分配：把复用的 <see cref="StringBuilder"/>
    /// 取成 string 交给 <c>Debug.Log</c>。属性容器、事件结构、耗时段全是值类型，
    /// 一条埋点等于「一次字符串拼接」——这也是 docs/telemetry.md 第 3 节说的开销量级。
    /// </para>
    /// <para>线程约定：只在主线程调用。序号、限流窗口都是普通字段，没有加锁。</para>
    /// </summary>
    public sealed class TelemetryService : ITelemetryService, IGameService, IDisposable
    {
        /// <summary>限流窗口长度：契约说的是「每秒最多条数」。</summary>
        private const int ThrottleWindowMs = 1000;

        private readonly TelemetryOptions options;
        private readonly ITelemetryClock clock;
        private readonly StringBuilder builder = new StringBuilder(256);

        /// <summary>
        /// 输出终点列表。**同一次格式化的结果按顺序分发给每一个**（见 <see cref="Write"/>），
        /// 绝不为第二个终点再格式化一遍——那样契约就有两份实现，迟早漂移。
        /// <para>不是 readonly：编辑器镜像要等到 <see cref="InitializeAsync"/> 才追加（理由见那里）。</para>
        /// </summary>
        private ITelemetrySink[] sinks;

        /// <summary>模块名 → 门面。一个模块只 new 一次，Scope() 不产生垃圾。</summary>
        private readonly Dictionary<string, ITelemetryScope> scopes =
            new Dictionary<string, ITelemetryScope>(StringComparer.Ordinal);

        private long sessionStartMs;
        private long windowStartMs;
        private int windowCount;
        private int droppedInWindow;
        private int sequence;
        private int errorCount;
        private bool sessionEnded;
        private bool disposed;
        private UnityLogTelemetryBridge bridge;

        /// <param name="sinks">
        /// 输出终点，至少一个。写成 <c>params</c> 是为了 <c>new TelemetryService(options, clock, sink)</c>
        /// 这种单终点写法一个字都不用改；容器那边注册的是 <c>ITelemetrySink[]</c>（见 GameLifetimeScope）。
        /// </param>
        public TelemetryService(TelemetryOptions options, ITelemetryClock clock, params ITelemetrySink[] sinks)
        {
            this.options = options ?? TelemetryOptions.Default;
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));

            if (sinks == null || sinks.Length == 0)
            {
                throw new ArgumentNullException(nameof(sinks));
            }

            for (int i = 0; i < sinks.Length; i++)
            {
                if (sinks[i] == null)
                {
                    throw new ArgumentNullException(nameof(sinks), $"第 {i} 个输出终点是 null。");
                }
            }

            this.sinks = sinks;

            // sid 只要「同一份日志里能把两次运行分开」，8 位十六进制足够；Guid 的哈希比 Random 省一次初始化
            SessionId = Guid.NewGuid().GetHashCode().ToString("x8", CultureInfo.InvariantCulture);
            sessionStartMs = clock.MillisecondsNow;
            windowStartMs = sessionStartMs;
        }

        /// <summary>本次运行的随机 id（8 位十六进制），写在会话头的 <c>sid</c> 里。</summary>
        public string SessionId { get; }

        public bool Enabled => options.Enabled && !disposed;

        public bool SessionStarted { get; private set; }

        public void Track(string module, string evt)
        {
            TelemetryProps props = default;
            Emit(TelemetryLevel.Info, module, evt, in props, null, null);
        }

        public void Track(string module, string evt, (string Key, PropValue Value) p0)
        {
            TelemetryProps props = TelemetryProps.Of(p0);
            Emit(TelemetryLevel.Info, module, evt, in props, null, null);
        }

        public void Track(string module, string evt, (string Key, PropValue Value) p0, (string Key, PropValue Value) p1)
        {
            TelemetryProps props = TelemetryProps.Of(p0, p1);
            Emit(TelemetryLevel.Info, module, evt, in props, null, null);
        }

        public void Track(
            string module,
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2)
        {
            TelemetryProps props = TelemetryProps.Of(p0, p1, p2);
            Emit(TelemetryLevel.Info, module, evt, in props, null, null);
        }

        public void Track(
            string module,
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2,
            (string Key, PropValue Value) p3)
        {
            TelemetryProps props = TelemetryProps.Of(p0, p1, p2, p3);
            Emit(TelemetryLevel.Info, module, evt, in props, null, null);
        }

        public void TrackWarn(string module, string evt, in TelemetryProps props = default)
        {
            Emit(TelemetryLevel.Warn, module, evt, in props, null, null);
        }

        public void TrackLevel(TelemetryLevel level, string module, string evt, in TelemetryProps props = default)
        {
            Emit(level, module, evt, in props, null, null);
        }

        public void TrackError(string module, string evt, Exception error, in TelemetryProps props = default)
        {
            errorCount++;

            // 错误路径允许分配：出错的频率本来就低，而「是什么异常」比省一次拼接重要得多
            string message = error == null
                ? "null"
                : string.Concat(error.GetType().Name, ": ", error.Message);
            Emit(TelemetryLevel.Error, module, evt, in props, message, error == null ? null : error.StackTrace);
        }

        public void TrackError(string module, string evt, string message, in TelemetryProps props = default)
        {
            errorCount++;
            Emit(TelemetryLevel.Error, module, evt, in props, message, null);
        }

        public void TrackError(string module, string evt, string message, string stackTrace)
        {
            errorCount++;
            TelemetryProps props = default;
            Emit(TelemetryLevel.Error, module, evt, in props, message, stackTrace);
        }

        public TelemetrySpan BeginSpan(string module, string name)
        {
            if (!Enabled)
            {
                return default;
            }

            return new TelemetrySpan(this, clock, module, name, clock.MillisecondsNow);
        }

        public ITelemetryScope Scope(string module)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (!scopes.TryGetValue(module, out ITelemetryScope scope))
            {
                scope = new TelemetryScope(this, module);
                scopes.Add(module, scope);
            }

            return scope;
        }

        /// <summary>
        /// 写会话头、接上 Unity 日志桥、按配置关掉 Debug.Log 的堆栈采集。
        /// 全程同步完成（没有 IO，也不等任何东西），返回一个已完成的 UniTask。
        /// </summary>
        public UniTask InitializeAsync(CancellationToken ct)
        {
            if (SessionStarted || disposed || ct.IsCancellationRequested)
            {
                return UniTask.CompletedTask;
            }

            if (options.DisableLogStackTrace)
            {
                // 埋点开销的大头：普通 Debug.Log 不再采集堆栈（Warning / Error 不受影响）。
                // 影响的是全工程的 Debug.Log，所以它是一个配置项而不是写死的行为。
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            }

            // 基线放在这里而不是构造函数：容器构建到真正初始化之间还有别的服务在建，
            // 那段时间不该算进会话时长，否则第一条事件的 t 就不是 0 了。
            sessionStartMs = clock.MillisecondsNow;
            windowStartMs = sessionStartMs;

            // 镜像必须在会话头之前挂上：session_start 是分析脚本切段的锚点，镜像里缺了它就整段都认不出来
            AttachEditorMirror();

            WriteSessionStart();
            SessionStarted = true;
            WriteLogPointer();

            bridge = new UnityLogTelemetryBridge(this);
            bridge.Attach();
            Application.quitting += OnApplicationQuitting;

            return UniTask.CompletedTask;
        }

        /// <summary>作用域销毁时由容器调用：补会话尾，摘掉日志桥与退出回调（注册与反注册成对）。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            // 先写会话尾再置 disposed：编辑器里退出播放模式走的是这条路，
            // Application.quitting 只有真正退出进程时才触发。
            WriteSessionEnd();
            disposed = true;

            Application.quitting -= OnApplicationQuitting;
            if (bridge != null)
            {
                bridge.Dispose();
                bridge = null;
            }

            CloseSinks();
            scopes.Clear();
        }

        /// <summary>
        /// 给实现了 <see cref="IDisposable"/> 的输出终点收尾。必须在 <see cref="WriteSessionEnd"/> 之后调，
        /// 否则会话尾还躺在缓冲里就被关掉了。
        /// <para>
        /// 服务替容器做这件事，是因为**容器对 <c>RegisterInstance</c> 进来的实例不负责销毁**
        /// （见 GameLifetimeScope.RegisterTelemetry 的注释）：带缓冲的终点不关就会丢尾、还占着文件句柄。
        /// 现有的两个实现（Unity 日志、测试用的记录 sink）都不是 IDisposable，语义一个字没变；
        /// 编辑器镜像的 Dispose 是幂等的，被重复关也没有副作用。
        /// </para>
        /// </summary>
        private void CloseSinks()
        {
            for (int i = 0; i < sinks.Length; i++)
            {
                if (sinks[i] is IDisposable closable)
                {
                    closable.Dispose();
                }
            }
        }

        /// <summary>
        /// 编辑器下多挂一个镜像终点，把这次会话的埋点原样再写一份到 <c>Logs/telemetry/&lt;sid&gt;.log</c>。
        /// 理由见 docs/telemetry.md「编辑器镜像」：<c>Editor.log</c> 是本机全局的，不是本工程独占，
        /// 本机同时开着另一个 Unity 时，本工程的埋点几十秒就被挤出尾部。
        /// <para>
        /// 镜像由服务自己建而不是从容器里注入，是因为文件名要用 <see cref="SessionId"/>——
        /// 它在本类的构造函数里生成，组合根拿不到；而 <c>Application.isPlaying</c> 这道闸
        /// 与 <see cref="WriteLogPointer"/> 是同一道，放在一起才不会漏（EditMode 测试也会跑
        /// <see cref="InitializeAsync"/>，不挡住的话每跑一次测试就多一个垃圾文件）。
        /// </para>
        /// <para>销毁由 <see cref="CloseSinks"/> 统一负责：谁建的谁关，flush 也在那一步兜底。</para>
        /// </summary>
        private void AttachEditorMirror()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return;
            }

            EditorMirrorTelemetrySink mirror = new EditorMirrorTelemetrySink(
                EditorMirrorTelemetrySink.DefaultDirectory, SessionId, options.EditorMirrorKeepSessions);

            int last = sinks.Length;
            Array.Resize(ref sinks, last + 1);
            sinks[last] = mirror;
#endif
        }

        /// <summary>
        /// 退出进程时补会话尾，并顺手把带缓冲的终点关掉。
        /// <para>
        /// 关这一步不能省：镜像自己也挂了 <c>Application.quitting</c>，但它是在构造时订阅的，
        /// **比本服务早**，于是它那次 flush 发生在会话尾写进来之前——不在这里再收一次尾，
        /// 退出编辑器那条路的 <c>session_end</c> 就会烂在缓冲里，分析脚本把这段会话判成崩溃。
        /// </para>
        /// </summary>
        private void OnApplicationQuitting()
        {
            WriteSessionEnd();
            CloseSinks();
        }

        private void Emit(TelemetryLevel level, string module, string evt, in TelemetryProps props, string error, string stack)
        {
            if (!TryBeginEvent(level, module, evt, out long timeMs, out int seq, out int frame))
            {
                return;
            }

            TelemetryEvent e = new TelemetryEvent(level, module, evt, timeMs, seq, frame, in props, error, stack, null);
            Write(in e);
        }

        /// <summary>
        /// 过滤 + 限流 + 取号。顺序是有讲究的：先做最便宜的判断（开关、级别），
        /// 最后才动序号——被丢掉的事件**不能占号**，否则日志里出现空洞，分析脚本会误判成「日志被截断」。
        /// </summary>
        private bool TryBeginEvent(TelemetryLevel level, string module, string evt, out long timeMs, out int seq, out int frame)
        {
            timeMs = 0L;
            seq = 0;
            frame = 0;

            if (disposed || !options.Enabled)
            {
                return false;
            }

            if (level < options.MinLevel)
            {
                return false;
            }

            if (module == null || evt == null)
            {
                return false;
            }

            if (!options.AllowsModule(module))
            {
                return false;
            }

            long now = clock.MillisecondsNow;
            RollThrottleWindow(now);
            if (!TryTakeThrottleSlot())
            {
                return false;
            }

            timeMs = now - sessionStartMs;
            seq = sequence++;
            frame = clock.FrameCount;
            return true;
        }

        /// <summary>跨过一秒就换窗口；上一个窗口丢过事件的话，在新窗口补一条 core/throttled。</summary>
        private void RollThrottleWindow(long now)
        {
            if (options.MaxEventsPerSecond <= 0)
            {
                return;
            }

            if (now - windowStartMs < ThrottleWindowMs)
            {
                return;
            }

            windowStartMs = now;
            windowCount = 0;

            if (droppedInWindow <= 0)
            {
                return;
            }

            int dropped = droppedInWindow;
            droppedInWindow = 0;
            WriteThrottled(now, dropped);
        }

        private bool TryTakeThrottleSlot()
        {
            if (options.MaxEventsPerSecond <= 0)
            {
                return true;
            }

            if (windowCount >= options.MaxEventsPerSecond)
            {
                droppedInWindow++;
                return false;
            }

            windowCount++;
            return true;
        }

        /// <summary>
        /// 补报「上一秒丢了多少条」。这条自己不走 <see cref="TryBeginEvent"/>：
        /// 它是在换窗口的过程中写出去的，再走一遍就会又碰一次窗口判定，逻辑绕回自己。
        /// </summary>
        private void WriteThrottled(long now, int dropped)
        {
            if (options.MinLevel > TelemetryLevel.Warn || !options.AllowsModule(TelemetryKeys.Core))
            {
                return;
            }

            windowCount++;
            TelemetryProps props = TelemetryProps.Of((TelemetryKeys.Props.N, dropped));
            TelemetryEvent e = new TelemetryEvent(
                TelemetryLevel.Warn,
                TelemetryKeys.Core,
                TelemetryKeys.CoreEvents.Throttled,
                now - sessionStartMs,
                sequence++,
                clock.FrameCount,
                in props);
            Write(in e);
        }

        /// <summary>
        /// 会话头。属性有 7 个、超过了 <see cref="TelemetryProps.Capacity"/>，
        /// 所以这一条直接把 <c>p</c> 的内容拼成字符串走 <see cref="TelemetryEvent.RawProps"/>——
        /// 它一次会话只写一条，不在热路径上，为它把槽位数从 4 抬到 8 反而让所有事件都变胖。
        /// </summary>
        private void WriteSessionStart()
        {
            if (options.MinLevel > TelemetryLevel.Info || !options.AllowsModule(TelemetryKeys.Core))
            {
                return;
            }

            builder.Clear();
            builder.Append("\"sid\":");
            TelemetryFormat.AppendJsonString(builder, SessionId, false);

            // at 是这段会话的墙钟起点。事件里的 t 只是会话内相对毫秒，跨会话排不了序；
            // 编辑器镜像又是一段会话一个文件，只剩文件 mtime 可用——而 mtime 会被拷贝、同步、备份改掉，
            // 时序判错一次，「成功会话 vs 失败会话」的对照就全错。写进日志里才是硬证据。
            builder.Append(",\"at\":");
            TelemetryFormat.AppendJsonString(
                builder,
                DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                false);

            // prod 是「这份日志是不是本工程写的」的唯一判据：Editor.log 的路径本机全局，
            // 两个 Unity 同开时后启动的会把先前那份挤成 Editor-prev.log，按固定路径去读很可能读到别人的。
            // 分析脚本拿它和本工程的 productName 对一下就能当场报错，而不是拿着别人的日志做分析。
            builder.Append(",\"prod\":");
            TelemetryFormat.AppendJsonString(builder, Application.productName, false);
            builder.Append(",\"ver\":");
            TelemetryFormat.AppendJsonString(builder, Application.version, false);
            builder.Append(",\"plat\":");
            TelemetryFormat.AppendJsonString(builder, Application.platform.ToString(), false);
            builder.Append(",\"unity\":");
            TelemetryFormat.AppendJsonString(builder, Application.unityVersion, false);
            builder.Append(",\"dev\":");
            TelemetryFormat.AppendJsonString(builder, SystemInfo.deviceModel, false);
            builder.Append(",\"scr\":\"");
            builder.Append(Screen.width);
            builder.Append('x');
            builder.Append(Screen.height);
            builder.Append('"');
            builder.Append(",\"mem\":");
            builder.Append(SystemInfo.systemMemorySize);
            string rawProps = builder.ToString();

            windowCount++;
            TelemetryProps empty = default;
            TelemetryEvent e = new TelemetryEvent(
                TelemetryLevel.Info,
                TelemetryKeys.Core,
                TelemetryKeys.CoreEvents.SessionStart,
                0L,
                sequence++,
                clock.FrameCount,
                in empty,
                null,
                null,
                rawProps);
            Write(in e);
        }

        /// <summary>
        /// 写日志指针文件 <c>Logs/telemetry-source.txt</c>，四行：
        /// 日志绝对路径 / productName / sid / 写入时刻（ISO8601）。
        /// <para>
        /// **这不是第二条日志通道**——文件里没有任何埋点数据，只有一句「本工程这次运行的日志写在哪」。
        /// 存在的理由见 docs/telemetry.md「日志到底在哪」：<c>Editor.log</c> 的路径本机全局、不带工程名，
        /// 同时开着两个 Unity 时按固定路径去猜，读到的很可能是另一个工程的日志。
        /// <see cref="Application.consoleLogPath"/> 给的是**当前实例真正在写的**那一份。
        /// </para>
        /// <para>
        /// 只在编辑器下写：真机上工程目录根本不存在，而 Player.log 的路径是确定的，不需要指针。
        /// 写失败（目录只读、路径异常）只记一条 Warn 就算了——它是便利设施，不能因为它让启动挂掉。
        /// </para>
        /// </summary>
        private void WriteLogPointer()
        {
#if UNITY_EDITOR
            // EditMode 测试也会调 InitializeAsync（假 sink，一行都不进 Unity 日志），
            // 让它把指针改成测试进程的日志，/analyze-telemetry 之后就会对着一份没有埋点的文件干瞪眼。
            // 只有真在跑游戏的那次才有资格改这个指针。
            if (!Application.isPlaying)
            {
                return;
            }

            try
            {
                // Application.dataPath 是 <工程根>/Assets，往上一层才是工程根。Logs/ 已经 gitignore。
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string directory = Path.Combine(projectRoot, "Logs");
                Directory.CreateDirectory(directory);

                // 编辑器下第一行指向镜像而不是 Editor.log（docs/telemetry.md 约束 3）：
                // 镜像才是本工程独占、一段会话一个文件、不会被另一个 Unity 挤掉的那一份。
                // 镜像没挂上或已经停写时才退回 consoleLogPath——宁可指一份吵的，也不能指一个不存在的文件。
                string source = ResolveMirrorPath() ?? Application.consoleLogPath;

                string content = string.Concat(
                    source, "\n",
                    Application.productName, "\n",
                    SessionId, "\n",
                    DateTime.Now.ToString("o", CultureInfo.InvariantCulture), "\n");
                File.WriteAllText(Path.Combine(directory, "telemetry-source.txt"), content);
            }
            catch (Exception e)
            {
                Log.Warn($"埋点日志指针文件没写成（Logs/telemetry-source.txt）：{e.Message}。"
                         + "埋点本身不受影响，只是 /analyze-telemetry 得自己指定日志路径。");
            }
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// 找出正在写的镜像文件路径，没有（没挂上、或已经停写）返回 null。
        /// 扫的是 <see cref="sinks"/> 而不是直接记一个字段：测试里镜像是从构造函数注进来的，
        /// 走同一条路才能连「指针指向谁」一起验。
        /// </summary>
        private string ResolveMirrorPath()
        {
            for (int i = 0; i < sinks.Length; i++)
            {
                if (sinks[i] is EditorMirrorTelemetrySink mirror && mirror.Available)
                {
                    return mirror.FilePath;
                }
            }

            return null;
        }
#endif

        /// <summary>会话尾。没有这一条，分析脚本就判定这次运行是崩了或被截断。</summary>
        private void WriteSessionEnd()
        {
            if (sessionEnded || !SessionStarted)
            {
                return;
            }

            sessionEnded = true;
            long now = clock.MillisecondsNow;
            TelemetryProps props = TelemetryProps.Of(
                (TelemetryKeys.Props.Ms, now - sessionStartMs),
                (TelemetryKeys.Props.N, sequence),
                (TelemetryKeys.Props.Errs, errorCount));
            Emit(TelemetryLevel.Info, TelemetryKeys.Core, TelemetryKeys.CoreEvents.SessionEnd, in props, null, null);
        }

        private void Write(in TelemetryEvent e)
        {
            builder.Clear();
            TelemetryFormat.Write(builder, in e);

            // 唯一一次堆分配：Debug.Log 只吃 string，这一步躲不掉。
            // 必须先取成 string 再交给 sink：sink 写出去的一瞬间 Unity 会**同步**回调
            // Application.logMessageReceived，日志桥可能在同一个调用栈上再进来一次并复用这个 builder。
            // 交出去之后这里不再碰 builder，所以嵌套是安全的。
            string line = builder.ToString();

            // 一次格式化，多处分发：每个终点收到的是**同一个字符串实例**，
            // 所以编辑器镜像里的行与 Unity 日志里的行必然逐字节相同（docs/telemetry.md 约束 2）。
            // 这里不为单个终点兜异常：sink 的实现自己负责不往外抛（镜像那份写失败会停写并记一条 Warn），
            // 在热路径上套 try/catch 反而让最常走的那条路变贵。
            for (int i = 0; i < sinks.Length; i++)
            {
                sinks[i].Write(e.Level, line);
            }
        }
    }
}
